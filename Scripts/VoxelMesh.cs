using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class VoxelMesh
{
    private static List<Vector3> verts = new List<Vector3>();
    private static List<Vector3> normals = new List<Vector3>();
    private static Dictionary<int, List<int>> trisLookup = new Dictionary<int, List<int>>();
    private static List<Vector2> uvs = new List<Vector2>();
    private static List<Color32> colors = new List<Color32>();
    public Chunk chunk;
    public VoxelWorld world;
    private Mesh mesh;
    private Material[] materials = new Material[0];
    public bool isUpdating { get; private set; } = false;
    public bool IsValid = false;

    private static bool[] visibleFaces = new bool[6];
    private static Color32[] lightFaces = new Color32[6];

    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector4 tangent;
        public Color32 color;
        public Vector2 uv;
    }

    private void AddChunk()
    {
        // bool[] visibleFaces = new bool[6];
        // Color32[] lightFaces = new Color32[6];
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int y = 0; y < Chunk.SIZE; y++)
            {
                for (int z = 0; z < Chunk.SIZE; z++)
                {
                    AddVoxel(x, y, z, ref visibleFaces, ref lightFaces);
                }
            }
        }
    }

    private void AddVoxel(int x, int y, int z, ref bool[] visibleFaces, ref Color32[] lightFaces)
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
        return mesh;
    }

    private void ResetDataLists()
    {
        if (verts.Capacity < Chunk.SIZE * Chunk.SIZE * Chunk.SIZE)
            verts.Capacity = Chunk.SIZE * Chunk.SIZE * Chunk.SIZE;
        if (normals.Capacity < Chunk.SIZE * Chunk.SIZE * Chunk.SIZE)
            normals.Capacity = Chunk.SIZE * Chunk.SIZE * Chunk.SIZE;
        if (uvs.Capacity < Chunk.SIZE * Chunk.SIZE * Chunk.SIZE)
            uvs.Capacity = Chunk.SIZE * Chunk.SIZE * Chunk.SIZE;
        if (colors.Capacity < Chunk.SIZE * Chunk.SIZE * Chunk.SIZE)
            colors.Capacity = Chunk.SIZE * Chunk.SIZE * Chunk.SIZE;
        verts.Clear();
        normals.Clear();
        uvs.Clear();
        colors.Clear();
        // clear the lists, not the dictionary to prevent GC
        foreach (var kvp in trisLookup)
            kvp.Value.Clear();
    }

    public void UpdateMesh()
    {
        if (isUpdating || mesh == null || chunk == null)
            return;
        isUpdating = true;
        ResetDataLists();
        AddChunk();
        Mesh.MeshDataArray array = Mesh.AllocateWritableMeshData(1);
        Mesh.MeshData data = array[0];
        // await Task.Run(() =>
        {
            NativeArray<VertexAttributeDescriptor> attributeDescriptors = new NativeArray<VertexAttributeDescriptor>(5, Allocator.Temp);
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
        }//);
        Mesh.ApplyAndDisposeWritableMeshData(array, mesh);
        mesh.bounds = new Bounds(Vector3.one * Chunk.SIZE * 0.5f, Vector3.one * Chunk.SIZE);
        int instId = mesh.GetInstanceID();
        Physics.BakeMesh(instId, false, MeshColliderCookingOptions.None);
        // Material[] tempList = new Material[trisLookup.Count];
        Array.Resize(ref materials, trisLookup.Count);
        // Material[] tempList = materials;
        int i2 = 0;
        foreach (var id in trisLookup.Keys)
        {
            if (id >= 0 && id < world.materials.Count)
                materials[i2++] = world.materials[id];
            else
                i2++;
        }
        // materials = tempList;
        isUpdating = false;
        chunk.shouldUpdate = Mathf.Max(chunk.shouldUpdate - 1, 0);
        IsValid = true;
    }

    public void Draw()
    {
        if (!IsValid)
            return;
        for (int i = 0; i < Mathf.Min(materials.Length, mesh.subMeshCount); i++)
        {
            Matrix4x4 matrix = Matrix4x4.Translate(chunk.chunkPosition * Chunk.SIZE);
            RenderParams render = new RenderParams(materials[i]);
            render.shadowCastingMode = ShadowCastingMode.On;
            render.receiveShadows = true;
            Graphics.RenderMesh(render, mesh, i, matrix);
        }
    }

    public VoxelMesh(VoxelWorld wo, Chunk ch)
    {
        mesh = new Mesh();
        world = wo;
        chunk = ch;
        isUpdating = false;
        chunk.QueueUpdateMesh();
    }
}