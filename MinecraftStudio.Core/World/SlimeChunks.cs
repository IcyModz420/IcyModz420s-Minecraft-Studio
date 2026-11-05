// MinecraftStudio.Core/World/SlimeChunks.cs: the slime chunks of a world — where slimes spawn underground.
// Owns: SlimeChunks. Java: one chunk in ten, chosen from the world seed by the game's own java.util.Random formula;
// Bedrock and Pocket Edition: the same chunks in every world (a Mersenne Twister seeded from the chunk's position alone).
// Other editions: not known, so none are shown. Slimes spawn in them below y 40 in any biome (in swamps also on the
// surface). Works for land not generated yet: it needs only the seed. Checked against the slimes saved in real worlds
// (McTests slime).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;

namespace MinecraftStudio.Core.World;

public static class SlimeChunks
{
    /// <summary>Whether a chunk of this world is a slime chunk, or null when the edition's rule is not known.</summary>
    public static Func<int, int, bool>? For(SaveWorld world) => world switch
    {
        JavaWorld j when j.Seed is long seed => (x, z) => Java(seed, x, z),
        BedrockWorld or PocketLegacyWorld => Bedrock,
        _ => null,
    };

    /// <summary>Java Edition: new Random(seed + x²·4987142 + x·5947611 + z²·4392871 + z·389711 ^ 987234911).nextInt(10) == 0 (int arithmetic where the game uses it).</summary>
    public static bool Java(long seed, int x, int z)
    {
        long s = unchecked(seed + (long)(x * x * 4987142) + (long)(x * 5947611) + (long)(z * z) * 4392871L + (long)(z * 389711)) ^ 987234911L;
        // java.util.Random: scramble, then nextInt(10).
        long r = (s ^ 0x5DEECE66DL) & ((1L << 48) - 1);
        int Next()
        {
            r = unchecked(r * 0x5DEECE66DL + 0xBL) & ((1L << 48) - 1);
            return (int)(r >> 17);
        }
        int u = Next(), v;
        while (unchecked(u - (v = u % 10) + 9) < 0) u = Next();
        return v == 0;
    }

    /// <summary>Bedrock Edition: the first number of a Mersenne Twister seeded with (x · 0x1f1f1f1f) ^ z, modulo 10, is 0.</summary>
    public static bool Bedrock(int x, int z) => FirstTwister(unchecked((uint)(x * 0x1f1f1f1f) ^ (uint)z)) % 10 == 0;

    /// <summary>MT19937's first output for a seed (only the state words it reads are worked out).</summary>
    static uint FirstTwister(uint seed)
    {
        uint m0 = seed, m1 = 0, m397 = 0, prev = seed;
        for (uint i = 1; i <= 397; i++)
        {
            prev = unchecked(1812433253u * (prev ^ (prev >> 30)) + i);
            if (i == 1) m1 = prev;
        }
        m397 = prev;
        uint y = (m0 & 0x80000000u) | (m1 & 0x7fffffffu);
        uint v = m397 ^ (y >> 1) ^ ((y & 1) != 0 ? 0x9908b0dfu : 0u);
        v ^= v >> 11;
        v ^= (v << 7) & 0x9d2c5680u;
        v ^= (v << 15) & 0xefc60000u;
        v ^= v >> 18;
        return v;
    }
}
