// MinecraftStudio.Core/Conversion/BlockEntities.cs: Block entities across editions. Canonical form = newest
// Java layout with namespaced ids, absolute x/y/z, canonical items (with Slot) and canonical text (JSON
// strings).
// Owns: BlockEntities.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Block entities across editions. Canonical form = newest Java layout with namespaced ids, absolute x/y/z,
/// canonical items (with Slot) and canonical text (JSON strings).
/// </summary>
public static partial class BlockEntities
{
    // canonical Java id, pre-1.11 Java id, Bedrock id
    static readonly (string Java, string Old, string Bedrock)[] Ids =
    [
        ("minecraft:chest", "Chest", "Chest"), ("minecraft:trapped_chest", "Chest", "Chest"), ("minecraft:ender_chest", "EnderChest", "EnderChest"),
        ("minecraft:furnace", "Furnace", "Furnace"), ("minecraft:blast_furnace", "", "BlastFurnace"), ("minecraft:smoker", "", "Smoker"),
        ("minecraft:hopper", "Hopper", "Hopper"), ("minecraft:dispenser", "Trap", "Dispenser"), ("minecraft:dropper", "Dropper", "Dropper"),
        ("minecraft:brewing_stand", "Cauldron", "BrewingStand"), ("minecraft:sign", "Sign", "Sign"), ("minecraft:hanging_sign", "", "HangingSign"),
        ("minecraft:mob_spawner", "MobSpawner", "MobSpawner"), ("minecraft:jukebox", "RecordPlayer", "Jukebox"), ("minecraft:beacon", "Beacon", "Beacon"),
        ("minecraft:skull", "Skull", "Skull"), ("minecraft:banner", "Banner", "Banner"), ("minecraft:bed", "Bed", "Bed"),
        ("minecraft:enchanting_table", "EnchantTable", "EnchantTable"), ("minecraft:end_portal", "Airportal", "EndPortal"),
        ("minecraft:end_gateway", "EndGateway", "EndGateway"), ("minecraft:command_block", "Control", "CommandBlock"),
        ("minecraft:structure_block", "Structure", "StructureBlock"), ("minecraft:daylight_detector", "DLDetector", "DaylightDetector"),
        ("minecraft:comparator", "Comparator", "Comparator"), ("minecraft:shulker_box", "", "ShulkerBox"), ("minecraft:barrel", "", "Barrel"),
        ("minecraft:lectern", "", "Lectern"), ("minecraft:bell", "", "Bell"), ("minecraft:campfire", "", "Campfire"), ("minecraft:beehive", "", "Beehive"),
        ("minecraft:conduit", "", "Conduit"), ("minecraft:jigsaw", "", "JigsawBlock"), ("minecraft:sculk_sensor", "", "SculkSensor"),
        ("minecraft:calibrated_sculk_sensor", "", "CalibratedSculkSensor"), ("minecraft:sculk_catalyst", "", "SculkCatalyst"),
        ("minecraft:sculk_shrieker", "", "SculkShrieker"), ("minecraft:chiseled_bookshelf", "", "ChiseledBookshelf"),
        ("minecraft:decorated_pot", "", "DecoratedPot"), ("minecraft:brushable_block", "", "BrushableBlock"), ("minecraft:crafter", "", "Crafter"),
        ("minecraft:trial_spawner", "", "TrialSpawner"), ("minecraft:vault", "", "Vault"), ("minecraft:creaking_heart", "", "CreakingHeart"),
        ("minecraft:piston", "Piston", "PistonArm"), ("minecraft:note_block", "Music", "Music"), ("minecraft:flower_pot", "FlowerPot", "FlowerPot"),
    ];

