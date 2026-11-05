// MinecraftStudio.Core/World/StructureKinds.cs: every kind of structure the game generates, under one key.
// Owns: StructureKinds — the key for a structure's saved id in any version (minecraft:village_desert, Village, Jungle_Pyramid,
// TeDP, EndCity …; a data pack's or mod's own ids are kept as they are), for the loot table of an unopened chest, barrel,
// dispenser or brushable block in Java or Bedrock (chests/buried_treasure, loot_tables/chests/buriedtreasure.json …) and for
// the mob of a spawner; its plain name ("Desert village"), its colour on the map, and how far apart two finds of it can be
// and still be the same structure.
namespace MinecraftStudio.Core.World;

public static class StructureKinds
{
    // Spread: how far apart finds of one structure can lie (its size); two of a kind are generated hundreds of blocks apart.
    sealed record Kind(string Name, uint Color, int Spread);

    static readonly Dictionary<string, Kind> Kinds = new(StringComparer.Ordinal)
    {
        ["village"] = new("Village", 0xFFD79650, 128),
        ["pillager_outpost"] = new("Pillager outpost", 0xFFC85A50, 48),
        ["shipwreck"] = new("Shipwreck", 0xFFAA7846, 24),
        ["buried_treasure"] = new("Buried treasure", 0xFFFFCD32, 4),
        ["desert_pyramid"] = new("Desert temple", 0xFFE6CD82, 24),
        ["jungle_pyramid"] = new("Jungle temple", 0xFF6EAA46, 24),
        ["igloo"] = new("Igloo", 0xFFBEE1FF, 16),
        ["swamp_hut"] = new("Witch hut", 0xFF9664C8, 16),
        ["stronghold"] = new("Stronghold", 0xFFAAAAB9, 128),
        ["monument"] = new("Ocean monument", 0xFF5AC8AF, 64),
        ["ocean_ruin"] = new("Ocean ruins", 0xFF50A5CD, 32),
        ["mansion"] = new("Woodland mansion", 0xFF8C5F37, 96),
        ["mineshaft"] = new("Mineshaft", 0xFFA07D5A, 112),
        ["fortress"] = new("Nether fortress", 0xFFAF3741, 128),
        ["nether_fossil"] = new("Nether fossil", 0xFFE1E1CD, 16),
        ["bastion_remnant"] = new("Bastion remnant", 0xFFD7AF3C, 64),
        ["ruined_portal"] = new("Ruined portal", 0xFF9650DC, 16),
        // Not generated: the Nether portals players built (Portals), each with where it leads.
        ["nether_portal"] = new("Nether portal (built)", 0xFFC86EFF, 8),
        ["end_city"] = new("End city", 0xFFCD96E1, 96),
        ["ancient_city"] = new("Ancient city", 0xFF3C828C, 128),
        ["trail_ruins"] = new("Trail ruins", 0xFFC8734B, 32),
        ["trial_chambers"] = new("Trial chambers", 0xFFD7824B, 128),
        ["dungeon"] = new("Dungeon", 0xFF6E915F, 8),
        ["desert_well"] = new("Desert well", 0xFFE6CD82, 4),
    };

    /// <summary>Every kind the game generates, in the order a list shows them.</summary>
    public static IReadOnlyCollection<string> All => Kinds.Keys;

    /// <summary>The plain name of a kind ("Woodland mansion"); a data pack's or mod's own id in words.</summary>
    public static string Name(string key) => Kinds.TryGetValue(key, out var k) ? k.Name : Capital(WorldSearch.Nice(key.Replace(':', ' ')));

    /// <summary>Its colour on the map (ARGB); grey for a data pack's or mod's own.</summary>
    public static uint Color(string key) => Kinds.TryGetValue(key, out var k) ? k.Color : 0xFFB4B4B4;

