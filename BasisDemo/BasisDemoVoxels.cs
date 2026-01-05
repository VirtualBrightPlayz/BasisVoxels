using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class BasisDemoVoxels : VoxelWorld
{
    public struct SandVoxelData
    {
        public double timeUntilFall;
    }

    public IGameMode GameMode => GetComponents<IGameMode>().FirstOrDefault(x => x.Enabled);

    [Header("Basis Demo")]
    public List<VoxelType> types = new List<VoxelType>();

    public GameObject breakBlockSound;

    public Light blockLightPrefab;
    public Light sun;

    public BasisNetworkPlayer OwnerId;
    public bool IsOwner = false;

    public float interactDistance = 5f;
    public float networkPlayerBlockDist = 1.1f;
    public LayerMask layers;
    public LayerMask playerLayers;

    public List<string> htmlVoxelColors = new List<string>();

    public Dictionary<Vector3Int, Light> blockLights = new Dictionary<Vector3Int, Light>();

    private void Awake()
    {
        materials.Clear();
        materials.AddRange(types.Select(x => x.material));
        htmlVoxelColors.Clear();
        htmlVoxelColors.AddRange(types.Select(x => x.htmlColor));
    }

    private void Start()
    {
        if (!genOnStart)
            seed = Random.Range(0, int.MaxValue);
        if (BasisLocalPlayer.Instance == null)
        {
            BasisLocalPlayer.OnLocalPlayerCreatedAndReady += InitLocalPlayer;
        }
        else
            InitLocalPlayer();
        if (genOnStart)
        {
            IsOwner = true;
            GenerateMap(true);
        }
    }

    public override void Update()
    {
        UpdateMapGen();
        UpdateTasks();
        UpdateTimeCycle();
        if (IsOwner)
            Tick();
        else
        {
            voxelsToTick.Clear();
            voxelUpdateQueue.Clear();
        }
    }

    public Dictionary<Vector3Int, Chunk> GetChunks()
    {
        return chunks;
    }

    private void InitLocalPlayer()
    {
        IGameMode[] modes = GetComponents<IGameMode>();
        for (int i = 0; i < modes.Length; i++)
        {
            modes[i].Enabled = false;
        }
        modes[0].Enabled = true;
        // BasisDeviceManagement.Instance.AllInputDevices.OnListItemRemoved += ResetIfNeeded; // TODO
        BasisLocalPlayer.OnLocalPlayerCreatedAndReady -= InitLocalPlayer;
    }

    private void OnDestroy()
    {
        OnDestroyMapGen();
    }

    public override void ProcessLight(Vector3Int pos)
    {
        if (TryGetVoxel(pos, out Voxel vox))
        {
            if (blockLights.TryGetValue(pos, out Light light))
            {
                if (vox.Emit.a == 0)
                {
                    blockLights.Remove(pos);
                    Destroy(light.gameObject);
                    return;
                }
                Color color = vox.Emit;
                color.a = 1f;
                light.color = color;
                light.range = vox.Emit.a;
            }
            else if (vox.Emit.a != 0)
            {
                light = Instantiate(blockLightPrefab, pos + Vector3.one * 0.5f, Quaternion.identity, transform);
                blockLights.Add(pos, light);
                Color color = vox.Emit;
                color.a = 1f;
                light.color = color;
                light.range = vox.Emit.a;
            }
        }
    }

    public override void UpdateTasks()
    {
        if (!genRunning && hasMap && chunkUpdateQueue.TryDequeue(out Vector3Int chunkPos))
        {
            UpdateChunks(chunkPos, true, true);
        }
    }

    public override void TickWorld(double delta)
    {
        Vector3Int[] queue = voxelsToTick.ToArray();
        voxelsToTick.Clear();
        for (int i = 0; i < queue.Length; i++)
        {
            Vector3Int voxelPos = queue[i];
            if (TryGetVoxel(voxelPos, out Voxel voxel))
            {
                TickVoxel(delta, voxelPos, voxel);
            }
        }
        while (voxelUpdateQueue.TryDequeue(out (Vector3Int pos, byte id) voxelData))
        {
            if (TryGetVoxel(voxelData.pos, out Voxel voxel))
            {
                voxel.Id = voxelData.id;
                SetVoxelWithData(voxelData.pos, voxel);
                QueueTickVoxelArea(voxelData.pos);
                PlayBlockSoundAt(voxelData.pos);
                QueueUpdateChunks(FloorPosition(voxelData.pos), false);
            }
        }
    }

    public override void TickVoxel(double delta, Vector3Int voxelPos, Voxel voxel)
    {
        base.TickVoxel(delta, voxelPos, voxel);
        if (types[voxel.Id].sand)
        {
            SandVoxelData sand = (SandVoxelData)voxel.UserData;
            if (TryGetVoxel(voxelPos + Vector3Int.down, out Voxel downVox) && !downVox.IsActive)
            {
                sand.timeUntilFall -= delta;
                if (sand.timeUntilFall <= 0)
                {
                    QueueSetVoxel(voxelPos + Vector3Int.down, voxel.Id);
                    QueueSetVoxel(voxelPos, 0);
                }
                else
                {
                    voxel.UserData = sand;
                    SetVoxelRaw(voxelPos, voxel);
                    QueueTickVoxel(voxelPos);
                }
            }
        }
    }

    public override Voxel SetVoxelData(Voxel vox, Vector3Int pos)
    {
        if (types[vox.Id].lit)
            vox.Emit = types[vox.Id].litColor;
        else
            vox.Emit = new Color32(0, 0, 0, vox.Emit.a);
        vox.Layer = types[vox.Id].layer;
        vox.UserData = null;
        if (types[vox.Id].sand)
        {
            vox.UserData = new SandVoxelData()
            {
                timeUntilFall = 0.2d,
            };
        }
        return vox;
    }

    [ContextMenu(nameof(SaveWorld))]
    public void SaveWorld()
    {
        TxtVoxelFile voxelFile = new TxtVoxelFile(htmlVoxelColors.ToArray());
        File.WriteAllText("world.txt", voxelFile.Write(Vector3Int.one * Chunk.SIZE * -renderDistance, Vector3Int.one * Chunk.SIZE * renderDistance, this));
    }

    public void PlaceHighlighter(Ray ray, Transform highlight)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) /*&& hit.transform.TryGetComponent(out VoxelWorldRenderer _)*/)
        {
            Vector3 block = hit.point - hit.normal * 0.5f;
            Vector3Int pos = GetVoxelPosition(block);
            highlight.position = pos + Vector3.one * 0.5f;
            highlight.gameObject.SetActive(true);
            if (highlight.TryGetComponent(out LineRenderer renderer))
            {
                renderer.SetPositions(new Vector3[] { ray.origin, hit.point });
                renderer.enabled = !BasisDeviceManagement.IsUserInDesktop();
            }
        }
        else
        {
            highlight.gameObject.SetActive(false);
        }
    }

    public void PlayBlockSoundAt(Vector3Int pos)
    {
        GameObject go = Instantiate(breakBlockSound, pos + Vector3.one * 0.5f, Quaternion.identity, transform);
        go.SetActive(true);
        if (go.TryGetComponent(out TempAudio audio))
        {
            audio.Play();
        }
        else
        {
            Destroy(go);
        }
    }

    public bool IsEntityBlocking(Vector3Int pos)
    {
        if (BasisNetworkManagement.Instance != null)
        {
            foreach (var plr in BasisNetworkPlayers.Players)
            {
                if (plr.Value.Player is BasisRemotePlayer remote)
                {
                    Vector3Int playerPos = GetVoxelPosition(remote.PlayerSelf.position);
                    if ((playerPos - pos).sqrMagnitude <= networkPlayerBlockDist * networkPlayerBlockDist)
                    {
                        return true;
                    }
                }
            }
        }
        return Physics.CheckBox(pos + Vector3.one * 0.5f, Vector3.one * 0.45f, Quaternion.identity, playerLayers);
    }

    public bool TryDestroyBlock(Ray ray, out Voxel vox, out Vector3Int voxPos)
    {
        vox = default;
        voxPos = default;
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) /*&& hit.transform.TryGetComponent(out VoxelWorldRenderer _)*/)
        {
            Vector3 block = hit.point - hit.normal * 0.5f;
            if ((int)block.y == 0)
                return false;
            Vector3Int pos = GetVoxelPosition(block);
            voxPos = pos;
            bool flag = false;
            if (TryGetVoxel(pos, out vox))
            {
                flag = true;
                Voxel newVox = vox;
                newVox.Id = 0;
                SetVoxelWithData(pos, newVox);
                QueueTickVoxelArea(pos);
                PlayBlockSoundAt(pos);
            }
            QueueUpdateChunks(FloorPosition(Vector3Int.FloorToInt(block)), true);
            return flag;
        }
        return false;
    }

    public bool TryPlaceBlock(Ray ray, byte id)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) /*&& hit.transform.TryGetComponent(out VoxelWorldRenderer _)*/)
        {
            Vector3 block = hit.point + hit.normal * 0.5f;
            Vector3Int pos = GetVoxelPosition(block);
            if (IsEntityBlocking(pos))
                return false;
            bool flag = false;
            if (TryGetVoxel(pos, out Voxel vox))
            {
                flag = true;
                vox.Id = id;
                SetVoxelWithData(pos, vox);
                QueueTickVoxelArea(pos);
                PlayBlockSoundAt(pos);
            }
            QueueUpdateChunks(FloorPosition(Vector3Int.FloorToInt(block)), true);
            return flag;
        }
        return false;
    }
}