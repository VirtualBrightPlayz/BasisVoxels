using System;
using UnityEngine;

public struct Voxel
{
    public byte Id;
    public Color32 Emit;
    public bool IsActive => Id != 0;
    public byte Layer;
    public object UserData;

    public void Init()
    {
        Id = 0;
        Layer = 0;
        Emit = new Color32(0, 0, 0, 0);
        UserData = null;
    }
}

public sealed class Chunk
{
    public const int SIZE = 16;
    public Voxel[] voxels;
    public Color32[] lights;
    public Color32[] visibleLights;
    public Vector3Int chunkPosition;
    public int shouldUpdate = 0;

    public bool TryGetVoxel(int x, int y, int z, out Voxel voxel)
    {
        voxel = default;
        if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || z < 0 || z >= SIZE)
            return false;
        voxel = voxels[x + y * SIZE + z * SIZE * SIZE];
        return true;
    }

    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
        if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || z < 0 || z >= SIZE)
            return;
        voxels[x + y * SIZE + z * SIZE * SIZE] = voxel;
    }

    public bool TryGetLight(int x, int y, int z, out Color32 light)
    {
        light = default;
        if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || z < 0 || z >= SIZE)
            return false;
        light = lights[x + y * SIZE + z * SIZE * SIZE];
        return true;
    }

    public bool TryGetVisibleLight(int x, int y, int z, out Color32 light)
    {
        light = default;
        if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || z < 0 || z >= SIZE)
            return false;
        light = visibleLights[x + y * SIZE + z * SIZE * SIZE];
        return true;
    }

    public void SetLight(int x, int y, int z, Color32 light)
    {
        if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || z < 0 || z >= SIZE)
            return;
        lights[x + y * SIZE + z * SIZE * SIZE] = light;
    }

    public void UpdateLightBuffers()
    {
        Array.Copy(lights, visibleLights, visibleLights.Length);
        Array.Fill(lights, new Color32(0, 0, 0, 0));
    }

    public void QueueUpdateMesh()
    {
        shouldUpdate++;
    }

    public Chunk()
    {
    }

    public Chunk(Vector3Int pos)
    {
        voxels = new Voxel[SIZE * SIZE * SIZE];
        lights = new Color32[SIZE * SIZE * SIZE];
        visibleLights = new Color32[SIZE * SIZE * SIZE];
        chunkPosition = pos;
        for (int i = 0; i < voxels.Length; i++)
        {
            voxels[i].Init();
            visibleLights[i] = lights[i] = voxels[i].Emit;
        }
    }
}