// MinecraftStudio.Core/LevelDb/LogFile.cs: LevelDB log/MANIFEST record framing (32 KiB blocks, CRC32C per
// fragment).
// Owns: LogFormat, LogReader, LogWriter, WriteBatch.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LevelDb;

/// <summary>LevelDB log/MANIFEST record framing (32 KiB blocks, CRC32C per fragment).</summary>
public static class LogFormat
{
    public const int BlockSize = 32768;
    public const int HeaderSize = 7;
    public const byte Zero = 0, Full = 1, First = 2, Middle = 3, Last = 4;
}

public sealed class LogReader
{
    /// <summary>Number of fragments skipped because of bad checksums or framing.</summary>
    public int CorruptFragments { get; private set; }
    public long BytesDropped { get; private set; }

    public IEnumerable<byte[]> ReadRecords(byte[] data)
    {
        List<byte>? scratch = null;
        int pos = 0;
        while (pos < data.Length)
        {
            int blockRemain = LogFormat.BlockSize - pos % LogFormat.BlockSize;
            if (blockRemain < LogFormat.HeaderSize) { pos += blockRemain; continue; }
            if (pos + LogFormat.HeaderSize > data.Length) break;
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 4));
            byte type = data[pos + 6];
            if (type == LogFormat.Zero && length == 0)
            {
                // Preallocated / zero-filled space: nothing more in this block.
                pos += blockRemain;
                continue;
            }
            if (LogFormat.HeaderSize + length > blockRemain || pos + LogFormat.HeaderSize + length > data.Length)
            {
                Corrupt(blockRemain);
                pos += blockRemain;
                scratch = null;
                continue;
            }
            var payload = data.AsSpan(pos + LogFormat.HeaderSize, length);
            uint actual = Crc32C.Append(Crc32C.Compute([type]), payload);
            if (Crc32C.Unmask(crc) != actual)
            {
                Corrupt(blockRemain);
                pos += blockRemain;
                scratch = null;
                continue;
            }
            pos += LogFormat.HeaderSize + length;
            switch (type)
            {
                case LogFormat.Full:
                    if (scratch != null) Corrupt(scratch.Count);
                    scratch = null;
                    yield return payload.ToArray();
                    break;
                case LogFormat.First:
                    if (scratch != null) Corrupt(scratch.Count);
                    scratch = [.. payload];
                    break;
                case LogFormat.Middle:
                    if (scratch == null) Corrupt(length); else scratch.AddRange(payload.ToArray());
                    break;
                case LogFormat.Last:
                    if (scratch == null) { Corrupt(length); break; }
                    scratch.AddRange(payload.ToArray());
                    yield return scratch.ToArray();
                    scratch = null;
                    break;
                default:
                    Corrupt(length);
                    scratch = null;
                    break;
            }
        }
    }

    void Corrupt(long bytes) { CorruptFragments++; BytesDropped += bytes; }
}

public sealed class LogWriter : IDisposable
{
    readonly Stream _s;
    int _blockOffset;

    /// <summary>Writes to the end of <paramref name="stream"/>, continuing its current block.</summary>
    public LogWriter(Stream stream)
    {
        _s = stream;
        _s.Seek(0, SeekOrigin.End);
        _blockOffset = (int)(_s.Length % LogFormat.BlockSize);
    }

    public void AddRecord(ReadOnlySpan<byte> payload)
    {
        bool begin = true;
        int left = payload.Length;
        int p = 0;
        Span<byte> header = stackalloc byte[LogFormat.HeaderSize];
        do
        {
            int leftover = LogFormat.BlockSize - _blockOffset;
            if (leftover < LogFormat.HeaderSize)
            {
                if (leftover > 0) _s.Write(new byte[leftover]);
                _blockOffset = 0;
            }
            int avail = LogFormat.BlockSize - _blockOffset - LogFormat.HeaderSize;
            int frag = Math.Min(left, avail);
            bool end = left == frag;
            byte type = begin && end ? LogFormat.Full : begin ? LogFormat.First : end ? LogFormat.Last : LogFormat.Middle;
            var data = payload.Slice(p, frag);
            uint crc = Crc32C.Mask(Crc32C.Append(Crc32C.Compute([type]), data));
            BinaryPrimitives.WriteUInt32LittleEndian(header, crc);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)frag);
            header[6] = type;
            _s.Write(header);
            _s.Write(data);
            _blockOffset += LogFormat.HeaderSize + frag;
            p += frag;
            left -= frag;
            begin = false;
        } while (left > 0);
    }

    public void Flush() => _s.Flush();
    public void Dispose() => _s.Dispose();
}

/// <summary>A LevelDB write batch: sequence, count, then put/delete records.</summary>
public sealed class WriteBatch
{
    public ulong Sequence { get; set; }
    public List<(byte[] Key, byte[]? Value)> Ops { get; } = [];

    public void Put(byte[] key, byte[] value) => Ops.Add((key, value));
    public void Delete(byte[] key) => Ops.Add((key, null));

    public static WriteBatch Parse(byte[] rec)
    {
        if (rec.Length < 12) throw new SaveFormatException("LevelDB: write batch is too short.");
        var b = new WriteBatch { Sequence = BinaryPrimitives.ReadUInt64LittleEndian(rec) };
        int count = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(8));
        int p = 12;
        for (int i = 0; i < count; i++)
        {
            byte tag = rec[p++];
            var key = Varint.ReadSlice(rec, ref p).ToArray();
            if (tag == (byte)ValueType.Value) b.Ops.Add((key, Varint.ReadSlice(rec, ref p).ToArray()));
            else if (tag == (byte)ValueType.Deletion) b.Ops.Add((key, null));
            else throw new SaveFormatException($"LevelDB: unknown write batch tag {tag}.");
        }
        return b;
    }

    public byte[] Encode()
    {
        var o = new List<byte>(64);
        Span<byte> head = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(head, Sequence);
        BinaryPrimitives.WriteInt32LittleEndian(head[8..], Ops.Count);
        o.AddRange(head.ToArray());
        foreach (var (key, value) in Ops)
        {
            o.Add(value != null ? (byte)ValueType.Value : (byte)ValueType.Deletion);
            Varint.WriteSlice(o, key);
            if (value != null) Varint.WriteSlice(o, value);
        }
        return o.ToArray();
    }
}
