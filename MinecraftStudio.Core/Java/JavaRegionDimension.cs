// MinecraftStudio.Core/Java/JavaRegionDimension.cs: A dimension stored in region files (region/, plus entities/
// and poi/ since 1.14/1.17).
// Owns: JavaRegionDimension.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Java;

/// <summary>A dimension stored in region files (region/, plus entities/ and poi/ since 1.14/1.17).</summary>
public sealed class JavaRegionDimension : WorldDimension
{
    readonly string _folder;
    readonly string _ext;
    readonly Dictionary<ChunkPos, (string File, int Index)> _index = [];
    readonly Dictionary<string, RegionFile> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly LinkedList<string> _lru = new();
    readonly Dictionary<string, HashSet<int>> _pendingDeletes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ChunkPos, (string File, int Index)> _deletedLocations = [];
    const int CacheSize = 12;

    public JavaRegionDimension(string folder, string id, string name, string extension) : base(id, name)
    {
        _folder = folder;
        _ext = extension;
        BuildIndex();
    }

    public string Folder => _folder;
    public string RegionFolder => Path.Combine(_folder, "region");
    public string Extension => _ext;
    public override IReadOnlyCollection<ChunkPos> ChunkPositions => _index.Keys;
    public override bool IsModified => _pendingDeletes.Count > 0 || _cache.Values.Any(r => r.IsModified);

