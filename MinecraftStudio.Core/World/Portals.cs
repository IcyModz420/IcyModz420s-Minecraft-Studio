// MinecraftStudio.Core/World/Portals.cs: the Nether portals players built, and where each one leads.
// Owns: Portal, PortalLink, Portals. Found from Java 1.14+'s points of interest (poi/: every portal block, exactly and fast),
// else from the blocks of every chunk (any edition); touching portal blocks are one portal. A portal leads where the game
// sends a player who steps in: the spot scaled by 8 (Overworld → Nether ÷ 8, back × 8), then the nearest portal there
// within 16 blocks in the Nether or 128 in the Overworld (Java 1.16+ and Bedrock; 128 both ways before 1.16) — else the
// game makes a new portal near that spot. A console save's Nether is smaller: its level.dat's HellScale (3 in the classic
// 864-block world), searched 128 both ways as the Java 1.x code they come from. A portal whose partner leads somewhere
// else is a one-way link. Only reads.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;

namespace MinecraftStudio.Core.World;

/// <summary>One built portal: its dimension (canonical id), the box of its portal blocks and how many there are.</summary>
public sealed record Portal(string DimensionId, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ, int Blocks)
{
    public int X => (MinX + MaxX) / 2;
    public int Y => MinY;
    public int Z => (MinZ + MaxZ) / 2;
}

/// <summary>Where a portal leads: the portal there (or none: a new one is made), the spot it lands on, in words.</summary>
public sealed record PortalLink(Portal From, Portal? To, int TargetX, int TargetY, int TargetZ, string Words);

public static class Portals
{
    public const string Overworld = "minecraft:overworld", Nether = "minecraft:the_nether";

