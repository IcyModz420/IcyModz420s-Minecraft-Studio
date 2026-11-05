// MinecraftStudio.Core/Rendering/Biomes.cs: Biome names, numeric ids per edition and the tints used when
// drawing maps (embedded biomes.csv).
// Owns: Biomes.
using System.Globalization;
using System.Reflection;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Rendering;

/// <summary>Biome names, numeric ids per edition and the tints used when drawing maps (embedded biomes.csv).</summary>
public static class Biomes
{
    public readonly record struct Tint(uint Grass, uint Foliage, uint Water);

    public sealed class BiomeInfo
    {
        /// <summary>Its place in <see cref="All"/>.</summary>
        public int Index { get; init; }
        public string Name { get; init; } = "plains";
        public int? JavaId { get; init; }
        public int? BedrockId { get; init; }
        public int? ConsoleId { get; init; }
        public string? BedrockName { get; init; }
        public uint MapColor { get; init; }
        public Tint Tint { get; init; }
    }

    static readonly Lazy<Tables> Data = new(Load);

    sealed class Tables
    {
        public readonly Dictionary<string, BiomeInfo> ByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, BiomeInfo> ByJava = [];
        public readonly Dictionary<int, BiomeInfo> ByBedrock = [];
        public readonly Dictionary<int, BiomeInfo> ByConsole = [];
        public readonly List<BiomeInfo> All = [];
    }

    static uint Hex(string s) => uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : 0;
    static int? Int(string s) => int.TryParse(s, out int v) ? v : null;

    static Tables Load()
    {
        var t = new Tables();
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftStudio.Data.biomes.csv");
        if (s == null) return t;
        using var r = new StreamReader(s);
        string? header = r.ReadLine();
        if (header == null) return t;
        var cols = header.Split(',');
        int Col(string name) => Array.IndexOf(cols, name);
        int cName = Col("java_current"), cNum = Col("java_numeric_pre_1_18"), c112 = Col("java_1_12_name"), c113 = Col("java_1_13_to_1_17_name"),
            cBid = Col("bedrock_id"), cBname = Col("bedrock_name"), cLce = Col("lce_id"), cMap = Col("map_rgb"),
            cGrass = Col("grass_je"), cFol = Col("foliage_je"), cWater = Col("water_je");
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var p = SplitCsv(line);
            string Get(int i) => i >= 0 && i < p.Count ? p[i].Trim() : "";
            string name = Get(cName);
            if (name.Length == 0) name = Get(c113).Length > 0 ? Get(c113) : Get(c112);
            if (name.Length == 0) continue;
            var fallback = Compute(name);
            var info = new BiomeInfo
            {
                Index = t.All.Count,
                Name = name,
                JavaId = Int(Get(cNum)),
                BedrockId = Int(Get(cBid)),
                ConsoleId = Int(Get(cLce)),
                BedrockName = Get(cBname) is { Length: > 0 } bn ? bn : null,
                MapColor = Hex(Get(cMap)),
                Tint = new Tint(
                    Get(cGrass).Length == 6 ? Hex(Get(cGrass)) : fallback.Grass,
                    Get(cFol).Length == 6 ? Hex(Get(cFol)) : fallback.Foliage,
                    Get(cWater).Length == 6 ? Hex(Get(cWater)) : fallback.Water),
            };
            t.All.Add(info);
            foreach (var n in new[] { name, Get(c112), Get(c113), info.BedrockName })
                if (!string.IsNullOrEmpty(n)) t.ByName.TryAdd(n, info);
            if (info.JavaId is int j) t.ByJava.TryAdd(j, info);
            if (info.BedrockId is int b) t.ByBedrock.TryAdd(b, info);
            if (info.ConsoleId is int c) t.ByConsole.TryAdd(c, info);
        }
        return t;
    }

    internal static List<string> SplitCsv(string line)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { list.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        list.Add(sb.ToString());
        return list;
    }

    public static IReadOnlyList<BiomeInfo> All => Data.Value.All;

    public static BiomeInfo? Find(string name)
    {
        string n = name.StartsWith("minecraft:", StringComparison.Ordinal) ? name[10..] : name;
        return Data.Value.ByName.TryGetValue(n, out var b) ? b : null;
    }

    public static BiomeInfo? FindId(int id, BiomeIdScheme scheme) => scheme switch
    {
        BiomeIdScheme.Bedrock => Data.Value.ByBedrock.GetValueOrDefault(id),
        BiomeIdScheme.Console => Data.Value.ByConsole.GetValueOrDefault(id) ?? Data.Value.ByJava.GetValueOrDefault(id),
        _ => Data.Value.ByJava.GetValueOrDefault(id),
    };

    public static string NameOf(int id, BiomeIdScheme scheme = BiomeIdScheme.Java) => FindId(id, scheme)?.Name ?? $"biome_{id}";

    static readonly Dictionary<string, Tint> Cache = new(StringComparer.Ordinal);
    public static readonly Tint Default = new(0x91BD59, 0x77AB2F, 0x3F76E4);

    public static Tint Get(string? biome)
    {
        if (biome == null) return Default;
        lock (Cache)
        {
            if (Cache.TryGetValue(biome, out var t)) return t;
            t = Find(biome)?.Tint ?? Compute(biome.StartsWith("minecraft:", StringComparison.Ordinal) ? biome[10..] : biome);
            Cache[biome] = t;
            return t;
        }
    }

    public static Tint GetId(int id, BiomeIdScheme scheme) => FindId(id, scheme)?.Tint ?? Default;

    // Fallback for biomes missing from the table: Minecraft's colormap triangle from temperature/rainfall.
    static Tint Compute(string n)
    {
        (double t, double r) = n switch
        {
            _ when n.Contains("desert") || n.Contains("savanna") || n.Contains("badlands") || n.Contains("nether") => (2.0, 0.0),
            _ when n.Contains("jungle") => (0.95, 0.9),
            _ when n.Contains("swamp") => (0.8, 0.9),
            _ when n.Contains("snow") || n.Contains("frozen") || n.Contains("ice") => (0.0, 0.5),
            _ when n.Contains("taiga") => (0.25, 0.8),
            _ when n.Contains("forest") => (0.7, 0.8),
            _ when n.Contains("peak") || n.Contains("hill") || n.Contains("mountain") => (0.2, 0.3),
            _ => (0.8, 0.4),
        };
        t = Math.Clamp(t, 0, 1);
        r = Math.Clamp(r, 0, 1) * t;
        return new Tint(Triangle(t, r, 0x47CD33, 0xBFB755, 0x80B497), Triangle(t, r, 0x1ABF00, 0xAEA42A, 0x60A17B), 0x3F76E4);
    }

    static uint Triangle(double t, double r, uint wet, uint dry, uint cold)
    {
        double wWet = r, wDry = t - r, wCold = 1 - t;
        int Ch(int shift) => (int)Math.Clamp(((wet >> shift) & 255) * wWet + ((dry >> shift) & 255) * wDry + ((cold >> shift) & 255) * wCold, 0, 255);
        return (uint)((Ch(16) << 16) | (Ch(8) << 8) | Ch(0));
    }

    public static uint Mix(uint a, uint b, double t)
    {
        int r = (int)(((a >> 16) & 255) * (1 - t) + ((b >> 16) & 255) * t);
        int g = (int)(((a >> 8) & 255) * (1 - t) + ((b >> 8) & 255) * t);
        int bl = (int)((a & 255) * (1 - t) + (b & 255) * t);
        return (uint)((r << 16) | (g << 8) | bl);
    }
}
