// MinecraftStudio.Core/LevelDb/Table.cs: a LevelDB SSTable: the block index, reading blocks, and writing a new table.
// Owns: TableFormat, TableReader, BlockBuilder, BloomFilter, FilterBlockBuilder, TableBuilder.
using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LevelDb;

public static class TableFormat
{
    public const ulong Magic = 0xdb4775248b80fb57UL;
    public const int FooterSize = 48;
    public const int BlockTrailerSize = 5;
}

/// <summary>Reads an SSTable (.ldb / .sst) with random access.</summary>
public sealed class TableReader : IDisposable
{
    readonly SafeFileHandle _handle;

    public TableReader(string path, ulong number)
    {
        Path = path;
        Number = number;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Length = RandomAccess.GetLength(_handle);
        if (Length < TableFormat.FooterSize) throw new SaveFormatException($"{System.IO.Path.GetFileName(path)}: file is too short to be a table.");
        var footer = new byte[TableFormat.FooterSize];
        RandomAccess.Read(_handle, footer, Length - TableFormat.FooterSize);
        if (BinaryPrimitives.ReadUInt64LittleEndian(footer.AsSpan(40)) != TableFormat.Magic)
            throw new SaveFormatException($"{System.IO.Path.GetFileName(path)}: bad table magic.");
        int p = 0;
        MetaIndex = BlockHandle.Decode(footer, ref p);
        Index = BlockHandle.Decode(footer, ref p);
    }

    public string Path { get; }
    public ulong Number { get; }
    public long Length { get; }
    public BlockHandle MetaIndex { get; }
    public BlockHandle Index { get; }
    public Dictionary<BlockCompression, int> CompressionCounts { get; } = [];

    /// <summary>Reads and decompresses one block, verifying its checksum.</summary>
    public byte[] ReadBlock(BlockHandle h, bool verify = true)
    {
        if (h.Offset + h.Size + TableFormat.BlockTrailerSize > (ulong)Length) throw new SaveFormatException($"{System.IO.Path.GetFileName(Path)}: block runs past the end of the file.");
        var buf = new byte[h.Size + TableFormat.BlockTrailerSize];
        int n = RandomAccess.Read(_handle, buf, (long)h.Offset);
        if (n != buf.Length) throw new SaveFormatException($"{System.IO.Path.GetFileName(Path)}: short read.");
        var data = buf.AsSpan(0, (int)h.Size);
        byte type = buf[h.Size];
        if (verify)
        {
            uint stored = Crc32C.Unmask(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan((int)h.Size + 1)));
            uint actual = Crc32C.Compute(buf.AsSpan(0, (int)h.Size + 1));
            if (stored != actual) throw new SaveFormatException($"{System.IO.Path.GetFileName(Path)}: block checksum mismatch at {h.Offset}.");
        }
        var kind = (BlockCompression)type;
        lock (CompressionCounts) CompressionCounts[kind] = CompressionCounts.GetValueOrDefault(kind) + 1;
        return kind switch
        {
            BlockCompression.None => data.ToArray(),
            BlockCompression.Snappy => Snappy.Decompress(data),
            BlockCompression.Zlib => Compression.ZlibDecompress(data, data.Length * 4),
            BlockCompression.ZlibRaw => Compression.Inflate(data, data.Length * 4),
            _ => throw new SaveFormatException($"{System.IO.Path.GetFileName(Path)}: unknown block compression {type}."),
        };
    }

    /// <summary>Handles of all data blocks, from the index block.</summary>
    public List<BlockHandle> DataBlocks()
    {
        var list = new List<BlockHandle>();
        var index = ReadBlock(Index);
        foreach (var e in BlockEntries(index))
        {
            int p = 0;
            list.Add(BlockHandle.Decode(index.AsSpan(e.ValueOffset, e.ValueLength), ref p));
        }
        return list;
    }

    public readonly record struct Entry(byte[] Key, int ValueOffset, int ValueLength);

    /// <summary>Decodes the prefix-compressed entries of a block.</summary>
    public static IEnumerable<Entry> BlockEntries(byte[] block)
    {
        if (block.Length < 4) yield break;
        int numRestarts = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(block.Length - 4));
        int limit = block.Length - 4 - numRestarts * 4;
        if (numRestarts < 0 || limit < 0) throw new SaveFormatException("LevelDB: bad block restart array.");
        int p = 0;
        byte[] last = [];
        while (p < limit)
        {
            int shared = (int)Varint.ReadU32(block, ref p);
            int nonShared = (int)Varint.ReadU32(block, ref p);
            int valueLen = (int)Varint.ReadU32(block, ref p);
            if (shared > last.Length || p + nonShared + valueLen > limit) throw new SaveFormatException("LevelDB: bad block entry.");
            var key = new byte[shared + nonShared];
            Buffer.BlockCopy(last, 0, key, 0, shared);
            Buffer.BlockCopy(block, p, key, shared, nonShared);
            p += nonShared;
            yield return new Entry(key, p, valueLen);
            p += valueLen;
            last = key;
        }
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>Builds prefix-compressed blocks.</summary>
sealed class BlockBuilder(int restartInterval)
{
    readonly List<byte> _buf = [];
    readonly List<int> _restarts = [0];
    int _counter;
    byte[] _last = [];

