// MinecraftStudio.Core/World/ChunkColumn.cs: 16x16x16 blocks as palette indices in YZX order (index = y*256 +
// z*16 + x).
// Owns: BlockSection, BiomeIdScheme, ChunkColumn, PackedArray.
namespace MinecraftStudio.Core.World;

/// <summary>16x16x16 blocks as palette indices in YZX order (index = y*256 + z*16 + x).</summary>
public sealed class BlockSection
{
    ushort[]? _indices;
    Func<ushort[]?>? _unpack;

    public BlockSection(int y, BlockState[] palette, ushort[]? indices)
    {
        Y = y;
        Palette = palette;
        _indices = indices;
    }

    /// <summary>Creates a section whose indices are unpacked on first use.</summary>
    public BlockSection(int y, BlockState[] palette, Func<ushort[]?> unpack)
    {
        Y = y;
        Palette = palette;
        _unpack = unpack;
    }

    /// <summary>Section index (block Y / 16).</summary>
    public int Y { get; }
    public BlockState[] Palette { get; set; }

    /// <summary>Null when every block is Palette[0].</summary>
    public ushort[]? Indices
    {
        get
        {
            if (_unpack != null) { _indices = _unpack(); _unpack = null; }
            return _indices;
        }
        set { _unpack = null; _indices = value; }
    }

    /// <summary>Biomes for this section: 64 entries (4x4x4 cells, index (y/4)*16 + (z/4)*4 + x/4), if stored per section.</summary>
    public string[]? Biomes { get; set; }
    /// <summary>Optional second layer (Bedrock water-logging / liquid layer).</summary>
    public BlockSection? Extra { get; set; }

    public static int Index(int x, int y, int z) => (y << 8) | (z << 4) | x;

    public BlockState Get(int x, int y, int z) => Indices == null ? Palette[0] : Palette[Indices[Index(x, y, z)]];

    /// <summary>True when the palette holds only air (no unpacking needed).</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var s in Palette) if (!s.IsAir) return false;
            return true;
        }
    }

    /// <summary>Highest non-air block y (0..15) in column x,z, or -1.</summary>
    public int TopY(int x, int z, Func<BlockState, bool>? skip = null)
    {
        if (Indices == null)
        {
            var s = Palette.Length > 0 ? Palette[0] : BlockState.Air;
            return s.IsAir || (skip != null && skip(s)) ? -1 : 15;
        }
        for (int y = 15; y >= 0; y--)
        {
            var s = Palette[Indices[(y << 8) | (z << 4) | x]];
            if (!s.IsAir && (skip == null || !skip(s))) return y;
        }
        return -1;
    }
}

/// <summary>Which numeric biome id table a chunk uses.</summary>
public enum BiomeIdScheme { Java, Bedrock, Console }

/// <summary>A decoded chunk column, independent of edition and version.</summary>
public sealed class ChunkColumn
{
    /// <summary>How to interpret <see cref="BiomeIds"/>.</summary>
    public BiomeIdScheme BiomeScheme { get; set; }

    public ChunkColumn(int x, int z, int minSection, int maxSection)
    {
        X = x;
        Z = z;
        MinSection = minSection;
        MaxSection = maxSection;
    }

    public int X { get; }
    public int Z { get; }
    /// <summary>Lowest and highest section index that may exist (e.g. -4..19 for 1.18+ overworld).</summary>
    public int MinSection { get; }
    public int MaxSection { get; }
    public SortedDictionary<int, BlockSection> Sections { get; } = [];

    /// <summary>Biome name per column (16x16, index z*16+x) at the surface, if known.</summary>
    public string?[]? SurfaceBiomes { get; set; }
    /// <summary>Numeric biome per column when the format stores ids.</summary>
    public int[]? BiomeIds { get; set; }
    /// <summary>Highest-block heights per column if the format stores them (may be stale).</summary>
    public int[]? HeightMap { get; set; }

    public long InhabitedTime { get; set; } = -1;
    public long LastUpdate { get; set; } = -1;
    public string? Status { get; set; }
    public int BlockEntityCount { get; set; }
    public int EntityCount { get; set; }

    public BlockState GetBlock(int x, int y, int z)
    {
        return Sections.TryGetValue(y >> 4, out var s) ? s.Get(x, y & 15, z) : BlockState.Air;
    }

