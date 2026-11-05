// MinecraftStudio.Core/World/StructureMap.cs: where a world's structures are, every kind and edition — villages, outposts,
// shipwrecks, buried treasure, temples, monuments, strongholds, mansions, mineshafts, fortresses, bastions, end cities,
// ancient cities, trail ruins, trial chambers, ruined portals, dungeons …
// Owns: StructureSite, StructureScan, StructureMap.Find. What the game saved comes first: Java's structure starts (1.13+, in
// each chunk; data/*.dat before 1.13), Bedrock's hardcoded spawn areas (fortresses, witch huts, monuments, outposts) and
// village records. Everything else shows by what it left: unopened loot containers and brushable blocks (by their loot
// table), spawners (by mob), vaults and trial spawners. A find inside a saved structure is counted as loot not taken yet;
// finds of one kind close together are one structure. Only generated land is saved, so only that shows. Only reads.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>
/// One structure: its kind (<see cref="StructureKinds"/> key) and name, the dimension (its <see cref="WorldDimension.Id"/>),
/// its middle and box, whether the game saved it (else it was told by what it left), how many of its loot containers have
/// never been opened, and what else was found there.
/// </summary>
public sealed record StructureSite(string Key, string Name, string DimensionId, int X, int Y, int Z,
    (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) Box, bool Saved, int Unlooted, IReadOnlyList<string> Notes)
{
    /// <summary>Its colour on the map (ARGB).</summary>
    public uint Color => StructureKinds.Color(Key);
}

public sealed class StructureScan
{
    public List<StructureSite> Sites { get; } = [];
    public int ChunksRead { get; set; }
    public List<string> Warnings { get; } = [];
}

public static partial class StructureMap
{
    /// <summary>A structure the game saved: its kind, name and box. Pieces: one of several boxes of one structure (joined later).</summary>
    // AnyHeight: the box's height isn't where the structure stands (a console save keeps an ocean ruin's or shipwreck's box at
    // its template's height, though the game set it on the sea floor): finds match it by x and z, and say its height.
    sealed record Saved(string Key, string Name, (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) Box, string Note, bool Pieces = false, bool AnyHeight = false);

    /// <summary>Something a structure left: a loot container never opened (Loot), a spawner, a vault.</summary>
    sealed record Trace(string Key, int X, int Y, int Z, string What, bool Loot);

    /// <summary>
    /// Every structure of the dimensions given. <paramref name="shared"/>: the world is being read by a map too (one opened for
    /// rescue cannot be opened twice), so each chunk is read under its dimension's lock, as the map reads it.
    /// </summary>
    public static StructureScan Find(SaveWorld world, IEnumerable<WorldDimension> dimensions, IProgress<string>? progress = null, CancellationToken cancel = default, bool shared = false)
    {
        var scan = new StructureScan();
        var villages = world is BedrockWorld bw ? BedrockVillages(bw) : [];
        foreach (var dim in dimensions)
        {
            var saved = new List<Saved>();
            var finds = new List<Trace>();
            saved.AddRange(OldJavaFeatures(world, dim));
            saved.AddRange(ConsoleFeatures(world, dim));
            if (dim is BedrockDimension bd) saved.AddRange(villages.Where(v => v.Dimension == bd.Dimension).Select(v => v.Site));
            var positions = dim.ChunkPositions.OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5).ThenBy(p => p.X).ThenBy(p => p.Z).ToList();
            CanonicalReader? reader = dim is JavaRegionDimension or AlphaDimension or BedrockDimension ? null : new CanonicalReader(world, dim);
            int done = 0;
            foreach (var pos in positions)
            {
                cancel.ThrowIfCancellationRequested();
                if (done++ % 128 == 0) progress?.Report($"Looked at {done - 1:N0} of {positions.Count:N0} chunks of {dim.DisplayName}");
                scan.ChunksRead++;
                void Read()
                {
                    switch (dim)
                    {
                        case BedrockDimension b: Bedrock(b, pos, saved, finds); break;
                        case JavaRegionDimension or AlphaDimension: Java(dim, pos, saved, finds); break;
                        default:
                            if (reader!.Get(pos) is { } c) foreach (var be in c.BlockEntities) BlockEntity(be, finds);
                            break;
                    }
                }
                try
                {
                    if (shared) lock (dim) Read();
                    else Read();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (scan.Warnings.Count < 20) scan.Warnings.Add($"{dim.DisplayName} chunk {pos}: {ex.Message}");
                }
            }
            scan.Sites.AddRange(Sites(saved, finds, dim.Id));
            // The portals players built, each with where it leads (that needs the other side's portals too).
            if (CanonicalReader.DimensionId(dim) is Portals.Overworld or Portals.Nether)
            {
                try { scan.Sites.AddRange(PortalSites(world, dim, progress, cancel, shared)); }
                catch (Exception ex) when (ex is not OperationCanceledException) { scan.Warnings.Add("The portals could not be read: " + ex.Message); }
            }
        }
        scan.Sites.Sort((a, b) => a.Key != b.Key ? string.CompareOrdinal(StructureKinds.Name(a.Key), StructureKinds.Name(b.Key)) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
        return scan;
    }

