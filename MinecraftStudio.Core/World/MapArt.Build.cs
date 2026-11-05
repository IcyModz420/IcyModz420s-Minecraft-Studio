// MinecraftStudio.Core/World/MapArt.Build.cs: map art built in survival — the blocks and the staircase for a picture.
// Part of MapArt (main part in MapArt.cs). Owns: BuildableColors (the map colours a version can show with blocks a player
// can place — one block per base colour: concrete, terracotta, planks, wool … — in the shades height gives: lower than the
// block to the north is dark, level is normal, higher is light; the fourth, darkest shade can't be built, and water's
// shade comes from its depth, so water is left out), BuildBlock, and Building (one map's 128 × 128 colours as a structure:
// each column stepped up for a light shade and down to its lowest step for a dark one, from a reference row along the
// north edge, the lowest block at y 0). Made with Make()
// on a buildable palette; saved with StructureFile; counted with StructureMaterials.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

public static partial class MapArt
{
    /// <summary>A block whose top shows a map colour, from a data version on (and what an older version uses instead).</summary>
    sealed record Buildable(string Color, string Block, int Since = 0, string? Before = null);

    // Solid blocks that don't fall, burn away or change by themselves; cheap where there is a choice (Java names).
    static readonly Buildable[] Blocks =
    [
        new("GRASS", "minecraft:grass_block"), new("SAND", "minecraft:sandstone"), new("WOOL", "minecraft:mushroom_stem", 1519),
        new("FIRE", "minecraft:redstone_block"), new("ICE", "minecraft:packed_ice"), new("METAL", "minecraft:iron_block"),
        new("PLANT", "minecraft:oak_leaves[persistent=true]"), new("SNOW", "minecraft:white_concrete", 1139, "minecraft:white_wool"),
        new("CLAY", "minecraft:clay"), new("DIRT", "minecraft:jungle_planks"), new("STONE", "minecraft:cobblestone"),
        new("WOOD", "minecraft:oak_planks"), new("QUARTZ", "minecraft:diorite", 100, "minecraft:quartz_block"),
        new("COLOR_ORANGE", "minecraft:orange_concrete", 1139, "minecraft:orange_wool"), new("COLOR_MAGENTA", "minecraft:magenta_concrete", 1139, "minecraft:magenta_wool"),
        new("COLOR_LIGHT_BLUE", "minecraft:light_blue_concrete", 1139, "minecraft:light_blue_wool"), new("COLOR_YELLOW", "minecraft:yellow_concrete", 1139, "minecraft:yellow_wool"),
        new("COLOR_LIGHT_GREEN", "minecraft:lime_concrete", 1139, "minecraft:lime_wool"), new("COLOR_PINK", "minecraft:pink_concrete", 1139, "minecraft:pink_wool"),
        new("COLOR_GRAY", "minecraft:gray_concrete", 1139, "minecraft:gray_wool"), new("COLOR_LIGHT_GRAY", "minecraft:light_gray_concrete", 1139, "minecraft:light_gray_wool"),
        new("COLOR_CYAN", "minecraft:cyan_concrete", 1139, "minecraft:cyan_wool"), new("COLOR_PURPLE", "minecraft:purple_concrete", 1139, "minecraft:purple_wool"),
        new("COLOR_BLUE", "minecraft:blue_concrete", 1139, "minecraft:blue_wool"), new("COLOR_BROWN", "minecraft:brown_concrete", 1139, "minecraft:brown_wool"),
        new("COLOR_GREEN", "minecraft:green_concrete", 1139, "minecraft:green_wool"), new("COLOR_RED", "minecraft:red_concrete", 1139, "minecraft:red_wool"),
        new("COLOR_BLACK", "minecraft:black_concrete", 1139, "minecraft:black_wool"),
        new("GOLD", "minecraft:gold_block"), new("DIAMOND", "minecraft:prismarine_bricks", 100, "minecraft:diamond_block"), new("LAPIS", "minecraft:lapis_block"),
        new("EMERALD", "minecraft:emerald_block"), new("PODZOL", "minecraft:spruce_planks", 100), new("NETHER", "minecraft:netherrack", 100),
        new("TERRACOTTA_WHITE", "minecraft:white_terracotta", 1139), new("TERRACOTTA_ORANGE", "minecraft:orange_terracotta", 1139),
        new("TERRACOTTA_MAGENTA", "minecraft:magenta_terracotta", 1139), new("TERRACOTTA_LIGHT_BLUE", "minecraft:light_blue_terracotta", 1139),
        new("TERRACOTTA_YELLOW", "minecraft:yellow_terracotta", 1139), new("TERRACOTTA_LIGHT_GREEN", "minecraft:lime_terracotta", 1139),
        new("TERRACOTTA_PINK", "minecraft:pink_terracotta", 1139), new("TERRACOTTA_GRAY", "minecraft:gray_terracotta", 1139),
        new("TERRACOTTA_LIGHT_GRAY", "minecraft:light_gray_terracotta", 1139), new("TERRACOTTA_CYAN", "minecraft:cyan_terracotta", 1139),
        new("TERRACOTTA_PURPLE", "minecraft:purple_terracotta", 1139), new("TERRACOTTA_BLUE", "minecraft:blue_terracotta", 1139),
        new("TERRACOTTA_BROWN", "minecraft:brown_terracotta", 1139), new("TERRACOTTA_GREEN", "minecraft:green_terracotta", 1139),
        new("TERRACOTTA_RED", "minecraft:red_terracotta", 1139), new("TERRACOTTA_BLACK", "minecraft:black_terracotta", 1139),
        new("CRIMSON_NYLIUM", "minecraft:crimson_nylium", 2566), new("CRIMSON_STEM", "minecraft:crimson_planks", 2566),
        new("CRIMSON_HYPHAE", "minecraft:crimson_hyphae[axis=y]", 2566), new("WARPED_NYLIUM", "minecraft:warped_nylium", 2566),
        new("WARPED_STEM", "minecraft:warped_planks", 2566), new("WARPED_HYPHAE", "minecraft:warped_hyphae[axis=y]", 2566),
        new("WARPED_WART_BLOCK", "minecraft:warped_wart_block", 2566), new("DEEPSLATE", "minecraft:cobbled_deepslate", 2724),
        new("RAW_IRON", "minecraft:raw_iron_block", 2724), new("GLOW_LICHEN", "minecraft:verdant_froglight[axis=y]", 3105),
    ];

