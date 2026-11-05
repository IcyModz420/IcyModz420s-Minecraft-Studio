// MinecraftStudio.Core/Conversion/BiomeNames.cs: Biome naming across editions.
// Owns: BiomeNames.
using System.Reflection;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Biome naming across editions. Canonical names are the newest Java names; biomes that 1.18 merged away keep
/// their 1.13–1.17 name (so old → old conversions stay exact) and are folded only when a 1.18+ target needs it.
/// </summary>
public static class BiomeNames
{
    sealed class Entry
    {
        public string Name = "";              // canonical, namespaced
        public int? JavaId, BedrockId, ConsoleId;
        public string? MergedInto;            // 1.18+ replacement for removed biomes
    }

    sealed class Tables
    {
        public readonly Dictionary<string, Entry> ByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, Entry> ByJava = [], ByBedrock = [], ByConsole = [];
    }

    static readonly Lazy<Tables> Data = new(Load);

    static Tables Load()
    {
        var t = new Tables();
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftStudio.Data.biomes.csv");
        if (s == null) return t;
        using var r = new StreamReader(s);
        var cols = (r.ReadLine() ?? "").Split(',');
        int C(string n) => Array.IndexOf(cols, n);
        int cCur = C("java_current"), cNum = C("java_numeric_pre_1_18"), c112 = C("java_1_12_name"), c113 = C("java_1_13_to_1_17_name"),
            cMerged = C("merged_into_1_18"), cBid = C("bedrock_id"), cBname = C("bedrock_name"), cLce = C("lce_id");
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var p = Rendering.Biomes.SplitCsv(line);
            string Get(int i) => i >= 0 && i < p.Count ? p[i].Trim() : "";
            int? Int(int i) => int.TryParse(Get(i), out int v) ? v : null;
            string name = Get(cCur);
            if (name.Length == 0) name = Get(c113).Length > 0 ? Get(c113) : Get(c112);
            if (name.Length == 0) continue;
            var e = new Entry
            {
                Name = "minecraft:" + name,
                JavaId = Int(cNum),
                BedrockId = Int(cBid),
                ConsoleId = Int(cLce),
                MergedInto = Get(cMerged) is { Length: > 0 } m ? "minecraft:" + m.Replace("minecraft:", "") : null,
            };
            foreach (var alias in new[] { name, Get(c112), Get(c113), Get(cBname) })
                if (alias.Length > 0) t.ByName.TryAdd(alias.StartsWith("minecraft:") ? alias : "minecraft:" + alias, e);
            if (e.JavaId is int j) t.ByJava.TryAdd(j, e);
            if (e.BedrockId is int b) t.ByBedrock.TryAdd(b, e);
            if (e.ConsoleId is int c) t.ByConsole.TryAdd(c, e);
        }
        return t;
    }

    static string Ns(string name) => name.Contains(':') ? name : "minecraft:" + name;

    // ---------------------------------------------------------------- sources -> canonical

    public static string FromJavaName(string name) => Data.Value.ByName.TryGetValue(Ns(name), out var e) ? e.Name : Ns(name);
    public static string FromJavaId(int id) => Data.Value.ByJava.TryGetValue(id, out var e) ? e.Name : "minecraft:plains";
    /// <summary>Whether a Java biome number is one of the game's own (a mod's numbers are not).</summary>
    public static bool KnowsJavaId(int id) => Data.Value.ByJava.ContainsKey(id);
    public static string FromConsoleId(int id) =>
        Data.Value.ByConsole.TryGetValue(id, out var e) || Data.Value.ByJava.TryGetValue(id, out e) ? e.Name : "minecraft:plains";

    public static string FromBedrockId(int id)
    {
        if (Data.Value.ByBedrock.TryGetValue(id, out var e)) return e.Name;
        return BiomeMap.JavaName(id);
    }

    // ---------------------------------------------------------------- canonical -> targets

    /// <summary>Biomes added after 1.18 with the older biome a target without them should use.</summary>
    static readonly (string Name, int Since, string Fallback)[] Newer =
    [
        ("minecraft:deep_dark", 3105, "minecraft:dripstone_caves"), ("minecraft:mangrove_swamp", 3105, "minecraft:swamp"),
        ("minecraft:cherry_grove", 3463, "minecraft:meadow"), ("minecraft:pale_garden", 4189, "minecraft:dark_forest"),
        ("minecraft:sulfur_caves", 4883, "minecraft:dripstone_caves"), ("minecraft:dappled_forest", 4883, "minecraft:forest"),
        ("minecraft:ice_caves", 4883, "minecraft:dripstone_caves"),
    ];

    /// <summary>1.18-era biomes for pre-1.18 numeric targets.</summary>
    static readonly Dictionary<string, string> To117 = new(StringComparer.Ordinal)
    {
        ["minecraft:meadow"] = "minecraft:plains", ["minecraft:grove"] = "minecraft:snowy_taiga", ["minecraft:snowy_slopes"] = "minecraft:snowy_mountains",
        ["minecraft:jagged_peaks"] = "minecraft:mountains", ["minecraft:frozen_peaks"] = "minecraft:snowy_mountains", ["minecraft:stony_peaks"] = "minecraft:stone_shore",
        ["minecraft:dripstone_caves"] = "minecraft:plains", ["minecraft:lush_caves"] = "minecraft:jungle", ["minecraft:deep_dark"] = "minecraft:plains",
        ["minecraft:mangrove_swamp"] = "minecraft:swamp", ["minecraft:cherry_grove"] = "minecraft:flower_forest", ["minecraft:pale_garden"] = "minecraft:dark_forest",
        ["minecraft:sulfur_caves"] = "minecraft:plains", ["minecraft:dappled_forest"] = "minecraft:forest", ["minecraft:ice_caves"] = "minecraft:snowy_tundra",
    };

    /// <summary>Name to store in a 1.18+ Java chunk of the given data version.</summary>
    public static string ToJavaName(string canonical, int dataVersion)
    {
        string n = canonical;
        if (Data.Value.ByName.TryGetValue(n, out var e) && e.MergedInto != null) n = e.MergedInto;
        for (int guard = 0; guard < 4; guard++)
        {
            var hit = Newer.FirstOrDefault(x => x.Name == n);
            if (hit.Name == null || dataVersion >= hit.Since) break;
            n = hit.Fallback;
        }
        return n;
    }

    /// <summary>Numeric id for pre-1.18 Java chunks (and Legacy Console when <paramref name="console"/>).</summary>
    public static int ToJavaId(string canonical, bool console = false)
    {
        string n = To117.TryGetValue(canonical, out var older) ? older : canonical;
        if (!Data.Value.ByName.TryGetValue(n, out var e)) return 1;
        if (console && e.ConsoleId is int c) return c;
        return e.JavaId is int j && (!console || j < 128 || j is >= 129 and <= 167) ? j : 1;
    }

    /// <summary>Bedrock biome ids added after 1.12: the game version that added them and what older versions use instead.</summary>
    static readonly (int Id, int Since, int Fallback)[] NewerBedrock =
    [
        (178, 0x01100000, 8), (179, 0x01100000, 8), (180, 0x01100000, 8), (181, 0x01100000, 8),          // 1.16 nether biomes → hell
        (182, 0x01120000, 3), (183, 0x01120000, 3), (184, 0x01120000, 12), (185, 0x01120000, 30),        // 1.18 mountains
        (186, 0x01120000, 1), (187, 0x01120000, 1), (188, 0x01120000, 3), (189, 0x01120000, 3),
        (190, 0x01130000, 1), (191, 0x01130000, 6),                                                     // 1.19 deep dark, mangrove swamp
        (192, 0x01140000, 186),                                                                         // 1.20 cherry grove → meadow
        (193, 0x01153200, 29),                                                                          // 1.21.50 pale garden → roofed forest
        (194, 0x011A0000, 188),                                                                         // 26.x sulfur caves → dripstone caves
    ];

    /// <summary>Bedrock biome id for a game version (0 = newest): biomes it did not have become the nearest older one.</summary>
    public static int ToBedrockId(string canonical, int gameVersion)
    {
        int id = ToBedrockId(canonical);
        if (gameVersion == 0) return id;
        for (int guard = 0; guard < 4; guard++)
        {
            var hit = NewerBedrock.FirstOrDefault(n => n.Id == id);
            if (hit.Since == 0 || gameVersion >= hit.Since) break;
            id = hit.Fallback;
        }
        return id;
    }

    public static int ToBedrockId(string canonical)
    {
        if (Data.Value.ByName.TryGetValue(canonical, out var e) && e.BedrockId is int b) return b;
        return BiomeMap.BedrockId(canonical);
    }
}
