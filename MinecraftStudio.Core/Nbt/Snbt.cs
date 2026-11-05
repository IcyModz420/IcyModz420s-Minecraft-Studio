// MinecraftStudio.Core/Nbt/Snbt.cs: SNBT (stringified NBT): reading and writing the text form.
// Owns: SnbtStyle, SnbtException, Snbt.
using System.Globalization;
using System.Text;

namespace MinecraftStudio.Core.Nbt;

public enum SnbtStyle { Compact, Pretty }

public sealed class SnbtException : Exception
{
    public SnbtException(string message, int position, int line, int column)
        : base($"{message} (line {line}, column {column})") { Position = position; Line = line; Column = column; }
    public int Position { get; }
    public int Line { get; }
    public int Column { get; }
}

/// <summary>Stringified NBT (the text form used by commands), with a parser that accepts what the writer produces.</summary>
public static class Snbt
{
    public static string Write(NbtTag tag, SnbtStyle style = SnbtStyle.Pretty, int maxArrayItems = int.MaxValue)
    {
        var sb = new StringBuilder();
        new Writer(sb, style == SnbtStyle.Pretty, maxArrayItems).Value(tag, 0);
        return sb.ToString();
    }

    public static string FormatValue(NbtTag tag, int maxArrayItems = 16) => Write(tag, SnbtStyle.Compact, maxArrayItems);

