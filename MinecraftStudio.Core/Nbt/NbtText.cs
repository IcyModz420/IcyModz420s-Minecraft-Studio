// MinecraftStudio.Core/Nbt/NbtText.cs: String codecs for NBT. Both are lossless: bytes that are not valid text
// are mapped to U+DC80..U+DCFF (like Python's surrogateescape) and written back as the original bytes.
// Owns: NbtText.
using System.Text;

namespace MinecraftStudio.Core.Nbt;

/// <summary>
/// String codecs for NBT. Both are lossless: bytes that are not valid text are mapped to
/// U+DC80..U+DCFF (like Python's surrogateescape) and written back as the original bytes.
/// </summary>
public static class NbtText
{
    const char EscapeBase = '\uDC00';

    static bool IsEscape(string s, int i) =>
        s[i] >= '\uDC80' && s[i] <= '\uDCFF' && (i == 0 || !char.IsHighSurrogate(s[i - 1]));

    /// <summary>Java's "modified UTF-8" (DataInput.readUTF).</summary>
    public static string DecodeModifiedUtf8(ReadOnlySpan<byte> s)
    {
        if (Ascii.IsValid(s) && s.IndexOf((byte)0) < 0) return Encoding.Latin1.GetString(s);
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80) { sb.Append((char)b); i++; }
            else if ((b & 0xE0) == 0xC0 && i + 1 < s.Length && (s[i + 1] & 0xC0) == 0x80)
            {
                sb.Append((char)(((b & 0x1F) << 6) | (s[i + 1] & 0x3F)));
                i += 2;
            }
            else if ((b & 0xF0) == 0xE0 && i + 2 < s.Length && (s[i + 1] & 0xC0) == 0x80 && (s[i + 2] & 0xC0) == 0x80)
            {
                sb.Append((char)(((b & 0x0F) << 12) | ((s[i + 1] & 0x3F) << 6) | (s[i + 2] & 0x3F)));
                i += 3;
            }
            else { sb.Append((char)(EscapeBase + b)); i++; }
        }
        return sb.ToString();
    }

    public static int ModifiedUtf8Length(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != 0 && c < 0x80) n += 1;
            else if (IsEscape(s, i)) n += 1;
            else if (c < 0x800) n += 2;
            else n += 3;
        }
        return n;
    }

    public static void EncodeModifiedUtf8(string s, Span<byte> dst)
    {
        int p = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != 0 && c < 0x80) dst[p++] = (byte)c;
            else if (IsEscape(s, i)) dst[p++] = (byte)(c - EscapeBase);
            else if (c < 0x800)
            {
                dst[p++] = (byte)(0xC0 | (c >> 6));
                dst[p++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                dst[p++] = (byte)(0xE0 | (c >> 12));
                dst[p++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                dst[p++] = (byte)(0x80 | (c & 0x3F));
            }
        }
    }

    /// <summary>Standard UTF-8 (Bedrock), keeping invalid bytes.</summary>
    public static string DecodeUtf8(ReadOnlySpan<byte> s)
    {
        if (Ascii.IsValid(s)) return Encoding.Latin1.GetString(s);
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80) { sb.Append((char)b); i++; continue; }
            int need, cp, min2 = 0x80, max2 = 0xBF;
            if (b >= 0xC2 && b <= 0xDF) { need = 1; cp = b & 0x1F; }
            else if (b >= 0xE0 && b <= 0xEF)
            {
                need = 2; cp = b & 0x0F;
                if (b == 0xE0) min2 = 0xA0;
                if (b == 0xED) max2 = 0x9F;
            }
            else if (b >= 0xF0 && b <= 0xF4)
            {
                need = 3; cp = b & 0x07;
                if (b == 0xF0) min2 = 0x90;
                if (b == 0xF4) max2 = 0x8F;
            }
            else { sb.Append((char)(EscapeBase + b)); i++; continue; }

            // The sequence needs bytes s[i+1] .. s[i+need].
            bool ok = i + need < s.Length;
            if (ok)
            {
                byte b2 = s[i + 1];
                if (b2 < min2 || b2 > max2) ok = false;
                for (int k = 2; ok && k <= need; k++) if ((s[i + k] & 0xC0) != 0x80) ok = false;
            }
            if (!ok) { sb.Append((char)(EscapeBase + b)); i++; continue; }
            for (int k = 1; k <= need; k++) cp = (cp << 6) | (s[i + k] & 0x3F);
            if (cp >= 0x10000) sb.Append(char.ConvertFromUtf32(cp));
            else sb.Append((char)cp);
            i += need + 1;
        }
        return sb.ToString();
    }

    public static int Utf8Length(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80) n += 1;
            else if (c < 0x800) n += 2;
            else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { n += 4; i++; }
            else if (IsEscape(s, i)) n += 1;
            else n += 3;
        }
        return n;
    }

    public static void EncodeUtf8(string s, Span<byte> dst)
    {
        int p = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80) dst[p++] = (byte)c;
            else if (c < 0x800)
            {
                dst[p++] = (byte)(0xC0 | (c >> 6));
                dst[p++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                int cp = char.ConvertToUtf32(c, s[i + 1]);
                i++;
                dst[p++] = (byte)(0xF0 | (cp >> 18));
                dst[p++] = (byte)(0x80 | ((cp >> 12) & 0x3F));
                dst[p++] = (byte)(0x80 | ((cp >> 6) & 0x3F));
                dst[p++] = (byte)(0x80 | (cp & 0x3F));
            }
            else if (IsEscape(s, i)) dst[p++] = (byte)(c - EscapeBase);
            else
            {
                if (char.IsSurrogate(c)) c = '�';
                dst[p++] = (byte)(0xE0 | (c >> 12));
                dst[p++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                dst[p++] = (byte)(0x80 | (c & 0x3F));
            }
        }
    }
}
