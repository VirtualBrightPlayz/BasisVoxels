using UnityEngine;

public struct Voxel
{
    public byte Id;
    public Color32 Emit;
    public Color32 Light;
    public bool IsActive => Id != 0;
    public object UserData;

    public void Init()
    {
        Id = 0;
        Emit = Light = new Color32(0, 0, 0, 0);
        UserData = null;
    }
}

public sealed class Chunk
{
    public const int SIZE = 16;
    public Voxel[] voxels;
    public Vector3Int chunkPosition;

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

    public Chunk()
    {
    }

    public Chunk(Vector3Int pos)
    {
        voxels = new Voxel[SIZE * SIZE * SIZE];
        chunkPosition = pos;
        for (int i = 0; i < voxels.Length; i++)
        {
            voxels[i].Init();
        }
    }
}