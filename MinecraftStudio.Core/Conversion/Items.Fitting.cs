// MinecraftStudio.Core/Conversion/Items.Fitting.cs: fitting for Items.
// Part of Items (main part in Items.cs).
using System.Collections.Concurrent;
using System.Text.Json;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Items
{
    // ---------------------------------------------------------------- what a target can hold

    /// <summary>Items added in Java 1.9–1.12 (legacy level), for targets older than the 1.12 numeric table.</summary>
    static readonly Lazy<Dictionary<string, double>> LegacySince = new(() =>
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        void Add(double level, params string[] names) { foreach (var n in names) d[Ns(n)] = level; }
        Add(1.09, "elytra", "shield", "end_crystal", "chorus_fruit", "popped_chorus_fruit", "chorus_flower", "chorus_plant", "beetroot", "beetroot_seeds",
            "beetroot_soup", "dragon_breath", "lingering_potion", "spectral_arrow", "tipped_arrow", "end_rod", "purpur_block", "purpur_pillar", "purpur_stairs",
            "purpur_slab", "end_stone_bricks", "structure_block", "dirt_path", "repeating_command_block", "chain_command_block", "dragon_head",
            "spruce_boat", "birch_boat", "jungle_boat", "acacia_boat", "dark_oak_boat");
        Add(1.10, "magma_block", "nether_wart_block", "red_nether_bricks", "bone_block", "structure_void");
        Add(1.11, "observer", "totem_of_undying", "shulker_shell", "iron_nugget");
        Add(1.12, "knowledge_book");
        foreach (var color in StateEdit.Colors)
        {
            Add(1.11, color + "_shulker_box");
            Add(1.12, color + "_glazed_terracotta", color + "_concrete", color + "_concrete_powder");
        }
        return d;
    });

    /// <summary>Which items a target has, and what it uses for a block item it lacks.</summary>
    sealed class ItemRules
    {
        public required string Version { get; init; }
        public required Func<string, bool> Knows { get; init; }
        public Func<string, string?> StandIn { get; init; } = _ => null;
        /// <summary>Java targets with string ids keep modded items (the world may be loaded with the mod).</summary>
        public bool KeepsModded { get; init; }
    }

    static readonly ConcurrentDictionary<int, ItemRules> JavaRulesCache = new();

    /// <summary>1.13+ Java: the version's item list (renames undone); block items fall back to that version's block.</summary>
    static ItemRules JavaRules(int dv) => JavaRulesCache.GetOrAdd(dv, v =>
    {
        var list = JavaBlockData.For(v);
        return new ItemRules
        {
            Version = "Java " + list.Name,
            Knows = id => list.Items.Count == 0 || list.Items.Contains(Renamed(id, v)),
            StandIn = id => JavaBlockData.Latest.Blocks.ContainsKey(id) ? CanonicalName(JavaBlocks.ToVersion(JavaBlocks.Canonicalize(World.BlockState.Get(id)), v).Name) : null,
            KeepsModded = true,
        };
    });

    /// <summary>Java up to 1.12 and Legacy Console: the 1.12 numeric table, limited to what existed at the target's level.</summary>
    static ItemRules LegacyRules(bool numeric)
    {
        var target = ConversionLog.Current?.Target;
        double level = target?.LegacyLevel ?? LegacyLevel.Release_1_12;
        return new ItemRules
        {
            Version = target?.Name ?? "Java 1.12",
            Knows = id => LegacyLookup(id).Exact && (!LegacySince.Value.TryGetValue(id, out var since) || level >= since),
            StandIn = id => JavaBlockData.Latest.Blocks.ContainsKey(id) && LegacyLookup(id) is { Exact: false } hit && JavaLegacyData.ItemLookup(hit.Id, hit.Data) is string n ? CanonicalName(n) : null,
            KeepsModded = !numeric && target?.Kind != TargetKind.LegacyConsole,
        };
    }

    /// <summary>Bedrock: items with a Bedrock counterpart that existed in the target (its Java content version).</summary>
    static ItemRules BedrockRules(ConversionTarget? t)
    {
        var g = Geyser.Value;
        if (t is { ContentDataVersion: > 0 })
        {
            var java = JavaRules(t.ContentDataVersion);
            return new ItemRules { Version = t.Name, Knows = id => g.JavaToBedrock.ContainsKey(id) && java.Knows(id), StandIn = java.StandIn };
        }
        if (t is { BedrockTerrain: not BedrockTerrain.States })
        {
            // Pocket Edition and Bedrock before 1.4: about the content of Java 1.12 (0.9–0.16: Java 1.8).
            var legacy = LegacyRules(numeric: true);
            return new ItemRules { Version = t.Name, Knows = id => g.JavaToBedrock.ContainsKey(id) && legacy.Knows(id), StandIn = legacy.StandIn };
        }
        return new ItemRules { Version = t?.Name ?? "Bedrock", Knows = id => g.JavaToBedrock.ContainsKey(id), KeepsModded = ConversionLog.Current?.KeepAddOns == true };
    }

    /// <summary>
    /// What a target can hold of a canonical item: the item itself, the item that version used instead (the stand-in of a
    /// block it lacks), or a placeholder named after the item. Every change goes into the conversion log.
    /// </summary>
    static NbtCompound Fit(NbtCompound canonical, ItemRules rules)
    {
        string id = Id(canonical);
        if (id == "minecraft:air" || rules.Knows(id)) return canonical;
        int count = Count(canonical);
        string version = ConversionLog.Current?.Target.Name ?? rules.Version;
        if (!id.StartsWith("minecraft:", StringComparison.Ordinal))
        {
            if (rules.KeepsModded)
            {
                ConversionLog.Note(LossKind.Item, id, "kept as it is: modded item, the world needs that mod or the game deletes it", count);
                return canonical;
            }
            ConversionLog.Note(LossKind.Item, id, $"became a placeholder (paper named after it): {version} cannot hold modded items", count);
            return Placeholder(canonical, $"Modded item, not in {version}");
        }
        // A stand-in must be a real item: never air (that deletes the stack) or plain paper (that hides what it was).
        if (rules.StandIn(id) is string stand && stand != id && stand is not ("minecraft:air" or "minecraft:cave_air" or "minecraft:void_air" or "minecraft:paper")
            && rules.Knows(stand))
        {
            ConversionLog.Note(LossKind.Item, id, $"became {stand}: {version} has no {ConversionLog.Friendly(id)}", count);
            var c = (NbtCompound)canonical.Clone();
            c.SetString("id", stand);
            return c;
        }
        ConversionLog.Note(LossKind.Item, id, $"became a placeholder (paper named after it): {version} has no such item", count);
        return Placeholder(canonical, $"Not in {version}");
    }

    /// <summary>
    /// Paper named after an item a version does not have, with lore saying what it was. The original stays in the item's
    /// custom data, so converting the world to a version that has the item brings it back. Items stored inside it (a
    /// shulker box or bundle the version lacks) go to the rescue chests so they stay usable.
    /// </summary>
    public static NbtCompound Placeholder(NbtCompound canonical, string reason)
    {
        string id = Id(canonical);
        var original = (NbtCompound)canonical.Clone();
        original.Remove("Slot");
        if (Components(original) is NbtCompound oc)
        {
            var inner = new List<NbtCompound>();
            foreach (var e in oc.GetList("minecraft:container")?.OfType<NbtCompound>() ?? [])
                if (e.GetCompound("item") is NbtCompound it) inner.Add(it);
            inner.AddRange(oc.GetList("minecraft:bundle_contents")?.OfType<NbtCompound>() ?? []);
            if (inner.Count > 0 && ConversionLog.Rescue($"Contents of a {ConversionLog.Friendly(id)}", inner))
            {
                oc.Remove("minecraft:container");
                oc.Remove("minecraft:bundle_contents");
            }
        }
        string title = ConversionLog.Friendly(id);
        title = title.Length == 0 ? id : char.ToUpperInvariant(title[0]) + title[1..];
        var comp = new NbtCompound
        {
            { "minecraft:custom_name", new NbtString(TextComponents.FromPlain(title)) },
            { "minecraft:lore", new NbtList(NbtType.String) { new NbtString(TextComponents.FromPlain("Was " + id)), new NbtString(TextComponents.FromPlain(reason)) } },
            { "minecraft:custom_data", new NbtCompound { { "mss_original", original } } },
        };
        return Make("minecraft:paper", Count(canonical), comp);
    }

    /// <summary>The item a placeholder stands for, when the placeholder was made by this converter.</summary>
    static NbtCompound? Restore(NbtCompound comp, int count)
    {
        if (comp.GetCompound("minecraft:custom_data")?.GetCompound("mss_original") is not NbtCompound original || original.GetString("id") is null) return null;
        var c = (NbtCompound)original.Clone();
        c.SetInt("count", Math.Max(1, count));
        return c;
    }

    /// <summary>A numbered item no vanilla version has (a mod's id in an old world): a named placeholder instead of nothing.</summary>
    static NbtCompound UnknownNumbered(NbtTag idTag, string? stringId, int damage, int count)
    {
        string what = stringId ?? $"item #{idTag.AsLong}" + (damage != 0 ? $":{damage}" : "");
        ConversionLog.Note(LossKind.Item, what, "unknown item (from a mod?): became a placeholder (paper named after it)", count);
        var comp = new NbtCompound
        {
            { "minecraft:custom_name", new NbtString(TextComponents.FromPlain("Unknown item " + what)) },
            { "minecraft:lore", new NbtList(NbtType.String) { new NbtString(TextComponents.FromPlain("No vanilla version has this item")) } },
        };
        return Make("minecraft:paper", count, comp);
    }

    /// <summary>The name an item had in a data version (renames undone).</summary>
    static string Renamed(string canonical, int dv)
    {
        string n = canonical;
        foreach (var (oldName, newName, since) in Renames)
            if (n == newName && dv < since) n = oldName;
        return n;
    }

    /// <summary>Components no conversion to this layout carries (custom item data, adventure rules ...): reported, not hidden.</summary>
    static void ReportUnmapped(NbtCompound? comp, IReadOnlySet<string> handled, string version)
    {
        if (comp == null) return;
        foreach (var (key, value) in comp)
        {
            if (handled.Contains(key)) continue;
            // The original of a placeholder: Bedrock cannot keep it, the placeholder's name and lore say what it was.
            if (key == "minecraft:custom_data" && value is NbtCompound cd && cd.Count == 1 && cd.ContainsKey("mss_original")) continue;
            ConversionLog.Note(LossKind.Item, "item data " + key, $"not carried over: {version} stores no such item data", lost: true);
        }
    }

    static readonly HashSet<string> TagHandled =
    [
        "minecraft:custom_data", "minecraft:damage", "minecraft:custom_name", "minecraft:lore", "minecraft:dyed_color", "minecraft:enchantments",
        "minecraft:stored_enchantments", "minecraft:potion_contents", "minecraft:unbreakable", "minecraft:repair_cost", "minecraft:map_id",
        "minecraft:container", "minecraft:written_book_content", "minecraft:writable_book_content", "minecraft:profile", "minecraft:trim",
        "minecraft:custom_model_data", "minecraft:fireworks", "minecraft:firework_explosion", "minecraft:banner_patterns", "minecraft:base_color",
        "minecraft:entity_data", "minecraft:block_entity_data", "minecraft:bucket_entity_data", "minecraft:instrument", "minecraft:pot_decorations",
        "minecraft:tooltip_display", "minecraft:max_damage", "minecraft:max_stack_size", "minecraft:rarity", "minecraft:enchantment_glint_override",
        "minecraft:map_color", "minecraft:map_decorations", "minecraft:recipes", "minecraft:lodestone_tracker", "minecraft:item_name",
    ];

    static readonly HashSet<string> BedrockHandled =
    [
        "minecraft:damage", "minecraft:custom_name", "minecraft:lore", "minecraft:dyed_color", "minecraft:enchantments", "minecraft:stored_enchantments",
        "minecraft:potion_contents", "minecraft:unbreakable", "minecraft:repair_cost", "minecraft:map_id", "minecraft:container",
        "minecraft:written_book_content", "minecraft:writable_book_content", "minecraft:tooltip_display", "minecraft:max_damage", "minecraft:max_stack_size",
        "minecraft:rarity", "minecraft:enchantment_glint_override", "minecraft:map_color", "minecraft:map_decorations", "minecraft:item_name",
        "minecraft:fireworks", "minecraft:trim", "minecraft:banner_patterns", "minecraft:base_color",
    ];
}
