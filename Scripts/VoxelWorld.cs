using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public abstract class VoxelWorld : MonoBehaviour
{
    public List<Material> materials = new List<Material>();
    public VoxelMesh prefab;
    protected Dictionary<Vector3Int, VoxelMesh> chunks = new Dictionary<Vector3Int, VoxelMesh>();
    public int seed = 1337;
    protected bool genRunning = false;
    protected Queue<Vector3Int> chunkUpdateQueue = new Queue<Vector3Int>();
    protected Queue<(Vector3Int, byte)> voxelUpdateQueue = new Queue<(Vector3Int, byte)>();

    public int tickRate = 20;
    public double tickSpeed = 1d;
    public int maxTicks = 60;
    private double lastTickTime;

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

    public Voxel GetVoxel(Chunk chunk, int x, int y, int z)
    {
        if (chunk != null)
        {
            Vector3Int voxelPos = new Vector3Int(x, y, z) + UnroundPosition(chunk.chunkPosition);
            return GetVoxel(voxelPos.x, voxelPos.y, voxelPos.z);
        }
        return null;
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
        return !vox.IsActive;
    }

    public virtual void UpdateTasks()
    {
        if (!genRunning && chunkUpdateQueue.TryDequeue(out Vector3Int chunkPos))
        {
            _ = UpdateChunks(chunkPos, true, true);
        }
    }

    public virtual void Tick()
    {
        double rt = Time.unscaledTimeAsDouble;
        int ticks = 0;
        while (rt > lastTickTime)
        {
            double delta = tickSpeed / tickRate;
            if (ticks < maxTicks)
                TickWorld(delta);
            lastTickTime += 1d / tickRate;
            ticks++;
        }
        if (ticks >= maxTicks)
        {
            Debug.LogWarning($"Max ticks reached! ({ticks} ticks)");
        }
    }

    public virtual void TickWorld(double delta)
    {
        foreach (var chunk in chunks.Values)
        {
            TickChunk(delta, chunk.chunk);
        }
        while (voxelUpdateQueue.TryDequeue(out (Vector3Int pos, byte id) voxelData))
        {
            SetVoxel(voxelData.pos, voxelData.id);
            QueueUpdateChunks(FloorPosition(voxelData.pos), false);
        }
    }

    public virtual void TickChunk(double delta, Chunk chunk)
    {
        Vector3Int chunkPos = UnroundPosition(chunk.chunkPosition);
        for (int x = 0; x < Chunk.SIZE; x++)
            for (int y = 0; y < Chunk.SIZE; y++)
                for (int z = 0; z < Chunk.SIZE; z++)
                {
                    TickVoxel(delta, chunkPos + new Vector3Int(x, y, z), chunk.GetVoxel(x, y, z));
                }
    }

    public virtual void TickVoxel(double delta, Vector3Int voxelPos, Voxel voxel)
    {
    }

    public void QueueUpdateChunks(Vector3Int chunkPos, bool now)
    {
        if (now)
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (chunks.TryGetValue(chunkPos + new Vector3Int(x, y, z), out VoxelMesh chunk))
                            chunk.UpdateMesh();
        if (chunkUpdateQueue.Contains(chunkPos))
            return;
        chunkUpdateQueue.Enqueue(chunkPos);
    }

    public void QueueSetVoxel(Vector3Int pos, byte id)
    {
        voxelUpdateQueue.Enqueue((pos, id));
    }

    public void SetVoxel(Vector3Int pos, byte id)
    {
        Voxel vox = GetVoxel(pos.x, pos.y, pos.z);
        if (vox == null)
            return;
        SetVoxelData(vox, pos, id);
    }

    public virtual void SetVoxelData(Voxel vox, Vector3Int pos, byte id)
    {
        vox.Id = id;
    }

    public virtual void ProcessLight(Vector3Int pos)
    {
    }

    public async Task UpdateChunks(Vector3Int chunkPos, bool updateMeshes, bool updateLightmap)
    {
        if (genRunning)
            return;
        genRunning = true;
        Queue<Vector3Int> subLights = new Queue<Vector3Int>();
        Queue<(Vector3Int, Color32)> lights = new Queue<(Vector3Int, Color32)>();
        if (updateLightmap)
        {
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (chunks.TryGetValue(chunkPos + new Vector3Int(x, y, z), out VoxelMesh chunk))
                        {
                            for (int x2 = 0; x2 < Chunk.SIZE; x2++)
                                for (int y2 = 0; y2 < Chunk.SIZE; y2++)
                                    for (int z2 = 0; z2 < Chunk.SIZE; z2++)
                                    {
                                        Voxel vox = chunk.chunk.GetVoxel(x2, y2, z2);
                                        vox.Light = new Color32(0, 0, 0, 0);
                                        if (vox.Emit.a == 0 || (vox.Emit.r == 0 && vox.Emit.g == 0 && vox.Emit.b == 0))
                                        {
                                            subLights.Enqueue(UnroundPosition(chunkPos + new Vector3Int(x, y, z)) + new Vector3Int(x2, y2, z2));
                                            vox.Emit.a = 0;
                                        }
                                        else
                                            lights.Enqueue((UnroundPosition(chunkPos + new Vector3Int(x, y, z)) + new Vector3Int(x2, y2, z2), vox.Emit));
                                    }
                        }
        }
        while (subLights.Count != 0)
        {
            Vector3Int pos2 = subLights.Dequeue();
            ProcessLight(pos2);
        }
        while (lights.Count != 0)
        {
            (Vector3Int pos2, Color32 col) = lights.Dequeue();
            ProcessLight(pos2);
            await Task.Run(() => UpdateLightmap(pos2, col));
        }
        if (updateMeshes)
        {
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (chunks.TryGetValue(chunkPos + new Vector3Int(x, y, z), out VoxelMesh chunk))
                            await chunk.UpdateMeshAsync();
        }
        genRunning = false;
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
            chunk.gameObject.SetActive(true);
            await chunk.Setup();
            chunks.Add(pos, chunk);
            return chunk;
        }
    }

    public async Task<VoxelMesh> SpawnChunk(Vector3Int pos)
    {
        if (!chunks.ContainsKey(pos))
        {
            VoxelMesh chunk = Instantiate(prefab, UnroundPosition(pos), Quaternion.identity, transform);
            chunk.world = this;
            chunk.gameObject.SetActive(true);
            await chunk.Setup();
            chunks.Add(pos, chunk);
            return chunk;
        }
        return null;
    }

    public abstract void GenerateVoxels(Chunk chunk);

    #region Lighting

    public void UpdateLightmap(Vector3Int voxPos, Color32 baseLight)
    {
        // if (baseLight.r == 0 && baseLight.g == 0 && baseLight.b == 0)
        //     return;
        Queue<(Vector3Int, Color32)> queue = new Queue<(Vector3Int, Color32)>();
        queue.Enqueue((voxPos, baseLight));
        List<Vector3Int> list = new List<Vector3Int>();
        while (queue.Count != 0)
        {
            (Vector3Int pos, Color32 light) = queue.Dequeue();
            if (list.Contains(pos) || light.a <= 0)
                continue;
            list.Add(pos);
            Voxel vox = GetVoxel(pos);
            if (vox == null)
                continue;
            if (vox.IsActive && pos != voxPos)
                continue;
            if (vox.Light.a > light.a)
                // if (vox.Light.r > light.r || vox.Light.g > light.g || vox.Light.b > light.b)
                    continue;
            float amount = (float)light.a / baseLight.a;
            light.r = (byte)(baseLight.r * amount);
            light.g = (byte)(baseLight.g * amount);
            light.b = (byte)(baseLight.b * amount);
            // vox.Light.r = (byte)Mathf.Clamp(vox.Light.r + light.r, 0, 255);
            // vox.Light.g = (byte)Mathf.Clamp(vox.Light.g + light.g, 0, 255);
            // vox.Light.b = (byte)Mathf.Clamp(vox.Light.b + light.b, 0, 255);
            vox.Light.r = light.r;
            vox.Light.g = light.g;
            vox.Light.b = light.b;
            vox.Light.a = light.a;
            light.a--;
            if (!list.Contains(pos + Vector3Int.up))
                queue.Enqueue((pos + Vector3Int.up, light));
            if (!list.Contains(pos + Vector3Int.down))
                queue.Enqueue((pos + Vector3Int.down, light));
            if (!list.Contains(pos + Vector3Int.left))
                queue.Enqueue((pos + Vector3Int.left, light));
            if (!list.Contains(pos + Vector3Int.right))
                queue.Enqueue((pos + Vector3Int.right, light));
            if (!list.Contains(pos + Vector3Int.forward))
                queue.Enqueue((pos + Vector3Int.forward, light));
            if (!list.Contains(pos + Vector3Int.back))
                queue.Enqueue((pos + Vector3Int.back, light));
        }
        if (baseLight.r == 0 && baseLight.g == 0 && baseLight.b == 0)
        {
            // Voxel vox = GetVoxel(voxPos);
            // vox.Emit = new Color32(0, 0, 0, 0);
        }
    }

    #endregion
}