    /// <summary>How far apart (blocks) two finds of the kind can lie and still be one structure.</summary>
    public static int Spread(string key) => Kinds.TryGetValue(key, out var k) ? k.Spread : 32;

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// The key and the name with its variant ("Desert village", "Ruined portal (ocean)") for a structure's saved id, any
    /// version; null for an empty start ("INVALID").
    /// </summary>
    public static (string Key, string Name)? FromStructureId(string id)
    {
        if (id.Length == 0 || id == "INVALID") return null;
        string ns = id.Contains(':') ? id[..id.IndexOf(':')] : "minecraft";
        string n = (id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id).ToLowerInvariant();
        if (ns != "minecraft") return (id.ToLowerInvariant(), Name(id.ToLowerInvariant()));
        switch (n)
        {
            case "tedp": n = "desert_pyramid"; break;
            case "tejp": n = "jungle_pyramid"; break;
            case "tesh": n = "swamp_hut"; break;
            case "iglu": n = "igloo"; break;
            case "endcity": n = "end_city"; break;
            case "buriedtreasure": n = "buried_treasure"; break;
        }
        foreach (var (prefix, key) in Variants)
            if (n.StartsWith(prefix, StringComparison.Ordinal) && n.Length > prefix.Length)
            {
                string variant = n[prefix.Length..].Replace('_', ' ');
                return key == "village" ? (key, Capital(variant) + " village")
                    : (key, $"{Name(key)} ({(variant == "mesa" ? "badlands" : variant)})");
            }
        return Kinds.ContainsKey(n) ? (n, Name(n)) : ("minecraft:" + n, Name("minecraft:" + n));
    }

    static readonly (string Prefix, string Key)[] Variants =
    [
        ("village_", "village"), ("ruined_portal_", "ruined_portal"), ("ocean_ruin_", "ocean_ruin"), ("mineshaft_", "mineshaft"), ("shipwreck_", "shipwreck"),
    ];

    /// <summary>The kind an unopened container's loot table belongs to (Java "chests/…" or "archaeology/…", Bedrock "loot_tables/chests/….json").</summary>
    public static string? FromLootTable(string table)
    {
        string t = table.ToLowerInvariant();
        foreach (var (part, key) in LootParts) if (t.Contains(part, StringComparison.Ordinal)) return key;
        return null;
    }

    static readonly (string Part, string Key)[] LootParts =
    [
        ("buried_treasure", "buried_treasure"), ("buriedtreasure", "buried_treasure"), ("shipwreck", "shipwreck"), ("desert_pyramid", "desert_pyramid"),
        ("desert_well", "desert_well"), ("jungle_temple", "jungle_pyramid"), ("igloo", "igloo"), ("stronghold", "stronghold"),
        ("woodland_mansion", "mansion"), ("end_city", "end_city"), ("nether_bridge", "fortress"), ("bastion", "bastion_remnant"),
        ("ancient_city", "ancient_city"), ("pillager_outpost", "pillager_outpost"), ("ruined_portal", "ruined_portal"),
        ("underwater_ruin", "ocean_ruin"), ("ocean_ruin", "ocean_ruin"), ("trail_ruins", "trail_ruins"), ("trial_chamber", "trial_chambers"),
        ("abandoned_mineshaft", "mineshaft"), ("simple_dungeon", "dungeon"), ("village", "village"),
    ];

    /// <summary>The kind whose spawner makes this mob (cave spiders: a mineshaft, blazes: a fortress …), else null (a player's own).</summary>
    public static string? FromSpawner(string mob)
    {
        string m = mob.ToLowerInvariant();
        if (m.StartsWith("minecraft:", StringComparison.Ordinal)) m = m[10..];
        return m switch
        {
            "cave_spider" => "mineshaft",
            "blaze" => "fortress",
            "silverfish" => "stronghold",
            "magma_cube" => "bastion_remnant",
            "zombie" or "skeleton" or "spider" => "dungeon",
            _ => null,
        };
    }
}
