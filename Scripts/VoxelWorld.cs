using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public abstract class VoxelWorld : MonoBehaviour
{
    public List<Material> materials = new List<Material>();
    public VoxelMesh prefab;
    protected Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
    public int seed = 1337;
    protected bool genRunning = false;
    protected Queue<Vector3Int> chunkUpdateQueue = new Queue<Vector3Int>();
    protected Queue<Vector3Int> voxelsToTick = new Queue<Vector3Int>();
    protected Queue<(Vector3Int, byte)> voxelUpdateQueue = new Queue<(Vector3Int, byte)>();

    public int tickRate = 20;
    public double tickSpeed = 1d;
    public int maxTicks = 60;
    protected double lastTickTime;
    protected bool tickRunning = false;

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
        return pos * Chunk.SIZE;
    }

    public static Vector3Int GetVoxelPosition(Vector3 pos)
    {
        return new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));
    }

    public bool TryGetChunk(Vector3Int pos, out Chunk chunk)
    {
        chunk = default;
        if (chunks.TryGetValue(pos, out chunk))
            return true;
        return false;
    }

    public Voxel GetVoxelOrDefault(int x, int y, int z)
    {
        Voxel vox;
        if (!TryGetVoxel(x, y, z, out vox))
            vox.Init();
        return vox;
    }

    public Voxel GetVoxelOrDefault(Chunk chunk, int x, int y, int z)
    {
        Voxel vox;
        if (!TryGetVoxel(chunk, x, y, z, out vox))
            vox.Init();
        return vox;
    }

    public Color32 GetVisibleLightOrZero(Chunk chunk, int x, int y, int z)
    {
        Vector3Int pos = new Vector3Int(x, y, z) + UnroundPosition(chunk.chunkPosition);
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out Chunk mesh))
        {
            Vector3Int voxelPos = GetVoxelPosition(pos) - UnroundPosition(chunkPos);
            if (mesh.TryGetVisibleLight(voxelPos.x, voxelPos.y, voxelPos.z, out Color32 light))
                return light;
        }
        // if (TryGetVoxelLight(pos, out Voxel _, out Color32 light))
            // return light;
        return new Color32(0, 0, 0, 0);
    }

    public bool TryGetVoxel(int x, int y, int z, out Voxel voxel)
    {
        return TryGetVoxel(new Vector3Int(x, y, z), out voxel);
    }

    public bool TryGetVoxel(Chunk chunk, int x, int y, int z, out Voxel voxel)
    {
        voxel = default;
        if (chunk != null)
        {
            Vector3Int voxelPos = new Vector3Int(x, y, z) + UnroundPosition(chunk.chunkPosition);
            return TryGetVoxel(voxelPos.x, voxelPos.y, voxelPos.z, out voxel);
        }
        return false;
    }

    public bool TryGetVoxel(Vector3Int pos, out Voxel voxel)
    {
        voxel = default;
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out Chunk chunk))
        {
            Vector3Int voxelPos = GetVoxelPosition(pos) - UnroundPosition(chunkPos);
            return chunk.TryGetVoxel(voxelPos.x, voxelPos.y, voxelPos.z, out voxel);
        }
        return false;
    }

    public bool TryGetVoxelLight(Vector3Int pos, out Voxel voxel, out Color32 light)
    {
        voxel = default;
        light = default;
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out Chunk chunk))
        {
            Vector3Int voxelPos = GetVoxelPosition(pos) - UnroundPosition(chunkPos);
            return chunk.TryGetVoxel(voxelPos.x, voxelPos.y, voxelPos.z, out voxel) && chunk.TryGetLight(voxelPos.x, voxelPos.y, voxelPos.z, out light);
        }
        return false;
    }

    public bool IsFaceVisible(Chunk chunk, int x, int y, int z, byte layer)
    {
        Vector3Int position = UnroundPosition(chunk.chunkPosition);
        return IsFaceVisible(position.x + x, position.y + y, position.z + z, layer);
    }

    public bool IsFaceVisible(int x, int y, int z, byte layer)
    {
        if (!TryGetVoxel(x, y, z, out Voxel vox))
            return true;
        return !vox.IsActive || layer != vox.Layer;
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
        if (tickRunning)
            return;
        tickRunning = true;
        double rt = Time.timeAsDouble;
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
        tickRunning = false;
    }

    public virtual void TickWorld(double delta)
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
                QueueUpdateChunks(FloorPosition(voxelData.pos), false);
            }
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
                        if (chunks.TryGetValue(chunkPos + new Vector3Int(x, y, z), out Chunk chunk))
                            chunk.QueueUpdateMesh();
        if (chunkUpdateQueue.Contains(chunkPos))
            return;
        chunkUpdateQueue.Enqueue(chunkPos);
    }

    public void QueueSetVoxel(Vector3Int pos, byte id)
    {
        voxelUpdateQueue.Enqueue((pos, id));
    }

    public void QueueTickVoxel(Vector3Int pos)
    {
        voxelsToTick.Enqueue(pos);
    }

    public void QueueTickVoxelArea(Vector3Int pos)
    {
        voxelsToTick.Enqueue(pos);
        voxelsToTick.Enqueue(pos + Vector3Int.up);
        voxelsToTick.Enqueue(pos + Vector3Int.down);
        voxelsToTick.Enqueue(pos + Vector3Int.left);
        voxelsToTick.Enqueue(pos + Vector3Int.right);
        voxelsToTick.Enqueue(pos + Vector3Int.forward);
        voxelsToTick.Enqueue(pos + Vector3Int.back);
    }

    public void SetVoxelRaw(Vector3Int pos, Voxel voxel)
    {
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out Chunk mesh))
        {
            Vector3Int voxelPos = GetVoxelPosition(pos) - UnroundPosition(chunkPos);
            mesh.SetVoxel(voxelPos.x, voxelPos.y, voxelPos.z, voxel);
        }
    }

    public void SetVoxelLightRaw(Vector3Int pos, Voxel voxel, Color32 light)
    {
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out Chunk mesh))
        {
            Vector3Int voxelPos = GetVoxelPosition(pos) - UnroundPosition(chunkPos);
            mesh.SetVoxel(voxelPos.x, voxelPos.y, voxelPos.z, voxel);
            mesh.SetLight(voxelPos.x, voxelPos.y, voxelPos.z, light);
        }
    }

    public void SetVoxelWithData(Vector3Int pos, Voxel voxel)
    {
        Vector3Int chunkPos = FloorPosition(pos);
        if (chunks.TryGetValue(chunkPos, out Chunk mesh))
        {
            Vector3Int voxelPos = GetVoxelPosition(pos) - UnroundPosition(chunkPos);
            mesh.SetVoxel(voxelPos.x, voxelPos.y, voxelPos.z, SetVoxelData(voxel, pos));
        }
    }

    public virtual Voxel SetVoxelData(Voxel vox, Vector3Int pos)
    {
        return vox;
    }

    public virtual void ProcessLight(Vector3Int pos)
    {
    }

    public async Task UpdateChunks(Vector3Int chunkPos, bool updateMeshes, bool updateLightmap, int area = 1)
    {
        if (genRunning)
            return;
        genRunning = true;
        Queue<Vector3Int> subLights = new Queue<Vector3Int>();
        Queue<(Vector3Int, Color32)> lights = new Queue<(Vector3Int, Color32)>();
        if (updateLightmap)
        {
            for (int x = -area; x <= area; x++)
                for (int y = -area; y <= area; y++)
                    for (int z = -area; z <= area; z++)
                        if (chunks.TryGetValue(chunkPos + new Vector3Int(x, y, z), out Chunk chunk))
                        {
                            for (int x2 = 0; x2 < Chunk.SIZE; x2++)
                                for (int y2 = 0; y2 < Chunk.SIZE; y2++)
                                    for (int z2 = 0; z2 < Chunk.SIZE; z2++)
                                    {
                                        if (chunk.TryGetVoxel(x2, y2, z2, out Voxel vox))
                                        {
                                            if (vox.Emit.a == 0)
                                            {
                                                chunk.SetVoxel(x2, y2, z2, vox);
                                                continue;
                                            }
                                            if (vox.Emit.a == 0 || (vox.Emit.r == 0 && vox.Emit.g == 0 && vox.Emit.b == 0))
                                            {
                                                subLights.Enqueue(UnroundPosition(chunkPos + new Vector3Int(x, y, z)) + new Vector3Int(x2, y2, z2));
                                                vox.Emit.a = 0;
                                            }
                                            else
                                                lights.Enqueue((UnroundPosition(chunkPos + new Vector3Int(x, y, z)) + new Vector3Int(x2, y2, z2), vox.Emit));
                                            chunk.SetVoxel(x2, y2, z2, vox);
                                        }
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
            await Task.Run(() => UpdateVoxelLightmap(pos2, col));
        }
        {
            for (int x = -area; x <= area; x++)
                for (int y = -area; y <= area; y++)
                    for (int z = -area; z <= area; z++)
                        if (chunks.TryGetValue(chunkPos + new Vector3Int(x, y, z), out Chunk chunk))
                        {
                            if (updateLightmap)
                                chunk.UpdateLightBuffers();
                            if (updateMeshes)
                                chunk.QueueUpdateMesh();
                        }
        }
        genRunning = false;
    }

    public Chunk SpawnOrGetChunk(Vector3Int pos)
    {
        if (chunks.TryGetValue(pos, out Chunk ch))
        {
            return ch;
        }
        else
        {
            // VoxelMesh chunk = Instantiate(prefab, UnroundPosition(pos), Quaternion.identity, transform);
            // chunk.world = this;
            // chunk.gameObject.SetActive(true);
            // chunk.Setup();
            Chunk chunk = new Chunk(pos);
            chunks.Add(pos, chunk);
            return chunk;
        }
    }

    public Chunk SpawnChunk(Vector3Int pos)
    {
        if (!chunks.ContainsKey(pos))
        {
            Chunk chunk = new Chunk(pos);
            chunks.Add(pos, chunk);
            return chunk;
        }
        return null;
    }

    public abstract void GenerateVoxels(Chunk chunk);

    #region Lighting

    public void UpdateVoxelLightmap(Vector3Int voxPos, Color32 baseLight)
    {
        Queue<(Vector3Int, Color32)> queue = new Queue<(Vector3Int, Color32)>();
        queue.Enqueue((voxPos, baseLight));
        List<Vector3Int> list = new List<Vector3Int>();
        while (queue.Count != 0)
        {
            (Vector3Int pos, Color32 light) = queue.Dequeue();
            if (list.Contains(pos) || light.a <= 0)
                continue;
            list.Add(pos);
            if (!TryGetVoxelLight(pos, out Voxel vox, out Color32 voxLight))
                continue;
            if (vox.IsActive && pos != voxPos)
                continue;
            if (voxLight.a > light.a)
                continue;
            float amount = (float)light.a / baseLight.a;
            light.r = (byte)(baseLight.r * amount);
            light.g = (byte)(baseLight.g * amount);
            light.b = (byte)(baseLight.b * amount);
            voxLight = light;
            light.a--;
            SetVoxelLightRaw(pos, vox, voxLight);
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
    }

    #endregion
}