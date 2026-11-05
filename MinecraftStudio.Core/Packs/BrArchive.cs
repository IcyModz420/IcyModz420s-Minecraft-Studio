// MinecraftStudio.Core/Packs/BrArchive.cs: The archives Bedrock packs its own resource packs in (the
// "__brarchive" folder of the installed game).
// Owns: BrArchive.
using System.Buffers.Binary;
using System.Text;

namespace MinecraftStudio.Core.Packs;

/// <summary>
/// The archives Bedrock packs its own resource packs in (the "__brarchive" folder of the installed game). One archive
/// holds the files of one folder: "__brarchive/textures/blocks.brarchive" is the folder "textures/blocks".
/// Layout: an 8-byte magic, the entry count and a version (both 32-bit little endian), then one 256-byte entry per
/// file (name length, name, then the data offset and size at bytes 248 and 252), then the file data.
/// </summary>
public static class BrArchive
{
    static readonly byte[] Magic = [0x7d, 0x27, 0x25, 0xb1, 0xa0, 0x52, 0x70, 0x26];
    const int EntrySize = 256, MaxName = 247;

    public static bool IsBrArchive(ReadOnlySpan<byte> data) => data.Length >= 16 && data[..8].SequenceEqual(Magic);

    /// <summary>The file table: each file's name and where its data is in the archive.</summary>
    public static List<(string Name, int Start, int Size)> Table(byte[] data)
    {
        if (!IsBrArchive(data)) throw new InvalidDataException("Not a Bedrock archive (.brarchive).");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if (count > (data.Length - 16) / EntrySize) throw new InvalidDataException("The archive's file table is cut off.");
        long dataStart = 16 + count * EntrySize;
        var table = new List<(string, int, int)>((int)count);
        for (int i = 0; i < count; i++)
        {
            int e = 16 + i * EntrySize;
            int len = data[e];
            if (len == 0 || len > MaxName) throw new InvalidDataException($"Entry {i} of the archive has a damaged name.");
            string name = Encoding.UTF8.GetString(data, e + 1, len);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e + 248));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e + 252));
            if (dataStart + offset + size > data.Length) throw new InvalidDataException($"\"{name}\" runs past the end of the archive.");
            table.Add((name, (int)(dataStart + offset), (int)size));
        }
        return table;
    }

    /// <summary>The files in an archive, in stored order.</summary>
    public static List<(string Name, byte[] Data)> Read(byte[] data) =>
        Table(data).Select(t => (t.Name, data.AsSpan(t.Start, t.Size).ToArray())).ToList();

    /// <summary>
    /// Every file of an installed pack folder, loose and archived, by path relative to the pack ("textures/blocks/stone.png").
    /// Loose files win over archived ones of the same path. Archives that cannot be read are listed in <paramref name="problems"/>.
    /// </summary>
    public static Dictionary<string, Func<byte[]>> PackFiles(string packFolder, List<string>? problems = null)
    {
        var files = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        string archives = Path.Combine(packFolder, "__brarchive");
        if (Directory.Exists(archives))
            foreach (var file in Directory.EnumerateFiles(archives, "*.brarchive", SearchOption.AllDirectories))
            {
                string folder = Path.ChangeExtension(Path.GetRelativePath(archives, file), null).Replace('\\', '/');
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    foreach (var (name, start, size) in Table(bytes))
                        files[folder + "/" + name] = () => bytes.AsSpan(start, size).ToArray();
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    problems?.Add($"{Path.GetRelativePath(packFolder, file)}: {e.Message}");
                }
            }
        foreach (var file in Directory.EnumerateFiles(packFolder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(packFolder, file).Replace('\\', '/');
            if (rel.StartsWith("__brarchive/", StringComparison.OrdinalIgnoreCase)) continue;
            string f = file;
            files[rel] = () => File.ReadAllBytes(f);
        }
        return files;
    }
}
