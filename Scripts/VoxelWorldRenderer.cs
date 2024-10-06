using System;
using System.Collections.Generic;
using UnityEngine;

public class VoxelWorldRenderer : MonoBehaviour
{
    public VoxelWorld world;
    public MeshCollider colliderPrefab;
    public Dictionary<Vector3Int, VoxelMesh> meshes = new Dictionary<Vector3Int, VoxelMesh>();
    public Dictionary<Vector3Int, MeshCollider> colliders = new Dictionary<Vector3Int, MeshCollider>();
    public int renderDistance = 8;
    private Camera cam;
    private Vector3Int[] lastFrame = new Vector3Int[0];
    private List<Vector3Int> foundChunks = new List<Vector3Int>();
    private List<Vector3Int> removedChunks = new List<Vector3Int>();

    private void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            return;
        }
        int max = (renderDistance * 2 + 1) * (renderDistance * 2 + 1) * (renderDistance * 2 + 1);
        // if (lastFrame.Length != max)
        //     lastFrame = new Vector3Int[max];
        foundChunks.Clear();
        foundChunks.Capacity = max;
        Vector3Int camChunkPos = VoxelWorld.FloorPosition(cam.transform.position);
        for (int x = -renderDistance; x <= renderDistance; x++)
            for (int y = -renderDistance; y <= renderDistance; y++)
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    if (world.TryGetChunk(camChunkPos + new Vector3Int(x, y, z), out Chunk chunk))
                    {
                        foundChunks.Add(chunk.chunkPosition);
                        if (meshes.TryGetValue(chunk.chunkPosition, out VoxelMesh mesh))
                        {
                            if (chunk.shouldUpdate > 0)
                            {
                                _ = mesh.UpdateMeshAsync();
                            }
                            mesh.Draw();
                            if (colliders.TryGetValue(chunk.chunkPosition, out MeshCollider collider))
                            {
                                collider.sharedMesh = mesh.GetMesh();
                            }
                            else
                            {
                                MeshCollider vCollider = Instantiate(colliderPrefab, VoxelWorld.UnroundPosition(chunk.chunkPosition), Quaternion.identity, transform);
                                vCollider.sharedMesh = mesh.GetMesh();
                                colliders.Add(chunk.chunkPosition, vCollider);
                            }
                        }
                        else
                        {
                            VoxelMesh vMesh = new VoxelMesh(world, chunk);
                            meshes.Add(chunk.chunkPosition, vMesh);
                        }
                    }
                }
        removedChunks.Clear();
        foreach (var kvp in colliders)
        {
            if (!foundChunks.Contains(kvp.Key))
            {
                Destroy(kvp.Value.gameObject);
                removedChunks.Add(kvp.Key);
            }
        }
        for (int i = 0; i < removedChunks.Count; i++)
        {
            colliders.Remove(removedChunks[i]);
        }
    }
}