    /// <summary>The portals of one dimension.</summary>
    public static List<Portal> Find(SaveWorld world, WorldDimension dim, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        string dimId = CanonicalReader.DimensionId(dim);
        var blocks = new HashSet<(int, int, int)>();
        if (dim is JavaRegionDimension j && Directory.Exists(Path.Combine(j.Folder, "poi")) && Directory.EnumerateFiles(Path.Combine(j.Folder, "poi"), "r.*.mca").Any())
            FromPoi(j, blocks, cancel);
        else
        {
            var reader = new CanonicalReader(world, dim);
            int done = 0;
            foreach (var pos in dim.ChunkPositions.Where(reader.Exists).OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5))
            {
                cancel.ThrowIfCancellationRequested();
                if (done++ % 128 == 0) progress?.Report($"Looking for portals: {done:N0} of {dim.ChunkPositions.Count:N0} chunks of {dim.DisplayName}");
                if (reader.Get(pos) is not { } c) continue;
                foreach (var (sy, section) in c.Sections)
                {
                    if (!section.Palette.Any(s => s.BaseName == "nether_portal")) continue;
                    for (int y = sy * 16; y < sy * 16 + 16; y++)
                        for (int z = 0; z < 16; z++)
                            for (int x = 0; x < 16; x++)
                                if (c.GetBlock(x, y, z).BaseName == "nether_portal") blocks.Add((c.X * 16 + x, y, c.Z * 16 + z));
                }
            }
        }
        return Group(blocks, dimId);
    }

    /// <summary>Java 1.14+: every portal block is a point of interest of type minecraft:nether_portal in poi/.</summary>
    static void FromPoi(JavaRegionDimension dim, HashSet<(int, int, int)> blocks, CancellationToken cancel)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dim.Folder, "poi"), "r.*.mca"))
        {
            cancel.ThrowIfCancellationRequested();
            RegionFile region;
            try { region = RegionFile.Open(file); } catch (Exception e) when (e is IOException or IO.SaveFormatException or InvalidDataException) { continue; }
            foreach (int index in region.ChunkIndexes())
            {
                Nbt.NbtCompound? nbt;
                try { nbt = region.GetChunkNbt(index); } catch (Exception e) when (e is IOException or IO.SaveFormatException or InvalidDataException) { continue; }
                if (nbt?.GetCompound("Sections") is not { } sections) continue;
                foreach (var (_, s) in sections)
                    foreach (var r in (s as Nbt.NbtCompound)?.GetList("Records")?.OfType<Nbt.NbtCompound>() ?? [])
                        if (r.GetString("type") == "minecraft:nether_portal" && r.GetInts("pos") is { Length: 3 } p) blocks.Add((p[0], p[1], p[2]));
            }
        }
    }

    /// <summary>Touching portal blocks as one portal each.</summary>
    static List<Portal> Group(HashSet<(int X, int Y, int Z)> blocks, string dimId)
    {
        var portals = new List<Portal>();
        var seen = new HashSet<(int, int, int)>();
        var stack = new Stack<(int X, int Y, int Z)>();
        foreach (var start in blocks)
        {
            if (!seen.Add(start)) continue;
            int x0 = start.X, x1 = start.X, y0 = start.Y, y1 = start.Y, z0 = start.Z, z1 = start.Z, n = 0;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var (x, y, z) = stack.Pop();
                n++;
                (x0, x1, y0, y1, z0, z1) = (Math.Min(x0, x), Math.Max(x1, x), Math.Min(y0, y), Math.Max(y1, y), Math.Min(z0, z), Math.Max(z1, z));
                foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                {
                    var next = (x + dx, y + dy, z + dz);
                    if (blocks.Contains(next) && seen.Add(next)) stack.Push(next);
                }
            }
            portals.Add(new Portal(dimId, x0, y0, z0, x1, y1, z1, n));
        }
        return portals;
    }

    /// <summary>
    /// Where each portal leads (Overworld ↔ Nether). <paramref name="newRules"/>: Java 1.16+ and Bedrock (16 blocks in the
    /// Nether, 128 in the Overworld); false: older Java and the consoles (128 both). <paramref name="scale"/>: Overworld
    /// blocks to one Nether block (8; a console save's own, <see cref="Scale"/>).
    /// </summary>
    public static List<PortalLink> Links(IReadOnlyList<Portal> overworld, IReadOnlyList<Portal> nether, bool newRules, int scale = 8)
    {
        var links = new List<PortalLink>();
        var target = new Dictionary<Portal, Portal?>();
        foreach (var (from, to, toNether) in overworld.Select(p => (p, nether, true)).Concat(nether.Select(p => (p, overworld, false))))
        {
            int tx = toNether ? FloorDiv(from.X, scale) : from.X * scale, tz = toNether ? FloorDiv(from.Z, scale) : from.Z * scale, ty = from.Y;
            int radius = toNether && newRules ? 16 : 128;
            Portal? best = null;
            double bestD = double.MaxValue;
            foreach (var p in to)
            {
                // The nearest portal block within the square around the spot (the box's nearest point).
                int nx = Math.Clamp(tx, p.MinX, p.MaxX), ny = Math.Clamp(ty, p.MinY, p.MaxY), nz = Math.Clamp(tz, p.MinZ, p.MaxZ);
                if (Math.Abs(nx - tx) > radius || Math.Abs(nz - tz) > radius) continue;
                double d = (nx - tx) * (double)(nx - tx) + (ny - ty) * (double)(ny - ty) + (nz - tz) * (double)(nz - tz);
                if (d < bestD) { bestD = d; best = p; }
            }
            target[from] = best;
            links.Add(new PortalLink(from, best, tx, ty, tz, ""));
        }
        // In words, with whether the portal it leads to leads back.
        return links.Select(l =>
        {
            string there = l.From.DimensionId == Overworld ? "the Nether" : "the Overworld";
            if (l.To == null)
                return l with { Words = $"makes a new portal in {there} near {l.TargetX}, {l.TargetY}, {l.TargetZ} (no portal is within {(l.From.DimensionId == Overworld && newRules ? 16 : 128)} blocks of that spot)" };
            var back = target.GetValueOrDefault(l.To);
            string words = $"leads to the portal in {there} at {l.To.X}, {l.To.Y}, {l.To.Z}";
            words += back == l.From ? ", which leads back here" : back == null ? ", which makes a new portal when used (it doesn't lead back here)" : $", which leads to another portal (at {back.X}, {back.Y}, {back.Z}): a one-way link";
            return l with { Words = words };
        }).ToList();
    }

    static int FloorDiv(int v, int by) => (int)Math.Floor(v / (double)by);

    /// <summary>Whether a world links portals the new way (Java 1.16+ — DataVersion 2566 — and Bedrock).</summary>
    public static bool NewRules(SaveWorld world) => world is not (JavaWorld or ConsoleWorld) || world is JavaWorld j && (j.DataVersion ?? 0) >= 2566;

    /// <summary>
    /// How many Overworld blocks make one Nether block: 8, or a console save's HellScale (its level.dat says; 3 — the
    /// classic world's — when an older save doesn't). The Xbox 360 TU70 sample's own pair agrees: its Nether portal is 17
    /// blocks from where the Overworld one leads at 3, and leading back lands 50 blocks from it (at 8, outside the world).
    /// </summary>
    public static int Scale(SaveWorld world) =>
        world is ConsoleWorld c ? (c.LevelData is { Roots.Count: > 0 } level && (level.Root as Nbt.NbtCompound)?.GetCompound("Data") is { } d && d.GetInt("HellScale") is int h and > 0 ? h : 3) : 8;
}
