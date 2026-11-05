// MinecraftStudio.Core/Conversion/BedrockBlocks.Upgrade.cs: upgrade for BedrockBlocks.
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
    // ---------------------------------------------------------------- upgrade schemas

    sealed class Flatten
    {
        public string Prefix = "", Property = "", Suffix = "";
        public NbtType Type = NbtType.String;
        public Dictionary<string, string> ValueRemaps = new(StringComparer.Ordinal);
    }

    sealed class StateRemap
    {
        public Dictionary<string, NbtTag> OldState = new(StringComparer.Ordinal);
        public string? NewName;
        public Flatten? NewFlattenedName;
        public Dictionary<string, NbtTag> NewState = new(StringComparer.Ordinal);
        public List<string> CopiedState = [];
    }

    sealed class Schema
    {
        public int Id;
        public int Version;
        /// <summary>Game version (major.minor.patch, revision 0) whose states this schema produces, from the file name.</summary>
        public int ToGame;
        public string File = "";
        public Dictionary<string, string> RenamedIds = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, NbtTag>> AddedProperties = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> RemovedProperties = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> RenamedProperties = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, List<(NbtTag Old, NbtTag New)>>> RemappedPropertyValues = new(StringComparer.Ordinal);
        public Dictionary<string, Flatten> FlattenedProperties = new(StringComparer.Ordinal);
        public Dictionary<string, List<StateRemap>> RemappedStates = new(StringComparer.Ordinal);
    }

    static readonly Lazy<List<List<Schema>>> Schemas = new(LoadSchemas);

    static NbtTag ReadTagModel(JsonElement e)
    {
        if (e.TryGetProperty("byte", out var b)) return new NbtByte((sbyte)b.GetInt32());
        if (e.TryGetProperty("int", out var i)) return new NbtInt(i.GetInt32());
        if (e.TryGetProperty("string", out var s)) return new NbtString(s.GetString() ?? "");
        throw new SaveFormatException("Upgrade schema tag has no byte/int/string value.");
    }

    static Dictionary<string, NbtTag> ReadTagMap(JsonElement e)
    {
        var d = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
        if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) d[p.Name] = ReadTagModel(p.Value);
        return d;
    }

    static Flatten ReadFlatten(JsonElement e)
    {
        var f = new Flatten
        {
            Prefix = e.TryGetProperty("prefix", out var p) ? p.GetString() ?? "" : "",
            Property = e.GetProperty("flattenedProperty").GetString() ?? "",
            Suffix = e.TryGetProperty("suffix", out var s) ? s.GetString() ?? "" : "",
            Type = e.TryGetProperty("flattenedPropertyType", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() switch { "byte" => NbtType.Byte, "int" => NbtType.Int, _ => NbtType.String } : NbtType.String,
        };
        if (e.TryGetProperty("flattenedValueRemaps", out var r) && r.ValueKind == JsonValueKind.Object)
            foreach (var kv in r.EnumerateObject()) f.ValueRemaps[kv.Name] = kv.Value.GetString() ?? "";
        return f;
    }

    /// <summary>"1.21.40.25_beta.json" or "1.26.30.json" → major.minor.patch as a version number (revision 0).</summary>
    static int ParseGameVersion(string text)
    {
        var parts = text.Split('.', '_').Select(p => int.TryParse(p, out int v) ? v : -1).TakeWhile(v => v >= 0).ToArray();
        return parts.Length >= 3 ? MakeVersion(parts[0], parts[1], parts[2], 0) : 0;
    }

    static List<List<Schema>> LoadSchemas()
    {
        using var doc = JsonDocument.Parse(EmbeddedData.Bytes("bedrock_upgrade_schemas.json.gz"));
        var all = new List<Schema>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string file = e.GetProperty("_file").GetString() ?? "";
            var s = new Schema
            {
                File = file,
                Id = int.Parse(file[..file.IndexOf('_')]),
                Version = MakeVersion(e.GetProperty("maxVersionMajor").GetInt32(), e.GetProperty("maxVersionMinor").GetInt32(),
                    e.GetProperty("maxVersionPatch").GetInt32(), e.GetProperty("maxVersionRevision").GetInt32()),
                ToGame = ParseGameVersion(file[(file.IndexOf("_to_", StringComparison.Ordinal) + 4)..]),
            };
            if (e.TryGetProperty("renamedIds", out var ri))
                foreach (var p in ri.EnumerateObject()) s.RenamedIds[p.Name] = p.Value.GetString() ?? p.Name;
            if (e.TryGetProperty("addedProperties", out var ap))
                foreach (var p in ap.EnumerateObject()) s.AddedProperties[p.Name] = ReadTagMap(p.Value);
            if (e.TryGetProperty("removedProperties", out var rp))
                foreach (var p in rp.EnumerateObject()) s.RemovedProperties[p.Name] = p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            if (e.TryGetProperty("renamedProperties", out var rn))
                foreach (var p in rn.EnumerateObject())
                    s.RenamedProperties[p.Name] = p.Value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString() ?? x.Name, StringComparer.Ordinal);
            if (e.TryGetProperty("remappedPropertyValues", out var rv) && e.TryGetProperty("remappedPropertyValuesIndex", out var index))
                foreach (var p in rv.EnumerateObject())
                {
                    var perProp = new Dictionary<string, List<(NbtTag, NbtTag)>>(StringComparer.Ordinal);
                    foreach (var q in p.Value.EnumerateObject())
                    {
                        var list = new List<(NbtTag, NbtTag)>();
                        foreach (var m in index.GetProperty(q.Value.GetString() ?? "").EnumerateArray())
                            list.Add((ReadTagModel(m.GetProperty("old")), ReadTagModel(m.GetProperty("new"))));
                        perProp[q.Name] = list;
                    }
                    s.RemappedPropertyValues[p.Name] = perProp;
                }
            if (e.TryGetProperty("flattenedProperties", out var fp))
                foreach (var p in fp.EnumerateObject()) s.FlattenedProperties[p.Name] = ReadFlatten(p.Value);
            if (e.TryGetProperty("remappedStates", out var rs))
                foreach (var p in rs.EnumerateObject())
                {
                    var list = new List<StateRemap>();
                    foreach (var m in p.Value.EnumerateArray())
                    {
                        var remap = new StateRemap();
                        if (m.TryGetProperty("oldState", out var os)) remap.OldState = ReadTagMap(os);
                        if (m.TryGetProperty("newName", out var nn) && nn.ValueKind == JsonValueKind.String) remap.NewName = nn.GetString();
                        if (m.TryGetProperty("newFlattenedName", out var nf) && nf.ValueKind == JsonValueKind.Object) remap.NewFlattenedName = ReadFlatten(nf);
                        if (m.TryGetProperty("newState", out var ns)) remap.NewState = ReadTagMap(ns);
                        if (m.TryGetProperty("copiedState", out var cs) && cs.ValueKind == JsonValueKind.Array) remap.CopiedState = cs.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                        list.Add(remap);
                    }
                    s.RemappedStates[p.Name] = list;
                }
            all.Add(s);
        }
        // Grouped by result version (ascending), schemas of one version in id order.
        return all.GroupBy(s => s.Version).OrderBy(g => g.Key).Select(g => g.OrderBy(s => s.Id).ToList()).ToList();
    }

    /// <summary>Upgrades a stored state of any version to the newest version known to the schemas.</summary>
    public static (string Name, Dictionary<string, NbtTag> States) Upgrade(string name, Dictionary<string, NbtTag> states, int version)
    {
        foreach (var group in Schemas.Value)
        {
            int resultVersion = group[0].Version;
            // Mojang sometimes changed states without bumping the version: then every schema of that version applies.
            if (version > resultVersion || (group.Count == 1 && version == resultVersion)) continue;
            foreach (var schema in group) (name, states) = Apply(schema, name, states);
        }
        return (name, states);
    }

    static bool TagEquals(NbtTag a, NbtTag b) => a.Type == b.Type && a.ContentEquals(b);

    static (string, Dictionary<string, NbtTag>) Apply(Schema schema, string name, Dictionary<string, NbtTag> states)
    {
        if (schema.RemappedStates.TryGetValue(name, out var remaps))
        {
            foreach (var remap in remaps)
            {
                if (remap.OldState.Count > states.Count) continue;
                bool match = true;
                foreach (var (k, v) in remap.OldState)
                    if (!states.TryGetValue(k, out var have) || !TagEquals(have, v)) { match = false; break; }
                if (!match) continue;
                string newName;
                var old = states;
                if (remap.NewName != null) newName = remap.NewName;
                else if (remap.NewFlattenedName != null) (newName, old) = ApplyFlatten(remap.NewFlattenedName, name, new Dictionary<string, NbtTag>(states, StringComparer.Ordinal));
                else newName = name;
                var result = new Dictionary<string, NbtTag>(remap.NewState, StringComparer.Ordinal);
                foreach (var copied in remap.CopiedState)
                    if (old.TryGetValue(copied, out var v)) result[copied] = v;
                return (newName, result);
            }
        }

        string? renamed = null;
        var s = new Dictionary<string, NbtTag>(states, StringComparer.Ordinal);
        if (schema.RenamedIds.TryGetValue(name, out var r)) renamed = r;
        else if (schema.FlattenedProperties.TryGetValue(name, out var flatten))
        {
            var (fn, fs) = ApplyFlatten(flatten, name, s);
            if (fn != name) { renamed = fn; s = fs; }
        }
        if (schema.AddedProperties.TryGetValue(name, out var added))
            foreach (var (k, v) in added) s.TryAdd(k, v);
        if (schema.RemovedProperties.TryGetValue(name, out var removed))
            foreach (var k in removed) s.Remove(k);
        if (schema.RenamedProperties.TryGetValue(name, out var renamedProps))
            foreach (var (oldKey, newKey) in renamedProps)
            {
                if (!s.Remove(oldKey, out var value)) continue;
                s[newKey] = LocateValue(schema, name, oldKey, value);
            }
        if (schema.RemappedPropertyValues.TryGetValue(name, out var remapped))
            foreach (var key in remapped.Keys)
                if (s.TryGetValue(key, out var value)) s[key] = LocateValue(schema, name, key, value);
        return (renamed ?? name, s);
    }

    static NbtTag LocateValue(Schema schema, string name, string property, NbtTag value)
    {
        if (schema.RemappedPropertyValues.TryGetValue(name, out var perBlock) && perBlock.TryGetValue(property, out var list))
            foreach (var (o, n) in list)
                if (TagEquals(o, value)) return n;
        return value;
    }

    static (string, Dictionary<string, NbtTag>) ApplyFlatten(Flatten f, string name, Dictionary<string, NbtTag> states)
    {
        if (!states.TryGetValue(f.Property, out var value) || value.Type != f.Type) return (name, states);
        string embed = value switch { NbtString s => s.Value, { IsNumeric: true } n => n.AsLong.ToString(), _ => "" };
        string newName = f.Prefix + (f.ValueRemaps.TryGetValue(embed, out var re) ? re : embed) + f.Suffix;
        states.Remove(f.Property);
        return (newName, states);
    }
}
