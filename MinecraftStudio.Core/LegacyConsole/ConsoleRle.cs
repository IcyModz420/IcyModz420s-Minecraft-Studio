// MinecraftStudio.Core/LegacyConsole/ConsoleRle.cs: Run-length codecs used by console saves.
// Owns: ConsoleRle.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

/// <summary>Run-length codecs used by console saves.</summary>
public static class ConsoleRle
{
    /// <summary>Chunk RLE (all platforms): FF n [v] encodes n+1 copies.</summary>
    public static byte[] DecodeChunk(ReadOnlySpan<byte> s, int expected)
    {
        var o = new ByteWriter(Math.Max(16, expected));
        int p = 0;
        while (p < s.Length)
        {
            byte b = s[p++];
            if (b != 0xFF) { o.U8(b); continue; }
            if (p >= s.Length) break;
            int n = s[p++];
            if (n < 3)
            {
                for (int i = 0; i <= n; i++) o.U8(0xFF);
            }
            else
            {
                if (p >= s.Length) break;
                byte v = s[p++];
                var span = o.Reserve(n + 1);
                span.Fill(v);
            }
        }
        return o.ToArray();
    }

    public static byte[] EncodeChunk(ReadOnlySpan<byte> s)
    {
        var o = new ByteWriter(s.Length / 2 + 16);
        int i = 0;
        while (i < s.Length)
        {
            byte v = s[i];
            int run = 1;
            while (i + run < s.Length && s[i + run] == v && run < 256) run++;
            if (v != 0xFF && run >= 4) { o.U8(0xFF); o.U8((byte)(run - 1)); o.U8(v); }
            else if (v != 0xFF) { for (int k = 0; k < run; k++) o.U8(v); }
            else if (run <= 3) { o.U8(0xFF); o.U8((byte)(run - 1)); }
            else { o.U8(0xFF); o.U8((byte)(run - 1)); o.U8(0xFF); }
            i += run;
        }
        return o.ToArray();
    }

    /// <summary>PS Vita save RLE: 00 n = n zero bytes.</summary>
    public static byte[] DecodeVita(ReadOnlySpan<byte> s, int expected)
    {
        var o = new ByteWriter(Math.Max(16, expected));
        int p = 0;
        while (p < s.Length && o.Length < expected)
        {
            byte b = s[p++];
            if (b != 0) { o.U8(b); continue; }
            if (p >= s.Length) break;
            o.Zeros(s[p++]);
        }
        return o.ToArray();
    }

    public static byte[] EncodeVita(ReadOnlySpan<byte> s)
    {
        var o = new ByteWriter(s.Length / 2 + 16);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != 0) { o.U8(s[i++]); continue; }
            int run = 1;
            while (i + run < s.Length && s[i + run] == 0 && run < 255) run++;
            o.U8(0);
            o.U8((byte)run);
            i += run;
        }
        return o.ToArray();
    }

    /// <summary>PS4/Xbox One/Switch split region RLE: 00 n = n zeros; 00 00 hh ll = BE16+256 zeros.</summary>
    public static byte[] DecodeSplit(ReadOnlySpan<byte> s, int expected)
    {
        var o = new ByteWriter(Math.Max(16, expected));
        int p = 0;
        while (p < s.Length && o.Length < expected)
        {
            byte b = s[p++];
            if (b != 0) { o.U8(b); continue; }
            if (p >= s.Length) break;
            int n = s[p++];
            if (n != 0) { o.Zeros(n); continue; }
            if (p + 2 > s.Length) break;
            o.Zeros(((s[p] << 8) | s[p + 1]) + 256);
            p += 2;
        }
        return o.ToArray();
    }

    public static byte[] EncodeSplit(ReadOnlySpan<byte> s)
    {
        var o = new ByteWriter(s.Length / 2 + 16);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != 0) { o.U8(s[i++]); continue; }
            int run = 1;
            while (i + run < s.Length && s[i + run] == 0 && run < 65791) run++;
            if (run <= 255) { o.U8(0); o.U8((byte)run); }
            else { o.U8(0); o.U8(0); o.U16BE((ushort)(run - 256)); }
            i += run;
        }
        return o.ToArray();
    }
}
