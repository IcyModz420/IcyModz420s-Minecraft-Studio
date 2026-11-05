// MinecraftStudio.Core/World/BlockNames.cs: Approximate Bedrock-to-Java block naming used for colours and
// display (full conversion uses mapping tables).
// Owns: BlockNames.
namespace MinecraftStudio.Core.World;

/// <summary>Approximate Bedrock-to-Java block naming used for colours and display (full conversion uses mapping tables).</summary>
public static class BlockNames
{
    static readonly Dictionary<string, string> Renames = new(StringComparer.Ordinal)
    {
        ["grass"] = "grass_block", ["tallgrass"] = "short_grass", ["waterlily"] = "lily_pad", ["reeds"] = "sugar_cane",
        ["snow_layer"] = "snow", ["snow"] = "snow_block", ["deadbush"] = "dead_bush", ["yellow_flower"] = "dandelion", ["red_flower"] = "poppy",
        ["hardened_clay"] = "terracotta", ["double_plant"] = "tall_grass", ["brick_block"] = "bricks", ["stonebrick"] = "stone_bricks",
        ["nether_brick"] = "nether_bricks", ["end_bricks"] = "end_stone_bricks", ["quartz_ore"] = "nether_quartz_ore",
        ["melon_block"] = "melon", ["lit_pumpkin"] = "jack_o_lantern", ["mob_spawner"] = "spawner", ["web"] = "cobweb",
        ["golden_rail"] = "powered_rail", ["noteblock"] = "note_block", ["slime"] = "slime_block", ["grass_path"] = "dirt_path",
        ["magma"] = "magma_block", ["red_nether_brick"] = "red_nether_bricks", ["flowing_water"] = "water", ["flowing_lava"] = "lava",
        ["stained_hardened_clay"] = "terracotta", ["invisiblebedrock"] = "invisible_bedrock", ["lit_redstone_ore"] = "redstone_ore",
        ["lit_furnace"] = "furnace", ["unlit_redstone_torch"] = "redstone_torch", ["wooden_slab"] = "oak_slab", ["double_wooden_slab"] = "oak_slab",
        ["stone_slab"] = "smooth_stone_slab", ["double_stone_slab"] = "smooth_stone_slab", ["fence"] = "oak_fence", ["wooden_door"] = "oak_door",
        ["trapdoor"] = "oak_trapdoor", ["wooden_pressure_plate"] = "oak_pressure_plate", ["wooden_button"] = "oak_button",
        ["standing_sign"] = "oak_sign", ["wall_sign"] = "oak_wall_sign", ["fence_gate"] = "oak_fence_gate", ["monster_egg"] = "infested_stone",
        ["coral_block"] = "tube_coral_block", ["seaLantern"] = "sea_lantern", ["podzol"] = "podzol", ["mycelium"] = "mycelium",
        ["concretepowder"] = "white_concrete_powder", ["kelp"] = "kelp", ["carpet"] = "white_carpet", ["wool"] = "white_wool",
        ["concrete"] = "white_concrete", ["stained_glass"] = "white_stained_glass", ["stained_glass_pane"] = "white_stained_glass_pane",
        ["shulker_box"] = "white_shulker_box", ["undyed_shulker_box"] = "shulker_box", ["leaves"] = "oak_leaves", ["leaves2"] = "acacia_leaves",
        ["log"] = "oak_log", ["log2"] = "acacia_log", ["planks"] = "oak_planks", ["sapling"] = "oak_sapling", ["wood"] = "oak_wood",
        ["sand"] = "sand", ["stone"] = "stone", ["dirt"] = "dirt", ["sandstone"] = "sandstone", ["red_sandstone"] = "red_sandstone",
        ["quartz_block"] = "quartz_block", ["prismarine"] = "prismarine", ["purpur_block"] = "purpur_block", ["cobblestone_wall"] = "cobblestone_wall",
        ["chemistry_table"] = "crafting_table", ["frame"] = "item_frame", ["portal"] = "nether_portal", ["end_portal_frame"] = "end_portal_frame",
    };

    static string Color(string v) => v == "silver" ? "light_gray" : v;

    /// <summary>Java-style base name for a Bedrock (or old Pocket Edition) block state.</summary>
    public static string JavaBaseName(BlockState s)
    {
        string b = s.BaseName;
        string? color = null, wood = null, stoneType = null, sandType = null, dirtType = null, flower = null, plant = null, tallgrass = null, sandstoneType = null;
        foreach (var (k, v) in s.PropertyPairs())
        {
            switch (k)
            {
                case "color": color = Color(v); break;
                case "wood_type": case "old_log_type": case "new_log_type": case "old_leaf_type": case "new_leaf_type": case "sapling_type": wood = v; break;
                case "stone_type": stoneType = v; break;
                case "sand_type": sandType = v; break;
                case "dirt_type": dirtType = v; break;
                case "flower_type": flower = v; break;
                case "double_plant_type": plant = v; break;
                case "tall_grass_type": tallgrass = v; break;
                case "sand_stone_type": sandstoneType = v; break;
            }
        }
        switch (b)
        {
            case "wool": case "carpet": case "concrete": case "stained_glass": case "stained_glass_pane": case "shulker_box":
                return (color ?? "white") + "_" + b;
            case "concretepowder": case "concrete_powder": return (color ?? "white") + "_concrete_powder";
            case "stained_hardened_clay": return (color ?? "white") + "_terracotta";
            case "log": case "log2": return (wood ?? (b == "log" ? "oak" : "acacia")) + "_log";
            case "leaves": case "leaves2": return (wood ?? (b == "leaves" ? "oak" : "acacia")) + "_leaves";
            case "planks": return (wood ?? "oak") + "_planks";
            case "sapling": return (wood ?? "oak") + "_sapling";
            case "wood": return (wood ?? "oak") + "_wood";
            case "stone":
                return stoneType switch
                {
                    "granite" => "granite", "granite_smooth" => "polished_granite", "diorite" => "diorite", "diorite_smooth" => "polished_diorite",
                    "andesite" => "andesite", "andesite_smooth" => "polished_andesite", _ => "stone",
                };
            case "sand": return sandType == "red" ? "red_sand" : "sand";
            case "dirt": return dirtType == "coarse" ? "coarse_dirt" : "dirt";
            case "red_flower":
                return flower switch
                {
                    "orchid" => "blue_orchid", "allium" => "allium", "houstonia" => "azure_bluet", "tulip_red" => "red_tulip", "tulip_orange" => "orange_tulip",
                    "tulip_white" => "white_tulip", "tulip_pink" => "pink_tulip", "oxeye" => "oxeye_daisy", "cornflower" => "cornflower",
                    "lily_of_the_valley" => "lily_of_the_valley", _ => "poppy",
                };
            case "double_plant":
                return plant switch { "sunflower" => "sunflower", "syringa" => "lilac", "fern" => "large_fern", "rose" => "rose_bush", "paeonia" => "peony", _ => "tall_grass" };
            case "tallgrass": return tallgrass == "fern" ? "fern" : "short_grass";
            case "sandstone": return sandstoneType switch { "heiroglyphs" => "chiseled_sandstone", "cut" => "cut_sandstone", "smooth" => "smooth_sandstone", _ => "sandstone" };
        }
        if (s.Name == "runtime_id") return "air";
        return Renames.TryGetValue(b, out var r) ? r : b;
    }
}
