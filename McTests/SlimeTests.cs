// McTests/SlimeTests.cs: "slime [world …]" — the slime chunks (SlimeChunks).
// Owns: SlimeTests — the Bedrock twister worked out from three of its words equals a full MT19937; the Java formula equals
// a plain java.util.Random port written with the game's hex constants; one chunk in ten is a slime chunk; and in real
// worlds (the samples, or the worlds given; --where lists each slime) the slimes saved below y 40 outside swamps sit in a
// slime chunk or one of the eight around it (they wander through caves). Saved slimes are few and come in clusters, so this
// is a sanity check; the formulas are the documented ones. Worlds are only read.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;

static class SlimeTests
{
    public static void Run(string[] args)
    {
        var rnd = new Random(1);
        int twisterWrong = 0, javaWrong = 0;
        for (int i = 0; i < 20000; i++)
        {
            int x = rnd.Next(-200000, 200000), z = rnd.Next(-200000, 200000);
            if (SlimeChunks.Bedrock(x, z) != (FullTwister(unchecked((uint)(x * 0x1f1f1f1f) ^ (uint)z)) % 10 == 0)) twisterWrong++;
            long seed = rnd.NextInt64(long.MinValue, long.MaxValue);
            if (SlimeChunks.Java(seed, x, z) != JavaReference(seed, x, z)) javaWrong++;
        }
        Program.Check(twisterWrong == 0, $"Bedrock: the twister's first number from three of its words equals a full MT19937's (20,000 chunks, {twisterWrong} differ)");
        Program.Check(javaWrong == 0, $"Java: the formula equals a plain java.util.Random port (20,000 chunks and seeds, {javaWrong} differ)");
        int java = 0, bedrock = 0;
        for (int z = -100; z < 100; z++)
            for (int x = -100; x < 100; x++)
            {
                if (SlimeChunks.Java(12345, x, z)) java++;
                if (SlimeChunks.Bedrock(x, z)) bedrock++;
            }
        Program.Check(Math.Abs(java / 40000.0 - 0.1) < 0.01 && Math.Abs(bedrock / 40000.0 - 0.1) < 0.01, $"one chunk in ten is a slime chunk (Java {java / 400.0:0.0}%, Bedrock {bedrock / 400.0:0.0}%)");

        var given = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var worlds = given.Count > 0 ? given : new[]
        {
            Path.Combine("amulet", "java", "vanilla", "1_12_2"), Path.Combine("amulet", "java", "vanilla", "1_13"),
            Path.Combine("amulet", "java", "vanilla", "1_16_5_dimension"), Path.Combine("amulet", "bedrock", "vanilla", "1_16"),
            Path.Combine("amulet", "bedrock", "vanilla", "1_18", "vanilla"),
        }.Select(r => Path.Combine(TestPaths.Data, r)).Where(p => Directory.Exists(p)).ToList();
        int total = 0, inSlime = 0, beside = 0;
        foreach (var path in worlds)
        {
            try
            {
                using var w = WorldLoader.Open(path);
                var slime = SlimeChunks.For(w);
                if (slime == null) { Program.Info($"{w.Name}: {w.FormatDescription} — no slime chunk rule"); continue; }
                var dim = w.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                if (dim == null) continue;
                var reader = new CanonicalReader(w, dim);
                int n = 0, hit = 0, near = 0;
                foreach (var pos in dim.ChunkPositions)
                {
                    var c = reader.Get(pos);
                    if (c == null) continue;
                    foreach (var e in c.Entities)
                    {
                        if (e.GetString("id") != "minecraft:slime" || e.GetList("Pos") is not { Count: 3 } p) continue;
                        int bx = (int)Math.Floor(p[0].AsDouble), by = (int)Math.Floor(p[1].AsDouble), bz = (int)Math.Floor(p[2].AsDouble);
                        if (by >= 40 || c.BiomeAt(bx & 15, by, bz & 15).Contains("swamp", StringComparison.Ordinal)) continue;
                        n++;
                        int cx = bx >> 4, cz = bz >> 4;
                        if (slime(cx, cz)) hit++;
                        else if (Enumerable.Range(-1, 3).Any(dz => Enumerable.Range(-1, 3).Any(dx => slime(cx + dx, cz + dz)))) near++;
                        if (args.Contains("--where"))
                        {
                            var around = new List<string>();
                            for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) if (slime(cx + dx, cz + dz)) around.Add($"{dx},{dz}");
                            Program.Info($"   slime at {p[0].AsDouble:0.0}, {p[1].AsDouble:0.0}, {p[2].AsDouble:0.0} (chunk {cx}, {cz}): slime chunks at {string.Join(" ", around)}");
                        }
                    }
                }
                if (n > 0) Program.Info($"{w.Name} ({w.FormatDescription}): {n} slimes underground, {hit} in slime chunks, {near} in one of the eight around one");
                total += n; inSlime += hit; beside += near;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or MinecraftStudio.Core.IO.SaveFormatException or UnauthorizedAccessException)
            {
                Program.Info($"{path}: could not be read ({ex.Message})");
            }
        }
        if (total == 0) Program.Info("(no slimes underground in these worlds: the real-world check is skipped)");
        else Program.Check(inSlime + beside == total, $"the {total} slimes saved underground: {inSlime} ({inSlime * 100.0 / total:0}%) in a slime chunk, {beside} in one of the eight around one, {total - inSlime - beside} farther");
    }

    static bool JavaReference(long seed, int x, int z)
    {
        var r = new JavaRandom(seed + (long)(x * x * 0x4c1906) + (long)(x * 0x5ac0db) + (long)(z * z) * 0x4307a7L + (long)(z * 0x5f24f) ^ 0x3ad8025fL);
        return r.NextInt(10) == 0;
    }

    sealed class JavaRandom(long s)
    {
        long _seed = (s ^ 0x5DEECE66DL) & ((1L << 48) - 1);

        int Next(int bits)
        {
            _seed = (_seed * 0x5DEECE66DL + 0xBL) & ((1L << 48) - 1);
            return (int)((ulong)_seed >> (48 - bits));
        }

        public int NextInt(int bound)
        {
            int r = Next(31), m = bound - 1;
            if ((bound & m) == 0) return (int)(bound * (long)r >> 31);
            for (int u = r; u - (r = u % bound) + m < 0; u = Next(31)) { }
            return r;
        }
    }

    /// <summary>MT19937 as the standard library writes it: 624 words, twisted, the first tempered.</summary>
    static uint FullTwister(uint seed)
    {
        var mt = new uint[624];
        mt[0] = seed;
        for (uint i = 1; i < 624; i++) mt[i] = 1812433253u * (mt[i - 1] ^ (mt[i - 1] >> 30)) + i;
        for (int i = 0; i < 624; i++)
        {
            uint y = (mt[i] & 0x80000000u) | (mt[(i + 1) % 624] & 0x7fffffffu);
            mt[i] = mt[(i + 397) % 624] ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfu : 0u);
        }
        uint v = mt[0];
        v ^= v >> 11;
        v ^= (v << 7) & 0x9d2c5680u;
        v ^= (v << 15) & 0xefc60000u;
        v ^= v >> 18;
        return v;
    }
}
