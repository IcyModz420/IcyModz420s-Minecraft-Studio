// MinecraftStudio.Core/LevelDb/Manifest.cs: LevelDB MANIFEST parsing — the set of live SSTable files and their key ranges.
// Owns: FileMeta, VersionEdit, ManifestState.
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LevelDb;

public sealed class FileMeta
{
    public int Level { get; set; }
    public ulong Number { get; set; }
    public ulong Size { get; set; }
    public byte[] Smallest { get; set; } = [];
    public byte[] Largest { get; set; } = [];
}

/// <summary>A MANIFEST VersionEdit record.</summary>
public sealed class VersionEdit
{
    const int Comparator = 1, LogNumber = 2, NextFileNumber = 3, LastSequence = 4, CompactPointer = 5, DeletedFile = 6, NewFile = 7, PrevLogNumber = 9;

    public string? ComparatorName { get; set; }
    public ulong? Log { get; set; }
    public ulong? PrevLog { get; set; }
    public ulong? NextFile { get; set; }
    public ulong? LastSeq { get; set; }
    public List<(int Level, ulong Number)> Deleted { get; } = [];
    public List<FileMeta> Added { get; } = [];
    public List<(int Level, byte[] Key)> CompactPointers { get; } = [];

    public static VersionEdit Decode(byte[] rec)
    {
        var e = new VersionEdit();
        int p = 0;
        while (p < rec.Length)
        {
            int tag = (int)Varint.ReadU32(rec, ref p);
            switch (tag)
            {
                case Comparator: e.ComparatorName = Encoding.ASCII.GetString(Varint.ReadSlice(rec, ref p)); break;
                case LogNumber: e.Log = Varint.ReadU64(rec, ref p); break;
                case PrevLogNumber: e.PrevLog = Varint.ReadU64(rec, ref p); break;
                case NextFileNumber: e.NextFile = Varint.ReadU64(rec, ref p); break;
                case LastSequence: e.LastSeq = Varint.ReadU64(rec, ref p); break;
                case CompactPointer:
                    {
                        int level = (int)Varint.ReadU32(rec, ref p);
                        e.CompactPointers.Add((level, Varint.ReadSlice(rec, ref p).ToArray()));
                        break;
                    }
                case DeletedFile:
                    {
                        int level = (int)Varint.ReadU32(rec, ref p);
                        e.Deleted.Add((level, Varint.ReadU64(rec, ref p)));
                        break;
                    }
                case NewFile:
                    {
                        var f = new FileMeta { Level = (int)Varint.ReadU32(rec, ref p) };
                        f.Number = Varint.ReadU64(rec, ref p);
                        f.Size = Varint.ReadU64(rec, ref p);
                        f.Smallest = Varint.ReadSlice(rec, ref p).ToArray();
                        f.Largest = Varint.ReadSlice(rec, ref p).ToArray();
                        e.Added.Add(f);
                        break;
                    }
                default:
                    throw new SaveFormatException($"LevelDB: unknown MANIFEST tag {tag}.");
            }
        }
        return e;
    }

    public byte[] Encode()
    {
        var o = new List<byte>();
        if (ComparatorName != null) { Varint.WriteU32(o, Comparator); Varint.WriteSlice(o, Encoding.ASCII.GetBytes(ComparatorName)); }
        if (Log is ulong log) { Varint.WriteU32(o, LogNumber); Varint.WriteU64(o, log); }
        if (PrevLog is ulong prev) { Varint.WriteU32(o, PrevLogNumber); Varint.WriteU64(o, prev); }
        if (NextFile is ulong next) { Varint.WriteU32(o, NextFileNumber); Varint.WriteU64(o, next); }
        if (LastSeq is ulong seq) { Varint.WriteU32(o, LastSequence); Varint.WriteU64(o, seq); }
        foreach (var (level, key) in CompactPointers) { Varint.WriteU32(o, CompactPointer); Varint.WriteU32(o, (uint)level); Varint.WriteSlice(o, key); }
        foreach (var (level, num) in Deleted) { Varint.WriteU32(o, DeletedFile); Varint.WriteU32(o, (uint)level); Varint.WriteU64(o, num); }
        foreach (var f in Added)
        {
            Varint.WriteU32(o, NewFile);
            Varint.WriteU32(o, (uint)f.Level);
            Varint.WriteU64(o, f.Number);
            Varint.WriteU64(o, f.Size);
            Varint.WriteSlice(o, f.Smallest);
            Varint.WriteSlice(o, f.Largest);
        }
        return o.ToArray();
    }
}

/// <summary>The database state described by a MANIFEST.</summary>
public sealed class ManifestState
{
    public string ManifestFile { get; set; } = "";
    public string Comparator { get; set; } = "leveldb.BytewiseComparator";
    public ulong LogNumber { get; set; }
    public ulong PrevLogNumber { get; set; }
    public ulong NextFileNumber { get; set; } = 2;
    public ulong LastSequence { get; set; }
    public Dictionary<ulong, FileMeta> Files { get; } = [];
    public int EditCount { get; set; }
    public int CorruptRecords { get; set; }

    public static ManifestState Load(string dbDir)
    {
        string current = Path.Combine(dbDir, "CURRENT");
        string? manifest = null;
        if (File.Exists(current))
        {
            string name = File.ReadAllText(current).Trim();
            if (name.Length > 0 && File.Exists(Path.Combine(dbDir, name))) manifest = Path.Combine(dbDir, name);
        }
        // Fall back to the newest MANIFEST if CURRENT is missing or broken.
        manifest ??= Directory.EnumerateFiles(dbDir, "MANIFEST-*")
            .Select(f => (f, ok: FileNames.TryParse(Path.GetFileName(f), out ulong n, out _), n))
            .Where(x => x.ok).OrderByDescending(x => x.n).Select(x => x.f).FirstOrDefault()
            ?? throw new SaveFormatException("LevelDB: no MANIFEST file found.");

        var state = new ManifestState { ManifestFile = manifest };
        var reader = new LogReader();
        foreach (var rec in reader.ReadRecords(File.ReadAllBytes(manifest)))
        {
            VersionEdit e;
            try { e = VersionEdit.Decode(rec); }
            catch { state.CorruptRecords++; continue; }
            state.Apply(e);
        }
        state.CorruptRecords += reader.CorruptFragments;
        return state;
    }

    public void Apply(VersionEdit e)
    {
        EditCount++;
        if (e.ComparatorName != null) Comparator = e.ComparatorName;
        if (e.Log is ulong l) LogNumber = l;
        if (e.PrevLog is ulong p) PrevLogNumber = p;
        if (e.NextFile is ulong n) NextFileNumber = n;
        if (e.LastSeq is ulong s) LastSequence = s;
        foreach (var (_, num) in e.Deleted) Files.Remove(num);
        foreach (var f in e.Added) Files[f.Number] = f;
    }
}
