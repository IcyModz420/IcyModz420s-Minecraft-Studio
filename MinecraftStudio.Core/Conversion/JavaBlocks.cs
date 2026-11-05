// MinecraftStudio.Core/Conversion/JavaBlocks.cs: Java block states across versions. "Canonical" means the
// newest Java naming (what the Bedrock mapping tables use); every reader converts to it and every writer
// converts from it to the target version.
// Owns: JavaBlocks, LegacyLevel.
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Java block states across versions. "Canonical" means the newest Java naming (what the Bedrock mapping tables use);
/// every reader converts to it and every writer converts from it to the target version.
/// </summary>
public static class JavaBlocks
{
    // ---------------------------------------------------------------- history of renames and property changes

    /// <summary>Blocks renamed after the flattening: (old name, new name, first data version with the new name).</summary>
    static readonly (string Old, string New, int Since)[] Renames =
    [
        ("minecraft:sign", "minecraft:oak_sign", 1933),            // 19w08a
        ("minecraft:wall_sign", "minecraft:oak_wall_sign", 1933),
        ("minecraft:grass_path", "minecraft:dirt_path", 2681),     // 20w45a
        ("minecraft:grass", "minecraft:short_grass", 3691),        // 23w46a
        ("minecraft:chain", "minecraft:iron_chain", 4536),         // 25w32a
    ];

    const int WallsEnumSince = 2504;          // 20w06a: wall sides true/false -> none/low/tall
    const int JigsawOrientationSince = 2504;  // 20w06a: jigsaw facing -> orientation
    const int CauldronSplitSince = 2681;      // 20w45a: cauldron[level] -> cauldron / water_cauldron[level]
    const int CreakingHeartStateSince = 4298; // 25w02a: creaking_heart active -> creaking_heart_state

    static readonly Dictionary<string, string> RenameToNew = Renames.ToDictionary(r => r.Old, r => r.New, StringComparer.Ordinal);

    static readonly ConcurrentDictionary<int, BlockState> CanonicalCache = new();

    /// <summary>18w43a: the old smooth "stone_slab" became "smooth_stone_slab" and "stone_slab" now means a plain stone slab.</summary>
    const int StoneSlabSplitSince = 1901;

    /// <summary>Upgrades a stored 1.13+ state (any version) to the canonical newest form.</summary>
    public static BlockState Canonicalize(BlockState s) => CanonicalCache.GetOrAdd(s.Id, _ => CanonicalizeUncached(s));

