using UnityEngine;

public class DisableAtDistance : MonoBehaviour
{
    public float distance;
    public VoxelMesh mesh;

    public void Update()
    {
        Camera cam = Camera.main;
        if (cam != null && mesh != null && mesh.chunk != null)
        {
            Vector3Int camPos = VoxelWorld.FloorPosition(cam.transform.position);
            Vector3Int chunkPos = mesh.chunk.chunkPosition;
            camPos.y = 0;
            chunkPos.y = 0;
            mesh.meshRenderer.enabled = (camPos - chunkPos).sqrMagnitude <= distance * distance;
        }
    }
}
