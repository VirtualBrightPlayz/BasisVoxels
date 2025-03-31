using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class VoxelWorldRenderer : MonoBehaviour
{
    public VoxelWorld world;
    public MeshCollider colliderPrefab;
    private List<VoxelMesh> meshes = new List<VoxelMesh>();
    private List<MeshCollider> colliders = new List<MeshCollider>();
    private List<Chunk> chunks = new List<Chunk>();
    private Queue<VoxelMesh> freeChunks = new Queue<VoxelMesh>();
    private Queue<MeshCollider> freeColliders = new Queue<MeshCollider>();
    private int chunkCount;
    public int renderDistance = 8;
    private Camera cam;
    private Vector3Int lastPosition = Vector3Int.zero;
    private Stopwatch sw = new Stopwatch();

    private void LateUpdate()
    {
        if (cam == null)
        {
            cam = Camera.main;
            return;
        }
        int size = renderDistance * 2 + 1;
        int max = size * size * size;
        Vector3Int camChunkPos = VoxelWorld.FloorPosition(cam.transform.position);
        /*
        if (camChunkPos != lastPosition)
        {
            Vector3Int minPos = camChunkPos - new Vector3Int(renderDistance, renderDistance, renderDistance);
            Vector3Int maxPos = camChunkPos + new Vector3Int(renderDistance, renderDistance, renderDistance);
            HashSet<int> usedPositions = new HashSet<int>();
            for (int i = 0; i < chunkCount; i++)
            {
                if (chunks[i].chunkPosition.x >= minPos.x && chunks[i].chunkPosition.y >= minPos.y && chunks[i].chunkPosition.z >= minPos.z &&
                    chunks[i].chunkPosition.x <= maxPos.x && chunks[i].chunkPosition.y <= maxPos.y && chunks[i].chunkPosition.z <= maxPos.z)
                {
                    usedPositions.Add(chunks[i].chunkPosition.x + chunks[i].chunkPosition.y * size + chunks[i].chunkPosition.z * size * size);
                    continue;
                }
                Destroy(colliders[i].gameObject);
                Destroy(meshes[i].GetMesh());
                meshes.RemoveAt(i);
                colliders.RemoveAt(i);
                chunks.RemoveAt(i);
                i--;
                chunkCount--;
            }
            for (int x = -renderDistance; x <= renderDistance; x++)
                for (int y = -renderDistance; y <= renderDistance; y++)
                    for (int z = -renderDistance; z <= renderDistance; z++)
                    {
                        Vector3Int chunkPos = camChunkPos + new Vector3Int(x, y, z);
                        if (usedPositions.Contains(chunkPos.x + chunkPos.y * size + chunkPos.z * size * size))
                            continue;
                        if (world.TryGetChunk(chunkPos, out Chunk chunk))
                        {
                            chunks.Add(chunk);
                            VoxelMesh mesh = new VoxelMesh(world, chunk);
                            meshes.Add(mesh);
                            MeshCollider collider = Instantiate(colliderPrefab, VoxelWorld.UnroundPosition(chunk.chunkPosition), Quaternion.identity, transform);
                            // collider.sharedMesh = mesh.GetMesh();
                            colliders.Add(collider);
                            mesh.UpdateMesh();
                        }
                    }
            chunkCount = chunks.Count;
        }
        for (int i = 0; i < chunkCount; i++)
        {
            meshes[i].Draw();
            if (chunks[i].shouldUpdate > 0)
                meshes[i].UpdateMesh();
            // colliders[i].sharedMesh = meshes[i].GetMesh();
        }
        */
        if (camChunkPos != lastPosition)
        {
            Vector3Int minPos = camChunkPos - new Vector3Int(renderDistance, renderDistance, renderDistance);
            Vector3Int maxPos = camChunkPos + new Vector3Int(renderDistance, renderDistance, renderDistance);
            HashSet<int> usedPositions = new HashSet<int>();
            for (int i = 0; i < chunkCount; i++)
            {
                if (chunks[i].chunkPosition.x >= minPos.x && chunks[i].chunkPosition.y >= minPos.y && chunks[i].chunkPosition.z >= minPos.z &&
                    chunks[i].chunkPosition.x <= maxPos.x && chunks[i].chunkPosition.y <= maxPos.y && chunks[i].chunkPosition.z <= maxPos.z)
                {
                    usedPositions.Add(chunks[i].chunkPosition.x + chunks[i].chunkPosition.y * size + chunks[i].chunkPosition.z * size * size);
                    continue;
                }
                freeChunks.Enqueue(meshes[i]);
                freeColliders.Enqueue(colliders[i]);
                meshes.RemoveAt(i);
                colliders.RemoveAt(i);
                chunks.RemoveAt(i);
                i--;
                chunkCount--;
            }
            for (int x = -renderDistance; x <= renderDistance; x++)
                for (int y = -renderDistance; y <= renderDistance; y++)
                    for (int z = -renderDistance; z <= renderDistance; z++)
                    {
                        Vector3Int chunkPos = camChunkPos + new Vector3Int(x, y, z);
                        if (usedPositions.Contains(chunkPos.x + chunkPos.y * size + chunkPos.z * size * size))
                            continue;
                        if (world.TryGetChunk(chunkPos, out Chunk chunk))
                        {
                            chunks.Add(chunk);
                            VoxelMesh mesh;
                            if (freeChunks.Count == 0)
                                mesh = new VoxelMesh(world, chunk);
                            else
                            {
                                mesh = freeChunks.Dequeue();
                                mesh.chunk = chunk;
                            }
                            MeshCollider collider;
                            if (freeColliders.Count == 0)
                                collider = Instantiate(colliderPrefab, VoxelWorld.UnroundPosition(chunk.chunkPosition), Quaternion.identity, transform);
                            else
                            {
                                collider = freeColliders.Dequeue();
                                collider.transform.position = VoxelWorld.UnroundPosition(chunk.chunkPosition);
                            }
                            if (chunk.shouldUpdate == 0)
                                chunk.QueueUpdateMesh();
                            mesh.IsValid = false;
                            // mesh.UpdateMesh();
                            // collider.sharedMesh = mesh.GetMesh();
                            collider.sharedMesh = null;
                            colliders.Add(collider);
                            meshes.Add(mesh);
                        }
                    }
            chunkCount = chunks.Count;
        }
        sw.Restart();
        for (int i = 0; i < chunkCount; i++)
        {
            meshes[i].Draw();
            if (chunks[i].shouldUpdate > 0 && sw.ElapsedMilliseconds <= 5)
            {
                meshes[i].UpdateMesh();
                colliders[i].sharedMesh = meshes[i].GetMesh();
            }
        }
        lastPosition = camChunkPos;
    }
}