// MinecraftStudio.Core/Conversion/BedrockBlocks.Numeric.cs: numeric for BedrockBlocks.
// Part of BedrockBlocks (main part in BedrockBlocks.cs).
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public static partial class BedrockBlocks
{
    // ---------------------------------------------------------------- numeric ids (Pocket Edition .. 1.12)

    sealed class LegacyData
    {
        public readonly Dictionary<int, string> IdToName = [];
        public readonly Dictionary<string, int> NameToId = new(StringComparer.Ordinal);
        public readonly Dictionary<(string Name, int Meta), BlockState> ToCurrent = [];
        public readonly Dictionary<BlockState, (string Name, int Meta)> FromCurrent = [];
        /// <summary>Current block name → the 1.12 id it came from.</summary>
        public readonly Dictionary<string, string> OriginOfName = new(StringComparer.Ordinal);
    }

    static readonly Lazy<LegacyData> Legacy = new(LoadLegacy);

    static LegacyData LoadLegacy()
    {
        var d = new LegacyData();
        using (var doc = JsonDocument.Parse(EmbeddedData.Bytes("bedrock_legacy_id_map.json.gz")))
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                int id = p.Value.GetInt32();
                d.IdToName.TryAdd(id, p.Name);
                d.NameToId.TryAdd(p.Name, id);
            }
        // r12_to_current_block_map: varint-length name, u16 LE meta, network-NBT state compound.
        byte[] bin = EmbeddedData.Bytes("r12_block_map.gz");
        var r = new ByteReader(bin);
        while (!r.AtEnd)
        {
            int len = (int)r.VarU32();
            string name = Encoding.UTF8.GetString(r.Span(len));
            int meta = r.U16LE();
            var tag = NbtBinary.ReadNamed(r, NbtFormat.BedrockNetwork);
            if (tag?.Tag is not NbtCompound c) continue;
            var states = c.GetCompound("states") ?? new NbtCompound();
            var state = BlockState.Get(c.GetString("name") ?? "minecraft:air", states.Select(kv => new KeyValuePair<string, string>(kv.Key, TagText(kv.Value))));
            d.ToCurrent[(name, meta)] = state;
            d.FromCurrent.TryAdd(state, (name, meta));
            d.OriginOfName.TryAdd(state.Name, name);
        }
        return d;
    }

    /// <summary>Current state for a {name, val} palette entry (1.2.13–1.12).</summary>
    public static BlockState? FromLegacyName(string name, int meta)
    {
        var d = Legacy.Value;
        if (d.ToCurrent.TryGetValue((name, meta), out var s)) return s;
        // Unused meta bits: try without the high bits, then the plain block.
        if (d.ToCurrent.TryGetValue((name, meta & 7), out s)) return s;
        if (d.ToCurrent.TryGetValue((name, 0), out s)) return s;
        return null;
    }

    /// <summary>Current state for a numeric block (Pocket Edition, 3DS, Bedrock before 1.2.13).</summary>
    public static BlockState? FromLegacy(int id, int meta)
    {
        var d = Legacy.Value;
        return d.IdToName.TryGetValue(id, out var name) ? FromLegacyName(name, meta) : null;
    }

    /// <summary>Numeric id/meta (and the 1.12 string id) for a current state, when the block existed in 1.12.</summary>
    public static (int Id, int Meta, string Name)? ToLegacy(BlockState current)
    {
        var d = Legacy.Value;
        if (!d.FromCurrent.TryGetValue(current, out var hit))
        {
            // Same block in another state: the closest state that maps back.
            BlockState? best = null;
            int bestScore = -1;
            var want = current.PropertyPairs().ToList();
            foreach (var s in StatesOf(current.Name))
            {
                if (!d.FromCurrent.ContainsKey(s)) continue;
                int score = s.PropertyPairs().Count(want.Contains);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            if (best == null) return null;
            hit = d.FromCurrent[best];
        }
        return d.NameToId.TryGetValue(hit.Name, out int id) ? (id, hit.Meta, hit.Name) : null;
    }

    public static string? LegacyName(int id) => Legacy.Value.IdToName.GetValueOrDefault(id);

    /// <summary>The number a block had before Bedrock 1.2.13 (and in Pocket Edition), or null.</summary>
    public static int? LegacyNumber(string name) => Legacy.Value.NameToId.TryGetValue(name, out int id) ? id : null;
}
