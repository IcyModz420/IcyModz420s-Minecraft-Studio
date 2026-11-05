// MinecraftStudio.Core/Conversion/BedrockBlocks.Downgrade.cs: downgrade for BedrockBlocks.
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
    // ---------------------------------------------------------------- downgrade (newest → an older game version)

    /// <summary>Block-state version a game version writes in its palettes (major.minor.patch, revision ignored).</summary>
    public static int StateVersionFor(int gameVersion)
    {
        int best = MakeVersion(1, 12, 0, 1);
        foreach (var group in Schemas.Value)
            foreach (var schema in group)
                if (schema.ToGame <= gameVersion) best = Math.Max(best, schema.Version);
        return best;
    }

    static readonly ConcurrentDictionary<(int State, int Game), (string Name, Dictionary<string, NbtTag> States)?> DowngradeCache = new();

    /// <summary>The current state as properly typed tags (types from the canonical list).</summary>
    static Dictionary<string, NbtTag> TypedStates(BlockState s)
    {
        var d = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
        foreach (var (k, v) in s.PropertyPairs()) d[k] = MakeTag(k, v);
        return d;
    }

    /// <summary>
    /// The state an older game version stores for a current canonical state: every upgrade schema that version did not
    /// know is undone (newest first), and the result is checked by upgrading it again. Null when nothing upgrades back to
    /// the same state (the block is newer than that version).
    /// </summary>
    public static (string Name, Dictionary<string, NbtTag> States)? Downgrade(BlockState current, int gameVersion) =>
        DowngradeCache.GetOrAdd((current.Id, gameVersion), _ =>
        {
            var undo = Schemas.Value.SelectMany(g => g).Where(s => s.ToGame > gameVersion).OrderByDescending(s => s.Id).ToList();
            int stateVersion = StateVersionFor(gameVersion);
            // Properties added later (a lightning rod's powered_bit) cannot be stored by the older version: ignore them.
            var added = undo.SelectMany(s => s.AddedProperties.Values).SelectMany(d => d.Keys).ToHashSet(StringComparer.Ordinal);
            string Comparable(BlockState b) => b.Name + "|" + string.Join(",", b.PropertyPairs().Where(kv => !added.Contains(kv.Key)).Select(kv => kv.Key + "=" + kv.Value));
            string wanted = Comparable(current);
            (string, Dictionary<string, NbtTag>)? found = null;
            string? origin = Legacy.Value.OriginOfName.GetValueOrDefault(current.Name);
            int tries = 0;
            void Search(int index, string name, Dictionary<string, NbtTag> states)
            {
                if (found != null || ++tries > 256) return;
                if (index == undo.Count)
                {
                    var (un, us) = Upgrade(name, new Dictionary<string, NbtTag>(states, StringComparer.Ordinal), stateVersion);
                    if (Comparable(Fit(BlockState.Get(un, us.Select(kv => new KeyValuePair<string, string>(kv.Key, TagText(kv.Value)))))) == wanted)
                        found = (name, states);
                    return;
                }
                foreach (var (n, st) in Unapply(undo[index], name, states, origin)) Search(index + 1, n, st);
            }
            Search(0, current.Name, TypedStates(current));
            return found;
        });

    static readonly ConcurrentDictionary<(string Origin, int Schema), string> OriginCache = new();

    /// <summary>The id a block that existed in 1.12 had just before a schema ran: its 1.12 id, renamed by earlier schemas.</summary>
    static string OriginAt(string origin12, Schema schema) => OriginCache.GetOrAdd((origin12, schema.Id), _ =>
    {
        string name = origin12;
        foreach (var earlier in Schemas.Value.SelectMany(g => g).Where(x => x.Id < schema.Id).OrderBy(x => x.Id))
            if (earlier.RenamedIds.TryGetValue(name, out var renamed)) name = renamed;
        return name;
    });

    /// <summary>
    /// Older forms a schema could have turned into this state, most likely first (the state itself last).
    /// <paramref name="origin12"/> is the 1.12 id of the block being downgraded, when it existed then.
    /// </summary>
    static IEnumerable<(string, Dictionary<string, NbtTag>)> Unapply(Schema schema, string name, Dictionary<string, NbtTag> states, string? origin12)
    {
        // A name this schema creates did not exist before it, so the state cannot have passed through unchanged.
        bool created = schema.RenamedIds.ContainsValue(name);
        // A whole-state remap that produced exactly this state.
        foreach (var (oldName, remaps) in schema.RemappedStates)
            foreach (var remap in remaps)
            {
                Dictionary<string, NbtTag>? fromName = null;
                string produced;
                if (remap.NewName != null) produced = remap.NewName;
                else if (remap.NewFlattenedName != null)
                {
                    if (Unflatten(remap.NewFlattenedName, name) is not NbtTag v || !FlattenMayProduce(remap.NewFlattenedName, oldName, name, schema, origin12)) continue;
                    fromName = new(StringComparer.Ordinal) { [remap.NewFlattenedName.Property] = v };
                    produced = name;
                }
                else produced = oldName;
                if (produced != name) continue;
                if (produced != oldName) created = true;
                bool ok = remap.NewState.All(kv => states.TryGetValue(kv.Key, out var have) && TagEquals(have, kv.Value))
                          && states.Keys.All(k => remap.NewState.ContainsKey(k) || remap.CopiedState.Contains(k));
                if (!ok) continue;
                var old = new Dictionary<string, NbtTag>(remap.OldState, StringComparer.Ordinal);
                foreach (var c in remap.CopiedState) if (states.TryGetValue(c, out var cv)) old[c] = cv;
                if (fromName != null) foreach (var (k, v) in fromName) old.TryAdd(k, v);
                yield return (oldName, old);
            }

        // Renamed or flattened ids: which old id became this name.
        var candidates = new List<(string Old, Dictionary<string, NbtTag> States)>();
        foreach (var (o, n) in schema.RenamedIds)
            if (n == name) candidates.Add((o, new Dictionary<string, NbtTag>(states, StringComparer.Ordinal)));
        foreach (var (o, f) in schema.FlattenedProperties)
        {
            if (Unflatten(f, name) is not NbtTag v || !FlattenMayProduce(f, o, name, schema, origin12)) continue;
            var st = new Dictionary<string, NbtTag>(states, StringComparer.Ordinal) { [f.Property] = v };
            candidates.Add((o, st));
            created = true;
        }
        if (!created && !schema.RenamedIds.ContainsKey(name)) candidates.Add((name, new Dictionary<string, NbtTag>(states, StringComparer.Ordinal)));
        foreach (var (old, st) in candidates)
        {
            // Property changes were made under the old id; undo them in reverse order. A renamed property had its value
            // remapped with the old key's table, so both are undone together.
            var renamedProps = schema.RenamedProperties.GetValueOrDefault(old);
            if (renamedProps != null)
                foreach (var (oldKey, newKey) in renamedProps)
                    if (st.Remove(newKey, out var value)) st[oldKey] = UnmapValue(schema, old, oldKey, value);
            if (schema.RemappedPropertyValues.TryGetValue(old, out var remapped))
                foreach (var prop in remapped.Keys)
                    if (renamedProps?.ContainsKey(prop) != true && st.TryGetValue(prop, out var value)) st[prop] = UnmapValue(schema, old, prop, value);
            if (schema.RemovedProperties.TryGetValue(old, out var removed))
                foreach (var k in removed) st.TryAdd(k, DefaultFor(k));
            if (schema.AddedProperties.TryGetValue(old, out var added))
                foreach (var k in added.Keys) st.Remove(k);
            yield return (old, st);
        }
    }

    /// <summary>
    /// Whether a flatten of <paramref name="oldId"/> could have created <paramref name="name"/>: flattens only made new
    /// names ("minecraft:dirt" is not a stone_type), and a block that existed in 1.12 can only come from the id it had
    /// at the time ("minecraft:white_wool" from "minecraft:wool", not from the chemistry table's bare "minecraft:" flatten).
    /// </summary>
    static bool FlattenMayProduce(Flatten f, string oldId, string name, Schema schema, string? origin12)
    {
        if (oldId == name || Legacy.Value.NameToId.ContainsKey(name)) return false;
        return origin12 != null ? OriginAt(origin12, schema) == oldId : !(f.Prefix == "minecraft:" && f.Suffix.Length == 0);
    }

    static NbtTag UnmapValue(Schema schema, string name, string property, NbtTag value)
    {
        if (schema.RemappedPropertyValues.TryGetValue(name, out var perBlock) && perBlock.TryGetValue(property, out var list))
            foreach (var (o, n) in list)
                if (TagEquals(n, value)) return o;
        return value;
    }

    /// <summary>Value of a property an old version had and a newer one removed (only flags and counters were removed).</summary>
    static NbtTag DefaultFor(string property) => property.EndsWith("_bit", StringComparison.Ordinal) ? new NbtByte(0)
        : property.EndsWith("_type", StringComparison.Ordinal) ? new NbtString("")
        : new NbtInt(0);

    /// <summary>The flattened property value a name came from, or null when the name is not one of this flatten's.</summary>
    static NbtTag? Unflatten(Flatten f, string name)
    {
        if (name.Length <= f.Prefix.Length + f.Suffix.Length || !name.StartsWith(f.Prefix, StringComparison.Ordinal) || !name.EndsWith(f.Suffix, StringComparison.Ordinal)) return null;
        string embed = name[f.Prefix.Length..^f.Suffix.Length];
        string? raw = f.ValueRemaps.FirstOrDefault(kv => kv.Value == embed).Key;
        // A value that is itself remapped to something else never appears in names.
        if (raw == null && f.ValueRemaps.TryGetValue(embed, out var other) && other != embed) return null;
        raw ??= embed;
        return f.Type switch
        {
            NbtType.Byte => sbyte.TryParse(raw, out var b) ? new NbtByte(b) : null,
            NbtType.Int => int.TryParse(raw, out var i) ? new NbtInt(i) : null,
            _ => new NbtString(raw),
        };
    }

    /// <summary>Serialized palette entry for a downgraded state.</summary>
    public static byte[] PaletteEntryBytes(string name, Dictionary<string, NbtTag> states, int version)
    {
        var c = new NbtCompound();
        c.SetString("name", name);
        var st = new NbtCompound();
        foreach (var (k, v) in states.OrderBy(kv => kv.Key, StringComparer.Ordinal)) st[k] = v;
        c["states"] = st;
        c.SetInt("version", version);
        return NbtBinary.Serialize(c, NbtFormat.Bedrock);
    }

    /// <summary>Serialized {name, val} palette entry (1.2.13 – 1.12).</summary>
    public static byte[] NameValEntryBytes(string name, int meta)
    {
        var c = new NbtCompound();
        c.SetString("name", name);
        c.SetShort("val", (short)meta);
        return NbtBinary.Serialize(c, NbtFormat.Bedrock);
    }
}
