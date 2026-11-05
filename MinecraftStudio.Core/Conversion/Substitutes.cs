// MinecraftStudio.Core/Conversion/Substitutes.cs: Stand-ins for blocks a target version does not have.
// Owns: Substitutes.
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Stand-ins for blocks a target version does not have. Each step proposes a simpler or older block with the
/// same look and shape (cherry stairs -> birch stairs -> oak stairs, deepslate iron ore -> iron ore, kelp -> water);
/// when nothing similar exists, solid blocks become the nearest-coloured plain cube and everything else air.
/// </summary>
public static class Substitutes
{
    // Specific blocks: candidates in order of preference.
    static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal)
    {
        // 1.17-1.18 caves & cliffs
        ["deepslate"] = ["stone"], ["cobbled_deepslate"] = ["cobblestone"], ["polished_deepslate"] = ["smooth_stone", "stone"],
        ["deepslate_bricks"] = ["stone_bricks"], ["cracked_deepslate_bricks"] = ["cracked_stone_bricks", "stone_bricks"],
        ["deepslate_tiles"] = ["stone_bricks"], ["cracked_deepslate_tiles"] = ["cracked_stone_bricks", "stone_bricks"],
        ["chiseled_deepslate"] = ["chiseled_stone_bricks", "stone_bricks"], ["reinforced_deepslate"] = ["bedrock"],
        ["infested_deepslate"] = ["infested_stone"], ["tuff"] = ["andesite", "stone"], ["polished_tuff"] = ["polished_andesite", "andesite"],
        ["tuff_bricks"] = ["stone_bricks"], ["chiseled_tuff"] = ["chiseled_stone_bricks"], ["chiseled_tuff_bricks"] = ["chiseled_stone_bricks"],
        ["calcite"] = ["diorite", "white_terracotta", "quartz_block"], ["dripstone_block"] = ["granite", "brown_terracotta"],
        ["pointed_dripstone"] = ["air"], ["smooth_basalt"] = ["stone"], ["tinted_glass"] = ["black_stained_glass", "glass"],
        ["powder_snow"] = ["snow_block"], ["rooted_dirt"] = ["coarse_dirt", "dirt"], ["moss_block"] = ["green_wool", "mossy_cobblestone"],
        ["moss_carpet"] = ["green_carpet", "air"], ["pale_moss_block"] = ["light_gray_wool"], ["pale_moss_carpet"] = ["light_gray_carpet", "air"],
        ["azalea"] = ["oak_leaves"], ["flowering_azalea"] = ["oak_leaves"], ["azalea_leaves"] = ["oak_leaves"], ["flowering_azalea_leaves"] = ["oak_leaves"],
        ["glow_lichen"] = ["air"], ["hanging_roots"] = ["air"], ["spore_blossom"] = ["air"], ["small_dripleaf"] = ["air"], ["big_dripleaf"] = ["lily_pad", "air"],
        ["big_dripleaf_stem"] = ["air"], ["cave_vines"] = ["air"], ["cave_vines_plant"] = ["air"], ["sculk_vein"] = ["air"],
        ["amethyst_cluster"] = ["air"], ["large_amethyst_bud"] = ["air"], ["medium_amethyst_bud"] = ["air"], ["small_amethyst_bud"] = ["air"],
        ["budding_amethyst"] = ["amethyst_block"], ["amethyst_block"] = ["purple_wool", "purpur_block", "obsidian"],
        ["lightning_rod"] = ["end_rod", "air"], ["light"] = ["air"], ["stone_slab"] = ["smooth_stone_slab"], ["stone_stairs"] = ["cobblestone_stairs"],
        ["raw_iron_block"] = ["iron_block"], ["raw_gold_block"] = ["gold_block"], ["copper_ore"] = ["stone"], ["deepslate_copper_ore"] = ["copper_ore"],
        // 1.19 wild
        ["mud"] = ["coarse_dirt", "dirt"], ["packed_mud"] = ["dirt"], ["mud_bricks"] = ["bricks"], ["muddy_mangrove_roots"] = ["dirt"],
        ["mangrove_roots"] = ["jungle_wood"], ["sculk"] = ["black_concrete", "black_wool"], ["sculk_catalyst"] = ["sculk"], ["sculk_shrieker"] = ["sculk"],
        ["sculk_sensor"] = ["sculk"], ["calibrated_sculk_sensor"] = ["sculk_sensor"], ["frogspawn"] = ["air"],
        ["ochre_froglight"] = ["glowstone"], ["verdant_froglight"] = ["glowstone"], ["pearlescent_froglight"] = ["glowstone"],
        // 1.20 trails & tales, 1.21
        ["decorated_pot"] = ["flower_pot"], ["suspicious_sand"] = ["sand"], ["suspicious_gravel"] = ["gravel"], ["sniffer_egg"] = ["air"],
        ["chiseled_bookshelf"] = ["bookshelf"], ["torchflower"] = ["dandelion"], ["torchflower_crop"] = ["wheat"], ["pitcher_plant"] = ["tall_grass"],
        ["pitcher_crop"] = ["wheat"], ["pink_petals"] = ["air"], ["crafter"] = ["dropper"], ["trial_spawner"] = ["spawner"], ["vault"] = ["spawner"],
        ["heavy_core"] = ["air"], ["copper_grate"] = ["iron_bars"], ["creaking_heart"] = ["pale_oak_log"], ["pale_hanging_moss"] = ["air"],
        ["open_eyeblossom"] = ["oxeye_daisy"], ["closed_eyeblossom"] = ["oxeye_daisy"], ["resin_clump"] = ["air"], ["resin_block"] = ["orange_terracotta"],
        ["chiseled_resin_bricks"] = ["resin_bricks"], ["resin_bricks"] = ["bricks"], ["firefly_bush"] = ["short_grass"], ["leaf_litter"] = ["air"],
        ["bush"] = ["short_grass"], ["cactus_flower"] = ["air"], ["short_dry_grass"] = ["dead_bush"], ["tall_dry_grass"] = ["dead_bush"],
        ["wildflowers"] = ["dandelion"], ["dried_ghast"] = ["air"], ["test_block"] = ["air"], ["test_instance_block"] = ["air"],
        // 1.16 nether
        ["soul_soil"] = ["soul_sand"], ["soul_fire"] = ["fire"], ["soul_torch"] = ["torch"], ["soul_wall_torch"] = ["wall_torch"],
        ["soul_lantern"] = ["lantern"], ["soul_campfire"] = ["campfire"], ["nether_gold_ore"] = ["nether_quartz_ore"], ["quartz_bricks"] = ["quartz_block"],
        ["chiseled_nether_bricks"] = ["nether_bricks"], ["cracked_nether_bricks"] = ["nether_bricks"], ["crimson_nylium"] = ["netherrack"],
        ["warped_nylium"] = ["netherrack"], ["warped_wart_block"] = ["nether_wart_block"], ["shroomlight"] = ["glowstone"],
        ["crimson_fungus"] = ["red_mushroom"], ["warped_fungus"] = ["brown_mushroom"], ["crimson_roots"] = ["dead_bush", "air"], ["warped_roots"] = ["dead_bush", "air"],
        ["nether_sprouts"] = ["air"], ["weeping_vines"] = ["air"], ["weeping_vines_plant"] = ["air"], ["twisting_vines"] = ["air"], ["twisting_vines_plant"] = ["air"],
        ["basalt"] = ["stone"], ["polished_basalt"] = ["stone"], ["blackstone"] = ["cobblestone"], ["polished_blackstone"] = ["stone"],
        ["polished_blackstone_bricks"] = ["nether_bricks", "stone_bricks"], ["cracked_polished_blackstone_bricks"] = ["polished_blackstone_bricks"],
        ["chiseled_polished_blackstone"] = ["polished_blackstone"], ["gilded_blackstone"] = ["blackstone"], ["crying_obsidian"] = ["obsidian"],
        ["respawn_anchor"] = ["obsidian"], ["lodestone"] = ["chiseled_stone_bricks"], ["ancient_debris"] = ["netherrack"], ["netherite_block"] = ["obsidian"],
        ["target"] = ["hay_block"], ["chain"] = ["iron_bars"], ["iron_chain"] = ["iron_bars"],
        // 1.14 village & pillage, 1.15 bees
        ["barrel"] = ["chest"], ["smoker"] = ["furnace"], ["blast_furnace"] = ["furnace"], ["smithing_table"] = ["crafting_table"],
        ["fletching_table"] = ["crafting_table"], ["cartography_table"] = ["crafting_table"], ["loom"] = ["crafting_table"], ["stonecutter"] = ["crafting_table"],
        ["grindstone"] = ["anvil"], ["composter"] = ["cauldron"], ["lectern"] = ["bookshelf"], ["bell"] = ["air"], ["lantern"] = ["torch"],
        ["campfire"] = ["air"], ["scaffolding"] = ["air"], ["sweet_berry_bush"] = ["short_grass"], ["cornflower"] = ["blue_orchid", "poppy"],
        ["lily_of_the_valley"] = ["white_tulip", "oxeye_daisy", "dandelion"], ["wither_rose"] = ["poppy"], ["jigsaw"] = ["structure_block", "air"],
        ["bee_nest"] = ["hay_block"], ["beehive"] = ["oak_planks"], ["honey_block"] = ["slime_block"], ["honeycomb_block"] = ["hay_block"],
        // 1.13 aquatic
        ["blue_ice"] = ["packed_ice", "ice"], ["conduit"] = ["sea_lantern", "glowstone"], ["dried_kelp_block"] = ["green_wool"], ["turtle_egg"] = ["air"],
        ["sea_pickle"] = ["water"], ["shulker_box"] = ["purple_shulker_box"], ["stripped_oak_log"] = ["oak_log"], ["smooth_stone"] = ["stone"],
        ["cave_air"] = ["air"], ["void_air"] = ["air"], ["dead_bush"] = ["air"], ["bamboo"] = ["sugar_cane"], ["bamboo_sapling"] = ["sugar_cane"],
        // pre-1.13 blocks, for very old targets
        ["red_sand"] = ["sand"], ["coarse_dirt"] = ["dirt"], ["podzol"] = ["dirt"], ["granite"] = ["stone"], ["polished_granite"] = ["granite"],
        ["diorite"] = ["stone"], ["polished_diorite"] = ["diorite"], ["andesite"] = ["stone"], ["polished_andesite"] = ["andesite"],
        ["prismarine"] = ["cyan_wool"], ["prismarine_bricks"] = ["prismarine"], ["dark_prismarine"] = ["prismarine"], ["sea_lantern"] = ["glowstone"],
        ["slime_block"] = ["lime_wool"], ["barrier"] = ["air"], ["red_sandstone"] = ["sandstone"], ["chiseled_red_sandstone"] = ["red_sandstone"],
        ["cut_red_sandstone"] = ["red_sandstone"], ["smooth_red_sandstone"] = ["red_sandstone"], ["chiseled_sandstone"] = ["sandstone"],
        ["cut_sandstone"] = ["sandstone"], ["smooth_sandstone"] = ["sandstone"], ["sandstone"] = ["sand"],
        ["purpur_block"] = ["magenta_wool"], ["purpur_pillar"] = ["purpur_block"], ["end_stone_bricks"] = ["end_stone"], ["end_stone"] = ["sandstone"],
        ["beetroots"] = ["carrots", "wheat"], ["carrots"] = ["wheat"], ["potatoes"] = ["wheat"], ["dirt_path"] = ["farmland", "dirt"],
        ["frosted_ice"] = ["ice"], ["magma_block"] = ["netherrack"], ["nether_wart_block"] = ["red_wool"], ["red_nether_bricks"] = ["nether_bricks"],
        ["bone_block"] = ["white_wool"], ["observer"] = ["dispenser"], ["end_rod"] = ["torch"], ["chorus_plant"] = ["air"], ["chorus_flower"] = ["air"],
        ["structure_void"] = ["air"], ["end_gateway"] = ["air"], ["packed_ice"] = ["ice"], ["terracotta"] = ["orange_wool"], ["hay_block"] = ["yellow_wool"],
        ["coal_block"] = ["black_wool"], ["iron_trapdoor"] = ["oak_trapdoor"], ["daylight_detector"] = ["oak_slab"], ["redstone_block"] = ["red_wool"],
        ["nether_quartz_ore"] = ["netherrack"], ["quartz_block"] = ["white_wool"], ["chiseled_quartz_block"] = ["quartz_block"], ["quartz_pillar"] = ["quartz_block"],
        ["smooth_quartz"] = ["quartz_block"], ["hopper"] = ["air"], ["dropper"] = ["dispenser"], ["comparator"] = ["repeater"], ["trapped_chest"] = ["chest"],
        ["light_weighted_pressure_plate"] = ["stone_pressure_plate"], ["heavy_weighted_pressure_plate"] = ["stone_pressure_plate"],
        ["activator_rail"] = ["powered_rail"], ["beacon"] = ["glass"], ["flower_pot"] = ["air"], ["anvil"] = ["iron_block"], ["chipped_anvil"] = ["anvil"],
        ["damaged_anvil"] = ["anvil"], ["emerald_ore"] = ["diamond_ore"], ["emerald_block"] = ["diamond_block"], ["ender_chest"] = ["chest"],
        ["tripwire_hook"] = ["air"], ["tripwire"] = ["air"], ["cocoa"] = ["air"], ["redstone_lamp"] = ["glowstone"], ["dragon_egg"] = ["obsidian"],
        ["end_portal_frame"] = ["end_stone"], ["end_portal"] = ["air"], ["cauldron"] = ["air"], ["water_cauldron"] = ["cauldron"], ["lava_cauldron"] = ["cauldron"],
        ["powder_snow_cauldron"] = ["cauldron"], ["brewing_stand"] = ["air"], ["enchanting_table"] = ["obsidian"], ["nether_wart"] = ["air"],
        ["nether_bricks"] = ["bricks"], ["nether_brick_fence"] = ["oak_fence"], ["lily_pad"] = ["air"], ["mycelium"] = ["grass_block"],
        ["vine"] = ["air"], ["melon"] = ["pumpkin"], ["melon_stem"] = ["pumpkin_stem"], ["pumpkin_stem"] = ["air"], ["attached_melon_stem"] = ["melon_stem"],
        ["attached_pumpkin_stem"] = ["pumpkin_stem"], ["iron_bars"] = ["glass_pane", "air"], ["glass_pane"] = ["air"], ["brown_mushroom_block"] = ["brown_wool", "dirt"],
        ["red_mushroom_block"] = ["red_wool", "netherrack", "bricks"], ["mushroom_stem"] = ["white_wool"], ["stone_bricks"] = ["stone"], ["mossy_stone_bricks"] = ["stone_bricks"],
        ["cracked_stone_bricks"] = ["stone_bricks"], ["chiseled_stone_bricks"] = ["stone_bricks"], ["infested_stone"] = ["stone"],
        ["infested_cobblestone"] = ["cobblestone"], ["infested_stone_bricks"] = ["stone_bricks"], ["infested_mossy_stone_bricks"] = ["mossy_stone_bricks"],
        ["infested_cracked_stone_bricks"] = ["cracked_stone_bricks"], ["infested_chiseled_stone_bricks"] = ["chiseled_stone_bricks"],
        ["piston"] = ["air"], ["sticky_piston"] = ["piston"], ["piston_head"] = ["air"], ["moving_piston"] = ["air"], ["cobweb"] = ["air"],
        ["short_grass"] = ["air"], ["fern"] = ["short_grass"], ["tall_grass"] = ["short_grass"], ["large_fern"] = ["fern"], ["sunflower"] = ["dandelion"],
        ["lilac"] = ["allium", "poppy"], ["rose_bush"] = ["poppy"], ["peony"] = ["pink_tulip", "poppy"], ["blue_orchid"] = ["poppy"], ["allium"] = ["poppy"],
        ["azure_bluet"] = ["oxeye_daisy"], ["red_tulip"] = ["poppy"], ["orange_tulip"] = ["poppy"], ["white_tulip"] = ["oxeye_daisy"], ["pink_tulip"] = ["poppy"],
        ["oxeye_daisy"] = ["dandelion"], ["cake"] = ["air"], ["repeater"] = ["air"], ["oak_trapdoor"] = ["air"], ["note_block"] = ["oak_planks"],
        ["red_bed"] = ["air"], ["lapis_ore"] = ["stone"], ["lapis_block"] = ["blue_wool"], ["dispenser"] = ["furnace"], ["powered_rail"] = ["rail"],
        ["detector_rail"] = ["rail"], ["pumpkin"] = ["carved_pumpkin", "orange_wool"], ["carved_pumpkin"] = ["pumpkin", "orange_wool"], ["jack_o_lantern"] = ["carved_pumpkin"],
        ["netherrack"] = ["red_wool", "bricks"], ["soul_sand"] = ["sand"], ["glowstone"] = ["yellow_wool"], ["nether_portal"] = ["air"], ["jukebox"] = ["oak_planks"],
        ["clay"] = ["gravel"], ["sugar_cane"] = ["air"], ["cactus"] = ["air"], ["snow_block"] = ["white_wool"], ["snow"] = ["air"], ["ice"] = ["glass"],
        ["lever"] = ["air"], ["stone_button"] = ["air"], ["stone_pressure_plate"] = ["air"], ["oak_pressure_plate"] = ["air"], ["iron_door"] = ["oak_door"],
        ["redstone_ore"] = ["stone"], ["redstone_torch"] = ["torch"], ["redstone_wall_torch"] = ["wall_torch"], ["redstone_wire"] = ["air"],
        ["oak_fence"] = ["air"], ["ladder"] = ["air"], ["rail"] = ["air"], ["oak_sign"] = ["air"], ["oak_wall_sign"] = ["air"], ["oak_door"] = ["air"],
        ["spawner"] = ["mossy_cobblestone"], ["chest"] = ["oak_planks"], ["furnace"] = ["cobblestone"], ["crafting_table"] = ["oak_planks"],
        ["wheat"] = ["air"], ["farmland"] = ["dirt"], ["wall_torch"] = ["torch"], ["torch"] = ["air"], ["fire"] = ["air"], ["diamond_ore"] = ["iron_ore"],
        ["diamond_block"] = ["iron_block"], ["water"] = ["air"], ["lava"] = ["air"],
    };

    static readonly (string Suffix, string[] Fallbacks)[] ShapeSuffixes =
    [
        ("_wall_hanging_sign", ["oak_wall_sign"]), ("_hanging_sign", ["oak_sign"]), ("_wall_sign", ["oak_wall_sign"]), ("_sign", ["oak_sign"]),
        ("_fence_gate", ["oak_fence_gate", "oak_fence"]), ("_pressure_plate", ["stone_pressure_plate", "oak_pressure_plate"]),
        ("_trapdoor", ["oak_trapdoor"]), ("_button", ["stone_button", "oak_button"]), ("_stairs", ["cobblestone_stairs", "oak_stairs"]),
        ("_slab", ["cobblestone_slab", "smooth_stone_slab", "oak_slab"]), ("_wall", ["cobblestone_wall", "oak_fence"]), ("_fence", ["oak_fence"]),
        ("_door", ["oak_door"]), ("_wall_banner", ["white_wall_banner"]), ("_banner", ["white_banner"]), ("_carpet", ["white_carpet"]),
        ("_bed", ["red_bed"]), ("_shulker_box", ["chest"]), ("_candle_cake", ["cake"]), ("_candle", ["torch"]),
    ];

    // Wood types (and nether "woods") step back to an older one with a similar colour.
    static readonly Dictionary<string, string> Woods = new(StringComparer.Ordinal)
    {
        ["cherry"] = "birch", ["pale_oak"] = "birch", ["mangrove"] = "jungle", ["bamboo"] = "jungle", ["crimson"] = "dark_oak", ["warped"] = "spruce",
        ["acacia"] = "oak", ["dark_oak"] = "spruce", ["jungle"] = "oak", ["birch"] = "oak", ["spruce"] = "oak",
    };

    // Stone-like materials of stairs/slabs/walls and the material they fall back to.
    static readonly Dictionary<string, string> Materials = new(StringComparer.Ordinal)
    {
        ["deepslate_brick"] = "stone_brick", ["deepslate_tile"] = "stone_brick", ["polished_deepslate"] = "stone_brick", ["cobbled_deepslate"] = "cobblestone",
        ["blackstone"] = "cobblestone", ["polished_blackstone"] = "stone_brick", ["polished_blackstone_brick"] = "nether_brick",
        ["tuff"] = "andesite", ["polished_tuff"] = "polished_andesite", ["tuff_brick"] = "stone_brick", ["mud_brick"] = "brick", ["resin_brick"] = "brick",
        ["cut_copper"] = "red_sandstone", ["exposed_cut_copper"] = "cut_copper", ["weathered_cut_copper"] = "prismarine", ["oxidized_cut_copper"] = "prismarine",
        ["smooth_sandstone"] = "sandstone", ["smooth_red_sandstone"] = "red_sandstone", ["smooth_quartz"] = "quartz", ["cut_sandstone"] = "sandstone",
        ["cut_red_sandstone"] = "red_sandstone", ["polished_granite"] = "granite", ["polished_diorite"] = "diorite", ["polished_andesite"] = "andesite",
        ["granite"] = "cobblestone", ["diorite"] = "cobblestone", ["andesite"] = "cobblestone", ["mossy_cobblestone"] = "cobblestone",
        ["mossy_stone_brick"] = "stone_brick", ["red_nether_brick"] = "nether_brick", ["end_stone_brick"] = "sandstone", ["stone"] = "cobblestone",
        ["prismarine_brick"] = "prismarine", ["dark_prismarine"] = "prismarine", ["prismarine"] = "stone_brick", ["purpur"] = "quartz",
        ["red_sandstone"] = "sandstone", ["quartz"] = "sandstone", ["nether_brick"] = "brick", ["sandstone"] = "cobblestone", ["brick"] = "cobblestone",
        ["stone_brick"] = "cobblestone", ["petrified_oak"] = "oak", ["smooth_stone"] = "stone",
    };

    static readonly string[] Colors = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];

    /// <summary>Candidate stand-ins for <paramref name="s"/>, most similar first (properties carried over).</summary>
    public static IEnumerable<BlockState> Candidates(BlockState s)
    {
        string b = s.BaseName;
        var props = s.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();
        BlockState Make(string name) => name == "air" ? BlockState.Air : BlockState.Get("minecraft:" + name, props);

        // Mod / add-on blocks: the vanilla block of the same name, else a vanilla block of the same kind.
        if (!s.Name.StartsWith("minecraft:", StringComparison.Ordinal))
        {
            string local = s.Name[(s.Name.IndexOf(':') + 1)..];
            if (JavaBlockData.Latest.Blocks.ContainsKey("minecraft:" + local)) { yield return Make(local); yield break; }
            string? kind = local switch
            {
                _ when local.Contains("deepslate") && local.EndsWith("_ore") => "deepslate",
                _ when local.Contains("end_stone") || local.StartsWith("end_") && local.EndsWith("_ore") => "end_stone",
                _ when local.Contains("netherrack") || local.Contains("nether") && local.EndsWith("_ore") => "netherrack",
                _ when local.EndsWith("_ore") => "stone",
                _ when local.EndsWith("_log") || local.EndsWith("_stem") => "oak_log",
                _ when local.EndsWith("_wood") || local.EndsWith("_hyphae") => "oak_wood",
                _ when local.EndsWith("_planks") => "oak_planks",
                _ when local.EndsWith("_leaves") => "oak_leaves",
                _ when local.EndsWith("_sapling") => "oak_sapling",
                _ when local.EndsWith("_grass") || local.EndsWith("_bush") || local.EndsWith("_flower") || local.Contains("berry") || local.Contains("marker") => "air",
                _ => null,
            };
            if (kind != null) { yield return Make(kind); yield break; }
        }

        // Underwater plants turn into the water they stand in.
        if (b is "seagrass" or "tall_seagrass" or "kelp" or "kelp_plant" or "bubble_column") { yield return BlockState.Get("minecraft:water", "level=0"); yield break; }
        if (b.Contains("coral", StringComparison.Ordinal) && !b.EndsWith("_block", StringComparison.Ordinal))
        {
            yield return s.GetProperty("waterlogged") == "false" ? BlockState.Air : BlockState.Get("minecraft:water", "level=0");
            yield break;
        }
        if (b.StartsWith("waxed_", StringComparison.Ordinal)) { yield return Make(b[6..]); yield break; }
        if (b.StartsWith("potted_", StringComparison.Ordinal)) { yield return BlockState.Get("minecraft:flower_pot"); yield break; }
        if ((b.EndsWith("_lantern", StringComparison.Ordinal) || b == "lantern") && s.GetProperty("hanging") == "true") { yield return BlockState.Air; yield break; }
        if (Table.TryGetValue(b, out var listed))
        {
            foreach (var n in listed) yield return Make(n);
            yield break;
        }
        if (b.StartsWith("stripped_", StringComparison.Ordinal)) { yield return Make(b[9..]); yield break; }
        if (b.EndsWith("_stem", StringComparison.Ordinal) || b.EndsWith("_hyphae", StringComparison.Ordinal))
        {
            string wood = b[..b.LastIndexOf('_')];
            if (Woods.TryGetValue(wood, out var w)) { yield return Make(w + (b.EndsWith("_stem") ? "_log" : "_wood")); yield break; }
        }
        if (b.StartsWith("deepslate_", StringComparison.Ordinal) && b.EndsWith("_ore", StringComparison.Ordinal)) { yield return Make(b[10..]); yield break; }
        if (b.EndsWith("_copper", StringComparison.Ordinal) || b == "copper_block" || b.Contains("copper", StringComparison.Ordinal) && !b.Contains("_stairs") && !b.Contains("_slab"))
        {
            if (b.Contains("door") || b.Contains("trapdoor")) { yield return Make(b.Contains("trapdoor") ? "iron_trapdoor" : "iron_door"); yield break; }
            if (b.Contains("bulb")) { yield return Make("redstone_lamp"); yield break; }
            if (b.Contains("torch")) { yield return Make(b.Contains("wall") ? "wall_torch" : "torch"); yield break; }
            if (b.Contains("lantern")) { yield return Make("lantern"); yield break; }
            if (b.Contains("bars")) { yield return Make("iron_bars"); yield break; }
            if (b.Contains("chain")) { yield return Make("iron_chain"); yield break; }
            if (b.Contains("chest")) { yield return Make("chest"); yield break; }
            if (b.Contains("golem") || b.Contains("statue")) { yield return BlockState.Air; yield break; }
        }
        // Wood families: cherry_planks -> birch_planks -> oak_planks, crimson_door -> dark_oak_door ...
        foreach (var (wood, older) in Woods)
            if (b.StartsWith(wood + "_", StringComparison.Ordinal))
            {
                string rest = b[(wood.Length + 1)..];
                if (rest is "log" or "wood" or "planks" or "leaves" or "sapling" or "propagule" or "slab" or "stairs" or "fence" or "fence_gate" or "door" or "trapdoor"
                    or "button" or "pressure_plate" or "sign" or "wall_sign" or "hanging_sign" or "wall_hanging_sign" or "boat" or "shelf" or "mosaic" or "mosaic_slab" or "mosaic_stairs" or "block")
                {
                    if (rest is "propagule") { yield return Make(older + "_sapling"); yield break; }
                    if (rest is "mosaic") { yield return Make("bamboo_planks"); yield break; }
                    if (rest is "mosaic_slab" or "mosaic_stairs") { yield return Make("bamboo_" + rest[7..]); yield break; }
                    if (rest is "block") { yield return Make("jungle_log"); yield break; }
                    if (rest is "shelf") { yield return Make(wood + "_slab"); yield break; }
                    yield return Make(older + "_" + rest);
                    yield break;
                }
            }
        // Colour families: concrete powder -> concrete -> terracotta -> wool.
        foreach (var color in Colors)
        {
            if (!b.StartsWith(color + "_", StringComparison.Ordinal)) continue;
            string rest = b[(color.Length + 1)..];
            string? next = rest switch
            {
                "concrete_powder" => "concrete", "concrete" => "terracotta", "glazed_terracotta" => "terracotta", "terracotta" => "wool",
                "stained_glass" => null, "stained_glass_pane" => null, "shulker_box" => null, "candle" => null, "candle_cake" => null, _ => null,
            };
            if (next != null) { yield return Make(color + "_" + next); yield break; }
            if (rest == "stained_glass") { yield return Make("glass"); yield break; }
            if (rest == "stained_glass_pane") { yield return Make("glass_pane"); yield break; }
            if (rest == "wool" && color != "white") { yield return Make("white_wool"); yield break; }
        }
        // Shaped blocks of a stone-like material: deepslate_brick_stairs -> stone_brick_stairs -> cobblestone_stairs.
        foreach (var (suffix, fallbacks) in ShapeSuffixes)
        {
            if (!b.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string material = b[..^suffix.Length];
            if (Materials.TryGetValue(material, out var older)) yield return Make(older + suffix);
            foreach (var f in fallbacks) yield return Make(f);
            yield break;
        }
        if (IsMobHead(b))
        {
            yield return Make(b.Contains("wall") ? "skeleton_wall_skull" : "skeleton_skull");
            yield break;
        }
        if (b.EndsWith("_torch", StringComparison.Ordinal)) { yield return Make(b.Contains("wall") ? "wall_torch" : "torch"); yield break; }
        if (b.EndsWith("_sapling", StringComparison.Ordinal)) { yield return Make("oak_sapling"); yield break; }
        if (b.EndsWith("_leaves", StringComparison.Ordinal)) { yield return Make("oak_leaves"); yield break; }
        if (b.EndsWith("_ore", StringComparison.Ordinal)) { yield return Make("stone"); yield break; }
    }

    /// <summary>
    /// The next stand-in to try: the first candidate the target has, otherwise the first candidate (to continue the
    /// chain from), otherwise a generic replacement (air for non-solid blocks, nearest-coloured cube for solid ones).
    /// </summary>
    public static BlockState? Next(BlockState s, Func<string, bool> exists, Func<IEnumerable<string>>? cubes = null)
    {
        BlockState? first = null;
        foreach (var c in Candidates(s))
        {
            if (c.IsAir || exists(c.Name)) return c;
            first ??= c;
        }
        if (first != null) return first;
        return Generic(s, exists, cubes);
    }

    static BlockState Generic(BlockState s, Func<string, bool> exists, Func<IEnumerable<string>>? cubes)
    {
        var def = JavaBlockData.Latest.Blocks.GetValueOrDefault(s.Name);
        if (def != null && !def.Solid) return BlockState.Air;
        if (cubes != null)
        {
            uint want = BlockColors.Lookup(s).Color;
            string? best = null;
            double bestDist = double.MaxValue;
            foreach (var name in cubes())
            {
                if (!exists(name)) continue;
                uint c = BlockColors.Lookup(BlockState.Get(name)).Color;
                double d = ColorDistance(want, c);
                if (d < bestDist) { bestDist = d; best = name; }
            }
            if (best != null) return BlockState.Get(best);
        }
        return exists("minecraft:stone") ? BlockState.Get("minecraft:stone") : BlockState.Air;
    }

    static double ColorDistance(uint a, uint b)
    {
        int dr = (int)((a >> 16) & 255) - (int)((b >> 16) & 255);
        int dg = (int)((a >> 8) & 255) - (int)((b >> 8) & 255);
        int db = (int)(a & 255) - (int)(b & 255);
        // Weighted for perceived brightness.
        return 2 * dr * dr + 4 * dg * dg + 3 * db * db;
    }

    /// <summary>Plain opaque cubes of the newest version (candidates for colour matching).</summary>
    /// <summary>Mob heads and skulls ("piston_head" is not one).</summary>
    public static bool IsMobHead(string baseName) =>
        (baseName.EndsWith("_head", StringComparison.Ordinal) || baseName.EndsWith("_skull", StringComparison.Ordinal)) && baseName != "piston_head";

    public static IEnumerable<string> PlainCubes() => JavaBlockData.Latest.Blocks.Values.Where(b => b.IsPlainCube).Select(b => b.Name);
}
