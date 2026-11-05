// MinecraftStudio.Core/LegacyConsole/ConsoleSave.cs: How the archive was wrapped on disk.
// Owns: ConsoleWrapper, ConsoleSave.
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.LegacyConsole;

/// <summary>How the archive was wrapped on disk.</summary>
public enum ConsoleWrapper
{
    /// <summary>Xbox 360 STFS package containing savegame.dat.</summary>
    Stfs,
    /// <summary>Xbox 360 savegame.dat: u32 length, then the .mcs blob.</summary>
    X360Dat,
    /// <summary>Xbox 360 .mcs / bundled world: u32 0, u32 size, LZX.</summary>
    Mcs,
    /// <summary>u32 0, u32 BE size, zlib (Wii U).</summary>
    ZlibBigEndian,
    /// <summary>u32 0, u32 LE size, zlib (PS4, Xbox One, Switch).</summary>
    ZlibLittleEndian,
    /// <summary>u32 0, u32 size, u32 size, raw deflate (PS3).</summary>
    Ps3Deflate,
    /// <summary>u32 0, u32 LE size, zero-RLE (PS Vita).</summary>
    VitaRle,
    /// <summary>The archive itself, not compressed.</summary>
    Raw,
}

public static partial class ConsoleSave
{
    public static bool LooksLikeConsoleFolder(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        foreach (var name in new[] { "GAMEDATA", "GAMEDATA.bin", "savegame.dat" })
        {
            string f = Path.Combine(folder, name);
            if (File.Exists(f) && LooksLikeConsoleFile(f)) return true;
        }
        return false;
    }

    public static bool LooksLikeConsoleFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 16) return false;
            var head = new byte[16];
            using (var fs = info.OpenRead()) fs.ReadExactly(head);
            string magic = System.Text.Encoding.ASCII.GetString(head, 0, 4);
            if (magic is "CON " or "LIVE" or "PIRS") return true;
            string name = info.Name.ToLowerInvariant();
            bool likelyName = name is "savegame.dat" or "gamedata" or "gamedata.bin" || name.EndsWith(".mcs") || name.EndsWith(".bin") || name.EndsWith(".dat");
            if (!likelyName) return false;
            // gzip files are Java NBT / Classic, not console saves.
            if (head[0] == 0x1F && head[1] == 0x8B) return false;
            return TryDecode(File.ReadAllBytes(path), out _, out _, out _);
        }
        catch { return false; }
    }

    /// <summary>Tries every known wrapper and returns the decoded archive.</summary>
    public static bool TryDecode(byte[] d, out ConsoleArchive? archive, out ConsoleWrapper wrapper, out ConsolePlatform platform)
    {
        archive = null;
        wrapper = ConsoleWrapper.Raw;
        platform = ConsolePlatform.Raw;
        if (d.Length < 12) return false;
        uint a = BinaryPrimitives.ReadUInt32BigEndian(d), b = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
        uint c = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8)), bLe = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(4));
        var attempts = new List<(ConsoleWrapper W, ConsolePlatform P, Func<byte[]> Decode, bool Le)>();
        if (a != 0 && b == 0 && a + 4 <= d.Length && c < 1 << 30)
            attempts.Add((ConsoleWrapper.X360Dat, ConsolePlatform.Xbox360, () => XMemLzx.Decompress(d, 12, (int)c, out _), false));
        if (a == 0)
        {
            if (d.Length > 10 && Compression.IsZlibHeader(d.AsSpan(8)))
            {
                bool be = b < bLe;
                uint size = be ? b : bLe;
                attempts.Add((be ? ConsoleWrapper.ZlibBigEndian : ConsoleWrapper.ZlibLittleEndian, be ? ConsolePlatform.WiiU : ConsolePlatform.PS4,
                    () => Compression.ZlibDecompress(TrimZeros(d.AsSpan(8)), (int)size), !be));
            }
            if (b == c && b < 1 << 30)
                attempts.Add((ConsoleWrapper.Ps3Deflate, ConsolePlatform.PS3, () => Compression.Inflate(d.AsSpan(12), (int)b), false));
            if (b < 1 << 30)
                attempts.Add((ConsoleWrapper.Mcs, ConsolePlatform.Xbox360, () => XMemLzx.Decompress(d, 8, (int)b, out _), false));
            if (bLe < 1 << 30)
                attempts.Add((ConsoleWrapper.VitaRle, ConsolePlatform.PSVita, () => ConsoleRle.DecodeVita(d.AsSpan(8), (int)bLe), true));
        }
        bool rawLe = d.Length > 10 && d[10] != 0;
        attempts.Add((ConsoleWrapper.Raw, ConsolePlatform.Raw, () => d, rawLe));
        foreach (var at in attempts)
        {
            byte[] dec;
            try { dec = at.Decode(); }
            catch { continue; }
            if (!ConsoleArchive.Validate(dec, at.Le)) continue;
            archive = ConsoleArchive.Parse(dec, at.Le);
            wrapper = at.W;
            platform = at.P;
            return true;
        }
        return false;
    }

    static ReadOnlySpan<byte> TrimZeros(ReadOnlySpan<byte> s)
    {
        int end = s.Length;
        while (end > 0 && s[end - 1] == 0) end--;
        return s[..Math.Min(s.Length, end + 4)];
    }

    public static SaveWorld Open(string path, IProgress<string>? progress = null)
    {
        if (Directory.Exists(path))
        {
            foreach (var name in new[] { "GAMEDATA", "GAMEDATA.bin", "savegame.dat" })
            {
                string f = Path.Combine(path, name);
                if (File.Exists(f)) return ConsoleWorld.Open(f, progress);
            }
            throw new SaveFormatException("No console save file found in the folder.");
        }
        return ConsoleWorld.Open(path, progress);
    }
}
