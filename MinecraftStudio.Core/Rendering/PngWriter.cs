// MinecraftStudio.Core/Rendering/PngWriter.cs: Minimal RGBA PNG encoder (no System.Drawing dependency), and 16-bit grey
// with alpha and text notes for heightmaps.
// Owns: PngWriter.
using System.Buffers.Binary;
using System.IO.Compression;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Rendering;

/// <summary>Minimal RGBA PNG encoder (no System.Drawing dependency).</summary>
public static class PngWriter
{
    public static byte[] Encode(ReadOnlySpan<uint> argb, int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        int p = 0;
        for (int y = 0; y < height; y++)
        {
            raw[p++] = 0;
            for (int x = 0; x < width; x++)
            {
                uint c = argb[y * width + x];
                raw[p++] = (byte)(c >> 16);
                raw[p++] = (byte)(c >> 8);
                raw[p++] = (byte)c;
                raw[p++] = (byte)(c >> 24);
            }
        }
        byte[] idat = IO.Compression.ZlibCompress(raw, CompressionLevel.Fastest);
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", idat);
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>
    /// A 16-bit grey PNG with alpha (a value below 0 is see-through) and text notes (tEXt, Latin-1): a heightmap, where
    /// 8 bits would lose the heights between levels.
    /// </summary>
    public static byte[] EncodeGray16(ReadOnlySpan<int> gray, int width, int height, IEnumerable<(string Key, string Value)>? notes = null)
    {
        int stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        var row = new byte[stride];
        for (int y = 0, p = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int v = gray[y * width + x];
                int g = Math.Clamp(v, 0, 65535), a = v < 0 ? 0 : 65535;
                row[x * 4] = (byte)(g >> 8); row[x * 4 + 1] = (byte)g; row[x * 4 + 2] = (byte)(a >> 8); row[x * 4 + 3] = (byte)a;
            }
            // Filter "Sub": land changes slowly from pixel to pixel, so the differences pack small.
            raw[p++] = 1;
            for (int i = 0; i < stride; i++) raw[p++] = (byte)(row[i] - (i >= 4 ? row[i - 4] : 0));
        }
        byte[] idat = IO.Compression.ZlibCompress(raw, CompressionLevel.Optimal);
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 16; ihdr[9] = 4; // 16-bit grey + alpha
        Chunk(ms, "IHDR", ihdr);
        foreach (var (key, value) in notes ?? [])
            Chunk(ms, "tEXt", [.. System.Text.Encoding.Latin1.GetBytes(key), 0, .. System.Text.Encoding.Latin1.GetBytes(value)]);
        Chunk(ms, "IDAT", idat);
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        s.Write(head);
        s.Write(data);
        uint crc = Crc32.Append(Crc32.Compute(head[4..]), data);
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }
}