    static readonly Dictionary<string, string> FromOld = Ids.Where(i => i.Old.Length > 0).GroupBy(i => i.Old).ToDictionary(g => g.Key, g => g.First().Java, StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> FromBedrockId = Ids.GroupBy(i => i.Bedrock).ToDictionary(g => g.Key, g => g.First().Java, StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> ToBedrockId = Ids.ToDictionary(i => i.Java, i => i.Bedrock, StringComparer.Ordinal);
    static readonly Dictionary<string, string> ToOldId = Ids.Where(i => i.Old.Length > 0).ToDictionary(i => i.Java, i => i.Old, StringComparer.Ordinal);

    // 1.11-1.12 namespaced ids that differ from the newest ones.
    static readonly Dictionary<string, string> Renamed112 = new(StringComparer.Ordinal)
    {
        ["minecraft:noteblock"] = "minecraft:note_block", ["minecraft:enchanting_table"] = "minecraft:enchanting_table",
    };

    static readonly string[] BannerCodes =
    [
        "b:base", "bl:square_bottom_left", "br:square_bottom_right", "tl:square_top_left", "tr:square_top_right", "bs:stripe_bottom", "ts:stripe_top",
        "ls:stripe_left", "rs:stripe_right", "cs:stripe_center", "ms:stripe_middle", "drs:stripe_downright", "dls:stripe_downleft", "ss:small_stripes",
        "cr:cross", "sc:straight_cross", "bt:triangle_bottom", "tt:triangle_top", "bts:triangles_bottom", "tts:triangles_top", "ld:diagonal_left",
        "rd:diagonal_up_right", "lud:diagonal_up_left", "rud:diagonal_right", "mc:circle", "mr:rhombus", "vh:half_vertical", "hh:half_horizontal",
        "vhr:half_vertical_right", "hhb:half_horizontal_bottom", "bo:border", "cbo:curly_border", "gra:gradient", "gru:gradient_up", "bri:bricks",
        "glb:globe", "cre:creeper", "sku:skull", "flo:flower", "moj:mojang", "pig:piglin", "flw:flow", "gus:guster",
    ];

    static readonly Dictionary<string, string> PatternByCode = BannerCodes.Select(s => s.Split(':')).ToDictionary(p => p[0], p => "minecraft:" + p[1]);
    static readonly Dictionary<string, string> CodeByPattern = BannerCodes.Select(s => s.Split(':')).ToDictionary(p => "minecraft:" + p[1], p => p[0]);

    public static string? PatternForCode(string code) => PatternByCode.GetValueOrDefault(code);
    public static string? CodeForPattern(string pattern) => CodeByPattern.GetValueOrDefault(pattern);

    /// <summary>Java data version each block entity type first appeared in (older types are not listed).</summary>
    static readonly Dictionary<string, int> Since = new(StringComparer.Ordinal)
    {
        ["minecraft:shulker_box"] = 819, ["minecraft:conduit"] = 1519, ["minecraft:barrel"] = 1952, ["minecraft:smoker"] = 1952,
        ["minecraft:blast_furnace"] = 1952, ["minecraft:lectern"] = 1952, ["minecraft:bell"] = 1952, ["minecraft:campfire"] = 1952,
        ["minecraft:jigsaw"] = 1952, ["minecraft:beehive"] = 2225, ["minecraft:sculk_sensor"] = 2724, ["minecraft:sculk_catalyst"] = 3105,
        ["minecraft:sculk_shrieker"] = 3105, ["minecraft:chiseled_bookshelf"] = 3463, ["minecraft:decorated_pot"] = 3463,
        ["minecraft:brushable_block"] = 3463, ["minecraft:calibrated_sculk_sensor"] = 3463, ["minecraft:hanging_sign"] = 3463,
        ["minecraft:crafter"] = 3953, ["minecraft:trial_spawner"] = 3953, ["minecraft:vault"] = 3953, ["minecraft:creaking_heart"] = 4189,
    };

    /// <summary>Whether a block entity type exists in a Java data version (or a Bedrock target's content version).</summary>
    public static bool KnownIn(string id, int dv) => !Since.TryGetValue(id, out int since) || dv >= since;

    /// <summary>The Java content version of a Bedrock target (block entity and mob types it has).</summary>
    static int BedrockContent(ConversionTarget? t) => t switch
    {
        { Kind: TargetKind.Bedrock, ContentDataVersion: > 0 } => t.ContentDataVersion,
        { Kind: TargetKind.Bedrock, BedrockTerrain: not BedrockTerrain.States } => 1343,
        _ => int.MaxValue,
    };

    /// <summary>Items a block entity holds (container slots, record, book, pot or suspicious block item).</summary>
    public static List<NbtCompound> CarriedItems(NbtCompound be)
    {
        var items = new List<NbtCompound>(be.GetList("Items")?.OfType<NbtCompound>() ?? []);
        foreach (var key in new[] { "RecordItem", "Book", "item" })
            if (be.GetCompound(key) is NbtCompound it) items.Add(it);
        return items.Where(i => !ConversionLog.IsEmptyItem(i)).ToList();
    }

    /// <summary>
    /// A block entity the target cannot hold: its items go to the rescue chests at the spawn and the loss is reported
    /// with its position. Returns null.
    /// </summary>
    public static NbtCompound? Drop(NbtCompound canonical, string why)
    {
        string id = canonical.GetString("id") ?? "?";
        string where = $"{canonical.GetInt("x")}, {canonical.GetInt("y")}, {canonical.GetInt("z")}";
        var items = CarriedItems(canonical);
        if (canonical.GetList("bees") is { Count: > 0 } bees) why += $" ({bees.Count} bee{(bees.Count == 1 ? "" : "s")} inside were lost)";
        if (canonical.GetString("LootTable") != null) why += " (an unopened loot chest)";
        if (items.Count == 0)
        {
            ConversionLog.Note(LossKind.Container, ConversionLog.Friendly(id), why, lost: canonical.GetList("bees") is { Count: > 0 }, where: where);
            return null;
        }
        bool rescued = ConversionLog.Rescue($"Items from a {ConversionLog.Friendly(id)}", items);
        ConversionLog.Note(LossKind.Container, ConversionLog.Friendly(id) + " with items",
            why + (rescued ? "; its items are in the rescue chests at the spawn" : "; its items were lost"), items.Count, lost: !rescued, where: where);
        return null;
    }

    static string Version(int dv) => ConversionLog.Current?.Target.Name ?? $"data version {dv}";

    static string Color(int woolIndex) => StateEdit.Colors[woolIndex & 15];
    static int ColorIndex(string? name) => Math.Max(0, Array.IndexOf(StateEdit.Colors, name ?? "white"));

    public static string CanonicalId(NbtCompound be)
    {
        string id = be.GetString("id") ?? be.GetString("Id") ?? "";
        if (FromOld.TryGetValue(id, out var j)) return j;
        if (!id.Contains(':')) id = "minecraft:" + id.ToLowerInvariant();
        return Renamed112.TryGetValue(id, out var r) ? r : id;
    }

    static NbtCompound Base(string id, NbtCompound source)
    {
        var c = new NbtCompound();
        c.SetString("id", id);
        c.SetInt("x", source.GetInt("x"));
        c.SetInt("y", source.GetInt("y"));
        c.SetInt("z", source.GetInt("z"));
        return c;
    }

    static NbtList ItemsFromJava(NbtList? list, int dv)
    {
        var result = new NbtList(NbtType.Compound);
        foreach (var it in list?.OfType<NbtCompound>() ?? [])
        {
            if (Items.FromJava(it, dv) is not NbtCompound c) continue;
            c.SetByte("Slot", (sbyte)it.GetInt("Slot"));
            result.Add(c);
        }
        return result;
    }

    static NbtList ItemsFromBedrock(NbtList? list)
    {
        var result = new NbtList(NbtType.Compound);
        foreach (var it in list?.OfType<NbtCompound>() ?? [])
        {
            if (Items.FromBedrock(it) is not NbtCompound c) continue;
            c.SetByte("Slot", (sbyte)it.GetInt("Slot"));
            result.Add(c);
        }
        return result;
    }
}
