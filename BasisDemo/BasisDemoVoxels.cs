using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedPlayer;
using Basis.Scripts.TransformBinders.BoneControl;
using BasisSerializer.OdinSerializer;
using UnityEngine;

public class BasisDemoVoxels : VoxelWorld
{
    public const ushort SeedMessageId = 5134;
    public const ushort VoxelMessageId = 5135;
    public const ushort ChunkMessageId = 5136;

    public struct NetSeedMsg
    {
        public int seed;
    }

    public struct NetVoxelMsg
    {
        public Vector3Int pos;
        public byte id;
    }

    public struct NetChunkMsg
    {
        public Vector3Int pos;
        public byte[] voxels;
    }

    private BasisNetworkedPlayer LocalNetworkPlayer;
    public bool IsOwner = false;

    public float interactDistance = 5f;
    public int minMaterial = 1;
    public int maxMaterial = 2;
    public int renderDistance = 5;
    public LayerMask layers;
    public byte placeBlockId = 1;
    public byte floorBlockId = 1;

    public List<string> htmlVoxelColors = new List<string>();
    private List<string> decorContents = new List<string>();
    public List<TextAsset> decor = new List<TextAsset>();

    public System.Random rng;
    public FastNoiseLite heightNoise;
    public FastNoiseLite heightNoise2;
    public FastNoiseLite biomeNoise;

    private BasisInput centerEye;
    private BasisInput leftHand;
    private BasisInput rightHand;
    private bool lastTriggerLeftMouse = false;
    private bool lastTriggerRightMouse = false;

