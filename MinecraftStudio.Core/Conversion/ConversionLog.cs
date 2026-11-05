// MinecraftStudio.Core/Conversion/ConversionLog.cs: What a change in a conversion was about.
// Owns: LossKind, LossEntry, RescueGroup, ConversionLog, RescueChests.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>What a change in a conversion was about.</summary>
public enum LossKind
{
    Player,
    Item,
    Entity,
    Container,
    Block,
    Biome,
    World,
}

/// <summary>One kind of change a conversion made: what it was, what became of it, how often and where it was first seen.</summary>
public sealed class LossEntry
{
    public required LossKind Kind { get; init; }
    public required string What { get; init; }
    public required string Outcome { get; init; }
    /// <summary>True when it is gone from the converted world (no stand-in, placeholder or rescue chest kept it).</summary>
    public bool Lost { get; init; }
    public long Count { get; set; }
    public string FirstSeen { get; set; } = "";

    public override string ToString() =>
        $"{What}: {Outcome}" + (Count > 1 ? $" (×{Count:N0})" : "") + (FirstSeen.Length > 0 ? $" — first at {FirstSeen}" : "");
}

/// <summary>Items kept for one rescue chest label ("Steve's items", "Items from a chiseled bookshelf").</summary>
public sealed class RescueGroup
{
    public required string Label { get; init; }
    public List<NbtCompound> Items { get; } = [];
}

/// <summary>
/// Everything a conversion changed or could not carry over. The item, entity and block entity converters report into the
/// log of the conversion running on the current thread, and hand over items that would otherwise disappear with
/// something the target cannot hold; the converter puts those into chests at the spawn.
/// </summary>
public sealed class ConversionLog
{
    static readonly AsyncLocal<ConversionLog?> Active = new();
    readonly object _lock = new();
    readonly Dictionary<(LossKind, string, string), LossEntry> _entries = [];
    readonly List<RescueGroup> _rescued = [];

    ConversionLog(ConversionTarget target) { Target = target; }

    /// <summary>The log of the conversion running on this thread; null outside a conversion.</summary>
    public static ConversionLog? Current => Active.Value;

    public ConversionTarget Target { get; }

    /// <summary>Where the converter is working (dimension and chunk), used as "first seen at".</summary>
    public string Where { get; set; } = "";

    readonly Dictionary<long, int> _bedrockMaps = [];
    int _nextMapId;

    /// <summary>
    /// The Java map id for a Bedrock map id: Bedrock ids are 64-bit, Java ids small numbers, so maps are renumbered from 0
    /// in the order they are met and every item holding a map follows the same numbers.
    /// </summary>
    public int MapIdForBedrock(long bedrockId)
    {
        lock (_lock)
        {
            if (!_bedrockMaps.TryGetValue(bedrockId, out int id)) _bedrockMaps[bedrockId] = id = _nextMapId++;
            return id;
        }
    }

    /// <summary>Bedrock to Bedrock with the world's add-on packs copied: add-on blocks, items and mobs stay as they are.</summary>
    public bool KeepAddOns { get; set; }

    /// <summary>The source is a Bedrock world (ids outside minecraft: are add-ons, not Java mods).</summary>
    public bool SourceIsBedrock { get; set; }

    /// <summary>False once the rescue chests were placed: later items can no longer be rescued.</summary>
    public bool AcceptsRescues { get; set; } = true;

    /// <summary>Makes a new log current for this thread until the returned scope is disposed.</summary>
    public static IDisposable Start(ConversionTarget target, out ConversionLog log)
    {
        var previous = Active.Value;
        log = new ConversionLog(target);
        Active.Value = log;
        return new Scope(previous);
    }

    sealed class Scope(ConversionLog? previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }

    public IReadOnlyList<LossEntry> Entries
    {
        get { lock (_lock) return [.. _entries.Values.OrderBy(e => e.Kind).ThenBy(e => e.Lost ? 0 : 1).ThenByDescending(e => e.Count)]; }
    }

    public IReadOnlyList<RescueGroup> Rescued
    {
        get { lock (_lock) return [.. _rescued]; }
    }

    public void Add(LossKind kind, string what, string outcome, long count = 1, bool lost = false, string? where = null)
    {
        lock (_lock)
        {
            var key = (kind, what, outcome);
            if (!_entries.TryGetValue(key, out var e))
                _entries[key] = e = new LossEntry { Kind = kind, What = what, Outcome = outcome, Lost = lost, FirstSeen = where ?? Where };
            e.Count += count;
        }
    }

    /// <summary>Records a change in the current conversion (does nothing outside one).</summary>
    public static void Note(LossKind kind, string what, string outcome, long count = 1, bool lost = false, string? where = null) =>
        Current?.Add(kind, what, outcome, count, lost, where);

    /// <summary>
    /// Keeps canonical items for the rescue chests. Returns false outside a conversion or after the chests were placed
    /// (the caller then reports the items as lost).
    /// </summary>
    public static bool Rescue(string label, IEnumerable<NbtCompound> canonicalItems)
    {
        var log = Current;
        if (log == null || !log.AcceptsRescues) return false;
        var items = canonicalItems.Where(i => !IsEmptyItem(i)).Select(i => (NbtCompound)i.Clone()).ToList();
        if (items.Count == 0) return true;
        lock (log._lock)
        {
            var group = log._rescued.FirstOrDefault(g => g.Label == label);
            if (group == null) log._rescued.Add(group = new RescueGroup { Label = label });
            group.Items.AddRange(items);
        }
        return true;
    }

    public static bool IsEmptyItem(NbtCompound item) =>
        (item.GetString("id") ?? "minecraft:air") == "minecraft:air" || item.GetInt("count", item.GetInt("Count", 1)) <= 0;

