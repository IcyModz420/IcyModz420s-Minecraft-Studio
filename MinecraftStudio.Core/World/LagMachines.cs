// MinecraftStudio.Core/World/LagMachines.cs: finds the lag machines in a world — the devices built to make a world or a
// server lag or crash — and only those: ordinary redstone, farms and storage are left alone.
// Owns: LagKind, LagMachine, LagScan and LagMachines.Find (every chunk of each dimension read once, any edition; what each
// finder in LagMachines.*.cs leaves is merged into one place per machine, the worst first). A saved world does not run, so
// only what ordinary play cannot leave counts (limits in LagMachines.Limits.cs, set from real survival worlds): piles in one
// block that the game's own crowding rule forbids, floods per chunk, solid masses of one redstone part, the updates a running
// machine leaves waiting, command blocks summoning every tick, chunks stuffed past what players can load. Only reads.
using MinecraftStudio.Core.Conversion;

namespace MinecraftStudio.Core.World;

/// <summary>What sort of lag machine was found.</summary>
public enum LagKind
{
    // Piles in one block no survival play makes.
    MinecartStack, BoatStack, ArmourStandStack, MobPile, StackedFrames,
    // Floods in one chunk.
    DroppedItems, ExperienceOrbs, FallingBlocks, LitTnt, Projectiles, PotionClouds, Markers,
    // Solid masses of one redstone part.
    PistonMass, ObserverMass, HopperMass, DispenserMass, LampMass, SculkMass, CrafterMass, RedstoneCarpet, RailCarpet,
    // A machine that was running when the world was saved.
    WaitingUpdates, WaitingFluids, MovingPistons,
    // Commands and data.
    SummoningCommandBlock, BookBan, ChunkBan,
}

/// <summary>
/// One lag machine: what it is (in words), in which dimension (its <see cref="WorldDimension.Id"/>), where (the block at
/// its heart, and the box it fills), how much of it there is, and everything found there that points to it.
/// </summary>
public sealed record LagMachine(LagKind Kind, string DimensionId, string What, int X, int Y, int Z,
    (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) Box, long Count, IReadOnlyList<string> Evidence)
{
    public string Title => LagMachines.Title(Kind);
}

public sealed class LagScan
{
    /// <summary>Every machine found, the worst first.</summary>
    public List<LagMachine> Machines { get; } = [];
    public int ChunksRead { get; set; }
    public List<string> Warnings { get; } = [];
}

public static partial class LagMachines
{
    /// <summary>The name of a kind of machine, in plain words.</summary>
    public static string Title(LagKind k) => k switch
    {
        LagKind.MinecartStack => "Minecart stack",
        LagKind.BoatStack => "Boat stack",
        LagKind.ArmourStandStack => "Armour stand stack",
        LagKind.MobPile => "Mob pile",
        LagKind.StackedFrames => "Stacked item frames",
        LagKind.DroppedItems => "Dropped item flood",
        LagKind.ExperienceOrbs => "Experience orb flood",
        LagKind.FallingBlocks => "Falling block machine",
        LagKind.LitTnt => "Lit TNT",
        LagKind.Projectiles => "Projectile flood",
        LagKind.PotionClouds => "Potion cloud flood",
        LagKind.Markers => "Marker flood",
        LagKind.PistonMass => "Piston lag machine",
        LagKind.ObserverMass => "Observer lag machine",
        LagKind.HopperMass => "Hopper lag machine",
        LagKind.DispenserMass => "Dispenser lag machine",
        LagKind.LampMass => "Lamp lag machine",
        LagKind.SculkMass => "Sculk sensor lag machine",
        LagKind.CrafterMass => "Crafter lag machine",
        LagKind.RedstoneCarpet => "Redstone carpet",
        LagKind.RailCarpet => "Powered rail field",
        LagKind.WaitingUpdates => "Running update machine",
        LagKind.WaitingFluids => "Liquid flow machine",
        LagKind.MovingPistons => "Running piston machine",
        LagKind.SummoningCommandBlock => "Command block summoning every tick",
        LagKind.BookBan => "Book ban",
        LagKind.ChunkBan => "Chunk ban",
        _ => k.ToString(),
    };

    /// <summary>Which kind names a place where several are found: what the machine is before what it does.</summary>
    static int Rank(LagKind k) => k switch
    {
        LagKind.ChunkBan or LagKind.BookBan => 0,
        LagKind.SummoningCommandBlock => 1,
        >= LagKind.PistonMass and <= LagKind.RailCarpet => 2,
        LagKind.MinecartStack or LagKind.BoatStack or LagKind.ArmourStandStack or LagKind.MobPile or LagKind.StackedFrames => 3,
        LagKind.MovingPistons or LagKind.WaitingUpdates or LagKind.WaitingFluids => 4,
        _ => 5,
    };

