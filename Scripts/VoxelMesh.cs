using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Mutex = System.Threading.Mutex;

public class VoxelMesh
{
    private List<Vector3> verts = new List<Vector3>();
    private List<Vector3> normals = new List<Vector3>();
    private Dictionary<int, List<int>> trisLookup = new Dictionary<int, List<int>>();
    private List<Vector2> uvs = new List<Vector2>();
    private List<Color32> colors = new List<Color32>();
    public VoxelWorld world;
    private Mesh mesh;
    private Material[] materials = new Material[0];
    public bool hasArray { get; private set; } = false;
    private Mesh.MeshDataArray array;
    private Mutex mutex = new Mutex();

    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector4 tangent;
        public Color32 color;
        public Vector2 uv;
    }

    private void AddChunk(Chunk chunk)
    {
        bool[] visibleFaces = new bool[6];
        Color32[] lightFaces = new Color32[6];
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int y = 0; y < Chunk.SIZE; y++)
            {
                for (int z = 0; z < Chunk.SIZE; z++)
                {
                    AddVoxel(chunk, x, y, z, ref visibleFaces, ref lightFaces);
                }
            }
        }
    }

    private void AddVoxel(Chunk chunk, int x, int y, int z, ref bool[] visibleFaces, ref Color32[] lightFaces)
    {
        if (chunk.TryGetVoxel(x, y, z, out Voxel vox) && vox.IsActive)
        {
            visibleFaces[0] = world.IsFaceVisible(chunk, x, y + 1, z, vox.Layer); // up
            visibleFaces[1] = world.IsFaceVisible(chunk, x, y - 1, z, vox.Layer); // down
            visibleFaces[2] = world.IsFaceVisible(chunk, x - 1, y, z, vox.Layer); // left
            visibleFaces[3] = world.IsFaceVisible(chunk, x + 1, y, z, vox.Layer); // right
            visibleFaces[4] = world.IsFaceVisible(chunk, x, y, z + 1, vox.Layer); // forward
            visibleFaces[5] = world.IsFaceVisible(chunk, x, y, z - 1, vox.Layer); // back

            lightFaces[0] = world.GetVisibleLightOrZero(chunk, x, y + 1, z);
            lightFaces[1] = world.GetVisibleLightOrZero(chunk, x, y - 1, z);
            lightFaces[2] = world.GetVisibleLightOrZero(chunk, x - 1, y, z);
            lightFaces[3] = world.GetVisibleLightOrZero(chunk, x + 1, y, z);
            lightFaces[4] = world.GetVisibleLightOrZero(chunk, x, y, z + 1);
            lightFaces[5] = world.GetVisibleLightOrZero(chunk, x, y, z - 1);

            for (int i = 0; i < 6; i++)
            {
                if (visibleFaces[i])
                {
                    AddFaceData(x, y, z, i, vox, lightFaces[i]);
                }
            }
        }
    }

    private void AddFaceData(int x, int y, int z, int faceIndex, Voxel vox, Color32 light)
    {
        Vector3 normal;
        switch (faceIndex)
        {
            default:
                return;
            case 0: // up
                normal = Vector3.up;
                verts.Add(new Vector3(x, y + 1, z));
                verts.Add(new Vector3(x, y + 1, z + 1));
                verts.Add(new Vector3(x + 1, y + 1, z + 1));
                verts.Add(new Vector3(x + 1, y + 1, z));
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(0, 1));
                break;
            case 1: // down
                normal = Vector3.down;
                verts.Add(new Vector3(x, y, z));
                verts.Add(new Vector3(x + 1, y, z));
                verts.Add(new Vector3(x + 1, y, z + 1));
                verts.Add(new Vector3(x, y, z + 1));
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(1, 0));
                break;
            case 2: // left
                normal = Vector3.left;
                verts.Add(new Vector3(x, y, z));
                verts.Add(new Vector3(x, y, z + 1));
                verts.Add(new Vector3(x, y + 1, z + 1));
                verts.Add(new Vector3(x, y + 1, z));
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(0, 1));
                break;
            case 3: // right
                normal = Vector3.right;
                verts.Add(new Vector3(x + 1, y, z + 1));
                verts.Add(new Vector3(x + 1, y, z));
                verts.Add(new Vector3(x + 1, y + 1, z));
                verts.Add(new Vector3(x + 1, y + 1, z + 1));
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(0, 1));
                break;
            case 4: // front
                normal = Vector3.forward;
                verts.Add(new Vector3(x, y, z + 1));
                verts.Add(new Vector3(x + 1, y, z + 1));
                verts.Add(new Vector3(x + 1, y + 1, z + 1));
                verts.Add(new Vector3(x, y + 1, z + 1));
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(0, 1));
                break;
            case 5: // back
                normal = Vector3.back;
                verts.Add(new Vector3(x + 1, y, z));
                verts.Add(new Vector3(x, y, z));
                verts.Add(new Vector3(x, y + 1, z));
                verts.Add(new Vector3(x + 1, y + 1, z));
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(0, 1));
                break;
        }
        colors.Add(light);
        colors.Add(light);
        colors.Add(light);
        colors.Add(light);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        AddTriangles(vox.Id);
    }

    private void AddTriangles(int id)
    {
        int vertCount = verts.Count;

        if (!trisLookup.ContainsKey(id))
            trisLookup.Add(id, new List<int>());

        // first triangle
        trisLookup[id].Add(vertCount - 4);
        trisLookup[id].Add(vertCount - 3);
        trisLookup[id].Add(vertCount - 2);

        // second triangle
        trisLookup[id].Add(vertCount - 4);
        trisLookup[id].Add(vertCount - 2);
        trisLookup[id].Add(vertCount - 1);
    }

    public Mesh GetMesh()
    {
        if (mesh.vertexCount != 0)
        {
            return mesh;
        }
        return null;
    }

    public Material[] GetMaterials()
    {
        return materials;
    }

    public void AllocMesh()
    {
        if (hasArray)
        {
            // Debug.LogError("Array already exists");
            return;
        }
        if (mutex.WaitOne())
        {
            try
            {
                array = Mesh.AllocateWritableMeshData(1);
                hasArray = true;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        /*Mesh.MeshData data = array[0];
        NativeArray<VertexAttributeDescriptor> attributeDescriptors = new NativeArray<VertexAttributeDescriptor>(5, Allocator.Temp);
        attributeDescriptors[0] = new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3);
        attributeDescriptors[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, dimension: 3);
        attributeDescriptors[2] = new VertexAttributeDescriptor(VertexAttribute.Tangent, dimension: 4);
        attributeDescriptors[3] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, dimension: 4);
        attributeDescriptors[4] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2);
        data.SetVertexBufferParams(verts.Count, attributeDescriptors);
        attributeDescriptors.Dispose();*/
    }

    public void ApplyMesh()
    {
        if (!hasArray)
        {
            Debug.LogError("Array doesn't already exists");
            return;
        }
        if (mutex.WaitOne())
        {
            try
            {
                Mesh.ApplyAndDisposeWritableMeshData(array, mesh);
                hasArray = false;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        mesh.bounds = new Bounds(Vector3.one * Chunk.SIZE * 0.5f, Vector3.one * Chunk.SIZE);
    }

    public bool UpdateMesh(Chunk chunk)
    {
        // Debug.Log($"ChunkUpdateMesh: {chunk.chunkPosition}");
        if (mutex.WaitOne())
        {
            try
            {
                if (hasArray)
                {
                    verts.Clear();
                    normals.Clear();
                    trisLookup.Clear();
                    uvs.Clear();
                    colors.Clear();
                    AddChunk(chunk);
                    Mesh.MeshData data = array[0];
                    {
                        NativeArray<VertexAttributeDescriptor> attributeDescriptors = new NativeArray<VertexAttributeDescriptor>(5, Allocator.TempJob);
                        attributeDescriptors[0] = new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3);
                        attributeDescriptors[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, dimension: 3);
                        attributeDescriptors[2] = new VertexAttributeDescriptor(VertexAttribute.Tangent, dimension: 4);
                        attributeDescriptors[3] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, dimension: 4);
                        attributeDescriptors[4] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2);
                        data.SetVertexBufferParams(verts.Count, attributeDescriptors);
                        attributeDescriptors.Dispose();
                        /*data.SetVertexBufferParams(verts.Count,
                            new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3),
                            new VertexAttributeDescriptor(VertexAttribute.Normal, dimension: 3),
                            new VertexAttributeDescriptor(VertexAttribute.Tangent, dimension: 4),
                            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, dimension: 4),
                            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2)
                        );*/
                        int indCount = 0;
                        foreach (var id in trisLookup.Keys)
                            indCount += trisLookup[id].Count;
                        data.SetIndexBufferParams(indCount, IndexFormat.UInt32);
                        NativeArray<ChunkVertex> vertex = data.GetVertexData<ChunkVertex>();
                        for (int i = 0; i < verts.Count; i++)
                        {
                            vertex[i] = new ChunkVertex()
                            {
                                position = verts[i],
                                normal = normals[i],
                                uv = uvs[i],
                                color = colors[i],
                            };
                        }
                        NativeArray<uint> index = data.GetIndexData<uint>();
                        data.subMeshCount = trisLookup.Count;
                        int j = 0;
                        int k = 0;
                        foreach (var id in trisLookup.Keys)
                        {
                            for (int i = 0; i < trisLookup[id].Count; i++)
                            {
                                index[j++] = (uint)trisLookup[id][i];
                            }
                            SubMeshDescriptor desc = new SubMeshDescriptor(j - trisLookup[id].Count, trisLookup[id].Count);
                            data.SetSubMesh(k++, desc);
                        }
                    }
                    // EntityId instId = mesh.GetEntityId();
                    // Physics.BakeMesh(instId, false, MeshColliderCookingOptions.None);
                    Material[] tempList = new Material[trisLookup.Count];
                    int i2 = 0;
                    foreach (var id in trisLookup.Keys)
                    {
                        if (id >= 0 && id < world.materials.Count)
                            tempList[i2++] = world.materials[id];
                        else
                            i2++;
                    }
                    materials = tempList;
                    // chunk.shouldUpdate = Mathf.Max(chunk.shouldUpdate - 1, 0);
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        return true;
    }

    public VoxelMesh(VoxelWorld wo)
    {
        mesh = new Mesh();
        world = wo;
    }
}