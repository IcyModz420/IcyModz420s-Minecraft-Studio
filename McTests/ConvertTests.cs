// McTests/ConvertTests.cs: ConvertTests.
// Owns: ConvertTests.
using System.Diagnostics;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

static class ConvertTests
{
    public static readonly string OutRoot = TestPaths.Output;

    static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));

    /// <summary>convert &lt;source&gt; &lt;target name part&gt; [radiusChunks] [dimension] : converts, reopens and compares.</summary>
    public static void Run(string[] args)
    {
        string src = args[0];
        var target = TargetCatalog.All.First(t => t.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase));
        int? radius = args.Length > 2 ? int.Parse(args[2]) : null;
        string? onlyDim = args.Length > 3 ? args[3] : null;
        string outDir = Path.Combine(OutRoot, Sanitize(Path.GetFileName(src.TrimEnd('\\'))) + "__" + Sanitize(target.Name));
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(OutRoot);
        using var source = WorldLoader.Open(src);
        Console.WriteLine($"== {source.Name}: {source.FormatDescription} -> {target.Name}");
        var info = WorldInfoReader.Read(source);
        var options = new ConversionOptions();
        if (radius is int r)
        {
            int cx = info.SpawnX >> 4, cz = info.SpawnZ >> 4;
            options.Area = (cx - r, cz - r, cx + r, cz + r);
        }
        if (onlyDim != null) options.Dimensions = [onlyDim];
        var sw = Stopwatch.StartNew();
        var report = WorldConverter.Run(source, target, outDir, options, new Progress<ConversionProgress>(p => { }));
        Program.Info($"converted {report.Chunks} chunks ({report.FailedChunks} failed) in {sw.Elapsed.TotalSeconds:0.0}s -> {outDir}");
        foreach (var n in report.Notes) Program.Info("note: " + n);
        foreach (var w in report.Warnings.Take(10)) Program.Info("warning: " + w);
        Program.Check(report.FailedChunks == 0, $"{source.Name} -> {target.Name}: no failed chunks");
        Verify(source, report.OpenPath ?? outDir, options, target);
    }

    static void Verify(SaveWorld source, string outDir, ConversionOptions options, ConversionTarget target)
    {
        SaveWorld output;
        try { output = WorldLoader.Open(outDir); }
        catch (Exception ex) { Program.Check(false, $"output reopens: {ex.Message}"); return; }
        using (output)
        {
            Program.Info($"output: {output.FormatDescription}; {string.Join(", ", output.Summary().Take(6).Select(kv => kv.Key + "=" + kv.Value))}");
            foreach (var dim in source.Dimensions)
            {
                string id = CanonicalReader.DimensionId(dim);
                if (options.Dimensions != null && !options.Dimensions.Contains(id)) continue;
                var outDim = output.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == id);
                if (outDim == null) { Program.Info($"{dim.DisplayName}: not in output"); continue; }
                var srcReader = new CanonicalReader(source, dim);
                var outReader = new CanonicalReader(output, outDim);
                // Console worlds are a square centred on the spawn (the Nether on spawn/8, the End on 0,0).
                (int X, int Z) offset = (0, 0);
                int half = int.MaxValue, yShift = 0;
                bool finite = target.Kind is TargetKind.PocketLegacy or TargetKind.Classic or TargetKind.Indev;
                if (target.Kind == TargetKind.LegacyConsole)
                {
                    var info = WorldInfoReader.Read(source);
                    offset = id switch { "minecraft:the_nether" => ((info.SpawnX >> 4) / 8, (info.SpawnZ >> 4) / 8), "minecraft:the_end" => (0, 0), _ => (info.SpawnX >> 4, info.SpawnZ >> 4) };
                    half = id == "minecraft:overworld" ? options.ConsoleWorldChunks / 2 : 9;
                }
                else if (finite)
                {
                    // 256 × 256 levels: source chunks spawn-8 .. spawn+7 become level chunks 0..15.
                    var info = WorldInfoReader.Read(source);
                    offset = ((info.SpawnX >> 4) - 8, (info.SpawnZ >> 4) - 8);
                    if (target.Kind == TargetKind.Classic) yShift = 31;
                }
                var positions = dim.ChunkPositions.Where(p => options.Area is not var (a, b, c, d) || (p.X >= a && p.X <= c && p.Z >= b && p.Z <= d))
                    .Where(p => finite ? p.X - offset.X is >= 0 and <= 15 && p.Z - offset.Z is >= 0 and <= 15
                                       : Math.Abs((long)p.X - offset.X) <= half - 1 && Math.Abs((long)p.Z - offset.Z) <= half - 1).ToList();
                var sample = positions.OrderBy(p => (p.X * 7919 + p.Z * 104729) & 0xFFFF).Take(400).ToList();
                long compared = 0, sameName = 0, sameState = 0;
                var mismatches = new Dictionary<string, long>();
                int missing = 0;
                foreach (var pos in sample)
                {
                    var s = srcReader.Get(pos);
                    var o = outReader.Get(new ChunkPos(pos.X - offset.X, pos.Z - offset.Z));
                    if (s == null) continue;
                    if (o == null) { missing++; continue; }
                    if (s.NeedsShapeUpdate) ShapeFixer.Fix(s, srcReader, walls: dim is not MinecraftStudio.Core.Bedrock.BedrockDimension);
                    // Read the output the way a conversion back would (Bedrock does not store links/leaf distances).
                    if (o.NeedsShapeUpdate) ShapeFixer.Fix(o, outReader, walls: outDim is not MinecraftStudio.Core.Bedrock.BedrockDimension);
                    int minY = s.Sections.Count > 0 ? s.Sections.Keys.Min() * 16 : 0, maxY = s.Sections.Count > 0 ? s.Sections.Keys.Max() * 16 + 15 : -1;
                    // Only the height the target can store (old Java: 0..255 / 0..127; the bottom layer may become bedrock).
                    if (target.Kind == TargetKind.LegacyConsole)
                    {
                        minY = Math.Max(minY, s.Sections.Keys.Min() < 0 ? 1 : 0);
                        maxY = Math.Min(maxY, 255);
                    }
                    else if (finite)
                    {
                        minY = Math.Max(minY, yShift + 1);
                        maxY = Math.Min(maxY, target.Kind == TargetKind.Classic ? yShift + 63 : 127);
                    }
                    else if (target.Kind == TargetKind.Bedrock && target.BedrockChunkVersion < 39)
                    {
                        int top = target.BedrockTerrain == BedrockTerrain.LegacyTerrain ? 127 : 255;
                        minY = Math.Max(minY, s.Sections.Keys.Min() < 0 ? 1 : 0);
                        maxY = Math.Min(maxY, top);
                    }
                    else if (target.Kind == TargetKind.Java && target.JavaFormat != JavaFormat.AnvilContainers)
                    {
                        int top = target.JavaFormat is JavaFormat.Alpha or JavaFormat.McRegion ? 127 : 255;
                        minY = Math.Max(minY, s.Sections.Keys.Min() < 0 ? 1 : 0);
                        maxY = Math.Min(maxY, top);
                    }
                    for (int y = minY; y <= maxY; y++)
                        for (int z = 0; z < 16; z++)
                            for (int x = 0; x < 16; x++)
                            {
                                var a = s.GetBlock(x, y, z);
                                var b = o.GetBlock(x, y - yShift, z);
                                if (a.IsAir && b.IsAir) continue;
                                compared++;
                                if (a.Name == b.Name) { sameName++; if (a == b) sameState++; else Count(mismatches, $"  props {a} -> {b}"); }
                                else Count(mismatches, $"{a.Name} -> {b.Name}");
                            }
                }
                double pctName = compared == 0 ? 100 : sameName * 100.0 / compared, pctState = compared == 0 ? 100 : sameState * 100.0 / compared;
                Program.Info($"{dim.DisplayName}: {sample.Count} chunks sampled, {missing} missing in output, {compared:N0} blocks: names {pctName:0.000}% same, states {pctState:0.000}% same");
                int show = int.TryParse(Environment.GetEnvironmentVariable("MSS_SHOW"), out int sh) ? sh : 25;
                foreach (var kv in mismatches.OrderByDescending(k => k.Value).Take(show)) Program.Info($"   {kv.Value,9:N0}  {kv.Key}");
                Program.Check(missing == 0, $"{dim.DisplayName}: every sampled chunk exists in the output");
                Program.Check(pctName >= 99.0, $"{dim.DisplayName}: >= 99% of block names survive ({pctName:0.00}%)");
                try
                {
                    var opt = new MapRenderOptions();
                    if (id.Contains("nether")) opt.StartY = 100;
                    var c = sample.Count > 0 ? sample[0] : default;
                    var rs = DimensionRenderer.Render(dim, opt, c.X - 24, c.Z - 24, c.X + 24, c.Z + 24);
                    var ro = DimensionRenderer.Render(outDim, opt, c.X - 24, c.Z - 24, c.X + 24, c.Z + 24);
                    string baseName = Path.Combine(OutRoot, Sanitize(Path.GetFileName(outDir)) + "_" + Sanitize(dim.DisplayName));
                    if (rs.Width > 0) File.WriteAllBytes(baseName + "_src.png", PngWriter.Encode(rs.Pixels, rs.Width, rs.Height));
                    if (ro.Width > 0) File.WriteAllBytes(baseName + "_out.png", PngWriter.Encode(ro.Pixels, ro.Width, ro.Height));
                }
                catch (Exception ex) { Program.Info("render failed: " + ex.Message); }
            }
        }
    }

    static void Count(Dictionary<string, long> d, string key) => d[key] = d.GetValueOrDefault(key) + 1;
}