    private async Task TryGenChunk(Vector3Int pos)
    {
        if (genRunning)
            return;
        genRunning = true;
        List<VoxelMesh> meshes = new List<VoxelMesh>();
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int y = 0; y <= renderDistance; y++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    VoxelMesh mesh = await SpawnChunk(new Vector3Int(pos.x + x, pos.y + y, pos.z + z));
                    if (mesh != null)
                        meshes.Add(mesh);
                }
            }
        }
        for (int i = 0; i < meshes.Count; i++)
        {
            await Task.Run(() =>
            {
                GenerateVoxels(meshes[i].chunk);
            });
        }
        for (int i = 0; i < meshes.Count; i++)
        {
            await Task.Run(() =>
            {
                GenerateDecor(meshes[i].chunk);
            });
        }
        foreach (var chunk in chunks.Values)
        {
            chunk.UpdateMesh();
        }
        genRunning = false;
    }

    public int GetSurfaceLevel(int x, int z)
    {
        for (int i = Chunk.SIZE * renderDistance; i >= 0; i--)
        {
            Voxel vox = GetVoxel(x, i, z);
            if (vox != null)
            {
                if (vox.IsActive)
                    return i;
            }
        }
        return 0;
    }

    public void PlaceDecorAt(int x, int y, int z)
    {
        TxtVoxelFile voxelFile = new TxtVoxelFile(htmlVoxelColors.ToArray());
        string selected = decorContents[rng.Next(decorContents.Count)];
        voxelFile.Read(selected, new Vector3Int(x, y, z), this);
    }

    public void GenerateDecor(Chunk chunk)
    {
        if (chunk.chunkPosition.y != 0)
            return;
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int z = 0; z < Chunk.SIZE; z++)
            {
                Vector3Int slicePos = chunk.chunkPosition * Chunk.SIZE + new Vector3Int(x, 0, z);
                int div = 8;
                if (slicePos.x % div == 0 && slicePos.z % div == 0)
                {
                    Vector3Int gridPos = slicePos / div;
                    Vector3 jitterOffset = new Vector3(Mathf.Sin(gridPos.x ^ gridPos.z), 0f, Mathf.Cos(gridPos.x * gridPos.z)) * div;
                    int decorX = slicePos.x + Mathf.RoundToInt(jitterOffset.x);
                    int decorZ = slicePos.z + Mathf.RoundToInt(jitterOffset.z);
                    PlaceDecorAt(decorX, GetSurfaceLevel(decorX, decorZ) + 1, decorZ);
                }
            }
        }
    }

    public override void GenerateVoxels(Chunk chunk)
    {
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int z = 0; z < Chunk.SIZE; z++)
            {
                Vector3Int slicePos = chunk.chunkPosition * Chunk.SIZE + new Vector3Int(x, 0, z);
                float height = GetHeight(slicePos.x, slicePos.z);
                int biome = GetBiome(slicePos.x, slicePos.z);
                int surfaceY = -1;
                for (int y = 0; y < Chunk.SIZE; y++)
                {
                    Vector3Int worldPos = chunk.chunkPosition * Chunk.SIZE + new Vector3Int(x, y, z);
                    Voxel vox = chunk.GetVoxel(x, y, z);
                    if (vox != null)
                    {
                        if (worldPos.y < 3)
                        {
                            vox.Id = floorBlockId;
                        }
                        else if (worldPos.y < height)
                        {
                            vox.Id = (byte)biome;
                            surfaceY = y;
                        }
                        else
                        {
                            vox.Id = 0;
                        }
                    }
                }

                int div = 4;
                if (slicePos.x % div == 0 && slicePos.z % div == 0 && surfaceY != -1 && surfaceY < Chunk.SIZE - 1)
                {
                    Vector3Int gridPos = slicePos / div;
                    Vector3 jitterOffset = new Vector3(Mathf.Sin(gridPos.x ^ gridPos.z), 0f, Mathf.Cos(gridPos.x * gridPos.z)) * div;
                    int decorX = slicePos.x + Mathf.RoundToInt(jitterOffset.x);
                    int decorZ = slicePos.z + Mathf.RoundToInt(jitterOffset.z);
                    // PlaceDecorAt(decorX, GetSurfaceLevel(decorX, decorZ) + 1, decorZ);
                }
            }
        }
    }

    public override float GetHeight(int x, int z)
    {
        return ((heightNoise.GetNoise(x, z) * 0.5f + 0.5f) + (heightNoise2.GetNoise(x, z) * 0.5f + 0.5f)) * Chunk.SIZE;
    }

    public override int GetBiome(int x, int z)
    {
        return Mathf.FloorToInt((biomeNoise.GetNoise(x, z) * 0.5f + 0.5f) * (maxMaterial - minMaterial + 1) + minMaterial);
    }

    private void Start()
    {
        seed = UnityEngine.Random.Range(0, int.MaxValue);
        decorContents.Clear();
        decorContents.AddRange(decor.Select(x => x.text));
        InitNetworking();
        if (BasisLocalPlayer.Instance == null)
        {
            BasisLocalPlayer.OnLocalPlayerCreatedAndReady += InitLocalPlayer;
        }
        else
            InitLocalPlayer();
    }

    private void InitLocalPlayer()
    {
        BasisLocalPlayer.Instance.Teleport(Vector3.up * Chunk.SIZE, Quaternion.identity);
        BasisLocalPlayer.Instance.LocalBoneDriver.OnPostSimulate += OnPostSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged += FindTrackerRoles;
        // BasisDeviceManagement.Instance.AllInputDevices.OnListItemRemoved += ResetIfNeeded; // TODO
        FindTrackerRoles();
        BasisLocalPlayer.OnLocalPlayerCreatedAndReady -= InitLocalPlayer;
    }

    private async void GenerateMap(bool actually)
    {
        heightNoise = new FastNoiseLite(seed);
        heightNoise.SetFrequency(0.01f);
        heightNoise2 = new FastNoiseLite(seed);
        heightNoise2.SetFrequency(0.01f);
        heightNoise2.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        biomeNoise = new FastNoiseLite(seed);
        biomeNoise.SetFrequency(0.005f);
        biomeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        rng = new System.Random(seed);
        if (actually)
            await TryGenChunk(Vector3Int.zero);
        BasisLocalPlayer.Instance.Teleport(Vector3.up * Chunk.SIZE, Quaternion.identity);
    }

    private void OnDestroy()
    {
        DeInitNetworking();
        BasisLocalPlayer.Instance.LocalBoneDriver.OnPostSimulate -= OnPostSimulate;
        BasisDeviceManagement.Instance.AllInputDevices.OnListChanged -= FindTrackerRoles;
    }

    [ContextMenu(nameof(SaveWorld))]
    public void SaveWorld()
    {
        TxtVoxelFile voxelFile = new TxtVoxelFile(htmlVoxelColors.ToArray());
        File.WriteAllText("world.txt", voxelFile.Write(Vector3Int.one * Chunk.SIZE * -renderDistance, Vector3Int.one * Chunk.SIZE * renderDistance, this));
    }

