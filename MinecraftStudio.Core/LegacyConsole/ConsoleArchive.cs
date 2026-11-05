// MinecraftStudio.Core/LegacyConsole/ConsoleArchive.cs: The decompressed "4J" save archive: header, file blobs,
// file table.
// Owns: ConsoleArchive.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

/// <summary>The decompressed "4J" save archive: header, file blobs, file table.</summary>
public sealed class ConsoleArchive
{
    public sealed class FileEntry
    {
        public string Name { get; set; } = "";
        public byte[] Data { get; set; } = [];
        /// <summary>Milliseconds since 1601-01-01 (v2+), 0 for v1 archives.</summary>
        public long Modified { get; set; }
        /// <summary>An all-zero table slot (seen in modded saves); kept so the table is written back unchanged.</summary>
        public bool Blank { get; set; }
    }

    public List<FileEntry> Files { get; } = [];
    public bool LittleEndian { get; set; }
    public ushort MinVersion { get; set; }
    public ushort Version { get; set; }
    public int EntrySize => Version <= 1 ? 136 : 144;
    public string? Warning { get; private set; }

    public FileEntry? Find(string name) => Files.FirstOrDefault(f => !f.Blank && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ConsoleArchive Parse(byte[] d, bool littleEndian)
    {
        ConsoleArchive? first = null;
        try
        {
            first = ParseAt(d, littleEndian, null);
            if (first.Files.Count == 0 || first.Files.Any(f => !f.Blank)) return first;
        }
        catch (SaveFormatException) when (d.Length >= 12) { }
        // Some third-party editors leave a stale table offset (pointing at zeros or an old table);
        // the current table always sits at the end of the data.
        try { return ParseAt(d, littleEndian, useEndOfFile: true); }
        catch (SaveFormatException) when (first != null) { return first; }
    }

    static ConsoleArchive ParseAt(byte[] d, bool littleEndian, bool? useEndOfFile)
    {
        if (d.Length < 12) throw new SaveFormatException("Save archive is too short.");
        var a = new ConsoleArchive { LittleEndian = littleEndian };
        uint U32(int at) => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        ushort U16(int at) => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
        uint tableOffset = U32(0);
        uint countOrSize = U32(4);
        a.MinVersion = U16(8);
        a.Version = U16(10);
        if (a.Version > 13) throw new SaveFormatException($"Unknown save archive version {a.Version}.");
        int entry = a.EntrySize;
        int count = a.Version <= 1 ? (int)(countOrSize / 136) : (int)countOrSize;
        if (count is < 0 or > 32768) throw new SaveFormatException("Save archive has an invalid file count.");
        if (useEndOfFile == true || tableOffset + (long)count * entry > d.Length || (count == 0 && tableOffset != 12))
        {
            // Some third-party editors leave a stale offset; the table is always at the end.
            long fixedOffset = d.Length - (long)count * entry;
            if (fixedOffset < 12) throw new SaveFormatException("Save archive file table does not fit.");
            a.Warning = $"The archive header pointed at the wrong place for the file table (0x{tableOffset:X}); it was found at the end of the file instead.";
            tableOffset = (uint)fixedOffset;
        }
        for (int i = 0; i < count; i++)
        {
            int at = (int)tableOffset + i * entry;
            string name = (littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(d, at, 128).Split('\0')[0];
            uint size = U32(at + 128), offset = U32(at + 132);
            long modified = 0;
            if (entry == 144) modified = littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(at + 136)) : BinaryPrimitives.ReadInt64BigEndian(d.AsSpan(at + 136));
            if (name.Length == 0 && size == 0 && offset == 0)
            {
                a.Files.Add(new FileEntry { Blank = true, Modified = modified });
                continue;
            }
            if (offset < 12 || offset + (long)size > tableOffset) throw new SaveFormatException($"File {name} lies outside the archive data.");
            if (name.Length == 0 || name.Any(ch => ch < 0x20 || ch == 0xFFFD)) throw new SaveFormatException("File table entry has an invalid name.");
            a.Files.Add(new FileEntry { Name = name, Data = d.AsSpan((int)offset, (int)size).ToArray(), Modified = modified });
        }
        return a;
    }

    /// <summary>Checks that a buffer decodes as an archive in the given byte order.</summary>
    public static bool Validate(byte[] d, bool littleEndian)
    {
        try
        {
            var a = Parse(d, littleEndian);
            return a.Files.Any(f => !f.Blank) || a.Files.Count == 0;
        }
        catch { return false; }
    }

    public byte[] ToBytes()
    {
        var w = new ByteWriter(Files.Sum(f => f.Data.Length) + Files.Count * EntrySize + 64);
        w.Zeros(12);
        var offsets = new List<int>();
        foreach (var f in Files)
        {
            offsets.Add(w.Length);
            w.Bytes(f.Data);
        }
        int tableOffset = w.Length;
        bool le = LittleEndian;
        for (int i = 0; i < Files.Count; i++)
        {
            var f = Files[i];
            if (f.Blank)
            {
                // 136-byte entries have no timestamp, so their last 8 bytes are size+offset (zero) instead.
                w.Zeros(EntrySize - 8);
                w.I64(EntrySize == 144 ? f.Modified : 0, le);
                continue;
            }
            var name = new byte[128];
            (le ? Encoding.Unicode : Encoding.BigEndianUnicode).GetBytes(f.Name.Length > 63 ? f.Name[..63] : f.Name).CopyTo(name, 0);
            w.Bytes(name);
            w.U32((uint)f.Data.Length, le);
            w.U32((uint)offsets[i], le);
            if (EntrySize == 144) w.I64(f.Modified, le);
        }
        var buf = w.RawBuffer;
        if (le)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), (uint)tableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), (uint)(Version <= 1 ? Files.Count * 136 : Files.Count));
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(8), MinVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(10), Version);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0), (uint)tableOffset);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), (uint)(Version <= 1 ? Files.Count * 136 : Files.Count));
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(8), MinVersion);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(10), Version);
        }
        return w.ToArray();
    }

    public static long Now1601Ms() => (long)(DateTime.UtcNow - new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

    public static DateTime? FromFileTimeMs(long ms) =>
        ms <= 0 ? null : new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();

    public static string Era(int version) => version switch
    {
        0 or 1 => "pre-release",
        2 => "TU0–TU4",
        3 or 4 => "TU5–TU8",
        5 => "TU9–TU13",
        6 => "TU14–TU16",
        7 => "TU14–TU16 (DLC)",
        8 => "TU17–TU18",
        9 => "TU19–TU35",
        10 => "TU36–TU68",
        11 => "TU69+",
        _ => $"version {version}",
    };
}
