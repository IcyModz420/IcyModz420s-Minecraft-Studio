// MinecraftStudio.Core/World/PetsHome.cs: lost pets brought back to their owners, in a copy of the world.
// Owns: PetsHome, PetMove. Every tamed mob of a dimension whose owner is a player of the world standing in that dimension,
// farther than PetsHome.Near blocks from them, is taken out of its chunk and put beside its owner (around them, a little
// apart, still and not falling) through StructurePlacement.EditIntoCopy — only those chunks are rewritten. A sitting pet
// keeps sitting (it waits where it is put). The copy is then read back: each pet once, beside its owner. Riders ride
// along. The world itself is only read.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

/// <summary>One pet to bring home: what (and its name), whose, from which chunk, and where it goes.</summary>
public sealed record PetMove(string Kind, string? Name, string Owner, Guid Uuid, ChunkPos From, double ToX, double ToY, double ToZ);

public static class PetsHome
{
    /// <summary>A pet this close to its owner is home already.</summary>
    public const int Near = 24;

    /// <summary>
    /// The pets of <paramref name="dim"/> to bring to their owners (those of <paramref name="onlyOwner"/>, when given),
    /// each with the spot beside its owner it goes to.
    /// </summary>
    public static List<PetMove> Plan(SaveWorld world, WorldDimension dim, string? onlyOwner = null, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        string dimId = CanonicalReader.DimensionId(dim);
        // The owners standing in this dimension, by UUID (the UUID a pet's Owner holds).
        // Named as the world's players are everywhere else (the Players page, Find's "tamed by").
        var names = WorldSearch.Owners(world);
        var owners = new Dictionary<Guid, (string Name, double X, double Y, double Z)>();
        foreach (var p in WorldInfoReader.Read(world).Players)
        {
            string name = names.TryGetValue(p.Uuid, out var known) ? known : p.Name;
            if (p.Dimension == dimId && p.Data.GetList("Pos") is { Count: 3 } pos && (onlyOwner == null || name.Equals(onlyOwner, StringComparison.OrdinalIgnoreCase)))
                owners.TryAdd(p.Uuid, (name, pos[0].AsDouble, pos[1].AsDouble, pos[2].AsDouble));
        }
        var moves = new List<PetMove>();
        if (owners.Count == 0) return moves;
        var reader = new CanonicalReader(world, dim);
        var count = new Dictionary<Guid, int>();
        int done = 0;
        foreach (var chunk in dim.ChunkPositions.Where(reader.Exists).OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5))
        {
            cancel.ThrowIfCancellationRequested();
            if (done++ % 64 == 0) progress?.Report($"Looking for pets: {done:N0} of {dim.ChunkPositions.Count:N0} chunks");
            if (reader.Get(chunk) is not { } c) continue;
            foreach (var e in c.Entities)
            {
                if (e.GetInts("Owner") is not { Length: 4 } o || !owners.TryGetValue(Players.UuidFromInts(o), out var owner)) continue;
                if (e.GetList("Pos") is not { Count: 3 } at || Uuid(e) is not Guid id) continue;
                double dx = at[0].AsDouble - owner.X, dz = at[2].AsDouble - owner.Z;
                if (dx * dx + dz * dz <= Near * Near) continue;
                // Around the owner, a little apart: eight to a ring, each ring a block farther out.
                var key = Players.UuidFromInts(o);
                int i = count[key] = count.GetValueOrDefault(key) + 1;
                double angle = (i - 1) % 8 * Math.PI / 4, radius = 2 + (i - 1) / 8;
                string? name = e["CustomName"] is NbtString { Value.Length: > 0 } n ? TextComponents.ToPlain(n.Value) : null;
                moves.Add(new PetMove(WorldSearch.Nice(e.GetString("id") ?? "mob"), name, owner.Name, id, chunk,
                    owner.X + Math.Cos(angle) * radius, owner.Y, owner.Z + Math.Sin(angle) * radius));
            }
        }
        return moves;
    }

    static Guid? Uuid(NbtCompound e) => e.GetInts("UUID") is { Length: 4 } u ? Players.UuidFromInts(u) : null;

    /// <summary>
    /// The planned pets moved beside their owners in a NEW copy of the world at <paramref name="output"/>. The notes say who
    /// came home to whom; the problems, any pet not found once beside its owner in the copy.
    /// </summary>
    public static PlacementResult BringIntoCopy(SaveWorld world, WorldDimension dim, IReadOnlyList<PetMove> moves, string output,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        if (world is not (Java.JavaWorld or Bedrock.BedrockWorld)) throw new NotSupportedException("Pets are brought home in Java and Bedrock worlds: other editions' pets can't be matched to their owners.");
        if (moves.Count == 0) throw new ArgumentException("There are no pets to bring home: each is beside its owner already, or its owner is not in this dimension.");
        string dimId = CanonicalReader.DimensionId(dim);
        // The pets as they are now, taken from the world first (the copy's chunks are rewritten in any order).
        var reader = new CanonicalReader(world, dim);
        var wanted = moves.ToDictionary(m => m.Uuid);
        var taken = new Dictionary<Guid, NbtCompound>();
        foreach (var from in moves.Select(m => m.From).Distinct())
            if (reader.Get(from) is { } c)
                foreach (var e in c.Entities)
                    if (Uuid(e) is Guid id && wanted.ContainsKey(id)) taken[id] = (NbtCompound)e.Clone();
        var arriving = new Dictionary<ChunkPos, List<NbtCompound>>();
        var stored = dim.ChunkPositions.ToHashSet();
        int nowhere = 0;
        foreach (var m in moves)
        {
            if (!taken.TryGetValue(m.Uuid, out var pet)) continue;
            var to = new ChunkPos((int)Math.Floor(m.ToX) >> 4, (int)Math.Floor(m.ToZ) >> 4);
            // Only into land the world has: a pet is never taken out of its chunk without a chunk to go to.
            if (!stored.Contains(to)) { taken.Remove(m.Uuid); nowhere++; continue; }
            Settle(pet, m.ToX, m.ToY, m.ToZ);
            if (!arriving.TryGetValue(to, out var list)) arriving[to] = list = [];
            list.Add(pet);
        }
        if (taken.Count == 0) throw new IOException("None of the pets can be brought home: the land beside their owners is not generated.");
        var chunks = moves.Where(m => taken.ContainsKey(m.Uuid)).Select(m => m.From).Concat(arriving.Keys).Distinct().ToList();
        void Edit(CanonicalChunk c)
        {
            var here = new ChunkPos(c.X, c.Z);
            c.Entities.RemoveAll(e => Uuid(e) is Guid id && taken.ContainsKey(id));
            if (arriving.TryGetValue(here, out var pets)) c.Entities.AddRange(pets.Select(p => (NbtCompound)p.Clone()));
        }
        var result = StructurePlacement.EditIntoCopy(world, dimId, chunks, Edit, output, progress, cancel);
        foreach (var g in moves.Where(m => taken.ContainsKey(m.Uuid)).GroupBy(m => m.Owner))
            result.Notes.Add($"{g.Key}: {string.Join(", ", g.Take(8).Select(m => m.Name != null ? $"{m.Name} the {m.Kind}" : $"a {m.Kind}"))}{(g.Count() > 8 ? $" and {g.Count() - 8} more" : "")} brought home.");
        if (nowhere > 0) result.Notes.Add($"{nowhere} pet(s) stayed where they were: the land beside their owner is not generated.");
        if (taken.Count + nowhere < moves.Count) result.Notes.Add($"{moves.Count - taken.Count - nowhere} pet(s) could not be read any more, so they stayed where they were.");

        // Read back: each pet once in the copy, in the chunk it was put in.
        using var copy = WorldLoader.Open(result.OpenPath);
        var copyDim = copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimId);
        var back = new CanonicalReader(copy, copyDim);
        var seen = new Dictionary<Guid, int>();
        foreach (var pos in chunks.Concat(arriving.Keys).Distinct())
            if (back.Get(pos) is { } c)
                foreach (var e in c.Entities)
                    if (Uuid(e) is Guid id && taken.ContainsKey(id)) seen[id] = seen.GetValueOrDefault(id) + (arriving.TryGetValue(pos, out var l) && l.Any(p => Uuid(p) == id) ? 1 : 100);
        int wrong = taken.Keys.Count(id => seen.GetValueOrDefault(id) != 1);
        if (wrong > 0) result.Problems.Add($"{wrong} pet(s) are not once beside their owner in the copy");
        return result;
    }

    /// <summary>A pet put down at a spot: there, still, not falling, not burning.</summary>
    static void Settle(NbtCompound pet, double x, double y, double z)
    {
        double dx = 0, dy = 0, dz = 0;
        if (pet.GetList("Pos") is { Count: 3 } old) { dx = x - old[0].AsDouble; dy = y - old[1].AsDouble; dz = z - old[2].AsDouble; }
        pet["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(x), new NbtDouble(y), new NbtDouble(z) };
        pet["Motion"] = new NbtList(NbtType.Double) { new NbtDouble(0), new NbtDouble(0), new NbtDouble(0) };
        pet.SetFloat("FallDistance", 0);
        if (pet.ContainsKey("fall_distance")) pet.SetDouble("fall_distance", 0);
        pet.SetShort("Fire", -1);
        // Riders come along, where they sit.
        foreach (var rider in pet.GetList("Passengers")?.OfType<NbtCompound>() ?? [])
            if (rider.GetList("Pos") is { Count: 3 } rp) Settle(rider, rp[0].AsDouble + dx, rp[1].AsDouble + dy, rp[2].AsDouble + dz);
    }
}
