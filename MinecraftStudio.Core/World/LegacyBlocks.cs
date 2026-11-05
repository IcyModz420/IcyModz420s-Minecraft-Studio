// MinecraftStudio.Core/World/LegacyBlocks.cs: Which numeric block id table a legacy chunk uses.
// Owns: LegacyIdScheme, LegacyBlocks.
namespace MinecraftStudio.Core.World;

/// <summary>Which numeric block id table a legacy chunk uses.</summary>
public enum LegacyIdScheme
{
    /// <summary>Java Classic to 1.12, and Legacy Console Edition.</summary>
    Java,
    /// <summary>Pocket Edition and Bedrock before the 1.2.13 palette change.</summary>
    Bedrock,
    /// <summary>Classic and Indev: ids 21-36 are the 16 coloured cloths.</summary>
    Classic,
}

/// <summary>Resolves legacy numeric blocks (id + 4-bit data) to modern block states.</summary>
public static class LegacyBlocks
{
    static readonly string[] Colors = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];
    static readonly string[] Woods = ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak"];

    static readonly BlockState?[] JavaCache = new BlockState?[4096 * 16];
    static readonly BlockState?[] BedrockCache = new BlockState?[4096 * 16];
    static readonly BlockState?[] ClassicCache = new BlockState?[4096 * 16];

    // Classic/Indev cloth colours for ids 21..36 (red, orange, yellow, chartreuse, green, spring green, cyan, capri,
    // ultramarine, violet, purple, magenta, rose, dark gray, gray, white) mapped to the nearest wool.
    static readonly string[] ClassicCloth =
        ["red", "orange", "yellow", "lime", "green", "lime", "cyan", "light_blue", "blue", "purple", "purple", "magenta", "pink", "gray", "light_gray", "white"];

    public static string ClassicName(int id, int d) => id is >= 21 and <= 36 ? ClassicCloth[id - 21] + "_wool" : JavaName(id, d);

    /// <summary>
    /// Exact state for a numeric block: Java ids through the 1.12 → 1.13 flattening table (canonical Java names),
    /// Pocket/Bedrock ids through Mojang's 1.12 → current table (current Bedrock names).
    /// </summary>
    public static BlockState Get(int id, int data, LegacyIdScheme scheme = LegacyIdScheme.Java)
    {
        var cache = scheme switch { LegacyIdScheme.Java => JavaCache, LegacyIdScheme.Bedrock => BedrockCache, _ => ClassicCache };
        int key = ((id & 0xFFF) << 4) | (data & 15);
        var s = cache[key];
        if (s != null) return s;
        s = scheme switch
        {
            LegacyIdScheme.Java => Conversion.JavaBlocks.FromLegacy(id, data & 15),
            LegacyIdScheme.Bedrock => Conversion.BedrockBlocks.FromLegacy(id, data & 15) ?? BlockState.Parse(BedrockName(id, data & 15)),
            _ => Conversion.JavaBlocks.FromClassic(id, data & 15),
        };
        if (s.LegacyId < 0) { s.LegacyId = id; s.LegacyData = data & 15; }
        cache[key] = s;
        return s;
    }

    /// <summary>The older approximate naming (kept for ids the tables do not know).</summary>
    public static BlockState Approximate(int id, int data, LegacyIdScheme scheme) => BlockState.Parse(scheme switch
    {
        LegacyIdScheme.Java => JavaName(id, data & 15),
        LegacyIdScheme.Bedrock => BedrockName(id, data & 15),
        _ => ClassicName(id, data & 15),
    });

    static string Pick(string[] names, int i) => i >= 0 && i < names.Length ? names[i] : names[0];
    static string Color(int d) => Colors[d & 15];
    static string Wood(int d) => Woods[Math.Min(d, 5)];
    static string Axis(int d) => (d >> 2) switch { 1 => "x", 2 => "z", _ => "y" };

    /// <summary>Java Edition 1.12 numeric ids (also Classic, Indev, Alpha, Beta and Legacy Console).</summary>
    public static string JavaName(int id, int d)
    {
        switch (id)
        {
            case 0: return "air";
            case 1: return Pick(["stone", "granite", "polished_granite", "diorite", "polished_diorite", "andesite", "polished_andesite"], d);
            case 2: return "grass_block";
            case 3: return Pick(["dirt", "coarse_dirt", "podzol"], d);
            case 4: return "cobblestone";
            case 5: return Wood(d) + "_planks";
            case 6: return Wood(d & 7) + "_sapling";
            case 7: return "bedrock";
            case 8: case 9: return $"water[level={(id == 9 ? 0 : d)}]";
            case 10: case 11: return $"lava[level={(id == 11 ? 0 : d)}]";
            case 12: return d == 1 ? "red_sand" : "sand";
            case 13: return "gravel";
            case 14: return "gold_ore";
            case 15: return "iron_ore";
            case 16: return "coal_ore";
            case 17: return (d >> 2) == 3 ? Wood(d & 3) + "_wood" : $"{Wood(d & 3)}_log[axis={Axis(d)}]";
            case 18: return Wood(d & 3) + "_leaves";
            case 19: return d == 1 ? "wet_sponge" : "sponge";
            case 20: return "glass";
            case 21: return "lapis_ore";
            case 22: return "lapis_block";
            case 23: return "dispenser";
            case 24: return Pick(["sandstone", "chiseled_sandstone", "cut_sandstone"], d);
            case 25: return "note_block";
            case 26: return "red_bed";
            case 27: return "powered_rail";
            case 28: return "detector_rail";
            case 29: return "sticky_piston";
            case 30: return "cobweb";
            case 31: return Pick(["dead_bush", "short_grass", "fern"], d);
            case 32: return "dead_bush";
            case 33: return "piston";
            case 34: return "piston_head";
            case 35: return Color(d) + "_wool";
            case 36: return "moving_piston";
            case 37: return "dandelion";
            case 38: return Pick(["poppy", "blue_orchid", "allium", "azure_bluet", "red_tulip", "orange_tulip", "white_tulip", "pink_tulip", "oxeye_daisy"], d);
            case 39: return "brown_mushroom";
            case 40: return "red_mushroom";
            case 41: return "gold_block";
            case 42: return "iron_block";
            case 43:
                if (d == 8) return "smooth_stone";
                if (d == 9) return "smooth_sandstone";
                if (d == 15) return "smooth_quartz";
                return Pick(["smooth_stone_slab", "sandstone_slab", "petrified_oak_slab", "cobblestone_slab", "brick_slab", "stone_brick_slab", "nether_brick_slab", "quartz_slab"], d & 7) + "[type=double]";
            case 44: return Pick(["smooth_stone_slab", "sandstone_slab", "petrified_oak_slab", "cobblestone_slab", "brick_slab", "stone_brick_slab", "nether_brick_slab", "quartz_slab"], d & 7) + ((d & 8) != 0 ? "[type=top]" : "[type=bottom]");
            case 45: return "bricks";
            case 46: return "tnt";
            case 47: return "bookshelf";
            case 48: return "mossy_cobblestone";
            case 49: return "obsidian";
            case 50: return d is >= 1 and <= 4 ? "wall_torch" : "torch";
            case 51: return "fire";
            case 52: return "spawner";
            case 53: return "oak_stairs";
            case 54: return "chest";
            case 55: return "redstone_wire";
            case 56: return "diamond_ore";
            case 57: return "diamond_block";
            case 58: return "crafting_table";
            case 59: return $"wheat[age={Math.Min(d, 7)}]";
            case 60: return "farmland";
            case 61: return "furnace";
            case 62: return "furnace[lit=true]";
            case 63: return "oak_sign";
            case 64: return "oak_door";
            case 65: return "ladder";
            case 66: return "rail";
            case 67: return "cobblestone_stairs";
            case 68: return "oak_wall_sign";
            case 69: return "lever";
            case 70: return "stone_pressure_plate";
            case 71: return "iron_door";
            case 72: return "oak_pressure_plate";
            case 73: return "redstone_ore";
            case 74: return "redstone_ore[lit=true]";
            case 75: return d is >= 1 and <= 4 ? "redstone_wall_torch[lit=false]" : "redstone_torch[lit=false]";
            case 76: return d is >= 1 and <= 4 ? "redstone_wall_torch" : "redstone_torch";
            case 77: return "stone_button";
            case 78: return $"snow[layers={(d & 7) + 1}]";
            case 79: return "ice";
            case 80: return "snow_block";
            case 81: return "cactus";
            case 82: return "clay";
            case 83: return "sugar_cane";
            case 84: return "jukebox";
            case 85: return "oak_fence";
            case 86: return "carved_pumpkin";
            case 87: return "netherrack";
            case 88: return "soul_sand";
            case 89: return "glowstone";
            case 90: return "nether_portal";
            case 91: return "jack_o_lantern";
            case 92: return "cake";
            case 93: return "repeater";
            case 94: return "repeater[powered=true]";
            case 95: return Color(d) + "_stained_glass";
            case 96: return "oak_trapdoor";
            case 97: return Pick(["infested_stone", "infested_cobblestone", "infested_stone_bricks", "infested_mossy_stone_bricks", "infested_cracked_stone_bricks", "infested_chiseled_stone_bricks"], d);
            case 98: return Pick(["stone_bricks", "mossy_stone_bricks", "cracked_stone_bricks", "chiseled_stone_bricks"], d);
            case 99: return d == 10 || d == 15 ? "mushroom_stem" : "brown_mushroom_block";
            case 100: return d == 10 || d == 15 ? "mushroom_stem" : "red_mushroom_block";
            case 101: return "iron_bars";
            case 102: return "glass_pane";
            case 103: return "melon";
            case 104: return "pumpkin_stem";
            case 105: return "melon_stem";
            case 106: return "vine";
            case 107: return "oak_fence_gate";
            case 108: return "brick_stairs";
            case 109: return "stone_brick_stairs";
            case 110: return "mycelium";
            case 111: return "lily_pad";
            case 112: return "nether_bricks";
            case 113: return "nether_brick_fence";
            case 114: return "nether_brick_stairs";
            case 115: return $"nether_wart[age={Math.Min(d, 3)}]";
            case 116: return "enchanting_table";
            case 117: return "brewing_stand";
            case 118: return "cauldron";
            case 119: return "end_portal";
            case 120: return "end_portal_frame";
            case 121: return "end_stone";
            case 122: return "dragon_egg";
            case 123: return "redstone_lamp";
            case 124: return "redstone_lamp[lit=true]";
            case 125: return Wood(d & 7) + "_slab[type=double]";
            case 126: return Wood(d & 7) + "_slab" + ((d & 8) != 0 ? "[type=top]" : "[type=bottom]");
            case 127: return "cocoa";
            case 128: return "sandstone_stairs";
            case 129: return "emerald_ore";
            case 130: return "ender_chest";
            case 131: return "tripwire_hook";
            case 132: return "tripwire";
            case 133: return "emerald_block";
            case 134: return "spruce_stairs";
            case 135: return "birch_stairs";
            case 136: return "jungle_stairs";
            case 137: return "command_block";
            case 138: return "beacon";
            case 139: return d == 1 ? "mossy_cobblestone_wall" : "cobblestone_wall";
            case 140:
                return d switch
                {
                    1 => "potted_poppy", 2 => "potted_dandelion", 3 => "potted_oak_sapling", 4 => "potted_spruce_sapling", 5 => "potted_birch_sapling",
                    6 => "potted_jungle_sapling", 7 => "potted_red_mushroom", 8 => "potted_brown_mushroom", 9 => "potted_cactus", 10 => "potted_dead_bush",
                    11 => "potted_fern", 12 => "potted_acacia_sapling", 13 => "potted_dark_oak_sapling", 14 => "potted_blue_orchid", 15 => "potted_allium",
                    _ => "flower_pot",
                };
            case 141: return "carrots";
            case 142: return "potatoes";
            case 143: return "oak_button";
            case 144: return (d & 7) is >= 2 and <= 5 ? "skeleton_wall_skull" : "skeleton_skull";
            case 145: return Pick(["anvil", "chipped_anvil", "damaged_anvil"], d >> 2);
            case 146: return "trapped_chest";
            case 147: return "light_weighted_pressure_plate";
            case 148: return "heavy_weighted_pressure_plate";
            case 149: return "comparator";
            case 150: return "comparator[powered=true]";
            case 151: return "daylight_detector";
            case 152: return "redstone_block";
            case 153: return "nether_quartz_ore";
            case 154: return "hopper";
            case 155: return d switch { 0 => "quartz_block", 1 => "chiseled_quartz_block", 3 => "quartz_pillar[axis=x]", 4 => "quartz_pillar[axis=z]", 5 or 9 or 13 => "chiseled_quartz_block", _ when (d & 3) == 0 => "quartz_block", _ => "quartz_pillar[axis=y]" };
            case 156: return "quartz_stairs";
            case 157: return "activator_rail";
            case 158: return "dropper";
            case 159: return Color(d) + "_terracotta";
            case 160: return Color(d) + "_stained_glass_pane";
            case 161: return ((d & 3) == 1 ? "dark_oak" : "acacia") + "_leaves";
            case 162: return (d >> 2) == 3 ? ((d & 3) == 1 ? "dark_oak" : "acacia") + "_wood" : $"{((d & 3) == 1 ? "dark_oak" : "acacia")}_log[axis={Axis(d)}]";
            case 163: return "acacia_stairs";
            case 164: return "dark_oak_stairs";
            case 165: return "slime_block";
            case 166: return "barrier";
            case 167: return "iron_trapdoor";
            case 168: return Pick(["prismarine", "prismarine_bricks", "dark_prismarine"], d);
            case 169: return "sea_lantern";
            case 170: return "hay_block";
            case 171: return Color(d) + "_carpet";
            case 172: return "terracotta";
            case 173: return "coal_block";
            case 174: return "packed_ice";
            case 175: return Pick(["sunflower", "lilac", "tall_grass", "large_fern", "rose_bush", "peony"], d & 7) + ((d & 8) != 0 ? "[half=upper]" : "[half=lower]");
            case 176: return "white_banner";
            case 177: return "white_wall_banner";
            case 178: return "daylight_detector[inverted=true]";
            case 179: return Pick(["red_sandstone", "chiseled_red_sandstone", "cut_red_sandstone"], d);
            case 180: return "red_sandstone_stairs";
            case 181: return d == 8 ? "smooth_red_sandstone" : "red_sandstone_slab[type=double]";
            case 182: return "red_sandstone_slab" + ((d & 8) != 0 ? "[type=top]" : "[type=bottom]");
            case 183: return "spruce_fence_gate";
            case 184: return "birch_fence_gate";
            case 185: return "jungle_fence_gate";
            case 186: return "dark_oak_fence_gate";
            case 187: return "acacia_fence_gate";
            case 188: return "spruce_fence";
            case 189: return "birch_fence";
            case 190: return "jungle_fence";
            case 191: return "dark_oak_fence";
            case 192: return "acacia_fence";
            case 193: return "spruce_door";
            case 194: return "birch_door";
            case 195: return "jungle_door";
            case 196: return "acacia_door";
            case 197: return "dark_oak_door";
            case 198: return "end_rod";
            case 199: return "chorus_plant";
            case 200: return "chorus_flower";
            case 201: return "purpur_block";
            case 202: return "purpur_pillar";
            case 203: return "purpur_stairs";
            case 204: return "purpur_slab[type=double]";
            case 205: return "purpur_slab" + ((d & 8) != 0 ? "[type=top]" : "[type=bottom]");
            case 206: return "end_stone_bricks";
            case 207: return "beetroots";
            case 208: return "dirt_path";
            case 209: return "end_gateway";
            case 210: return "repeating_command_block";
            case 211: return "chain_command_block";
            case 212: return "frosted_ice";
            case 213: return "magma_block";
            case 214: return "nether_wart_block";
            case 215: return "red_nether_bricks";
            case 216: return "bone_block";
            case 217: return "structure_void";
            case 218: return "observer";
            case >= 219 and <= 234: return Colors[id - 219] + "_shulker_box";
            case >= 235 and <= 250: return Colors[id - 235] + "_glazed_terracotta";
            case 251: return Color(d) + "_concrete";
            case 252: return Color(d) + "_concrete_powder";
            case 255: return "structure_block";
            default: return $"legacy_block_{id}";
        }
    }

    /// <summary>Pocket Edition / pre-1.2.13 Bedrock numeric ids (same as Java where not listed).</summary>
    public static string BedrockName(int id, int d)
    {
        switch (id)
        {
            case 85: return Wood(d & 7) + "_fence";
            case 95: return "invisible_bedrock";
            case 125: return "dropper";
            case 126: return "activator_rail";
            case 157: return Wood(d & 7) + "_slab[type=double]";
            case 158: return Wood(d & 7) + "_slab" + ((d & 8) != 0 ? "[type=top]" : "[type=bottom]");
            case 188: return "repeating_command_block";
            case 189: return "chain_command_block";
            case 198: return "dirt_path";
            case 199: return "item_frame";
            case 200: return "chorus_flower";
            case 201: return d is 2 ? "purpur_pillar" : "purpur_block";
            case 203: return "purpur_stairs";
            case 205: return "shulker_box";
            case 206: return "end_stone_bricks";
            case 207: return "frosted_ice";
            case 208: return "end_rod";
            case 209: return "end_gateway";
            case 213: return "magma_block";
            case 214: return "nether_wart_block";
            case 215: return "red_nether_bricks";
            case 216: return "bone_block";
            case 218: return Color(d) + "_shulker_box";
            case 219: return "purple_glazed_terracotta";
            case 220: return "white_glazed_terracotta";
            case 221: return "orange_glazed_terracotta";
            case 222: return "magenta_glazed_terracotta";
            case 223: return "light_blue_glazed_terracotta";
            case 224: return "yellow_glazed_terracotta";
            case 225: return "lime_glazed_terracotta";
            case 226: return "pink_glazed_terracotta";
            case 227: return "gray_glazed_terracotta";
            case 228: return "light_gray_glazed_terracotta";
            case 229: return "cyan_glazed_terracotta";
            case 231: return "blue_glazed_terracotta";
            case 232: return "brown_glazed_terracotta";
            case 233: return "green_glazed_terracotta";
            case 234: return "red_glazed_terracotta";
            case 235: return "black_glazed_terracotta";
            case 236: return Color(d) + "_concrete";
            case 237: return Color(d) + "_concrete_powder";
            case 240: return "chorus_plant";
            case 241: return Color(d) + "_stained_glass";
            case 243: return "podzol";
            case 244: return "beetroots";
            case 245: return "stonecutter";
            case 246: return "glowing_obsidian";
            case 247: return "nether_reactor_core";
            case 248: return "info_update";
            case 249: return "info_update2";
            case 250: return "moving_piston";
            case 251: return "observer";
            case 252: return "structure_block";
            case 253: return "hard_glass";
            case 254: return "hard_" + Color(d) + "_stained_glass";
            case 255: return "reserved6";
            default: return JavaName(id, d);
        }
    }
}
