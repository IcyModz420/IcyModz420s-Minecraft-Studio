// MinecraftStudio.Core/LegacyConsole/ConsoleRegion.cs: A console region file (r.X.Z.mcr inside the archive, or
// a split GAMEDATA_000… file).
// Owns: ConsoleRegion.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

/// <summary>A console region file (r.X.Z.mcr inside the archive, or a split GAMEDATA_000… file).</summary>
public sealed class ConsoleRegion
{
    public sealed class Chunk
    {
        public uint Flags { get; set; }          // bits 30-31 of the header word
        public byte[] Payload { get; set; } = []; // compressed bytes as stored
        public int DecompressedSize { get; set; }
        public int RleSize { get; set; }          // PS3 only
        public uint Timestamp { get; set; }
        public bool RleEncoded => (Flags & 0x80000000u) != 0;
        public bool NewSave => (Flags & 0x40000000u) != 0;
    }

    readonly Chunk?[] _chunks = new Chunk?[1024];
    public ConsolePlatform Platform { get; }
    public bool LittleEndian => Platform.IsLittleEndian();
    public bool Modified { get; private set; }

    ConsoleRegion(ConsolePlatform platform) { Platform = platform; }

    public Chunk? this[int index] => _chunks[index];
    public IEnumerable<int> Indexes() { for (int i = 0; i < 1024; i++) if (_chunks[i] != null) yield return i; }

    public static ConsoleRegion Parse(byte[] d, ConsolePlatform platform)
    {
        var r = new ConsoleRegion(platform);
        if (d.Length < 8192) return r;
        bool le = r.LittleEndian;
        int headerLen = platform == ConsolePlatform.PS3 ? 12 : 8;
        for (int i = 0; i < 1024; i++)
        {
            uint loc = le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(i * 4)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(i * 4));
            if (loc == 0) continue;
            long off = (long)(loc >> 8) * 4096;
            if (off < 8192 || off + headerLen > d.Length) continue;
            uint w0 = le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)off)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan((int)off));
            int dsize = le ? BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan((int)off + 4)) : BinaryPrimitives.ReadInt32BigEndian(d.AsSpan((int)off + 4));
            int rle = 0;
            if (platform == ConsolePlatform.PS3) rle = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan((int)off + 8));
            int clen = (int)(w0 & 0x3FFFFFFF);
            if (off + headerLen + clen > d.Length) clen = (int)(d.Length - off - headerLen);
            uint ts = le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4096 + i * 4)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4096 + i * 4));
            r._chunks[i] = new Chunk
            {
                Flags = w0 & 0xC0000000u,
                DecompressedSize = dsize,
                RleSize = rle,
                Payload = d.AsSpan((int)off + headerLen, clen).ToArray(),
                Timestamp = ts,
            };
        }
        return r;
    }

    /// <summary>Decodes a chunk: platform codec, then chunk RLE.</summary>
    public byte[] Decode(int index)
    {
        var c = _chunks[index] ?? throw new InvalidOperationException("No chunk at that index.");
        bool lzx = Platform == ConsolePlatform.Xbox360 || (Platform == ConsolePlatform.Raw && !Compression.IsZlibHeader(c.Payload));
        byte[] raw = lzx
            ? XMemLzx.Decompress(c.Payload, 0, c.RleEncoded ? 0 : c.DecompressedSize, out _)
            : Platform == ConsolePlatform.PS3
                ? Compression.Inflate(c.Payload, c.RleSize > 0 ? c.RleSize : c.DecompressedSize)
                : Compression.ZlibDecompress(c.Payload, c.DecompressedSize);
        var data = c.RleEncoded ? ConsoleRle.DecodeChunk(raw, c.DecompressedSize) : raw;
        if (data.Length != c.DecompressedSize) throw new SaveFormatException($"Chunk decoded to {data.Length} bytes, expected {c.DecompressedSize}.");
        return data;
    }

    public void Set(int index, byte[] data, uint flags, uint? timestamp = null)
    {
        byte[] body = (flags & 0x80000000u) != 0 ? ConsoleRle.EncodeChunk(data) : data;
        byte[] payload = Platform switch
        {
            ConsolePlatform.Xbox360 or ConsolePlatform.Raw => XMemLzx.Compress(body),
            ConsolePlatform.PS3 => Compression.Deflate(body),
            _ => Compression.ZlibCompress(body),
        };
        _chunks[index] = new Chunk
        {
            Flags = flags,
            Payload = payload,
            DecompressedSize = data.Length,
            RleSize = Platform == ConsolePlatform.PS3 ? body.Length : 0,
            Timestamp = timestamp ?? _chunks[index]?.Timestamp ?? 0,
        };
        Modified = true;
    }

    public void Delete(int index)
    {
        if (_chunks[index] == null) return;
        _chunks[index] = null;
        Modified = true;
    }

    /// <summary>Puts a chunk taken out with <see cref="Delete"/> back (undo before saving).</summary>
    public void Restore(int index, Chunk chunk)
    {
        _chunks[index] = chunk;
        Modified = true;
    }

    public bool IsEmpty => _chunks.All(c => c == null);
    public void MarkSaved() => Modified = false;

    public byte[] ToBytes()
    {
        bool le = LittleEndian;
        int headerLen = Platform == ConsolePlatform.PS3 ? 12 : 8;
        var w = new ByteWriter(8192 + _chunks.Sum(c => c == null ? 0 : c.Payload.Length + 4096));
        w.Zeros(8192);
        for (int i = 0; i < 1024; i++)
        {
            var c = _chunks[i];
            if (c == null) continue;
            int start = w.Length / 4096;
            w.U32(c.Flags | (uint)c.Payload.Length, le);
            w.I32(c.DecompressedSize, le);
            if (Platform == ConsolePlatform.PS3) w.I32BE(c.RleSize);
            w.Bytes(c.Payload);
            w.Align(4096);
            int sectors = (headerLen + c.Payload.Length + 4095) / 4096;
            uint loc = (uint)((start << 8) | Math.Min(255, sectors));
            if (le) { w.PatchU32LE(i * 4, loc); w.PatchU32LE(4096 + i * 4, c.Timestamp); }
            else { w.PatchI32BE(i * 4, (int)loc); w.PatchI32BE(4096 + i * 4, (int)c.Timestamp); }
        }
        return w.ToArray();
    }
}
