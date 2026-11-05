// MinecraftStudio.Core/LevelDb/LevelDbBuilder.cs: Builds a brand-new LevelDB database with bounded memory: puts
// are buffered, spilled to sorted run files when the buffer fills, and merged at the end into non-overlapping
// tables on one level plus a MANIFEST, an empty log and CURRENT.
// Owns: LevelDbBuilder.
namespace MinecraftStudio.Core.LevelDb;

/// <summary>
/// Builds a brand-new LevelDB database with bounded memory: puts are buffered, spilled to sorted run files when the
/// buffer fills, and merged at the end into non-overlapping tables on one level plus a MANIFEST, an empty log and CURRENT.
/// A key put twice keeps the last value.
/// </summary>
public sealed class LevelDbBuilder : IDisposable
{
    readonly string _dir;
    readonly string _tempDir;
    readonly BlockCompression _compression;
    readonly long _spillBytes;
    Dictionary<byte[], byte[]> _buffer = new(ByteArrayComparer.Instance);
    long _bufferBytes;
    readonly List<string> _runs = [];
    bool _finished;

    public LevelDbBuilder(string dbDir, BlockCompression compression = BlockCompression.ZlibRaw, long spillBytes = 128L << 20)
    {
        _dir = dbDir;
        _compression = compression;
        _spillBytes = spillBytes;
        Directory.CreateDirectory(dbDir);
        if (Directory.EnumerateFileSystemEntries(dbDir).Any()) throw new IOException($"The database folder is not empty: {dbDir}");
        _tempDir = Path.Combine(dbDir, ".build-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public long RecordCount { get; private set; }

    public void Put(byte[] key, byte[] value)
    {
        if (_finished) throw new InvalidOperationException("The database has already been written.");
        if (_buffer.TryGetValue(key, out var old)) _bufferBytes -= old.Length;
        else _bufferBytes += key.Length + 48;
        _buffer[key] = value;
        _bufferBytes += value.Length;
        if (_bufferBytes >= _spillBytes) Spill();
    }

    void Spill()
    {
        if (_buffer.Count == 0) return;
        var keys = _buffer.Keys.ToList();
        keys.Sort(ByteArrayComparer.Instance);
        string path = Path.Combine(_tempDir, $"run{_runs.Count:D4}.bin");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
        using (var w = new BinaryWriter(fs))
        {
            foreach (var k in keys)
            {
                var v = _buffer[k];
                w.Write(k.Length);
                w.Write(k);
                w.Write(v.Length);
                w.Write(v);
            }
        }
        _runs.Add(path);
        _buffer = new Dictionary<byte[], byte[]>(ByteArrayComparer.Instance);
        _bufferBytes = 0;
    }

    sealed class RunReader : IDisposable
    {
        readonly BinaryReader _r;
        public RunReader(string path, int index)
        {
            _r = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20));
            Index = index;
        }
        public int Index { get; }
        public byte[] Key { get; private set; } = [];
        public byte[] Value { get; private set; } = [];
        public bool Next()
        {
            if (_r.BaseStream.Position >= _r.BaseStream.Length) return false;
            Key = _r.ReadBytes(_r.ReadInt32());
            Value = _r.ReadBytes(_r.ReadInt32());
            return true;
        }
        public void Dispose() => _r.Dispose();
    }

    /// <summary>Merges everything into tables and writes MANIFEST, log and CURRENT.</summary>
    public void Finish(IProgress<string>? progress = null)
    {
        if (_finished) return;
        _finished = true;
        Spill();
        var readers = new List<RunReader>();
        try
        {
            for (int i = 0; i < _runs.Count; i++)
            {
                var rr = new RunReader(_runs[i], i);
                if (rr.Next()) readers.Add(rr); else rr.Dispose();
            }
            // Min-heap on (key, -run): equal keys come out newest run first; older duplicates are skipped.
            var heap = new PriorityQueue<RunReader, (byte[] Key, int Run)>(Comparer<(byte[] Key, int Run)>.Create((a, b) =>
            {
                int c = a.Key.AsSpan().SequenceCompareTo(b.Key);
                return c != 0 ? c : b.Run.CompareTo(a.Run);
            }));
            foreach (var r in readers) heap.Enqueue(r, (r.Key, r.Index));

            ulong fileNumber = 2;
            var files = new List<FileMeta>();
            const long TargetSize = 2L << 20;
            const ulong Sequence = 1;
            TableBuilder? builder = null;
            ulong currentNumber = 0;
            long total = 0;
            byte[]? lastKey = null;

            void Close()
            {
                if (builder == null) return;
                builder.Finish();
                files.Add(new FileMeta { Number = currentNumber, Size = (ulong)builder.FileSize, Smallest = builder.SmallestKey!, Largest = builder.LargestKey! });
                total += builder.FileSize;
                builder.Dispose();
                builder = null;
            }

            while (heap.TryDequeue(out var reader, out _))
            {
                var key = reader.Key;
                var value = reader.Value;
                if (reader.Next()) heap.Enqueue(reader, (reader.Key, reader.Index));
                if (lastKey != null && key.AsSpan().SequenceEqual(lastKey)) continue; // older duplicate
                lastKey = key;
                if (builder == null)
                {
                    currentNumber = fileNumber++;
                    var fs = new FileStream(FileNames.Table(_dir, currentNumber), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
                    builder = new TableBuilder(fs, _compression);
                }
                builder.Add(InternalKey.Make(key, Sequence, ValueType.Value), value);
                RecordCount++;
                if (builder.FileSize >= TargetSize) Close();
                if ((RecordCount & 16383) == 0) progress?.Report($"Writing database: {RecordCount:N0} records");
            }
            Close();

            // One level that holds everything without immediate compaction (level L holds about 10^L MB).
            int level = 1;
            for (long cap = 10L << 20; level < 6 && total > cap; cap *= 10) level++;
            foreach (var f in files) f.Level = level;

            ulong logNumber = fileNumber++;
            ulong manifestNumber = fileNumber++;
            var edit = new VersionEdit
            {
                ComparatorName = "leveldb.BytewiseComparator",
                Log = logNumber,
                PrevLog = 0,
                NextFile = fileNumber,
                LastSeq = Sequence,
            };
            edit.Added.AddRange(files);
            using (var fs = new FileStream(FileNames.Manifest(_dir, manifestNumber), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new LogWriter(fs))
            {
                w.AddRecord(edit.Encode());
                fs.Flush(true);
            }
            File.WriteAllBytes(FileNames.Log(_dir, logNumber), []);
            File.WriteAllText(Path.Combine(_dir, "CURRENT"), Path.GetFileName(FileNames.Manifest(_dir, manifestNumber)) + "\n");
        }
        finally
        {
            foreach (var r in readers) r.Dispose();
            Cleanup();
        }
    }

    void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    public void Dispose()
    {
        if (!_finished) Cleanup();
    }
}