    public int EstimatedSize => _buf.Count + _restarts.Count * 4 + 4;
    public bool Empty => _buf.Count == 0;

    public void Add(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        int shared = 0;
        if (_counter < restartInterval)
        {
            int min = Math.Min(_last.Length, key.Length);
            while (shared < min && _last[shared] == key[shared]) shared++;
        }
        else
        {
            _restarts.Add(_buf.Count);
            _counter = 0;
        }
        Varint.WriteU32(_buf, (uint)shared);
        Varint.WriteU32(_buf, (uint)(key.Length - shared));
        Varint.WriteU32(_buf, (uint)value.Length);
        foreach (byte b in key[shared..]) _buf.Add(b);
        foreach (byte b in value) _buf.Add(b);
        _last = key.ToArray();
        _counter++;
    }

    public byte[] Finish()
    {
        var o = new byte[_buf.Count + _restarts.Count * 4 + 4];
        _buf.CopyTo(o);
        int p = _buf.Count;
        foreach (int r in _restarts) { BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(p), r); p += 4; }
        BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(p), _restarts.Count);
        return o;
    }

    public void Reset()
    {
        _buf.Clear();
        _restarts.Clear();
        _restarts.Add(0);
        _counter = 0;
        _last = [];
    }
}

/// <summary>LevelDB's built-in bloom filter ("leveldb.BuiltinBloomFilter2"), 10 bits per key, 6 probes, as Bedrock writes.</summary>
public static class BloomFilter
{
    public const string PolicyName = "leveldb.BuiltinBloomFilter2";
    const int BitsPerKey = 10;
    const int Probes = 6;

    public static uint Hash(ReadOnlySpan<byte> data, uint seed = 0xbc9f1d34)
    {
        const uint m = 0xc6a4a793;
        uint h = seed ^ (uint)(data.Length * m);
        int i = 0;
        for (; i + 4 <= data.Length; i += 4)
        {
            h += BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
            h *= m;
            h ^= h >> 16;
        }
        switch (data.Length - i)
        {
            case 3: h += (uint)data[i + 2] << 16; goto case 2;
            case 2: h += (uint)data[i + 1] << 8; goto case 1;
            case 1: h += data[i]; h *= m; h ^= h >> 24; break;
        }
        return h;
    }

    public static void Create(List<byte[]> keys, List<byte> dst)
    {
        int bits = Math.Max(64, keys.Count * BitsPerKey);
        int bytes = (bits + 7) / 8;
        bits = bytes * 8;
        var array = new byte[bytes];
        foreach (var key in keys)
        {
            uint h = Hash(key);
            uint delta = (h >> 17) | (h << 15);
            for (int j = 0; j < Probes; j++)
            {
                uint pos = h % (uint)bits;
                array[pos / 8] |= (byte)(1 << (int)(pos % 8));
                h += delta;
            }
        }
        dst.AddRange(array);
        dst.Add(Probes);
    }
}

/// <summary>Builds the filter block: one bloom filter per 2 KiB of data-block offsets (LevelDB FilterBlockBuilder).</summary>
public sealed class FilterBlockBuilder
{
    const int BaseLg = 11;
    readonly List<byte[]> _keys = [];
    readonly List<byte> _result = [];
    readonly List<int> _offsets = [];

    public void StartBlock(long blockOffset)
    {
        long index = blockOffset >> BaseLg;
        while (index > _offsets.Count) Generate();
    }

    public void AddKey(byte[] userKey) => _keys.Add(userKey);

    void Generate()
    {
        _offsets.Add(_result.Count);
        if (_keys.Count == 0) return;
        BloomFilter.Create(_keys, _result);
        _keys.Clear();
    }

    public byte[] Finish()
    {
        if (_keys.Count > 0) Generate();
        int arrayOffset = _result.Count;
        var o = new List<byte>(_result);
        Span<byte> b4 = stackalloc byte[4];
        foreach (int off in _offsets) { BinaryPrimitives.WriteInt32LittleEndian(b4, off); o.AddRange(b4.ToArray()); }
        BinaryPrimitives.WriteInt32LittleEndian(b4, arrayOffset);
        o.AddRange(b4.ToArray());
        o.Add(BaseLg);
        return o.ToArray();
    }
}