    /// <summary>Something one finder saw at one place. A supporting hit (a fast clock) only backs up a machine found beside it.</summary>
    sealed record Hit(LagKind Kind, int X, int Y, int Z, (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) Box, long Count, string Text, bool Supporting = false);

    /// <summary>
    /// Every lag machine in the dimensions given (each chunk read once). Damaged chunks are skipped and reported in the
    /// warnings; nothing is written. <paramref name="shared"/>: the world is being read by a map too (one opened for rescue
    /// cannot be opened twice), so each chunk is read under its dimension's lock, as the map reads it. <paramref name="only"/>:
    /// just those chunks are looked at (the check after a clean-up).
    /// </summary>
    public static LagScan Find(SaveWorld world, IEnumerable<WorldDimension> dimensions, IProgress<string>? progress = null, CancellationToken cancel = default, bool shared = false,
        IReadOnlySet<ChunkPos>? only = null)
    {
        var scan = new LagScan();
        foreach (var dim in dimensions)
        {
            var reader = new CanonicalReader(world, dim);
            var positions = dim.ChunkPositions.Where(p => (only == null || only.Contains(p)) && reader.Exists(p)).OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5).ThenBy(p => p.X).ThenBy(p => p.Z).ToList();
            var hits = new List<Hit>();
            var parts = new Parts();
            int done = 0;
            foreach (var pos in positions)
            {
                cancel.ThrowIfCancellationRequested();
                if (done++ % 64 == 0) progress?.Report($"Looked at {done - 1:N0} of {positions.Count:N0} chunks of {dim.DisplayName}");
                scan.ChunksRead++;
                CanonicalChunk? c;
                if (shared) Monitor.Enter(dim);
                try
                {
                    try { Ticks(dim, pos, hits); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { scan.Warnings.Add($"{dim.DisplayName} chunk {pos}: its waiting updates could not be read ({ex.Message})."); }
                    c = reader.Get(pos);
                }
                finally { if (shared) Monitor.Exit(dim); }
                if (c == null) continue;
                Entities(c, hits);
                Data(c, hits);
                parts.Add(c, hits);
            }
            progress?.Report($"Looking for solid masses of redstone parts in {dim.DisplayName}");
            parts.FindMasses(hits, cancel);
            scan.Machines.AddRange(Places(hits, dim.Id));
            scan.Warnings.AddRange(reader.Warnings.Take(20));
        }
        scan.Machines.Sort((a, b) => Rank(a.Kind) != Rank(b.Kind) ? Rank(a.Kind).CompareTo(Rank(b.Kind)) : b.Count.CompareTo(a.Count));
        return scan;
    }

    /// <summary>The hits of one dimension gathered into one place per machine: whatever lies within a chunk of each other.</summary>
    static IEnumerable<LagMachine> Places(List<Hit> hits, string dimId)
    {
        const int Near = 16;
        var parent = Enumerable.Range(0, hits.Count).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        bool Close(Hit a, Hit b) =>
            a.Box.MinX - Near <= b.Box.MaxX && b.Box.MinX - Near <= a.Box.MaxX &&
            a.Box.MinY - Near <= b.Box.MaxY && b.Box.MinY - Near <= a.Box.MaxY &&
            a.Box.MinZ - Near <= b.Box.MaxZ && b.Box.MinZ - Near <= a.Box.MaxZ;
        for (int i = 0; i < hits.Count; i++)
            for (int j = i + 1; j < hits.Count; j++)
                if (Close(hits[i], hits[j])) parent[Root(i)] = Root(j);
        foreach (var group in Enumerable.Range(0, hits.Count).GroupBy(Root))
        {
            var members = group.Select(i => hits[i]).OrderBy(h => h.Supporting).ThenBy(h => Rank(h.Kind)).ThenByDescending(h => h.Count).ToList();
            // A fast clock alone is no lag machine.
            if (members[0].Supporting) continue;
            var main = members[0];
            var box = (MinX: members.Min(h => h.Box.MinX), MinY: members.Min(h => h.Box.MinY), MinZ: members.Min(h => h.Box.MinZ),
                MaxX: members.Max(h => h.Box.MaxX), MaxY: members.Max(h => h.Box.MaxY), MaxZ: members.Max(h => h.Box.MaxZ));
            var evidence = members.Skip(1).Select(h => h.Text).Where(t => t != main.Text).Distinct().Take(12).ToList();
            yield return new LagMachine(main.Kind, dimId, main.Text, main.X, main.Y, main.Z, box, main.Count, evidence);
        }
    }

    static (int, int, int, int, int, int) Around(int x, int y, int z) => (x, y, z, x, y, z);
}
