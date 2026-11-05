// MinecraftStudio.Core/Rendering/MapRenderer.cs: renders a chunk/region to an image for the map view.
// Owns: MapRenderOptions, MapRenderer.
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Rendering;

public sealed class MapRenderOptions
{
    /// <summary>Start scanning at this Y (cave / nether view). Null = from the top.</summary>
    public int? StartY { get; set; }
    public bool HeightShading { get; set; } = true;
    public bool WaterDepth { get; set; } = true;
    public bool BiomeTints { get; set; } = true;
}

/// <summary>Top-down rendering of chunk columns into 16x16 ARGB tiles.</summary>
public static class MapRenderer
{
    public const int NoHeight = int.MinValue;

    static void Resolve(BlockState s)
    {
        if (s.MapColorResolved) return;
        var (color, tint) = BlockColors.Lookup(s);
        s.MapColor = color;
        s.MapTint = (byte)tint;
        s.IsWaterLike = s.BaseName is "water" or "flowing_water" or "bubble_column" or "seagrass" or "tall_seagrass" or "kelp" or "kelp_plant"
            || s.Properties.Contains("waterlogged=true", StringComparison.Ordinal);
        s.MapColorResolved = true;
    }

    static bool Skip(BlockState s)
    {
        if (s.IsAir) return true;
        Resolve(s);
        return (s.MapColor >> 24) == 0; // fully transparent (barrier, light, structure void)
    }

    /// <summary>Renders <paramref name="col"/> into pixels[z*16+x]; heights receive the top block Y (or NoHeight).</summary>
    public static void RenderChunk(ChunkColumn col, Span<uint> pixels, Span<int> heights, MapRenderOptions opt, ReadOnlySpan<int> northEdge = default)
    {
        pixels.Clear();
        for (int i = 0; i < 256; i++) heights[i] = NoHeight;
        var sections = col.Sections.Values.OrderByDescending(s => s.Y).ToArray();
        if (sections.Length == 0) return;

        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                int i = z * 16 + x;
                var (y, state, floorY, floor) = FindSurface(sections, x, z, opt.StartY);
                if (state == null) continue;
                heights[i] = y;
                uint color = state.IsWaterLike
                    ? 0xFF000000u | (opt.BiomeTints ? BiomeAt(col, sections, x, y, z).Water : 0x3F76E4u)
                    : BaseColor(state, col, sections, x, y, z, opt);
                if (state.IsWaterLike && opt.WaterDepth)
                {
                    uint floorColor = floor != null ? BaseColor(floor, col, sections, x, floorY, z, opt) : 0xFF202030u;
                    int depth = floor != null ? y - floorY : 32;
                    double a = Math.Clamp(0.45 + depth * 0.06, 0.45, 0.92);
                    color = 0xFF000000u | Biomes.Mix(floorColor & 0xFFFFFF, color & 0xFFFFFF, a);
                }
                pixels[i] = color;
            }

