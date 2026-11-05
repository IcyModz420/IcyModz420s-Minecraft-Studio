// MinecraftStudio.Core/Conversion/Items.cs: Item stacks across editions.
// Owns: Items.
using System.Collections.Concurrent;
using System.Text.Json;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Item stacks across editions. Canonical form = newest Java: {id, count (Int), components{...}} with
/// minecraft:damage, custom_name, lore, enchantments, stored_enchantments, potion_contents, dyed_color, unbreakable,
/// repair_cost, container, written/writable book contents and custom_data (anything else).
/// </summary>
public static partial class Items
{
    // ---------------------------------------------------------------- tables

    /// <summary>Item renames after the flattening: (old, new, first data version with the new name).</summary>
    static readonly (string Old, string New, int Since)[] Renames =
    [
        ("minecraft:cactus_green", "minecraft:green_dye", 1933), ("minecraft:dandelion_yellow", "minecraft:yellow_dye", 1933),
        ("minecraft:rose_red", "minecraft:red_dye", 1933), ("minecraft:sign", "minecraft:oak_sign", 1933),
        ("minecraft:zombie_pigman_spawn_egg", "minecraft:zombified_piglin_spawn_egg", 2510), ("minecraft:grass_path", "minecraft:dirt_path", 2681),
        ("minecraft:pottery_shard_archer", "minecraft:archer_pottery_sherd", 3443), ("minecraft:pottery_shard_arms_up", "minecraft:arms_up_pottery_sherd", 3443),
        ("minecraft:pottery_shard_prize", "minecraft:prize_pottery_sherd", 3443), ("minecraft:pottery_shard_skull", "minecraft:skull_pottery_sherd", 3443),
        ("minecraft:grass", "minecraft:short_grass", 3691), ("minecraft:scute", "minecraft:turtle_scute", 3807), ("minecraft:chain", "minecraft:iron_chain", 4536),
    ];

    static readonly Dictionary<string, string> RenameToNew = Renames.ToDictionary(r => r.Old, r => r.New, StringComparer.Ordinal);

    /// <summary>Java ≤1.12 numeric enchantment ids.</summary>
    static readonly Dictionary<int, string> JavaLegacyEnchantments = new()
    {
        [0] = "protection", [1] = "fire_protection", [2] = "feather_falling", [3] = "blast_protection", [4] = "projectile_protection", [5] = "respiration",
        [6] = "aqua_affinity", [7] = "thorns", [8] = "depth_strider", [9] = "frost_walker", [10] = "binding_curse", [16] = "sharpness", [17] = "smite",
        [18] = "bane_of_arthropods", [19] = "knockback", [20] = "fire_aspect", [21] = "looting", [22] = "sweeping", [32] = "efficiency", [33] = "silk_touch",
        [34] = "unbreaking", [35] = "fortune", [48] = "power", [49] = "punch", [50] = "flame", [51] = "infinity", [61] = "luck_of_the_sea", [62] = "lure",
        [65] = "loyalty", [66] = "impaling", [67] = "riptide", [68] = "channeling", [70] = "mending", [71] = "vanishing_curse",
    };

    /// <summary>Bedrock enchantment ids.</summary>
    static readonly string[] BedrockEnchantments =
    [
        "protection", "fire_protection", "feather_falling", "blast_protection", "projectile_protection", "thorns", "respiration", "depth_strider",
        "aqua_affinity", "sharpness", "smite", "bane_of_arthropods", "knockback", "fire_aspect", "looting", "efficiency", "silk_touch", "unbreaking",
        "fortune", "power", "punch", "flame", "infinity", "luck_of_the_sea", "lure", "frost_walker", "mending", "binding_curse", "vanishing_curse",
        "impaling", "riptide", "loyalty", "channeling", "multishot", "piercing", "quick_charge", "soul_speed", "swift_sneak", "wind_burst", "density", "breach",
    ];

    /// <summary>Bedrock potion aux values.</summary>
    static readonly string[] BedrockPotions =
    [
        "water", "mundane", "long_mundane", "thick", "awkward", "night_vision", "long_night_vision", "invisibility", "long_invisibility", "leaping",
        "long_leaping", "strong_leaping", "fire_resistance", "long_fire_resistance", "swiftness", "long_swiftness", "strong_swiftness", "slowness",
        "long_slowness", "water_breathing", "long_water_breathing", "healing", "strong_healing", "harming", "strong_harming", "poison", "long_poison",
        "strong_poison", "regeneration", "long_regeneration", "strong_regeneration", "strength", "long_strength", "strong_strength", "weakness",
        "long_weakness", "wither", "turtle_master", "long_turtle_master", "strong_turtle_master", "slow_falling", "long_slow_falling", "strong_slowness",
        "wind_charged", "weaving", "oozing", "infested",
    ];

    /// <summary>Java ≤1.12 (and Legacy Console) enchantment number → name, and back.</summary>
    public static string? JavaLegacyEnchantmentName(int id) => JavaLegacyEnchantments.TryGetValue(id, out var n) ? n : null;
    public static int? JavaLegacyEnchantmentNumber(string name)
    {
        string bare = Bare(name) == "sweeping_edge" ? "sweeping" : Bare(name);
        foreach (var (id, n) in JavaLegacyEnchantments) if (n == bare) return id;
        return null;
    }

    /// <summary>Bedrock enchantment number → name, and back.</summary>
    public static string? BedrockEnchantmentName(int id) => id >= 0 && id < BedrockEnchantments.Length ? BedrockEnchantments[id] : null;
    public static int? BedrockEnchantmentNumber(string name) => Array.IndexOf(BedrockEnchantments, Bare(name)) is int i && i >= 0 ? i : null;

