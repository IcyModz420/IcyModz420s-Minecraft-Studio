// McTests/BiomePaintTests.cs: "biomepaint" — a box of a world given another biome, into a new copy.
// Owns: BiomePaintTests — on copies of Java 1.16.5 (4×4×4 biome cells), Java 1.12.2 (a biome per column), Bedrock 1.16
// and 1.18: the copy passes its checks; every biome cell (or column) the box touches is the new biome; every block of
// the changed chunks is the same as before (only biomes change; cave air written as air); chunks outside the box keep their biomes; an unknown
// biome is refused; the original world is byte for byte unchanged.
using System.Security.Cryptography;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class BiomePaintTests
{
    public static void Run(string[] args)
    {
        Program.Check(BiomePaint.Canonical("Desert") == "minecraft:desert" && BiomePaint.Canonical("minecraft:cherry_grove") == "minecraft:cherry_grove" && BiomePaint.Canonical("nowhere") == null,
            "biome names: typed with or without minecraft:, any case; an unknown one is none");
        foreach (var (label, source) in new[]
        {
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
            ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
        })
        {
            if (!Directory.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            var before = Hashes(source);
            string outDir = Path.Combine(TestPaths.Output, "biomepaint", string.Concat(label.Where(char.IsLetterOrDigit)));
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            try { One(label, source, outDir); }
            catch (Exception ex) { Program.Check(false, $"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
            finally { try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch (IOException) { } }
            Program.Check(before.SequenceEqual(Hashes(source)), $"{label}: the original world is byte for byte unchanged");
        }
    }

    static void One(string label, string source, string outDir)
    {
        PlacementResult r;
        int minX, minZ, maxX, maxZ, minY, maxY;
        using (var w = WorldLoader.Open(source))
        {
            var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var stored = dim.ChunkPositions.ToHashSet();
            var c = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).First(p => stored.Contains(new ChunkPos(p.X + 1, p.Z)));
            (minX, minZ, maxX, maxZ) = (c.X * 16 + 3, c.Z * 16 + 3, c.X * 16 + 20, c.Z * 16 + 12);
            (minY, maxY) = StructurePlacement.Height(w, "minecraft:overworld");
            bool refused = false;
            try { BiomePaint.PaintIntoCopy(w, "minecraft:overworld", minX, minY, minZ, maxX, maxY, maxZ, "nowhere", outDir + "-x"); }
            catch (ArgumentException) { refused = true; }
            Program.Check(refused && !Directory.Exists(outDir + "-x"), $"{label}: an unknown biome is refused before anything is written");
            r = BiomePaint.PaintIntoCopy(w, "minecraft:overworld", minX, minY, minZ, maxX, maxY, maxZ, "desert", outDir);
        }
        Program.Check(r.Verified, $"{label}: the copy passes its checks ({r.ChunksChanged} chunks rewritten){(r.Verified ? "" : " — " + string.Join("; ", r.Problems))}");
        foreach (var n in r.Notes) Program.Info($"   {label}: {n}");
        using var a = WorldLoader.Open(source);
        using var b = WorldLoader.Open(r.OpenPath);
        var ra = new CanonicalReader(a, a.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld"));
        var rb = new CanonicalReader(b, b.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld"));
        int painted = 0, notPainted = 0, blocksDiffer = 0;
        for (int cz = minZ >> 4; cz <= maxZ >> 4; cz++)
            for (int cx = minX >> 4; cx <= maxX >> 4; cx++)
            {
                var ca = ra.Get(new ChunkPos(cx, cz));
                var cb = rb.Get(new ChunkPos(cx, cz));
                if (ca == null || cb == null) continue;
                for (int y = 0; y < 256; y += 7)
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                        {
                            // Every kind of air is written as air (the game treats them the same): that is no change.
                            var (pa, pb) = (ca.GetBlock(x, y, z), cb.GetBlock(x, y, z));
                            if (pa != pb && !(pa.IsAir && pb.IsAir) && blocksDiffer++ < 6) Program.Info($"   {label}: {cx * 16 + x},{y},{cz * 16 + z}: {pa} became {pb}");
                        }
                // The box's columns (and cells): the new biome.
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        int wx = cx * 16 + x, wz = cz * 16 + z;
                        if (wx < minX || wx > maxX || wz < minZ || wz > maxZ) continue;
                        string? biome = BiomeAt(cb, x, 64, z);
                        if (biome == "minecraft:desert") painted++; else notPainted++;
                    }
            }
        Program.Check(painted > 0 && notPainted == 0, $"{label}: every column of the box is desert in the copy ({painted:N0} of {painted + notPainted:N0})");
        Program.Check(blocksDiffer == 0, $"{label}: the blocks of the changed chunks are just as they were (only biomes changed)");
        // A biome the version doesn't have (cherry grove came in 1.20) is said to have become the closest one.
        if (label == "Java 1.16.5")
        {
            string again = outDir + "-cherry";
            try
            {
                using var w2 = WorldLoader.Open(source);
                var r2 = BiomePaint.PaintIntoCopy(w2, "minecraft:overworld", minX, minY, minZ, maxX, maxY, maxZ, "cherry_grove", again);
                Program.Check(r2.Verified && r2.Notes.Any(n => n.Contains("isn't in this world's version") && n.Contains("flower forest")),
                    $"{label}: cherry grove (1.20) becomes flower forest, and the notes say so ({string.Join(" ", r2.Notes)})");
            }
            finally { try { Directory.Delete(again, true); } catch (IOException) { } }
        }
        // A chunk away from the box keeps its biome.
        var away = a.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld").ChunkPositions
            .FirstOrDefault(p => p.X < (minX >> 4) - 2 || p.X > (maxX >> 4) + 2);
        if (ra.Get(away) is CanonicalChunk fa && rb.Get(away) is CanonicalChunk fb)
            Program.Check(BiomeAt(fa, 5, 64, 5) == BiomeAt(fb, 5, 64, 5), $"{label}: a chunk away from the box keeps its biome ({BiomeAt(fb, 5, 64, 5)})");
    }

    /// <summary>The biome at a block: its 4×4×4 cell, or its column.</summary>
    static string? BiomeAt(CanonicalChunk c, int x, int y, int z)
    {
        if (c.Biomes.TryGetValue(y >> 4, out var cells)) return cells[((y & 15) >> 2) * 16 + (z >> 2) * 4 + (x >> 2)];
        return c.ColumnBiomes?[z * 16 + x];
    }

    static List<string> Hashes(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => Path.GetRelativePath(folder, f) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToList();
}
