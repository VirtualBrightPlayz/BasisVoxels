using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class DemoVoxelWorld : VoxelWorld
{
    public int minMaterial = 1;
    public int maxMaterial = 2;
    public int renderDistance = 5;
    private Vector3 lastPosition;
    public FastNoiseLite heightNoise;
    public FastNoiseLite heightNoise2;
    public FastNoiseLite biomeNoise;

    private async Task TryGenChunk(Vector3Int pos)
    {
        if (genRunning)
            return;
        genRunning = true;
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int y = -renderDistance; y <= renderDistance; y++)
            {
                await GenChunk(new Vector3Int(pos.x + x, 0, pos.z + y));
            }
        }
        genRunning = false;
    }

    public override void GenerateVoxels(Chunk chunk)
    {
    }

    public override float GetHeight(int x, int z)
    {
        return (heightNoise.GetNoise(x, z) * 0.5f + 0.5f) * (heightNoise2.GetNoise(x, z) * 0.5f + 0.5f);
    }

    public override int GetBiome(int x, int z)
    {
        return Mathf.FloorToInt((biomeNoise.GetNoise(x, z) * 0.5f + 0.5f) * (maxMaterial - minMaterial + 1) + minMaterial);
    }

    private void Start()
    {
        heightNoise = new FastNoiseLite(seed);
        heightNoise.SetFrequency(0.01f);
        heightNoise2 = new FastNoiseLite(seed);
        heightNoise2.SetFrequency(0.001f);
        heightNoise2.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        biomeNoise = new FastNoiseLite(seed);
        biomeNoise.SetFrequency(0.005f);
        biomeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        _ = TryGenChunk(RoundPosition(Vector3.zero));
    }

    private void FixedUpdate()
    {
        Transform cam = Camera.main.transform;
        Vector3Int pos = RoundPosition(cam.position);
        Vector3Int lastPos = RoundPosition(lastPosition);
        if (pos != lastPos)
        {
            _ = TryGenChunk(pos);
        }
        lastPosition = cam.position;
    }
}