    /// <summary>Every enchantment name the tables know (without the minecraft: prefix), for pickers.</summary>
    public static IReadOnlyList<string> EnchantmentNames => EnchantmentList.Value;
    static readonly Lazy<List<string>> EnchantmentList = new(() => JavaLegacyEnchantments.Values.Concat(BedrockEnchantments).Append("sweeping_edge")
        .Where(n => n != "sweeping").Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList());

    /// <summary>Every current Java item id and every Bedrock item name (minecraft:…), for pickers.</summary>
    public static IReadOnlyList<string> AllJavaItems => JavaItemList.Value;
    public static IReadOnlyList<string> AllBedrockItems => BedrockItemList.Value;
    static readonly Lazy<List<string>> JavaItemList = new(() => Geyser!.Value.JavaToBedrock.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
    static readonly Lazy<List<string>> BedrockItemList = new(() => Geyser!.Value.JavaToBedrock.Values.Select(v => v.Bedrock).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList());

    static string Bare(string name) => name.StartsWith("minecraft:", StringComparison.Ordinal) ? name[10..] : name;

    sealed class GeyserData
    {
        public readonly Dictionary<string, (string Bedrock, int Data, bool Block)> JavaToBedrock = new(StringComparer.Ordinal);
        public readonly Dictionary<(string, int), string> BedrockToJava = [];
        public readonly Dictionary<string, string> BedrockAnyData = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> LegacyBedrockNames = new(StringComparer.Ordinal);
        public readonly Dictionary<(string, int), string> LegacyBedrockComplex = [];
        /// <summary>Current Bedrock item name → the 1.16 name (and data value) it had before the item flattening.</summary>
        public readonly Dictionary<string, (string Name, int Data)> CurrentToLegacy = new(StringComparer.Ordinal);
    }

    static readonly Lazy<GeyserData> Geyser = new(() =>
    {
        var g = new GeyserData();
        using (var doc = JsonDocument.Parse(EmbeddedData.Bytes("geyser_items.json.gz")))
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                string bedrock = p.Value.TryGetProperty("bedrock_identifier", out var b) ? b.GetString() ?? p.Name : p.Name;
                int data = p.Value.TryGetProperty("bedrock_data", out var d) ? d.GetInt32() : 0;
                bool block = p.Value.TryGetProperty("is_block", out var ib) && ib.GetBoolean();
                g.JavaToBedrock[p.Name] = (bedrock, data, block);
                g.BedrockToJava.TryAdd((bedrock, data), p.Name);
                g.BedrockAnyData.TryAdd(bedrock, p.Name);
            }
        using (var doc = JsonDocument.Parse(EmbeddedData.Bytes("r16_to_current_item_map.json.gz")))
        {
            if (doc.RootElement.TryGetProperty("simple", out var simple))
                foreach (var p in simple.EnumerateObject()) g.LegacyBedrockNames[p.Name] = p.Value.GetString() ?? p.Name;
            if (doc.RootElement.TryGetProperty("complex", out var complex))
                foreach (var p in complex.EnumerateObject())
                    foreach (var m in p.Value.EnumerateObject())
                        if (int.TryParse(m.Name, out int meta)) g.LegacyBedrockComplex[(p.Name, meta)] = m.Value.GetString() ?? p.Name;
        }
        foreach (var ((old, meta), current) in g.LegacyBedrockComplex) g.CurrentToLegacy.TryAdd(current, (old, meta));
        foreach (var (old, current) in g.LegacyBedrockNames) g.CurrentToLegacy.TryAdd(current, (old, -1));
        return g;
    });

    static string Ns(string id) => id.Contains(':') ? id : "minecraft:" + id;

    /// <summary>Newest Java name for an item name of any 1.13+ version.</summary>
    public static string CanonicalName(string id)
    {
        id = Ns(id);
        return RenameToNew.TryGetValue(id, out var n) ? n : id;
    }

    /// <summary>
    /// Item name to write for a 1.13+ Java target (renames undone; a vanilla item the version lacks becomes the closest older
    /// item). A mod's item keeps its id: Fit keeps it for Java (the world is loaded with the mod), and the vanilla list
    /// never has it — making it paper here lost it without a trace.
    /// </summary>
    public static string JavaName(string canonical, int dataVersion)
    {
        if (!canonical.StartsWith("minecraft:", StringComparison.Ordinal) && canonical.Contains(':')) return canonical;
        string n = canonical;
        foreach (var (oldName, newName, since) in Renames)
            if (n == newName && dataVersion < since) n = oldName;
        var list = JavaBlockData.For(dataVersion);
        if (list.Items.Count > 0 && !list.Items.Contains(n))
        {
            // A block item: use the block stand-in of that version; otherwise an inert item.
            if (JavaBlockData.Latest.Blocks.ContainsKey(n))
            {
                var stand = JavaBlocks.ToVersion(JavaBlocks.Canonicalize(World.BlockState.Get(n)), dataVersion);
                if (list.Items.Contains(stand.Name)) return stand.Name;
            }
            return list.Items.Contains("minecraft:paper") ? "minecraft:paper" : n;
        }
        return n;
    }

    static NbtCompound Make(string id, int count, NbtCompound? components)
    {
        var c = new NbtCompound();
        c.SetString("id", id);
        c.SetInt("count", Math.Max(1, count));
        if (components is { Count: > 0 }) c["components"] = components;
        return c;
    }

    public static string Id(NbtCompound canonical) => canonical.GetString("id") ?? "minecraft:air";
    public static int Count(NbtCompound canonical) => canonical.GetInt("count", 1);
    public static NbtCompound? Components(NbtCompound canonical) => canonical.GetCompound("components");
}
