// McTests/KeyDump.cs: KeyDump.
// Owns: KeyDump.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Nbt;

static class KeyDump
{
    /// <summary>chunkkeys &lt;world&gt; x z [dim] : every record of one chunk.</summary>
    public static void Chunk(string[] args)
    {
        using var w = BedrockWorld.Open(args[0]);
        int x = int.Parse(args[1]), z = int.Parse(args[2]), dim = args.Length > 3 ? int.Parse(args[3]) : 0;
        var d = w.Dimensions.Cast<BedrockDimension>().First(dd => dd.Dimension == dim);
        foreach (var key in d.KeysAt(new MinecraftStudio.Core.World.ChunkPos(x, z)))
        {
            var v = w.Db.Get(key)!;
            Console.WriteLine($"{BedrockKeys.Describe(key),-40} {v.Length,7}  {MinecraftStudio.Core.IO.BinaryUtil.Hex(v, 24)}");
        }
        var col = d.LoadChunk(new MinecraftStudio.Core.World.ChunkPos(x, z));
        Console.WriteLine($"sections: {string.Join(", ", col!.Sections.Keys)}");
        foreach (var s in col.Sections.Values.OrderByDescending(s => s.Y).Take(3))
            Console.WriteLine($"  y={s.Y} palette: {string.Join(" | ", s.Palette.Take(6))}");
    }

    /// <summary>playerkeys &lt;world&gt; [filter] : every player's top-level fields (those whose name holds the filter, else all names).</summary>
    public static void PlayerKeys(string[] args)
    {
        using var w = MinecraftStudio.Core.World.WorldLoader.Open(args[0]);
        string? filter = args.Length > 1 ? args[1] : null;
        foreach (var p in w.Players)
        {
            Console.WriteLine($"{p.Name} ({p.Source}):");
            foreach (var (k, v) in p.Data)
                if (filter == null) Console.Write(k + " ");
                else if (k.Contains(filter, StringComparison.OrdinalIgnoreCase)) Console.WriteLine($"  {k} = {v}");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// playerground &lt;world&gt; : each player's saved position and the blocks just under it and 1.62 lower — which of the
    /// two a standing player is on tells whether the edition keeps a player's height at the feet or at the eyes.
    /// </summary>
    public static void PlayerGround(string[] args)
    {
        using var w = MinecraftStudio.Core.World.WorldLoader.Open(args[0]);
        var dim = w.Dimensions.FirstOrDefault(d => d.Id is "minecraft:overworld" or "overworld" or "0") ?? w.Dimensions.First();
        foreach (var p in w.Players)
        {
            if (p.Data.GetList("Pos") is not { Count: 3 } pos) { Console.WriteLine($"{p.Name}: no Pos"); continue; }
            double x = pos[0].AsDouble, y = pos[1].AsDouble, z = pos[2].AsDouble;
            string At(double yy)
            {
                int bx = (int)Math.Floor(x), by = (int)Math.Floor(yy), bz = (int)Math.Floor(z);
                try { return dim.LoadChunk(new MinecraftStudio.Core.World.ChunkPos(bx >> 4, bz >> 4))?.GetBlock(bx & 15, by, bz & 15).Name ?? "(no chunk)"; }
                catch (Exception e) { return "(" + e.Message + ")"; }
            }
            Console.WriteLine($"{p.Name}: Pos {x:0.###}, {y:0.###}, {z:0.###}  OnGround {p.Data["OnGround"]}  Dimension {p.Data["Dimension"]}{p.Data["DimensionId"]}"
                + $"\n  at y {At(y)}, just under {At(y - 0.01)} | at y-1.62 {At(y - 1.62)}, just under that {At(y - 1.63)}  ({pos[1].Type})");
        }
    }

    /// <summary>keys &lt;world&gt; [tagFilter] : prints keys and value sizes.</summary>
    public static void Run(string[] args)
    {
        using var w = BedrockWorld.Open(args[0]);
        string? filter = args.Length > 1 ? args[1] : null;
        var groups = new Dictionary<string, int>();
        int shown = 0;
        foreach (var key in w.Db.Keys().OrderBy(k => k, MinecraftStudio.Core.LevelDb.ByteArrayComparer.Instance))
        {
            string d = BedrockKeys.Describe(key);
            if (filter != null && !d.Contains(filter)) continue;
            if (shown++ < 40)
            {
                var v = w.Db.Get(key)!;
                string preview = "";
                try
                {
                    if (v.Length > 0 && v[0] == 10) preview = Snbt.Write(NbtBinary.ReadAll(v, NbtFormat.Bedrock)[0].Tag, SnbtStyle.Compact, 4);
                }
                catch { }
                if (preview.Length > 160) preview = preview[..160];
                Console.WriteLine($"{Convert.ToHexString(key),-30} {d,-40} {v.Length,7}  {MinecraftStudio.Core.IO.BinaryUtil.Hex(v, 16)}  {preview}");
            }
        }
        Console.WriteLine($"{shown} keys matched");
    }

    /// <summary>shape &lt;world&gt;… : is every chunk stored the way the game stores it (the converter's check)?</summary>
    public static void Shape(string[] args)
    {
        foreach (var path in args)
        {
            using var w = BedrockWorld.Open(path);
            var found = BedrockChunkShape.Check(w);
            Program.Check(found.Count == 0, $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}: " + (found.Count == 0 ? "every chunk as the game stores it" : string.Join("; ", found.Select(f => $"{f.Count:N0} × {f.Problem} (first: {f.First})"))));
        }
    }

    /// <summary>keystats &lt;world&gt; : how many records of each kind (per dimension), their total size.</summary>
    public static void Stats(string[] args)
    {
        using var w = BedrockWorld.Open(args[0]);
        var groups = new SortedDictionary<string, (int N, long Bytes)>(StringComparer.Ordinal);
        foreach (var key in w.Db.Keys())
        {
            string d = BedrockKeys.Describe(key);
            d = System.Text.RegularExpressions.Regex.Replace(d, @"^chunk -?\d+,-?\d+ ?", "");
            d = System.Text.RegularExpressions.Regex.Replace(d, @"y=-?\d+|[0-9a-f]{8}-[0-9a-f-]{27}|\d{3,}", "#");
            int len = w.Db.Get(key)?.Length ?? 0;
            groups.TryGetValue(d, out var g);
            groups[d] = (g.N + 1, g.Bytes + len);
        }
        foreach (var (k, g) in groups) Console.WriteLine($"{g.N,9} {g.Bytes,12}  {k}");
    }
}
