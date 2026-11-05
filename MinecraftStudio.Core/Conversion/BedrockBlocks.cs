// MinecraftStudio.Core/Conversion/BedrockBlocks.cs: Bedrock block states: the canonical state list of the
// newest supported version (pmmp BedrockData), Mojang's block-state upgrade history (pmmp
// BedrockBlockUpgradeSchema, applied the way PocketMine does) and the numeric ids used before 1.2.13 and by
// Pocket Edition, 3DS and the {name, val} palettes of 1.2.13–1.12.
// Owns: BedrockBlocks.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Bedrock block states: the canonical state list of the newest supported version (pmmp BedrockData),
/// Mojang's block-state upgrade history (pmmp BedrockBlockUpgradeSchema, applied the way PocketMine does)
/// and the numeric ids used before 1.2.13 and by Pocket Edition, 3DS and the {name, val} palettes of 1.2.13–1.12.
/// </summary>
public static partial class BedrockBlocks
{
    /// <summary>Block-state version written by 1.21.60 through 26.x (1.21.60.33).</summary>
    public const int CurrentVersion = (1 << 24) | (21 << 16) | (60 << 8) | 33;

    public static int MakeVersion(int major, int minor, int patch, int revision) => (major << 24) | (minor << 16) | (patch << 8) | revision;
    public static string VersionText(int v) => $"{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";

    // ---------------------------------------------------------------- canonical states

    sealed class CanonicalData
    {
        public readonly HashSet<BlockState> States = [];
        public readonly Dictionary<string, List<BlockState>> ByName = new(StringComparer.Ordinal);
        public readonly Dictionary<string, NbtType> PropertyTypes = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, HashSet<string>>> Values = new(StringComparer.Ordinal);
    }

    static readonly Lazy<CanonicalData> Canonical = new(LoadCanonical);

    static CanonicalData LoadCanonical()
    {
        var data = new CanonicalData();
        foreach (var root in NbtBinary.ReadAll(EmbeddedData.Bytes("canonical_block_states.nbt.gz"), NbtFormat.BedrockNetwork))
        {
            if (root.Tag is not NbtCompound c) continue;
            string name = c.GetString("name") ?? "";
            var states = c.GetCompound("states") ?? new NbtCompound();
            var pairs = new List<KeyValuePair<string, string>>();
            if (!data.Values.TryGetValue(name, out var values)) data.Values[name] = values = new(StringComparer.Ordinal);
            foreach (var (k, v) in states)
            {
                data.PropertyTypes.TryAdd(k, v.Type);
                string text = TagText(v);
                pairs.Add(new(k, text));
                if (!values.TryGetValue(k, out var set)) values[k] = set = new(StringComparer.Ordinal);
                set.Add(text);
            }
            var s = BlockState.Get(name, pairs);
            data.States.Add(s);
            if (!data.ByName.TryGetValue(name, out var list)) data.ByName[name] = list = [];
            list.Add(s);
        }
        return data;
    }

    /// <summary>State value as text: bytes 0/1 become false/true like the rest of the tool shows them.</summary>
    public static string TagText(NbtTag t) => t switch
    {
        NbtString s => s.Value,
        NbtByte b => b.Value == 0 ? "false" : b.Value == 1 ? "true" : b.Value.ToString(),
        { IsNumeric: true } n => n.AsLong.ToString(),
        _ => Snbt.FormatValue(t),
    };

    public static int CanonicalStateCount => Canonical.Value.States.Count;
    public static IEnumerable<BlockState> CanonicalStates => Canonical.Value.States;
    public static bool IsKnownBlock(string name) => Canonical.Value.ByName.ContainsKey(name);
    public static bool IsCanonical(BlockState s) => Canonical.Value.States.Contains(s);
    public static IReadOnlyList<BlockState> StatesOf(string name) => Canonical.Value.ByName.TryGetValue(name, out var l) ? l : [];

    /// <summary>NBT type of a state property (byte for the *_bit flags, int for numbers, string otherwise).</summary>
    public static NbtType PropertyType(string property, string value)
    {
        if (Canonical.Value.PropertyTypes.TryGetValue(property, out var t)) return t;
        if (value is "true" or "false") return NbtType.Byte;
        return int.TryParse(value, out _) ? NbtType.Int : NbtType.String;
    }

    static NbtTag MakeTag(string property, string value) => PropertyType(property, value) switch
    {
        NbtType.Byte => new NbtByte((sbyte)(value == "true" ? 1 : value == "false" ? 0 : sbyte.TryParse(value, out var b) ? b : 0)),
        NbtType.Int => new NbtInt(int.TryParse(value, out int i) ? i : 0),
        _ => new NbtString(value),
    };

