// Structures/StructurePreview.cs: small pictures of a structure, to see what is about to be placed.
// Owns: StructurePreview — Top (looking down: the highest block of each column, lit by its height) and Front (looking
// north from the south side: the nearest block of each column, darker the further back), both in the map's block
// colours with plants and leaves in a plain green, one pixel per block. Nothing of the game is drawn: only colours.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class StructurePreview
{
    /// <summary>Looking down on the structure: x across, z down.</summary>
    public static RgbaImage Top(Structure s)
    {
        var img = new RgbaImage(s.SizeX, s.SizeZ);
        var colors = new Dictionary<BlockState, uint>();
        for (int z = 0; z < s.SizeZ; z++)
            for (int x = 0; x < s.SizeX; x++)
                for (int y = s.SizeY - 1; y >= 0; y--)
                {
                    var b = s.Get(x, y, z);
                    if (Color(b, colors) is not uint c) continue;
                    // Higher is lighter, so the shape of the building shows.
                    double f = s.SizeY <= 1 ? 1 : 0.65 + 0.45 * y / (s.SizeY - 1);
                    img.Pixels[z * s.SizeX + x] = MapRenderer.Scale(c, f);
                    break;
                }
        return img;
    }

    /// <summary>Looking at the south side of the structure: x across, y up.</summary>
    public static RgbaImage Front(Structure s)
    {
        var img = new RgbaImage(s.SizeX, s.SizeY);
        var colors = new Dictionary<BlockState, uint>();
        for (int y = 0; y < s.SizeY; y++)
            for (int x = 0; x < s.SizeX; x++)
                for (int z = s.SizeZ - 1; z >= 0; z--)
                {
                    var b = s.Get(x, y, z);
                    if (Color(b, colors) is not uint c) continue;
                    double f = s.SizeZ <= 1 ? 1 : 1.0 - 0.4 * (s.SizeZ - 1 - z) / (s.SizeZ - 1);
                    img.Pixels[(s.SizeY - 1 - y) * s.SizeX + x] = MapRenderer.Scale(c, f);
                    break;
                }
        return img;
    }

    // The block's map colour (null for air and see-through blocks), tinted like a plains biome.
    static uint? Color(BlockState b, Dictionary<BlockState, uint> cache)
    {
        if (b.IsAir) return null;
        if (!cache.TryGetValue(b, out uint c))
        {
            var (color, tint) = BlockColors.Lookup(b);
            var t = Biomes.Default;
            c = tint switch
            {
                TintKind.Grass => 0xFF000000u | t.Grass,
                TintKind.Foliage => 0xFF000000u | t.Foliage,
                TintKind.Water => 0xFF000000u | t.Water,
                _ => color,
            };
            cache[b] = c;
        }
        return (c >> 24) == 0 ? null : c | 0xFF000000u;
    }
}
