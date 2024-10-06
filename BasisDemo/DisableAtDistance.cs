using Basis.Scripts.Drivers;
using UnityEngine;

public class DisableAtDistance : MonoBehaviour
{
    public float distance;
    public VoxelMesh mesh;

    public void Update()
    {
        if (BasisLocalCameraDriver.Instance == null)
            return;
        Camera cam = BasisLocalCameraDriver.Instance.Camera;
        if (cam != null && mesh != null && mesh.chunk != null)
        {
            Vector3Int camPos = VoxelWorld.FloorPosition(cam.transform.position);
            Vector3Int chunkPos = mesh.chunk.chunkPosition;
            camPos.y = 0;
            chunkPos.y = 0;
            // mesh.meshCollider.enabled = mesh.meshRenderer.enabled = (camPos - chunkPos).sqrMagnitude <= distance * distance;
        }
    }
}
