// MinecraftStudio.Core/Java/AlphaDimension.cs: Infdev/Alpha chunk-per-file storage: base36(x mod 64)/base36(z
// mod 64)/c.base36(x).base36(z).dat.
// Owns: AlphaDimension, Base36.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Java;

/// <summary>Infdev/Alpha chunk-per-file storage: base36(x mod 64)/base36(z mod 64)/c.base36(x).base36(z).dat.</summary>
public sealed class AlphaDimension : WorldDimension
{
    readonly string _folder;
    readonly Dictionary<ChunkPos, string> _files = [];
    readonly HashSet<ChunkPos> _deleted = [];

    public AlphaDimension(string folder, string id, string name) : base(id, name)
    {
        _folder = folder;
        Scan();
    }

    public static bool HasChunks(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateDirectories(folder).Any(d => Base36.TryParse(Path.GetFileName(d), out _) && Directory.EnumerateFiles(d, "c.*.dat", SearchOption.AllDirectories).Any());

    void Scan()
    {
        foreach (var dir in Directory.EnumerateDirectories(_folder))
        {
            if (!Base36.TryParse(Path.GetFileName(dir), out _)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "c.*.dat", SearchOption.AllDirectories))
            {
                var parts = Path.GetFileName(f).Split('.');
                if (parts.Length == 4 && Base36.TryParse(parts[1], out int x) && Base36.TryParse(parts[2], out int z))
                    _files[new ChunkPos(x, z)] = f;
            }
        }
    }

    /// <summary>The dimension's folder (the chunk files sit in base-36 subfolders below it).</summary>
    public string Folder => _folder;

    public static string ChunkPath(string folder, int x, int z) =>
        Path.Combine(folder, Base36.Format(x & 63), Base36.Format(z & 63), $"c.{Base36.Format(x)}.{Base36.Format(z)}.dat");

    /// <summary>Every chunk file of the dimension.</summary>
    public IEnumerable<string> ChunkFiles => _files.Values;

    public override IReadOnlyCollection<ChunkPos> ChunkPositions => _files.Keys;
    public override bool IsModified => _deleted.Count > 0;

    public override NbtCompound? LoadChunkNbt(ChunkPos pos) =>
        _files.TryGetValue(pos, out var f) ? NbtFile.Load(f).Root as NbtCompound : null;

    public override bool CanSaveChunkNbt => true;

    public override void SaveChunkNbt(ChunkPos pos, NbtCompound nbt)
    {
        if (!_files.TryGetValue(pos, out var f)) throw new InvalidOperationException("The chunk is not stored in this dimension.");
        var file = NbtFile.Load(f);
        file.Root = nbt;
        file.Save(f);
    }

    public override ChunkColumn? LoadChunk(ChunkPos pos)
    {
        var nbt = LoadChunkNbt(pos);
        return nbt == null ? null : JavaChunk.Decode(nbt, pos.X, pos.Z);
    }

    public override ChunkInfo? GetChunkInfo(ChunkPos pos)
    {
        if (!_files.TryGetValue(pos, out var f)) return null;
        var nbt = (NbtCompound)NbtFile.Load(f).Root;
        var s = JavaChunk.Summarize(nbt);
        return new ChunkInfo { Pos = pos, LastUpdate = s.LastUpdate, InhabitedTime = s.InhabitedTime, Status = s.Status, StoredBytes = new FileInfo(f).Length, Timestamp = File.GetLastWriteTime(f) };
    }

    public override void DeleteChunks(IEnumerable<ChunkPos> chunks)
    {
        foreach (var c in chunks) if (_files.ContainsKey(c)) _deleted.Add(c);
        foreach (var c in _deleted) _files.Remove(c);
    }

    public override bool CanRestoreChunks => true;

    public override IReadOnlyList<ChunkPos> RestoreChunks(IEnumerable<ChunkPos> chunks)
    {
        var restored = new List<ChunkPos>();
        foreach (var c in chunks)
        {
            string f = ChunkPath(_folder, c.X, c.Z);
            if (!_deleted.Remove(c) || !File.Exists(f)) continue;
            _files[c] = f;
            restored.Add(c);
        }
        return restored;
    }

    public override void SaveChanges()
    {
        foreach (var c in _deleted)
        {
            string f = ChunkPath(_folder, c.X, c.Z);
            if (File.Exists(f)) File.Delete(f);
        }
        _deleted.Clear();
    }

    public override void DiscardChanges() { _deleted.Clear(); _files.Clear(); Scan(); }
}

public static class Base36
{
    public static string Format(int v)
    {
        if (v == 0) return "0";
        bool neg = v < 0;
        long n = Math.Abs((long)v);
        var chars = new Stack<char>();
        while (n > 0) { int d = (int)(n % 36); chars.Push((char)(d < 10 ? '0' + d : 'a' + d - 10)); n /= 36; }
        return (neg ? "-" : "") + new string(chars.ToArray());
    }

    public static bool TryParse(string s, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        bool neg = s[0] == '-';
        long v = 0;
        for (int i = neg ? 1 : 0; i < s.Length; i++)
        {
            char c = char.ToLowerInvariant(s[i]);
            int d = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'z' ? c - 'a' + 10 : -1;
            if (d < 0) return false;
            v = v * 36 + d;
            if (v > int.MaxValue) return false;
        }
        if (neg && s.Length == 1) return false;
        value = (int)(neg ? -v : v);
        return true;
    }
}
