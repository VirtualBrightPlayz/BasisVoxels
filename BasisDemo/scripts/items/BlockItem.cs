using UnityEngine;

[CreateAssetMenu]
public class BlockItem : ItemData
{
    public VoxelType voxel;

    public override void InitWorldItem(Item item)
    {
        MeshFilter filter = item.GetComponent<MeshFilter>();
        MeshRenderer render = item.GetComponent<MeshRenderer>();
        render.sharedMaterial = voxel.material;
        // filter.sharedMesh = 
    }
}