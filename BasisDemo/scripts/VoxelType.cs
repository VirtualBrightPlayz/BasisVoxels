using UnityEngine;

[CreateAssetMenu]
public class VoxelType : ScriptableObject
{
    public Material material;
    public string htmlColor;
    public bool lit = false;
    public Color32 litColor = new Color32(255, 255, 255, 16);
    public bool showInMenu = true;
    public bool sand = false;
    public bool liquid = false;
}