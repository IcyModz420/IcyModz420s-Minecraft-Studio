// McTests/LeafDebug.cs: LeafDebug.
// Owns: LeafDebug.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;

static class LeafDebug
{
    /// <summary>leafdebug &lt;java world&gt; : recompute leaf distances of real chunks and show the first disagreements.</summary>
    public static void Run(string[] args)
    {
        using var w = WorldLoader.Open(args[0]);
        var dim = w.Dimensions[0];
        var reader = new CanonicalReader(w, dim);
        int shown = 0;
        foreach (var pos in dim.ChunkPositions.Take(200))
        {
            var c = reader.Get(pos);
            if (c == null) continue;
            // Copy with every leaf reset to distance 7, then let the fixer recompute.
            var copy = new CanonicalChunk(c.X, c.Z);
            foreach (var (sy, s) in c.Sections)
                copy.Sections[sy] = new BlockSection(sy, s.Palette.Select(p => p.BaseName.EndsWith("_leaves") && p.GetProperty("distance") != null ? With(p, "distance", "7") : p).ToArray(), s.Indices?.ToArray());
            ShapeFixer.Fix(copy, reader, walls: false);
            foreach (var (sy, s) in c.Sections)
                for (int i = 0; i < 4096 && shown < 6; i++)
                {
                    int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                    var a = c.GetBlock(x, y, z);
                    if (!a.BaseName.EndsWith("_leaves")) continue;
                    var b = copy.GetBlock(x, y, z);
                    if (a.GetProperty("distance") == b.GetProperty("distance")) continue;
                    shown++;
                    Console.WriteLine($"chunk {pos} block {x},{y},{z}: stored {a.GetProperty("distance")} computed {b.GetProperty("distance")}");
                    foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                    {
                        var n = reader.BlockAt(pos.X * 16 + x + dx, y + dy, pos.Z * 16 + z + dz);
                        Console.WriteLine($"     {dx},{dy},{dz}: {n}");
                    }
                }
            if (shown >= 6) break;
        }
    }

    /// <summary>leafdebug2 &lt;java world&gt; &lt;bedrock copy&gt; : leaf distances after a Java -> Bedrock -> Java round trip.</summary>
    public static void RoundTrip(string[] args)
    {
        using var src = WorldLoader.Open(args[0]);
        using var outw = WorldLoader.Open(args[1]);
        var sr = new CanonicalReader(src, src.Dimensions[0]);
        var or = new CanonicalReader(outw, outw.Dimensions[0]);
        int shown = 0;
        foreach (var pos in outw.Dimensions[0].ChunkPositions)
        {
            var s = sr.Get(pos);
            var o = or.Get(pos);
            if (s == null || o == null) continue;
            ShapeFixer.Fix(o, or, walls: false);
            foreach (var (sy, sec) in s.Sections)
                for (int i = 0; i < 4096 && shown < 4; i++)
                {
                    int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                    var a = s.GetBlock(x, y, z);
                    if (!a.BaseName.EndsWith("_leaves")) continue;
                    var b = o.GetBlock(x, y, z);
                    if (a.GetProperty("distance") == b.GetProperty("distance")) continue;
                    shown++;
                    Console.WriteLine($"chunk {pos} block {x},{y},{z}: source {a} output {b}");
                    foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                        Console.WriteLine($"     {dx},{dy},{dz}: src {sr.BlockAt(pos.X * 16 + x + dx, y + dy, pos.Z * 16 + z + dz)}   out {or.BlockAt(pos.X * 16 + x + dx, y + dy, pos.Z * 16 + z + dz)}");
                }
            if (shown >= 4) break;
        }
    }

    static BlockState With(BlockState s, string k, string v) =>
        BlockState.Get(s.Name, s.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Key == k ? v : p.Value)));
}
