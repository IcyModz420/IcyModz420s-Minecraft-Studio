// MinecraftStudio.Core/World/LandShape.cs: the land in a box flattened, smoothed, raised or lowered — into a new copy.
// Owns: LandShapeKind, LandShapeOptions, LandPlan and LandShape — Plan (each column's ground now, by the heights export's
// rules: plants, trees and water left out; and the height it gets: flat at a y, smoothed over a radius, raised or lowered,
// soft edges blending into the land around), Shaper (the canonical edit: each column's top moves as one piece — the ground
// and the three blocks under it with whatever stands on them, chests and mobs too — the gap filled with the block below;
// water stays at its level) and ShapeIntoCopy (StructurePlacement.EditIntoCopy, then every column's ground read back).
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

public enum LandShapeKind { Flatten, Smooth, Raise, Lower }

/// <param name="Amount">Flatten: the y of the new ground; Smooth: the radius in blocks (1–16); Raise/Lower: blocks.</param>
/// <param name="SoftEdge">Columns this close to the box's edge change only part of the way, so it blends in (0: a sharp edge).</param>
public sealed record LandShapeOptions(LandShapeKind Kind, int Amount, int SoftEdge = 4);

/// <summary>
/// The box's columns (row by row from its north-west corner): the ground now and the ground they get (NoHeight: none), and
/// the lowest and highest y the world keeps.
/// </summary>
public sealed record LandPlan(int MinX, int MinZ, int MaxX, int MaxZ, int[] Now, int[] Then, int Bottom, int Top)
{
    public int Width => MaxX - MinX + 1;
    public int Columns => Now.Count(h => h != MapRenderer.NoHeight);
    public int Changed => Enumerable.Range(0, Now.Length).Count(i => Now[i] != Then[i]);
    public int MostUp => Enumerable.Range(0, Now.Length).Select(i => Then[i] - Now[i]).DefaultIfEmpty(0).Max();
    public int MostDown => Enumerable.Range(0, Now.Length).Select(i => Now[i] - Then[i]).DefaultIfEmpty(0).Max();
}

public static class LandShape
{
    /// <summary>The blocks that move with the ground: it and the three under it (grass over dirt, sand over sand).</summary>
    const int Surface = 4;
    /// <summary>The most columns one change may cover.</summary>
    public const int MaxColumns = 1024 * 1024;

    /// <summary>Each column's ground now and the ground it gets; nothing is written.</summary>
    public static LandPlan Plan(SaveWorld world, string dimensionId, int minX, int minZ, int maxX, int maxZ, LandShapeOptions o, CancellationToken cancel = default)
    {
        if (maxX < minX || maxZ < minZ) throw new ArgumentException("The box is empty.");
        if ((long)(maxX - minX + 1) * (maxZ - minZ + 1) > MaxColumns) throw new ArgumentException($"That box is too large (more than {MaxColumns:N0} columns). Choose a smaller area.");
        if (o.Kind == LandShapeKind.Smooth && o.Amount is < 1 or > 16) throw new ArgumentException("Smooth over a radius of 1 to 16 blocks.");
        if (o.Kind is LandShapeKind.Raise or LandShapeKind.Lower && o.Amount is < 1 or > 256) throw new ArgumentException("Raise or lower by 1 to 256 blocks.");
        if (o.SoftEdge is < 0 or > 64) throw new ArgumentException("Soft edges of 0 to 64 blocks.");
        var dim = world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId) ?? throw new ArgumentException($"The world has no {dimensionId}.");
        var (bottom, top) = StructurePlacement.Height(world, dimensionId);
        if (o.Kind == LandShapeKind.Flatten && (o.Amount <= bottom || o.Amount > top)) throw new ArgumentException($"The world keeps ground from y {bottom + 1} to y {top}.");

        // The ground now, with a margin around the box as wide as the smoothing reaches.
        int margin = o.Kind == LandShapeKind.Smooth ? o.Amount : 0;
        int x0 = minX - margin, z0 = minZ - margin, w = maxX - minX + 1 + 2 * margin, l = maxZ - minZ + 1 + 2 * margin;
        var ground = new int[w * l];
        Array.Fill(ground, MapRenderer.NoHeight);
        var reader = new CanonicalReader(world, dim);
        var tile = new int[256];
        for (int cz = z0 >> 4; cz <= (z0 + l - 1) >> 4; cz++)
            for (int cx = x0 >> 4; cx <= (x0 + w - 1) >> 4; cx++)
            {
                cancel.ThrowIfCancellationRequested();
                if (reader.Get(new ChunkPos(cx, cz)) is not CanonicalChunk c) continue;
                MapRenderer.GroundHeights(c.Sections.Values, tile, bedrockNames: false);
                for (int i = 0; i < 256; i++)
                {
                    int x = cx * 16 + (i & 15) - x0, z = cz * 16 + (i >> 4) - z0;
                    if (x >= 0 && x < w && z >= 0 && z < l) ground[z * w + x] = tile[i];
                }
            }
        double[]? smooth = o.Kind == LandShapeKind.Smooth ? Blur(ground, w, l, o.Amount) : null;

