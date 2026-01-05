using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public partial class DemoVoxelWorld : VoxelWorld
{
    public List<VoxelAsset> voxelTypes = new List<VoxelAsset>();
    public int minMaterial = 1;
    public int maxMaterial = 2;
    public int renderDistance = 5;
    public int maxHeightChunks = 3;
    public bool genOnStart = false;
    private Vector3 lastPosition;
    public FastNoiseLite heightNoise;
    public FastNoiseLite heightNoise2;
    public FastNoiseLite biomeNoise;

    private bool mapGenRunning = false;
    private Thread mapGenThread;
    private ConcurrentQueue<Vector3Int> chunksToGen = new ConcurrentQueue<Vector3Int>();
    private ConcurrentQueue<Chunk> chunksToSpawn = new ConcurrentQueue<Chunk>();

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
        Chunk chunk = new Chunk(chunkPos);
        GenerateVoxels(chunk);
        chunk.QueueUpdateMesh();
        chunksToSpawn.Enqueue(chunk);
    }

    private void GenerateMap(bool actually)
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

        if (actually)
        {
            Debug.Log("Loading from folder...");
            LoadFromFolder(Path.Combine(Application.persistentDataPath, "my_world"));
            Debug.Log("MapGen Thread Starting...");
            mapGenRunning = true;
            mapGenThread = new Thread(MapGenLoop);
            mapGenThread.Start();
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

    public override void GenerateVoxels(Chunk chunk)
    {
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int z = 0; z < Chunk.SIZE; z++)
            {
                Vector3Int slicePos = chunk.chunkPosition * Chunk.SIZE + new Vector3Int(x, 0, z);
                float height = GetHeight(slicePos.x, slicePos.z) * Chunk.SIZE * maxHeightChunks;
                // int biome = GetBiome(slicePos.x, slicePos.z);
                for (int y = 0; y < Chunk.SIZE; y++)
                {
                    Vector3Int worldPos = chunk.chunkPosition * Chunk.SIZE + new Vector3Int(x, y, z);
                    if (chunk.TryGetVoxel(x, y, z, out Voxel vox))
                    {
                        if (worldPos.y < 3 && worldPos.y < height)
                        {
                            vox.Id = 1;
                            chunk.SetVoxel(x, y, z, vox);
                        }
                        else if (worldPos.y < height)
                        {
                            vox.Id = 2;
                            chunk.SetVoxel(x, y, z, vox);
                        }
                        else
                        {
                            vox.Id = 0;
                            chunk.SetVoxel(x, y, z, vox);
                        }
                    }
                }
            }
        }
    }

    public float GetHeight(int x, int z)
    {
        return (heightNoise.GetNoise(x, z) * 0.5f + 0.5f) * (heightNoise2.GetNoise(x, z) * 0.5f + 0.5f);
    }

    public int GetBiome(int x, int z)
    {
        return Mathf.FloorToInt((biomeNoise.GetNoise(x, z) * 0.5f + 0.5f) * (maxMaterial - minMaterial + 1) + minMaterial);
    }

    public override Voxel SetVoxelData(Voxel vox, Vector3Int pos)
    {
        if (voxelTypes[vox.Id].lit)
            vox.Emit = voxelTypes[vox.Id].emission;
        else
            vox.Emit = new Color32(0, 0, 0, vox.Emit.a);
        vox.Layer = voxelTypes[vox.Id].layer;
        vox.UserData = null;
        // TODO: sand
        return vox;
    }

    public void OnDestroy()
    {
        mapGenRunning = false;
        mapGenThread?.Join();
        SaveToFolder(Path.Combine(Application.persistentDataPath, "my_world"));
    }

    public void Start()
    {
        materials.Clear();
        for (int i = 0; i < voxelTypes.Count; i++)
            materials.Add(voxelTypes[i].material);
        GenerateMap(genOnStart);
    }

    public override void Update()
    {
        base.Update();
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
}