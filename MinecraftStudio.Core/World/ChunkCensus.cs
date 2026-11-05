// MinecraftStudio.Core/World/ChunkCensus.cs: the busiest chunks of a world — where lag comes from.
// Owns: ChunkCensus, CensusResult and BusyChunk — every chunk of a dimension read (any edition, through CanonicalReader)
// and counted: entities (mobs, minecarts, item frames …), dropped items and experience orbs among them, and block
// entities (hoppers, chests, furnaces, spawners …); the busiest chunks first, with what is most common in each, and the
// totals by kind for the whole dimension. Only reads.
using MinecraftStudio.Core.Conversion;

namespace MinecraftStudio.Core.World;

/// <summary>One chunk's count: its entities (of them, dropped items and experience orbs) and block entities, and the most common kinds.</summary>
public sealed record BusyChunk(ChunkPos Pos, int Entities, int Dropped, int BlockEntities, IReadOnlyList<(string Id, int Count)> Most)
{
    /// <summary>How busy it is: everything in it the game ticks or keeps track of.</summary>
    public int Load => Entities + BlockEntities;
}

public sealed class CensusResult
{
    /// <summary>The busiest chunks, busiest first (at most the number asked for; chunks with nothing left out).</summary>
    public List<BusyChunk> Chunks { get; } = [];
    public Dictionary<string, long> EntityTotals { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> BlockEntityTotals { get; } = new(StringComparer.Ordinal);
    public int ChunksRead { get; set; }
    public List<string> Warnings { get; } = [];
}

public static class ChunkCensus
{
    /// <summary>Entities that pile up and slow a world down when there are many of them.</summary>
    static bool IsDropped(string id) => id is "minecraft:item" or "minecraft:experience_orb";

    public static CensusResult Run(SaveWorld world, WorldDimension dim, int keep = 200, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var r = new CensusResult();
        var reader = new CanonicalReader(world, dim);
        var positions = dim.ChunkPositions.Where(reader.Exists).OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5).ThenBy(p => p.X).ThenBy(p => p.Z).ToList();
        var busy = new List<BusyChunk>();
        foreach (var pos in positions)
        {
            cancel.ThrowIfCancellationRequested();
            if (r.ChunksRead % 64 == 0) progress?.Report($"Counted {r.ChunksRead:N0} of {positions.Count:N0} chunks of {dim.DisplayName}");
            r.ChunksRead++;
            if (reader.Get(pos) is not { } c) continue;
            var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            int entities = 0, dropped = 0;
            void Entity(Nbt.NbtCompound e)
            {
                string id = e.GetString("id") ?? "?";
                entities++;
                if (IsDropped(id)) dropped++;
                kinds[id] = kinds.GetValueOrDefault(id) + 1;
                r.EntityTotals[id] = r.EntityTotals.GetValueOrDefault(id) + 1;
                // Riders are entities of their own.
                foreach (var rider in e.GetList("Passengers")?.OfType<Nbt.NbtCompound>() ?? []) Entity(rider);
            }
            foreach (var e in c.Entities) Entity(e);
            foreach (var be in c.BlockEntities)
            {
                string id = be.GetString("id") ?? "?";
                if (!id.Contains(':')) id = "minecraft:" + id.ToLowerInvariant();
                kinds[id] = kinds.GetValueOrDefault(id) + 1;
                r.BlockEntityTotals[id] = r.BlockEntityTotals.GetValueOrDefault(id) + 1;
            }
            if (entities + c.BlockEntities.Count == 0) continue;
            var most = kinds.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(3).Select(k => (k.Key, k.Value)).ToList();
            busy.Add(new BusyChunk(pos, entities, dropped, c.BlockEntities.Count, most));
        }
        r.Chunks.AddRange(busy.OrderByDescending(b => b.Load).ThenByDescending(b => b.Dropped).ThenBy(b => b.Pos.X).ThenBy(b => b.Pos.Z).Take(Math.Max(1, keep)));
        r.Warnings.AddRange(reader.Warnings.Take(20));
        return r;
    }
}