        if (!opt.HeightShading) return;
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                int i = z * 16 + x;
                int h = heights[i];
                if (h == NoHeight) continue;
                int north = z > 0 ? heights[i - 16] : northEdge.Length == 16 ? northEdge[x] : NoHeight;
                if (north == NoHeight) continue;
                int diff = h - north;
                if (diff == 0) continue;
                double f = Math.Clamp(1 + diff * 0.06, 0.72, 1.22);
                pixels[i] = Scale(pixels[i], f);
            }
    }

    /// <summary>
    /// The ground's height in each column (index z*16+x), or NoHeight: the top block that is the land itself — water and
    /// the ice on it, plants and thin covers, tree tops and see-through blocks left out (StructureGround.IsGround) — so a
    /// lake gives its floor and a forest its forest floor.
    /// </summary>
    public static void GroundHeights(ChunkColumn col, Span<int> heights, bool bedrockNames) => GroundHeights(col.Sections.Values, heights, bedrockNames);

    /// <summary>The same for any chunk's sections (a canonical chunk: Java names).</summary>
    public static void GroundHeights(IEnumerable<BlockSection> columnSections, Span<int> heights, bool bedrockNames)
    {
        heights.Fill(NoHeight);
        var sections = columnSections.OrderByDescending(s => s.Y).ToArray();
        Func<BlockState, bool> notGround = b => !IsGround(b, bedrockNames);
        for (int i = 0; i < 256; i++)
            foreach (var s in sections)
            {
                int y = s.TopY(i & 15, i >> 4, notGround);
                if (y >= 0) { heights[i] = (s.Y << 4) + y; break; }
            }
    }

    /// <summary>Water, or what only grows in it (seagrass, kelp, bubble columns).</summary>
    public static bool IsWaterLike(BlockState b)
    {
        Resolve(b);
        return b.IsWaterLike;
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Id, bool Bedrock), bool> GroundCache = new();

    static bool IsGround(BlockState b, bool bedrockNames) => !b.IsAir && GroundCache.GetOrAdd((b.Id, bedrockNames), _ =>
    {
        Resolve(b);
        return (b.MapColor >> 24) != 0 && !b.IsWaterLike && b.BaseName is not ("ice" or "frosted_ice") && Structures.StructureGround.IsGround(b, bedrockNames);
    });

    static (int Y, BlockState? State, int FloorY, BlockState? Floor) FindSurface(BlockSection[] sections, int x, int z, int? startY)
    {
        bool needAir = startY.HasValue;
        int top = startY ?? int.MaxValue;
        BlockState? surface = null;
        int surfaceY = 0;
        foreach (var s in sections)
        {
            int baseY = s.Y << 4;
            if (baseY > top) continue;
            if (s.IsEmpty)
            {
                needAir = false;
                continue;
            }
            int yStart = Math.Min(15, top - baseY);
            for (int y = yStart; y >= 0; y--)
            {
                var b = s.Get(x, y, z);
                if (needAir)
                {
                    if (b.IsAir) needAir = false;
                    continue;
                }
                if (Skip(b)) continue;
                if (surface == null)
                {
                    surface = b;
                    surfaceY = baseY + y;
                    if (!b.IsWaterLike) return (surfaceY, surface, 0, null);
                    continue;
                }
                if (!b.IsWaterLike) return (surfaceY, surface, baseY + y, b);
            }
        }
        return (surfaceY, surface, 0, null);
    }

    static uint BaseColor(BlockState s, ChunkColumn col, BlockSection[] sections, int x, int y, int z, MapRenderOptions opt)
    {
        Resolve(s);
        uint c = s.MapColor;
        var tint = (TintKind)s.MapTint;
        if (tint == TintKind.None || !opt.BiomeTints) return c;
        var t = BiomeAt(col, sections, x, y, z);
        uint tc = tint switch { TintKind.Grass => t.Grass, TintKind.Foliage => t.Foliage, TintKind.Water => t.Water, _ => 0xFFFFFF };
        if (tint == TintKind.Water) return 0xFF000000u | tc;
        // The texture colour is greyscale-ish; multiply with the tint.
        double lum = (((c >> 16) & 255) * 0.3 + ((c >> 8) & 255) * 0.59 + (c & 255) * 0.11) / 255.0;
        double k = Math.Clamp(lum / 0.55, 0.55, 1.25);
        return Scale(0xFF000000u | tc, k);
    }

    static Biomes.Tint BiomeAt(ChunkColumn col, BlockSection[] sections, int x, int y, int z)
    {
        foreach (var s in sections)
        {
            if (s.Y != y >> 4 || s.Biomes == null) continue;
            return Biomes.Get(SectionBiome(s.Biomes, x, y & 15, z));
        }
        if (col.SurfaceBiomes?[z * 16 + x] is string name) return Biomes.Get(name);
        if (col.BiomeIds != null) return Biomes.GetId(col.BiomeIds[z * 16 + x], col.BiomeScheme);
        foreach (var s in sections) if (s.Biomes != null) return Biomes.Get(SectionBiome(s.Biomes, x, 0, z));
        return Biomes.Default;
    }

    /// <summary>The biome at a block of the column (stored per section, per column, or by number), or null when the chunk keeps none.</summary>
    public static Biomes.BiomeInfo? BiomeInfoAt(ChunkColumn col, int x, int y, int z)
    {
        foreach (var s in col.Sections.Values)
            if (s.Y == y >> 4 && s.Biomes != null) return Biomes.Find(SectionBiome(s.Biomes, x, y & 15, z));
        if (col.SurfaceBiomes?[z * 16 + x] is string name) return Biomes.Find(name);
        if (col.BiomeIds != null) return Biomes.FindId(col.BiomeIds[z * 16 + x], col.BiomeScheme);
        foreach (var s in col.Sections.Values) if (s.Biomes != null) return Biomes.Find(SectionBiome(s.Biomes, x, 0, z));
        return null;
    }

    /// <summary>Section biome lookup for 4x4x4 cells (64 entries) or per-block storage (4096 entries).</summary>
    public static string SectionBiome(string[] biomes, int x, int y, int z) => biomes.Length >= 4096
        ? biomes[(y << 8) | (z << 4) | x]
        : biomes[((y >> 2) << 4) | ((z >> 2) << 2) | (x >> 2)];

    public static uint Scale(uint argb, double f)
    {
        int r = (int)Math.Clamp(((argb >> 16) & 255) * f, 0, 255);
        int g = (int)Math.Clamp(((argb >> 8) & 255) * f, 0, 255);
        int b = (int)Math.Clamp((argb & 255) * f, 0, 255);
        return (argb & 0xFF000000u) | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    }
}
