// McTests/BeDump.cs: be <world> <dimension id> <x> <y> <z>: prints the canonical block entity at a position.
// Owns: BeDump.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

/// <summary>be &lt;world&gt; &lt;dimension id&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;: prints the canonical block entity at a position.</summary>
static class BeDump
{
    public static void Run(string[] args)
    {
        if (args[0] == "item")
        {
            var it = new NbtCompound { { "id", new NbtShort(short.Parse(args[1])) }, { "Damage", new NbtShort(short.Parse(args[2])) }, { "Count", new NbtByte(24) } };
            var r = Items.FromBedrock(it);
            Console.WriteLine(r == null ? "null" : Snbt.Write(r, SnbtStyle.Compact));
            var (n, d) = Items.LegacyItemNumber(args.Length > 3 ? args[3] : "minecraft:stone_slab");
            Console.WriteLine($"legacy number of {(args.Length > 3 ? args[3] : "minecraft:stone_slab")}: {n}:{d}");
            return;
        }
        using var w = WorldLoader.Open(args[0]);
        if (args.Length == 1) { var i = WorldInfoReader.Read(w); Console.WriteLine($"spawn {i.SpawnX} {i.SpawnY} {i.SpawnZ}"); return; }
        var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == args[1]);
        int x = int.Parse(args[2]), y = int.Parse(args[3]), z = int.Parse(args[4]);
        var c = new CanonicalReader(w, dim).Get(new ChunkPos(x >> 4, z >> 4));
        var be = c?.BlockEntities.FirstOrDefault(b => b.GetInt("x") == x && b.GetInt("y") == y && b.GetInt("z") == z);
        Console.WriteLine(c == null ? "no chunk" : be == null ? "no block entity; block " + c.GetBlock(x & 15, y, z & 15) : Snbt.Write(be, SnbtStyle.Compact));
    }
}
