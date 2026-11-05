// MinecraftStudio.Core/Bedrock/BedrockChunk.cs: Decodes Bedrock / Pocket Edition chunk records into
// ChunkColumn.
// Owns: BedrockChunk.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Bedrock;

/// <summary>Decodes Bedrock / Pocket Edition chunk records into <see cref="ChunkColumn"/>.</summary>
public static class BedrockChunk
{
    /// <summary>Palette entry {name, states, version} or {name, val}, upgraded to the current Bedrock state.</summary>
    public static BlockState PaletteEntry(NbtCompound c) => Conversion.BedrockBlocks.FromPaletteNbt(c);

    /// <summary>Decodes one SubChunkPrefix value. Returns the section (Y from the key, or the value for v9).</summary>
    public static BlockSection? DecodeSubChunk(byte[] data, int keyY)
    {
        if (data.Length == 0) return null;
        int version = data[0];
        switch (version)
        {
            case 0: case 2: case 3: case 4: case 5: case 6: case 7:
                return DecodeLegacySubChunk(data, keyY);
            case 1:
                {
                    int p = 1;
                    return ReadStorage(data, ref p, keyY);
                }
            case 8:
            case 9:
                {
                    int count = data[1];
                    int p = 2;
                    int y = keyY;
                    if (version == 9) { y = (sbyte)data[2]; p = 3; }
                    BlockSection? first = null;
                    for (int i = 0; i < count; i++)
                    {
                        var s = ReadStorage(data, ref p, y);
                        if (i == 0) first = s;
                        else if (i == 1 && first != null) first.Extra = s;
                    }
                    return first ?? new BlockSection(y, [BlockState.Air], (ushort[]?)null);
                }
            default:
                throw new SaveFormatException($"Unknown sub-chunk version {version}.");
        }
    }

    /// <summary>Versions 0, 2-7: 4096 block ids and 2048 bytes of data nibbles, XZY order.</summary>
    static BlockSection DecodeLegacySubChunk(byte[] data, int y)
    {
        if (data.Length < 1 + 4096) throw new SaveFormatException("Legacy sub-chunk is too short.");
        var map = new Dictionary<int, ushort>();
        var palette = new List<BlockState>();
        var idx = new ushort[4096];
        bool hasData = data.Length >= 1 + 4096 + 2048;
        for (int i = 0; i < 4096; i++)
        {
            int id = data[1 + i];
            int d = 0;
            if (hasData) { byte b = data[1 + 4096 + (i >> 1)]; d = (i & 1) == 0 ? b & 15 : b >> 4; }
            int key = (id << 4) | d;
            if (!map.TryGetValue(key, out ushort pi))
            {
                pi = (ushort)palette.Count;
                palette.Add(LegacyBlocks.Get(id, d, LegacyIdScheme.Bedrock));
                map[key] = pi;
            }
            // i = (x*16 + z)*16 + y  ->  ours = (y*16 + z)*16 + x
            int x = i >> 8, z = (i >> 4) & 15, yy = i & 15;
            idx[(yy << 8) | (z << 4) | x] = pi;
        }
        return new BlockSection(y, palette.ToArray(), palette.Count == 1 ? null : idx);
    }

