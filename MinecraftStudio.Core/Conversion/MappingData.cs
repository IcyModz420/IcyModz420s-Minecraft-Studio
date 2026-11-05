// MinecraftStudio.Core/Conversion/MappingData.cs: Access to the embedded mapping datasets (minecraft-data MIT,
// BedrockBlockUpgradeSchema / BedrockData CC0, GeyserMC MIT).
// Owns: EmbeddedData, JavaBlockData, JavaLegacyData, BedrockJavaMap, BiomeMap.
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>Access to the embedded mapping datasets (minecraft-data MIT, BedrockBlockUpgradeSchema / BedrockData CC0, GeyserMC MIT).</summary>
static class EmbeddedData
{
    public static Stream Open(string name)
    {
        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftStudio.Data.mappings." + name)
            ?? throw new FileNotFoundException("Embedded mapping data is missing: " + name);
        return name.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(s, CompressionMode.Decompress) : s;
    }

    public static byte[] Bytes(string name)
    {
        using var s = Open(name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static IEnumerable<string> Lines(string name)
    {
        using var r = new StreamReader(Open(name));
        string? line;
        while ((line = r.ReadLine()) != null) yield return line;
    }
}

/// <summary>Block and item lists of Java versions 1.12 … 26.1 (which blocks/properties exist per data version).</summary>
public static class JavaBlockData
{
    public sealed class BlockDef
    {
        public required string Name { get; init; }
        public int NumericId { get; init; }
        public Dictionary<string, string[]> Properties { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Default { get; } = new(StringComparer.Ordinal);
        /// <summary>Has a collision box (false for flowers, torches, rails, air...).</summary>
        public bool Solid { get; init; } = true;
        public bool Transparent { get; init; }
        /// <summary>How much light the block absorbs (0-15) and gives off (0-15).</summary>
        public int FilterLight { get; init; } = 15;
        public int EmitLight { get; init; }
        /// <summary>A plain opaque cube (no properties, has collision, not transparent).</summary>
        public bool IsPlainCube => Solid && !Transparent && Properties.Count == 0 && FilterLight == 15;
    }

    public sealed class VersionBlocks
    {
        public required string Name { get; init; }
        public int DataVersion { get; init; }
        public Dictionary<string, BlockDef> Blocks { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Items { get; } = new(StringComparer.Ordinal);
        /// <summary>minecraft-data's numeric item ids (the real numeric ids only for 1.12).</summary>
        public Dictionary<string, int> ItemIds { get; } = new(StringComparer.Ordinal);
    }

    static readonly Lazy<List<VersionBlocks>> Data = new(Load);

    static List<VersionBlocks> Load()
    {
        var list = new List<VersionBlocks>();
        VersionBlocks? cur = null;
        foreach (var line in EmbeddedData.Lines("java_versions.tsv.gz"))
        {
            var p = line.Split('\t');
            if (p[0] == "#VERSION") { cur = new VersionBlocks { Name = p[1], DataVersion = int.Parse(p[2]) }; list.Add(cur); continue; }
            if (cur == null) continue;
            if (p[0] == "I")
            {
                cur.Items.Add("minecraft:" + p[1]);
                if (p.Length > 2 && int.TryParse(p[2], out int itemId)) cur.ItemIds["minecraft:" + p[1]] = itemId;
                continue;
            }
            if (p[0] != "B") continue;
            var def = new BlockDef
            {
                Name = "minecraft:" + p[1],
                NumericId = int.TryParse(p[2], out int id) ? id : -1,
                Solid = p.Length <= 5 || p[5] != "e",
                Transparent = p.Length > 6 && p[6] == "t",
                FilterLight = p.Length > 7 && int.TryParse(p[7], out int fl) ? fl : 15,
                EmitLight = p.Length > 8 && int.TryParse(p[8], out int el) ? el : 0,
            };
            if (p.Length > 3 && p[3].Length > 0)
                foreach (var kv in p[3].Split(','))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0) def.Default[kv[..eq]] = kv[(eq + 1)..];
                }
            if (p.Length > 4 && p[4].Length > 0)
                foreach (var prop in p[4].Split(';'))
                {
                    int eq = prop.IndexOf('=');
                    if (eq > 0) def.Properties[prop[..eq]] = prop[(eq + 1)..].Split('|');
                }
            cur.Blocks[def.Name] = def;
        }
        return list;
    }

    public static IReadOnlyList<VersionBlocks> Versions => Data.Value;
    public static VersionBlocks Latest => Data.Value[^1];

    static readonly Lazy<(Dictionary<string, int> ByName, Dictionary<int, string> ById)> Legacy112 = new(() =>
    {
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var byId = new Dictionary<int, string>();
        var v = Data.Value[0];
        foreach (var (name, id) in v.ItemIds) { byName.TryAdd(name, id); byId.TryAdd(id, name); }
        foreach (var b in v.Blocks.Values)
            if (b.NumericId >= 0) { byName.TryAdd(b.Name, b.NumericId); byId.TryAdd(b.NumericId, b.Name); }
        return (byName, byId);
    });

    /// <summary>1.12 item (and block item) names to numeric ids.</summary>
    public static IReadOnlyDictionary<string, int> LegacyItemIds => Legacy112.Value.ByName;
    /// <summary>Numeric ids to 1.12 item names (1.8–1.12 stored items by these names).</summary>
    public static IReadOnlyDictionary<int, string> LegacyItemNames => Legacy112.Value.ById;

    /// <summary>The newest list whose data version is ≤ <paramref name="dataVersion"/> (1.12 list for older versions).</summary>
    public static VersionBlocks For(int dataVersion)
    {
        var best = Data.Value[0];
        foreach (var v in Data.Value) if (v.DataVersion <= dataVersion) best = v;
        return best;
    }
}

/// <summary>Java 1.12 numeric id:data ↔ 1.13 block states and item names (minecraft-data legacy.json).</summary>
public static class JavaLegacyData
{
    sealed class Tables
    {
        public readonly Dictionary<int, string> Blocks = [];
        public readonly Dictionary<(int Id, int Damage), string> Items = [];
        public readonly Dictionary<string, (int Id, int Damage)> ItemReverse = new(StringComparer.Ordinal);
    }

    static readonly Lazy<Tables> Data = new(Load);

    static Tables Load()
    {
        var t = new Tables();
        foreach (var line in EmbeddedData.Lines("java_legacy.tsv.gz"))
        {
            var p = line.Split('\t');
            if (p.Length < 3) continue;
            var idData = p[1].Split(':');
            if (idData.Length != 2 || !int.TryParse(idData[0], out int id) || !int.TryParse(idData[1], out int data)) continue;
            if (p[0] == "I")
            {
                t.Items[(id, data)] = p[2];
                t.ItemReverse.TryAdd(p[2], (id, data));
                continue;
            }
            t.Blocks[(id << 4) | data] = p[2];
        }
        return t;
    }

    /// <summary>
    /// 1.13 state string for a numeric block, e.g. "minecraft:oak_log[axis=y]". Data values the table lacks
    /// (Alpha chests with data 0, unused bits) fall back to data 0, then to the block's first listed value.
    /// </summary>
    public static string? Lookup(int id, int data)
    {
        var b = Data.Value.Blocks;
        if (b.TryGetValue((id << 4) | (data & 15), out var s) || b.TryGetValue(id << 4, out s)) return s;
        if (id is < 0 or > 4095) return null;
        for (int d = 1; d < 16; d++) if (b.TryGetValue((id << 4) | d, out s)) return s;
        return null;
    }

    /// <summary>Only the exact id:data pair.</summary>
    public static string? LookupExact(int id, int data) => Data.Value.Blocks.GetValueOrDefault((id << 4) | (data & 15));

    /// <summary>1.13 item name for a numeric item id and damage.</summary>
    public static string? ItemLookup(int id, int damage) =>
        Data.Value.Items.TryGetValue((id, damage), out var s) ? s : Data.Value.Items.TryGetValue((id, 0), out s) ? s : null;

    /// <summary>Numeric item id and damage for a 1.13 item name.</summary>
    public static (int Id, int Damage)? ItemReverse(string name) => Data.Value.ItemReverse.TryGetValue(name, out var v) ? v : null;
}

/// <summary>Java (latest) ↔ Bedrock 1.26.30 block states (minecraft-data blocksJ2B / blocksB2J).</summary>
public static class BedrockJavaMap
{
    sealed class Table
    {
        public readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<(Dictionary<string, string> Props, string Target)>> ByName = new(StringComparer.Ordinal);
    }

    static readonly Lazy<Table> J2B = new(() => LoadTable("j2b.tsv.gz", bedrockKeys: false));
    static readonly Lazy<Table> B2J = new(() => LoadTable("b2j.tsv.gz", bedrockKeys: true));
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int, BlockState?> ToBedrockCache = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int, BlockState?> ToJavaCache = new();

    static Table LoadTable(string name, bool bedrockKeys)
    {
        var t = new Table();
        foreach (var line in EmbeddedData.Lines(name))
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            var from = BlockState.Parse(line[..tab].Replace("[]", ""));
            // Some B2J keys write the *_bit flags as 0/1; the rest of the tool uses false/true.
            if (bedrockKeys) from = BedrockBlocks.NormalizeBits(from);
            string to = line[(tab + 1)..].Replace("[]", "");
            t.Exact[from.ToString()] = to;
            if (!t.ByName.TryGetValue(from.Name, out var list)) t.ByName[from.Name] = list = [];
            list.Add((from.PropertyPairs().ToDictionary(p => p.Key, p => p.Value), to));
        }
        return t;
    }

    static string? Find(Table t, BlockState s)
    {
        if (t.Exact.TryGetValue(s.ToString(), out var exact)) return exact;
        if (!t.ByName.TryGetValue(s.Name, out var candidates)) return null;
        var props = s.PropertyPairs().ToDictionary(p => p.Key, p => p.Value);
        string? best = null;
        int bestScore = -1;
        foreach (var (cp, target) in candidates)
        {
            int score = cp.Count(kv => props.TryGetValue(kv.Key, out var v) && v == kv.Value);
            if (score > bestScore) { bestScore = score; best = target; }
        }
        return best;
    }

    /// <summary>Bedrock state for a canonical (newest Java) state, or null when the block has no Bedrock equivalent.</summary>
    public static BlockState? ToBedrock(BlockState java) =>
        ToBedrockCache.GetOrAdd(java.Id, _ => Find(J2B.Value, java) is string s ? BlockState.Parse(s) : null);

    /// <summary>Canonical Java state for a current Bedrock state, or null when the block has no Java equivalent.</summary>
    public static BlockState? ToJava(BlockState bedrock) =>
        ToJavaCache.GetOrAdd(bedrock.Id, _ => Find(B2J.Value, BedrockBlocks.NormalizeBits(bedrock)) is string s ? BlockState.Parse(s) : null);

    public static bool HasJavaBlock(string name) => J2B.Value.ByName.ContainsKey(name);
    public static int JavaStateCount => J2B.Value.Exact.Count;
}

/// <summary>Biome ids between editions (Geyser biomes.json + embedded biomes.csv).</summary>
public static class BiomeMap
{
    static readonly Lazy<Dictionary<string, int>> JavaToBedrock = new(() =>
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(EmbeddedData.Bytes("geyser_biomes.json.gz"));
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.TryGetProperty("bedrock_id", out var id)) d[p.Name] = id.GetInt32();
        return d;
    });

    public static int BedrockId(string javaBiome)
    {
        string n = javaBiome.Contains(':') ? javaBiome : "minecraft:" + javaBiome;
        if (JavaToBedrock.Value.TryGetValue(n, out int id)) return id;
        var info = Rendering.Biomes.Find(n);
        return info?.BedrockId ?? 1;
    }

    public static string JavaName(int bedrockId)
    {
        foreach (var (k, v) in JavaToBedrock.Value) if (v == bedrockId) return k;
        return "minecraft:" + (Rendering.Biomes.FindId(bedrockId, BiomeIdScheme.Bedrock)?.Name ?? "plains");
    }

    /// <summary>Pre-1.18 Java numeric biome id for a modern name (1 = plains when unknown).</summary>
    public static int JavaLegacyId(string javaBiome)
    {
        var info = Rendering.Biomes.Find(javaBiome);
        return info?.JavaId ?? 1;
    }
}
