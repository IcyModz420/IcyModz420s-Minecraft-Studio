// MinecraftStudio.Core/IO/SafeFile.cs: Writes files through a temporary file so a crash never leaves a
// half-written save.
// Owns: SafeFile.
namespace MinecraftStudio.Core.IO;

/// <summary>Writes files through a temporary file so a crash never leaves a half-written save.</summary>
public static class SafeFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> data)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".mss-tmp");
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(data);
            fs.Flush(true);
        }
        if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    public static void WriteAllBytes(string path, byte[] data) => WriteAllBytes(path, data.AsSpan());

    /// <summary>Opens a temp file next to <paramref name="path"/>; call <see cref="Commit"/> to replace the target.</summary>
    public static FileStream CreateTemp(string path, out string tmp)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".mss-tmp");
        return new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    }

    public static void Commit(string tmp, string path)
    {
        if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    /// <summary>
    /// Reads a whole file even when parts of it cannot be read (a failing disk or memory card): the file is read again
    /// in 4 KB pieces and pieces that fail become zeros. <paramref name="unreadable"/> is the number of such pieces.
    /// </summary>
    public static byte[] ReadAllBytesTolerant(string path, out int unreadable)
    {
        unreadable = 0;
        try { return File.ReadAllBytes(path); }
        catch (IOException) when (File.Exists(path)) { }
        const int Piece = 4096;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
        long length = fs.Length;
        if (length > Array.MaxLength) throw new IOException($"{Path.GetFileName(path)} is too large to read.");
        var data = new byte[length];
        for (long pos = 0; pos < length; pos += Piece)
        {
            int n = (int)Math.Min(Piece, length - pos);
            try
            {
                fs.Position = pos;
                fs.ReadExactly(data, (int)pos, n);
            }
            catch (IOException)
            {
                unreadable++;
                Array.Clear(data, (int)pos, n);
            }
        }
        return data;
    }
}
