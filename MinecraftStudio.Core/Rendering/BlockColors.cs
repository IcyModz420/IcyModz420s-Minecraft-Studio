// MinecraftStudio.Core/Rendering/BlockColors.cs: per-block map/render colours and biome tints.
// Owns: TintKind, BlockColors.
using System.Globalization;
using System.Reflection;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Rendering;

public enum TintKind : byte { None, Grass, Foliage, Water, Fixed }

/// <summary>Top-down colours for blocks (embedded block_colors.csv plus name-based fallbacks).</summary>
public static class BlockColors
{
    // Lazy: the built-in arrays below are initialised after this field in declaration order.
    static readonly Lazy<Dictionary<string, (uint Color, TintKind Tint)>> TableLazy = new(Load);
    static Dictionary<string, (uint Color, TintKind Tint)> Table => TableLazy.Value;

    static Dictionary<string, (uint, TintKind)> Load()
    {
        var map = new Dictionary<string, (uint, TintKind)>(StringComparer.Ordinal);
        foreach (var (name, rgb, tint) in BuiltIn) map[name] = (0xFF000000u | rgb, tint);
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftStudio.Data.block_colors.csv");
        if (s != null)
        {
            using var r = new StreamReader(s);
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(',');
                if (p.Length < 4) continue;
                string name = p[0].Trim();
                if (name.StartsWith("minecraft:", StringComparison.Ordinal)) name = name[10..];
                if (!byte.TryParse(p[1], out byte rr) || !byte.TryParse(p[2], out byte gg) || !byte.TryParse(p[3], out byte bb)) continue;
                var tint = TintKind.None;
                if (p.Length > 4)
                {
                    string t = p[4].Trim().ToLowerInvariant();
                    tint = t switch { "grass" => TintKind.Grass, "foliage" or "lily_pad" or "dry_foliage" => TintKind.Foliage, "water" => TintKind.Water, _ => TintKind.None };
                }
                // Hand-picked texture colours above win; the table (game map colours) fills in everything else.
                map.TryAdd(name, (0xFF000000u | ((uint)rr << 16) | ((uint)gg << 8) | bb, tint));
            }
        }
        return map;
    }

    public static (uint Color, TintKind Tint) Lookup(BlockState state)
    {
        // Bedrock names (wool+color, log+old_log_type, ...) resolve to the Java-style name first.
        string alias = BlockNames.JavaBaseName(state);
        if (Table.TryGetValue(alias, out var c)) return c;
        if (Table.TryGetValue(state.BaseName, out c)) return c;
        if (JavaEquivalent(state) is string java && Table.TryGetValue(java, out c)) return c;
        return Guess(alias);
    }

    /// <summary>Java base name of a Bedrock-only block name through the conversion tables (null for Java names).</summary>
    static string? JavaEquivalent(BlockState state)
    {
        if (!state.Name.StartsWith("minecraft:", StringComparison.Ordinal) || !Conversion.BedrockBlocks.IsKnownBlock(state.Name)) return null;
        var java = Conversion.BedrockJavaMap.ToJava(state);
        return java != null && java.Name != state.Name ? java.BaseName : null;
    }

    static (uint, TintKind) Guess(string n)
    {
        foreach (var (color, hex) in DyeColors)
            if (n.StartsWith(color + "_", StringComparison.Ordinal))
            {
                uint rgb = hex;
                if (n.EndsWith("terracotta", StringComparison.Ordinal) && !n.Contains("glazed")) rgb = Mix(rgb, 0x985E43, 0.55);
                if (n.Contains("glass")) rgb = Mix(rgb, 0xFFFFFF, 0.25);
                return (0xFF000000u | rgb, TintKind.None);
            }
        (string Key, uint Rgb, TintKind Tint)[] rules =
        [
            ("water", 0x3F76E4, TintKind.Water), ("lava", 0xD96415, TintKind.None), ("leaves", 0x4A7C2E, TintKind.Foliage),
            ("grass", 0x7CBD6B, TintKind.Grass), ("fern", 0x6FA65A, TintKind.Grass), ("vine", 0x4A7C2E, TintKind.Foliage),
            ("crimson", 0x8C2F3B, TintKind.None), ("warped", 0x2B6963, TintKind.None), ("mangrove", 0x763531, TintKind.None),
            ("cherry", 0xE3B4AD, TintKind.None), ("bamboo", 0xC2AE5A, TintKind.None), ("birch", 0xC8B77A, TintKind.None),
            ("spruce", 0x6B5030, TintKind.None), ("jungle", 0xA17552, TintKind.None), ("acacia", 0xA85A32, TintKind.None),
            ("dark_oak", 0x4A3018, TintKind.None), ("pale_oak", 0xE5DCD6, TintKind.None), ("oak", 0xA2834F, TintKind.None),
            ("deepslate", 0x505053, TintKind.None), ("blackstone", 0x2A2328, TintKind.None), ("basalt", 0x5A5A5E, TintKind.None),
            ("copper", 0xC06E50, TintKind.None), ("tuff", 0x6C6D66, TintKind.None), ("calcite", 0xDFE0DC, TintKind.None),
            ("amethyst", 0x8562BE, TintKind.None), ("prismarine", 0x5DA89A, TintKind.None), ("purpur", 0xA97DA9, TintKind.None),
            ("end_stone", 0xDBDE9E, TintKind.None), ("nether", 0x612A2A, TintKind.None), ("quartz", 0xECE6DF, TintKind.None),
            ("sandstone", 0xD8CB9B, TintKind.None), ("red_sand", 0xBE6621, TintKind.None), ("sand", 0xDBCFA3, TintKind.None),
            ("mud", 0x3C393D, TintKind.None), ("snow", 0xF9FEFE, TintKind.None), ("ice", 0x91B4FE, TintKind.None),
            ("coral", 0xD05C8A, TintKind.None), ("mushroom", 0xA0816B, TintKind.None), ("moss", 0x596E2D, TintKind.None),
            ("brick", 0x966253, TintKind.None), ("granite", 0x9A6B58, TintKind.None), ("diorite", 0xBDBCBD, TintKind.None),
            ("andesite", 0x888889, TintKind.None), ("cobble", 0x7F7F7F, TintKind.None), ("stone", 0x7D7D7D, TintKind.None),
            ("ore", 0x7D7D7D, TintKind.None), ("log", 0x6B5236, TintKind.None), ("wood", 0x6B5236, TintKind.None),
            ("planks", 0xA2834F, TintKind.None), ("dirt", 0x866043, TintKind.None), ("glass", 0xC0DCDF, TintKind.None),
            ("rail", 0x7D6E58, TintKind.None), ("flower", 0xD0C040, TintKind.None), ("tulip", 0xD05050, TintKind.None),
            ("sapling", 0x4E8A2A, TintKind.None), ("carpet", 0xD0D0D0, TintKind.None), ("wool", 0xE9ECEC, TintKind.None),
            ("slab", 0x9A9A9A, TintKind.None), ("stairs", 0x9A9A9A, TintKind.None), ("wall", 0x7F7F7F, TintKind.None),
            ("fence", 0xA2834F, TintKind.None), ("door", 0x9C7E4E, TintKind.None), ("bed", 0xA02722, TintKind.None),
        ];
        foreach (var r in rules) if (n.Contains(r.Key, StringComparison.Ordinal)) return (0xFF000000u | r.Rgb, r.Tint);
        if (n.StartsWith("potted_", StringComparison.Ordinal)) return (0xFF7C4536u, TintKind.None);
        if (n.EndsWith("_head", StringComparison.Ordinal) || n.EndsWith("_skull", StringComparison.Ordinal)) return (0xFF9A9A9Au, TintKind.None);
        return (0xFF000000u | HashColor(n), TintKind.None);
    }

    /// <summary>Stable muted colour for blocks with no known colour (modded or brand-new blocks).</summary>
    public static uint HashColor(string name)
    {
        uint h = 2166136261;
        foreach (char ch in name) { h ^= ch; h *= 16777619; }
        double hue = (h % 360) / 60.0, s = 0.35, v = 0.75;
        int i = (int)hue;
        double f = hue - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return ((uint)(r * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(b * 255);
    }

    /// <summary>True when the block has no colour of its own (rendered with a generated colour).</summary>
    public static bool IsGuessed(BlockState state) =>
        !Table.ContainsKey(BlockNames.JavaBaseName(state)) && !Table.ContainsKey(state.BaseName)
        && !(JavaEquivalent(state) is string java && Table.ContainsKey(java));

    static uint Mix(uint a, uint b, double t)
    {
        int r = (int)(((a >> 16) & 255) * (1 - t) + ((b >> 16) & 255) * t);
        int g = (int)(((a >> 8) & 255) * (1 - t) + ((b >> 8) & 255) * t);
        int bl = (int)((a & 255) * (1 - t) + (b & 255) * t);
        return (uint)((r << 16) | (g << 8) | bl);
    }

    static readonly (string, uint)[] DyeColors =
    [
        ("white", 0xE9ECEC), ("orange", 0xF07613), ("magenta", 0xBD44B3), ("light_blue", 0x3AAFD9), ("yellow", 0xF8C527),
        ("lime", 0x70B919), ("pink", 0xED8DAC), ("gray", 0x3E4447), ("light_gray", 0x8E8E86), ("cyan", 0x158991),
        ("purple", 0x792AAC), ("blue", 0x35399D), ("brown", 0x724728), ("green", 0x546D1B), ("red", 0xA12722), ("black", 0x141519),
    ];

    /// <summary>Hand-picked colours for the most common blocks (texture averages).</summary>
    static readonly (string, uint, TintKind)[] BuiltIn =
    [
        ("stone", 0x7D7D7D, TintKind.None), ("granite", 0x956755, TintKind.None), ("polished_granite", 0x9A6B59, TintKind.None),
        ("diorite", 0xBCBCBC, TintKind.None), ("polished_diorite", 0xC0C1C2, TintKind.None), ("andesite", 0x888888, TintKind.None),
        ("polished_andesite", 0x848685, TintKind.None), ("grass_block", 0x7CBD6B, TintKind.Grass), ("dirt", 0x866043, TintKind.None),
        ("coarse_dirt", 0x775539, TintKind.None), ("podzol", 0x5C3F18, TintKind.None), ("cobblestone", 0x7F7F7F, TintKind.None),
        ("bedrock", 0x555555, TintKind.None), ("water", 0x3F76E4, TintKind.Water), ("flowing_water", 0x3F76E4, TintKind.Water),
        ("lava", 0xD96415, TintKind.None), ("flowing_lava", 0xD96415, TintKind.None), ("sand", 0xDBCFA3, TintKind.None),
        ("red_sand", 0xBE6621, TintKind.None), ("gravel", 0x837F7E, TintKind.None), ("gold_ore", 0x8F8C7D, TintKind.None),
        ("iron_ore", 0x88827F, TintKind.None), ("coal_ore", 0x747474, TintKind.None), ("oak_log", 0x6D5532, TintKind.None),
        ("spruce_log", 0x3B2610, TintKind.None), ("birch_log", 0xD5CDA9, TintKind.None), ("jungle_log", 0x564419, TintKind.None),
        ("acacia_log", 0x676157, TintKind.None), ("dark_oak_log", 0x3C2E1A, TintKind.None), ("oak_leaves", 0x4A7C2E, TintKind.Foliage),
        ("spruce_leaves", 0x3D6340, TintKind.None), ("birch_leaves", 0x5D7F3C, TintKind.None), ("jungle_leaves", 0x4E8A26, TintKind.Foliage),
        ("acacia_leaves", 0x4E7A2A, TintKind.Foliage), ("dark_oak_leaves", 0x3E6A20, TintKind.Foliage), ("mangrove_leaves", 0x4E8A26, TintKind.Foliage),
        ("azalea_leaves", 0x5A7426, TintKind.None), ("flowering_azalea_leaves", 0x657A3C, TintKind.None), ("cherry_leaves", 0xE5AECE, TintKind.None),
        ("pale_oak_leaves", 0x7A8577, TintKind.None), ("glass", 0xC0DCDF, TintKind.None), ("lapis_ore", 0x637290, TintKind.None),
        ("sandstone", 0xD8CB9B, TintKind.None), ("short_grass", 0x7CBD6B, TintKind.Grass), ("grass", 0x7CBD6B, TintKind.Grass),
        ("tall_grass", 0x7CBD6B, TintKind.Grass), ("fern", 0x6FA65A, TintKind.Grass), ("large_fern", 0x6FA65A, TintKind.Grass),
        ("dead_bush", 0x6B4F29, TintKind.None), ("seagrass", 0x2E7A3A, TintKind.None), ("tall_seagrass", 0x2E7A3A, TintKind.None),
        ("kelp", 0x56822E, TintKind.None), ("kelp_plant", 0x56822E, TintKind.None), ("lily_pad", 0x208030, TintKind.Foliage),
        ("vine", 0x4A7C2E, TintKind.Foliage), ("sugar_cane", 0x94C065, TintKind.Grass), ("cactus", 0x5A8A2E, TintKind.None),
        ("pumpkin", 0xC67A1A, TintKind.None), ("carved_pumpkin", 0xC67A1A, TintKind.None), ("melon", 0x6F9A27, TintKind.None),
        ("snow", 0xF9FEFE, TintKind.None), ("snow_block", 0xF9FEFE, TintKind.None), ("powder_snow", 0xF8FDFD, TintKind.None),
        ("ice", 0x91B4FE, TintKind.None), ("packed_ice", 0x8DB4FA, TintKind.None), ("blue_ice", 0x74A8FD, TintKind.None),
        ("clay", 0xA0A6B3, TintKind.None), ("terracotta", 0x985E43, TintKind.None), ("mycelium", 0x6F6265, TintKind.None),
        ("netherrack", 0x612726, TintKind.None), ("soul_sand", 0x513E32, TintKind.None), ("soul_soil", 0x4B3A2E, TintKind.None),
        ("glowstone", 0xABA16B, TintKind.None), ("nether_bricks", 0x2C1519, TintKind.None), ("nether_wart_block", 0x732A2A, TintKind.None),
        ("warped_wart_block", 0x167777, TintKind.None), ("crimson_nylium", 0x822929, TintKind.None), ("warped_nylium", 0x2B7265, TintKind.None),
        ("basalt", 0x515155, TintKind.None), ("blackstone", 0x2A2328, TintKind.None), ("magma_block", 0x8E3F1F, TintKind.None),
        ("end_stone", 0xDBDE9E, TintKind.None), ("obsidian", 0x0F0B19, TintKind.None), ("crying_obsidian", 0x200A3C, TintKind.None),
        ("oak_planks", 0xA2834F, TintKind.None), ("spruce_planks", 0x735531, TintKind.None), ("birch_planks", 0xC0AF79, TintKind.None),
        ("jungle_planks", 0xA07351, TintKind.None), ("acacia_planks", 0xA85A32, TintKind.None), ("dark_oak_planks", 0x432B14, TintKind.None),
        ("bricks", 0x966253, TintKind.None), ("bookshelf", 0x755F3C, TintKind.None), ("mossy_cobblestone", 0x6E775F, TintKind.None),
        ("torch", 0xFFD84F, TintKind.None), ("wall_torch", 0xFFD84F, TintKind.None), ("chest", 0xA3772E, TintKind.None),
        ("crafting_table", 0x825F3A, TintKind.None), ("furnace", 0x6E6E6E, TintKind.None), ("farmland", 0x8F6646, TintKind.None),
        ("wheat", 0xB89F4A, TintKind.None), ("carrots", 0x4E8A2A, TintKind.None), ("potatoes", 0x4E8A2A, TintKind.None),
        ("beetroots", 0x4E8A2A, TintKind.None), ("dirt_path", 0x947A41, TintKind.None), ("grass_path", 0x947A41, TintKind.None),
        ("stone_bricks", 0x7A7A7A, TintKind.None), ("mossy_stone_bricks", 0x737966, TintKind.None), ("cracked_stone_bricks", 0x767676, TintKind.None),
        ("smooth_stone", 0x9E9E9E, TintKind.None), ("deepslate", 0x505053, TintKind.None), ("tuff", 0x6C6D66, TintKind.None),
        ("calcite", 0xDFE0DC, TintKind.None), ("dripstone_block", 0x866B5C, TintKind.None), ("moss_block", 0x596E2D, TintKind.None),
        ("moss_carpet", 0x596E2D, TintKind.None), ("mud", 0x3C393D, TintKind.None), ("mangrove_roots", 0x4A3B2B, TintKind.None),
        ("muddy_mangrove_roots", 0x463B2F, TintKind.None), ("rooted_dirt", 0x90684D, TintKind.None), ("azalea", 0x5F7A2C, TintKind.None),
        ("flowering_azalea", 0x707B4C, TintKind.None), ("big_dripleaf", 0x6F9030, TintKind.None), ("small_dripleaf", 0x6F9030, TintKind.None),
        ("glow_lichen", 0x70837A, TintKind.None), ("sculk", 0x0D1E24, TintKind.None), ("bamboo", 0x5D912A, TintKind.None),
        ("sweet_berry_bush", 0x3E6A2A, TintKind.None), ("poppy", 0xA2202A, TintKind.None), ("dandelion", 0xF0D525, TintKind.None),
        ("cornflower", 0x4B6BC9, TintKind.None), ("oxeye_daisy", 0xD6DCDA, TintKind.None), ("azure_bluet", 0xC9D1D8, TintKind.None),
        ("blue_orchid", 0x2AA0D6, TintKind.None), ("allium", 0xB57BDF, TintKind.None), ("lily_of_the_valley", 0xE3E9E3, TintKind.None),
        ("sunflower", 0xF0C82A, TintKind.None), ("lilac", 0xC79EC9, TintKind.None), ("rose_bush", 0xA2202A, TintKind.None),
        ("peony", 0xE4B2D6, TintKind.None), ("brown_mushroom", 0x9A7556, TintKind.None), ("red_mushroom", 0xD73A3A, TintKind.None),
        ("brown_mushroom_block", 0x95704F, TintKind.None), ("red_mushroom_block", 0xC52F2E, TintKind.None), ("mushroom_stem", 0xCBC4B9, TintKind.None),
        ("hay_block", 0xA68C12, TintKind.None), ("oak_fence", 0xA2834F, TintKind.None), ("cobblestone_wall", 0x7F7F7F, TintKind.None),
        ("rail", 0x7D6E58, TintKind.None), ("powered_rail", 0x8D7247, TintKind.None), ("redstone_wire", 0x8F0000, TintKind.None),
        ("redstone_block", 0xAF1904, TintKind.None), ("iron_block", 0xDCDCDC, TintKind.None), ("gold_block", 0xF6D03E, TintKind.None),
        ("diamond_block", 0x62DBD5, TintKind.None), ("emerald_block", 0x2ACB57, TintKind.None), ("lapis_block", 0x1E438C, TintKind.None),
        ("coal_block", 0x101010, TintKind.None), ("quartz_block", 0xECE6DF, TintKind.None), ("prismarine", 0x63A397, TintKind.None),
        ("sea_lantern", 0xACC8BE, TintKind.None), ("purpur_block", 0xA97DA9, TintKind.None), ("chorus_plant", 0x5E395E, TintKind.None),
        ("chorus_flower", 0x977797, TintKind.None), ("end_stone_bricks", 0xDADD9F, TintKind.None), ("tnt", 0xDB441A, TintKind.None),
        ("spawner", 0x1B2A35, TintKind.None), ("slime_block", 0x6FC05B, TintKind.None), ("honey_block", 0xFBB935, TintKind.None),
        ("bubble_column", 0x3F76E4, TintKind.Water), ("bone_block", 0xD1CEB6, TintKind.None), ("cut_sandstone", 0xD9CC9F, TintKind.None),
        ("red_sandstone", 0xB5621F, TintKind.None), ("mangrove_log", 0x544228, TintKind.None), ("cherry_log", 0x371C21, TintKind.None),
        ("pale_oak_log", 0x544C49, TintKind.None), ("crimson_stem", 0x5C1D1E, TintKind.None), ("warped_stem", 0x3A3A4D, TintKind.None),
        ("pointed_dripstone", 0x816556, TintKind.None), ("hanging_roots", 0xA1735C, TintKind.None), ("cave_vines", 0x5A6D29, TintKind.None),
        ("cave_vines_plant", 0x5A6D29, TintKind.None), ("twisting_vines", 0x148F7C, TintKind.None), ("weeping_vines", 0x690000, TintKind.None),
        ("shroomlight", 0xF09246, TintKind.None), ("ancient_debris", 0x5F4039, TintKind.None), ("reinforced_deepslate", 0x515556, TintKind.None),
        ("barrier", 0x00000000, TintKind.None), ("invisible_bedrock", 0x555555, TintKind.None),
        ("ladder", 0x7A6038, TintKind.None), ("cake", 0xE6D2C8, TintKind.None), ("iron_bars", 0x6D6C6A, TintKind.None),
        ("repeater", 0xA19E9C, TintKind.None), ("comparator", 0xA8A4A2, TintKind.None), ("tripwire", 0x9A9A9A, TintKind.None),
        ("tripwire_hook", 0x8A7A5E, TintKind.None), ("end_rod", 0xE0DADA, TintKind.None), ("lever", 0x6F6F6F, TintKind.None),
        ("moving_piston", 0x99846A, TintKind.None), ("sign", 0x9C7E4E, TintKind.None), ("flower_pot", 0x7C4536, TintKind.None),
        ("cobweb", 0xDCDCDC, TintKind.None), ("web", 0xDCDCDC, TintKind.None), ("fire", 0xE0901E, TintKind.None),
        ("soul_fire", 0x40C8D0, TintKind.None), ("suspicious_gravel", 0x837F7E, TintKind.None), ("suspicious_sand", 0xDBCFA3, TintKind.None),
        ("leaf_litter", 0x9A6A2E, TintKind.None), ("bush", 0x6FA65A, TintKind.Grass), ("firefly_bush", 0x5C7A3A, TintKind.None),
        ("lantern", 0x5D5A55, TintKind.None), ("bell", 0xD4AE3A, TintKind.None), ("campfire", 0x8A5A2E, TintKind.None),
        ("composter", 0x7A5A32, TintKind.None), ("barrel", 0x8A6438, TintKind.None), ("hopper", 0x4A4A4A, TintKind.None),
        ("anvil", 0x444444, TintKind.None), ("chipped_anvil", 0x444444, TintKind.None), ("damaged_anvil", 0x444444, TintKind.None),
        ("closed_eyeblossom", 0x6B5B60, TintKind.None), ("open_eyeblossom", 0xE88A2E, TintKind.None), ("dried_ghast", 0xB8A8A0, TintKind.None),
        ("structure_void", 0x00000000, TintKind.None), ("light", 0x00000000, TintKind.None),
    ];

    public static string ToHex(uint argb) => "#" + (argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
}
