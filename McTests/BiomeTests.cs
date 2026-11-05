// McTests/BiomeTests.cs: "biomes" — the map's biome overlay reads a biome for every kind of world.
// Owns: BiomeTests — chunks near the spawn of Java (1.12, 1.13, 1.16), Bedrock (1.16, 1.18), Xbox 360, Pocket and 3DS
// sample worlds, the biome at each 4×4 cell's surface (MapRenderer.BiomeInfoAt, what the overlay draws): known biomes
// are found, every one has a map colour or a grass tint, and the most common ones are printed to look at.
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

static class BiomeTests
{
    static readonly (string Label, string Path)[] Worlds =
    [
        ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
        ("Java 1.13", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_13")),
        ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
        ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
        ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
        ("PE 0.x", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")),
        ("3DS 1.9", Path.Combine(TestPaths.Data, "3ds", "1.9")),
    ];

    public static void Run(string[] args)
    {
        foreach (var (label, path) in Worlds)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            try
            {
                using var w = WorldLoader.Open(path);
                var dim = w.Dimensions.FirstOrDefault(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld") ?? w.Dimensions[0];
                // Pocket Edition 0.1–0.8 keeps no biomes: nothing to show there.
                if (w is MinecraftStudio.Core.Bedrock.PocketLegacyWorld) { Program.Info($"{label}: keeps no biomes (Pocket Edition 0.x)"); continue; }
                var counts = new Dictionary<string, int>();
                int cells = 0, unknown = 0, uncoloured = 0;
                // The chunks nearest the spawn: those at the edge of what was explored are often unfinished (no biomes yet).
                var info = MinecraftStudio.Core.Conversion.WorldInfoReader.Read(w);
                foreach (var pos in dim.ChunkPositions.OrderBy(p => Math.Abs((long)p.X - (info.SpawnX >> 4)) + Math.Abs((long)p.Z - (info.SpawnZ >> 4))).Take(64))
                {
                    var col = dim.LoadChunk(pos);
                    if (col == null) continue;
                    var heights = new int[256];
                    var pixels = new uint[256];
                    MapRenderer.RenderChunk(col, pixels, heights, new MapRenderOptions());
                    for (int z = 2; z < 16; z += 4)
                        for (int x = 2; x < 16; x += 4)
                        {
                            cells++;
                            int h = heights[z * 16 + x];
                            var b = MapRenderer.BiomeInfoAt(col, x, h == MapRenderer.NoHeight ? 64 : h, z);
                            if (b == null) { unknown++; continue; }
                            if (b.MapColor == 0 && b.Tint.Grass == 0) uncoloured++;
                            counts[b.Name] = counts.GetValueOrDefault(b.Name) + 1;
                        }
                }
                if (counts.Count == 0 && dim.ChunkPositions.FirstOrDefault() is var first && dim.LoadChunk(first) is { } c0)
                {
                    string raw = dim is MinecraftStudio.Core.Java.JavaRegionDimension jr && jr.LoadChunkNbt(first) is { } root
                        ? string.Join(", ", MinecraftStudio.Core.Java.JavaChunk.LevelOf(root).Select(kv => $"{kv.Key}:{kv.Value.Type}")) : "-";
                    Program.Info($"{label}: debug: ids {c0.BiomeIds?.Length.ToString() ?? "none"} ({string.Join(",", c0.BiomeIds?.Take(6) ?? [])}), scheme {c0.BiomeScheme}, surface {c0.SurfaceBiomes?.Length.ToString() ?? "none"}, sections with biomes {c0.Sections.Values.Count(s => s.Biomes != null)}; level: {raw}");
                }
                Program.Check(cells > 0 && counts.Count > 0 && unknown < cells && uncoloured == 0,
                    $"{label}: biomes in {cells - unknown} of {cells} cells ({counts.Count} kinds; {unknown} unknown, {uncoloured} without a colour)");
                Program.Info($"{label}: {string.Join(", ", counts.OrderByDescending(c => c.Value).Take(6).Select(c => $"{c.Key} {c.Value}"))}");
            }
            catch (Exception e) { Program.Check(false, $"{label}: threw {e.GetType().Name}: {e.Message}"); }
        }
    }
}
