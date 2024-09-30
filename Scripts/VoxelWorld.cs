using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public abstract class VoxelWorld : MonoBehaviour
{
    public List<Material> materials = new List<Material>();
    public VoxelMesh prefab;
    protected Dictionary<Vector3Int, VoxelMesh> chunks = new Dictionary<Vector3Int, VoxelMesh>();
    public int seed = 1337;
    protected bool genRunning = false;

    public static Vector3Int RoundPosition(Vector3 pos)
    {
        Vector3 pos2 = pos - Vector3.one * Chunk.SIZE / 2f;
        return new Vector3Int(Mathf.RoundToInt(pos2.x / Chunk.SIZE), Mathf.RoundToInt(pos2.y / Chunk.SIZE), Mathf.RoundToInt(pos2.z / Chunk.SIZE));
    }

    public static Vector3Int FloorPosition(Vector3 pos)
    {
        Vector3 pos2 = pos;
        return new Vector3Int(Mathf.FloorToInt(pos2.x / Chunk.SIZE), Mathf.FloorToInt(pos2.y / Chunk.SIZE), Mathf.FloorToInt(pos2.z / Chunk.SIZE));
    }

    public static Vector3Int UnroundPosition(Vector3Int pos)
    {
        return new Vector3Int(pos.x * Chunk.SIZE, pos.y * Chunk.SIZE, pos.z * Chunk.SIZE);
    }

    public Voxel GetVoxel(int x, int y, int z)
    {
        return GetVoxel(new Vector3(x, y, z));
    }

    public Vector3Int GetVoxelPosition(Vector3 pos)
    {
        return new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));
    }

    public Voxel GetVoxel(Vector3 pos)
    {
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out VoxelMesh chunk) && chunk.chunk != null)
        {
            Vector3Int voxelPos = GetVoxelPosition(pos) - UnroundPosition(chunkPos);
            return chunk.chunk.GetVoxel(voxelPos.x, voxelPos.y, voxelPos.z);
        }
        return null;
    }

    public bool IsFaceVisible(Chunk chunk, int x, int y, int z)
    {
        Vector3Int position = UnroundPosition(chunk.chunkPosition);
        return IsFaceVisible(position.x + x, position.y + y, position.z + z);
    }

    public bool IsFaceVisible(int x, int y, int z)
    {
        Voxel vox = GetVoxel(x, y, z);
        if (vox == null)
            return true;
        return !GetVoxel(x, y, z).IsActive;
    }

    public void UpdateChunk(Vector3 pos)
    {
        if (genRunning)
            return;
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out VoxelMesh chunk))
        {
            chunk.UpdateMesh();
        }
    }

    public void UpdateChunks(Vector3 pos)
    {
        if (genRunning)
            return;
        Vector3Int chunkPos = FloorPosition(pos);
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (chunks.TryGetValue(chunkPos + new Vector3Int(x, y, z), out VoxelMesh chunk))
                        chunk.UpdateMesh();
    }

    [Obsolete]
    public async Task GenChunk(Vector3Int pos)
    {
        if (!chunks.ContainsKey(pos))
        {
            VoxelMesh chunk = Instantiate(prefab, UnroundPosition(pos), Quaternion.identity, transform);
            chunk.world = this;
            chunks.Add(pos, chunk);
            chunk.gameObject.SetActive(true);
            await chunk.Setup();
            await chunk.Generate();
        }
    }

    public async Task<VoxelMesh> SpawnOrGetChunk(Vector3Int pos)
    {
        if (chunks.TryGetValue(pos, out VoxelMesh ch))
        {
            return ch;
        }
        else
        {
            VoxelMesh chunk = Instantiate(prefab, UnroundPosition(pos), Quaternion.identity, transform);
            chunk.world = this;
            chunks.Add(pos, chunk);
            chunk.gameObject.SetActive(true);
            await chunk.Setup();
            return chunk;
        }
    }

    public async Task<VoxelMesh> SpawnChunk(Vector3Int pos)
    {
        if (!chunks.ContainsKey(pos))
        {
            VoxelMesh chunk = Instantiate(prefab, UnroundPosition(pos), Quaternion.identity, transform);
            chunk.world = this;
            chunks.Add(pos, chunk);
            chunk.gameObject.SetActive(true);
            await chunk.Setup();
            return chunk;
        }
        return null;
    }

    public abstract void GenerateVoxels(Chunk chunk);
}