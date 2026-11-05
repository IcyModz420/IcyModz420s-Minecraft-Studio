// MinecraftStudio.Core/LevelDb/LevelDbFormat.cs: Block compression ids (upstream LevelDB plus Mojang's
// additions).
// Owns: BlockCompression, ValueType, BlockHandle, Varint, InternalKey, ByteArrayComparer, FileNames.
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MinecraftStudio.Core.LevelDb;

/// <summary>Block compression ids (upstream LevelDB plus Mojang's additions).</summary>
public enum BlockCompression : byte
{
    None = 0,
    Snappy = 1,
    /// <summary>Mojang: zlib with header.</summary>
    Zlib = 2,
    /// <summary>Mojang: raw deflate (what Bedrock writes).</summary>
    ZlibRaw = 4,
}

public enum ValueType : byte { Deletion = 0, Value = 1 }

/// <summary>Location of a block inside a table file.</summary>
public readonly record struct BlockHandle(ulong Offset, ulong Size)
{
    public const int MaxEncodedLength = 20;

    public static BlockHandle Decode(ReadOnlySpan<byte> s, ref int p) => new(Varint.ReadU64(s, ref p), Varint.ReadU64(s, ref p));

    public void Encode(List<byte> output)
    {
        Varint.WriteU64(output, Offset);
        Varint.WriteU64(output, Size);
    }
}

public static class Varint
{
    public static uint ReadU32(ReadOnlySpan<byte> s, ref int p)
    {
        uint r = 0;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            if (p >= s.Length) throw new IO.SaveFormatException("LevelDB: truncated varint.");
            byte b = s[p++];
            r |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return r;
        }
        throw new IO.SaveFormatException("LevelDB: bad varint32.");
    }

    public static ulong ReadU64(ReadOnlySpan<byte> s, ref int p)
    {
        ulong r = 0;
        for (int shift = 0; shift <= 63; shift += 7)
        {
            if (p >= s.Length) throw new IO.SaveFormatException("LevelDB: truncated varint.");
            byte b = s[p++];
            r |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return r;
        }
        throw new IO.SaveFormatException("LevelDB: bad varint64.");
    }

    public static void WriteU32(List<byte> o, uint v) { while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; } o.Add((byte)v); }
    public static void WriteU64(List<byte> o, ulong v) { while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; } o.Add((byte)v); }

    public static ReadOnlySpan<byte> ReadSlice(ReadOnlySpan<byte> s, ref int p)
    {
        int len = (int)ReadU32(s, ref p);
        if (len < 0 || p + len > s.Length) throw new IO.SaveFormatException("LevelDB: slice runs past the end.");
        var r = s.Slice(p, len);
        p += len;
        return r;
    }

    public static void WriteSlice(List<byte> o, ReadOnlySpan<byte> data)
    {
        WriteU32(o, (uint)data.Length);
        foreach (byte b in data) o.Add(b);
    }
}

/// <summary>Internal keys: user key + 8-byte little-endian (sequence &lt;&lt; 8 | type).</summary>
public static class InternalKey
{
    public static byte[] Make(ReadOnlySpan<byte> userKey, ulong sequence, ValueType type)
    {
        var k = new byte[userKey.Length + 8];
        userKey.CopyTo(k);
        BinaryPrimitives.WriteUInt64LittleEndian(k.AsSpan(userKey.Length), (sequence << 8) | (byte)type);
        return k;
    }

    public static ReadOnlySpan<byte> UserKey(ReadOnlySpan<byte> internalKey) => internalKey[..^8];
    public static ulong Trailer(ReadOnlySpan<byte> internalKey) => BinaryPrimitives.ReadUInt64LittleEndian(internalKey[^8..]);
    public static ulong Sequence(ReadOnlySpan<byte> internalKey) => Trailer(internalKey) >> 8;
    public static ValueType Type(ReadOnlySpan<byte> internalKey) => (ValueType)(Trailer(internalKey) & 0xFF);

    /// <summary>Internal key ordering: user key ascending, then sequence descending.</summary>
    public static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int c = UserKey(a).SequenceCompareTo(UserKey(b));
        if (c != 0) return c;
        ulong ta = Trailer(a), tb = Trailer(b);
        return ta > tb ? -1 : ta < tb ? 1 : 0;
    }
}

/// <summary>Byte array equality/ordering for dictionaries and sorting.</summary>
public sealed class ByteArrayComparer : IEqualityComparer<byte[]>, IComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance = new();
    public bool Equals(byte[]? x, byte[]? y) => x == null ? y == null : y != null && x.AsSpan().SequenceEqual(y);
    public int GetHashCode(byte[] obj)
    {
        var h = new HashCode();
        h.AddBytes(obj);
        return h.ToHashCode();
    }
    public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
}

public static class FileNames
{
    public static string Table(string dir, ulong number) => Path.Combine(dir, $"{number:D6}.ldb");
    public static string LegacyTable(string dir, ulong number) => Path.Combine(dir, $"{number:D6}.sst");
    public static string Log(string dir, ulong number) => Path.Combine(dir, $"{number:D6}.log");
    public static string Manifest(string dir, ulong number) => Path.Combine(dir, $"MANIFEST-{number:D6}");

    public static bool TryParse(string fileName, out ulong number, out string kind)
    {
        number = 0;
        kind = "";
        if (fileName.StartsWith("MANIFEST-", StringComparison.Ordinal))
        {
            kind = "manifest";
            return ulong.TryParse(fileName.AsSpan(9), out number);
        }
        int dot = fileName.IndexOf('.');
        if (dot <= 0 || !ulong.TryParse(fileName.AsSpan(0, dot), out number)) return false;
        kind = fileName[(dot + 1)..] switch { "ldb" or "sst" => "table", "log" => "log", _ => "" };
        return kind.Length > 0;
    }
}
