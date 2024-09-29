using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class VoxelMesh : MonoBehaviour
{
    private List<Vector3> verts = new List<Vector3>();
    private Dictionary<int, List<int>> trisLookup = new Dictionary<int, List<int>>();
    private List<Vector2> uvs = new List<Vector2>();
    public Chunk chunk;
    public VoxelWorld world;
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private bool isUpdating = false;

    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector4 tangent;
        public Vector2 uv;
    }

    private void AddChunk()
    {
        bool[] visibleFaces = new bool[6];
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            for (int y = 0; y < Chunk.SIZE; y++)
            {
                for (int z = 0; z < Chunk.SIZE; z++)
                {
                    AddVoxel(x, y, z, ref visibleFaces);
                }
            }
        }
    }

    private void AddVoxel(int x, int y, int z, ref bool[] visibleFaces)
    {
        Voxel vox = chunk.GetVoxel(x, y, z);
        if (vox.IsActive)
        {
            visibleFaces[0] = world.IsFaceVisible(chunk, x, y + 1, z); // up
            visibleFaces[1] = world.IsFaceVisible(chunk, x, y - 1, z); // down
            visibleFaces[2] = world.IsFaceVisible(chunk, x - 1, y, z); // left
            visibleFaces[3] = world.IsFaceVisible(chunk, x + 1, y, z); // right
            visibleFaces[4] = world.IsFaceVisible(chunk, x, y, z + 1); // forward
            visibleFaces[5] = world.IsFaceVisible(chunk, x, y, z - 1); // back

            for (int i = 0; i < visibleFaces.Length; i++)
            {
                if (visibleFaces[i])
                {
                    AddFaceData(x, y, z, i, vox.Id);
                }
            }
        }
    }

    private void AddFaceData(int x, int y, int z, int faceIndex, int id)
    {
        switch (faceIndex)
        {
            case 0: // up
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
        AddTriangles(id);
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

    public async void UpdateMesh()
    {
        await UpdateMeshAsync();
    }

    public async Task UpdateMeshAsync()
    {
        if (isUpdating)
            return;
        isUpdating = true;
        verts.Clear();
        trisLookup.Clear();
        uvs.Clear();
        verts.Capacity = Chunk.SIZE * Chunk.SIZE * Chunk.SIZE;
        uvs.Capacity = Chunk.SIZE * Chunk.SIZE * Chunk.SIZE;
        await Task.Run(AddChunk);
        // AddChunk();
        Mesh.MeshDataArray array = Mesh.AllocateWritableMeshData(1);
        Mesh.MeshData data = array[0];
        await Task.Run(() =>
        {
            data.SetVertexBufferParams(verts.Count,
                new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, dimension: 3),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, dimension: 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, dimension: 2)
            );
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
                    uv = uvs[i],
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
                data.SetSubMesh(k++, new SubMeshDescriptor(j - trisLookup[id].Count, trisLookup[id].Count));
            }
        });
        Mesh.ApplyAndDisposeWritableMeshData(array, mesh);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        Physics.BakeMesh(mesh.GetInstanceID(), false, MeshColliderCookingOptions.UseFastMidphase);
        if (verts.Count == 0)
        {
            meshFilter.sharedMesh = null;
            meshCollider.sharedMesh = null;
        }
        else
        {
            meshFilter.sharedMesh = mesh;
            meshCollider.sharedMesh = mesh;
        }
        // List<Material> tempList = new List<Material>();
        Material[] tempList = new Material[trisLookup.Count];
        int i2 = 0;
        foreach (var id in trisLookup.Keys)
        {
            if (id >= 0 && id < world.materials.Count)
                tempList[i2++] = world.materials[id];
            else
                i2++;
        }
        meshRenderer.sharedMaterials = tempList;
        isUpdating = false;
        // return Task.CompletedTask;
    }

    public async Task Setup()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        mesh = new Mesh();
        Vector3 pos = transform.position;
        await Task.Run(() =>
        {
            chunk = new Chunk(VoxelWorld.FloorPosition(pos));
        });
    }

    [Obsolete]
    public async Task Generate()
    {
        await Task.Run(() =>
        {
            world.GenerateVoxels(chunk);
        });
    }
}