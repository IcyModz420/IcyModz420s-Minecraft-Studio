// McTests/LceIds.cs: LceIds.
// Owns: LceIds.
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.World;

static class LceIds
{
    sealed class Stat
    {
        public long Count;
        public readonly Dictionary<int, long> Data = [], Y = [];
        public readonly Dictionary<string, long> Above = [], Below = [], Side = [], Biome = [];
    }

    static void Add<T>(Dictionary<T, long> d, T k) where T : notnull => d[k] = d.GetValueOrDefault(k) + 1;

    /// <summary>lceids &lt;console save&gt; : statistics of the 1.13-era block ids (lce:block_N) to identify them.</summary>
    public static void Run(string[] args)
    {
        using var w = (ConsoleWorld)ConsoleSave.Open(args[0]);
        var stats = new SortedDictionary<int, Stat>();
        foreach (var dim in w.Dimensions.OfType<ConsoleDimension>())
            foreach (var pos in dim.ChunkPositions)
            {
                var data = dim.LoadConsoleChunk(pos);
                if (data == null) continue;
                var col = data.Column;
                foreach (var (sy, s) in col.Sections)
                {
                    if (!s.Palette.Any(p => p.Name.StartsWith("lce:"))) continue;
                    for (int i = 0; i < 4096; i++)
                    {
                        var b = s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]];
                        if (!b.Name.StartsWith("lce:block_")) continue;
                        int id = int.Parse(b.Name[10..]);
                        int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                        if (!stats.TryGetValue(id, out var st)) stats[id] = st = new Stat();
                        st.Count++;
                        Add(st.Data, int.TryParse(b.GetProperty("data"), out int d) ? d : 0);
                        Add(st.Y, y / 8 * 8);
                        var up = col.GetBlock(x, y + 1, z);
                        Add(st.Above, up.Name == b.Name ? $"same(d{b.GetProperty("data") ?? "0"}->d{up.GetProperty("data") ?? "0"})" : Short(up));
                        Add(st.Below, Short(col.GetBlock(x, y - 1, z)));
                        if (x < 15) Add(st.Side, Short(col.GetBlock(x + 1, y, z)));
                        if (col.BiomeIds != null) Add(st.Biome, MinecraftStudio.Core.Rendering.Biomes.NameOf(col.BiomeIds[z * 16 + x], BiomeIdScheme.Console));
                        if (b.Properties.Contains("waterlogged")) Add(st.Data, 100);
                    }
                }
            }
        foreach (var (id, st) in stats)
        {
            string Top<T>(Dictionary<T, long> d, int n) where T : notnull => string.Join(", ", d.OrderByDescending(kv => kv.Value).Take(n).Select(kv => $"{kv.Key}:{kv.Value}"));
            Console.WriteLine($"lce {id} (0x{id:X}): {st.Count} blocks");
            Console.WriteLine($"   data   {Top(st.Data, 16)}");
            Console.WriteLine($"   y      {Top(st.Y, 8)}");
            Console.WriteLine($"   above  {Top(st.Above, 6)}");
            Console.WriteLine($"   below  {Top(st.Below, 6)}");
            Console.WriteLine($"   side   {Top(st.Side, 6)}");
            Console.WriteLine($"   biome  {Top(st.Biome, 5)}");
        }
    }

    static string Short(BlockState s) => s.Name.StartsWith("lce:") ? s.Name[4..] : s.BaseName;
}
