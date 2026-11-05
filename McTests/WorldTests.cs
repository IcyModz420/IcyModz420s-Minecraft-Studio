// McTests/WorldTests.cs: WorldTests.
// Owns: WorldTests.
using System.Diagnostics;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

static class WorldTests
{
    public static string OutDir = Path.Combine(AppContext.BaseDirectory, "renders");

    public static SaveWorld OpenAny(string path)
    {
        if (JavaWorld.LooksLikeJavaWorld(path)) return JavaWorld.Open(path);
        throw new InvalidOperationException("Unknown world type: " + path);
    }

    /// <summary>render &lt;world&gt; [maxChunksRadius] : renders every dimension to PNG.</summary>
    public static void Render(string[] args)
    {
        Directory.CreateDirectory(OutDir);
        var world = OpenAny(args[0]);
        int? radius = args.Length > 1 ? int.Parse(args[1]) : null;
        Console.WriteLine($"== {world.Name}: {world.FormatDescription}");
        foreach (var (k, v) in world.Summary()) Program.Info($"{k}: {v}");
        foreach (var dim in world.Dimensions)
        {
            var sw = Stopwatch.StartNew();
            var opt = new MapRenderOptions();
            if (dim.Id.Contains("nether")) opt.StartY = 100;
            var r = radius is int rad
                ? DimensionRenderer.Render(dim, opt, -rad, -rad, rad, rad)
                : DimensionRenderer.Render(dim, opt);
            string file = Path.Combine(OutDir, Sanitize(world.Name) + "_" + Sanitize(dim.DisplayName) + ".png");
            if (r.Width > 0) File.WriteAllBytes(file, PngWriter.Encode(r.Pixels, r.Width, r.Height));
            Program.Info($"{dim.DisplayName}: {dim.ChunkPositions.Count} chunks, rendered {r.Rendered} ({r.Failed} failed) {r.Width}x{r.Height} in {sw.Elapsed.TotalSeconds:0.0}s -> {file}");
            Program.Check(r.Failed == 0, $"{world.Name} {dim.DisplayName}: every chunk decoded");
            int unknown = BlockState.Count;
        }
        var unknownBlocks = Enumerable.Range(0, BlockState.Count).Select(BlockState.FromId)
            .Where(s => s.MapColorResolved && !s.IsAir && BlockColors.IsGuessed(s)).Select(s => s.Name).Distinct().ToList();
        if (unknownBlocks.Count > 0) Program.Info("blocks without a colour: " + string.Join(", ", unknownBlocks.Take(60)));
    }

    /// <summary>Opens every Java world and decodes a sample of chunks from each dimension.</summary>
    public static void AllJava(string[] args)
    {
        string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        int sample = args.Length > 0 ? int.Parse(args[0]) : 200;
        foreach (var dir in Directory.EnumerateDirectories(saves))
        {
            if (!JavaWorld.LooksLikeJavaWorld(dir)) { Program.Info($"(not a world) {Path.GetFileName(dir)}"); continue; }
            JavaWorld w;
            try { w = JavaWorld.Open(dir); }
            catch (Exception ex) { Program.Check(false, $"{dir}: open failed {ex.Message}"); continue; }
            Console.WriteLine($"== {Path.GetFileName(dir)}: {w.FormatDescription}; seed {w.Seed}; {w.Players.Count} players");
            foreach (var dim in w.Dimensions)
            {
                var layouts = new Dictionary<JavaChunkLayout, int>();
                int ok = 0, bad = 0, blocks = 0;
                foreach (var pos in dim.ChunkPositions.Take(sample))
                {
                    try
                    {
                        var nbt = dim.LoadChunkNbt(pos)!;
                        var layout = JavaChunk.Detect(nbt);
                        layouts[layout] = layouts.GetValueOrDefault(layout) + 1;
                        var col = dim.LoadChunk(pos)!;
                        foreach (var s in col.Sections.Values) { var idx = s.Indices; blocks += idx?.Length ?? 4096; }
                        var p = JavaChunk.Position(nbt);
                        if (p != null && (p.Value.X != pos.X || p.Value.Z != pos.Z)) Program.Info($"   chunk at {pos} says {p}");
                        ok++;
                    }
                    catch (Exception ex) { bad++; if (bad < 4) Program.Info($"   {pos}: {ex.Message}"); }
                }
                Program.Check(bad == 0, $"{Path.GetFileName(dir)} {dim.DisplayName}: decoded {ok} chunks");
                Program.Info($"{dim.DisplayName}: {dim.ChunkPositions.Count} chunks; layouts {string.Join(", ", layouts.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }
        }
    }

    static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
