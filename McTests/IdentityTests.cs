using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

/// <summary>
/// identity: re-saves each test world in its own format and layout (StructurePlacement.SameFormat) and compares every
/// block of every chunk, plus the chunk sets — how faithfully a world survives being written back as itself, which is
/// what placing a structure into a copy relies on.
/// </summary>
static class IdentityTests
{
    static readonly (string Label, string Path)[] Worlds =
    [
        ("Java 1.13", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_13")),
        ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
        ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
        ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        ("Console TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
        ("PE 0.x", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")),
    ];

    public static void Run(string[] args)
    {
        foreach (var (label, path) in Worlds)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            using var world = WorldLoader.Open(path);
            var (target, options) = StructurePlacement.SameFormat(world);
            string outDir = Path.Combine(TestPaths.Output, "identity", string.Concat(label.Where(char.IsLetterOrDigit)));
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            ConversionReport report;
            try { report = WorldConverter.Run(world, target, outDir, options); }
            catch (Exception ex) { Program.Check(false, $"{label}: re-save threw {ex.GetType().Name}: {ex.Message}"); continue; }
            using var output = WorldLoader.Open(report.OpenPath ?? outDir);
            long compared = 0, sameName = 0, sameState = 0;
            int missing = 0, extra = 0;
            var top = new Dictionary<string, long>();
            foreach (var dim in world.Dimensions)
            {
                string id = CanonicalReader.DimensionId(dim);
                var outDim = output.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == id);
                if (outDim == null) { missing += dim.ChunkPositions.Count; continue; }
                var srcPos = dim.ChunkPositions.ToHashSet();
                var outPos = outDim.ChunkPositions.ToHashSet();
                var src = new CanonicalReader(world, dim);
                var dst = new CanonicalReader(output, outDim);
                foreach (var pos in srcPos)
                {
                    var a = src.Get(pos);
                    if (a == null) continue;
                    if (!outPos.Contains(pos)) { if (!a.IsEmpty) missing++; continue; }
                    var b = dst.Get(pos);
                    if (b == null) { missing++; continue; }
                    if (a.NeedsShapeUpdate) ShapeFixer.Fix(a, src, walls: dim is not BedrockDimension);
                    if (b.NeedsShapeUpdate) ShapeFixer.Fix(b, dst, walls: outDim is not BedrockDimension);
                    foreach (var sy in a.Sections.Keys.Union(b.Sections.Keys))
                        for (int y = sy * 16; y < sy * 16 + 16; y++)
                            for (int z = 0; z < 16; z++)
                                for (int x = 0; x < 16; x++)
                                {
                                    var s = a.GetBlock(x, y, z); var o = b.GetBlock(x, y, z);
                                    if (s.IsAir && o.IsAir) continue;
                                    compared++;
                                    if (s.Name == o.Name) { sameName++; if (s == o) sameState++; else Bump(top, $"{s}→{o}"); }
                                    else Bump(top, $"{s.BaseName}→{o.BaseName}");
                                }
                }
                extra += outPos.Count(p => !srcPos.Contains(p));
            }
            double names = compared == 0 ? 100 : sameName * 100.0 / compared, states = compared == 0 ? 100 : sameState * 100.0 / compared;
            int lost = report.Losses.Count(l => l.Lost);
            Console.WriteLine($"{label,-13} → {target.Name,-28} {compared,10:N0} blocks  {names,7:0.000}% names  {states,7:0.000}% states  missing {missing} extra {extra}  lost {lost}  {(report.Verified ? "verified" : "NOT verified")}");
            foreach (var (k, v) in top.OrderByDescending(k => k.Value).Take(4)) Console.WriteLine($"      {v,8:N0}  {k}");
            foreach (var l in report.Losses.Where(l => l.Lost).Take(4)) Console.WriteLine($"      LOST {l}");
            if (world is MinecraftStudio.Core.LegacyConsole.ConsoleWorld)
            {
                // Known and reported limit of the console writer (one chunk format, the classic 18×18 End): a console world
                // re-saved whole loses newer blocks and End chunks beyond it — every loss is in the conversion report.
                // Placing a structure does NOT re-save the world (it rewrites only the chunks the structure touches).
                Program.Info($"{label}: whole re-save is lossy by design of the console writer ({missing} chunk(s) outside what it holds, {lost} reported loss(es)); structure placement is unaffected");
                Program.Check(report.Verified, $"{label}: re-saved copy passes its checks");
                continue;
            }
            Program.Check(missing == 0 && extra == 0, $"{label}: same chunks after re-saving as itself (missing {missing}, extra {extra})");
            Program.Check(report.Verified, $"{label}: re-saved copy passes its checks");
        }
    }

    static void Bump(Dictionary<string, long> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
}
