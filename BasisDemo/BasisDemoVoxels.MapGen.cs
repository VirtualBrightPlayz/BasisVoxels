using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
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
    public bool hasMap = false;

    private Vector3Int lastPos;

    private async Task TryGenChunk(Vector3Int pos)
    {
        if (genRunning)
            return;
        genRunning = true;
        List<VoxelMesh> meshes = new List<VoxelMesh>();
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int y = 0; y <= renderDistance; y++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    VoxelMesh mesh = await SpawnChunk(new Vector3Int(pos.x + x, y, pos.z + z));
                    if (mesh != null)
                        meshes.Add(mesh);
                }
            }
        }
        for (int i = 0; i < meshes.Count; i++)
        {
            await Task.Run(() =>
            {
                GenerateVoxels(meshes[i].chunk);
            });
        }
        for (int i = 0; i < meshes.Count; i++)
        {
            if (meshes[i].chunk.chunkPosition.y != 0)
                continue;
            await Task.Run(() =>
            {
                GenerateDecor(meshes[i].chunk);
            });
        }
        foreach (var chunk in meshes)
        {
            chunk.UpdateMesh();
        }
        foreach (var chunk in chunks.Values)
        {
            // if (chunk.meshRenderer != null && chunk.meshRenderer.enabled)
            //     chunk.UpdateMesh();
        }
        genRunning = false;
    }

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
                    Voxel vox = chunk.GetVoxel(x, y, z);
                    if (vox != null)
                    {
                        if (worldPos.y < 3 && worldPos.y < height)
                        {
                            vox.Id = floorBlockId;
                        }
                        else if (worldPos.y < height)
                        {
                            vox.Id = (byte)types.IndexOf(biomes[biome].surface);
                        }
                        else
                        {
                            vox.Id = 0;
                        }
                    }
                }
            }
        }
    }

    public float GetHeight(int x, int z)
    {
        return ((heightNoise.GetNoise(x, z) * 0.5f + 0.5f) * 1f + (heightNoise2.GetNoise(x, z) * 0.5f + 0.5f) * 2f) * Chunk.SIZE * renderDistance / 2f;
    }

    public int GetBiome(int x, int z)
    {
        float val = biomeNoise.GetNoise(x, z);
        return biomes.IndexOf(biomes.OrderBy(p => Mathf.Abs(p.biomePosition - val)).First());
    }

    public async void UpdateMapGen()
    {
        // TODO: multiplayer
        Camera cam = Camera.main;
        if (cam == null || !IsOwner)
            return;
        Vector3Int pos = FloorPosition(cam.transform.position);
        if (lastPos != pos && !genRunning)
        {
            await TryGenChunk(pos);
            if (chunks.TryGetValue(pos, out VoxelMesh mesh))
            {
                mesh.UpdateMesh();
            }
            lastPos = pos;
        }
    }

    private async Task GenerateMap(bool actually)
    {
        Debug.Log("Generating Map...");
        hasMap = true;
        heightNoise = new FastNoiseLite(seed);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFrequency(0.005f);

        heightNoise2 = new FastNoiseLite(seed);
        heightNoise2.SetFrequency(0.005f);
        heightNoise2.SetNoiseType(FastNoiseLite.NoiseType.Cellular);

        biomeNoise = new FastNoiseLite(seed + 1);
        biomeNoise.SetFrequency(0.002f);
        biomeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        rng = new System.Random(seed);
        if (actually)
        {
            await TryGenChunk(Vector3Int.zero);
            if (genOnStart)
                BasisLocalPlayer.Instance.Teleport(Vector3.up * Chunk.SIZE * renderDistance, Quaternion.identity);
        }
    }
}