    /// <summary>Canonical form of a state stored by data version <paramref name="dataVersion"/> (names whose meaning changed).</summary>
    public static BlockState Canonicalize(BlockState s, int dataVersion)
    {
        if (dataVersion is > 0 and < StoneSlabSplitSince && s.Name == "minecraft:stone_slab")
            s = BlockState.Get("minecraft:smooth_stone_slab", s.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));
        return Canonicalize(s);
    }

    static BlockState CanonicalizeUncached(BlockState s)
    {
        string name = RenameToNew.TryGetValue(s.Name, out var renamed) ? renamed : s.Name;
        var props = s.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        switch (name)
        {
            case "minecraft:cauldron" when props.Remove("level", out var level):
                if (level != "0") { name = "minecraft:water_cauldron"; props["level"] = level; }
                break;
            case "minecraft:jigsaw" when props.Remove("facing", out var facing):
                props["orientation"] = facing switch { "up" => "up_north", "down" => "down_south", _ => facing + "_up" };
                break;
            case "minecraft:creaking_heart" when props.Remove("active", out var active):
                props["creaking_heart_state"] = active == "true" ? "awake" : "uprooted";
                break;
            case "minecraft:torchflower_crop" when props.GetValueOrDefault("age") == "2":
                props["age"] = "1";
                break;
        }
        if (name.EndsWith("_wall", StringComparison.Ordinal))
            foreach (var side in Sides)
                if (props.TryGetValue(side, out var v) && v is "true" or "false") props[side] = v == "true" ? "low" : "none";
        return Fit(name, props, JavaBlockData.Latest);
    }

    static readonly string[] Sides = ["north", "east", "south", "west"];

    /// <summary>Keeps the properties the block has in <paramref name="version"/>, fills the rest with its defaults.</summary>
    static BlockState Fit(string name, Dictionary<string, string> props, JavaBlockData.VersionBlocks version)
    {
        if (!version.Blocks.TryGetValue(name, out var def)) return BlockState.Get(name, props);
        var result = new Dictionary<string, string>(def.Default, StringComparer.Ordinal);
        foreach (var (k, v) in props)
            if (def.Properties.TryGetValue(k, out var values) && Array.IndexOf(values, v) >= 0) result[k] = v;
        // Properties without a recorded default (minecraft-data omits it for some blocks): first value.
        foreach (var (k, values) in def.Properties)
            if (!result.ContainsKey(k) && values.Length > 0) result[k] = values[0];
        return BlockState.Get(name, result);
    }

    // ---------------------------------------------------------------- numeric ids (Classic .. 1.12, Legacy Console)

    /// <summary>
    /// Properties that 1.12 worked out from the neighbours instead of storing. minecraft-data's legacy table
    /// picks arbitrary values for them; they are reset to the plain form and recomputed by <see cref="ShapeFixer"/>.
    /// </summary>
    static void ResetDerived(string name, Dictionary<string, string> p)
    {
        string b = name[10..];
        if (b.EndsWith("_stairs", StringComparison.Ordinal)) p["shape"] = "straight";
        else if (b.EndsWith("_wall", StringComparison.Ordinal)) { foreach (var s in Sides) if (p.ContainsKey(s)) p[s] = "false"; p["up"] = "true"; }
        else if (b.EndsWith("_fence", StringComparison.Ordinal) || b.EndsWith("glass_pane", StringComparison.Ordinal) || b == "iron_bars" || b == "tripwire")
            foreach (var s in Sides) { if (p.ContainsKey(s)) p[s] = "false"; }
        else if (b == "redstone_wire") foreach (var s in Sides) p[s] = "none";
        else if (b is "fire" or "chorus_plant") { foreach (var s in Sides) if (p.ContainsKey(s)) p[s] = "false"; if (p.ContainsKey("up")) p["up"] = "false"; if (p.ContainsKey("down")) p["down"] = "false"; }
        else if (b is "grass_block" or "mycelium" or "podzol") p["snowy"] = "false";
        else if (b == "vine" && p.ContainsKey("up")) p["up"] = "false";
    }

    static readonly ConcurrentDictionary<int, BlockState> LegacyCache = new();

    /// <summary>Exact state for a 1.12-and-older numeric block (before block-entity and neighbour fix-ups).</summary>
    public static BlockState FromLegacy(int id, int data)
    {
        int key = ((id & 0xFFFF) << 4) | (data & 15);
        return LegacyCache.GetOrAdd(key, _ =>
        {
            string? text = JavaLegacyData.Lookup(id, data & 15);
            if (text == null) return BlockState.Get($"minecraft:legacy_block_{id}", (data & 15) == 0 ? "" : $"data={data & 15}");
            var parsed = BlockState.Parse(text);
            var props = parsed.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            ResetDerived(parsed.Name, props);
            var s = Canonicalize(BlockState.Get(parsed.Name, props), 1451);
            if (s.LegacyId < 0) { s.LegacyId = id; s.LegacyData = data & 15; }
            return s;
        });
    }

    // Classic / Indev cloth 21..36, mapped to the nearest wool.
    static readonly string[] ClassicCloth =
        ["red", "orange", "yellow", "lime", "green", "lime", "cyan", "light_blue", "blue", "purple", "purple", "magenta", "pink", "gray", "light_gray", "white"];

    /// <summary>Classic and Indev ids (21-36 are coloured cloth; the rest match Java).</summary>
    public static BlockState FromClassic(int id, int data) =>
        id is >= 21 and <= 36 ? BlockState.Get($"minecraft:{ClassicCloth[id - 21]}_wool") : FromLegacy(id, data);

    // ---------------------------------------------------------------- canonical -> a target Java version

    static readonly ConcurrentDictionary<(int State, int Version), BlockState> TargetCache = new();

    /// <summary>
    /// The state to write for a flattened (1.13+) Java target: renames and property changes undone for older
    /// versions, properties the target lacks dropped, and blocks it does not have replaced by stand-ins.
    /// </summary>
    public static BlockState ToVersion(BlockState canonical, int dataVersion)
    {
        if (canonical.IsAir) return BlockState.Air;
        return TargetCache.GetOrAdd((canonical.Id, dataVersion), _ =>
        {
            var list = JavaBlockData.For(dataVersion);
            bool Exists(string name) => list.Blocks.ContainsKey(Downgrade(BlockState.Get(name), dataVersion).Name);
            var s = canonical;
            for (int guard = 0; guard < 10; guard++)
            {
                if (s.IsAir) return BlockState.Air;
                var down = Downgrade(s, dataVersion);
                if (list.Blocks.ContainsKey(down.Name))
                    return Fit(down.Name, down.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), list);
                var next = Substitutes.Next(s, Exists, Substitutes.PlainCubes);
                if (next == null || next == s) break;
                s = next;
            }
            return BlockState.Get("minecraft:stone");
        });
    }

    /// <summary>Undoes renames and property changes that happened after <paramref name="dataVersion"/>.</summary>
    public static BlockState Downgrade(BlockState s, int dataVersion)
    {
        string name = s.Name;
        Dictionary<string, string>? props = null;
        Dictionary<string, string> P() => props ??= s.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var (oldName, newName, since) in Renames)
            if (name == newName && dataVersion < since) name = oldName;
        // Before 18w43a "stone_slab" was the smooth slab; plain stone slabs did not exist yet and become smooth ones.
        if (dataVersion < StoneSlabSplitSince && name is "minecraft:smooth_stone_slab") name = "minecraft:stone_slab";
        if (dataVersion < CauldronSplitSince)
        {
            switch (name)
            {
                case "minecraft:water_cauldron" or "minecraft:powder_snow_cauldron":
                    P()["level"] = s.GetProperty("level") ?? "3";
                    name = "minecraft:cauldron";
                    break;
                case "minecraft:lava_cauldron":
                    P()["level"] = "0";
                    name = "minecraft:cauldron";
                    break;
                case "minecraft:cauldron":
                    P()["level"] = "0";
                    break;
            }
        }
        if (dataVersion < WallsEnumSince && name.EndsWith("_wall", StringComparison.Ordinal))
            foreach (var side in Sides)
                if (P().TryGetValue(side, out var v) && v is "low" or "tall" or "none") P()[side] = v == "none" ? "false" : "true";
        if (dataVersion < JigsawOrientationSince && name == "minecraft:jigsaw" && P().Remove("orientation", out var o))
            P()["facing"] = o.Split('_')[0];
        if (dataVersion < CreakingHeartStateSince && name == "minecraft:creaking_heart" && P().Remove("creaking_heart_state", out var st))
            P()["active"] = st == "awake" ? "true" : "false";
        if (props == null && name == s.Name) return s;
        return BlockState.Get(name, props ?? P());
    }

    // ---------------------------------------------------------------- canonical -> numeric ids

    sealed class ReverseData
    {
        public readonly Dictionary<string, List<(int Key, Dictionary<string, string> Props)>> ByName = new(StringComparer.Ordinal);
    }

    static readonly Lazy<ReverseData> Reverse = new(() =>
    {
        var r = new ReverseData();
        for (int id = 0; id < 256; id++)
            for (int d = 0; d < 16; d++)
            {
                if (JavaLegacyData.LookupExact(id, d) is not string text) continue;
                var s = BlockState.Parse(text);
                var props = s.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                ResetDerived(s.Name, props);
                var c = Canonicalize(BlockState.Get(s.Name, props), 1451);
                if (!r.ByName.TryGetValue(c.Name, out var list)) r.ByName[c.Name] = list = [];
                list.Add(((id << 4) | d, c.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
            }
        return r;
    });

    // Properties that 1.12 did not store (or stored on the other half / in a block entity): a mismatch costs little.
    static readonly HashSet<string> WeakProperties = new(StringComparer.Ordinal)
    {
        "north", "south", "east", "west", "up", "down", "shape", "hinge", "powered", "occupied", "snowy", "waterlogged",
        "note", "instrument", "has_record", "locked", "distance", "attached", "disarmed", "in_wall", "conditional", "triggered", "enabled",
    };

    static readonly ConcurrentDictionary<(int State, int Level), (int, int)> LegacyTargetCache = new();

    /// <summary>
    /// Numeric id and data for a canonical state, limited to the blocks that existed in the target
    /// (see <see cref="LegacyLevel"/>); blocks the target lacks are replaced by stand-ins.
    /// </summary>
    public static (int Id, int Data) ToLegacy(BlockState canonical, double level = LegacyLevel.Release_1_12)
    {
        if (canonical.IsAir) return (0, 0);
        return LegacyTargetCache.GetOrAdd((canonical.Id, (int)(level * 10000)), _ =>
        {
            bool Exists(string name) => Reverse.Value.ByName.ContainsKey(name) && TryReverse(BlockState.Get(name), level) != null;
            var s = canonical;
            for (int guard = 0; guard < 12; guard++)
            {
                if (s.IsAir) return (0, 0);
                // The reverse table is keyed by canonical names, so no version downgrade is needed first.
                if (TryReverse(s, level) is (int id, int data)) return (id, data);
                var next = Substitutes.Next(s, Exists, Substitutes.PlainCubes);
                if (next == null || next == s) break;
                s = next;
            }
            return (1, 0);
        });
    }

    static (int, int)? TryReverse(BlockState s, double level)
    {
        if (!Reverse.Value.ByName.TryGetValue(s.Name, out var candidates)) return null;
        int best = -1, bestScore = int.MinValue;
        var want = s.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var (key, props) in candidates)
        {
            if (!LegacyLevel.Allows(key >> 4, key & 15, level)) continue;
            int score = 0;
            foreach (var (k, v) in props)
            {
                bool same = want.TryGetValue(k, out var w) && w == v;
                score += same ? (WeakProperties.Contains(k) ? 1 : 16) : 0;
            }
            if (score > bestScore) { bestScore = score; best = key; }
        }
        return best >= 0 ? (best >> 4, best & 15) : null;
    }

    /// <summary>Classic id (0-49) for a canonical state; wool becomes the nearest cloth colour.</summary>
    public static int ToClassic(BlockState canonical)
    {
        if (canonical.BaseName.EndsWith("_wool", StringComparison.Ordinal))
        {
            string color = canonical.BaseName[..^5];
            int i = Array.IndexOf(ClassicCloth, color);
            return i >= 0 ? 21 + i : color switch { "black" => 34, "brown" => 22, _ => 36 };
        }
        var (id, _) = ToLegacy(canonical, LegacyLevel.Classic);
        return id is >= 0 and <= 49 ? id : 1;
    }
}

