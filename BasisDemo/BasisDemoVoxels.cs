using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedPlayer;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class BasisDemoVoxels : VoxelWorld
{
    public struct SandVoxelData
    {
        public double timeUntilFall;
    }

    private BasisNetworkedPlayer LocalNetworkPlayer;
    [Header("Basis Demo")]
    public List<VoxelType> types = new List<VoxelType>();

    public Transform highlighter;
    public GameObject breakBlockSound;
    public GameObject inventoryPrefab;

    public Light blockLightPrefab;
    public Light sun;

    private Transform[] highlighters;

    public ushort OwnerId = 0;
    public bool IsOwner = false;

    public float interactDistance = 5f;
    public float networkPlayerBlockDist = 1.1f;
    public int renderDistance = 5;
    public LayerMask layers;
    public LayerMask playerLayers;
    public byte placeBlockId = 1;

    public List<string> htmlVoxelColors = new List<string>();

    public Dictionary<Vector3Int, Light> blockLights = new Dictionary<Vector3Int, Light>();

    public bool genOnStart = false;

    private void Awake()
    {
        materials.Clear();
        materials.AddRange(types.Select(x => x.material));
        htmlVoxelColors.Clear();
        htmlVoxelColors.AddRange(types.Select(x => x.htmlColor));
        InitNetworking();
    }

    private void Start()
    {
        inventoryAction.Enable();
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
            _ = GenerateMap(true);
        }
    }

    private void Update()
    {
        UpdateTasks();
        UpdateMapGen();
        UpdateTimeCycle();
        if (IsOwner)
            Tick();
        else
        {
            voxelsToTick.Clear();
            voxelUpdateQueue.Clear();
        }
    }

    private void InitLocalPlayer()
    {
        BasisLocalPlayer.Instance.LocalBoneDriver.OnPostSimulate += OnPostSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged += FindTrackerRoles;
        // BasisDeviceManagement.Instance.AllInputDevices.OnListItemRemoved += ResetIfNeeded; // TODO
        FindTrackerRoles();
        BasisLocalPlayer.OnLocalPlayerCreatedAndReady -= InitLocalPlayer;
    }

    private void OnDestroy()
    {
        DeInitNetworking();
        BasisLocalPlayer.Instance.LocalBoneDriver.OnPostSimulate -= OnPostSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged -= FindTrackerRoles;
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
            _ = UpdateChunks(chunkPos, true, true);
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
                SendVoxel(voxelData.pos, voxel.Id);
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

#region Inputs

    [Header("Inputs")]
    public InputAction inventoryAction;

    private BasisInput centerEye;
    private BasisInput leftHand;
    private BasisInput rightHand;
    private bool lastTriggerLeftMouse = false;
    private bool lastTriggerRightMouse = false;
    private bool lastInventoryButton = false;

    private void FindTrackerRoles()
    {
        centerEye = FindTrackerByRole(BasisBoneTrackedRole.CenterEye);
        leftHand = FindTrackerByRole(BasisBoneTrackedRole.LeftHand);
        rightHand = FindTrackerByRole(BasisBoneTrackedRole.RightHand);
        if (highlighters != null)
        {
            for (int i = 0; i < highlighters.Length; i++)
            {
                if (highlighters[i] != null)
                {
                    Destroy(highlighters[i].gameObject);
                }
            }
        }
        highlighters = new Transform[3];
        highlighters[0] = Instantiate(highlighter, transform);
        highlighters[1] = Instantiate(highlighter, transform);
        highlighters[2] = Instantiate(highlighter, transform);
    }

    private BasisInput FindTrackerByRole(BasisBoneTrackedRole TrackedRole)
    {
        int count = BasisDeviceManagement.Instance.AllInputDevices.Count;
        for (int Index = 0; Index < count; Index++)
        {
            BasisInput Input = BasisDeviceManagement.Instance.AllInputDevices[Index];
            if (Input != null)
            {
                if (Input.TryGetRole(out BasisBoneTrackedRole role))
                {
                    if (role == TrackedRole)
                    {
                        return Input;
                    }
                }
                else
                {
                    Debug.LogError("Missing Role " + role);
                }
            }
            else
            {
                Debug.LogError("There was a missing BasisInput at " + Index);
            }
        }
        return null;
    }

    private void OnPostSimulate()
    {
        if (highlighters == null)
            return;
        if (BasisUIManagement.Instance.basisUIBases.Count != 0)
            return;
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            if (BasisLocalInputActions.Instance != null && centerEye != null)
            {
                Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                PlaceHighlighter(dir, highlighters[0]);
                bool leftMouse = BasisLocalInputActions.Instance.LeftMousePressed.action.ReadValue<float>() >= 0.5f;
                bool rightMouse = BasisLocalInputActions.Instance.RightMousePressed.action.ReadValue<float>() >= 0.5f;
                bool inventoryBtn = inventoryAction.ReadValue<float>() >= 0.5f;
                if (leftMouse && !lastTriggerLeftMouse)
                {
                    TryDestroyBlock(dir);
                }
                if (rightMouse && !lastTriggerRightMouse)
                {
                    TryPlaceBlock(dir, placeBlockId);
                }
                if (inventoryBtn && !lastInventoryButton)
                {
                    ToggleInventoryUI();
                }
                lastTriggerLeftMouse = leftMouse;
                lastTriggerRightMouse = rightMouse;
                lastInventoryButton = inventoryBtn;
            }
        }
        else
        {
            if (centerEye != null)
            {
                Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                highlighters[0].gameObject.SetActive(false);
                if (centerEye.InputState.Trigger >= 0.5f && centerEye.LastState.Trigger < 0.5f)
                {
                    TryDestroyBlock(dir);
                }
            }
            if (leftHand != null)
            {
                Ray dir = new Ray(leftHand.transform.position, leftHand.transform.forward);
                PlaceHighlighter(dir, highlighters[1]);
                if (leftHand.InputState.Trigger >= 0.5f && leftHand.LastState.Trigger < 0.5f)
                {
                    TryDestroyBlock(dir);
                }
            }
            if (rightHand != null)
            {
                Ray dir = new Ray(rightHand.transform.position, rightHand.transform.forward);
                PlaceHighlighter(dir, highlighters[2]);
                if (rightHand.InputState.Trigger >= 0.5f && rightHand.LastState.Trigger < 0.5f)
                {
                    TryPlaceBlock(dir, placeBlockId);
                }
                if (rightHand.InputState.SecondaryButtonGetState && !rightHand.LastState.SecondaryButtonGetState)
                {
                    ToggleInventoryUI();
                }
            }
        }
    }

    #endregion


    public void ToggleInventoryUI()
    {
        if (InventoryUI.Instance != null)
        {
            InventoryUI.Instance.CloseThisMenu();
        }
        else
        {
            GameObject obj = Instantiate(inventoryPrefab);
            if (obj.TryGetComponent(out InventoryUI inv))
            {
                inv.Open();
                inv.FillBlocks(this);
            }
            else
            {
                Destroy(obj);
            }
        }
    }

    public void PlaceHighlighter(Ray ray, Transform highlight)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
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
        foreach (var plr in BasisNetworkManagement.Players)
        {
            if (plr.Value.Player is BasisRemotePlayer remote)
            {
                if (remote.RemoteBoneDriver.FindBone(out BasisBoneControl ctrl, BasisBoneTrackedRole.Hips))
                {
                    Vector3Int playerPos = GetVoxelPosition(ctrl.BoneTransform.position);
                    if ((playerPos - pos).sqrMagnitude <= networkPlayerBlockDist * networkPlayerBlockDist)
                    {
                        return true;
                    }
                }
            }
        }
        return Physics.CheckBox(pos + Vector3.one * 0.5f, Vector3.one * 0.45f, Quaternion.identity, playerLayers);
    }

    public void TryDestroyBlock(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point - hit.normal * 0.5f;
            if ((int)block.y == 0)
                return;
            Vector3Int pos = GetVoxelPosition(block);
            if (TryGetVoxel(pos, out Voxel vox))
            {
                vox.Id = 0;
                SetVoxelWithData(pos, vox);
                QueueTickVoxelArea(pos);
                PlayBlockSoundAt(pos);
                SendVoxel(pos, 0);
            }
            QueueUpdateChunks(FloorPosition(block), true);
        }
    }

    public void TryPlaceBlock(Ray ray, byte id)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point + hit.normal * 0.5f;
            Vector3Int pos = GetVoxelPosition(block);
            if (IsEntityBlocking(pos))
                return;
            if (TryGetVoxel(pos, out Voxel vox))
            {
                vox.Id = id;
                SetVoxelWithData(pos, vox);
                QueueTickVoxelArea(pos);
                PlayBlockSoundAt(pos);
                SendVoxel(pos, id);
            }
            QueueUpdateChunks(FloorPosition(block), true);
        }
    }
}