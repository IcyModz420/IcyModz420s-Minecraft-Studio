// MinecraftStudio.Core/LegacyConsole/ConsoleChunk.cs: Legacy Console chunk decode/encode (the grid chunk formats).
// Owns: ConsoleChunkFormat, ConsoleChunkData, ConsoleChunk.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.LegacyConsole;

public enum ConsoleChunkFormat
{
    Unknown,
    /// <summary>Pre-release: NBT with 128-high Blocks (32768).</summary>
    Nbt128,
    /// <summary>TU9–TU16 era: NBT with two 128-high halves (65536).</summary>
    Nbt256,
    /// <summary>Binary chunk v8–v11: 4×4×4 grids, two 128-high halves.</summary>
    Grid128,
    /// <summary>Binary chunk v12/v13 (Aquatic): 16 sections of 4×4×4 grids with 16-bit block values.</summary>
    Section16,
}

/// <summary>Decoded console chunk: blocks as a column plus the metadata and trailing NBT.</summary>
public sealed class ConsoleChunkData
{
    public ConsoleChunkFormat Format { get; init; }
    public int Version { get; init; }
    public int X { get; init; }
    public int Z { get; init; }
    public long LastUpdate { get; init; }
    public long InhabitedTime { get; init; } = -1;
    public NbtCompound? Nbt { get; init; }
    /// <summary>Where the NBT lies in the decoded record (the whole record for the NBT formats), -1 when there is none.</summary>
    public int NbtStart { get; init; } = -1;
    public int NbtEnd { get; init; } = -1;
    public required ChunkColumn Column { get; init; }
}

public static class ConsoleChunk
{
    public static ConsoleChunkFormat Detect(byte[] p)
    {
        if (p.Length == 0) return ConsoleChunkFormat.Unknown;
        if (p[0] == (byte)NbtType.Compound)
        {
            try
            {
                var root = NbtBinary.ReadCompound(p, NbtFormat.Java);
                int n = (root.GetCompound("Level") ?? root).GetBytes("Blocks")?.Length ?? 0;
                return n >= 65536 ? ConsoleChunkFormat.Nbt256 : ConsoleChunkFormat.Nbt128;
            }
            catch { return ConsoleChunkFormat.Unknown; }
        }
        int v = BinaryPrimitives.ReadUInt16BigEndian(p);
        return v switch { >= 8 and <= 11 => ConsoleChunkFormat.Grid128, 12 or 13 => ConsoleChunkFormat.Section16, _ => ConsoleChunkFormat.Unknown };
    }

    static BlockState Block(int id, int data)
    {
        if (id < 256) return LegacyBlocks.Get(id, data, LegacyIdScheme.Java);
        return BlockState.Get($"lce:block_{id}", data == 0 ? "" : $"data={data}");
    }

    public static ConsoleChunkData Decode(byte[] p, int fallbackX, int fallbackZ)
    {
        return Detect(p) switch
        {
            ConsoleChunkFormat.Nbt128 or ConsoleChunkFormat.Nbt256 => DecodeNbt(p, fallbackX, fallbackZ),
            ConsoleChunkFormat.Grid128 => DecodeGrid(p),
            ConsoleChunkFormat.Section16 => DecodeSections(p),
            _ => throw new SaveFormatException($"Unknown console chunk format (first bytes {BinaryUtil.Hex(p.AsSpan(0, Math.Min(8, p.Length)))})."),
        };
    }

    // The NBT that follows a binary chunk's blocks (entities, block entities, ticks) and where it ends; none when it is not there.
    static (NbtCompound? Nbt, int End) Tail(byte[] b, int p)
    {
        if (p >= b.Length || b[p] != (byte)NbtType.Compound) return (null, -1);
        try
        {
            var r = new ByteReader(b, p, b.Length - p);
            return NbtBinary.ReadNamed(r, NbtFormat.Java)?.Tag is NbtCompound c ? (c, p + r.Position) : (null, -1);
        }
        catch { return (null, -1); }
    }