/// <summary>Which numeric block ids a pre-flattening game version knows (from the embedded legacy_blocks.csv).</summary>
public static class LegacyLevel
{
    public const double Classic = 0.0, Indev = 0.1, Infdev = 0.2, Alpha = 0.3, Alpha_1_2 = 0.4, Beta_1_2 = 0.52, Beta_1_3 = 0.53,
        Beta_1_7 = 0.57, Beta_1_8 = 0.58, Release_1_0 = 1.0, Release_1_2 = 1.02, Release_1_7 = 1.07, Release_1_8 = 1.08, Release_1_12 = 1.12;

    static readonly Lazy<double[]> IntroducedAt = new(Load);

    static double[] Load()
    {
        var arr = new double[256];
        Array.Fill(arr, 99);
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftStudio.Data.legacy_blocks.csv");
        if (s == null) return arr;
        using var r = new StreamReader(s);
        string? header = r.ReadLine();
        if (header == null) return arr;
        var cols = header.Split(',');
        int cId = Array.IndexOf(cols, "id"), cIntro = Array.IndexOf(cols, "introduced_java");
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var p = Rendering.Biomes.SplitCsv(line);
            if (cId < 0 || cIntro < 0 || p.Count <= cIntro || !int.TryParse(p[cId], out int id) || id is < 0 or > 255) continue;
            arr[id] = Parse(p[cIntro]);
        }
        return arr;
    }

    /// <summary>"Classic rd-132211" -> 0, "Alpha 1.0.4" -> 0.3, "Beta 1.7" -> 0.57, "1.5" -> 1.05, "1.12" -> 1.12.</summary>
    public static double Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return 99;
        if (text.StartsWith("Classic", StringComparison.OrdinalIgnoreCase)) return Classic;
        if (text.StartsWith("Indev", StringComparison.OrdinalIgnoreCase)) return Indev;
        if (text.StartsWith("Infdev", StringComparison.OrdinalIgnoreCase)) return Infdev;
        if (text.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase)) return text.Contains("1.2") ? Alpha_1_2 : Alpha;
        if (text.StartsWith("Beta", StringComparison.OrdinalIgnoreCase))
        {
            var v = text[4..].Trim().Split('.');
            return v.Length >= 2 && int.TryParse(v[1], out int minor) ? 0.5 + minor / 100.0 : 0.5;
        }
        var parts = text.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)) return 1 + m / 100.0;
        return 1.0;
    }

    /// <summary>True when block <paramref name="id"/> existed at <paramref name="level"/>.</summary>
    public static bool Allows(int id, int data, double level)
    {
        if (id is < 0 or > 255) return false;
        if (level >= Release_1_12) return IntroducedAt.Value[id] < 99;
        if (IntroducedAt.Value[id] > level + 1e-9) return false;
        // Classic and Indev used 21-36 for coloured cloth; only the Java meaning of the shared ids is allowed later.
        if (level < Infdev && id is >= 21 and <= 36) return false;
        // Coloured wool arrived in Beta 1.2, the other planks/logs/leaves types later still.
        if (data != 0 && level < Beta_1_2 && id is 35 or 17 or 18 or 5) return false;
        return true;
    }
}
