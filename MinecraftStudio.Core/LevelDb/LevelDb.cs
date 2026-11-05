// MinecraftStudio.Core/LevelDb/LevelDb.cs: a Bedrock LevelDB database: open, read values on demand (LRU block cache), write and compact.
// Owns: CommitMode, LevelDatabase.
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LevelDb;

public enum CommitMode
{
    /// <summary>Append the changes to a new log file (small, fast edits).</summary>
    AppendLog,
    /// <summary>Rewrite the whole database into fresh tables (after pruning; also defragments).</summary>
    Compact,
}

/// <summary>
/// Read/write access to a LevelDB database (Mojang's fork used by Bedrock and Pocket Edition 0.9+).
/// Keys are indexed when the database is opened; values are read on demand.
/// </summary>
public sealed class LevelDatabase : IDisposable
{
    struct Ref
    {
        public ulong Seq;
        public bool Deleted;
        public int Table;          // index into _tables, or -1 when Inline holds the value
        public long BlockOffset;
        public int BlockSize;
        public int ValueOffset;
        public int ValueLength;
        public byte[]? Inline;
    }

    readonly Dictionary<byte[], Ref> _entries = new(ByteArrayComparer.Instance);
    readonly List<TableReader> _tables = [];
    readonly Dictionary<byte[], byte[]?> _pending = new(ByteArrayComparer.Instance);
    readonly Dictionary<(int, long), byte[]> _blockCache = [];
    readonly LinkedList<(int, long)> _blockLru = new();
    long _cacheBytes;
    const long CacheLimit = 96L << 20;
    readonly object _lock = new();

    LevelDatabase(string dir, ManifestState manifest)
    {
        Directory_ = dir;
        Manifest = manifest;
    }

