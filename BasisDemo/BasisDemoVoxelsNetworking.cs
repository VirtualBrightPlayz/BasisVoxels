using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Basis;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;

public partial class BasisDemoVoxelsNetworking : BasisNetworkBehaviour
{
    public const ushort SeedMessageId = 5134;
    public const ushort VoxelMessageId = 5135;
    public const ushort ChunkMessageId = 5136;
    public const ushort TimeMessageId = 5137;

    public BasisDemoVoxels demo;

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
        using (MemoryStream output = new MemoryStream())
        using (DeflateStream stream = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            stream.Write(data);
            return output.ToArray();
        }
    }

    public static byte[] Decompress(byte[] data)
    {
        using (MemoryStream output = new MemoryStream())
        using (MemoryStream input = new MemoryStream(data))
        using (DeflateStream stream = new DeflateStream(input, CompressionMode.Decompress))
        {
            stream.CopyTo(output);
            return output.ToArray();
        }
    }

    public override void Start()
    {
        base.Start();
        demo.OnSetVoxel += (voxelData, chunk, voxel) => SendVoxel(voxelData, voxel.Id);
        demo.OnSendChunkToPlayer += SendChunks;
    }

    public override void OnOwnershipTransfer(BasisNetworkPlayer NetIdNewOwner)
    {
        bool wasOwner = demo.IsOwner;
        demo.OwnerId = NetIdNewOwner;
        demo.IsOwner = IsLocalOwner();
        if (IsLocalOwner() && !wasOwner)
        {
            if (!demo.hasMap)
            {
                demo.GenerateMap(true);
                foreach (var pos in demo.GetChunks().Keys)
                {
                    SendChunk(pos);
                }
                SendSeed();
            }
        }
    }

    public override void OnNetworkReady()
    {
        TakeOwnership();
    }

    public override void OnPlayerJoined(BasisNetworkPlayer player)
    {
        if (IsLocalOwner())
        {
            ushort[] targets = new ushort[] { player.playerId };
            SendChunks(player.playerId, Vector3Int.zero);
            SendSeed(targets);
            SendTime(demo.timeRotation);
        }
    }

    public override void OnPlayerLeft(BasisNetworkPlayer player)
    {
        TakeOwnership();
        sentChunks.Remove(player.playerId);
        demo.playerPositions.TryRemove(player.playerId, out _);
    }

    public override void OnNetworkMessage(ushort PlayerID, byte[] buffer, DeliveryMethod DeliveryMethod)
    {
        using (MemoryStream stream = new MemoryStream(buffer))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            ushort messageIndex = reader.ReadUInt16();
            switch (messageIndex)
            {
                case SeedMessageId:
                    demo.seed = reader.ReadInt32();
                    demo.GenerateMap(false);
                    break;
                case VoxelMessageId:
                    int x = reader.ReadInt32();
                    int y = reader.ReadInt32();
                    int z = reader.ReadInt32();
                    byte id = reader.ReadByte();
                    Vector3Int pos = new Vector3Int(x, y, z);
                    if (demo.TryGetVoxel(pos, out Voxel vox))
                    {
                        vox.Id = id;
                        demo.SetVoxelWithData(pos, vox);
                        demo.QueueTickVoxelArea(pos);
                        demo.PlayBlockSoundAt(pos);
                        demo.QueueUpdateChunks(VoxelWorld.FloorPosition(pos), true);
                    }
                    break;
                case ChunkMessageId:
                    if (PlayerID != CurrentOwnerId)
                        break;
                    NetChunkMsg msg = new NetChunkMsg();
                    msg.pos.x = reader.ReadInt32();
                    msg.pos.y = reader.ReadInt32();
                    msg.pos.z = reader.ReadInt32();
                    msg.voxels = Decompress(reader.ReadBytes(reader.ReadInt32()));
                    OnNetChunk(msg);
                    break;
                case TimeMessageId:
                    if (PlayerID != CurrentOwnerId)
                        break;
                    demo.timeRotation = reader.ReadSingle();
                    break;
            }
        }
    }

    private void OnNetChunk(NetChunkMsg msg)
    {
        Chunk mesh = demo.SpawnOrGetChunk(msg.pos);
        int count = Mathf.Min(mesh.voxels.Length, msg.voxels.Length);
        for (int i = 0; i < count; i++)
        {
            mesh.voxels[i].Id = msg.voxels[i];
            if (demo.types[msg.voxels[i]].lit)
            {
                mesh.voxels[i].Emit = demo.types[msg.voxels[i]].litColor;
            }
            else
                mesh.voxels[i].Emit = new Color32(0, 0, 0, 0);
            mesh.voxels[i].Layer = demo.types[msg.voxels[i]].layer;
        }
        demo.QueueUpdateChunks(msg.pos, false);
    }

    private void SendSeed(ushort[] targets = null)
    {
        if (BasisNetworkConnection.LocalPlayerPeer == null)
            return;
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(SeedMessageId);
            writer.Write(demo.seed);
            SendCustomNetworkEvent(stream.ToArray(), DeliveryMethod.ReliableOrdered, targets);
        }
    }

    private void SendTime(float time)
    {
        if (BasisNetworkConnection.LocalPlayerPeer == null)
            return;
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(TimeMessageId);
            writer.Write(time);
            SendCustomNetworkEvent(stream.ToArray(), DeliveryMethod.ReliableOrdered);
        }
    }

    private void SendVoxel(Vector3Int pos, byte id)
    {
        if (BasisNetworkConnection.LocalPlayerPeer == null)
            return;
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(VoxelMessageId);
            writer.Write(pos.x);
            writer.Write(pos.y);
            writer.Write(pos.z);
            writer.Write(id);
            SendCustomNetworkEvent(stream.ToArray(), DeliveryMethod.ReliableOrdered);
        }
    }

    private void SendChunks(ushort target, Vector3Int chunkPos)
    {
        if (!sentChunks.ContainsKey(target))
            sentChunks.Add(target, new List<Vector3Int>());
        Vector3Int chunkPosNoY = chunkPos;
        chunkPosNoY.y = 0;
        ushort[] targets = new ushort[] { target };
        foreach (var pos in demo.GetChunks().Keys)
        {
            Vector3Int chunk = pos;
            chunk.y = 0;
            if ((chunk - chunkPosNoY).sqrMagnitude <= demo.renderDistance * demo.renderDistance)
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
        if (BasisNetworkConnection.LocalPlayerPeer == null)
            return;
        if (demo.TryGetChunk(pos, out Chunk chunk))
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                byte[] voxels = chunk.voxels.Select(x => x.Id).ToArray();
                voxels = Compress(voxels);
                writer.Write(ChunkMessageId);
                writer.Write(pos.x);
                writer.Write(pos.y);
                writer.Write(pos.z);
                writer.Write(voxels.Length);
                writer.Write(voxels);
                SendCustomNetworkEvent(stream.ToArray(), DeliveryMethod.ReliableOrdered, targets);
            }
        }
    }
}