    /// <summary>The block along the north edge whose height sets the first row's shade (it lies outside the map).</summary>
    public static readonly BlockState ReferenceBlock = BlockState.Get("minecraft:cobblestone");

    /// <summary>The block that shows a base colour in a version (null: none can be built there).</summary>
    public static BlockState? BuildBlock(int baseColor, int dataVersion)
    {
        foreach (var b in Blocks)
        {
            if (Maps.BaseId(b.Color) != baseColor) continue;
            string? name = dataVersion >= b.Since ? b.Block : b.Before;
            return name == null ? null : BlockState.Parse(name);
        }
        return null;
    }

    /// <summary>
    /// The map colours (index, RGB) a version can show with placed blocks: the normal shade only (<paramref name="staircase"/>
    /// false: one flat layer), or also the dark and light shades the steps give.
    /// </summary>
    public static List<(byte Color, int Rgb)> BuildableColors(int dataVersion, bool staircase)
    {
        var list = new List<(byte, int)>();
        foreach (var b in Blocks)
        {
            if (Maps.BaseId(b.Color) is not int id || BuildBlock(id, dataVersion) == null) continue;
            foreach (int shade in staircase ? new[] { 0, 1, 2 } : [1])
                if (Maps.Rgb((byte)(id * 4 + shade)) is int rgb and >= 0) list.Add(((byte)(id * 4 + shade), rgb));
        }
        return list;
    }

    /// <summary>
    /// One map's colours as the structure to build: 128 blocks across, 129 deep (z 0 is the reference row just north of
    /// the map, the picture's rows are z 1 to 128), each block one higher than the one north of it for the light shade,
    /// lower for the dark one (down to the column's lowest step); each column's lowest block at y 0. See-through pixels stay empty.
    /// </summary>
    public static Structure Building(CanonicalMap map, int dataVersion)
    {
        var heights = new int[Size, Size + 1];
        int top = 0;
        for (int x = 0; x < Size; x++)
        {
            // Light: one above the block to the north. Dark: only lower than it, so down to the lowest step so far (the
            // column stays as low as its longest run of light shades allows). Normal: level with it.
            int h = 0, low = 0;
            for (int z = 0; z < Size; z++)
            {
                byte c = map.Colors[z * Size + x];
                int shade = c & 3;
                if (c >= 4 && shade == 2) h++;
                else if (c >= 4 && shade == 0) h = h > low ? low : h - 1;
                heights[x, z + 1] = h;
                low = Math.Min(low, h);
            }
            for (int z = 0; z <= Size; z++) { heights[x, z] -= low; top = Math.Max(top, heights[x, z]); }
        }
        var s = new Structure(Size, top + 1, Size + 1) { DataVersion = dataVersion };
        var blocks = new Dictionary<int, BlockState?>();
        for (int x = 0; x < Size; x++)
        {
            s.Set(x, heights[x, 0], 0, ReferenceBlock);
            for (int z = 0; z < Size; z++)
            {
                byte c = map.Colors[z * Size + x];
                if (c < 4) continue;
                int id = c >> 2;
                if (!blocks.TryGetValue(id, out var block)) blocks[id] = block = BuildBlock(id, dataVersion);
                if (block != null) s.Set(x, heights[x, z + 1], z + 1, block);
            }
        }
        return s;
    }

    /// <summary>A picture as maps in colours that can be built (see <see cref="BuildableColors"/>).</summary>
    public static List<CanonicalMap> MakeBuildable(Imaging.RgbaImage picture, int across, int down, Imaging.PictureOptions options, int dataVersion, bool staircase, bool dither) =>
        Make(picture, across, down, options, BuildableColors(dataVersion, staircase), dither);
}
