// MinecraftStudio.Core/IO/Compression.cs: Compression schemes found in Minecraft saves.
// Owns: CompressionKind, Compression, Lz4, Snappy.
using System.Buffers.Binary;
using System.IO.Compression;

namespace MinecraftStudio.Core.IO;

/// <summary>Compression schemes found in Minecraft saves.</summary>
public enum CompressionKind
{
    None,
    Gzip,
    Zlib,
    /// <summary>Raw deflate without a zlib header (Bedrock LevelDB type 4).</summary>
    Deflate,
    /// <summary>lz4-java LZ4BlockOutputStream framing (Java region compression type 4).</summary>
    Lz4Java,
    /// <summary>Xbox 360 XMemCompress (LZX).</summary>
    Lzx,
    Snappy,
}

public static class Compression
{
    public static CompressionKind Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B) return CompressionKind.Gzip;
        if (IsZlibHeader(data)) return CompressionKind.Zlib;
        if (data.Length >= 8 && BinaryUtil.StartsWith(data, "LZ4Block"u8)) return CompressionKind.Lz4Java;
        return CompressionKind.None;
    }

    public static bool IsZlibHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && (data[0] & 0x0F) == 8 && (data[0] >> 4) <= 7 && ((data[0] << 8) | data[1]) % 31 == 0;

    public static byte[] Decompress(ReadOnlySpan<byte> data, CompressionKind kind, int sizeHint = 0) => kind switch
    {
        CompressionKind.None => data.ToArray(),
        CompressionKind.Gzip => Gunzip(data, sizeHint),
        CompressionKind.Zlib => ZlibDecompress(data, sizeHint),
        CompressionKind.Deflate => Inflate(data, sizeHint),
        CompressionKind.Lz4Java => Lz4.DecompressJavaStream(data),
        CompressionKind.Snappy => Snappy.Decompress(data),
        CompressionKind.Lzx => throw new NotSupportedException("LZX data needs its frame layout; use XMemCompress."),
        _ => throw new NotSupportedException(kind.ToString()),
    };

    public static byte[] Compress(ReadOnlySpan<byte> data, CompressionKind kind) => kind switch
    {
        CompressionKind.None => data.ToArray(),
        CompressionKind.Gzip => Gzip(data),
        CompressionKind.Zlib => ZlibCompress(data),
        CompressionKind.Deflate => Deflate(data),
        CompressionKind.Lz4Java => Lz4.CompressJavaStream(data),
        _ => throw new NotSupportedException($"Writing {kind} data is not supported here."),
    };

    /// <summary>Decompresses data that may or may not be compressed (gzip, zlib or plain).</summary>
    public static byte[] DecompressAuto(byte[] data, out CompressionKind kind)
    {
        kind = Detect(data);
        if (kind == CompressionKind.None) return data;
        try { return Decompress(data, kind); }
        catch when (kind == CompressionKind.Zlib) { kind = CompressionKind.None; return data; }
    }

    static byte[] ReadAll(Stream s, int sizeHint)
    {
        using var ms = new MemoryStream(sizeHint > 0 ? sizeHint : 4096);
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static unsafe byte[] Inflate(ReadOnlySpan<byte> data, int sizeHint = 0)
    {
        fixed (byte* p = data)
        {
            using var src = new UnmanagedMemoryStream(p, data.Length);
            using var d = new DeflateStream(src, CompressionMode.Decompress);
            return ReadAll(d, sizeHint > 0 ? sizeHint : data.Length * 4);
        }
    }

    public static unsafe byte[] ZlibDecompress(ReadOnlySpan<byte> data, int sizeHint = 0)
    {
        fixed (byte* p = data)
        {
            using var src = new UnmanagedMemoryStream(p, data.Length);
            using var d = new ZLibStream(src, CompressionMode.Decompress);
            return ReadAll(d, sizeHint > 0 ? sizeHint : data.Length * 4);
        }
    }

    public static unsafe byte[] Gunzip(ReadOnlySpan<byte> data, int sizeHint = 0)
    {
        if (sizeHint <= 0 && data.Length >= 18) sizeHint = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]), 256u << 20);
        fixed (byte* p = data)
        {
            using var src = new UnmanagedMemoryStream(p, data.Length);
            using var d = new GZipStream(src, CompressionMode.Decompress);
            return ReadAll(d, sizeHint);
        }
    }

    public static byte[] Deflate(ReadOnlySpan<byte> data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream(data.Length / 2 + 64);
        using (var d = new DeflateStream(ms, level, leaveOpen: true)) d.Write(data);
        return ms.ToArray();
    }

    public static byte[] ZlibCompress(ReadOnlySpan<byte> data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream(data.Length / 2 + 64);
        using (var d = new ZLibStream(ms, level, leaveOpen: true)) d.Write(data);
        return ms.ToArray();
    }

    /// <summary>Gzip with the same header Java's GZIPOutputStream writes (no name, mtime 0).</summary>
    public static byte[] Gzip(ReadOnlySpan<byte> data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream(data.Length / 2 + 64);
        ms.Write([0x1F, 0x8B, 8, 0, 0, 0, 0, 0, 0, 0]);
        using (var d = new DeflateStream(ms, level, leaveOpen: true)) d.Write(data);
        Span<byte> trailer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, Crc32.Compute(data));
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], (uint)data.Length);
        ms.Write(trailer);
        return ms.ToArray();
    }
}

