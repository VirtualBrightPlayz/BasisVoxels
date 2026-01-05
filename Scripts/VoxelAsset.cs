using UnityEngine;

public partial class VoxelAsset : ScriptableObject
{
    public Material material;
    public bool lit = false;
    public Color32 emission = new Color32(255, 255, 255, 16);
    public byte layer = 0;
    public bool sand = false;
}