    static ConsoleChunkData DecodeNbt(byte[] p, int fx, int fz)
    {
        var reader = new ByteReader(p, 0, p.Length);
        var root = NbtBinary.ReadNamed(reader, NbtFormat.Java)?.Tag as NbtCompound ?? throw new SaveFormatException("The chunk's NBT has no root compound.");
        int nbtEnd = reader.Position;
        var level = root.GetCompound("Level") ?? root;
        byte[] blocks = level.GetBytes("Blocks") ?? [];
        byte[] data = level.GetBytes("Data") ?? [];
        bool tall = blocks.Length >= 65536;
        int x = level.GetInt("xPos", fx), z = level.GetInt("zPos", fz);
        var col = new ChunkColumn(x, z, 0, tall ? 15 : 7) { BiomeScheme = BiomeIdScheme.Console };
        int height = tall ? 256 : 128;
        for (int sy = 0; sy < height / 16; sy++)
        {
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int lx = 0; lx < 16; lx++)
                for (int lz = 0; lz < 16; lz++)
                    for (int ly = 0; ly < 16; ly++)
                    {
                        int y = sy * 16 + ly;
                        int i = (y >= 128 ? 32768 : 0) + (y & 127) + lz * 128 + lx * 2048;
                        if (i >= blocks.Length) continue;
                        int id = blocks[i];
                        int d = PackedArray.Nibble(data, i);
                        int key = (id << 4) | d;
                        if (!map.TryGetValue(key, out ushort pi))
                        {
                            pi = (ushort)palette.Count;
                            palette.Add(Block(id, d));
                            map[key] = pi;
                        }
                        idx[BlockSection.Index(lx, ly, lz)] = pi;
                    }
            if (palette.Count == 0 || (palette.Count == 1 && palette[0].IsAir)) continue;
            col.Sections[sy] = new BlockSection(sy, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
        if (level.GetBytes("Biomes") is { Length: >= 256 } biomes) col.BiomeIds = biomes.Take(256).Select(b => (int)b).ToArray();
        col.LastUpdate = level.GetLong("LastUpdate", -1);
        col.InhabitedTime = level.GetLong("InhabitedTime", -1);
        col.BlockEntityCount = level.GetList("TileEntities")?.Count ?? 0;
        col.EntityCount = level.GetList("Entities")?.Count ?? 0;
        return new ConsoleChunkData
        {
            Format = tall ? ConsoleChunkFormat.Nbt256 : ConsoleChunkFormat.Nbt128,
            X = x, Z = z, LastUpdate = col.LastUpdate, InhabitedTime = col.InhabitedTime, Nbt = root, NbtStart = 0, NbtEnd = nbtEnd, Column = col,
        };
    }

    /// <summary>One 128-high half of a grid-128 chunk: 512 grids of 4×4×4, ids only.</summary>
    static byte[] DecodeBlockStorage(byte[] b, ref int p)
    {
        int size = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p));
        p += 4;
        int baseAt = p;
        int data0 = baseAt + 1024;
        var output = new byte[32768];
        for (int t = 0; t < 512; t++)
        {
            int v = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(baseAt + 2 * t));
            int type = v & 3;
            if (type == 3)
            {
                if ((v & 4) != 0) output.AsSpan(t * 64, 64).Fill((byte)(v >> 8));
                else b.AsSpan(data0 + (v >> 2) * 2, 64).CopyTo(output.AsSpan(t * 64));
                continue;
            }
            int off = data0 + (v >> 2) * 2;
            int bits = type switch { 0 => 1, 1 => 2, _ => 4 };
            int npal = 1 << bits;
            var pal = b.AsSpan(off, npal);
            var raw = b.AsSpan(off + npal, 8 * bits);
            for (int i = 0; i < 64; i++)
            {
                int bitpos = i * bits;
                int val = (raw[bitpos >> 3] >> (bitpos & 7)) & (npal - 1);
                output[t * 64 + i] = pal[val];
            }
        }
        p = baseAt + size;
        return output;
    }

    /// <summary>Sparse nibble storage: per y-level a 128-byte plane (x*16+z nibbles) or an all-0/all-15 marker.</summary>
    static byte[][] SparseNibbles(byte[] b, ref int p)
    {
        int count = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p));
        var idx = b.AsSpan(p + 4, 128);
        int data = p + 4 + 128;
        var planes = new byte[128][];
        for (int y = 0; y < 128; y++)
        {
            int k = idx[y];
            planes[y] = k == 0x80 ? new byte[128] : k == 0x81 ? Enumerable.Repeat((byte)0xFF, 128).ToArray() : b.AsSpan(data + k * 128, 128).ToArray();
        }
        p = data + count * 128;
        return planes;
    }

    static int PlaneNibble(byte[][] planes, int x, int y, int z)
    {
        int i = x * 16 + z;
        byte b = planes[y][i >> 1];
        return (i & 1) == 0 ? b & 15 : b >> 4;
    }

    static ConsoleChunkData DecodeGrid(byte[] b)
    {
        int ver = BinaryPrimitives.ReadUInt16BigEndian(b);
        int x = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(2));
        int z = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(6));
        long last = BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(10));
        int p = 18;
        long inhabited = -1;
        if (ver >= 9) { inhabited = BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(18)); p = 26; }
        var lower = DecodeBlockStorage(b, ref p);
        var upper = DecodeBlockStorage(b, ref p);
        var dataLo = SparseNibbles(b, ref p);
        var dataHi = SparseNibbles(b, ref p);
        for (int k = 0; k < 4; k++) SparseNibbles(b, ref p); // sky and block light
        var heights = b.AsSpan(p, 256).ToArray(); p += 256;
        p += 2; // terrain populated flags
        var biomes = b.AsSpan(p, 256).ToArray(); p += 256;
        var (nbt, nbtEnd) = Tail(b, p);
        int nbtStart = nbt != null ? p : -1;

        var col = new ChunkColumn(x, z, 0, 15) { BiomeScheme = BiomeIdScheme.Console, LastUpdate = last, InhabitedTime = inhabited };
        for (int sy = 0; sy < 16; sy++)
        {
            var store = sy < 8 ? lower : upper;
            var data = sy < 8 ? dataLo : dataHi;
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int lx = 0; lx < 16; lx++)
                for (int lz = 0; lz < 16; lz++)
                    for (int ly = 0; ly < 16; ly++)
                    {
                        int yy = (sy * 16 + ly) & 127;
                        int t = ((lx >> 2) * 4 + (lz >> 2)) * 32 + (yy >> 2);
                        int k = ((lx & 3) * 4 + (lz & 3)) * 4 + (yy & 3);
                        int id = store[t * 64 + k];
                        int d = PlaneNibble(data, lx, yy, lz);
                        int key = (id << 4) | d;
                        if (!map.TryGetValue(key, out ushort pi))
                        {
                            pi = (ushort)palette.Count;
                            palette.Add(Block(id, d));
                            map[key] = pi;
                        }
                        idx[BlockSection.Index(lx, ly, lz)] = pi;
                    }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            col.Sections[sy] = new BlockSection(sy, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
        col.BiomeIds = biomes.Select(v => v == 0xFF ? 1 : (int)v).ToArray();
        col.HeightMap = heights.Select(h => (int)h).ToArray();
        col.BlockEntityCount = nbt?.GetList("TileEntities")?.Count ?? 0;
        col.EntityCount = nbt?.GetList("Entities")?.Count ?? 0;
        return new ConsoleChunkData { Format = ConsoleChunkFormat.Grid128, Version = ver, X = x, Z = z, LastUpdate = last, InhabitedTime = inhabited, Nbt = nbt, NbtStart = nbtStart, NbtEnd = nbtEnd, Column = col };
    }

    static readonly Dictionary<int, int> GridBits = new() { [2] = 1, [3] = 1, [4] = 2, [5] = 2, [6] = 3, [7] = 3, [8] = 4, [9] = 4 };

    static void DecodeGrid16(byte[] b, int p, int fmt, int v, Span<ushort> blocks, Span<ushort> liquid, out bool hasLiquid)
    {
        hasLiquid = false;
        if (fmt == 0) { blocks.Fill((ushort)v); return; }
        if (fmt is 0xE or 0xF)
        {
            for (int k = 0; k < 64; k++) blocks[k] = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p + 2 * k));
            if (fmt == 0xF)
            {
                hasLiquid = true;
                for (int k = 0; k < 64; k++) liquid[k] = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p + 128 + 2 * k));
            }
            return;
        }
        if (!GridBits.TryGetValue(fmt, out int bits)) throw new SaveFormatException($"Unknown grid format {fmt}.");
        int n = 1 << bits;
        Span<ushort> pal = stackalloc ushort[16];
        for (int i = 0; i < n; i++) pal[i] = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p + 2 * i));
        int q = p + 2 * n;
        for (int k = 0; k < 64; k++)
        {
            int row = k >> 3, colBit = k & 7, idx = 0;
            for (int j = 0; j < bits; j++) idx |= ((b[q + j * 8 + row] >> (7 - colBit)) & 1) << j;
            blocks[k] = pal[idx];
        }
        if ((fmt & 1) != 0)
        {
            hasLiquid = true;
            int lq = q + bits * 8;
            for (int k = 0; k < 64; k++)
            {
                int row = k >> 3, colBit = k & 7, idx = 0;
                for (int j = 0; j < bits; j++) idx |= ((b[lq + j * 8 + row] >> (7 - colBit)) & 1) << j;
                liquid[k] = pal[idx];
            }
        }
    }

    static BlockState Value16(int v)
    {
        int data = v & 0xF, id = (v >> 4) & 0x7FF;
        var s = Block(id, data);
        if ((v & 0x8000) == 0) return s;
        if (s.GetProperty("waterlogged") == "false")
            s = BlockState.Get(s.Name, s.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Key == "waterlogged" ? "true" : p.Value)));
        else if (s.Name.StartsWith("lce:", StringComparison.Ordinal))
            s = BlockState.Get(s.Name, s.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).Append(new("waterlogged", "true")));
        return s;
    }

    static ConsoleChunkData DecodeSections(byte[] b)
    {
        int ver = BinaryPrimitives.ReadUInt16BigEndian(b);
        int p = ver >= 13 ? 4 : 2;
        int x = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p));
        int z = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p + 4));
        long last = BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(p + 8));
        long inhabited = BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(p + 16));
        p += 24;
        int total = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p)) << 8;
        var jumps = new int[16];
        for (int s = 0; s < 16; s++) jumps[s] = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p + 2 + s * 2));
        var pages = b.AsSpan(p + 34, 16).ToArray();
        int baseAt = p + 50;
        var col = new ChunkColumn(x, z, 0, 15) { BiomeScheme = BiomeIdScheme.Console, LastUpdate = last, InhabitedTime = inhabited };
        Span<ushort> gb = stackalloc ushort[64];
        Span<ushort> gl = stackalloc ushort[64];
        for (int s = 0; s < 16; s++)
        {
            if (pages[s] == 0) continue;
            int sp = baseAt + jumps[s];
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int gx = 0; gx < 4; gx++)
                for (int gz = 0; gz < 4; gz++)
                    for (int gy = 0; gy < 4; gy++)
                    {
                        int v = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(sp + 2 * (gy + gz * 4 + gx * 16)));
                        int fmt = v >> 12, off = (v & 0xFFF) * 4;
                        DecodeGrid16(b, sp + 128 + off, fmt, v, gb, gl, out _);
                        for (int k = 0; k < 64; k++)
                        {
                            int lx = k >> 4, lz = (k >> 2) & 3, ly = k & 3;
                            int value = gb[k];
                            if (!map.TryGetValue(value, out ushort pi))
                            {
                                pi = (ushort)palette.Count;
                                palette.Add(Value16(value));
                                map[value] = pi;
                            }
                            idx[BlockSection.Index(gx * 4 + lx, gy * 4 + ly, gz * 4 + lz)] = pi;
                        }
                    }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            col.Sections[s] = new BlockSection(s, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
        p = baseAt + total;
        for (int k = 0; k < 4; k++) SparseNibbles(b, ref p);
        var heights = b.AsSpan(p, 256).ToArray(); p += 256;
        p += 2;
        var biomes = b.AsSpan(p, 256).ToArray(); p += 256;
        var (nbt, nbtEnd) = Tail(b, p);
        int nbtStart = nbt != null ? p : -1;
        col.BiomeIds = biomes.Select(v => v == 0xFF ? 1 : (int)v).ToArray();
        col.HeightMap = heights.Select(h => (int)h).ToArray();
        col.BlockEntityCount = nbt?.GetList("TileEntities")?.Count ?? 0;
        col.EntityCount = nbt?.GetList("Entities")?.Count ?? 0;
        return new ConsoleChunkData { Format = ConsoleChunkFormat.Section16, Version = ver, X = x, Z = z, LastUpdate = last, InhabitedTime = inhabited, Nbt = nbt, NbtStart = nbtStart, NbtEnd = nbtEnd, Column = col };
    }
}