/// <summary>LZ4 block format plus the lz4-java "LZ4Block" stream framing used by Minecraft region files.</summary>
public static class Lz4
{
    static ReadOnlySpan<byte> Magic => "LZ4Block"u8;
    const uint JavaSeed = 0x9747b28c;
    const int MethodRaw = 0x10, MethodLz4 = 0x20;

    public static int DecompressBlock(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int sp = 0, dp = 0;
        while (true)
        {
            if (sp >= src.Length) throw new SaveFormatException("LZ4: truncated block.");
            int token = src[sp++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[sp++]; lit += b; } while (b == 255); }
            if (lit > 0)
            {
                if (sp + lit > src.Length || dp + lit > dst.Length) throw new SaveFormatException("LZ4: literal run out of bounds.");
                src.Slice(sp, lit).CopyTo(dst[dp..]);
                sp += lit; dp += lit;
            }
            if (sp >= src.Length) break; // last sequence has literals only
            int offset = src[sp] | (src[sp + 1] << 8);
            sp += 2;
            if (offset == 0 || offset > dp) throw new SaveFormatException("LZ4: bad match offset.");
            int mlen = token & 15;
            if (mlen == 15) { int b; do { b = src[sp++]; mlen += b; } while (b == 255); }
            mlen += 4;
            if (dp + mlen > dst.Length) throw new SaveFormatException("LZ4: match out of bounds.");
            int from = dp - offset;
            if (offset >= mlen) { dst.Slice(from, mlen).CopyTo(dst[dp..]); dp += mlen; }
            else for (int k = 0; k < mlen; k++) dst[dp++] = dst[from + k];
        }
        return dp;
    }

    /// <summary>Greedy single-pass LZ4 block compressor (valid output, moderate ratio).</summary>
    public static byte[] CompressBlock(ReadOnlySpan<byte> src)
    {
        var w = new ByteWriter(src.Length / 2 + 32);
        const int HashLog = 16;
        var table = new int[1 << HashLog];
        Array.Fill(table, -1);
        int anchor = 0, ip = 0;
        int matchLimit = src.Length - 12; // LZ4 rule: last match must start >= 12 bytes before end
        while (ip < matchLimit)
        {
            uint seq = BinaryPrimitives.ReadUInt32LittleEndian(src[ip..]);
            int h = (int)((seq * 2654435761u) >> (32 - HashLog));
            int cand = table[h];
            table[h] = ip;
            if (cand >= 0 && ip - cand <= 65535 && BinaryPrimitives.ReadUInt32LittleEndian(src[cand..]) == seq)
            {
                int mlen = 4;
                int maxLen = src.Length - 5 - ip; // last 5 bytes are always literals
                while (mlen < maxLen && src[cand + mlen] == src[ip + mlen]) mlen++;
                EmitSequence(w, src.Slice(anchor, ip - anchor), ip - cand, mlen);
                ip += mlen;
                anchor = ip;
                continue;
            }
            ip++;
        }
        // trailing literals
        int litLen = src.Length - anchor;
        int tok = Math.Min(litLen, 15) << 4;
        w.U8((byte)tok);
        if (litLen >= 15) WriteLen(w, litLen - 15);
        w.Bytes(src[anchor..]);
        return w.ToArray();
    }

    static void EmitSequence(ByteWriter w, ReadOnlySpan<byte> literals, int offset, int matchLen)
    {
        int ml = matchLen - 4;
        w.U8((byte)((Math.Min(literals.Length, 15) << 4) | Math.Min(ml, 15)));
        if (literals.Length >= 15) WriteLen(w, literals.Length - 15);
        w.Bytes(literals);
        w.U16LE((ushort)offset);
        if (ml >= 15) WriteLen(w, ml - 15);
    }

    static void WriteLen(ByteWriter w, int n)
    {
        while (n >= 255) { w.U8(255); n -= 255; }
        w.U8((byte)n);
    }

    public static byte[] DecompressJavaStream(ReadOnlySpan<byte> data)
    {
        var output = new ByteWriter(Math.Max(1024, data.Length * 3));
        int p = 0;
        while (p < data.Length)
        {
            if (data.Length - p < 21 || !data.Slice(p, 8).SequenceEqual(Magic)) throw new SaveFormatException("LZ4 stream: missing LZ4Block header.");
            int token = data[p + 8];
            int method = token & 0xF0;
            int clen = BinaryPrimitives.ReadInt32LittleEndian(data[(p + 9)..]);
            int olen = BinaryPrimitives.ReadInt32LittleEndian(data[(p + 13)..]);
            uint check = BinaryPrimitives.ReadUInt32LittleEndian(data[(p + 17)..]);
            p += 21;
            if (olen == 0 && clen == 0) break; // end mark
            if (clen < 0 || olen < 0 || p + clen > data.Length) throw new SaveFormatException("LZ4 stream: bad block lengths.");
            var dst = output.Reserve(olen);
            if (method == MethodRaw)
            {
                if (clen != olen) throw new SaveFormatException("LZ4 stream: raw block length mismatch.");
                data.Slice(p, clen).CopyTo(dst);
            }
            else if (method == MethodLz4)
            {
                if (DecompressBlock(data.Slice(p, clen), dst) != olen) throw new SaveFormatException("LZ4 stream: block decoded to the wrong size.");
            }
            else throw new SaveFormatException($"LZ4 stream: unknown method 0x{method:X2}.");
            if ((XxHash32.Compute(dst, JavaSeed) & 0x0FFFFFFF) != check) throw new SaveFormatException("LZ4 stream: checksum mismatch.");
            p += clen;
        }
        return output.ToArray();
    }

    public static byte[] CompressJavaStream(ReadOnlySpan<byte> data, int blockSize = 1 << 16)
    {
        int level = Math.Max(0, 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(blockSize - 1)) - 10);
        var w = new ByteWriter(data.Length / 2 + 64);
        for (int off = 0; off < data.Length; off += blockSize)
        {
            var chunk = data.Slice(off, Math.Min(blockSize, data.Length - off));
            byte[] comp = CompressBlock(chunk);
            bool raw = comp.Length >= chunk.Length;
            w.Bytes(Magic);
            w.U8((byte)((raw ? MethodRaw : MethodLz4) | level));
            w.I32LE(raw ? chunk.Length : comp.Length);
            w.I32LE(chunk.Length);
            w.U32LE(XxHash32.Compute(chunk, JavaSeed) & 0x0FFFFFFF);
            if (raw) w.Bytes(chunk); else w.Bytes(comp);
        }
        w.Bytes(Magic);
        w.U8((byte)(MethodRaw | level));
        w.I32LE(0); w.I32LE(0); w.I32LE(0);
        return w.ToArray();
    }
}

