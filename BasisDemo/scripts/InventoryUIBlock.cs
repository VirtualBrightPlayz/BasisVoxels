using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUIBlock : MonoBehaviour
{
    public Button button;
    public TMP_Text text;
    public VoxelType block;
    public BasisDemoVoxels world;

    public void SetBlock(VoxelType voxel)
    {
        block = voxel;
        name = block.name;
        text.text = block.name;
        gameObject.SetActive(true);
    }

    public void Selected()
    {
        switch (world.GameMode)
        {
            case CreativeMode creative:
                creative.placeBlockId = (byte)world.types.IndexOf(block);
                break;
            // case LimitedMode limited:
            //     limited.placeBlockId = (byte)world.types.IndexOf(block);
            //     break;
        }
    }
}