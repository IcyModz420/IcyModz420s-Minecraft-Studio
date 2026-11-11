// McTests/UnfinishedTests.cs: "unfinished" — chunks a Java world saved before the game finished making them, and chunks
// with no blocks at all, convert to a Bedrock world the game opens.
// Owns: UnfinishedTests — a copy of the 1.16.5 sample with one chunk set back to "features" and one emptied of every block:
// the unfinished one is left for Bedrock to make (said in the notes), the empty one becomes a modern air chunk (Data3D,
// no old-style Data2D record — thousands of those made a real converted world fail to open), and the checks pass.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class UnfinishedTests
{
    public static void Run()
    {
        CrashWorld();
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) { Program.Info("(skipped: the 1.16.5 sample is not in TestData)"); return; }
        string src = Path.Combine(TestPaths.Output, "unfinished-src"), dst = Path.Combine(TestPaths.Output, "unfinished-bedrock");
        foreach (var d in new[] { src, dst }) if (Directory.Exists(d)) Directory.Delete(d, true);
        try
        {
            CopyDir(java, src);
            ChunkPos unfinished, empty;
            using (var w = WorldLoader.Open(src))
            {
                var ow = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                // Two chunks the game finished (the sample has its own ring of unfinished ones round the edge).
                var pos = ow.ChunkPositions.OrderBy(p => p.X).ThenBy(p => p.Z)
                    .Where(p => ow.LoadChunkNbt(p) is NbtCompound r && JavaChunk.StatusOf(JavaChunk.LevelOf(r)) == "full").ToList();
                unfinished = pos[0]; empty = pos[^1];
                var a = ow.LoadChunkNbt(unfinished)!;
                JavaChunk.LevelOf(a).SetString("Status", "features");
                JavaChunk.LevelOf(a)["Entities"] = new NbtList(NbtType.Compound);
                ow.SaveChunkNbt(unfinished, a);
                var b = ow.LoadChunkNbt(empty)!;
                var level = JavaChunk.LevelOf(b);
                level["Sections"] = new NbtList(NbtType.Compound);
                level["TileEntities"] = new NbtList(NbtType.Compound);
                level["Entities"] = new NbtList(NbtType.Compound);
                ow.SaveChunkNbt(empty, b);
            }
            var bedrock = TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock);
            ConversionReport report;
            using (var w = WorldLoader.Open(src))
                report = WorldConverter.Run(w, bedrock, dst, new ConversionOptions { Dimensions = ["minecraft:overworld"] });
            Program.Check(report.Verified, $"converted to {bedrock.Name} and every check passed");
            foreach (var n in report.Notes.Take(6)) Program.Info("  note: " + n);
            Program.Check(report.Notes.Any(n => n.Contains("chunks the game had not finished making", StringComparison.Ordinal)), "the unfinished chunk is named in the notes");
            using var back = BedrockWorld.Open(report.OpenPath ?? dst);
            bool Has(ChunkPos p, ChunkTag t) => back.Db.Get(BedrockKeys.Make(p.X, p.Z, 0, t)) != null;
            Program.Check(!Has(unfinished, ChunkTag.Version) && !Has(unfinished, ChunkTag.FinalizedState), "the unfinished chunk is left for Bedrock to make");
            Program.Check(Has(empty, ChunkTag.Version) && Has(empty, ChunkTag.Data3D) && !Has(empty, ChunkTag.Data2D), "the chunk with no blocks is a modern air chunk, not old-style terrain");
            Program.Info($"  empty chunk {empty}: Version {Has(empty, ChunkTag.Version)} Data3D {Has(empty, ChunkTag.Data3D)} Data2D {Has(empty, ChunkTag.Data2D)}");
            Program.Check(BedrockChunkShape.Check(back).Count == 0, "every chunk stored the way the game stores it");
            int old = back.Db.Keys().Count(k => BedrockKeys.Describe(k).EndsWith("Data2D", StringComparison.Ordinal));
            Program.Info($"({old} old-style chunks with blocks, left for the game to extend below 0)");
        }
        catch (Exception ex) { Program.Check(false, $"threw {ex.GetType().Name}: {ex.Message}"); }
        finally { foreach (var d in new[] { src, dst }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { } }
    }

    /// <summary>The real converted world that did not open (3,307 blank old-style chunks) must fail the converter's check.</summary>
    static void CrashWorld()
    {
        string bad = Path.Combine(TestPaths.Data, "regressions", "bedrock-blank-old-chunks");
        if (!Directory.Exists(bad)) { Program.Info("(skipped the world that did not open: not in TestData)"); return; }
        using var w = BedrockWorld.Open(bad);
        var found = BedrockChunkShape.Check(w);
        Program.Check(found.Any(f => f.Problem.StartsWith("an old-style chunk with no blocks", StringComparison.Ordinal) && f.Count == 3307),
            $"the converted world that did not open fails the chunk check ({string.Join("; ", found.Select(f => $"{f.Count} × {f.Problem}"))})");
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
