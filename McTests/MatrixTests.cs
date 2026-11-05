// McTests/MatrixTests.cs: matrix: every source edition × a target of every writer and era. Each output is
// reopened and compared block by block in the area both can hold; results are printed as one table.
// Owns: MatrixTests.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;

/// <summary>
/// matrix: every source edition × a target of every writer and era. Each output is reopened and compared block by block
/// in the area both can hold; results are printed as one table.
/// </summary>
static class MatrixTests
{
    static readonly string Downloads = TestPaths.Data;

    static readonly (string Label, string Path)[] Sources =
    [
        ("Java 1.21.1", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves", "Minecraft Hard")),
        ("Java 1.13", Downloads + @"\amulet\java\vanilla\1_13"),
        ("Java 1.12.2", Downloads + @"\amulet\java\vanilla\1_12_2"),
        ("Bedrock 1.18", Downloads + @"\amulet\bedrock\vanilla\1_18\vanilla"),
        ("Bedrock 1.16", Downloads + @"\amulet\bedrock\vanilla\1_16"),
        ("Console TU70", Downloads + @"\lce\mcsaves_x360_fgh_tu70\savegame.dat"),
        ("3DS 1.9", Downloads + @"\3ds\1.9"),
        ("PE 0.x", Downloads + @"\pymclevel\testfiles\PocketWorld"),
        ("Indev", Downloads + @"\pymclevel\testfiles\hell.mclevel"),
        ("Classic", Downloads + @"\pymclevel\testfiles\Dojo_64_64_128.dat"),
    ];

    static readonly string[] Targets =
    [
        "Java 26.1", "Java 1.17 –", "Java 1.12 –", "Java Beta 1.7", "Java Infdev",
        "Bedrock 1.21.120", "Bedrock 1.20.0", "Bedrock 1.16.0", "Bedrock 1.4 –", "Bedrock 1.2.13", "Bedrock 1.1 –",
        "Pocket Edition 0.9", "Pocket Edition 0.2", "Xbox 360", "PlayStation 4", "Classic 0.0", "Indev",
    ];

    sealed record Result(string Source, string Target, int Chunks, int Failed, int Missing, long Compared, double Names, double States, string Top, string? Error, double Seconds, string Verify = "");

    public static void Run(string[] args)
    {
        var sources = args.Length > 0 ? Sources.Where(s => args.Any(a => s.Label.Contains(a, StringComparison.OrdinalIgnoreCase))).ToArray() : Sources;
        var results = new List<Result>();
        string root = Path.Combine(ConvertTests.OutRoot, "matrix");
        foreach (var (label, path) in sources)
        {
            SaveWorld source;
            try { source = WorldLoader.Open(path); }
            catch (Exception ex) { results.Add(new(label, "(open)", 0, 0, 0, 0, 0, 0, "", ex.Message, 0)); continue; }
            using (source)
                foreach (var name in Targets)
                {
                    var target = TargetCatalog.All.First(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                    results.Add(RunOne(label, source, target, Path.Combine(root, Sanitize(label) + "__" + Sanitize(target.Name))));
                    var r = results[^1];
                    Console.WriteLine($"{r.Source,-13} → {Short(r.Target),-24} {(r.Error != null ? "ERROR " + r.Error : $"{r.Chunks,5} ch  {r.Names,7:0.00}% names  {r.States,7:0.00}% states  missing {r.Missing}  {r.Seconds:0.0}s  {r.Top}\n                  {r.Verify}")}");
                }
        }
        Console.WriteLine();
        int errors = results.Count(r => r.Error != null), missing = results.Count(r => r.Missing > 0 || r.Failed > 0);
        Program.Check(errors == 0, $"no conversion threw ({errors} did)");
        Program.Check(missing == 0, $"no chunk was lost or failed ({missing} conversions had some)");
        int unverified = results.Count(r => r.Error == null && !r.Verify.StartsWith("verified"));
        Program.Check(unverified == 0, $"every converted world passed its checks ({unverified} did not)");
    }

    static string Short(string s) => s.Length > 24 ? s[..24] : s;
    static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));

