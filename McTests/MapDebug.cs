// McTests/MapDebug.cs: MapDebug.
// Owns: MapDebug.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class MapDebug
{
    /// <summary>mapdebugb : default Java state for every Bedrock block name whose Java targets differ by name (many-to-one cases).</summary>
    public static void Bedrock(string[] args)
    {
        var names = Enumerable.Range(0, 1).SelectMany(_ => BedrockBlocksNames()).Distinct().OrderBy(n => n).ToList();
        foreach (var name in names)
        {
            var targets = BedrockBlocks.StatesOf(name).Select(s => BedrockJavaMap.ToJava(s)?.Name ?? "-").Distinct().ToList();
            if (args.Length > 0 && !name.Contains(args[0])) continue;
            if (targets.Count == 1 && targets[0] == name) continue;
            var first = BedrockBlocks.StatesOf(name)[0];
            Console.WriteLine($"{name,-45} -> {string.Join(", ", targets.Take(6))}{(targets.Count > 6 ? $" (+{targets.Count - 6})" : "")}");
        }
    }

    static IEnumerable<string> BedrockBlocksNames()
    {
        for (int id = 0; id < BlockState.Count; id++) { }
        // All canonical Bedrock names via the legacy table and the J2B outputs.
        var set = new HashSet<string>();
        foreach (var j in JavaBlockData.Latest.Blocks.Keys)
            if (BedrockJavaMap.ToBedrock(JavaBlocks.Canonicalize(BlockState.Get(j))) is BlockState b) set.Add(b.Name);
        return set;
    }

    /// <summary>mapdebug &lt;java state&gt;... : Java -> Bedrock -> Java through the tables, with the palette NBT round trip.</summary>
    public static void Run(string[] args)
    {
        foreach (var a in args)
        {
            var j = JavaBlocks.Canonicalize(BlockState.Parse(a));
            var b = BedrockJavaMap.ToBedrock(j);
            Console.WriteLine($"{j}\n  J2B   -> {b}");
            if (b == null) continue;
            var nbt = BedrockBlocks.ToPaletteNbt(BedrockBlocks.Fit(b));
            Console.WriteLine($"  NBT   -> {Snbt.Write(nbt, SnbtStyle.Compact)}");
            var back = BedrockBlocks.FromPaletteNbt(nbt);
            Console.WriteLine($"  read  -> {back}");
            Console.WriteLine($"  B2J   -> {BedrockJavaMap.ToJava(back)}");
            Console.WriteLine($"  final -> {CanonicalReader.BedrockToJava(back)}");
        }
    }
}