        int bw = maxX - minX + 1, bl = maxZ - minZ + 1;
        var now = new int[bw * bl];
        var then = new int[bw * bl];
        for (int z = 0; z < bl; z++)
            for (int x = 0; x < bw; x++)
            {
                int g = ground[(z + margin) * w + x + margin], i = z * bw + x;
                now[i] = then[i] = g;
                if (g == MapRenderer.NoHeight) continue;
                double want = o.Kind switch
                {
                    LandShapeKind.Flatten => o.Amount,
                    LandShapeKind.Raise => g + o.Amount,
                    LandShapeKind.Lower => g - o.Amount,
                    _ => smooth![(z + margin) * w + x + margin],
                };
                // Soft edges: the outer columns go part of the way, more the further in they are.
                int edge = Math.Min(Math.Min(x, bw - 1 - x), Math.Min(z, bl - 1 - z)) + 1;
                if (o.SoftEdge > 0 && edge <= o.SoftEdge) want = g + (want - g) * edge / (o.SoftEdge + 1);
                then[i] = Math.Clamp(g + (int)Math.Round(want - g, MidpointRounding.AwayFromZero), bottom + 1, top);
            }
        return new LandPlan(minX, minZ, maxX, maxZ, now, then, bottom, top);
    }

    // A weighted average of the ground within the radius (Gaussian, columns without ground left out), in two passes.
    static double[] Blur(int[] ground, int w, int l, int radius)
    {
        double sigma = Math.Max(1, radius / 2.0);
        var k = Enumerable.Range(-radius, 2 * radius + 1).Select(d => Math.Exp(-d * d / (2 * sigma * sigma))).ToArray();
        var sum = new double[w * l];
        var weight = new double[w * l];
        for (int z = 0; z < l; z++)
            for (int x = 0; x < w; x++)
            {
                double s = 0, n = 0;
                for (int d = -radius; d <= radius; d++)
                {
                    int xx = x + d;
                    if (xx < 0 || xx >= w || ground[z * w + xx] == MapRenderer.NoHeight) continue;
                    s += k[d + radius] * ground[z * w + xx];
                    n += k[d + radius];
                }
                sum[z * w + x] = s;
                weight[z * w + x] = n;
            }
        var result = new double[w * l];
        for (int z = 0; z < l; z++)
            for (int x = 0; x < w; x++)
            {
                double s = 0, n = 0;
                for (int d = -radius; d <= radius; d++)
                {
                    int zz = z + d;
                    if (zz < 0 || zz >= l) continue;
                    s += k[d + radius] * sum[zz * w + x];
                    n += k[d + radius] * weight[zz * w + x];
                }
                result[z * w + x] = n > 0 ? s / n : ground[z * w + x];
            }
        return result;
    }

    /// <summary>The canonical edit that gives each of the plan's columns in a chunk its new ground.</summary>
    public static Action<CanonicalChunk> Shaper(LandPlan plan) => c =>
    {
        for (int lz = 0; lz < 16; lz++)
            for (int lx = 0; lx < 16; lx++)
            {
                int x = c.X * 16 + lx, z = c.Z * 16 + lz;
                if (x < plan.MinX || x > plan.MaxX || z < plan.MinZ || z > plan.MaxZ) continue;
                int i = (z - plan.MinZ) * plan.Width + (x - plan.MinX);
                int g = plan.Now[i], t = plan.Then[i];
                if (g == MapRenderer.NoHeight || g == t) continue;
                Move(c, lx, lz, g, t, plan.Bottom, plan.Top);
            }
    };

    // One column's top moved from ground g to t: everything above the cut level (the surface layers below the lower of
    // the two) shifts as one; raising repeats the block at the cut level into the gap, lowering drops the blocks just
    // under the surface. Water that stood over the ground keeps its level.
    static void Move(CanonicalChunk c, int lx, int lz, int g, int t, int bottomY, int topY)
    {
        int d = t - g;
        int cut = Math.Max(bottomY, Math.Min(g, t) - Surface);
        int colTop = g;
        for (int y = topY; y > g; y--)
            if (!c.GetBlock(lx, y, lz).IsAir) { colTop = y; break; }
        int waterTop = int.MinValue;
        for (int y = g + 1; y <= colTop && MapRenderer.IsWaterLike(c.GetBlock(lx, y, lz)); y++) waterTop = y;
        var old = new BlockState[colTop - cut + 1];
        for (int y = cut; y <= colTop; y++) old[y - cut] = c.GetBlock(lx, y, lz);
        BlockState Old(int y) => y <= cut ? old[0] : y > colTop ? BlockState.Air : old[y - cut];
        int end = Math.Min(topY, Math.Max(colTop, colTop + d));
        BlockState water = BlockState.Get("minecraft:water", [new KeyValuePair<string, string>("level", "0")]);
        for (int y = cut + 1; y <= end; y++)
        {
            var b = Old(y - d);
            if (waterTop != int.MinValue)
            {
                if (y > waterTop && MapRenderer.IsWaterLike(b)) b = BlockState.Air;          // raised out of the water
                else if (y > t && y <= waterTop && b.IsAir) b = water;                   // lowered under it
            }
            c.SetBlock(lx, y, lz, b);
        }
        int x = c.X * 16 + lx, z = c.Z * 16 + lz;
        // Chests, signs and the rest go with their blocks; those in the blocks lowering drops go with them.
        foreach (var be in c.BlockEntities.ToList())
        {
            if (be.GetInt("x") != x || be.GetInt("z") != z) continue;
            int y = be.GetInt("y");
            if (y <= cut) continue;
            if (d < 0 && y <= cut - d) { c.BlockEntities.Remove(be); continue; }
            if (y + d > topY) { c.BlockEntities.Remove(be); continue; }
            be.SetInt("y", y + d);
        }
        // Mobs and items standing on the column move with it.
        foreach (var e in c.Entities)
        {
            if (Entities.Position(e) is not var (ex, ey, ez) || (int)Math.Floor(ex) != x || (int)Math.Floor(ez) != z || ey <= cut + 1) continue;
            e["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(ex), new NbtDouble(Math.Max(cut + 1, ey + d)), new NbtDouble(ez) };
        }
    }

    /// <summary>
    /// The box's land shaped in a NEW copy of the world (the world itself is only read): copied, only the box's chunks
    /// rewritten in the world's own format, checked, and every column's ground read back at its planned height.
    /// </summary>
    public static PlacementResult ShapeIntoCopy(SaveWorld world, string dimensionId, int minX, int minZ, int maxX, int maxZ, LandShapeOptions o,
        string output, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        progress?.Report(new ConversionProgress { Stage = "Reading the land", Done = 0, Total = 1 });
        var plan = Plan(world, dimensionId, minX, minZ, maxX, maxZ, o, cancel);
        if (plan.Changed == 0) throw new InvalidOperationException(plan.Columns == 0 ? "There is no ground in that box to shape." : "The land in that box is already that shape: nothing would change.");
        var chunks = new List<ChunkPos>();
        for (int cz = minZ >> 4; cz <= maxZ >> 4; cz++)
            for (int cx = minX >> 4; cx <= maxX >> 4; cx++) chunks.Add(new ChunkPos(cx, cz));
        var result = StructurePlacement.EditIntoCopy(world, dimensionId, chunks, Shaper(plan), output, progress, cancel);
        // Each column's ground, read back in the copy.
        progress?.Report(new ConversionProgress { Stage = "Checking the land in the copy", Done = 0, Total = 1 });
        using var copy = WorldLoader.Open(result.OpenPath);
        var dim = copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimensionId);
        var reader = new CanonicalReader(copy, dim);
        var tile = new int[256];
        int wrong = 0, moved = 0;
        string? first = null;
        foreach (var pos in chunks)
        {
            if (reader.Get(pos) is not CanonicalChunk c) continue;
            MapRenderer.GroundHeights(c.Sections.Values, tile, bedrockNames: false);
            for (int i2 = 0; i2 < 256; i2++)
            {
                int x = pos.X * 16 + (i2 & 15), z = pos.Z * 16 + (i2 >> 4);
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;
                int i = (z - minZ) * plan.Width + (x - minX);
                if (plan.Now[i] == MapRenderer.NoHeight) continue;
                if (plan.Now[i] != plan.Then[i]) moved++;
                if (tile[i2] != plan.Then[i]) { wrong++; first ??= $"x {x}, z {z}: ground at y {tile[i2]} instead of {plan.Then[i]}"; }
            }
        }
        if (wrong > 0) result.Problems.Add($"{wrong:N0} column(s) did not get their new height (first {first})");
        result.Notes.Insert(0, $"{moved:N0} columns changed: up to {plan.MostUp} blocks up and {plan.MostDown} down.");
        return result;
    }
}
