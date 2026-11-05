// MinecraftStudio.Core/IO/Checksums.cs: CRC-32 (IEEE 802.3, as used by zip and gzip).
// Owns: Crc32, Crc32C, XxHash32, Adler32.
using System.Buffers.Binary;
using System.Numerics;

namespace MinecraftStudio.Core.IO;

/// <summary>CRC-32 (IEEE 802.3, as used by zip and gzip).</summary>
public static class Crc32
{
    static readonly uint[] Table = Build(0xEDB88320u);

    internal static uint[] Build(uint poly)
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? poly ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Append(0, data);

    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = ~crc;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}

/// <summary>CRC-32C (Castagnoli), used by LevelDB, with LevelDB's checksum masking.</summary>
public static class Crc32C
{
    static readonly uint[] Table = Crc32.Build(0x82F63B78u);

    public static uint Compute(ReadOnlySpan<byte> data) => Append(0, data);

    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = ~crc;
        int i = 0;
        // Hardware CRC32C when available (SSE4.2 / ARM CRC).
        if (System.Runtime.Intrinsics.X86.Sse42.X64.IsSupported)
        {
            ulong c64 = c;
            for (; i + 8 <= data.Length; i += 8)
                c64 = System.Runtime.Intrinsics.X86.Sse42.X64.Crc32(c64, BinaryPrimitives.ReadUInt64LittleEndian(data[i..]));
            c = (uint)c64;
        }
        else if (System.Runtime.Intrinsics.X86.Sse42.IsSupported)
        {
            for (; i + 4 <= data.Length; i += 4)
                c = System.Runtime.Intrinsics.X86.Sse42.Crc32(c, BinaryPrimitives.ReadUInt32LittleEndian(data[i..]));
        }
        for (; i < data.Length; i++) c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return ~c;
    }

    const uint MaskDelta = 0xa282ead8u;
    public static uint Mask(uint crc) => ((crc >> 15) | (crc << 17)) + MaskDelta;
    public static uint Unmask(uint masked) { uint rot = masked - MaskDelta; return (rot >> 17) | (rot << 15); }
}

/// <summary>xxHash32, used by the LZ4 block streams Minecraft writes.</summary>
public static class XxHash32
{
    const uint P1 = 2654435761u, P2 = 2246822519u, P3 = 3266489917u, P4 = 668265263u, P5 = 374761393u;

    public static uint Compute(ReadOnlySpan<byte> data, uint seed)
    {
        int len = data.Length, i = 0;
        uint h;
        if (len >= 16)
        {
            uint v1 = seed + P1 + P2, v2 = seed + P2, v3 = seed, v4 = seed - P1;
            for (; i + 16 <= len; i += 16)
            {
                v1 = Round(v1, BinaryPrimitives.ReadUInt32LittleEndian(data[i..]));
                v2 = Round(v2, BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 4)..]));
                v3 = Round(v3, BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 8)..]));
                v4 = Round(v4, BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 12)..]));
            }
            h = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
        }
        else h = seed + P5;
        h += (uint)len;
        for (; i + 4 <= len; i += 4)
            h = BitOperations.RotateLeft(h + BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) * P3, 17) * P4;
        for (; i < len; i++)
            h = BitOperations.RotateLeft(h + data[i] * P5, 11) * P1;
        h ^= h >> 15; h *= P2; h ^= h >> 13; h *= P3; h ^= h >> 16;
        return h;
    }

    static uint Round(uint acc, uint input) => BitOperations.RotateLeft(acc + input * P2, 13) * P1;
}

/// <summary>Adler-32 (zlib trailer).</summary>
public static class Adler32
{
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        int i = 0;
        while (i < data.Length)
        {
            int n = Math.Min(5552, data.Length - i);
            for (int k = 0; k < n; k++) { a += data[i++]; b += a; }
            a %= 65521; b %= 65521;
        }
        return (b << 16) | a;
    }
}
