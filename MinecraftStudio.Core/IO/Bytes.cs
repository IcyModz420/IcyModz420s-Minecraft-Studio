// MinecraftStudio.Core/IO/Bytes.cs: Thrown when a file or record does not have the expected layout.
// Owns: SaveFormatException, ByteReader, ByteWriter, BinaryUtil.
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace MinecraftStudio.Core.IO;

/// <summary>Thrown when a file or record does not have the expected layout.</summary>
public class SaveFormatException : Exception
{
    public SaveFormatException(string message) : base(message) { }
    public SaveFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Bounds-checked cursor over a byte array, with big- and little-endian reads.</summary>
public sealed class ByteReader
{
    readonly byte[] _buf;
    readonly int _start;
    readonly int _end;
    int _pos;

    public ByteReader(byte[] buffer) : this(buffer, 0, buffer.Length) { }

    public ByteReader(byte[] buffer, int offset, int count)
    {
        if ((uint)offset > (uint)buffer.Length || (uint)count > (uint)(buffer.Length - offset))
            throw new ArgumentOutOfRangeException(nameof(count));
        _buf = buffer;
        _start = offset;
        _pos = offset;
        _end = offset + count;
    }

    public byte[] Buffer => _buf;
    /// <summary>Position relative to the start of the readable window.</summary>
    public int Position { get => _pos - _start; set { if (value < 0 || _start + value > _end) throw new ArgumentOutOfRangeException(nameof(value)); _pos = _start + value; } }
    /// <summary>Absolute index into <see cref="Buffer"/>.</summary>
    public int AbsolutePosition => _pos;
    public int Length => _end - _start;
    public int Remaining => _end - _pos;
    public bool AtEnd => _pos >= _end;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int Take(int n)
    {
        if (n < 0 || n > _end - _pos)
            throw new SaveFormatException($"Unexpected end of data (needed {n} bytes at offset {_pos - _start}, {_end - _pos} left).");
        int p = _pos;
        _pos += n;
        return p;
    }

    public void Skip(int n) => Take(n);
    public byte U8() => _buf[Take(1)];
    public sbyte S8() => (sbyte)_buf[Take(1)];
    public ReadOnlySpan<byte> Span(int n) => new(_buf, Take(n), n);
    public byte[] Bytes(int n) => Span(n).ToArray();
    public ReadOnlySpan<byte> PeekSpan(int n) => n <= _end - _pos ? new(_buf, _pos, n) : throw new SaveFormatException("Unexpected end of data.");

    public short I16BE() => BinaryPrimitives.ReadInt16BigEndian(Span(2));
    public short I16LE() => BinaryPrimitives.ReadInt16LittleEndian(Span(2));
    public ushort U16BE() => BinaryPrimitives.ReadUInt16BigEndian(Span(2));
    public ushort U16LE() => BinaryPrimitives.ReadUInt16LittleEndian(Span(2));
    public int I32BE() => BinaryPrimitives.ReadInt32BigEndian(Span(4));
    public int I32LE() => BinaryPrimitives.ReadInt32LittleEndian(Span(4));
    public uint U32BE() => BinaryPrimitives.ReadUInt32BigEndian(Span(4));
    public uint U32LE() => BinaryPrimitives.ReadUInt32LittleEndian(Span(4));
    public long I64BE() => BinaryPrimitives.ReadInt64BigEndian(Span(8));
    public long I64LE() => BinaryPrimitives.ReadInt64LittleEndian(Span(8));
    public ulong U64LE() => BinaryPrimitives.ReadUInt64LittleEndian(Span(8));
    public float F32BE() => BinaryPrimitives.ReadSingleBigEndian(Span(4));
    public float F32LE() => BinaryPrimitives.ReadSingleLittleEndian(Span(4));
    public double F64BE() => BinaryPrimitives.ReadDoubleBigEndian(Span(8));
    public double F64LE() => BinaryPrimitives.ReadDoubleLittleEndian(Span(8));

    public short I16(bool le) => le ? I16LE() : I16BE();
    public ushort U16(bool le) => le ? U16LE() : U16BE();
    public int I32(bool le) => le ? I32LE() : I32BE();
    public uint U32(bool le) => le ? U32LE() : U32BE();
    public long I64(bool le) => le ? I64LE() : I64BE();
    public float F32(bool le) => le ? F32LE() : F32BE();
    public double F64(bool le) => le ? F64LE() : F64BE();

    public uint VarU32()
    {
        uint result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = U8();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new SaveFormatException("VarInt is too long.");
    }

    public ulong VarU64()
    {
        ulong result = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            byte b = U8();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new SaveFormatException("VarLong is too long.");
    }

    public int VarS32() { uint v = VarU32(); return (int)(v >> 1) ^ -(int)(v & 1); }
    public long VarS64() { ulong v = VarU64(); return (long)(v >> 1) ^ -(long)(v & 1); }

    /// <summary>Reads a NUL-terminated ASCII/Latin-1 string of at most <paramref name="max"/> bytes.</summary>
    public string CString(int max = int.MaxValue)
    {
        int start = _pos;
        int limit = Math.Min(_end, max == int.MaxValue ? _end : _pos + max);
        int i = start;
        while (i < limit && _buf[i] != 0) i++;
        string s = Encoding.Latin1.GetString(_buf, start, i - start);
        _pos = i < _end ? i + 1 : i;
        return s;
    }
}

/// <summary>Growable byte buffer with big- and little-endian writes.</summary>
public sealed class ByteWriter
{
    byte[] _buf;
    int _len;