    public static bool IsBareKey(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s) if (!IsBareChar(c)) return false;
        return true;
    }

    static bool IsBareChar(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.' or '+';

    public static string QuoteKey(string key) => IsBareKey(key) ? key : Quote(key);

    public static string Quote(string s)
    {
        char q = s.Contains('"') && !s.Contains('\'') ? '\'' : '"';
        var sb = new StringBuilder(s.Length + 2).Append(q);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c == q) sb.Append('\\').Append(c);
                    else if (c < 0x20 || (c >= '\uDC80' && c <= '\uDCFF')) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append(q).ToString();
    }

    public static string FormatFloat(float v)
    {
        if (float.IsNaN(v)) return "NaNf";
        if (float.IsPositiveInfinity(v)) return "Infinityf";
        if (float.IsNegativeInfinity(v)) return "-Infinityf";
        string s = v.ToString("R", CultureInfo.InvariantCulture);
        if (!s.Contains('.') && !s.Contains('E')) s += ".0";
        return s + "f";
    }

    public static string FormatDouble(double v)
    {
        if (double.IsNaN(v)) return "NaNd";
        if (double.IsPositiveInfinity(v)) return "Infinityd";
        if (double.IsNegativeInfinity(v)) return "-Infinityd";
        string s = v.ToString("R", CultureInfo.InvariantCulture);
        if (!s.Contains('.') && !s.Contains('E')) s += ".0";
        return s + "d";
    }

    sealed class Writer(StringBuilder sb, bool pretty, int maxItems)
    {
        void Indent(int depth) { if (pretty) sb.Append(' ', depth * 4); }
        void NewLine() { if (pretty) sb.Append('\n'); }

        public void Value(NbtTag tag, int depth)
        {
            switch (tag)
            {
                case NbtByte b: sb.Append(b.Value.ToString(CultureInfo.InvariantCulture)).Append('b'); break;
                case NbtShort s: sb.Append(s.Value.ToString(CultureInfo.InvariantCulture)).Append('s'); break;
                case NbtInt i: sb.Append(i.Value.ToString(CultureInfo.InvariantCulture)); break;
                case NbtLong l: sb.Append(l.Value.ToString(CultureInfo.InvariantCulture)).Append('L'); break;
                case NbtFloat f: sb.Append(FormatFloat(f.Value)); break;
                case NbtDouble d: sb.Append(FormatDouble(d.Value)); break;
                case NbtString str: sb.Append(Quote(str.Value)); break;
                case NbtByteArray ba: Array("B", ba.Value.Length, k => ((sbyte)ba.Value[k]).ToString(CultureInfo.InvariantCulture) + "b"); break;
                case NbtIntArray ia: Array("I", ia.Value.Length, k => ia.Value[k].ToString(CultureInfo.InvariantCulture)); break;
                case NbtLongArray la: Array("L", la.Value.Length, k => la.Value[k].ToString(CultureInfo.InvariantCulture) + "L"); break;
                case NbtList list: List(list, depth); break;
                case NbtCompound c: Compound(c, depth); break;
                default: throw new ArgumentException("Unknown tag " + tag.GetType().Name);
            }
        }

        void Array(string prefix, int count, Func<int, string> item)
        {
            sb.Append('[').Append(prefix).Append(';');
            int shown = Math.Min(count, maxItems);
            for (int k = 0; k < shown; k++)
            {
                sb.Append(k == 0 ? (pretty ? " " : "") : (pretty ? ", " : ","));
                sb.Append(item(k));
            }
            if (shown < count) sb.Append($", … {count - shown:N0} more");
            sb.Append(']');
        }

        void List(NbtList list, int depth)
        {
            if (list.Count == 0) { sb.Append("[]"); return; }
            bool multiline = pretty && (list.ElementType is NbtType.Compound or NbtType.List || list.Count > 0 && list[0] is NbtString);
            int shown = Math.Min(list.Count, maxItems);
            sb.Append('[');
            for (int k = 0; k < shown; k++)
            {
                if (k > 0) sb.Append(',');
                if (multiline) { NewLine(); Indent(depth + 1); }
                else if (k > 0 && pretty) sb.Append(' ');
                Value(list[k], depth + 1);
            }
            if (shown < list.Count) sb.Append($", … {list.Count - shown:N0} more");
            if (multiline) { NewLine(); Indent(depth); }
            sb.Append(']');
        }

        void Compound(NbtCompound c, int depth)
        {
            if (c.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            int k = 0;
            foreach (var kv in c)
            {
                if (k++ > 0) sb.Append(',');
                if (pretty) { NewLine(); Indent(depth + 1); }
                sb.Append(QuoteKey(kv.Key)).Append(pretty ? ": " : ":");
                Value(kv.Value, depth + 1);
            }
            if (pretty) { NewLine(); Indent(depth); }
            sb.Append('}');
        }
    }

    public static NbtTag Parse(string text)
    {
        var p = new Parser(text);
        var tag = p.ParseValue(0);
        p.SkipWs();
        if (!p.AtEnd) throw p.Error("Unexpected text after the value");
        return tag;
    }

    public static bool TryParse(string text, out NbtTag? tag, out string? error)
    {
        try { tag = Parse(text); error = null; return true; }
        catch (SnbtException ex) { tag = null; error = ex.Message; return false; }
    }

    /// <summary>Parses a bare value typed into an editor for a tag of a known type ("5" for a Byte tag means 5b).</summary>
    public static NbtTag ParseAs(string text, NbtType type)
    {
        string t = text.Trim();
        var inv = CultureInfo.InvariantCulture;
        static string Strip(string s, char suffix) => s.Length > 1 && char.ToLowerInvariant(s[^1]) == suffix ? s[..^1] : s;
        try
        {
            switch (type)
            {
                case NbtType.Byte:
                    if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return new NbtByte(1);
                    if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return new NbtByte(0);
                    return new NbtByte(sbyte.Parse(Strip(t, 'b'), NumberStyles.Integer, inv));
                case NbtType.Short: return new NbtShort(short.Parse(Strip(t, 's'), NumberStyles.Integer, inv));
                case NbtType.Int: return new NbtInt(int.Parse(t, NumberStyles.Integer, inv));
                case NbtType.Long: return new NbtLong(long.Parse(Strip(t, 'l'), NumberStyles.Integer, inv));
                case NbtType.Float: return new NbtFloat(ParseFloatText(Strip(t, 'f')));
                case NbtType.Double: return new NbtDouble(ParseDoubleText(Strip(t, 'd')));
                case NbtType.String:
                    if (t.Length >= 2 && (t[0] == '"' || t[0] == '\'') && t[^1] == t[0]) return Parse(t);
                    return new NbtString(text);
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new SnbtException($"\"{text}\" is not a valid {NbtTypeInfo.Name(type)} value", 0, 1, 1);
        }
        var parsed = Parse(t);
        if (parsed.Type != type) throw new SnbtException($"Expected a {NbtTypeInfo.Name(type)} value but got {NbtTypeInfo.Name(parsed.Type)}", 0, 1, 1);
        return parsed;
    }

    static float ParseFloatText(string s) => s switch
    {
        "NaN" => float.NaN,
        "Infinity" or "+Infinity" => float.PositiveInfinity,
        "-Infinity" => float.NegativeInfinity,
        _ => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    static double ParseDoubleText(string s) => s switch
    {
        "NaN" => double.NaN,
        "Infinity" or "+Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        _ => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    sealed class Parser(string s)
    {
        int _p;
        public bool AtEnd => _p >= s.Length;

        public SnbtException Error(string msg)
        {
            int line = 1, col = 1;
            for (int i = 0; i < _p && i < s.Length; i++) { if (s[i] == '\n') { line++; col = 1; } else col++; }
            return new SnbtException(msg, _p, line, col);
        }

        public void SkipWs() { while (_p < s.Length && char.IsWhiteSpace(s[_p])) _p++; }
        char Peek() { SkipWs(); return _p < s.Length ? s[_p] : '\0'; }

        void Expect(char c)
        {
            if (Peek() != c) throw Error($"Expected '{c}'");
            _p++;
        }

        public NbtTag ParseValue(int depth)
        {
            if (depth > NbtBinary.MaxDepth) throw Error("Nested too deeply");
            char c = Peek();
            if (c == '{') return ParseCompound(depth);
            if (c == '[') return ParseListOrArray(depth);
            if (c == '"' || c == '\'') return new NbtString(ParseQuoted());
            if (c == '\0') throw Error("Expected a value");
            int start = _p;
            string tok = ParseBare();
            if (tok.Length == 0) throw Error($"Unexpected character '{s[_p]}'");
            return InferBare(tok, start);
        }

        string ParseBare()
        {
            int start = _p;
            while (_p < s.Length && IsBareChar(s[_p])) _p++;
            return s[start.._p];
        }

        string ParseKey()
        {
            char c = Peek();
            if (c == '"' || c == '\'') return ParseQuoted();
            string k = ParseBare();
            if (k.Length == 0) throw Error("Expected a key");
            return k;
        }

        string ParseQuoted()
        {
            char q = s[_p++];
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= s.Length) throw Error("Unterminated string");
                char c = s[_p++];
                if (c == q) break;
                if (c != '\\') { sb.Append(c); continue; }
                if (_p >= s.Length) throw Error("Unterminated escape");
                char e = s[_p++];
                switch (e)
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 's': sb.Append(' '); break;
                    case 'x': sb.Append((char)Hex(2)); break;
                    case 'u': sb.Append((char)Hex(4)); break;
                    case 'U': sb.Append(char.ConvertFromUtf32(Hex(8))); break;
                    default: throw Error($"Unknown escape \\{e}");
                }
            }
            return sb.ToString();
        }

        int Hex(int n)
        {
            if (_p + n > s.Length) throw Error("Bad escape");
            if (!int.TryParse(s.AsSpan(_p, n), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) throw Error("Bad escape");
            _p += n;
            return v;
        }

        NbtTag ParseCompound(int depth)
        {
            Expect('{');
            var c = new NbtCompound();
            if (Peek() == '}') { _p++; return c; }
            while (true)
            {
                string key = ParseKey();
                Expect(':');
                c[key] = ParseValue(depth + 1);
                char n = Peek();
                if (n == ',') { _p++; if (Peek() == '}') { _p++; return c; } continue; }
                if (n == '}') { _p++; return c; }
                throw Error("Expected ',' or '}'");
            }
        }

        NbtTag ParseListOrArray(int depth)
        {
            Expect('[');
            SkipWs();
            if (_p + 1 < s.Length && s[_p + 1] == ';' && (s[_p] is 'B' or 'I' or 'L' or 'b' or 'i' or 'l'))
            {
                char kind = char.ToUpperInvariant(s[_p]);
                _p += 2;
                var values = new List<long>();
                if (Peek() != ']')
                {
                    while (true)
                    {
                        int at = _p;
                        SkipWs();
                        string tok = ParseBare();
                        long v = ParseArrayItem(tok, kind, at);
                        values.Add(v);
                        char n = Peek();
                        if (n == ',') { _p++; if (Peek() == ']') break; continue; }
                        if (n == ']') break;
                        throw Error("Expected ',' or ']'");
                    }
                }
                Expect(']');
                return kind switch
                {
                    'B' => new NbtByteArray(values.Select(v => (byte)(sbyte)v).ToArray()),
                    'I' => new NbtIntArray(values.Select(v => (int)v).ToArray()),
                    _ => new NbtLongArray(values.ToArray()),
                };
            }

            var list = new NbtList();
            if (Peek() == ']') { _p++; return list; }
            while (true)
            {
                int at = _p;
                var item = ParseValue(depth + 1);
                if (list.Count > 0 && item.Type != list.ElementType)
                {
                    _p = at;
                    throw Error($"List items must all be {NbtTypeInfo.Name(list.ElementType)}, found {NbtTypeInfo.Name(item.Type)}");
                }
                list.Add(item);
                char n = Peek();
                if (n == ',') { _p++; if (Peek() == ']') { _p++; return list; } continue; }
                if (n == ']') { _p++; return list; }
                throw Error("Expected ',' or ']'");
            }
        }

        long ParseArrayItem(string tok, char kind, int at)
        {
            string t = tok;
            if (t.Length > 1 && char.ToUpperInvariant(t[^1]) is 'B' or 'S' or 'L' && !(t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))) t = t[..^1];
            if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1;
            if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0;
            if (!long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v)) { _p = at; throw Error($"'{tok}' is not a whole number"); }
            bool ok = kind switch { 'B' => v is >= sbyte.MinValue and <= byte.MaxValue, 'I' => v is >= int.MinValue and <= uint.MaxValue, _ => true };
            if (!ok) { _p = at; throw Error($"{v} is out of range for this array"); }
            return v;
        }

        static NbtTag InferBare(string tok, int start)
        {
            var inv = CultureInfo.InvariantCulture;
            if (tok.Equals("true", StringComparison.OrdinalIgnoreCase)) return new NbtByte(1);
            if (tok.Equals("false", StringComparison.OrdinalIgnoreCase)) return new NbtByte(0);
            char last = char.ToLowerInvariant(tok[^1]);
            string body = tok.Length > 1 ? tok[..^1] : tok;
            bool intBody = IsInteger(body);
            switch (last)
            {
                case 'b' when intBody && sbyte.TryParse(body, NumberStyles.Integer, inv, out var b): return new NbtByte(b);
                case 's' when intBody && short.TryParse(body, NumberStyles.Integer, inv, out var sh): return new NbtShort(sh);
                case 'l' when intBody && long.TryParse(body, NumberStyles.Integer, inv, out var l): return new NbtLong(l);
                case 'f' when IsDecimal(body, true): return new NbtFloat(ParseFloatText(body));
                case 'd' when IsDecimal(body, true): return new NbtDouble(ParseDoubleText(body));
            }
            if (IsInteger(tok) && int.TryParse(tok, NumberStyles.Integer, inv, out int i)) return new NbtInt(i);
            if (IsDecimal(tok, false) && double.TryParse(tok, NumberStyles.Float, inv, out double d)) return new NbtDouble(d);
            return new NbtString(tok);
        }

        static bool IsInteger(string t)
        {
            int i = 0;
            if (i < t.Length && (t[i] == '-' || t[i] == '+')) i++;
            if (i >= t.Length) return false;
            for (; i < t.Length; i++) if (t[i] < '0' || t[i] > '9') return false;
            return true;
        }

        /// <summary>Decimal number; without a suffix a '.' or exponent is required (otherwise it is an int).</summary>
        static bool IsDecimal(string t, bool suffixed)
        {
            string body = t.TrimStart('-', '+');
            if (body is "NaN" or "Infinity") return suffixed;
            int i = 0, digits = 0;
            bool dot = false, exp = false;
            for (; i < t.Length; i++)
            {
                char c = t[i];
                if (c is '-' or '+') { if (i != 0 && char.ToLowerInvariant(t[i - 1]) != 'e') return false; }
                else if (c >= '0' && c <= '9') digits++;
                else if (c == '.') { if (dot || exp) return false; dot = true; }
                else if (c is 'e' or 'E') { if (exp || digits == 0) return false; exp = true; }
                else return false;
            }
            if (digits == 0) return false;
            if (char.ToLowerInvariant(t[^1]) == 'e' || t[^1] is '+' or '-') return false;
            return suffixed || dot || exp;
        }
    }
}