/// <summary>Writes a table file. Keys must be added in internal-key order.</summary>
public sealed class TableBuilder : IDisposable
{
    readonly Stream _out;
    readonly BlockCompression _compression;
    readonly int _blockSize;
    readonly BlockBuilder _data = new(16);
    readonly BlockBuilder _index = new(1);
    readonly FilterBlockBuilder? _filter;
    byte[]? _lastKey;
    BlockHandle? _pendingHandle;
    byte[]? _pendingKey;
    long _offset;

    /// <param name="blockSize">Uncompressed data block target (Bedrock uses about 160 KiB).</param>
    public TableBuilder(Stream output, BlockCompression compression, int blockSize = 160 * 1024, bool bloomFilter = true)
    {
        _out = output;
        _compression = compression;
        _blockSize = blockSize;
        if (bloomFilter)
        {
            _filter = new FilterBlockBuilder();
            _filter.StartBlock(0);
        }
    }

    public int Entries { get; private set; }
    public byte[]? SmallestKey { get; private set; }
    public byte[]? LargestKey => _lastKey;
    public long FileSize => _offset;

    public void Add(byte[] internalKey, ReadOnlySpan<byte> value)
    {
        if (_lastKey != null && InternalKey.Compare(_lastKey, internalKey) >= 0)
            throw new InvalidOperationException("LevelDB: keys must be added in increasing order.");
        if (_pendingHandle is BlockHandle h)
        {
            var enc = new List<byte>();
            h.Encode(enc);
            _index.Add(_pendingKey!, enc.ToArray());
            _pendingHandle = null;
        }
        SmallestKey ??= internalKey;
        _filter?.AddKey(internalKey.AsSpan(0, internalKey.Length - 8).ToArray());
        _data.Add(internalKey, value);
        _lastKey = internalKey;
        Entries++;
        if (_data.EstimatedSize >= _blockSize) FlushData();
    }

    void FlushData()
    {
        if (_data.Empty) return;
        _pendingHandle = WriteBlock(_data.Finish(), _compression);
        _pendingKey = _lastKey;
        _data.Reset();
        _filter?.StartBlock(_offset);
    }

    BlockHandle WriteBlock(byte[] contents, BlockCompression type)
    {
        byte[] payload = type switch
        {
            BlockCompression.None => contents,
            BlockCompression.Zlib => Compression.ZlibCompress(contents),
            BlockCompression.ZlibRaw => Compression.Deflate(contents),
            _ => throw new NotSupportedException($"Writing {type} blocks is not supported."),
        };
        // Like LevelDB: keep the block uncompressed when compression saves less than 12.5%.
        if (type != BlockCompression.None && payload.Length >= contents.Length - contents.Length / 8)
        {
            payload = contents;
            type = BlockCompression.None;
        }
        var handle = new BlockHandle((ulong)_offset, (ulong)payload.Length);
        _out.Write(payload);
        Span<byte> trailer = stackalloc byte[TableFormat.BlockTrailerSize];
        trailer[0] = (byte)type;
        uint crc = Crc32C.Mask(Crc32C.Append(Crc32C.Compute(payload), [(byte)type]));
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[1..], crc);
        _out.Write(trailer);
        _offset += payload.Length + TableFormat.BlockTrailerSize;
        return handle;
    }

    public void Finish()
    {
        FlushData();
        if (_pendingHandle is BlockHandle h)
        {
            var enc = new List<byte>();
            h.Encode(enc);
            _index.Add(_pendingKey!, enc.ToArray());
            _pendingHandle = null;
        }
        var meta = new BlockBuilder(16);
        if (_filter != null)
        {
            var filterHandle = WriteBlock(_filter.Finish(), BlockCompression.None);
            var enc = new List<byte>();
            filterHandle.Encode(enc);
            meta.Add(System.Text.Encoding.ASCII.GetBytes("filter." + BloomFilter.PolicyName), enc.ToArray());
        }
        var metaHandle = WriteBlock(meta.Finish(), BlockCompression.None);
        var indexHandle = WriteBlock(_index.Finish(), _compression);
        var footer = new List<byte>(TableFormat.FooterSize);
        metaHandle.Encode(footer);
        indexHandle.Encode(footer);
        while (footer.Count < 40) footer.Add(0);
        var magic = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(magic, TableFormat.Magic);
        footer.AddRange(magic);
        _out.Write(footer.ToArray());
        _offset += footer.Count;
        _out.Flush();
    }

    public void Dispose() => _out.Dispose();
}
