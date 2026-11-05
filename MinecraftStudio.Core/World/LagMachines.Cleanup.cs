// MinecraftStudio.Core/World/LagMachines.Cleanup.cs: lag machines cleaned up, in a copy of the world.
// Part of LagMachines (main part in LagMachines.cs). Owns: CleanIntoCopy — the chunks of the chosen machines (one
// dimension) rewritten by StructurePlacement.EditIntoCopy with only what made them lag taken out, by the finder's own
// rules: piles thinned to what fits in one block (mobs to the game's crowding rule of 24, named and tamed ones kept first;
// minecarts, boats, armour stands and falling blocks to one; item frames to one per face), floods of dropped items, orbs,
// lit TNT, projectiles, clouds and markers removed, solid masses and carpets of one part taken away, waiting updates gone
// with the rewrite (the writers save none), pistons caught mid-push removed, summoning command blocks emptied (their
// commands kept in the notes), book-ban containers and chunk-ban chunks relieved of their biggest items. The copy is then
// looked through again: none of the cleaned machines may be left. The world itself is only read.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

public static partial class LagMachines
{
    /// <summary>How many of a pile stay in one block: the game's crowding rule for mobs, one of anything else.</summary>
    const int MobsKeptInOneBlock = 24;

    /// <summary>
    /// The <paramref name="machines"/> (all in one dimension) cleaned up in a NEW copy of <paramref name="world"/> at
    /// <paramref name="output"/>. The notes say what was taken out of each; the problems, any machine still found after.
    /// </summary>
    public static PlacementResult CleanIntoCopy(SaveWorld world, IReadOnlyCollection<LagMachine> machines, string output,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        if (world is Legacy.FiniteWorld) throw new NotSupportedException("Classic and Indev levels hold no lag machines or dropped items to clean up.");
        if (machines.Count == 0) throw new ArgumentException("Choose the lag machines to clean up.");
        if (machines.Select(m => m.DimensionId).Distinct().Count() > 1) throw new ArgumentException("Clean up the machines of one dimension at a time.");
        var dim = world.Dimensions.FirstOrDefault(d => d.Id == machines.First().DimensionId) ?? throw new ArgumentException("The world has no such dimension.");
        string dimId = CanonicalReader.DimensionId(dim);
        var list = machines.ToList();
        var chunks = list.SelectMany(ChunksOf).Distinct().ToList();
        var done = list.ToDictionary(m => m, _ => new Taken());
        var gate = new object();
        void Edit(CanonicalChunk c)
        {
            foreach (var m in list)
                if (Touches(m, c))
                {
                    var t = new Taken();
                    Clean(c, m, t);
                    lock (gate) done[m].Add(t);
                }
        }
        var result = StructurePlacement.EditIntoCopy(world, dimId, chunks, Edit, output, progress, cancel);
        // A few of a kind are told one by one; many (dropped items cleared from 40 chunks) as one line.
        foreach (var kind in list.GroupBy(m => m.Kind))
        {
            if (kind.Count() <= 3)
            {
                foreach (var m in kind) if (done[m].Words(m) is string words) result.Notes.Add($"{m.Title} at {m.X}, {m.Y}, {m.Z}: {words}.");
                continue;
            }
            var all = new Taken();
            foreach (var m in kind) all.Add(done[m]);
            if (all.Words(kind.First()) is string sum) result.Notes.Add($"{kind.First().Title} ×{kind.Count():N0}: {sum}.");
        }

        // Looked through again, chunk by chunk: what was cleaned must not be found.
        progress?.Report(new ConversionProgress { Stage = "Looking through the cleaned chunks again", Done = 0, Total = 1 });
        using var copy = WorldLoader.Open(result.OpenPath);
        var copyDim = copy.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimId);
        if (copyDim == null) { result.Problems.Add("the copy no longer has this dimension"); return result; }
        var near = chunks.SelectMany(p => new[] { p, new ChunkPos(p.X - 1, p.Z), new ChunkPos(p.X + 1, p.Z), new ChunkPos(p.X, p.Z - 1), new ChunkPos(p.X, p.Z + 1) }).ToHashSet();
        var after = Find(copy, [copyDim], null, cancel, only: near);
        foreach (var m in list)
            if (after.Machines.FirstOrDefault(a => Same(a.Kind, m.Kind) && Overlap(a.Box, m.Box)) is { } left)
                result.Problems.Add($"{m.Title} at {m.X}, {m.Y}, {m.Z} is still there after the clean-up ({left.What})");
        return result;
    }

    /// <summary>The chunks a machine's box lies in.</summary>
    static IEnumerable<ChunkPos> ChunksOf(LagMachine m)
    {
        for (int cz = m.Box.MinZ >> 4; cz <= m.Box.MaxZ >> 4; cz++)
            for (int cx = m.Box.MinX >> 4; cx <= m.Box.MaxX >> 4; cx++)
                yield return new ChunkPos(cx, cz);
    }

    static bool Touches(LagMachine m, CanonicalChunk c) =>
        m.Box.MinX >> 4 <= c.X && c.X <= m.Box.MaxX >> 4 && m.Box.MinZ >> 4 <= c.Z && c.Z <= m.Box.MaxZ >> 4;

    static bool Overlap((int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) a, (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) b) =>
        a.MinX <= b.MaxX + 1 && b.MinX <= a.MaxX + 1 && a.MinY <= b.MaxY + 1 && b.MinY <= a.MaxY + 1 && a.MinZ <= b.MaxZ + 1 && b.MinZ <= a.MaxZ + 1;

    // A machine found again is the same one when it is the same sort of thing (a pile found as a chunk flood, say).
    static bool Same(LagKind a, LagKind b) => a == b || Group(a) == Group(b);

    static int Group(LagKind k) => k switch
    {
        LagKind.MobPile => 1, LagKind.MinecartStack => 2, LagKind.FallingBlocks => 3,
        LagKind.WaitingUpdates or LagKind.WaitingFluids => 4,
        _ => 100 + (int)k,
    };

    /// <summary>What was taken out of one machine, counted.</summary>
    sealed class Taken
    {
        public int Entities, Blocks, Items, Commands;
        public readonly Dictionary<string, int> Kinds = new(StringComparer.Ordinal);
        public readonly List<string> Named = [];
        public readonly List<string> CommandsEmptied = [];

        public void Entity(NbtCompound e)
        {
            Entities++;
            string id = EntityId(e);
            Kinds[id] = Kinds.GetValueOrDefault(id) + 1;
            if (Named.Count < 6 && e["CustomName"] is NbtTag name) Named.Add(WorldSearch.Nice(id) + " \"" + PlainName(name) + "\"");
        }

        public void Add(Taken t)
        {
            Entities += t.Entities; Blocks += t.Blocks; Items += t.Items; Commands += t.Commands;
            foreach (var (k, n) in t.Kinds) Kinds[k] = Kinds.GetValueOrDefault(k) + n;
            foreach (var n in t.Named) if (Named.Count < 6) Named.Add(n);
            CommandsEmptied.AddRange(t.CommandsEmptied);
        }

        public string? Words(LagMachine m)
        {
            var parts = new List<string>();
            if (Entities > 0) parts.Add($"took out {Many(Entities, Kinds, "entities")}" + (Named.Count > 0 ? $" (with names: {string.Join(", ", Named)})" : ""));
            if (Blocks > 0) parts.Add($"took away {Blocks:N0} {PartWords(m.Kind)}" + (m.Kind == LagKind.MovingPistons ? " caught mid-push" : ""));
            if (Items > 0) parts.Add($"took {Items:N0} of the biggest item stacks (books, shulker boxes …) out of its containers");
            if (Commands > 0) parts.Add($"emptied {Commands:N0} command block{(Commands == 1 ? "" : "s")}; the command{(Commands == 1 ? " was" : "s were")}: " + string.Join(" | ", CommandsEmptied.Take(4)));
            if (m.Kind is LagKind.WaitingUpdates or LagKind.WaitingFluids or LagKind.MovingPistons) parts.Add("the updates it left waiting are gone");
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
    }

    static string PlainName(NbtTag name) => name switch
    {
        NbtString { Value.Length: > 0 } s when s.Value[0] is '{' or '[' or '"' => TextComponents.ToPlain(s.Value),
        NbtString s => s.Value,
        _ => TextComponents.ToPlain(TextComponents.FromJavaTag(name)),
    };

    /// <summary>One machine's part of one chunk cleaned (the chunk is canonical: newest Java names and forms).</summary>
    static void Clean(CanonicalChunk c, LagMachine m, Taken t)
    {
        switch (m.Kind)
        {
            case LagKind.MinecartStack: Thin(c, m, Family.Minecart, t); break;
            case LagKind.BoatStack: Thin(c, m, Family.Boat, t); break;
            case LagKind.ArmourStandStack: Thin(c, m, Family.ArmourStand, t); break;
            case LagKind.MobPile: Thin(c, m, Family.Mob, t); break;
            case LagKind.FallingBlocks: Thin(c, m, Family.FallingBlock, t); break;
            case LagKind.StackedFrames: Frames(c, m, t); break;
            case LagKind.DroppedItems: RemoveAll(c, Family.Item, t); break;
            case LagKind.ExperienceOrbs: RemoveAll(c, Family.Orb, t); break;
            case LagKind.LitTnt: RemoveAll(c, Family.Tnt, t); break;
            case LagKind.Projectiles: RemoveAll(c, Family.Projectile, t); break;
            case LagKind.PotionClouds: RemoveAll(c, Family.Cloud, t); break;
            case LagKind.Markers: RemoveAll(c, Family.Marker, t); break;
            case >= LagKind.PistonMass and <= LagKind.RailCarpet:
                TakeBlocks(c, m, name => (CubeParts.TryGetValue(name, out var k) || FloorParts.TryGetValue(name, out k)) && k == m.Kind, t);
                break;
            case LagKind.MovingPistons:
                TakeBlocks(c, null, name => name == "moving_piston", t);
                break;
            case LagKind.SummoningCommandBlock:
                foreach (var be in c.BlockEntities)
                    if (SummonsEveryTick(c, be, be.GetInt("x"), be.GetInt("y"), be.GetInt("z")) is string command)
                    {
                        be.SetString("Command", "");
                        be.SetByte("auto", 0);
                        be.SetByte("powered", 0);
                        t.Commands++;
                        t.CommandsEmptied.Add(Short(command));
                    }
                break;
            case LagKind.BookBan:
                foreach (var be in c.BlockEntities)
                    if (InBox(m, be.GetInt("x"), be.GetInt("y"), be.GetInt("z")) && be["Items"] is NbtList && Bytes(be) >= Limits.ContainerBytes)
                        Lighten(be, Limits.ContainerBytes / 4, t);
                break;
            case LagKind.ChunkBan:
                ChunkBan(c, t);
                break;
            // Waiting updates (block_ticks, fluid ticks, Bedrock's pending ticks): the rewrite saves none.
        }
    }

    static bool InBox(LagMachine m, int x, int y, int z, int pad = 1) =>
        x >= m.Box.MinX - pad && x <= m.Box.MaxX + pad && y >= m.Box.MinY - pad && y <= m.Box.MaxY + pad && z >= m.Box.MinZ - pad && z <= m.Box.MaxZ + pad;

    static (int X, int Y, int Z)? BlockOf(NbtCompound e) =>
        e.GetList("Pos") is { Count: 3 } p ? ((int)Math.Floor(p[0].AsDouble), (int)Math.Floor(p[1].AsDouble), (int)Math.Floor(p[2].AsDouble)) : null;

    /// <summary>Named, tamed or otherwise owned mobs stay first when a pile is thinned.</summary>
    static bool Cherished(NbtCompound e) =>
        e["CustomName"] != null || e["Owner"] != null || e["OwnerUUID"] != null || e.GetBool("Tame");

    /// <summary>
    /// A pile thinned: in each 1×2×1 cell of the machine's box holding a pile of this family (as the finder counts them),
    /// only as many stay as fit (24 mobs, else one); a chunk-wide flood of the family is brought down below half its limit.
    /// </summary>
    static void Thin(CanonicalChunk c, LagMachine m, Family family, Taken t)
    {
        int cellLimit = family switch
        {
            Family.Mob => Limits.MobsInOneBlock, Family.Minecart => Limits.MinecartsInOneBlock, Family.Boat => Limits.BoatsInOneBlock,
            Family.FallingBlock => Limits.FallingBlocksInOneBlock, _ => Limits.ArmourStandsInOneBlock,
        };
        int keep = family == Family.Mob ? MobsKeptInOneBlock : 1;
        var drop = new HashSet<NbtCompound>(ReferenceEqualityComparer.Instance);
        foreach (var cell in c.Entities.Where(e => FamilyOf(EntityId(e)) == family && BlockOf(e) is { } b && InBox(m, b.X, b.Y, b.Z))
                     .GroupBy(e => { var b = BlockOf(e)!.Value; return (b.X, b.Y >> 1, b.Z); }))
        {
            var here = cell.ToList();
            if (here.Count < cellLimit) continue;
            foreach (var e in here.OrderByDescending(Cherished).Skip(keep)) drop.Add(e);
        }
        // A flood across the whole chunk (the finder's per-chunk rule for mobs, minecarts and falling blocks).
        int chunkLimit = family switch { Family.Mob => Limits.MobsInOneChunk, Family.Minecart => Limits.MinecartsInOneChunk, Family.FallingBlock => Limits.FallingBlocksInOneChunk, _ => int.MaxValue };
        var rest = c.Entities.Where(e => FamilyOf(EntityId(e)) == family && !drop.Contains(e)).ToList();
        if (rest.Count >= chunkLimit)
            foreach (var e in rest.OrderBy(Cherished).Take(rest.Count - chunkLimit / 2)) drop.Add(e);
        Remove(c, drop, t);
    }

    /// <summary>Item frames stacked on one face: the first stays.</summary>
    static void Frames(CanonicalChunk c, LagMachine m, Taken t)
    {
        var drop = new HashSet<NbtCompound>(ReferenceEqualityComparer.Instance);
        foreach (var face in c.Entities.Where(e => FamilyOf(EntityId(e)) == Family.Frame && InBox(m, e.GetInt("TileX"), e.GetInt("TileY"), e.GetInt("TileZ")))
                     .GroupBy(e => (e.GetInt("TileX"), e.GetInt("TileY"), e.GetInt("TileZ"), e.GetLongOrNull("Facing") ?? e.GetLongOrNull("facing") ?? -1)))
            foreach (var e in face.Skip(1)) drop.Add(e);
        Remove(c, drop, t);
    }

    static void RemoveAll(CanonicalChunk c, Family family, Taken t) =>
        Remove(c, new HashSet<NbtCompound>(c.Entities.Where(e => FamilyOf(EntityId(e)) == family), ReferenceEqualityComparer.Instance), t);

    static void Remove(CanonicalChunk c, HashSet<NbtCompound> drop, Taken t)
    {
        if (drop.Count == 0) return;
        foreach (var e in drop) t.Entity(e);
        c.Entities.RemoveAll(drop.Contains);
    }

    /// <summary>Every block of the machine's parts in its box (or the whole chunk) made air; their block entities (a hopper's items) go with them.</summary>
    static void TakeBlocks(CanonicalChunk c, LagMachine? m, Func<string, bool> part, Taken t)
    {
        var gone = new HashSet<(int, int, int)>();
        foreach (var (sy, section) in c.Sections)
        {
            if (!section.Palette.Any(s => part(s.BaseName))) continue;
            for (int y = sy * 16; y < sy * 16 + 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        int wx = c.X * 16 + x, wz = c.Z * 16 + z;
                        if (m != null && !InBox(m, wx, y, wz, pad: 0)) continue;
                        if (!part(c.GetBlock(x, y, z).BaseName)) continue;
                        gone.Add((wx, y, wz));
                    }
        }
        foreach (var (x, y, z) in gone) c.SetBlock(x & 15, y, z & 15, BlockState.Air);
        t.Blocks += gone.Count;
        c.BlockEntities.RemoveAll(be => gone.Contains((be.GetInt("x"), be.GetInt("y"), be.GetInt("z"))));
        if (gone.Count > 0) c.NeedsShapeUpdate = true;
    }

    /// <summary>A container's biggest item stacks taken out until its item data is under <paramref name="under"/> bytes.</summary>
    static void Lighten(NbtCompound container, long under, Taken t)
    {
        if (container["Items"] is not NbtList items) return;
        foreach (var item in items.OfType<NbtCompound>().OrderByDescending(Bytes).ToList())
        {
            if (Bytes(container) < under) break;
            items.Remove(item);
            t.Items++;
        }
    }

    /// <summary>A chunk-ban chunk brought under half the limit: the biggest item stacks in its containers, then the biggest entities, go first.</summary>
    static void ChunkBan(CanonicalChunk c, Taken t)
    {
        long Total() => c.BlockEntities.Sum(Bytes) + c.Entities.Sum(Bytes);
        long target = Limits.ChunkBytes / 2;
        var stacks = c.BlockEntities.Where(be => be["Items"] is NbtList)
            .SelectMany(be => ((NbtList)be["Items"]!).OfType<NbtCompound>().Select(item => (Container: be, Item: item, Size: Bytes(item))))
            .OrderByDescending(s => s.Size).ToList();
        long total = Total();
        foreach (var s in stacks)
        {
            if (total < target) return;
            ((NbtList)s.Container["Items"]!).Remove(s.Item);
            total -= s.Size;
            t.Items++;
        }
        var drop = new HashSet<NbtCompound>(ReferenceEqualityComparer.Instance);
        foreach (var e in c.Entities.OrderByDescending(Bytes))
        {
            if (total < target) break;
            drop.Add(e);
            total -= Bytes(e);
        }
        Remove(c, drop, t);
    }
}