    void BuildIndex()
    {
        _index.Clear();
        foreach (var f in Directory.EnumerateFiles(RegionFolder, "*" + _ext))
        {
            if (!RegionFile.TryParseName(Path.GetFileName(f), out int rx, out int rz)) continue;
            var header = new byte[4096];
            int read;
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                read = fs.Read(header, 0, header.Length);
            for (int i = 0; i < 1024 && i * 4 + 4 <= read; i++)
            {
                uint loc = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(i * 4));
                if (loc == 0 || (loc >> 8) == 0) continue;
                var pos = new ChunkPos(rx * 32 + (i & 31), rz * 32 + (i >> 5));
                _index[pos] = (f, i);
            }
        }
    }

    /// <summary>Chunks the rescue read from another copy than where the region header points (read from there on demand).</summary>
    readonly Dictionary<ChunkPos, ChunkCopy> _rescued = [];
    readonly Dictionary<string, RegionRecovery> _recoveries = new(StringComparer.OrdinalIgnoreCase);
    bool _rescueMode;

    /// <summary>What the rescue found in each region file (by path), for writing a repaired copy without searching again.</summary>
    public IReadOnlyDictionary<string, RegionRecovery> Recoveries => _recoveries;

    /// <summary>Chunks the rescue found no readable copy of (they stay listed, so a conversion reports them as lost).</summary>
    public int LostChunks { get; private set; }

    /// <summary>
    /// Rescue: searches every region file for chunks its header no longer lists and for older copies of chunks whose
    /// data is broken, and keeps what it finds; chunks with no readable copy stay listed (and fail to load). Region
    /// files are read even where the disk cannot read them. Returns what it found, per file.
    /// </summary>
    public List<string> Rescue(CancellationToken cancel = default, IProgress<string>? progress = null)
    {
        _rescueMode = true;
        var findings = new List<string>();
        var files = Directory.EnumerateFiles(RegionFolder, "*" + _ext).ToList();
        for (int n = 0; n < files.Count; n++)
        {
            string f = files[n];
            cancel.ThrowIfCancellationRequested();
            if (!RegionFile.TryParseName(Path.GetFileName(f), out int rx, out int rz)) continue;
            progress?.Report($"Searching {DisplayName} for chunks: region file {n + 1:N0} of {files.Count:N0}");
            string where = $"{DisplayName}, {Path.GetFileName(f)}";
            RegionFile region;
            int unreadable;
            try { region = RegionFile.OpenTolerant(f, out unreadable); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { findings.Add($"{where}: could not be read ({e.Message})."); continue; }
            if (unreadable > 0) findings.Add($"{where}: {unreadable * 4:N0} KB of the file could not be read from the disk.");
            var stamp = RegionRecovery.StampOf(f);
            var rec = region.Recover(RegionContent.Terrain, cancel);
            rec.Stamp = stamp;
            _recoveries[f] = rec;
            LostChunks += rec.Lost;
            foreach (var (i, copy) in rec.Chunks)
            {
                if (copy.IsListed) continue;
                var pos = new ChunkPos(rx * 32 + (i & 31), rz * 32 + (i >> 5));
                _rescued[pos] = copy;
                _index[pos] = (f, i);
            }
            if (rec.Unlisted > 0) findings.Add($"{where}: {World.WorldRescue.Count(rec.Unlisted, "chunk the file's header had lost was", "chunks the file's header had lost were")} found and kept.");
            if (rec.Older > 0) findings.Add($"{where}: {World.WorldRescue.Count(rec.Older, "damaged chunk was replaced by an older copy", "damaged chunks were replaced by older copies")} still in the file; the latest changes there may be missing.");
            if (rec.Lost > 0) findings.Add($"{where}: {World.WorldRescue.Count(rec.Lost, "damaged chunk has", "damaged chunks have")} no readable copy and cannot be rescued.");
            if (rec.Chunks.Count == 0 && rec.Lost == 0 && region.FileLength > 2 * RegionFile.SectorSize)
                findings.Add($"{where}: no readable chunk was found in the file (it may have been emptied or overwritten).");
        }
        return findings;
    }

    public RegionFile GetRegion(string file)
    {
        if (_cache.TryGetValue(file, out var r))
        {
            _lru.Remove(file);
            _lru.AddFirst(file);
            return r;
        }
        r = _rescueMode ? RegionFile.OpenTolerant(file, out _) : RegionFile.Open(file);
        _cache[file] = r;
        _lru.AddFirst(file);
        while (_lru.Count > CacheSize)
        {
            string last = _lru.Last!.Value;
            if (_cache[last].IsModified) break; // keep edited regions until saved
            _lru.RemoveLast();
            _cache.Remove(last);
        }
        return r;
    }

    public IEnumerable<string> RegionFiles => _index.Values.Select(v => v.File).Distinct(StringComparer.OrdinalIgnoreCase);

    public override NbtCompound? LoadChunkNbt(ChunkPos pos)
    {
        if (!_index.TryGetValue(pos, out var loc)) return null;
        if (_rescued.TryGetValue(pos, out var copy)) return NbtBinary.ReadCompound(GetRegion(loc.File).ReadCopy(loc.Index, copy).Decompress(), NbtFormat.Java);
        return GetRegion(loc.File).GetChunkNbt(loc.Index);
    }

    public override ChunkColumn? LoadChunk(ChunkPos pos)
    {
        var nbt = LoadChunkNbt(pos);
        return nbt == null ? null : JavaChunk.Decode(nbt, pos.X, pos.Z);
    }

    public override bool CanSaveChunkNbt => true;

    public override void SaveChunkNbt(ChunkPos pos, NbtCompound nbt)
    {
        if (_rescueMode) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        if (!_index.TryGetValue(pos, out var loc)) throw new InvalidOperationException("The chunk is not stored in this dimension.");
        var region = GetRegion(loc.File);
        region.SetChunk(loc.Index, nbt);
        region.Save();
    }

    /// <summary>Entity data for a chunk from entities/ (1.17+), or null. A rescue reads it from any readable copy.</summary>
    public NbtCompound? LoadEntitiesNbt(ChunkPos pos)
    {
        string file = Path.Combine(_folder, "entities", $"r.{pos.RegionX}.{pos.RegionZ}.mca");
        if (!File.Exists(file)) return null;
        int index = RegionFile.Index(pos.X & 31, pos.Z & 31);
        if (!_rescueMode) return RegionFile.Open(file).GetChunkNbt(index);
        if (!_recoveries.TryGetValue(file, out var rec))
        {
            var stamp = RegionRecovery.StampOf(file);
            _recoveries[file] = rec = GetRegion(file).Recover(RegionContent.Entities);
            rec.Stamp = stamp;
        }
        var region = GetRegion(file);
        return rec.Chunks.TryGetValue(index, out var copy) ? NbtBinary.ReadCompound(region.ReadCopy(index, copy).Decompress(), NbtFormat.Java) : null;
    }

    public override ChunkInfo? GetChunkInfo(ChunkPos pos)
    {
        if (!_index.TryGetValue(pos, out var loc)) return null;
        var region = GetRegion(loc.File);
        var raw = _rescued.TryGetValue(pos, out var copy) ? region.ReadCopy(loc.Index, copy) : region.GetRaw(loc.Index);
        if (raw == null) return null;
        var nbt = NbtBinary.ReadCompound(raw.Decompress(), NbtFormat.Java);
        var s = JavaChunk.Summarize(nbt);
        return new ChunkInfo
        {
            Pos = pos,
            InhabitedTime = s.InhabitedTime,
            LastUpdate = s.LastUpdate,
            Status = s.Status,
            DataVersion = s.DataVersion ?? 0,
            StoredBytes = raw.Payload.Length,
            Timestamp = raw.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(raw.Timestamp).LocalDateTime : null,
        };
    }

    public override void DeleteChunks(IEnumerable<ChunkPos> chunks)
    {
        foreach (var pos in chunks)
        {
            if (!_index.Remove(pos, out var loc)) continue;
            if (!_pendingDeletes.TryGetValue(loc.File, out var set)) _pendingDeletes[loc.File] = set = [];
            set.Add(loc.Index);
            _deletedLocations[pos] = loc;
        }
    }

    public override bool CanRestoreChunks => true;

    public override IReadOnlyList<ChunkPos> RestoreChunks(IEnumerable<ChunkPos> chunks)
    {
        var restored = new List<ChunkPos>();
        foreach (var pos in chunks)
        {
            if (!_deletedLocations.TryGetValue(pos, out var loc) || !_pendingDeletes.TryGetValue(loc.File, out var set) || !set.Remove(loc.Index)) continue;
            if (set.Count == 0) _pendingDeletes.Remove(loc.File);
            _deletedLocations.Remove(pos);
            _index[pos] = loc;
            restored.Add(pos);
        }
        return restored;
    }

    public override void DiscardChanges()
    {
        _deletedLocations.Clear();
        _pendingDeletes.Clear();
        foreach (var r in _cache.Values) r.DiscardChanges();
        BuildIndex();
    }

    public override void SaveChanges()
    {
        if (_rescueMode) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        foreach (var r in _cache.Values.Where(r => r.IsModified && !_pendingDeletes.ContainsKey(r.FilePath!)).ToList()) r.Save();
        foreach (var (file, indexes) in _pendingDeletes)
        {
            var region = GetRegion(file);
            foreach (int i in indexes) region.DeleteChunk(i);
            bool kept = region.Save();
            if (!kept) { _cache.Remove(file); _lru.Remove(file); }
            // Same chunks in entities/ and poi/ (1.14+/1.17+)
            foreach (var sub in new[] { "entities", "poi" })
            {
                string other = Path.Combine(_folder, sub, Path.GetFileName(file));
                if (!File.Exists(other)) continue;
                var r = RegionFile.Open(other);
                foreach (int i in indexes) if (r.HasChunk(i)) r.DeleteChunk(i);
                if (r.IsModified) r.Save();
            }
        }
        _pendingDeletes.Clear();
    }
}
