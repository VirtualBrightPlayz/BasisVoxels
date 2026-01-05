using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public partial class VoxelWorld : MonoBehaviour
{
    public List<Material> materials = new List<Material>();
    protected HashSet<Vector3Int> chunkPositions = new HashSet<Vector3Int>();
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

    public Action<double> OnTick = (_) => { };
    public Action<Chunk> OnSpawnChunk = (_) => { };
    public Action<Vector3Int, Chunk, Voxel> OnSetVoxel = (_, _, _) => { };

    public static Vector3Int RoundPosition(Vector3 pos)
    {
        Vector3 pos2 = pos - Vector3.one * Chunk.SIZE / 2f;
        return new Vector3Int(Mathf.RoundToInt(pos2.x / Chunk.SIZE), Mathf.RoundToInt(pos2.y / Chunk.SIZE), Mathf.RoundToInt(pos2.z / Chunk.SIZE));
    }

    public static Vector3Int FloorPosition(Vector3Int pos)
    {
        Vector3Int pos2 = pos;
        return new Vector3Int(Mathf.FloorToInt((float)pos2.x / Chunk.SIZE), Mathf.FloorToInt((float)pos2.y / Chunk.SIZE), Mathf.FloorToInt((float)pos2.z / Chunk.SIZE));
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
        if (!chunk.TryGetVoxel(x, y, z, out Voxel vox))
        {
            Vector3Int position = UnroundPosition(chunk.chunkPosition);
            return IsFaceVisible(position.x + x, position.y + y, position.z + z, layer);
        }
        return !vox.IsActive || layer != vox.Layer;
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
            UpdateChunks(chunkPos, true, true);
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
        OnTick?.Invoke(delta);
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
            OnSetVoxel?.Invoke(pos, mesh, voxel);
        }
    }

    // pos in world space
    public void SetVoxelWithData(Chunk mesh, Vector3Int pos, Voxel voxel)
    {
        Vector3Int voxelPos = pos;
        mesh.SetVoxel(voxelPos.x, voxelPos.y, voxelPos.z, SetVoxelData(voxel, UnroundPosition(mesh.chunkPosition) + pos));
    }

    public virtual Voxel SetVoxelData(Voxel vox, Vector3Int pos)
    {
        return vox;
    }

    public virtual void ProcessLight(Vector3Int pos)
    {
    }

    public void UpdateChunks(Vector3Int chunkPos, bool updateMeshes, bool updateLightmap, int area = 1)
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
            // await Task.Run(() => UpdateVoxelLightmap(pos2, col));
            UpdateVoxelLightmap(pos2, col);
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
        if (chunkPositions.Contains(pos) && chunks.TryGetValue(pos, out Chunk ch))
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
            chunkPositions.Add(pos);
            OnSpawnChunk?.Invoke(chunk);
            return chunk;
        }
    }

    public Chunk SpawnChunk(Vector3Int pos)
    {
        if (!chunkPositions.Contains(pos))
        {
            Chunk chunk = new Chunk(pos);
            chunks.Add(pos, chunk);
            chunkPositions.Add(pos);
            OnSpawnChunk?.Invoke(chunk);
            return chunk;
        }
        return null;
    }

    public virtual void GenerateVoxels(Chunk chunk)
    {
    }

    public virtual void Update()
    {
        UpdateTasks();
        Tick();
    }

    #region File IO

    public void SaveToFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        using FileStream levelFS = File.OpenWrite(Path.Combine(path, "level.dat"));
        ulong count = SaveLevelFormat1(levelFS);
        for (ulong i = 1; i <= count; i++)
        {
            using FileStream chunkFS = File.OpenWrite(Path.Combine(path, i + ".dat"));
            using BinaryWriter chunkWriter = new BinaryWriter(chunkFS);
            List<Chunk> foundChunks = new List<Chunk>();
            foreach (var kvp in chunks)
            {
                if (kvp.Value.fileId == i)
                {
                    foundChunks.Add(kvp.Value);
                }
            }
            chunkWriter.Write((int)foundChunks.Count);
            for (int j = 0; j < foundChunks.Count; j++)
            {
                SaveChunkFormat1(chunkWriter, foundChunks[j]);
            }
        }
    }

    public ulong SaveLevelFormat1(Stream stream)
    {
        using BinaryWriter writer = new BinaryWriter(stream);
        writer.Write((uint)1); // version
        writer.Write((ulong)chunks.Count);
        ulong i = 1;
        int j = 0;
        foreach (var kvp in chunks)
        {
            j++;
            if (j > Chunk.SIZE)
            {
                i++;
                j = 0;
            }
            writer.Write(kvp.Key.x);
            writer.Write(kvp.Key.y);
            writer.Write(kvp.Key.z);
            writer.Write(i);
            kvp.Value.fileId = i;
        }
        return i;
    }

    public void SaveChunkFormat1(BinaryWriter writer, Chunk chunk)
    {
        writer.Write(chunk.chunkPosition.x);
        writer.Write(chunk.chunkPosition.y);
        writer.Write(chunk.chunkPosition.z);
        for (int i = 0; i < Chunk.SIZE * Chunk.SIZE * Chunk.SIZE; i++)
            writer.Write(chunk.voxels[i].Id);
    }

    public void LoadFromFolder(string path)
    {
        if (!Directory.Exists(path))
            return;
        using FileStream levelFS = File.OpenRead(Path.Combine(path, "level.dat"));
        ulong[] files = LoadLevelFormat1(levelFS);
        for (ulong i = 0; i < (ulong)files.LongLength; i++)
        {
            ulong fileId = files[i];
            using FileStream chunkFS = File.OpenRead(Path.Combine(path, fileId + ".dat"));
            using BinaryReader chunkReader = new BinaryReader(chunkFS);
            int count = chunkReader.ReadInt32();
            for (int j = 0; j < count; j++)
            {
                LoadChunkFormat1(chunkReader).QueueUpdateMesh();
            }
        }
    }

    public ulong[] LoadLevelFormat1(Stream stream)
    {
        HashSet<ulong> files = new HashSet<ulong>();
        using BinaryReader reader = new BinaryReader(stream);
        reader.ReadUInt32(); // version
        ulong count = reader.ReadUInt64();
        for (ulong i = 0; i < count; i++)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            int z = reader.ReadInt32();
            ulong fileId = reader.ReadUInt64();
            files.Add(fileId);
        }
        return files.ToArray();
    }

    public Chunk LoadChunkFormat1(BinaryReader reader)
    {
        int x = reader.ReadInt32();
        int y = reader.ReadInt32();
        int z = reader.ReadInt32();
        Chunk chunk = SpawnOrGetChunk(new Vector3Int(x, y, z));
        for (int i = 0; i < Chunk.SIZE * Chunk.SIZE * Chunk.SIZE; i++)
            chunk.voxels[i].Id = reader.ReadByte();
        return chunk;
    }

    #endregion

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