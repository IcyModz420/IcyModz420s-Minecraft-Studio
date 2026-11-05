// MinecraftStudio.Core/World/BlockState.cs: An interned block state (namespaced name + sorted properties).
// Legacy numeric blocks (id:data) are resolved to names through LegacyBlocks.
// Owns: BlockState.
using System.Collections.Concurrent;
using System.Text;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>
/// An interned block state (namespaced name + sorted properties). Legacy numeric blocks
/// (id:data) are resolved to names through <see cref="LegacyBlocks"/>.
/// </summary>
public sealed class BlockState
{
    static readonly ConcurrentDictionary<string, BlockState> Interned = new(StringComparer.Ordinal);
    static readonly List<BlockState> ById = [];
    static readonly object IdLock = new();

    BlockState(string name, string properties, int id)
    {
        Name = name;
        Properties = properties;
        Id = id;
        IsAir = name is "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air" or "air" || name.Length == 0;
        BaseName = name.StartsWith("minecraft:", StringComparison.Ordinal) ? name[10..] : name;
    }

    /// <summary>Namespaced name, e.g. minecraft:oak_log.</summary>
    public string Name { get; }
    /// <summary>Name without the minecraft: namespace.</summary>
    public string BaseName { get; }
    /// <summary>Sorted "key=value,key=value" (empty when the block has no properties).</summary>
    public string Properties { get; }
    public int Id { get; }
    public bool IsAir { get; }
    /// <summary>Legacy numeric id/data when this state came from (or maps to) a numeric block.</summary>
    public int LegacyId { get; internal set; } = -1;
    public int LegacyData { get; internal set; }

    /// <summary>Cached map colour (set by the renderer).</summary>
    public uint MapColor { get; set; }
    public byte MapTint { get; set; }
    public bool MapColorResolved { get; set; }
    /// <summary>Cached: water or a waterlogged block (set by the renderer).</summary>
    public bool IsWaterLike { get; set; }

    public static BlockState Air { get; } = Get("minecraft:air");
    public static int Count { get { lock (IdLock) return ById.Count; } }
    public static BlockState FromId(int id) { lock (IdLock) return ById[id]; }

    public static string Normalize(string name)
    {
        if (name.Length == 0) return "minecraft:air";
        return name.Contains(':') ? name : "minecraft:" + name;
    }

    public static BlockState Get(string name, string properties = "")
    {
        name = Normalize(name);
        string key = properties.Length == 0 ? name : name + "[" + properties + "]";
        if (Interned.TryGetValue(key, out var s)) return s;
        lock (IdLock)
        {
            if (Interned.TryGetValue(key, out s)) return s;
            s = new BlockState(name, properties, ById.Count);
            ById.Add(s);
            Interned[key] = s;
            return s;
        }
    }

    public static BlockState Get(string name, IEnumerable<KeyValuePair<string, string>>? properties) =>
        Get(name, properties == null ? "" : FormatProperties(properties));

    public static string FormatProperties(IEnumerable<KeyValuePair<string, string>> props)
    {
        var list = props.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        if (list.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var p in list) { if (sb.Length > 0) sb.Append(','); sb.Append(p.Key).Append('=').Append(p.Value); }
        return sb.ToString();
    }

    /// <summary>Parses "minecraft:oak_log[axis=y]".</summary>
    public static BlockState Parse(string text)
    {
        int b = text.IndexOf('[');
        if (b < 0) return Get(text.Trim());
        string name = text[..b].Trim();
        string inner = text[(b + 1)..].TrimEnd(']', ' ');
        var props = inner.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => new KeyValuePair<string, string>(p[0].Trim(), p[1].Trim()));
        return Get(name, props);
    }

    /// <summary>
    /// Java palette entry in any form: {Name, Properties} (1.13+), {id, properties} (26.3 snapshot 7+),
    /// a bare string, or a {"": "name"} wrapper from heterogeneous lists.
    /// </summary>
    public static BlockState FromJavaNbt(NbtTag entry)
    {
        if (entry is NbtString s) return Get(s.Value);
        if (entry is not NbtCompound c) return Air;
        if (c.Count == 1 && c[""] is NbtString wrapped) return Get(wrapped.Value);
        string name = c.GetString("Name") ?? c.GetString("id") ?? "minecraft:air";
        var props = c.GetCompound("Properties") ?? c.GetCompound("properties");
        if (props is { Count: > 0 })
            return Get(name, props.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value is NbtString v ? v.Value : kv.Value.ToString())));
        return Get(name);
    }

    public NbtCompound ToJavaNbt()
    {
        var c = new NbtCompound();
        c.SetString("Name", Name);
        if (Properties.Length > 0)
        {
            var p = new NbtCompound();
            foreach (var (k, v) in PropertyPairs()) p.SetString(k, v);
            c["Properties"] = p;
        }
        return c;
    }

    public IEnumerable<(string Key, string Value)> PropertyPairs()
    {
        if (Properties.Length == 0) yield break;
        foreach (var part in Properties.Split(','))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) yield return (part[..eq], part[(eq + 1)..]);
        }
    }

    public string? GetProperty(string key)
    {
        foreach (var (k, v) in PropertyPairs()) if (k == key) return v;
        return null;
    }

    public override string ToString() => Properties.Length == 0 ? Name : $"{Name}[{Properties}]";
}
