using UnityEngine;

public class Voxel
{
    public byte Id = 0;
    public bool IsActive => Id != 0;
}

public class Chunk
{
    public const int SIZE = 16;
    public Voxel[] voxels;
    public Vector3Int chunkPosition;

    public Voxel GetVoxel(int x, int y, int z)
    {
        if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || z < 0 || z >= SIZE)
            return null;
        return voxels[x + y * SIZE + z * SIZE * SIZE];
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
            voxels[i] = new Voxel();
        }
    }
}