    /// <summary>Topmost non-air block for column x,z (block y and state), or null.</summary>
    public (int Y, BlockState State)? Top(int x, int z, Func<BlockState, bool>? skip = null)
    {
        foreach (var kv in Sections.Reverse())
        {
            int y = kv.Value.TopY(x, z, skip);
            if (y >= 0) return ((kv.Key << 4) + y, kv.Value.Get(x, y, z));
        }
        return null;
    }
}

/// <summary>Bit-packed integer arrays used by chunk formats.</summary>
public static class PackedArray
{
    /// <summary>Java 1.16+ layout: values never span two longs.</summary>
    public static void UnpackNonSpanning(long[] data, int bits, Span<ushort> output)
    {
        if (bits <= 0) { output.Clear(); return; }
        int perLong = 64 / bits;
        ulong mask = (1UL << bits) - 1;
        int i = 0;
        for (int l = 0; l < data.Length && i < output.Length; l++)
        {
            ulong v = (ulong)data[l];
            for (int k = 0; k < perLong && i < output.Length; k++, i++)
            {
                output[i] = (ushort)(v & mask);
                v >>= bits;
            }
        }
        if (i < output.Length) output[i..].Clear();
    }

    /// <summary>Java 1.13-1.15 layout: values are packed back to back and may span longs.</summary>
    public static void UnpackSpanning(long[] data, int bits, Span<ushort> output)
    {
        if (bits <= 0) { output.Clear(); return; }
        ulong mask = (1UL << bits) - 1;
        for (int i = 0; i < output.Length; i++)
        {
            long bit = (long)i * bits;
            int idx = (int)(bit >> 6);
            int off = (int)(bit & 63);
            if (idx >= data.Length) { output[i..].Clear(); return; }
            ulong v = (ulong)data[idx] >> off;
            if (off + bits > 64 && idx + 1 < data.Length) v |= (ulong)data[idx + 1] << (64 - off);
            output[i] = (ushort)(v & mask);
        }
    }

    public static long[] PackNonSpanning(ReadOnlySpan<ushort> values, int bits)
    {
        int perLong = 64 / bits;
        var data = new long[(values.Length + perLong - 1) / perLong];
        for (int i = 0; i < values.Length; i++)
            data[i / perLong] |= (long)values[i] << ((i % perLong) * bits);
        return data;
    }

    public static long[] PackSpanning(ReadOnlySpan<ushort> values, int bits)
    {
        var data = new long[(values.Length * bits + 63) / 64];
        for (int i = 0; i < values.Length; i++)
        {
            long bit = (long)i * bits;
            int idx = (int)(bit >> 6), off = (int)(bit & 63);
            data[idx] |= (long)values[i] << off;
            if (off + bits > 64) data[idx + 1] |= (long)((ulong)values[i] >> (64 - off));
        }
        return data;
    }

    /// <summary>Bits per entry for a non-spanning array of <paramref name="count"/> entries stored in <paramref name="longs"/> longs.</summary>
    public static int InferNonSpanningBits(int longs, int count, int preferred)
    {
        if (preferred > 0 && (count + 64 / preferred - 1) / (64 / preferred) == longs) return preferred;
        for (int bits = 1; bits <= 32; bits++)
            if ((count + 64 / bits - 1) / (64 / bits) == longs) return bits;
        return preferred;
    }

    /// <summary>Unpacks Bedrock word-based palette storage (32-bit LE words, values never span words).</summary>
    public static void UnpackWords(ReadOnlySpan<byte> words, int bits, Span<ushort> output)
    {
        if (bits <= 0) { output.Clear(); return; }
        int perWord = 32 / bits;
        uint mask = (1u << bits) - 1;
        int i = 0;
        for (int w = 0; w + 4 <= words.Length && i < output.Length; w += 4)
        {
            uint v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(words[w..]);
            for (int k = 0; k < perWord && i < output.Length; k++, i++)
            {
                output[i] = (ushort)(v & mask);
                v >>= bits;
            }
        }
        if (i < output.Length) output[i..].Clear();
    }

    /// <summary>Nibble array (two 4-bit values per byte, low nibble first).</summary>
    public static int Nibble(byte[] arr, int index)
    {
        int b = index >> 1;
        if (b >= arr.Length) return 0;
        return (index & 1) == 0 ? arr[b] & 0x0F : arr[b] >> 4;
    }

    public static void SetNibble(byte[] arr, int index, int value)
    {
        int b = index >> 1;
        if ((index & 1) == 0) arr[b] = (byte)((arr[b] & 0xF0) | (value & 0x0F));
        else arr[b] = (byte)((arr[b] & 0x0F) | ((value & 0x0F) << 4));
    }
}
