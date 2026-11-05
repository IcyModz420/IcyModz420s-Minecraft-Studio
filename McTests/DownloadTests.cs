// McTests/DownloadTests.cs: DownloadTests.
// Owns: DownloadTests.
using System.Diagnostics;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

static class DownloadTests
{
    public static readonly string Root = TestPaths.Data;

    /// <summary>downloads [filter] : opens every downloaded sample world, decodes all chunks and renders PNGs.</summary>
    public static void Run(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;
        Directory.CreateDirectory(WorldTests.OutDir);
        var candidates = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
            if (WorldLoader.Detect(dir) != WorldKind.Unknown && !candidates.Any(c => dir.StartsWith(c + "\\", StringComparison.OrdinalIgnoreCase))) candidates.Add(dir);
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            if (candidates.Any(c => file.StartsWith(c + "\\", StringComparison.OrdinalIgnoreCase))) continue;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".mclevel" or ".dat" or ".mine" or "" && WorldLoader.Detect(file) == WorldKind.JavaFinite) candidates.Add(file);
        }
        foreach (var path in candidates)
        {
            if (filter != null && !path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            string label = path[(Root.Length + 1)..];
            var sw = Stopwatch.StartNew();
            SaveWorld w;
            try { w = WorldLoader.Open(path); }
            catch (Exception ex) { Program.Check(false, $"{label}: open failed: {ex.GetType().Name}: {ex.Message}"); continue; }
            using (w)
            {
                Console.WriteLine($"== {label}: {w.FormatDescription}  [{w.GetType().Name}, {sw.ElapsedMilliseconds} ms]");
                foreach (var (k, v) in w.Summary().Take(9)) Program.Info($"{k}: {v}");
                foreach (var dim in w.Dimensions)
                {
                    int ok = 0, bad = 0, nonEmpty = 0;
                    foreach (var pos in dim.ChunkPositions)
                    {
                        try
                        {
                            var col = dim.LoadChunk(pos);
                            if (col == null) continue;
                            foreach (var s in col.Sections.Values) _ = s.Indices;
                            if (col.Sections.Count > 0) nonEmpty++;
                            ok++;
                        }
                        catch (Exception ex) { bad++; if (bad <= 3) Program.Info($"   {pos}: {ex.GetType().Name}: {ex.Message}"); }
                    }
                    Program.Check(bad == 0, $"{label} {dim.DisplayName}: {ok} chunks decoded ({nonEmpty} with blocks)");
                    try
                    {
                        var opt = new MapRenderOptions();
                        if (dim.Id.Contains("nether")) opt.StartY = 100;
                        var c = dim.ChunkPositions.Count > 0 ? dim.ChunkPositions.MinBy(p => Math.Abs(p.X) + Math.Abs(p.Z)) : default;
                        var r = DimensionRenderer.Render(dim, opt, c.X - 40, c.Z - 40, c.X + 40, c.Z + 40);
                        string file = Path.Combine(WorldTests.OutDir, "dl_" + string.Concat(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')) + "_" + dim.DisplayName.Replace(" ", "") + ".png");
                        if (r.Width > 0) File.WriteAllBytes(file, PngWriter.Encode(r.Pixels, r.Width, r.Height));
                    }
                    catch (Exception ex) { Program.Check(false, $"{label} {dim.DisplayName}: render failed {ex.Message}"); }
                }
                if (w is FiniteWorld fw && fw.Classic != null)
                {
                    // Unchanged Classic level re-encodes to the same content.
                    var again = ClassicLevel.Parse(fw.Classic.ToBytes());
                    Program.Check(again.Blocks.AsSpan().SequenceEqual(fw.Classic.Blocks) && again.Width == fw.Classic.Width && again.Format == fw.Classic.Format,
                        $"{label}: Classic level round-trips ({fw.Classic.Format})");
                    if (fw.Classic.Format == ClassicFormat.Serialized) Program.Info("Level: " + fw.Classic.DescribeSerialized().Split('\n').Length + " lines of serialized fields");
                }
            }
        }
        var unknown = Enumerable.Range(0, BlockState.Count).Select(BlockState.FromId)
            .Where(s => s.MapColorResolved && !s.IsAir && BlockColors.IsGuessed(s)).Select(s => s.Name).Distinct().ToList();
        if (unknown.Count > 0) Program.Info("blocks without a colour: " + string.Join(", ", unknown.Take(60)));
    }
}