    static Result RunOne(string label, SaveWorld source, ConversionTarget target, string outDir)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            var info = WorldInfoReader.Read(source);
            var options = new ConversionOptions();
            // Big worlds: a 9×9-chunk area around the spawn keeps the matrix quick (finite targets pick their own area).
            int total = source.Dimensions.Sum(d => d.ChunkPositions.Count);
            if (total > 400 && target.Kind is TargetKind.Java or TargetKind.Bedrock)
                options.Area = ((info.SpawnX >> 4) - 4, (info.SpawnZ >> 4) - 4, (info.SpawnX >> 4) + 4, (info.SpawnZ >> 4) + 4);
            var report = WorldConverter.Run(source, target, outDir, options);
            var (compared, names, states, missingChunks, top) = Compare(source, report.OpenPath ?? outDir, options, target, info);
            string verify = (report.Verified ? "verified" : "FAILED: " + string.Join(", ", report.Checks.Where(c => !c.Passed).Select(c => c.Name + " (" + c.Detail + ")")))
                + $"; {report.Losses.Count(l => l.Lost)} lost / {report.Losses.Count} log entries, {report.Issues.Count} issues, {report.RescueChests.Count} rescue chests";
            if (Environment.GetEnvironmentVariable("MSS_LOG") != null)
            {
                foreach (var l in report.Losses) Console.WriteLine($"      {(l.Lost ? "LOST" : "kept")} {l.Kind}: {l}");
                foreach (var i in report.Issues) Console.WriteLine("      issue: " + i);
            }
            return new(label, target.Name, report.Chunks, report.FailedChunks, missingChunks, compared, names, states, top, null, sw.Elapsed.TotalSeconds, verify);
        }
        catch (Exception ex)
        {
            return new(label, target.Name, 0, 0, 0, 0, 0, 0, "", ex.GetType().Name + ": " + ex.Message + " @ " + ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim(), sw.Elapsed.TotalSeconds);
        }
    }

    /// <summary>Block names and states compared over every dimension both worlds have, in the area and height the target holds.</summary>
    static (long Compared, double Names, double States, int Missing, string Top) Compare(SaveWorld source, string outPath, ConversionOptions options, ConversionTarget target, WorldInfo info)
    {
        using var output = WorldLoader.Open(outPath);
        long compared = 0, sameName = 0, sameState = 0;
        int missing = 0;
        var mismatches = new Dictionary<string, long>();
        bool finite = target.Kind is TargetKind.PocketLegacy or TargetKind.Classic or TargetKind.Indev;
        foreach (var dim in source.Dimensions)
        {
            string id = CanonicalReader.DimensionId(dim);
            if (!WorldConverter.SupportsDimension(target, id)) continue;
            var outDim = output.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == id);
            (int X, int Z) offset = (0, 0);
            int half = int.MaxValue, yShift = 0;
            if (target.Kind == TargetKind.LegacyConsole)
            {
                offset = id switch { "minecraft:the_nether" => ((info.SpawnX >> 4) / 8, (info.SpawnZ >> 4) / 8), "minecraft:the_end" => (0, 0), _ => (info.SpawnX >> 4, info.SpawnZ >> 4) };
                half = id == "minecraft:overworld" ? options.ConsoleWorldChunks / 2 : 9;
            }
            else if (finite)
            {
                offset = ((info.SpawnX >> 4) - 8, (info.SpawnZ >> 4) - 8);
                if (target.Kind == TargetKind.Classic) yShift = 31;
            }
            var positions = dim.ChunkPositions
                .Where(p => options.Area is not var (a, b, c, d) || (p.X >= a && p.X <= c && p.Z >= b && p.Z <= d))
                .Where(p => finite ? p.X - offset.X is >= 0 and <= 15 && p.Z - offset.Z is >= 0 and <= 15
                                   : Math.Abs((long)p.X - offset.X) <= half - 1 && Math.Abs((long)p.Z - offset.Z) <= half - 1)
                .OrderBy(p => (p.X * 7919 + p.Z * 104729) & 0xFFFF).Take(120).ToList();
            // A dimension with no chunks in the converted area is rightly absent from the output.
            if (outDim == null) { if (positions.Count > 0) missing++; continue; }
            var srcReader = new CanonicalReader(source, dim);
            var outReader = new CanonicalReader(output, outDim);
            foreach (var pos in positions)
            {
                var s = srcReader.Get(pos);
                if (s == null) continue;
                var o = outReader.Get(new ChunkPos(pos.X - offset.X, pos.Z - offset.Z));
                if (o == null) { if (!s.IsEmpty) { missing++; if (Environment.GetEnvironmentVariable("MSS_MISSING") != null) Console.WriteLine($"   missing {pos} ({s.Sections.Count} sections, {s.Sections.Values.Sum(x => x.Palette.Length)} palette entries)"); } continue; }
                if (s.NeedsShapeUpdate) ShapeFixer.Fix(s, srcReader, walls: dim is not BedrockDimension);
                if (o.NeedsShapeUpdate) ShapeFixer.Fix(o, outReader, walls: outDim is not BedrockDimension);
                if (s.Sections.Count == 0) continue;
                int minY = s.Sections.Keys.Min() * 16, maxY = s.Sections.Keys.Max() * 16 + 15;
                bool cutBelow = s.Sections.Keys.Min() < 0;
                int top = target.Kind switch
                {
                    TargetKind.LegacyConsole => 255,
                    TargetKind.Java when target.JavaFormat is JavaFormat.Alpha or JavaFormat.McRegion => 127,
                    TargetKind.Java when target.JavaFormat != JavaFormat.AnvilContainers => 255,
                    TargetKind.Bedrock when target.BedrockTerrain == BedrockTerrain.LegacyTerrain => 127,
                    TargetKind.Bedrock when target.BedrockChunkVersion < 39 => 255,
                    TargetKind.PocketLegacy or TargetKind.Indev => 127,
                    TargetKind.Classic => yShift + 63,
                    _ => int.MaxValue,
                };
                if (top != int.MaxValue)
                {
                    // The target is lower than the source: the bottom layer may have become bedrock.
                    minY = Math.Max(minY, target.Kind == TargetKind.Classic ? yShift + 1 : cutBelow || minY <= 0 ? 1 : minY);
                    maxY = Math.Min(maxY, top);
                }
                // Nether in 0–127 targets: the roof is the top.
                for (int y = minY; y <= maxY; y++)
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                        {
                            var a = s.GetBlock(x, y, z);
                            var b = o.GetBlock(x, y - yShift, z);
                            if (a.IsAir && b.IsAir) continue;
                            compared++;
                            if (a.Name == b.Name) { sameName++; if (a == b) sameState++; }
                            else mismatches[$"{a.BaseName}→{b.BaseName}"] = mismatches.GetValueOrDefault($"{a.BaseName}→{b.BaseName}") + 1;
                        }
            }
        }
        string topText = string.Join(", ", mismatches.OrderByDescending(k => k.Value).Take(3).Select(k => $"{k.Key} {k.Value:N0}"));
        return (compared, compared == 0 ? 100 : sameName * 100.0 / compared, compared == 0 ? 100 : sameState * 100.0 / compared, missing, topText);
    }
}
