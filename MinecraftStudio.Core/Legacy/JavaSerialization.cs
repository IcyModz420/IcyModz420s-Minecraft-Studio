// MinecraftStudio.Core/Legacy/JavaSerialization.cs: Generic reader for java.io.ObjectOutputStream data (Classic
// levels are serialized Java objects). Primitive fields and array payloads keep their byte offsets so same-size
// edits can be patched in place.
// Owns: JavaSerialization.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Legacy;

/// <summary>
/// Generic reader for java.io.ObjectOutputStream data (Classic levels are serialized Java objects).
/// Primitive fields and array payloads keep their byte offsets so same-size edits can be patched in place.
/// </summary>
public static class JavaSerialization
{
    const byte TC_NULL = 0x70, TC_REFERENCE = 0x71, TC_CLASSDESC = 0x72, TC_OBJECT = 0x73, TC_STRING = 0x74, TC_ARRAY = 0x75,
        TC_CLASS = 0x76, TC_BLOCKDATA = 0x77, TC_ENDBLOCKDATA = 0x78, TC_RESET = 0x79, TC_BLOCKDATALONG = 0x7A, TC_EXCEPTION = 0x7B,
        TC_LONGSTRING = 0x7C, TC_PROXYCLASSDESC = 0x7D, TC_ENUM = 0x7E;
    const byte SC_WRITE_METHOD = 0x01, SC_SERIALIZABLE = 0x02, SC_EXTERNALIZABLE = 0x04, SC_BLOCK_DATA = 0x08;
    const int BaseHandle = 0x7E0000;

    public sealed class ClassDesc
    {
        public string Name = "";
        public long Suid;
        public byte Flags;
        public List<FieldDesc> Fields = [];
        public ClassDesc? Super;
        public override string ToString() => Name;
    }

    public sealed class FieldDesc
    {
        public char Type;
        public string Name = "";
        public string? ClassName;
    }

    public sealed class JObject
    {
        public ClassDesc Class = null!;
        /// <summary>Field values by name (primitives boxed; objects as JObject/JArray/string/null).</summary>
        public Dictionary<string, object?> Fields = new(StringComparer.Ordinal);
        /// <summary>Byte offset of each primitive field's value in the stream.</summary>
        public Dictionary<string, int> Offsets = new(StringComparer.Ordinal);
        public List<object?> Annotations = [];
        public object? this[string field] => Fields.GetValueOrDefault(field);
        public override string ToString() => Class.Name;
    }

    public sealed class JArray
    {
        public ClassDesc Class = null!;
        public object? Values;
        /// <summary>Offset of the first element (after the length).</summary>
        public int PayloadOffset;
        public int Length;
    }

    public sealed class JEnum
    {
        public ClassDesc Class = null!;
        public string Constant = "";
    }

    public sealed class BlockData
    {
        public byte[] Data = [];
    }

    public static List<object?> Parse(byte[] data, int offset = 0)
    {
        var p = new Parser(data, offset);
        return p.ParseAll();
    }

    sealed class Parser(byte[] d, int start)
    {
        int _p = start;
        readonly List<object?> _handles = [];

