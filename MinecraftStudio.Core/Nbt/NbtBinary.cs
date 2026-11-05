// MinecraftStudio.Core/Nbt/NbtBinary.cs: Binary NBT dialect.
// Owns: NbtFormat, NamedTag, NbtBinary.
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Nbt;

/// <summary>Binary NBT dialect.</summary>
public sealed record NbtFormat(string Name, bool LittleEndian, bool VarInt, bool ModifiedUtf8)
{
    /// <summary>Java Edition and Legacy Console Edition: big-endian, modified UTF-8.</summary>
    public static readonly NbtFormat Java = new("Java (big-endian)", false, false, true);
    /// <summary>Bedrock / Pocket Edition files: little-endian, UTF-8.</summary>
    public static readonly NbtFormat Bedrock = new("Bedrock (little-endian)", true, false, false);
    /// <summary>Bedrock network NBT: zig-zag varints for ints, longs and lengths.</summary>
    public static readonly NbtFormat BedrockNetwork = new("Bedrock network (varint)", true, true, false);
    /// <summary>Little-endian with modified UTF-8 strings (some console ports).</summary>
    public static readonly NbtFormat LittleEndianModifiedUtf8 = new("Little-endian (modified UTF-8)", true, false, true);

    public static IReadOnlyList<NbtFormat> All { get; } = [Java, Bedrock, BedrockNetwork, LittleEndianModifiedUtf8];
    public override string ToString() => Name;
}

public sealed class NamedTag
{
    public NamedTag(string name, NbtTag tag) { Name = name; Tag = tag; }
    public string Name { get; set; }
    public NbtTag Tag { get; set; }
}

public static class NbtBinary
{
    public const int MaxDepth = 512;

    /// <summary>Reads one named root tag. Returns null when the next tag is TAG_End.</summary>
    public static NamedTag? ReadNamed(ByteReader r, NbtFormat format)
    {
        var type = (NbtType)r.U8();
        if (type == NbtType.End) return null;
        var d = new Decoder(r, format);
        string name = d.ReadString();
        return new NamedTag(name, d.ReadPayload(type, 0));
    }

    public static NbtTag ReadPayload(ByteReader r, NbtType type, NbtFormat format) => new Decoder(r, format).ReadPayload(type, 0);

    /// <summary>Parses a single root compound from a buffer.</summary>
    public static NbtCompound ReadCompound(byte[] data, NbtFormat format, out string name, int offset = 0, int count = -1)
    {
        var r = new ByteReader(data, offset, count < 0 ? data.Length - offset : count);
        var t = ReadNamed(r, format) ?? throw new SaveFormatException("Empty NBT data.");
        name = t.Name;
        return t.Tag as NbtCompound ?? throw new SaveFormatException($"Root tag is {NbtTypeInfo.Name(t.Tag.Type)}, expected Compound.");
    }

    public static NbtCompound ReadCompound(byte[] data, NbtFormat format) => ReadCompound(data, format, out _);

    /// <summary>Reads consecutive root tags until the data runs out (Bedrock stores lists of entities this way).</summary>
    public static List<NamedTag> ReadAll(byte[] data, NbtFormat format, int offset = 0, int count = -1)
    {
        var r = new ByteReader(data, offset, count < 0 ? data.Length - offset : count);
        var list = new List<NamedTag>();
        while (!r.AtEnd)
        {
            var t = ReadNamed(r, format);
            if (t == null) continue;
            list.Add(t);
        }
        return list;
    }

    public static void WriteNamed(ByteWriter w, string name, NbtTag tag, NbtFormat format)
    {
        var e = new Encoder(w, format);
        w.U8((byte)tag.Type);
        e.WriteString(name);
        e.WritePayload(tag, 0);
    }

    public static void WritePayload(ByteWriter w, NbtTag tag, NbtFormat format) => new Encoder(w, format).WritePayload(tag, 0);

    public static byte[] Serialize(NbtTag tag, NbtFormat format, string name = "")
    {
        var w = new ByteWriter(1024);
        WriteNamed(w, name, tag, format);
        return w.ToArray();
    }

    public static byte[] SerializeAll(IEnumerable<NamedTag> tags, NbtFormat format)
    {
        var w = new ByteWriter(1024);
        foreach (var t in tags) WriteNamed(w, t.Name, t.Tag, format);
        return w.ToArray();
    }

    sealed class Decoder(ByteReader r, NbtFormat f)
    {
        readonly bool _le = f.LittleEndian;
        readonly bool _var = f.VarInt;

        public string ReadString()
        {
            int len = _var ? checked((int)r.VarU32()) : r.U16(_le);
            var span = r.Span(len);
            return f.ModifiedUtf8 ? NbtText.DecodeModifiedUtf8(span) : NbtText.DecodeUtf8(span);
        }

        int ReadLength()
        {
            int n = _var ? r.VarS32() : r.I32(_le);
            return n;
        }

        int ReadArrayLength(int elementSize)
        {
            int n = ReadLength();
            if (n < 0) throw new SaveFormatException($"Negative array length {n}.");
            if (!_var && (long)n * elementSize > r.Remaining) throw new SaveFormatException($"Array length {n} runs past the end of the data.");
            return n;
        }

