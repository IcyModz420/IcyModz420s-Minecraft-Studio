// McTests/ReplaceTests.cs: "replace" — replace or fill blocks in a box, into a new copy of a world.
// Owns: ReplaceTests — rules: patterns ("*_ore", names, [key=value]), the new block keeping the old one's facing and the
// like, unknown blocks and properties refused, the first matching rule winning; block entities kept with unchanged blocks
// and gone with replaced ones; on Java 1.16.5, Java 1.12.2 and Bedrock 1.16 sample worlds: the counted changes are the
// ones made, every ore in the box became stone in the copy and nothing else changed, a filled box is all glass, and the
// original world is byte for byte unchanged.
using System.Security.Cryptography;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class ReplaceTests
{
    public static void Run(string[] args)
    {
        Rules();
        Converting();
        foreach (var (label, source) in new[]
        {
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        })
        {
            if (!Directory.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            var before = Hashes(source);
            try { One(label, source); }
            catch (Exception ex) { Program.Check(false, $"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
            Program.Check(before.SequenceEqual(Hashes(source)), $"{label}: the original world is byte for byte unchanged");
        }
    }

    /// <summary>
    /// The same rules while converting (ConversionOptions.BlockRules, the Convert window's "Change blocks as it converts"):
    /// the Java 1.16.5 sample to Bedrock with every ore made stone — no ore left, the check still finds every block as it
    /// should be, the notes count the change; a rule naming no block stops the conversion before anything is written.
    /// </summary>
    static void Converting()
    {
        Console.WriteLine("== The same rules while converting");
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) return;
        string out1 = Path.Combine(TestPaths.Output, "replace-convert"), out2 = Path.Combine(TestPaths.Output, "replace-convert-bad");
        foreach (var d in new[] { out1, out2 }) if (Directory.Exists(d)) Directory.Delete(d, true);
        try
        {
            var bedrock = TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock);
            ConversionReport report;
            using (var w = WorldLoader.Open(java))
                report = WorldConverter.Run(w, bedrock, out1, new ConversionOptions { Dimensions = ["minecraft:overworld"], BlockRules = [new("*_ore", "stone")] });
            using var back = WorldLoader.Open(report.OpenPath ?? out1);
            var dim = back.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var ores = WorldSearch.Run(back, dim, new SearchOptions { Query = "*_ore", Items = false, Entities = false, Text = false, Players = false });
            long left = ores.BlockCounts.Values.Sum();
            Program.Check(report.Verified && left == 0 && report.Notes.Any(n => n.StartsWith("Your block rules changed", StringComparison.Ordinal) && n.EndsWith("to stone.", StringComparison.Ordinal)),
                $"converted to Bedrock with *_ore → stone: {left} ores left, checked ({string.Join(" | ", report.Notes.Where(n => n.StartsWith("Your", StringComparison.Ordinal)).Take(2))})");
            bool refused = false;
            try
            {
                using var w = WorldLoader.Open(java);
                WorldConverter.Run(w, bedrock, out2, new ConversionOptions { BlockRules = [new("*_ore", "no_such_block")] });
            }
            catch (ArgumentException) { refused = true; }
            Program.Check(refused && (!Directory.Exists(out2) || !Directory.EnumerateFileSystemEntries(out2).Any(f => f.EndsWith("level.dat", StringComparison.Ordinal))), "a rule naming no block stops the conversion before the world is written");
        }
        catch (Exception ex) { Program.Check(false, $"converting with rules threw {ex.GetType().Name}: {ex.Message}"); }
        finally { foreach (var d in new[] { out1, out2 }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { } }
    }

    static void Rules()
    {
        var map = BlockReplace.Compile([new("*_ore", "stone"), new("oak_stairs", "spruce_stairs"), new("stone", "granite"), new("oak_log[axis=y]", "birch_log")]);
        BlockState B(string s) => BlockState.Parse(s);
        Program.Check(map(B("minecraft:iron_ore"))?.Name == "minecraft:stone" && map(B("minecraft:deepslate_diamond_ore"))?.Name == "minecraft:stone",
            "\"*_ore\" matches every ore");
        Program.Check(map(B("minecraft:stone")) is { Name: "minecraft:granite" } && map(B("minecraft:iron_ore"))?.Name == "minecraft:stone",
            "the first matching rule wins (ore → stone is not then made granite)");
        var stairs = map(B("minecraft:oak_stairs[facing=east,half=top,shape=outer_left,waterlogged=true]"));
        Program.Check(stairs?.ToString() == "minecraft:spruce_stairs[facing=east,half=top,shape=outer_left,waterlogged=true]",
            $"the new block keeps the old one's facing, half, shape and water ({stairs})");
        Program.Check(map(B("minecraft:oak_log[axis=y]"))?.Name == "minecraft:birch_log" && map(B("minecraft:oak_log[axis=x]")) == null,
            "[axis=y] matches only those states");
        Program.Check(map(B("minecraft:dirt")) == null, "a block no rule matches is left as it is");
        Program.Check(Throws(() => BlockReplace.Compile([new("stone", "stonee")])) && Throws(() => BlockReplace.Compile([new("stone", "oak_stairs[facing=up]")]))
            && Throws(() => BlockReplace.Compile([new("", "")])), "an unknown block, a property it doesn't have, or no rule at all is refused");
        var fill = BlockReplace.Compile([new("*", "glass")]);
        Program.Check(fill(BlockState.Air)?.Name == "minecraft:glass" && fill(B("minecraft:bedrock"))?.Name == "minecraft:glass", "\"*\" is every block, air too (fill)");

        var s = new Structure(2, 1, 1);
        s.Set(0, 0, 0, B("minecraft:chest[facing=north,type=single,waterlogged=false]"));
        s.Set(1, 0, 0, B("minecraft:barrel[facing=up,open=false]"));
        s.BlockEntities[s.Index(0, 0, 0)] = new NbtCompound { { "id", new NbtString("minecraft:chest") } };
        s.BlockEntities[s.Index(1, 0, 0)] = new NbtCompound { { "id", new NbtString("minecraft:barrel") } };
        var (edited, summary) = BlockReplace.Apply(s, BlockReplace.Compile([new("chest", "stone")]));
        Program.Check(edited.Get(0, 0, 0).Name == "minecraft:stone" && !edited.BlockEntities.ContainsKey(s.Index(0, 0, 0)) && edited.BlockEntities.ContainsKey(s.Index(1, 0, 0)) && summary.BlockEntitiesRemoved == 1,
            "a replaced chest's block entity goes with it; the barrel beside it keeps its own");
    }

    static void One(string label, string source)
    {
        string outDir = Path.Combine(TestPaths.Output, "replace", string.Concat(label.Where(char.IsLetterOrDigit)));
        string fillDir = outDir + "-fill";
        foreach (var d in new[] { outDir, fillDir }) if (Directory.Exists(d)) Directory.Delete(d, true);
        try
        {
            int minX, minZ, minY, maxY;
            ReplaceSummary counted;
            PlacementResult r;
            ReplaceSummary made;
            using (var w = WorldLoader.Open(source))
            {
                var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                (minY, maxY) = StructurePlacement.Height(w, "minecraft:overworld");
                maxY = Math.Min(maxY, minY + 90);
                // A 2×2-chunk box near the middle that has ore in it (and all four chunks generated).
                var reader = new CanonicalReader(w, dim);
                var stored = dim.ChunkPositions.ToHashSet();
                int lowY = minY, highY = maxY;
                var c = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).Take(400)
                    .Where(p => stored.Contains(new ChunkPos(p.X + 1, p.Z)) && stored.Contains(new ChunkPos(p.X, p.Z + 1)) && stored.Contains(new ChunkPos(p.X + 1, p.Z + 1)))
                    .First(p => reader.Get(p) is CanonicalChunk ch && ch.Sections.Values.Any(sec => sec.Y * 16 >= lowY - 15 && sec.Y * 16 <= highY && sec.Palette.Any(b => b.BaseName.EndsWith("_ore", StringComparison.Ordinal))));
                (minX, minZ) = (c.X * 16, c.Z * 16);
                var rules = new List<ReplaceRule> { new("*_ore", "stone") };
                counted = BlockReplace.Count(w, dim, minX, minY, minZ, minX + 31, maxY, minZ + 31, rules);
                (r, made) = BlockReplace.ReplaceIntoCopy(w, "minecraft:overworld", minX, minY, minZ, minX + 31, maxY, minZ + 31, rules, outDir);
            }
            Program.Check(counted.Total > 0 && counted.Total == made.Total, $"{label}: the counted changes are the ones made ({made.Total:N0} ores: {string.Join(", ", made.Changes.Take(3).Select(x => $"{x.Count} {x.From.Replace("minecraft:", "")}"))})");
            Program.Check(r.Verified, $"{label}: the copy passes its checks{(r.Verified ? "" : " — " + string.Join("; ", r.Problems))}");
            foreach (var n in r.Notes) Program.Info($"   {label}: {n}");
            int ores = 0;
            using (var copy = WorldLoader.Open(r.OpenPath))
            {
                var reader = new CanonicalReader(copy, copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld"));
                for (int cz = minZ >> 4; cz <= (minZ + 31) >> 4; cz++)
                    for (int cx = minX >> 4; cx <= (minX + 31) >> 4; cx++)
                        if (reader.Get(new ChunkPos(cx, cz)) is CanonicalChunk ch)
                            for (int y = minY; y <= maxY; y++)
                                for (int z = 0; z < 16; z++)
                                    for (int x = 0; x < 16; x++)
                                        if (ch.GetBlock(x, y, z).BaseName.EndsWith("_ore", StringComparison.Ordinal)) ores++;
            }
            Program.Check(ores == 0, $"{label}: no ore is left in the box in the copy ({ores})");

            // Fill a small box with glass.
            using (var w = WorldLoader.Open(source))
            {
                var (fr, fs) = BlockReplace.ReplaceIntoCopy(w, "minecraft:overworld", minX + 2, 70, minZ + 2, minX + 5, 72, minZ + 5, [new("*", "glass")], fillDir);
                using var copy = WorldLoader.Open(fr.OpenPath);
                var reader = new CanonicalReader(copy, copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld"));
                int glass = 0;
                for (int y = 70; y <= 72; y++)
                    for (int z = minZ + 2; z <= minZ + 5; z++)
                        for (int x = minX + 2; x <= minX + 5; x++)
                            if (reader.Get(new ChunkPos(x >> 4, z >> 4))?.GetBlock(x & 15, y, z & 15).BaseName == "glass") glass++;
                Program.Check(fr.Verified && glass == 48, $"{label}: a 4×3×4 box filled with glass is all glass in the copy ({glass} of 48)");
            }
        }
        finally
        {
            foreach (var d in new[] { outDir, fillDir }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { }
        }
    }

    static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }

    static List<string> Hashes(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => Path.GetRelativePath(folder, f) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToList();
}