    /// <summary>Byte flags written as 0/1 become false/true (other properties unchanged).</summary>
    public static BlockState NormalizeBits(BlockState s)
    {
        if (s.Properties.Length == 0 || (!s.Properties.Contains("=0", StringComparison.Ordinal) && !s.Properties.Contains("=1", StringComparison.Ordinal))) return s;
        var types = Canonical.Value.PropertyTypes;
        bool changed = false;
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var (k, v) in s.PropertyPairs())
        {
            if (v is "0" or "1" && types.GetValueOrDefault(k) == NbtType.Byte) { pairs.Add(new(k, v == "1" ? "true" : "false")); changed = true; }
            else pairs.Add(new(k, v));
        }
        return changed ? BlockState.Get(s.Name, pairs) : s;
    }

    /// <summary>
    /// Brings a state onto the canonical list: byte flags written as 0/1 become false/true, properties the block
    /// does not have are dropped and missing ones get the value of the block's first canonical state.
    /// Unknown blocks (add-ons, education edition) are returned unchanged.
    /// </summary>
    public static BlockState Fit(BlockState s)
    {
        var data = Canonical.Value;
        if (data.States.Contains(s)) return s;
        if (!data.ByName.TryGetValue(s.Name, out var list)) return s;
        var allowed = data.Values[s.Name];
        var props = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in list[0].PropertyPairs()) props[k] = v;
        foreach (var (k, v0) in s.PropertyPairs())
        {
            if (!allowed.TryGetValue(k, out var values)) continue;
            string v = v0;
            if (data.PropertyTypes.GetValueOrDefault(k) == NbtType.Byte && v is "0" or "1") v = v == "1" ? "true" : "false";
            if (values.Contains(v)) props[k] = v;
        }
        var fitted = BlockState.Get(s.Name, props);
        return data.States.Contains(fitted) ? fitted : BestMatch(list, fitted);
    }

    static BlockState BestMatch(List<BlockState> candidates, BlockState wanted)
    {
        var want = wanted.PropertyPairs().ToList();
        BlockState best = candidates[0];
        int bestScore = -1;
        foreach (var c in candidates)
        {
            int score = c.PropertyPairs().Count(want.Contains);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    static readonly ConcurrentDictionary<(int State, int Version), byte[]> PaletteBytes = new();

    /// <summary>Palette entry {name, states, version} with each state stored with its proper NBT type.</summary>
    public static NbtCompound ToPaletteNbt(BlockState s, int version = CurrentVersion)
    {
        var c = new NbtCompound();
        c.SetString("name", s.Name);
        var states = new NbtCompound();
        foreach (var (k, v) in s.PropertyPairs()) states[k] = MakeTag(k, v);
        c["states"] = states;
        c.SetInt("version", version);
        return c;
    }

    /// <summary>Serialized little-endian palette entry, cached per state.</summary>
    public static byte[] PaletteEntryBytes(BlockState s, int version = CurrentVersion) =>
        PaletteBytes.GetOrAdd((s.Id, version), _ => NbtBinary.Serialize(ToPaletteNbt(s, version), NbtFormat.Bedrock));

    // ---------------------------------------------------------------- reading stored palette entries

    static readonly ConcurrentDictionary<string, BlockState> PaletteCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Any stored palette entry — {name, states, version} (1.13+), {name, val} (1.2.13–1.12) or a bare name —
    /// upgraded to the current canonical state.
    /// </summary>
    public static BlockState FromPaletteNbt(NbtCompound c)
    {
        string name = c.GetString("name") ?? c.GetString("Name") ?? "minecraft:air";
        if (!name.Contains(':')) name = "minecraft:" + name;
        var states = c.GetCompound("states");
        int version = c.GetInt("version", 0);
        if (states == null && c["val"] is { IsNumeric: true } val)
            return FromLegacyName(name, (int)val.AsLong) ?? BlockState.Get(name, $"val={val.AsLong}");
        string key = name + "|" + version + "|" + (states == null ? "" : Snbt.Write(states, SnbtStyle.Compact));
        return PaletteCache.GetOrAdd(key, _ =>
        {
            var dict = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
            if (states != null) foreach (var (k, v) in states) dict[k] = v;
            var (newName, newStates) = Upgrade(name, dict, version);
            return Fit(BlockState.Get(newName, newStates.Select(kv => new KeyValuePair<string, string>(kv.Key, TagText(kv.Value)))));
        });
    }
}
