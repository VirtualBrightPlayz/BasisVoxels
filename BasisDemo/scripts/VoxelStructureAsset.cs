using UnityEngine;

[CreateAssetMenu]
public class VoxelStructureAsset : ScriptableObject
{
    public BiomeAsset biome;
    public int density = 8;
    public TextAsset text;
    [HideInInspector]
    public string contents;

    private void OnEnable()
    {
        contents = text.text;
    }
}