    /// <summary>The built portals of the Overworld or the Nether as sites, each noting where it leads.</summary>
    static IEnumerable<StructureSite> PortalSites(SaveWorld world, WorldDimension dim, IProgress<string>? progress, CancellationToken cancel, bool shared)
    {
        string here = CanonicalReader.DimensionId(dim);
        var other = world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == (here == Portals.Overworld ? Portals.Nether : Portals.Overworld));
        List<Portal> Of(WorldDimension d)
        {
            if (!shared) return Portals.Find(world, d, progress, cancel);
            lock (d) return Portals.Find(world, d, progress, cancel);
        }
        var mine = Of(dim);
        if (mine.Count == 0) return [];
        // Where a portal leads follows the game's rules: Java, Bedrock and the consoles (their Nether scaled by level.dat's
        // HellScale); the 3DS, whose Nether scale isn't known, is listed without guessing.
        if (world is not (JavaWorld or BedrockWorld or LegacyConsole.ConsoleWorld))
            return mine.Select(p => new StructureSite("nether_portal", "Nether portal", dim.Id, p.X, p.Y, p.Z, (p.MinX, p.MinY, p.MinZ, p.MaxX, p.MaxY, p.MaxZ), true, 0,
                ["where it leads isn't worked out for this edition (its Nether's scale isn't known)"])).ToList();
        var theirs = other == null ? [] : Of(other);
        int scale = Portals.Scale(world);
        var links = here == Portals.Overworld ? Portals.Links(mine, theirs, Portals.NewRules(world), scale) : Portals.Links(theirs, mine, Portals.NewRules(world), scale);
        return links.Where(l => l.From.DimensionId == here).Select(l => new StructureSite("nether_portal", "Nether portal", dim.Id, l.From.X, l.From.Y, l.From.Z,
            (l.From.MinX, l.From.MinY, l.From.MinZ, l.From.MaxX, l.From.MaxY, l.From.MaxZ), true, 0, [l.Words])).ToList();
    }

    /// <summary>A block entity's loot table, spawner mob, or vault (canonical Java ids or Bedrock's).</summary>
    static void BlockEntity(NbtCompound be, List<Trace> finds)
    {
        int x = be.GetInt("x"), y = be.GetInt("y"), z = be.GetInt("z");
        string id = (be.GetString("id") ?? "").Replace("minecraft:", "", StringComparison.Ordinal);
        if (be.GetString("LootTable") is { Length: > 0 } table && StructureKinds.FromLootTable(table) is string key)
        {
            string what = id switch
            {
                "brushable_block" or "suspicious_sand" or "suspicious_gravel" or "BrushableBlock" => "suspicious sand or gravel not brushed yet",
                "barrel" or "Barrel" => "an unopened barrel",
                "dispenser" or "Dispenser" => "an unopened dispenser",
                _ => "an unopened chest",
            };
            finds.Add(new Trace(key, x, y, z, what, Loot: true));
            return;
        }
        if (id is "mob_spawner" or "MobSpawner")
        {
            string mob = be.GetCompound("SpawnData")?.GetCompound("entity")?.GetString("id") ?? be.GetCompound("SpawnData")?.GetString("id")
                ?? be.GetString("EntityId") ?? be.GetString("EntityIdentifier") ?? "";
            if (StructureKinds.FromSpawner(mob) is string k) finds.Add(new Trace(k, x, y, z, $"a {WorldSearch.Nice(mob)} spawner", Loot: false));
            return;
        }
        if (id is "vault" or "Vault") finds.Add(new Trace("trial_chambers", x, y, z, "a vault", Loot: false));
        else if (id is "trial_spawner" or "TrialSpawner") finds.Add(new Trace("trial_chambers", x, y, z, "a trial spawner", Loot: false));
    }

    /// <summary>Saved structures, then the finds: inside one of its kind they count for it, else close ones of a kind make one.</summary>
    static IEnumerable<StructureSite> Sites(List<Saved> pieces, List<Trace> finds, string dimId)
    {
        var saved = Joined(pieces);
        var unlooted = new int[saved.Count];
        var notes = saved.Select(s => new List<string> { s.Note }).ToArray();
        var loose = new List<Trace>();
        var heights = new int?[saved.Count];
        foreach (var f in finds)
        {
            int i = saved.FindIndex(s => s.Key == f.Key && Inside(s.Box, f.X, s.AnyHeight ? s.Box.MinY : f.Y, f.Z, 8));
            if (i < 0) { loose.Add(f); continue; }
            heights[i] ??= f.Y;
            if (f.Loot) unlooted[i]++;
            else if (notes[i].Count < 8 && !notes[i].Contains(f.What)) notes[i].Add(f.What);
        }
        for (int i = 0; i < saved.Count; i++)
        {
            var s = saved[i];
            var b = s.Box;
            int y = (b.MinY + b.MaxY) / 2;
            if (s.AnyHeight)
            {
                // Its height from what was found in it; else said not to be known.
                if (heights[i] is int found) { b = (b.MinX, found - (b.MaxY - b.MinY) / 2, b.MinZ, b.MaxX, found + (b.MaxY - b.MinY + 1) / 2, b.MaxZ); y = found; }
                else notes[i].Add("its height isn't saved (on the sea floor)");
            }
            yield return new StructureSite(s.Key, s.Name, dimId, (b.MinX + b.MaxX) / 2, y, (b.MinZ + b.MaxZ) / 2, b, true, unlooted[i], notes[i]);
        }
        foreach (var kind in loose.GroupBy(f => f.Key))
        {
            int spread = StructureKinds.Spread(kind.Key);
            var left = kind.ToList();
            while (left.Count > 0)
            {
                // Everything within the kind's spread of what is already in, from the first one on.
                var group = new List<Trace> { left[0] };
                left.RemoveAt(0);
                for (bool grew = true; grew;)
                {
                    grew = false;
                    for (int j = left.Count - 1; j >= 0; j--)
                        if (group.Any(g => Math.Abs(g.X - left[j].X) <= spread && Math.Abs(g.Z - left[j].Z) <= spread && Math.Abs(g.Y - left[j].Y) <= spread))
                        {
                            group.Add(left[j]);
                            left.RemoveAt(j);
                            grew = true;
                        }
                }
                var box = (group.Min(g => g.X), group.Min(g => g.Y), group.Min(g => g.Z), group.Max(g => g.X), group.Max(g => g.Y), group.Max(g => g.Z));
                int loot = group.Count(g => g.Loot);
                var told = group.GroupBy(g => g.What).Select(g => g.Count() == 1 ? g.Key : $"{g.Count()} × {g.Key}").ToList();
                yield return new StructureSite(kind.Key, StructureKinds.Name(kind.Key), dimId, (box.Item1 + box.Item4) / 2, (box.Item2 + box.Item5) / 2,
                    (box.Item3 + box.Item6) / 2, box, false, loot, ["told by " + string.Join(", ", told)]);
            }
        }
    }

    /// <summary>Boxes saved as pieces of one structure (a fortress's spawn areas) joined where they touch or nearly do.</summary>
    static List<Saved> Joined(List<Saved> saved)
    {
        var whole = saved.Where(s => !s.Pieces).ToList();
        foreach (var kind in saved.Where(s => s.Pieces).GroupBy(s => s.Key))
        {
            var boxes = kind.Select(s => s.Box).ToList();
            for (bool merged = true; merged;)
            {
                merged = false;
                for (int i = 0; i < boxes.Count && !merged; i++)
                    for (int j = i + 1; j < boxes.Count && !merged; j++)
                        if (Touch(boxes[i], boxes[j], 16))
                        {
                            var (a, b) = (boxes[i], boxes[j]);
                            boxes[i] = (Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Min(a.MinZ, b.MinZ), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY), Math.Max(a.MaxZ, b.MaxZ));
                            boxes.RemoveAt(j);
                            merged = true;
                        }
            }
            var first = kind.First();
            whole.AddRange(boxes.Select(b => first with { Box = b, Pieces = false }));
        }
        return whole;
    }

    static bool Touch((int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) a, (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) b, int gap) =>
        a.MinX - gap <= b.MaxX && b.MinX - gap <= a.MaxX && a.MinY - gap <= b.MaxY && b.MinY - gap <= a.MaxY && a.MinZ - gap <= b.MaxZ && b.MinZ - gap <= a.MaxZ;

    static bool Inside((int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) b, int x, int y, int z, int margin) =>
        x >= b.MinX - margin && x <= b.MaxX + margin && y >= b.MinY - margin && y <= b.MaxY + margin && z >= b.MinZ - margin && z <= b.MaxZ + margin;

    /// <summary>The box an int[6] (min x, y, z, max x, y, z) holds; null when it is not one.</summary>
    static (int, int, int, int, int, int)? Box(NbtTag? t) =>
        t is NbtIntArray { Value.Length: 6 } a ? (Math.Min(a.Value[0], a.Value[3]), Math.Min(a.Value[1], a.Value[4]), Math.Min(a.Value[2], a.Value[5]),
            Math.Max(a.Value[0], a.Value[3]), Math.Max(a.Value[1], a.Value[4]), Math.Max(a.Value[2], a.Value[5])) : null;
}
