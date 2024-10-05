using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedPlayer;
using BasisSerializer.OdinSerializer;
using UnityEngine;

public partial class BasisDemoVoxels
{
    public const ushort SeedMessageId = 5134;
    public const ushort VoxelMessageId = 5135;
    public const ushort ChunkMessageId = 5136;
    public const ushort TimeMessageId = 5137;
    public const string OwnershipID = "VoxelWorld";

    private Dictionary<ushort, List<Vector3Int>> sentChunks = new Dictionary<ushort, List<Vector3Int>>();

    public struct NetSeedMsg
    {
        public int seed;
    }

    public struct NetTimeMsg
    {
        public float time;
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
        BasisNetworkManagement.OnOwnershipTransfer += OnOwnerTransfer;
    }

    private void DeInitNetworking()
    {
        BasisScene.OnNetworkMessageReceived -= OnSceneMessage;
        BasisNetworkManagement.OnLocalPlayerJoined -= OnLocalJoin;
        BasisNetworkManagement.OnRemotePlayerJoined -= OnRemoteJoin;
        BasisNetworkManagement.OnRemotePlayerLeft -= OnRemoteLeft;
        BasisNetworkManagement.OnOwnershipTransfer -= OnOwnerTransfer;
    }

    private async void OnOwnerTransfer(string UniqueEntityID, ushort NetIdNewOwner, bool IsOwner)
    {
        if (UniqueEntityID == OwnershipID)
        {
            bool wasOwner = this.IsOwner;
            OwnerId = NetIdNewOwner;
            this.IsOwner = IsOwner;
            if (IsOwner && !wasOwner)
            {
                if (!hasMap)
                {
                    await GenerateMap(true);
                    foreach (var pos in chunks.Keys)
                    {
                        SendChunk(pos);
                    }
                    SendSeed();
                }
            }
        }
    }

    private void OnLocalJoin(BasisNetworkedPlayer player1, BasisLocalPlayer player2)
    {
        LocalNetworkPlayer = player1;
        BasisNetworkManagement.RequestCurrentOwnership(OwnershipID);
    }

    private void OnRemoteJoin(BasisNetworkedPlayer player1, BasisRemotePlayer player2)
    {
        if (IsOwner)
        {
            ushort[] targets = new ushort[] { player1.NetId };
            SendChunks(player1.NetId, Vector3Int.zero);
            SendSeed(targets);
            SendTime(timeRotation);
        }
    }

    private void OnRemoteLeft(BasisNetworkedPlayer player1, BasisRemotePlayer player2)
    {
        BasisNetworkManagement.RequestCurrentOwnership(OwnershipID);
        sentChunks.Remove(player1.NetId);
        playerPositions.Remove(player1.NetId);
    }

    private void OnSceneMessage(ushort PlayerID, ushort MessageIndex, byte[] buffer, ushort[] Recipients)
    {
        switch (MessageIndex)
        {
            case SeedMessageId:
            {
                NetSeedMsg msg = SerializationUtility.DeserializeValue<NetSeedMsg>(buffer, DataFormat.Binary);
                seed = msg.seed;
                OnNetSeed();
                break;
            }
            case VoxelMessageId:
            {
                NetVoxelMsg msg = SerializationUtility.DeserializeValue<NetVoxelMsg>(buffer, DataFormat.Binary);
                if (TryGetVoxel(msg.pos, out Voxel vox))
                {
                    vox.Id = msg.id;
                    SetVoxelWithData(msg.pos, vox);
                    QueueTickVoxelArea(msg.pos);
                    PlayBlockSoundAt(msg.pos);
                    QueueUpdateChunks(FloorPosition(msg.pos), true);
                }
                break;
            }
            case ChunkMessageId:
            {
                if (PlayerID != OwnerId)
                    break;
                NetChunkMsg msg = SerializationUtility.DeserializeValue<NetChunkMsg>(Decompress(buffer), DataFormat.Binary);
                OnNetChunk(msg);
                break;
            }
            case TimeMessageId:
            {
                if (PlayerID != OwnerId)
                    break;
                NetTimeMsg msg = SerializationUtility.DeserializeValue<NetTimeMsg>(buffer, DataFormat.Binary);
                timeRotation = msg.time;
                break;
            }
        }
    }

    private async void OnNetSeed()
    {
        await GenerateMap(false);
    }

    private void OnNetChunk(NetChunkMsg msg)
    {
        VoxelMesh mesh = SpawnOrGetChunk(msg.pos);
        int count = Mathf.Min(mesh.chunk.voxels.Length, msg.voxels.Length);
        for (int i = 0; i < count; i++)
        {
            mesh.chunk.voxels[i].Id = msg.voxels[i];
            if (types[msg.voxels[i]].lit)
            {
                mesh.chunk.voxels[i].Emit = types[msg.voxels[i]].litColor;
            }
            else
                mesh.chunk.voxels[i].Emit = new Color32(0, 0, 0, 0);
        }
        QueueUpdateChunks(msg.pos, false);
    }

    private void SendSeed(ushort[] targets = null)
    {
        if (BasisNetworkManagement.Instance == null || !BasisNetworkManagement.Instance.HasInitalizedClient)
            return;
        byte[] data = SerializationUtility.SerializeValue(new NetSeedMsg() { seed = seed }, DataFormat.Binary);
        BasisScene.NetworkMessageSend(SeedMessageId, data, DarkRift.DeliveryMethod.ReliableOrdered, targets);
    }

    private void SendVoxel(Vector3Int pos)
    {
        if (TryGetVoxel(pos, out Voxel vox))
        {
            SendVoxel(pos, vox.Id);
        }
    }

    private void SendTime(float time)
    {
        if (BasisNetworkManagement.Instance == null || !BasisNetworkManagement.Instance.HasInitalizedClient)
            return;
        byte[] data = SerializationUtility.SerializeValue(new NetTimeMsg()
        {
            time = time,
        }, DataFormat.Binary);
        BasisScene.NetworkMessageSend(TimeMessageId, data, DarkRift.DeliveryMethod.ReliableOrdered);
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

    private void SendChunks(ushort target, Vector3Int chunkPos)
    {
        if (!sentChunks.ContainsKey(target))
            sentChunks.Add(target, new List<Vector3Int>());
        Vector3Int chunkPosNoY = chunkPos;
        chunkPosNoY.y = 0;
        ushort[] targets = new ushort[] { target };
        foreach (var pos in chunks.Keys)
        {
            Vector3Int chunk = pos;
            chunk.y = 0;
            if ((chunk - chunkPosNoY).sqrMagnitude <= renderDistance * renderDistance)
            {
                if (!sentChunks[target].Contains(pos))
                {
                    SendChunk(pos, targets);
                    sentChunks[target].Add(pos);
                }
            }
            else
            {
                sentChunks[target].Remove(pos);
            }
        }
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
}