    public ByteWriter(int capacity = 256) => _buf = new byte[Math.Max(16, capacity)];

    public int Length => _len;
    public void Clear() => _len = 0;
    public ReadOnlySpan<byte> WrittenSpan => new(_buf, 0, _len);
    public byte[] RawBuffer => _buf;
    public byte[] ToArray() => WrittenSpan.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Span<byte> Grow(int n)
    {
        if (_len + n > _buf.Length)
        {
            long want = Math.Max((long)_buf.Length * 2, (long)_len + n);
            if (want > Array.MaxLength) want = Math.Max((long)_len + n, Array.MaxLength);
            Array.Resize(ref _buf, (int)want);
        }
        var s = new Span<byte>(_buf, _len, n);
        _len += n;
        return s;
    }

    public Span<byte> Reserve(int n) => Grow(n);
    public void U8(byte b) { if (_len == _buf.Length) Grow(1)[0] = b; else _buf[_len++] = b; }
    public void Bytes(ReadOnlySpan<byte> data) => data.CopyTo(Grow(data.Length));
    public void Zeros(int n) => Grow(n).Clear();
    public void Align(int alignment) { int pad = (alignment - _len % alignment) % alignment; if (pad > 0) Zeros(pad); }

    public void I16BE(short v) => BinaryPrimitives.WriteInt16BigEndian(Grow(2), v);
    public void I16LE(short v) => BinaryPrimitives.WriteInt16LittleEndian(Grow(2), v);
    public void U16BE(ushort v) => BinaryPrimitives.WriteUInt16BigEndian(Grow(2), v);
    public void U16LE(ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(Grow(2), v);
    public void I32BE(int v) => BinaryPrimitives.WriteInt32BigEndian(Grow(4), v);
    public void I32LE(int v) => BinaryPrimitives.WriteInt32LittleEndian(Grow(4), v);
    public void U32BE(uint v) => BinaryPrimitives.WriteUInt32BigEndian(Grow(4), v);
    public void U32LE(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Grow(4), v);
    public void I64BE(long v) => BinaryPrimitives.WriteInt64BigEndian(Grow(8), v);
    public void I64LE(long v) => BinaryPrimitives.WriteInt64LittleEndian(Grow(8), v);
    public void U64LE(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Grow(8), v);
    public void F32BE(float v) => BinaryPrimitives.WriteSingleBigEndian(Grow(4), v);
    public void F32LE(float v) => BinaryPrimitives.WriteSingleLittleEndian(Grow(4), v);
    public void F64BE(double v) => BinaryPrimitives.WriteDoubleBigEndian(Grow(8), v);
    public void F64LE(double v) => BinaryPrimitives.WriteDoubleLittleEndian(Grow(8), v);

    public void I16(short v, bool le) { if (le) I16LE(v); else I16BE(v); }
    public void U16(ushort v, bool le) { if (le) U16LE(v); else U16BE(v); }
    public void I32(int v, bool le) { if (le) I32LE(v); else I32BE(v); }
    public void U32(uint v, bool le) { if (le) U32LE(v); else U32BE(v); }
    public void I64(long v, bool le) { if (le) I64LE(v); else I64BE(v); }
    public void F32(float v, bool le) { if (le) F32LE(v); else F32BE(v); }
    public void F64(double v, bool le) { if (le) F64LE(v); else F64BE(v); }

    public void VarU32(uint v)
    {
        while (v >= 0x80) { U8((byte)(v | 0x80)); v >>= 7; }
        U8((byte)v);
    }

    public void VarU64(ulong v)
    {
        while (v >= 0x80) { U8((byte)(v | 0x80)); v >>= 7; }
        U8((byte)v);
    }

    public void VarS32(int v) => VarU32((uint)((v << 1) ^ (v >> 31)));
    public void VarS64(long v) => VarU64((ulong)((v << 1) ^ (v >> 63)));

    public void PatchI32BE(int at, int v) => BinaryPrimitives.WriteInt32BigEndian(_buf.AsSpan(at, 4), v);
    public void PatchI32LE(int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(_buf.AsSpan(at, 4), v);
    public void PatchU32LE(int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(at, 4), v);
    public void PatchU16BE(int at, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(_buf.AsSpan(at, 2), v);
    public void PatchU16LE(int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(at, 2), v);
}

/// <summary>Small helpers shared by the format readers.</summary>
public static class BinaryUtil
{
    public static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) => data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    public static string Hex(ReadOnlySpan<byte> data, int max = 64)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length && i < max; i++) { if (i > 0) sb.Append(' '); sb.Append(data[i].ToString("X2")); }
        if (data.Length > max) sb.Append(" …");
        return sb.ToString();
    }

    public static int CeilDiv(int a, int b) => (a + b - 1) / b;

    /// <summary>Number of bits needed to store values 0..count-1 (0 for count &lt;= 1).</summary>
    public static int BitsFor(int count)
    {
        int bits = 0;
        while ((1 << bits) < count) bits++;
        return bits;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.##} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