/// <summary>Snappy raw-block decompression (LevelDB compression type 1).</summary>
public static class Snappy
{
    public static byte[] Decompress(ReadOnlySpan<byte> src)
    {
        int p = 0;
        uint len = 0;
        for (int shift = 0; ; shift += 7)
        {
            if (p >= src.Length || shift > 28) throw new SaveFormatException("Snappy: bad length.");
            byte b = src[p++];
            len |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
        }
        var dst = new byte[len];
        int d = 0;
        while (p < src.Length)
        {
            byte tag = src[p++];
            int type = tag & 3;
            if (type == 0)
            {
                int n = tag >> 2;
                if (n >= 60) { int bytes = n - 59; n = 0; for (int i = 0; i < bytes; i++) n |= src[p++] << (8 * i); }
                n++;
                if (p + n > src.Length || d + n > dst.Length) throw new SaveFormatException("Snappy: literal out of bounds.");
                src.Slice(p, n).CopyTo(dst.AsSpan(d));
                p += n; d += n;
                continue;
            }
            int length, offset;
            if (type == 1) { length = ((tag >> 2) & 7) + 4; offset = ((tag >> 5) << 8) | src[p++]; }
            else if (type == 2) { length = (tag >> 2) + 1; offset = src[p] | (src[p + 1] << 8); p += 2; }
            else { length = (tag >> 2) + 1; offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(src[p..]); p += 4; }
            if (offset <= 0 || offset > d || d + length > dst.Length) throw new SaveFormatException("Snappy: bad copy.");
            for (int i = 0; i < length; i++, d++) dst[d] = dst[d - offset];
        }
        if (d != dst.Length) throw new SaveFormatException("Snappy: size mismatch.");
        return dst;
    }
}
