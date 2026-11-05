// MinecraftStudio.Core/Rendering/DimensionRenderer.cs: Renders a whole dimension (or a part of it) into one
// image, one pixel per block; and, when asked, each column's ground height beside it (the heights export).
// Owns: DimensionRenderer.
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Rendering;

/// <summary>Renders a whole dimension (or a part of it) into one image, one pixel per block.</summary>
public static class DimensionRenderer
{
    /// <summary>The picture; <paramref name="Ground"/> (when asked for) each column's ground height, NoHeight where none.</summary>
    public sealed record Result(uint[] Pixels, int Width, int Height, int MinChunkX, int MinChunkZ, int Rendered, int Failed, int[]? Ground = null);

    /// <param name="ground">also each column's ground height (MapRenderer.GroundHeights: water, plants and trees left out).</param>
    public static Result Render(WorldDimension dim, MapRenderOptions opt, int? minX = null, int? minZ = null, int? maxX = null, int? maxZ = null, IProgress<double>? progress = null, CancellationToken cancel = default,
        bool ground = false)
    {
        var chunks = dim.ChunkPositions.Where(p => (minX == null || p.X >= minX) && (maxX == null || p.X <= maxX) && (minZ == null || p.Z >= minZ) && (maxZ == null || p.Z <= maxZ)).ToList();
        if (chunks.Count == 0) return new Result([], 0, 0, 0, 0, 0, 0);
        int cx0 = chunks.Min(p => p.X), cz0 = chunks.Min(p => p.Z);
        int cx1 = chunks.Max(p => p.X), cz1 = chunks.Max(p => p.Z);
        long w = (cx1 - cx0 + 1) * 16L, h = (cz1 - cz0 + 1) * 16L;
        if (w * h > 400_000_000) throw new InvalidOperationException($"The area is too large to render in one image ({w}x{h}).");
        var pixels = new uint[w * h];
        var tile = new uint[256];
        var heights = new int[256];
        int[]? groundAll = null, groundTile = null;
        if (ground)
        {
            groundAll = new int[w * h];
            Array.Fill(groundAll, MapRenderer.NoHeight);
            groundTile = new int[256];
        }
        bool bedrockNames = dim is Bedrock.BedrockDimension;
        var bottomRows = new Dictionary<ChunkPos, int[]>();
        int done = 0, failed = 0;
        // Sort by region then by z so region files are read once and north edges are available.
        foreach (var pos in chunks.OrderBy(p => p.RegionZ).ThenBy(p => p.RegionX).ThenBy(p => p.Z).ThenBy(p => p.X))
        {
            cancel.ThrowIfCancellationRequested();
            ChunkColumn? col;
            try { col = dim.LoadChunk(pos); }
            catch { failed++; continue; }
            if (col == null) continue;
            bottomRows.TryGetValue(new ChunkPos(pos.X, pos.Z - 1), out var north);
            MapRenderer.RenderChunk(col, tile, heights, opt, north);
            bottomRows[pos] = heights.AsSpan(240, 16).ToArray();
            long ox = (pos.X - cx0) * 16L, oz = (pos.Z - cz0) * 16L;
            for (int z = 0; z < 16; z++)
                tile.AsSpan(z * 16, 16).CopyTo(pixels.AsSpan((int)((oz + z) * w + ox), 16));
            if (groundAll != null)
            {
                MapRenderer.GroundHeights(col, groundTile, bedrockNames);
                for (int z = 0; z < 16; z++)
                    groundTile.AsSpan(z * 16, 16).CopyTo(groundAll.AsSpan((int)((oz + z) * w + ox), 16));
            }
            done++;
            if ((done & 63) == 0) progress?.Report(done / (double)chunks.Count);
        }
        return new Result(pixels, (int)w, (int)h, cx0, cz0, done, failed, groundAll);
    }
}