#region Networking

    public static byte[] Compress(byte[] data)
    {
        MemoryStream output = new MemoryStream();
        using (DeflateStream stream = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            stream.Write(data);
        }
        return output.ToArray();
    }

    public static byte[] Decompress(byte[] data)
    {
        MemoryStream input = new MemoryStream(data);
        MemoryStream output = new MemoryStream();
        using (DeflateStream stream = new DeflateStream(input, CompressionMode.Decompress))
        {
            stream.CopyTo(output);
        }
        return output.ToArray();
    }

    private void InitNetworking()
    {
        BasisScene.OnNetworkMessageReceived += OnSceneMessage;
        BasisNetworkManagement.OnLocalPlayerJoined += OnLocalJoin;
        BasisNetworkManagement.OnRemotePlayerJoined += OnRemoteJoin;
        BasisNetworkManagement.OnRemotePlayerLeft += OnRemoteLeft;
        if (BasisNetworkManagement.Instance == null || !BasisNetworkManagement.Instance.HasInitalizedClient)
        {
            // GenerateMap(true);
        }
    }

    private void DeInitNetworking()
    {
        BasisScene.OnNetworkMessageReceived -= OnSceneMessage;
        BasisNetworkManagement.OnLocalPlayerJoined -= OnLocalJoin;
        BasisNetworkManagement.OnRemotePlayerJoined -= OnRemoteJoin;
        BasisNetworkManagement.OnRemotePlayerLeft -= OnRemoteLeft;
    }

    public void ComputeCurrentOwner()
    {
        ushort OldestPlayerInInstance = BasisNetworkManagement.Instance.GetOldestAvailablePlayerUshort();
        IsOwner = OldestPlayerInInstance == LocalNetworkPlayer.NetId;
    }

    private async void OnLocalJoin(BasisNetworkedPlayer player1, BasisLocalPlayer player2)
    {
        LocalNetworkPlayer = player1;
        await Task.Delay(1000);
        ComputeCurrentOwner();
        if (IsOwner)
        {
            GenerateMap(true);
            SendSeed();
        }
    }

    private void OnRemoteJoin(BasisNetworkedPlayer player1, BasisRemotePlayer player2)
    {
        ComputeCurrentOwner();
        if (IsOwner)
        {
            SendSeed();
            foreach (var pos in chunks.Keys)
            {
                SendChunk(pos, new ushort[] { player1.NetId });
            }
        }
    }

    private void OnRemoteLeft(BasisNetworkedPlayer player1, BasisRemotePlayer player2)
    {
        ComputeCurrentOwner();
    }

    private void OnSceneMessage(ushort PlayerID, ushort MessageIndex, byte[] buffer, ushort[] Recipients)
    {
        switch (MessageIndex)
        {
            case SeedMessageId:
            {
                NetSeedMsg msg = SerializationUtility.DeserializeValue<NetSeedMsg>(buffer, DataFormat.Binary);
                seed = msg.seed;
                GenerateMap(false);
                break;
            }
            case VoxelMessageId:
            {
                NetVoxelMsg msg = SerializationUtility.DeserializeValue<NetVoxelMsg>(buffer, DataFormat.Binary);
                Voxel vox = GetVoxel(msg.pos);
                if (vox != null)
                {
                    vox.Id = msg.id;
                    UpdateChunks(msg.pos);
                }
                break;
            }
            case ChunkMessageId:
            {
                NetChunkMsg msg = SerializationUtility.DeserializeValue<NetChunkMsg>(Decompress(buffer), DataFormat.Binary);
                OnNetChunk(msg);
                break;
            }
        }
    }

    private async void OnNetChunk(NetChunkMsg msg)
    {
        VoxelMesh mesh = await SpawnOrGetChunk(msg.pos);
        int count = Mathf.Min(mesh.chunk.voxels.Length, msg.voxels.Length);
        for (int i = 0; i < count; i++)
        {
            mesh.chunk.voxels[i].Id = msg.voxels[i];
        }
        await mesh.UpdateMeshAsync();
    }

    private void SendSeed()
    {
        if (BasisNetworkManagement.Instance == null || !BasisNetworkManagement.Instance.HasInitalizedClient)
            return;
        byte[] data = SerializationUtility.SerializeValue(new NetSeedMsg() { seed = seed }, DataFormat.Binary);
        BasisScene.NetworkMessageSend(SeedMessageId, data, DarkRift.DeliveryMethod.ReliableOrdered);
    }

    private void SendVoxel(Vector3Int pos)
    {
        Voxel vox = GetVoxel(pos.x, pos.y, pos.z);
        if (vox != null)
        {
            SendVoxel(pos, vox.Id);
        }
    }

    private void SendVoxel(Vector3Int pos, byte id)
    {
        if (BasisNetworkManagement.Instance == null || !BasisNetworkManagement.Instance.HasInitalizedClient)
            return;
        byte[] data = SerializationUtility.SerializeValue(new NetVoxelMsg()
        {
            pos = pos,
            id = id,
        }, DataFormat.Binary);
        BasisScene.NetworkMessageSend(VoxelMessageId, data, DarkRift.DeliveryMethod.ReliableOrdered);
    }

    private void SendChunk(Vector3Int pos, ushort[] targets = null)
    {
        if (BasisNetworkManagement.Instance == null || !BasisNetworkManagement.Instance.HasInitalizedClient)
            return;
        if (chunks.TryGetValue(pos, out VoxelMesh chunk))
        {
            byte[] data = SerializationUtility.SerializeValue(new NetChunkMsg()
            {
                pos = pos,
                voxels = chunk.chunk.voxels.Select(x => x.Id).ToArray(), // TODO: improve this?
            }, DataFormat.Binary);
            BasisScene.NetworkMessageSend(ChunkMessageId, Compress(data), DarkRift.DeliveryMethod.ReliableOrdered, targets);
        }
    }

