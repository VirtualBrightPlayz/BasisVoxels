using UnityEngine;

[CreateAssetMenu]
public class BiomeAsset : ScriptableObject
{
    [Range(-1f, 1f)]
    public float biomePosition;
    public VoxelType surface;
}