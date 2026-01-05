using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

public partial class BasisDemoVoxels
{
    [Header("MapGen")]
    public List<VoxelStructureAsset> structureAssets = new List<VoxelStructureAsset>();

    public List<BiomeAsset> biomes = new List<BiomeAsset>();

    public byte floorBlockId = 1;

    public System.Random rng;
    public FastNoiseLite heightNoise;
    public FastNoiseLite heightNoise2;
    public FastNoiseLite biomeNoise;

    public List<VoxelAsset> voxelTypes = new List<VoxelAsset>();
    public int minMaterial = 1;
    public int maxMaterial = 2;
    public int renderDistance = 5;
    public int maxHeightChunks = 3;
    public bool hasMap = false;
    public float maxHeight = 16f;
    public bool genOnStart = false;
    private Vector3 lastPosition;

    private bool mapGenRunning = false;
    private Thread mapGenThread;
    private ConcurrentQueue<Vector3Int> chunksToGen = new ConcurrentQueue<Vector3Int>();
    private ConcurrentQueue<Chunk> chunksToSpawn = new ConcurrentQueue<Chunk>();

    private Vector3Int lastPos;
    public ConcurrentDictionary<ushort, Vector3Int> playerPositions = new ConcurrentDictionary<ushort, Vector3Int>();

    public Action<ushort, Vector3Int> OnSendChunkToPlayer;

    public int GetSurfaceLevel(int x, int z)
    {
        float height = GetHeight(x, z);
        return Mathf.FloorToInt(height) + 1;
    }

