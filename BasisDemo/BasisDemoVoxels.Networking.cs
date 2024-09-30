using System.IO;
using System.IO.Compression;
using System.Linq;
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
    public const string OwnershipID = "VoxelWorld";

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
            foreach (var pos in chunks.Keys)
            {
                SendChunk(pos, targets);
            }
            SendSeed(targets);
        }
    }

    private void OnRemoteLeft(BasisNetworkedPlayer player1, BasisRemotePlayer player2)
    {
        BasisNetworkManagement.RequestCurrentOwnership(OwnershipID);
    }

    private void OnSceneMessage(ushort PlayerID, ushort MessageIndex, byte[] buffer, ushort[] Recipients)
    {
        switch (MessageIndex)
        {
            case SeedMessageId:
            {
                NetSeedMsg msg = SerializationUtility.DeserializeValue<NetSeedMsg>(buffer, DataFormat.Binary);
                seed = msg.seed;
                _ = GenerateMap(false);
                break;
            }
            case VoxelMessageId:
            {
                NetVoxelMsg msg = SerializationUtility.DeserializeValue<NetVoxelMsg>(buffer, DataFormat.Binary);
                Voxel vox = GetVoxel(msg.pos);
                if (vox != null)
                {
                    vox.Id = msg.id;
                    PlayBlockSoundAt(msg.pos);
                    if (types[msg.id].lit)
                        vox.Emit = types[msg.id].litColor;
                    else
                        vox.Emit = new Color32(0, 0, 0, 0);
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

    private void SendSeed(ushort[] targets = null)
    {
        if (BasisNetworkManagement.Instance == null || !BasisNetworkManagement.Instance.HasInitalizedClient)
            return;
        byte[] data = SerializationUtility.SerializeValue(new NetSeedMsg() { seed = seed }, DataFormat.Binary);
        BasisScene.NetworkMessageSend(SeedMessageId, data, DarkRift.DeliveryMethod.ReliableOrdered, targets);
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
}