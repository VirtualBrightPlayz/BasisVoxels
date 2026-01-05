using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public partial class VoxelWorldRenderer : MonoBehaviour
{
    public VoxelWorld world;
    private List<VoxelMesh> meshes = new List<VoxelMesh>();
    private List<MeshRenderer> instances = new List<MeshRenderer>();
    private List<MeshCollider> colliders = new List<MeshCollider>();
    private List<Chunk> chunks = new List<Chunk>();
    private System.Threading.Mutex meshingMutex = new System.Threading.Mutex();
    private List<(VoxelMesh, Chunk, int)> meshesToMesh = new List<(VoxelMesh, Chunk, int)>();
    private ConcurrentQueue<(VoxelMesh, Chunk, int)> meshesToUpdate = new ConcurrentQueue<(VoxelMesh, Chunk, int)>();

    public int renderDistance = 8;
    public Camera cam;
    public Vector3Int lastPosition = Vector3Int.one * 100;
    private List<Thread> threads = new List<Thread>();
    private bool isRunning = false;

    public void Start()
    {
        for (int x = -renderDistance; x <= renderDistance; x++)
            for (int y = -renderDistance; y <= renderDistance; y++)
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    chunks.Add(null);
                    VoxelMesh mesh = new VoxelMesh(world);
                    meshes.Add(mesh);
                    GameObject inst = new GameObject("Chunk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                    inst.transform.parent = transform;
                    instances.Add(inst.GetComponent<MeshRenderer>());
                    colliders.Add(inst.GetComponent<MeshCollider>());
                    inst.GetComponent<MeshRenderer>().enabled = false;
                    inst.GetComponent<MeshCollider>().enabled = false;
                }

        isRunning = true;
        for (int i = 0; i < 1; i++)
        {
            Thread thread = new Thread(MeshingThread);
            threads.Add(thread);
            thread.Start();
        }
    }

    public void OnDestroy()
    {
        isRunning = false;
        for (int i = 0; i < threads.Count; i++)
        {
            threads[i]?.Join();
        }
    }

    public void MeshingThread()
    {
        // return;
        while (isRunning)
        {
            if (meshingMutex.WaitOne())
            {
                try
                {
                    // while (meshesToMesh.Count != 0)
                    {
                        TickMeshing();
                    }
                    // TickMeshing();
                    Thread.Sleep(1);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    meshingMutex.ReleaseMutex();
                }
            }
            Thread.Yield();
        }
    }

    private void TickMeshing()
    {
        int smallest = -1;
        float dist = float.PositiveInfinity;
        for (int i = 0; i < meshesToMesh.Count; i++)
        {
            float d = (meshesToMesh[i].Item2.chunkPosition - lastPosition).sqrMagnitude;
            if (dist > d)
            {
                smallest = i;
                dist = d;
            }
        }
        if (smallest != -1)
        {
            meshesToMesh[smallest].Item1.UpdateMesh(meshesToMesh[smallest].Item2);
            meshesToUpdate.Enqueue((meshesToMesh[smallest].Item1, meshesToMesh[smallest].Item2, meshesToMesh[smallest].Item3));
            meshesToMesh.RemoveAt(smallest);
            // Debug.Log($"smallest={smallest}");
        }
    }

    public void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
            return;
        }
        int size = renderDistance * 2 + 1;
        int max = size * size * size;
        Vector3Int camChunkPos = VoxelWorld.FloorPosition(Vector3Int.FloorToInt(cam.transform.position));
        if (camChunkPos != lastPosition)
        {
            Vector3Int minPos = camChunkPos - new Vector3Int(renderDistance, renderDistance, renderDistance);
            Vector3Int maxPos = camChunkPos + new Vector3Int(renderDistance, renderDistance, renderDistance);
            List<Vector3Int> usedPositions = new List<Vector3Int>();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i] == null)
                    continue;
                if (chunks[i].chunkPosition.x >= minPos.x && chunks[i].chunkPosition.y >= minPos.y && chunks[i].chunkPosition.z >= minPos.z &&
                    chunks[i].chunkPosition.x <= maxPos.x && chunks[i].chunkPosition.y <= maxPos.y && chunks[i].chunkPosition.z <= maxPos.z)
                {
                    usedPositions.Add(chunks[i].chunkPosition);
                    continue;
                }
                chunks[i] = null;
                // instances[i].transform.localPosition = Vector3.down * 100f;
                instances[i].enabled = false;
                colliders[i].enabled = false;
            }
            for (int x = -renderDistance; x <= renderDistance; x++)
                for (int y = -renderDistance; y <= renderDistance; y++)
                    for (int z = -renderDistance; z <= renderDistance; z++)
                    {
                        Vector3Int chunkPos = camChunkPos + new Vector3Int(x, y, z);
                        if (world.TryGetChunk(chunkPos, out Chunk chunk))
                        {
                            if (chunks.Contains(chunk))
                                continue;
                            // find null chunk to replace and update
                            for (int i = 0; i < chunks.Count; i++)
                            {
                                if (chunks[i] == null && !meshes[i].hasArray)
                                {
                                    chunks[i] = chunk;
                                    // instances[i].enabled = false;
                                    // colliders[i].enabled = false;
                                    // instances[i].transform.localPosition = VoxelWorld.UnroundPosition(chunks[i].chunkPosition);
                                    chunk.QueueUpdateMesh();
                                    break;
                                }
                            }
                        }
                    }
        }
        for (int i = 0; i < meshes.Count; i++)
        {
            if (chunks[i] != null)
            {
                // instances[i].transform.localPosition = VoxelWorld.UnroundPosition(chunks[i].chunkPosition);
            }
            else
            {
                instances[i].enabled = false;
                colliders[i].enabled = false;
            }
        }
        if (meshingMutex.WaitOne())
        {
            try
            {
                for (int i = 0; i < meshes.Count; i++)
                {
                    if (chunks[i] != null && chunks[i].shouldUpdate > 0)
                    {
                        meshes[i].AllocMesh();
                        meshesToMesh.Add((meshes[i], chunks[i], i));
                        chunks[i].shouldUpdate = 0;//Mathf.Max(chunks[i].shouldUpdate - 1, 0);
                    }
                }
            }
            finally
            {
                meshingMutex.ReleaseMutex();
            }
        }
        while (meshesToUpdate.TryDequeue(out var data))
        {
            VoxelMesh vmesh = data.Item1;
            int i = meshes.IndexOf(vmesh);
            int j = chunks.IndexOf(data.Item2);
            i = j;
            if (i != -1)
            {
                vmesh.ApplyMesh();
                Mesh mes = vmesh.GetMesh();
                if (mes == null)
                {
                    instances[i].enabled = false;
                    colliders[i].enabled = false;
                    continue;
                }
                instances[i].enabled = true;
                instances[i].sharedMaterials = vmesh.GetMaterials();
                instances[i].GetComponent<MeshFilter>().sharedMesh = mes;
                instances[i].transform.localPosition = VoxelWorld.UnroundPosition(data.Item2.chunkPosition);
                colliders[i].enabled = true;
                colliders[i].sharedMesh = mes;
                // colliders[i].transform.localPosition = VoxelWorld.UnroundPosition(chunks[i].chunkPosition);
            }
        }
        lastPosition = camChunkPos;
    }
}