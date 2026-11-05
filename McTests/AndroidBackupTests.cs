using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using MinecraftStudio.Core.IO;

/// <summary>
/// backup: the no-root Android .ab reader. Builds a synthetic backup (ANDROID BACKUP header + zlib + tar) and checks
/// AndroidBackup.Read returns exactly the regular files with their bytes, and that encrypted / non-backup streams are
/// rejected. This is the untrusted-input parser behind "get worlds from inside the app without root".
/// </summary>
static class AndroidBackupTests
{
    public static void Run(string[] args)
    {
        Console.WriteLine("== Android .ab reader");
        var files = new (string Path, byte[] Data)[]
        {
            ("apps/com.mojang.minecraftpe/f/games/com.mojang/minecraftWorlds/MyWorld/level.dat", "LEVEL"u8.ToArray()),
            ("apps/com.mojang.minecraftpe/f/games/com.mojang/minecraftWorlds/MyWorld/db/000001.ldb", new byte[1000]),
            ("apps/com.mojang.minecraftpe/f/games/com.mojang/minecraftWorlds/Another World/level.dat", "LVL2"u8.ToArray()),
            ("apps/com.mojang.minecraftpe/sp/prefs.xml", "<prefs/>"u8.ToArray()),
        };

        byte[] ab = BuildAb(files, compressed: true, encryption: "none");
        var got = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        AndroidBackup.Read(new MemoryStream(ab), (name, data) => { var ms = new MemoryStream(); data.CopyTo(ms); got[name] = ms.ToArray(); });
        Program.Check(got.Count == files.Length, $"reads every regular file ({got.Count}/{files.Length})");
        foreach (var (path, data) in files)
            Program.Check(got.TryGetValue(path, out var b) && b.AsSpan().SequenceEqual(data), $"file bytes match: {path.Split('/')[^1]}");

        // Uncompressed (compression flag 0) also reads.
        var got2 = new List<string>();
        AndroidBackup.Read(new MemoryStream(BuildAb(files, compressed: false, encryption: "none")), (name, _) => got2.Add(name));
        Program.Check(got2.Count == files.Length, "reads an uncompressed backup too");

        // Encrypted -> EncryptedException.
        bool enc = false;
        try { AndroidBackup.Read(new MemoryStream(BuildAb(files, true, "AES-256")), (_, _) => { }); }
        catch (AndroidBackup.EncryptedException) { enc = true; }
        Program.Check(enc, "a password-protected backup is refused with EncryptedException");

        // Not a backup -> InvalidDataException.
        bool bad = false;
        try { AndroidBackup.Read(new MemoryStream("not a backup\n"u8.ToArray()), (_, _) => { }); }
        catch (InvalidDataException) { bad = true; }
        Program.Check(bad, "a non-backup stream is refused with InvalidDataException");
    }

    static byte[] BuildAb((string Path, byte[] Data)[] files, bool compressed, string encryption)
    {
        using var tar = new MemoryStream();
        using (var tw = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
            foreach (var (path, data) in files)
                tw.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream(data) });
        tar.Position = 0;

        byte[] body;
        if (compressed)
        {
            using var comp = new MemoryStream();
            using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true)) tar.CopyTo(z);
            body = comp.ToArray();
        }
        else body = tar.ToArray();

        var header = Encoding.ASCII.GetBytes($"ANDROID BACKUP\n5\n{(compressed ? 1 : 0)}\n{encryption}\n");
        var ab = new byte[header.Length + body.Length];
        header.CopyTo(ab, 0);
        body.CopyTo(ab, header.Length);
        return ab;
    }
}
