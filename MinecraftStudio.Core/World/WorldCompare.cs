// MinecraftStudio.Core/World/WorldCompare.cs: what differs between two worlds — a world and its backup, or a world and
// its conversion to another edition (blocks are compared by their canonical names, so editions can be compared).
// Owns: WorldCompare, CompareResult and ChunkDiff — every chunk of one dimension in either world: blocks that differ
// (with the commonest changes, "stone → air"), block entities added, gone or changed (a chest's contents), how many
// entities of each kind, and chunks only one of the worlds has. Only reads.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>Where a chunk is: in both worlds, or only in one.</summary>
public enum ChunkPresence { Both, OnlyFirst, OnlySecond }

/// <summary>One chunk that differs.</summary>
public sealed record ChunkDiff(ChunkPos Pos, ChunkPresence Presence, int Blocks, int BlockEntities, int Entities, IReadOnlyList<(string From, string To, int Count)> Top)
{
    public int Total => Blocks + BlockEntities + Entities;
}

public sealed class CompareResult
{
    /// <summary>The chunks that differ, the most changed first.</summary>
    public List<ChunkDiff> Chunks { get; } = [];
    /// <summary>Every block change ("from → to") with how often, over the whole dimension.</summary>
    public Dictionary<(string From, string To), long> Changes { get; } = [];
    public int Compared { get; set; }
    public int Same { get; set; }
    public long BlocksChanged { get; set; }
    public List<string> Warnings { get; } = [];
}

public static class WorldCompare
{
    /// <summary>
    /// Compares dimension <paramref name="da"/> of <paramref name="a"/> with <paramref name="db"/> of <paramref name="b"/>,
    /// chunk by chunk. A chunk's position may be moved (<paramref name="offsetX"/>, <paramref name="offsetZ"/> chunks: a
    /// console world converted to Java lands elsewhere); y is compared where both have blocks.
    /// </summary>
    public static CompareResult Run(SaveWorld a, WorldDimension da, SaveWorld b, WorldDimension db, int offsetX = 0, int offsetZ = 0,
        IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var r = new CompareResult();
        var ra = new CanonicalReader(a, da);
        var rb = new CanonicalReader(b, db);
        var inA = da.ChunkPositions.Where(ra.Exists).ToHashSet();
        var inB = db.ChunkPositions.Where(rb.Exists).Select(p => new ChunkPos(p.X - offsetX, p.Z - offsetZ)).ToHashSet();
        var all = inA.Union(inB).OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5).ThenBy(p => p.X).ThenBy(p => p.Z).ToList();
        int done = 0;
        foreach (var pos in all)
        {
            cancel.ThrowIfCancellationRequested();
            if (done++ % 64 == 0) progress?.Report($"Compared {done:N0} of {all.Count:N0} chunks");
            bool hasA = inA.Contains(pos), hasB = inB.Contains(pos);
            var ca = hasA ? ra.Get(pos) : null;
            var cb = hasB ? rb.Get(new ChunkPos(pos.X + offsetX, pos.Z + offsetZ)) : null;
            if (ca == null && cb == null) continue;
            if (ca == null || cb == null)
            {
                var only = ca ?? cb!;
                int blocks = only.Sections.Values.Sum(s => s.Indices == null ? (s.Palette[0].IsAir ? 0 : 4096) : s.Indices.Count(i => !s.Palette[i].IsAir));
                r.Chunks.Add(new ChunkDiff(pos, ca == null ? ChunkPresence.OnlySecond : ChunkPresence.OnlyFirst, blocks, only.BlockEntities.Count, only.Entities.Count, []));
                continue;
            }
            r.Compared++;
            var changes = new Dictionary<(string, string), int>();
            int changed = 0;
            // Only the heights both chunks have (a 1.18 world and its 1.12 conversion differ in height, not in blocks).
            var ys = ca.Sections.Keys.Intersect(cb.Sections.Keys).ToList();
            foreach (var sy in ys)
                for (int y = sy * 16; y < sy * 16 + 16; y++)
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                        {
                            var p = ca.GetBlock(x, y, z);
                            var q = cb.GetBlock(x, y, z);
                            if (p == q || p.IsAir && q.IsAir) continue;
                            changed++;
                            changes[(p.Name, q.Name)] = changes.GetValueOrDefault((p.Name, q.Name)) + 1;
                        }
            foreach (var sy in ca.Sections.Keys.Except(ys).Concat(cb.Sections.Keys.Except(ys)).Distinct())
            {
                // A section only one has: what it holds that is not air is different.
                var s = ca.Sections.TryGetValue(sy, out var sa) ? sa : cb.Sections[sy];
                int solid = s.Indices == null ? (s.Palette[0].IsAir ? 0 : 4096) : s.Indices.Count(i => !s.Palette[i].IsAir);
                changed += solid;
            }
            int tiles = BlockEntityChanges(ca, cb);
            int entities = EntityChanges(ca, cb);
            foreach (var (k, v) in changes) r.Changes[k] = r.Changes.GetValueOrDefault(k) + v;
            r.BlocksChanged += changed;
            if (changed + tiles + entities == 0) { r.Same++; continue; }
            var top = changes.OrderByDescending(kv => kv.Value).Take(3).Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
            r.Chunks.Add(new ChunkDiff(pos, ChunkPresence.Both, changed, tiles, entities, top));
        }
        r.Chunks.Sort((x, y) => y.Total.CompareTo(x.Total) is var c and not 0 ? c : x.Pos.X != y.Pos.X ? x.Pos.X.CompareTo(y.Pos.X) : x.Pos.Z.CompareTo(y.Pos.Z));
        r.Warnings.AddRange(ra.Warnings.Concat(rb.Warnings).Take(20));
        return r;
    }

    /// <summary>Block entities at positions only one chunk has, or whose contents differ (positions and ids aside).</summary>
    static int BlockEntityChanges(CanonicalChunk a, CanonicalChunk b)
    {
        static Dictionary<(int, int, int), NbtCompound> At(CanonicalChunk c)
        {
            var d = new Dictionary<(int, int, int), NbtCompound>();
            foreach (var be in c.BlockEntities) d[((be.GetInt("x") & 15), be.GetInt("y"), (be.GetInt("z") & 15))] = be;
            return d;
        }
        var pa = At(a);
        var pb = At(b);
        int n = 0;
        foreach (var (k, be) in pa)
            if (!pb.TryGetValue(k, out var other) || !Content(be).ContentEquals(Content(other))) n++;
        n += pb.Keys.Count(k => !pa.ContainsKey(k));
        return n;
    }

    /// <summary>A block entity without its own position (a world and a copy moved elsewhere compare the same).</summary>
    static NbtCompound Content(NbtCompound be)
    {
        var c = (NbtCompound)be.Clone();
        foreach (var k in new[] { "x", "y", "z", "keepPacked" }) c.Remove(k);
        return c;
    }

    /// <summary>How many entities more or fewer of each kind (mobs move, so only their numbers are compared).</summary>
    static int EntityChanges(CanonicalChunk a, CanonicalChunk b)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in a.Entities) { string id = e.GetString("id") ?? "?"; count[id] = count.GetValueOrDefault(id) + 1; }
        foreach (var e in b.Entities) { string id = e.GetString("id") ?? "?"; count[id] = count.GetValueOrDefault(id) - 1; }
        return count.Values.Sum(Math.Abs);
    }
}