    /// <summary>"minecraft:chiseled_bookshelf" → "chiseled bookshelf"; modded ids keep their namespace.</summary>
    public static string Friendly(string id)
    {
        int colon = id.IndexOf(':');
        string ns = colon < 0 ? "minecraft" : id[..colon], name = colon < 0 ? id : id[(colon + 1)..];
        name = name.Replace('_', ' ');
        return ns == "minecraft" ? name : $"{name} ({ns})";
    }
}

/// <summary>
/// Puts rescued items into named chests at the spawn: every stack the converted world could not hold where it was
/// (players the target cannot link to an account, slots or containers the version lacks, mobs it does not have).
/// </summary>
public static class RescueChests
{
    const int ChestSlots = 27;

    /// <summary>The chunk the chests go in: the spawn chunk when it is converted, otherwise the converted chunk nearest to it.</summary>
    public static ChunkPos? PickChunk(IReadOnlyCollection<ChunkPos> converted, WorldInfo info)
    {
        if (converted.Count == 0) return null;
        var spawn = new ChunkPos(info.SpawnX >> 4, info.SpawnZ >> 4);
        return converted.Contains(spawn) ? spawn
            : converted.MinBy(p => (long)(p.X - spawn.X) * (p.X - spawn.X) + (long)(p.Z - spawn.Z) * (p.Z - spawn.Z));
    }

    /// <summary>
    /// Places one chest per 27 rescued stacks on the surface of <paramref name="c"/>, on every other column so no two
    /// chests join into a double chest, between <paramref name="minY"/> and <paramref name="maxY"/> (the heights the
    /// target holds, in source coordinates). Returns the positions used and how many stacks could not be placed.
    /// </summary>
    public static (List<(int X, int Y, int Z, string Label)> Placed, int Unplaced) Place(CanonicalChunk c, IReadOnlyList<RescueGroup> groups, int spawnX, int spawnZ, int minY, int maxY)
    {
        var placed = new List<(int, int, int, string)>();
        var chests = new List<(string Label, List<NbtCompound> Items)>();
        foreach (var g in groups)
        {
            int parts = (g.Items.Count + ChestSlots - 1) / ChestSlots;
            for (int i = 0; i < parts; i++)
                chests.Add((parts > 1 ? $"{g.Label} ({i + 1}/{parts})" : g.Label, g.Items.Skip(i * ChestSlots).Take(ChestSlots).ToList()));
        }
        // Columns on odd coordinates (never next to each other), skipping the spawn column: those whose ground is near
        // the spawn's ground first (not the bottom of a pit or the top of a tree), then the nearest.
        int sx = Math.Clamp(spawnX - c.X * 16, 0, 15), sz = Math.Clamp(spawnZ - c.Z * 16, 0, 15);
        int reference = Surface(c, sx, sz, minY, maxY);
        if (reference == int.MinValue) reference = maxY;
        var columns = Enumerable.Range(0, 8).SelectMany(i => Enumerable.Range(0, 8).Select(j => (X: i * 2 + 1, Z: j * 2 + 1)))
            .Where(p => p.X != sx || p.Z != sz)
            .Select(p => (p.X, p.Z, Y: Surface(c, p.X, p.Z, minY, maxY)))
            .Where(p => p.Y != int.MinValue)
            .OrderBy(p => Math.Abs(p.Y - reference) <= 4 ? 0 : 1)
            .ThenBy(p => (p.X - sx) * (p.X - sx) + (p.Z - sz) * (p.Z - sz))
            .Select(p => (p.X, p.Z)).ToList();
        var chest = BlockState.Parse("minecraft:chest[facing=south,type=single,waterlogged=false]");
        int unplaced = 0, next = 0;
        foreach (var (label, items) in chests)
        {
            int y = int.MinValue, x = 0, z = 0;
            while (next < columns.Count && y == int.MinValue)
            {
                (x, z) = columns[next++];
                y = Surface(c, x, z, minY, maxY);
            }
            if (y == int.MinValue) { unplaced += items.Count; continue; }
            int wx = c.X * 16 + x, wz = c.Z * 16 + z;
            c.SetBlock(x, y, z, chest);
            c.BlockEntities.RemoveAll(b => b.GetInt("x") == wx && b.GetInt("y") == y && b.GetInt("z") == wz);
            var list = new NbtList(NbtType.Compound);
            for (int i = 0; i < items.Count; i++)
            {
                var it = (NbtCompound)items[i].Clone();
                it.SetByte("Slot", (sbyte)i);
                list.Add(it);
            }
            c.BlockEntities.Add(new NbtCompound
            {
                { "id", new NbtString("minecraft:chest") }, { "x", new NbtInt(wx) }, { "y", new NbtInt(y) }, { "z", new NbtInt(wz) },
                { "Items", list }, { "CustomName", new NbtString(TextComponents.FromPlain(label)) },
            });
            placed.Add((wx, y, wz, label));
        }
        return (placed, unplaced);
    }

    /// <summary>The first free block above the highest block of a column, or int.MinValue when the column is full.</summary>
    static int Surface(CanonicalChunk c, int x, int z, int minY, int maxY)
    {
        int top = c.Sections.Count == 0 ? minY : Math.Min(maxY, c.Sections.Keys.Max() * 16 + 15);
        for (int y = top; y >= minY; y--)
            if (!c.GetBlock(x, y, z).IsAir)
                return y + 1 <= maxY && c.GetBlock(x, y + 1, z).IsAir ? y + 1 : int.MinValue;
        return minY + 1 <= maxY ? minY + 1 : int.MinValue;
    }
}
