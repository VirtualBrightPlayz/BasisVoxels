using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class TxtVoxelFile
{
    public List<Color> colorLookup = new List<Color>();

    public TxtVoxelFile()
    {
    }

    public TxtVoxelFile(string[] htmlColors)
    {
        colorLookup.AddRange(htmlColors.Select(x => ColorUtility.TryParseHtmlString('#' + x, out Color color) ? color : Color.white));
    }

    public void Read(string content, Vector3Int offset, VoxelWorld world)
    {
        string[] lines = content.Replace("\r", "").Split("\n");
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#'))
                continue;
            string[] values = lines[i].Split(' ');
            if (values.Length > 3)
            {
                int x = int.Parse(values[0]);
                int y = int.Parse(values[1]);
                int z = int.Parse(values[2]);
                string hex = values[3];
                if (ColorUtility.TryParseHtmlString('#' + hex, out Color color))
                {
                    int id = colorLookup.IndexOf(color);
                    if (id == -1)
                        id = 0;
                    // swap y and z
                    if (world.TryGetVoxel(offset.x + x, offset.y + z, offset.z + y, out Voxel vox))
                    {
                        vox.Id = (byte)id;
                        world.SetVoxelWithData(new Vector3Int(offset.x + x, offset.y + z, offset.z + y), vox);
                    }
                }
            }
        }
    }

    public string Write(Vector3Int start, Vector3Int end, VoxelWorld world)
    {
        StringBuilder sb = new StringBuilder();
        for (int x = start.x; x < end.x; x++)
        {
            for (int y = start.y; y < end.y; y++)
            {
                for (int z = start.z; z < end.z; z++)
                {
                    if (world.TryGetVoxel(x, z, y, out Voxel vox) && vox.IsActive)
                    {
                        Color color = colorLookup[vox.Id];
                        string hex = ColorUtility.ToHtmlStringRGB(color).ToLower();
                        // swap y and z
                        sb.AppendLine($"{x} {z} {y} {hex}");
                    }
                }
            }
        }
        return sb.ToString();
    }
}