    /// <summary>One palettised block storage: header, packed words, palette.</summary>
    static BlockSection ReadStorage(byte[] data, ref int p, int y)
    {
        int header = data[p++];
        int bits = header >> 1;
        bool runtime = (header & 1) != 0;
        ushort[]? idx = null;
        int paletteCount;
        if (bits == 0)
        {
            // Single-value storage. Newer versions still write the palette count; tolerate both.
            if (p + 4 < data.Length && BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p)) == 1 && data[p + 4] == (byte)NbtType.Compound) { paletteCount = 1; p += 4; }
            else paletteCount = 1;
        }
        else
        {
            if (bits > 16) throw new SaveFormatException($"Sub-chunk storage has {bits} bits per block.");
            int perWord = 32 / bits;
            int words = (4096 + perWord - 1) / perWord;
            if (p + words * 4 > data.Length) throw new SaveFormatException("Sub-chunk storage is truncated.");
            var raw = new ushort[4096];
            PackedArray.UnpackWords(data.AsSpan(p, words * 4), bits, raw);
            p += words * 4;
            idx = new ushort[4096];
            for (int i = 0; i < 4096; i++)
            {
                int x = i >> 8, z = (i >> 4) & 15, yy = i & 15;
                idx[(yy << 8) | (z << 4) | x] = raw[i];
            }
            paletteCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p));
            p += 4;
        }
        if (paletteCount <= 0 || paletteCount > 4096) throw new SaveFormatException($"Sub-chunk palette has {paletteCount} entries.");
        var palette = new BlockState[paletteCount];
        if (runtime)
        {
            // Runtime ids are only used on the network; keep them as numbers.
            for (int i = 0; i < paletteCount; i++) { palette[i] = BlockState.Get("runtime_id", $"id={BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p))}"); p += 4; }
        }
        else
        {
            var r = new ByteReader(data, p, data.Length - p);
            for (int i = 0; i < paletteCount; i++)
            {
                var t = NbtBinary.ReadNamed(r, NbtFormat.Bedrock) ?? throw new SaveFormatException("Sub-chunk palette entry is empty.");
                palette[i] = t.Tag is NbtCompound c ? PaletteEntry(c) : BlockState.Air;
            }
            p += r.Position;
        }
        if (idx != null) for (int i = 0; i < 4096; i++) if (idx[i] >= paletteCount) idx[i] = 0;
        return new BlockSection(y, palette, paletteCount == 1 ? null : idx);
    }

    /// <summary>Pre-1.0 LegacyTerrain: 16x16x128 ids, data, sky/block light, heights, biome colours.</summary>
    public static void DecodeLegacyTerrain(byte[] data, ChunkColumn col)
    {
        if (data.Length < 32768 + 16384) throw new SaveFormatException("LegacyTerrain record is too short.");
        for (int sy = 0; sy < 8; sy++)
        {
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    for (int y = 0; y < 16; y++)
                    {
                        int i = (x << 11) | (z << 7) | (sy * 16 + y);
                        int id = data[i];
                        byte b = data[32768 + (i >> 1)];
                        int d = (i & 1) == 0 ? b & 15 : b >> 4;
                        int key = (id << 4) | d;
                        if (!map.TryGetValue(key, out ushort pi))
                        {
                            pi = (ushort)palette.Count;
                            palette.Add(LegacyBlocks.Get(id, d, LegacyIdScheme.Bedrock));
                            map[key] = pi;
                        }
                        idx[(y << 8) | (z << 4) | x] = pi;
                    }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            col.Sections[sy] = new BlockSection(sy, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
        int biomeAt = 32768 + 16384 * 3 + 256;
        if (data.Length >= biomeAt + 1024)
        {
            var ids = new int[256];
            for (int i = 0; i < 256; i++)
            {
                // Each entry: biome id in the high byte, grass colour RGB in the rest. Index is x*16+z? (stored z-major in practice)
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(biomeAt + i * 4));
                ids[i] = (int)(v & 0xFF);
            }
            col.BiomeIds = ids;
        }
    }

    /// <summary>Data2D: 256 int16 heights + 256 biome ids. Data2DLegacy: heights + 256 int32 (biome id + colour).</summary>
    public static void DecodeData2D(byte[] data, ChunkColumn col, bool legacy)
    {
        if (data.Length < 512) return;
        var heights = new int[256];
        for (int i = 0; i < 256; i++) heights[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i * 2));
        col.HeightMap = heights;
        var ids = new int[256];
        // 1.21.40+ writes u16 biome ids (1024 bytes) for old-type worlds; before that u8 (768 bytes).
        if (!legacy && data.Length >= 1024) for (int i = 0; i < 256; i++) ids[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(512 + i * 2));
        else if (!legacy && data.Length >= 768) for (int i = 0; i < 256; i++) ids[i] = data[512 + i];
        else if (legacy && data.Length >= 512 + 1024) for (int i = 0; i < 256; i++) ids[i] = data[512 + i * 4];
        else return;
        col.BiomeIds = ids;
    }

    /// <summary>Data3D (1.18+): heights, then one palettised biome storage per sub-chunk from the bottom.</summary>
    public static void DecodeData3D(byte[] data, ChunkColumn col, int minSection)
    {
        if (data.Length < 512) return;
        var heights = new int[256];
        for (int i = 0; i < 256; i++) heights[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i * 2)) + minSection * 16;
        col.HeightMap = heights;
        int p = 512;
        int sy = minSection;
        int[]? previous = null;
        var surface = new int[256];
        bool any = false;
        while (p < data.Length)
        {
            int header = data[p++];
            int[] biomes;
            if (header == 0xFF)
            {
                if (previous == null) break;
                biomes = previous;
            }
            else
            {
                int bits = header >> 1;
                biomes = new int[4096];
                if (bits == 0)
                {
                    int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p));
                    p += 4;
                    Array.Fill(biomes, v);
                }
                else
                {
                    int perWord = 32 / bits;
                    int words = (4096 + perWord - 1) / perWord;
                    if (p + words * 4 + 4 > data.Length) break;
                    var raw = new ushort[4096];
                    PackedArray.UnpackWords(data.AsSpan(p, words * 4), bits, raw);
                    p += words * 4;
                    int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p));
                    p += 4;
                    if (count <= 0 || p + count * 4 > data.Length) break;
                    var pal = new int[count];
                    for (int i = 0; i < count; i++) pal[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + i * 4));
                    p += count * 4;
                    for (int i = 0; i < 4096; i++) biomes[i] = pal[raw[i] < count ? raw[i] : 0];
                }
            }
            previous = biomes;
            // Record the biome of the highest block per column for the 2D map.
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                {
                    int h = heights[z * 16 + x] - 1;
                    if (h >> 4 == sy) { surface[z * 16 + x] = biomes[(x << 8) | (z << 4) | (h & 15)]; any = true; }
                    else if (sy == minSection && !any) surface[z * 16 + x] = biomes[(x << 8) | (z << 4)];
                }
            sy++;
        }
        col.BiomeIds = surface;
    }
}