    public string Directory_ { get; }
    public ManifestState Manifest { get; }
    public ulong MaxSequence { get; private set; }
    public int CorruptLogFragments { get; private set; }
    public int UnreadableTables { get; private set; }
    public List<string> Warnings { get; } = [];
    public Dictionary<BlockCompression, int> CompressionCounts { get; } = [];
    public BlockCompression PreferredCompression =>
        CompressionCounts.Where(kv => kv.Key != BlockCompression.None).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).DefaultIfEmpty(BlockCompression.ZlibRaw).First();

    public int Count { get { lock (_lock) return _entries.Count(e => !e.Value.Deleted && !_pending.ContainsKey(e.Key)) + _pending.Count(p => p.Value != null); } }

    /// <summary>
    /// Opened by reading every table and log file in the folder instead of following the MANIFEST (for a damaged
    /// world). Such a database is only read: the rescue writes a new world.
    /// </summary>
    public bool OpenedByScan { get; private set; }

    /// <summary>Never written (a world opened for rescue).</summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Opens a database whose CURRENT or MANIFEST is damaged or missing, the way LevelDB's own repair does: every
    /// table and every log in the folder is read, and for each key the record with the highest sequence number wins.
    /// </summary>
    public static LevelDatabase OpenScan(string dbDir, IProgress<string>? progress = null)
    {
        var manifest = new ManifestState();
        ulong highest = 0;
        foreach (var f in System.IO.Directory.EnumerateFiles(dbDir))
        {
            if (!FileNames.TryParse(Path.GetFileName(f), out ulong num, out string kind)) continue;
            highest = Math.Max(highest, num);
            if (kind == "table") manifest.Files[num] = new FileMeta { Number = num, Level = 0, Size = (ulong)new System.IO.FileInfo(f).Length };
        }
        manifest.LogNumber = 0;
        manifest.PrevLogNumber = 0;
        manifest.NextFileNumber = highest + 1;
        var db = new LevelDatabase(dbDir, manifest) { OpenedByScan = true };
        db.Load(progress);
        return db;
    }

    public static LevelDatabase Open(string dbDir, IProgress<string>? progress = null)
    {
        var manifest = ManifestState.Load(dbDir);
        var db = new LevelDatabase(dbDir, manifest);
        db.Load(progress);
        return db;
    }

    void Load(IProgress<string>? progress)
    {
        MaxSequence = Manifest.LastSequence;
        int n = 0;
        foreach (var f in Manifest.Files.Values.OrderBy(f => f.Level).ThenBy(f => f.Number))
        {
            n++;
            string path = FileNames.Table(Directory_, f.Number);
            if (!File.Exists(path)) path = FileNames.LegacyTable(Directory_, f.Number);
            if (!File.Exists(path)) { Warnings.Add($"Table {f.Number:D6} listed in the MANIFEST is missing."); UnreadableTables++; continue; }
            progress?.Report($"Reading table {n}/{Manifest.Files.Count}");
            TableReader table;
            try { table = new TableReader(path, f.Number); }
            catch (Exception ex) { Warnings.Add(ex.Message); UnreadableTables++; continue; }
            int ti = _tables.Count;
            _tables.Add(table);
            try
            {
                foreach (var h in table.DataBlocks())
                {
                    byte[] block;
                    try { block = table.ReadBlock(h); }
                    catch (Exception ex) { Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}"); continue; }
                    foreach (var e in TableReader.BlockEntries(block))
                    {
                        if (e.Key.Length < 8) continue;
                        ulong trailer = InternalKey.Trailer(e.Key);
                        var r = new Ref
                        {
                            Seq = trailer >> 8,
                            Deleted = (trailer & 0xFF) == (ulong)ValueType.Deletion,
                            Table = ti,
                            BlockOffset = (long)h.Offset,
                            BlockSize = (int)h.Size,
                            ValueOffset = e.ValueOffset,
                            ValueLength = e.ValueLength,
                        };
                        Merge(e.Key.AsSpan(0, e.Key.Length - 8).ToArray(), r);
                    }
                }
                foreach (var kv in table.CompressionCounts) CompressionCounts[kv.Key] = CompressionCounts.GetValueOrDefault(kv.Key) + kv.Value;
            }
            catch (Exception ex) { Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}"); UnreadableTables++; }
        }

        // Logs newer than the MANIFEST's log number hold the most recent writes.
        var logs = System.IO.Directory.EnumerateFiles(Directory_, "*.log")
            .Select(p => (p, ok: FileNames.TryParse(Path.GetFileName(p), out ulong num, out string kind) && kind == "log", num))
            .Where(x => x.ok && (x.num >= Manifest.LogNumber || x.num == Manifest.PrevLogNumber))
            .OrderBy(x => x.num).ToList();
        foreach (var (path, _, num) in logs)
        {
            progress?.Report($"Replaying log {num:D6}");
            var reader = new LogReader();
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception ex) { Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}"); continue; }
            foreach (var rec in reader.ReadRecords(data))
            {
                WriteBatch batch;
                try { batch = WriteBatch.Parse(rec); }
                catch (Exception ex) { Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}"); continue; }
                ulong seq = batch.Sequence;
                foreach (var (key, value) in batch.Ops)
                {
                    Merge(key, new Ref { Seq = seq, Deleted = value == null, Table = -1, Inline = value });
                    seq++;
                }
            }
            CorruptLogFragments += reader.CorruptFragments;
        }
    }

    void Merge(byte[] userKey, Ref r)
    {
        if (r.Seq > MaxSequence) MaxSequence = r.Seq;
        if (_entries.TryGetValue(userKey, out var existing) && existing.Seq > r.Seq) return;
        _entries[userKey] = r;
    }

    /// <summary>Live keys (including pending puts, excluding pending deletes).</summary>
    public List<byte[]> Keys()
    {
        lock (_lock)
        {
            var keys = new List<byte[]>(_entries.Count);
            foreach (var kv in _entries)
                if (!kv.Value.Deleted && !_pending.ContainsKey(kv.Key)) keys.Add(kv.Key);
            foreach (var kv in _pending) if (kv.Value != null) keys.Add(kv.Key);
            return keys;
        }
    }

    public bool ContainsKey(byte[] key)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(key, out var p)) return p != null;
            return _entries.TryGetValue(key, out var r) && !r.Deleted;
        }
    }

    public byte[]? Get(byte[] key)
    {
        Ref r;
        lock (_lock)
        {
            if (_pending.TryGetValue(key, out var p)) return p;
            if (!_entries.TryGetValue(key, out r) || r.Deleted) return null;
            if (r.Inline != null) return r.Inline;
        }
        byte[] block = GetBlock(r.Table, r.BlockOffset, r.BlockSize);
        return block.AsSpan(r.ValueOffset, r.ValueLength).ToArray();
    }

    /// <summary>Stored (compressed) size of a key's value, for statistics.</summary>
    public int ValueLength(byte[] key)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(key, out var p)) return p?.Length ?? 0;
            return _entries.TryGetValue(key, out var r) && !r.Deleted ? (r.Inline?.Length ?? r.ValueLength) : 0;
        }
    }

    byte[] GetBlock(int table, long offset, int size)
    {
        lock (_lock)
        {
            if (_blockCache.TryGetValue((table, offset), out var cached))
            {
                _blockLru.Remove((table, offset));
                _blockLru.AddFirst((table, offset));
                return cached;
            }
        }
        var block = _tables[table].ReadBlock(new BlockHandle((ulong)offset, (ulong)size), verify: false);
        lock (_lock)
        {
            if (!_blockCache.ContainsKey((table, offset)))
            {
                _blockCache[(table, offset)] = block;
                _blockLru.AddFirst((table, offset));
                _cacheBytes += block.Length;
                while (_cacheBytes > CacheLimit && _blockLru.Last != null)
                {
                    var last = _blockLru.Last.Value;
                    _blockLru.RemoveLast();
                    if (_blockCache.Remove(last, out var b)) _cacheBytes -= b.Length;
                }
            }
        }
        return block;
    }

    public void Put(byte[] key, byte[] value) { lock (_lock) _pending[key] = value; }
    public void Delete(byte[] key) { lock (_lock) { if (ContainsKey(key)) _pending[key] = null; } }
    /// <summary>Takes back a delete that has not been committed yet. False when the key had no pending delete.</summary>
    public bool Undelete(byte[] key) { lock (_lock) return _pending.TryGetValue(key, out var v) && v == null && _pending.Remove(key); }
    public bool HasChanges { get { lock (_lock) return _pending.Count > 0; } }
    public int PendingCount { get { lock (_lock) return _pending.Count; } }
    public void DiscardChanges() { lock (_lock) _pending.Clear(); }

    /// <summary>True if another process (the game) holds the database lock.</summary>
    public static bool IsLocked(string dbDir)
    {
        string lockFile = Path.Combine(dbDir, "LOCK");
        if (!File.Exists(lockFile)) return false;
        try
        {
            using var fs = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    IEnumerable<ulong> UsedFileNumbers()
    {
        foreach (var f in System.IO.Directory.EnumerateFiles(Directory_))
            if (FileNames.TryParse(Path.GetFileName(f), out ulong n, out _)) yield return n;
    }

    ulong NextFileNumber() => Math.Max(Manifest.NextFileNumber, UsedFileNumbers().DefaultIfEmpty(0UL).Max() + 1);

    public void Commit(CommitMode mode, IProgress<string>? progress = null)
    {
        if (OpenedByScan || ReadOnly) throw new InvalidOperationException("This world was opened for rescue, so it is only read and never changed. Rescue it into a new world to keep working on it.");
        if (!HasChanges && mode == CommitMode.AppendLog) return;
        if (IsLocked(Directory_)) throw new IOException("The world is open in Minecraft (the database is locked). Close the game and try again.");
        if (mode == CommitMode.AppendLog) CommitToLog();
        else CommitCompact(progress);
    }

    void CommitToLog()
    {
        ulong number = NextFileNumber();
        var batch = new WriteBatch { Sequence = MaxSequence + 1 };
        lock (_lock) foreach (var kv in _pending) batch.Ops.Add((kv.Key, kv.Value));
        string path = FileNames.Log(Directory_, number);
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var w = new LogWriter(fs))
        {
            w.AddRecord(batch.Encode());
            w.Flush();
            fs.Flush(true);
        }
        lock (_lock)
        {
            ulong seq = batch.Sequence;
            foreach (var (key, value) in batch.Ops)
                _entries[key] = new Ref { Seq = seq++, Deleted = value == null, Table = -1, Inline = value };
            MaxSequence = seq - 1;
            _pending.Clear();
        }
    }

    /// <summary>Writes every live entry into new tables, a new MANIFEST and CURRENT, then removes the old files.</summary>
    void CommitCompact(IProgress<string>? progress)
    {
        var keys = Keys();
        keys.Sort(ByteArrayComparer.Instance);
        ulong fileNum = NextFileNumber();
        var compression = PreferredCompression;
        var newFiles = new List<FileMeta>();
        var createdPaths = new List<string>();
        ulong seq = MaxSequence + 1;
        const long TargetSize = 2L << 20;
        TableBuilder? builder = null;
        FileStream? stream = null;
        string? currentPath = null;
        ulong currentNumber = 0;
        long total = 0;

        void Close()
        {
            if (builder == null) return;
            builder.Finish();
            var meta = new FileMeta { Number = currentNumber, Size = (ulong)builder.FileSize, Smallest = builder.SmallestKey!, Largest = builder.LargestKey! };
            total += builder.FileSize;
            stream!.Flush(true);
            builder.Dispose();
            newFiles.Add(meta);
            builder = null;
            stream = null;
        }

        try
        {
            int i = 0;
            foreach (var key in keys)
            {
                byte[]? value = Get(key);
                if (value == null) continue;
                ulong s;
                lock (_lock) s = _pending.ContainsKey(key) || !_entries.TryGetValue(key, out var r) ? seq : r.Seq;
                if (builder == null)
                {
                    currentNumber = fileNum++;
                    currentPath = FileNames.Table(Directory_, currentNumber);
                    stream = new FileStream(currentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    createdPaths.Add(currentPath);
                    builder = new TableBuilder(stream, compression);
                }
                builder.Add(InternalKey.Make(key, s, ValueType.Value), value);
                if (builder.FileSize >= TargetSize) Close();
                if ((++i & 4095) == 0) progress?.Report($"Writing {i:N0}/{keys.Count:N0} records");
            }
            Close();

            // Place all files on one level that can hold them without triggering compaction.
            int level = 1;
            for (long cap = 10L << 20; level < 6 && total > cap; cap *= 10) level++;
            foreach (var f in newFiles) f.Level = level;

            ulong logNumber = fileNum++;
            ulong manifestNumber = fileNum++;
            var edit = new VersionEdit
            {
                ComparatorName = Manifest.Comparator,
                Log = logNumber,
                PrevLog = 0,
                NextFile = fileNum,
                LastSeq = Math.Max(MaxSequence, seq),
            };
            edit.Added.AddRange(newFiles);
            string manifestPath = FileNames.Manifest(Directory_, manifestNumber);
            createdPaths.Add(manifestPath);
            using (var fs = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new LogWriter(fs))
            {
                w.AddRecord(edit.Encode());
                fs.Flush(true);
            }
            string logPath = FileNames.Log(Directory_, logNumber);
            createdPaths.Add(logPath);
            File.WriteAllBytes(logPath, []);

            // Switch CURRENT atomically.
            string tmp = Path.Combine(Directory_, $"{manifestNumber:D6}.dbtmp");
            File.WriteAllText(tmp, Path.GetFileName(manifestPath) + "\n");
            string current = Path.Combine(Directory_, "CURRENT");
            if (File.Exists(current)) File.Replace(tmp, current, null);
            else File.Move(tmp, current);
        }
        catch
        {
            builder?.Dispose();
            foreach (var p in createdPaths) try { File.Delete(p); } catch { }
            throw;
        }

        // Old files are no longer referenced.
        foreach (var t in _tables) t.Dispose();
        var keep = new HashSet<string>(createdPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var f in System.IO.Directory.EnumerateFiles(Directory_))
        {
            string name = Path.GetFileName(f);
            if (keep.Contains(f) || !FileNames.TryParse(name, out _, out _)) continue;
            try { File.Delete(f); } catch (Exception ex) { Warnings.Add($"Could not delete old file {name}: {ex.Message}"); }
        }
        // Reload the fresh state.
        lock (_lock)
        {
            _tables.Clear();
            _entries.Clear();
            _pending.Clear();
            _blockCache.Clear();
            _blockLru.Clear();
            _cacheBytes = 0;
        }
        var fresh = ManifestState.Load(Directory_);
        Manifest.Files.Clear();
        foreach (var kv in fresh.Files) Manifest.Files[kv.Key] = kv.Value;
        Manifest.LogNumber = fresh.LogNumber;
        Manifest.PrevLogNumber = fresh.PrevLogNumber;
        Manifest.NextFileNumber = fresh.NextFileNumber;
        Manifest.LastSequence = fresh.LastSequence;
        Manifest.ManifestFile = fresh.ManifestFile;
        CompressionCounts.Clear();
        Load(progress);
    }

    public void Dispose()
    {
        foreach (var t in _tables) t.Dispose();
        _tables.Clear();
    }
}