#endregion

#region Inputs

    private void FindTrackerRoles()
    {
        centerEye = FindTrackerByRole(BasisBoneTrackedRole.CenterEye);
        leftHand = FindTrackerByRole(BasisBoneTrackedRole.LeftHand);
        rightHand = FindTrackerByRole(BasisBoneTrackedRole.RightHand);
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
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            if (BasisLocalInputActions.Instance != null && centerEye != null)
            {
                bool leftMouse = BasisLocalInputActions.Instance.LeftMousePressed.action.ReadValue<float>() >= 0.5f;
                bool rightMouse = BasisLocalInputActions.Instance.RightMousePressed.action.ReadValue<float>() >= 0.5f;
                if (leftMouse && !lastTriggerLeftMouse)
                {
                    Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                    TryDestroyBlock(dir);
                }
                if (rightMouse && !lastTriggerRightMouse)
                {
                    Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                    TryPlaceBlock(dir, placeBlockId);
                }
                lastTriggerLeftMouse = leftMouse;
                lastTriggerRightMouse = rightMouse;
            }
        }
        else
        {
            if (centerEye != null)
            {
                if (centerEye.InputState.Trigger >= 0.5f && centerEye.LastState.Trigger < 0.5f)
                {
                    Ray dir = new Ray(centerEye.transform.position, centerEye.transform.forward);
                    TryDestroyBlock(dir);
                }
            }
            if (leftHand != null)
            {
                if (leftHand.InputState.Trigger >= 0.5f && leftHand.LastState.Trigger < 0.5f)
                {
                    Ray dir = new Ray(leftHand.transform.position, leftHand.transform.forward);
                    TryDestroyBlock(dir);
                }
            }
            if (rightHand != null)
            {
                if (rightHand.InputState.Trigger >= 0.5f && rightHand.LastState.Trigger < 0.5f)
                {
                    Ray dir = new Ray(rightHand.transform.position, rightHand.transform.forward);
                    TryPlaceBlock(dir, placeBlockId);
                }
            }
        }
    }

#endregion

    public void TryDestroyBlock(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point - hit.normal * 0.5f;
            if ((int)block.y == 0)
                return;
            Voxel vox = GetVoxel(block);
            if (vox != null)
            {
                vox.Id = 0;
                SendVoxel(GetVoxelPosition(block), 0);
            }
            UpdateChunks(block);
        }
    }

    public void TryPlaceBlock(Ray ray, byte id)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, layers) && hit.transform.TryGetComponent(out VoxelMesh _))
        {
            Vector3 block = hit.point + hit.normal * 0.5f;
            Voxel vox = GetVoxel(block);
            if (vox != null)
            {
                vox.Id = id;
                SendVoxel(GetVoxelPosition(block), id);
            }
            UpdateChunks(block);
        }
    }
}