    public void PlaceDecorAt(int x, int y, int z, VoxelStructureAsset asset)
    {
        TxtVoxelFile voxelFile = new TxtVoxelFile(htmlVoxelColors.ToArray());
        string selected = asset.contents;
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
                int biome = GetBiome(slicePos.x, slicePos.z);
                for (int i = 0; i < structureAssets.Count; i++)
                {
                    VoxelStructureAsset structure = structureAssets[i];
                    if (structure.biome != null && structure.biome != biomes[biome])
                        continue;
                    int div = structure.density;
                    // if (slicePos.x % div == 0 && slicePos.z % div == 0)
                    {
                        Vector3Int gridPos = slicePos / div;
                        Vector3 jitterOffset = new Vector3(Mathf.Sin(gridPos.x ^ gridPos.z), 0f, Mathf.Cos(gridPos.x * gridPos.z)) * div;
                        int decorX = gridPos.x * div + Mathf.FloorToInt(jitterOffset.x);
                        int decorZ = gridPos.z * div + Mathf.FloorToInt(jitterOffset.z);
                        if (slicePos.x == decorX && slicePos.z == decorZ)
                            PlaceDecorAt(decorX, GetSurfaceLevel(decorX, decorZ), decorZ, structure);
                    }
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
                for (int y = 0; y < Chunk.SIZE; y++)
                {
                    Vector3Int worldPos = chunk.chunkPosition * Chunk.SIZE + new Vector3Int(x, y, z);
                    if (chunk.TryGetVoxel(x, y, z, out Voxel vox))
                    {
                        if (worldPos.y < 3 && worldPos.y < height)
                        {
                            vox.Id = floorBlockId;
                            // SetVoxelWithData(worldPos, vox);
                            chunk.SetVoxel(x, y, z, SetVoxelData(vox, worldPos));
                        }
                        else if (worldPos.y < height)
                        {
                            vox.Id = (byte)types.IndexOf(biomes[biome].surface);
                            // SetVoxelWithData(worldPos, vox);
                            chunk.SetVoxel(x, y, z, SetVoxelData(vox, worldPos));
                        }
                        else
                        {
                            vox.Id = 0;
                            // SetVoxelWithData(worldPos, vox);
                            chunk.SetVoxel(x, y, z, SetVoxelData(vox, worldPos));
                        }
                    }
                }
            }
        }
    }

    public float GetHeight(int x, int z)
    {
        return ((heightNoise.GetNoise(x, z) * 0.5f + 0.5f) * 1f + (heightNoise2.GetNoise(x, z) * 0.5f + 0.5f) * 2f) * maxHeight;
    }

    public int GetBiome(int x, int z)
    {
        float val = biomeNoise.GetNoise(x, z);
        int idx = 0;
        float idxVal = Mathf.Abs(biomes[0].biomePosition - val);
        for (int i = 0; i < biomes.Count; i++)
        {
            float iVal = Mathf.Abs(biomes[i].biomePosition - val);
            if (iVal < idxVal)
            {
                idxVal = iVal;
                idx = i;
            }
        }
        return idx;
        // return biomes.IndexOf(biomes.OrderBy(p => Mathf.Abs(p.biomePosition - val)).First());
    }

    public void QueueGenChunksNear(Vector3Int pos)
    {
        Vector3Int p = new Vector3Int(pos.x, 0, pos.z);
        List<Vector3Int> positions = new List<Vector3Int>();
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int y = 0; y <= maxHeightChunks; y++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    Vector3Int chunkPos = new Vector3Int(pos.x + x, y, pos.z + z);
                    if (chunkPositions.Add(chunkPos))
                    {
                        positions.Add(chunkPos);
                        // positions.Add(new Vector3Int(x, y, z));
                        // new Thread(GenAndSpawnChunkThread).Start(chunkPos);
                        // chunksToGen.Enqueue(chunkPos);
                    }
                }
            }
        }
        foreach (var chunkPos in positions.OrderBy(x => (x - p).sqrMagnitude))
        {
            // chunksToGen.Enqueue(chunkPos);
            new Thread(GenAndSpawnChunkThread).Start(chunkPos);
        }
    }

    private void GenAndSpawnChunkThread(object data)
    {
        Vector3Int chunkPos = (Vector3Int)data;
        // Debug.Log(chunkPos);
        Chunk chunk = new Chunk(chunkPos);
        GenerateVoxels(chunk);
        chunk.QueueUpdateMesh();
        chunksToSpawn.Enqueue(chunk);
    }

    public void GenerateMap(bool actually)
    {
        heightNoise = new FastNoiseLite(seed);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFrequency(0.005f);

        heightNoise2 = new FastNoiseLite(seed);
        heightNoise2.SetFrequency(0.005f);
        heightNoise2.SetNoiseType(FastNoiseLite.NoiseType.Cellular);

        biomeNoise = new FastNoiseLite(seed + 1);
        biomeNoise.SetFrequency(0.002f);
        biomeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        if (actually && !mapGenRunning)
        {
            Debug.Log("Loading from folder...");
            LoadFromFolder(Path.Combine(Application.persistentDataPath, "my_world"));
            Debug.Log("MapGen Thread Starting...");
            mapGenRunning = true;
            mapGenThread = new Thread(MapGenLoop);
            mapGenThread.Start();
            hasMap = true;
        }
    }

    private void MapGenLoop()
    {
        while (mapGenRunning)
        {
            if (chunksToGen.TryDequeue(out Vector3Int chunkPos))
            {
                Chunk chunk = new Chunk(chunkPos);
                GenerateVoxels(chunk);
                chunk.QueueUpdateMesh();
                chunksToSpawn.Enqueue(chunk);
            }
            Thread.Yield();
        }
    }

    public void UpdateMapGen()
    {
        {
            Camera cam = Camera.main;
            Vector3Int pos = RoundPosition(cam.transform.position);
            Vector3Int lastPos = RoundPosition(lastPosition);
            if (pos != lastPos)
            {
                QueueGenChunksNear(pos);
            }
            lastPosition = cam.transform.position;
            while (chunksToSpawn.TryDequeue(out Chunk chunk))
            {
                chunks.Add(chunk.chunkPosition, chunk);
                chunkPositions.Add(chunk.chunkPosition);
                OnSpawnChunk?.Invoke(chunk);
            }
        }
        if (!IsOwner)
            return;
        try
        {
            List<Task> tasks = new List<Task>();
            foreach (var plr in BasisNetworkPlayers.Players)
            {
                if (plr.Value.Player is BasisRemotePlayer remote)
                {
                    {
                        Vector3Int playerChunkPos = FloorPosition(Vector3Int.FloorToInt(remote.PlayerSelf.position));
                        playerChunkPos.y = 0;
                        if (!playerPositions.ContainsKey(plr.Key))
                        {
                            playerPositions.TryAdd(plr.Key, playerChunkPos);
                            QueueGenChunksNear(playerChunkPos);
                        }
                        else if (playerPositions[plr.Key] != playerChunkPos)
                        {
                            playerPositions[plr.Key] = playerChunkPos;
                            QueueGenChunksNear(playerChunkPos);
                        }
                    }
                }
            }
            if (BasisLocalCameraDriver.Instance != null && BasisLocalCameraDriver.Instance.Camera != null)
            {
                Vector3Int pos = FloorPosition(Vector3Int.FloorToInt(BasisLocalCameraDriver.Instance.Camera.transform.position));
                if (lastPos != pos)
                {
                    QueueGenChunksNear(pos);
                    lastPos = pos;
                }
            }
            foreach (var plr in BasisNetworkPlayers.Players)
            {
                if (playerPositions.TryGetValue(plr.Key, out Vector3Int pos))
                    OnSendChunkToPlayer?.Invoke(plr.Key, pos);
            }
        }
        finally
        {
            // mapgenMutex.ReleaseMutex();
        }
    }

    public void OnDestroyMapGen()
    {
        mapGenRunning = false;
        mapGenThread?.Join();
        SaveToFolder(Path.Combine(Application.persistentDataPath, "my_world"));
    }
}