        byte U8() => _p < d.Length ? d[_p++] : throw new SaveFormatException("Serialized stream ended early.");
        short I16() { var v = BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(_p)); _p += 2; return v; }
        ushort U16() { var v = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(_p)); _p += 2; return v; }
        int I32() { var v = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(_p)); _p += 4; return v; }
        long I64() { var v = BinaryPrimitives.ReadInt64BigEndian(d.AsSpan(_p)); _p += 8; return v; }

        string Utf(int len)
        {
            if (_p + len > d.Length) throw new SaveFormatException("Serialized string runs past the end.");
            string s = Nbt.NbtText.DecodeModifiedUtf8(d.AsSpan(_p, len));
            _p += len;
            return s;
        }

        int NewHandle(object? o) { _handles.Add(o); return _handles.Count - 1; }

        public List<object?> ParseAll()
        {
            if (U16() != 0xACED) throw new SaveFormatException("Not a Java serialization stream.");
            if (U16() != 5) throw new SaveFormatException("Unsupported Java serialization version.");
            var list = new List<object?>();
            while (_p < d.Length) list.Add(ReadContent());
            return list;
        }

        object? ReadContent()
        {
            byte tc = d[_p];
            if (tc == TC_BLOCKDATA || tc == TC_BLOCKDATALONG) return ReadBlockData();
            return ReadObject();
        }

        BlockData ReadBlockData()
        {
            byte tc = U8();
            int len = tc == TC_BLOCKDATA ? U8() : I32();
            var b = new BlockData { Data = d.AsSpan(_p, len).ToArray() };
            _p += len;
            return b;
        }

        object? ReadObject()
        {
            byte tc = U8();
            switch (tc)
            {
                case TC_NULL: return null;
                case TC_REFERENCE:
                    {
                        int h = I32() - BaseHandle;
                        if (h < 0 || h >= _handles.Count) throw new SaveFormatException($"Bad back-reference {h}.");
                        return _handles[h];
                    }
                case TC_STRING: { int h = NewHandle(null); string s = Utf(U16()); _handles[h] = s; return s; }
                case TC_LONGSTRING: { int h = NewHandle(null); string s = Utf((int)I64()); _handles[h] = s; return s; }
                case TC_CLASSDESC: _p--; return ReadClassDesc();
                case TC_PROXYCLASSDESC: _p--; return ReadClassDesc();
                case TC_CLASS: { var cd = ReadClassDesc(); NewHandle(cd); return cd; }
                case TC_ARRAY: return ReadArray();
                case TC_ENUM:
                    {
                        var cd = ReadClassDesc()!;
                        var e = new JEnum { Class = cd };
                        NewHandle(e);
                        e.Constant = ReadObject() as string ?? "";
                        return e;
                    }
                case TC_OBJECT: return ReadNewObject();
                case TC_RESET: _handles.Clear(); return ReadObject();
                case TC_EXCEPTION: throw new SaveFormatException("The stream contains a serialized exception.");
                default: throw new SaveFormatException($"Unknown serialization tag 0x{tc:X2} at {_p - 1}.");
            }
        }

        ClassDesc? ReadClassDesc()
        {
            byte tc = U8();
            switch (tc)
            {
                case TC_NULL: return null;
                case TC_REFERENCE:
                    {
                        int h = I32() - BaseHandle;
                        return h >= 0 && h < _handles.Count ? _handles[h] as ClassDesc : throw new SaveFormatException("Bad class reference.");
                    }
                case TC_CLASSDESC:
                    {
                        var cd = new ClassDesc { Name = Utf(U16()), Suid = I64() };
                        NewHandle(cd);
                        cd.Flags = U8();
                        int n = I16();
                        for (int i = 0; i < n; i++)
                        {
                            var f = new FieldDesc { Type = (char)U8(), Name = Utf(U16()) };
                            if (f.Type is '[' or 'L') f.ClassName = ReadObject() as string;
                            cd.Fields.Add(f);
                        }
                        SkipAnnotation();
                        cd.Super = ReadClassDesc();
                        return cd;
                    }
                case TC_PROXYCLASSDESC:
                    {
                        var cd = new ClassDesc { Name = "<proxy>" };
                        NewHandle(cd);
                        int n = I32();
                        for (int i = 0; i < n; i++) Utf(U16());
                        SkipAnnotation();
                        cd.Super = ReadClassDesc();
                        return cd;
                    }
                default: throw new SaveFormatException($"Expected a class descriptor, found 0x{tc:X2}.");
            }
        }

        void SkipAnnotation()
        {
            while (true)
            {
                if (d[_p] == TC_ENDBLOCKDATA) { _p++; return; }
                ReadContent();
            }
        }

        List<object?> ReadAnnotation()
        {
            var list = new List<object?>();
            while (true)
            {
                if (d[_p] == TC_ENDBLOCKDATA) { _p++; return list; }
                list.Add(ReadContent());
            }
        }

        JArray ReadArray()
        {
            var cd = ReadClassDesc()!;
            var a = new JArray { Class = cd };
            NewHandle(a);
            int n = I32();
            if (n < 0) throw new SaveFormatException("Negative array length.");
            a.Length = n;
            a.PayloadOffset = _p;
            char et = cd.Name.Length > 1 ? cd.Name[1] : 'L';
            int size = et switch { 'B' or 'Z' => 1, 'C' or 'S' => 2, 'I' or 'F' => 4, 'J' or 'D' => 8, _ => 0 };
            if (size > 0)
            {
                if ((long)n * size > d.Length - _p) throw new SaveFormatException("Array runs past the end of the stream.");
                switch (et)
                {
                    case 'B': a.Values = d.AsSpan(_p, n).ToArray(); break;
                    case 'I': { var v = new int[n]; for (int i = 0; i < n; i++) v[i] = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(_p + i * 4)); a.Values = v; break; }
                    case 'S': { var v = new short[n]; for (int i = 0; i < n; i++) v[i] = BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(_p + i * 2)); a.Values = v; break; }
                    case 'J': { var v = new long[n]; for (int i = 0; i < n; i++) v[i] = BinaryPrimitives.ReadInt64BigEndian(d.AsSpan(_p + i * 8)); a.Values = v; break; }
                    case 'F': { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(_p + i * 4)); a.Values = v; break; }
                    case 'D': { var v = new double[n]; for (int i = 0; i < n; i++) v[i] = BinaryPrimitives.ReadDoubleBigEndian(d.AsSpan(_p + i * 8)); a.Values = v; break; }
                    default: a.Values = d.AsSpan(_p, n * size).ToArray(); break;
                }
                _p += n * size;
            }
            else
            {
                var v = new object?[n];
                for (int i = 0; i < n; i++) v[i] = ReadObject();
                a.Values = v;
            }
            return a;
        }

        JObject ReadNewObject()
        {
            var cd = ReadClassDesc() ?? throw new SaveFormatException("Object without a class.");
            var o = new JObject { Class = cd };
            NewHandle(o);
            var chain = new List<ClassDesc>();
            for (var c = cd; c != null; c = c.Super) chain.Add(c);
            chain.Reverse();
            foreach (var c in chain)
            {
                if ((c.Flags & SC_EXTERNALIZABLE) != 0)
                {
                    if ((c.Flags & SC_BLOCK_DATA) != 0) o.Annotations.AddRange(ReadAnnotation());
                    else throw new SaveFormatException($"Externalizable class {c.Name} without block data cannot be parsed.");
                    continue;
                }
                foreach (var f in c.Fields)
                {
                    o.Offsets[f.Name] = _p;
                    o.Fields[f.Name] = f.Type switch
                    {
                        'B' => (object)(sbyte)U8(),
                        'C' => (char)U16(),
                        'D' => BitConverter.Int64BitsToDouble(I64()),
                        'F' => BitConverter.Int32BitsToSingle(I32()),
                        'I' => I32(),
                        'J' => I64(),
                        'S' => I16(),
                        'Z' => U8() != 0,
                        _ => ReadObject(),
                    };
                }
                if ((c.Flags & SC_WRITE_METHOD) != 0) o.Annotations.AddRange(ReadAnnotation());
            }
            return o;
        }
    }

    /// <summary>Finds the first object of a class (searching nested fields and arrays).</summary>
    public static JObject? FindObject(IEnumerable<object?> roots, Func<JObject, bool> match)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<object?>(roots.Reverse());
        while (stack.Count > 0)
        {
            var o = stack.Pop();
            if (o == null || !seen.Add(o)) continue;
            if (o is JObject jo)
            {
                if (match(jo)) return jo;
                foreach (var v in jo.Fields.Values) stack.Push(v);
                foreach (var v in jo.Annotations) stack.Push(v);
            }
            else if (o is JArray ja && ja.Values is object?[] items) foreach (var v in items) stack.Push(v);
        }
        return null;
    }

    public static string Describe(object? o, int depth = 0)
    {
        var sb = new StringBuilder();
        Write(sb, o, depth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return sb.ToString();
    }

    static void Write(StringBuilder sb, object? o, int depth, HashSet<object> seen)
    {
        string pad = new(' ', depth * 2);
        switch (o)
        {
            case null: sb.Append("null"); break;
            case string s: sb.Append('"').Append(s).Append('"'); break;
            case JObject jo when !seen.Add(jo): sb.Append($"<{jo.Class.Name} …>"); break;
            case JObject jo:
                sb.Append(jo.Class.Name).Append(" {\n");
                foreach (var (k, v) in jo.Fields)
                {
                    sb.Append(pad).Append("  ").Append(k).Append(" = ");
                    if (depth < 6) Write(sb, v, depth + 1, seen); else sb.Append('…');
                    sb.Append('\n');
                }
                sb.Append(pad).Append('}');
                break;
            case JArray ja: sb.Append($"{ja.Class.Name}[{ja.Length}]"); break;
            case JEnum je: sb.Append(je.Class.Name).Append('.').Append(je.Constant); break;
            case BlockData bd: sb.Append($"<{bd.Data.Length} bytes of block data>"); break;
            default: sb.Append(o); break;
        }
    }
}