        public NbtTag ReadPayload(NbtType type, int depth)
        {
            switch (type)
            {
                case NbtType.Byte: return new NbtByte(r.S8());
                case NbtType.Short: return new NbtShort(r.I16(_le));
                case NbtType.Int: return new NbtInt(_var ? r.VarS32() : r.I32(_le));
                case NbtType.Long: return new NbtLong(_var ? r.VarS64() : r.I64(_le));
                case NbtType.Float: return new NbtFloat(r.F32(_le));
                case NbtType.Double: return new NbtDouble(r.F64(_le));
                case NbtType.ByteArray:
                    {
                        int n = ReadArrayLength(1);
                        return new NbtByteArray(r.Bytes(n));
                    }
                case NbtType.String: return new NbtString(ReadString());
                case NbtType.IntArray:
                    {
                        int n = ReadArrayLength(4);
                        var a = new int[n];
                        if (_var) for (int i = 0; i < n; i++) a[i] = r.VarS32();
                        else for (int i = 0; i < n; i++) a[i] = r.I32(_le);
                        return new NbtIntArray(a);
                    }
                case NbtType.LongArray:
                    {
                        int n = ReadArrayLength(8);
                        var a = new long[n];
                        if (_var) for (int i = 0; i < n; i++) a[i] = r.VarS64();
                        else for (int i = 0; i < n; i++) a[i] = r.I64(_le);
                        return new NbtLongArray(a);
                    }
                case NbtType.List:
                    {
                        if (depth >= MaxDepth) throw new SaveFormatException("NBT is nested too deeply.");
                        var et = (NbtType)r.U8();
                        int n = ReadLength();
                        if (n < 0) n = 0;
                        if (et > NbtType.LongArray) throw new SaveFormatException($"List has unknown element type {(int)et}.");
                        if (et == NbtType.End) return new NbtList(NbtType.End);
                        if (n > r.Remaining) throw new SaveFormatException($"List length {n} runs past the end of the data.");
                        var list = new NbtList(et, n);
                        for (int i = 0; i < n; i++) list.AddUnchecked(ReadPayload(et, depth + 1));
                        return list;
                    }
                case NbtType.Compound:
                    {
                        if (depth >= MaxDepth) throw new SaveFormatException("NBT is nested too deeply.");
                        var c = new NbtCompound();
                        while (true)
                        {
                            var t = (NbtType)r.U8();
                            if (t == NbtType.End) break;
                            if (t > NbtType.LongArray) throw new SaveFormatException($"Unknown tag type {(int)t} at offset {r.Position - 1}.");
                            string name = ReadString();
                            c.AddRaw(name, ReadPayload(t, depth + 1));
                        }
                        return c;
                    }
                default:
                    throw new SaveFormatException($"Unknown tag type {(int)type}.");
            }
        }
    }

    sealed class Encoder(ByteWriter w, NbtFormat f)
    {
        readonly bool _le = f.LittleEndian;
        readonly bool _var = f.VarInt;

        public void WriteString(string s)
        {
            int len = f.ModifiedUtf8 ? NbtText.ModifiedUtf8Length(s) : NbtText.Utf8Length(s);
            if (_var) w.VarU32((uint)len);
            else
            {
                if (len > ushort.MaxValue) throw new SaveFormatException($"String is too long for NBT ({len} bytes).");
                w.U16((ushort)len, _le);
            }
            var dst = w.Reserve(len);
            if (f.ModifiedUtf8) NbtText.EncodeModifiedUtf8(s, dst); else NbtText.EncodeUtf8(s, dst);
        }

        void WriteLength(int n)
        {
            if (_var) w.VarS32(n); else w.I32(n, _le);
        }

        public void WritePayload(NbtTag tag, int depth)
        {
            if (depth > MaxDepth) throw new SaveFormatException("NBT is nested too deeply.");
            switch (tag)
            {
                case NbtByte b: w.U8((byte)b.Value); break;
                case NbtShort s: w.I16(s.Value, _le); break;
                case NbtInt i: if (_var) w.VarS32(i.Value); else w.I32(i.Value, _le); break;
                case NbtLong l: if (_var) w.VarS64(l.Value); else w.I64(l.Value, _le); break;
                case NbtFloat fl: w.F32(fl.Value, _le); break;
                case NbtDouble d: w.F64(d.Value, _le); break;
                case NbtByteArray ba: WriteLength(ba.Value.Length); w.Bytes(ba.Value); break;
                case NbtString str: WriteString(str.Value); break;
                case NbtIntArray ia:
                    WriteLength(ia.Value.Length);
                    foreach (int v in ia.Value) { if (_var) w.VarS32(v); else w.I32(v, _le); }
                    break;
                case NbtLongArray la:
                    WriteLength(la.Value.Length);
                    foreach (long v in la.Value) { if (_var) w.VarS64(v); else w.I64(v, _le); }
                    break;
                case NbtList list:
                    w.U8((byte)(list.Count > 0 ? list[0].Type : list.ElementType));
                    WriteLength(list.Count);
                    foreach (var item in list) WritePayload(item, depth + 1);
                    break;
                case NbtCompound c:
                    foreach (var kv in c)
                    {
                        w.U8((byte)kv.Value.Type);
                        WriteString(kv.Key);
                        WritePayload(kv.Value, depth + 1);
                    }
                    w.U8(0);
                    break;
                default:
                    throw new SaveFormatException($"Cannot write {tag.GetType().Name}.");
            }
        }
    }
}
