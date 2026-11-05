// MinecraftStudio.Core/World/BiomePaint.cs: a box of a world given another biome — into a new copy.
// Owns: BiomePaint — Names (every biome this program knows), Canonical (a typed name as its id), Painter (the canonical
// edit: every 4×4×4 biome cell the box touches, as the game stores biomes since 1.15, and the box's columns for older
// versions' one-biome-per-column), PaintIntoCopy (StructurePlacement.EditIntoCopy: the world copied, only those chunks
// rewritten in its own format, checked; the world itself never changes). A biome the world's version doesn't have
// becomes the closest one it has (the converter's rule, said in the notes).
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

public static class BiomePaint
{
    /// <summary>Every biome's id ("minecraft:cherry_grove"), in name order.</summary>
    public static IReadOnlyList<string> Names { get; } = Biomes.All.Select(b => "minecraft:" + b.Name).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>A biome typed with or without "minecraft:" as its id, or null when there is no such biome.</summary>
    public static string? Canonical(string name)
    {
        name = name.Trim().ToLowerInvariant().Replace(' ', '_');
        return Biomes.Find(name) is { } b ? "minecraft:" + b.Name : null;
    }

    /// <summary>The edit that gives the inclusive box (world coordinates) the biome, in one canonical chunk.</summary>
    public static Action<CanonicalChunk> Painter(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, string biome) => c =>
    {
        int bx = c.X * 16, bz = c.Z * 16;
        int x0 = Math.Max(minX, bx) - bx, x1 = Math.Min(maxX, bx + 15) - bx, z0 = Math.Max(minZ, bz) - bz, z1 = Math.Min(maxZ, bz + 15) - bz;
        if (x0 > x1 || z0 > z1) return;
        // 3D biomes: the 4×4×4 cells the box touches, in the sections that keep biomes.
        foreach (var sy in c.Biomes.Keys.ToList())
        {
            int sectionLow = sy * 16, sectionHigh = sy * 16 + 15;
            if (sectionHigh < minY || sectionLow > maxY) continue;
            var cells = (string[])c.Biomes[sy].Clone(); // the arrays may be shared between sections
            for (int cy = 0; cy < 4; cy++)
            {
                int y0 = sectionLow + cy * 4;
                if (y0 + 3 < minY || y0 > maxY) continue;
                for (int cz = 0; cz < 4; cz++)
                {
                    if (cz * 4 + 3 < z0 || cz * 4 > z1) continue;
                    for (int cx = 0; cx < 4; cx++)
                        if (cx * 4 + 3 >= x0 && cx * 4 <= x1) cells[cy * 16 + cz * 4 + cx] = biome;
                }
            }
            c.Biomes[sy] = cells;
        }
        // One biome per column (before 1.15, and Bedrock's old 2D biomes).
        if (c.ColumnBiomes is string[] columns)
        {
            var copy = (string[])columns.Clone();
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) copy[z * 16 + x] = biome;
            c.ColumnBiomes = copy;
        }
    };

    /// <summary>
    /// Gives the inclusive box the biome in a NEW copy of the world at <paramref name="output"/> (the world is only read):
    /// only the box's chunks are rewritten, and checked.
    /// </summary>
    public static PlacementResult PaintIntoCopy(SaveWorld world, string dimensionId, int minX, int minY, int minZ, int maxX, int maxY, int maxZ, string biome,
        string output, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        if (world is Legacy.FiniteWorld) throw new NotSupportedException("Classic and Indev levels keep no biomes.");
        if (world is Bedrock.PocketLegacyWorld) throw new NotSupportedException("Pocket Edition 0.x worlds' biomes aren't written by this program.");
        string id = Canonical(biome) ?? throw new ArgumentException($"There is no biome called \"{biome}\".");
        if (maxX < minX || maxY < minY || maxZ < minZ) throw new ArgumentException("The box is empty.");
        var chunks = new List<ChunkPos>();
        for (int cz = minZ >> 4; cz <= maxZ >> 4; cz++)
            for (int cx = minX >> 4; cx <= maxX >> 4; cx++) chunks.Add(new ChunkPos(cx, cz));
        if (chunks.Count > 65536) throw new ArgumentException("That box is too large (more than 65,536 chunks). Choose a smaller area.");
        var result = StructurePlacement.EditIntoCopy(world, dimensionId, chunks, Painter(minX, minY, minZ, maxX, maxY, maxZ, id), output, progress, cancel);
        // What the world's version made of it, read in the copy: a biome it doesn't have became the closest one it has.
        try
        {
            using var copy = WorldLoader.Open(result.OpenPath);
            var dim = copy.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId);
            if (dim != null && new CanonicalReader(copy, dim).Get(new ChunkPos(minX >> 4, minZ >> 4)) is CanonicalChunk c)
            {
                int y = Math.Clamp(64, minY, maxY), x = minX & 15, z = minZ & 15;
                string? got = c.Biomes.TryGetValue(y >> 4, out var cells) ? cells[((y & 15) >> 2) * 16 + (z >> 2) * 4 + (x >> 2)] : c.ColumnBiomes?[z * 16 + x];
                if (got != null && got != id)
                    result.Notes.Add($"{Pretty(id)} isn't in this world's version, so the area is {Pretty(got)}, the closest biome it has.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or IO.SaveFormatException) { }
        return result;
    }

    static string Pretty(string id) => WorldSearch.Nice(id);
}
