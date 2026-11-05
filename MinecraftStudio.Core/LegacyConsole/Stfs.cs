// MinecraftStudio.Core/LegacyConsole/Stfs.cs: Xbox 360 STFS package (CON/LIVE/PIRS): reads files with hash
// verification, and rebuilds a package with new file contents (block layout, hash tables, top hash and header
// hash).
// Owns: StfsPackage.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

/// <summary>
/// Xbox 360 STFS package (CON/LIVE/PIRS): reads files with hash verification, and rebuilds a package with new
/// file contents (block layout, hash tables, top hash and header hash). The package signature is kept as-is;
/// retail consoles additionally need the package re-signed by a resigning tool.
/// </summary>
public sealed class StfsPackage
{
    readonly byte[] _d;
    readonly int _sex;
    readonly int[] _step;

    public sealed class Entry
    {
        public string Name { get; init; } = "";
        public byte Flags { get; init; }
        public bool IsDirectory => (Flags & 0x80) != 0;
        public bool Consecutive => (Flags & 0x40) != 0;
        public int Blocks { get; init; }
        public int StartBlock { get; init; }
        public short Parent { get; init; }
        public int Size { get; init; }
        public uint UpdateTime { get; init; }
        public uint AccessTime { get; init; }
    }

    public StfsPackage(byte[] data)
    {
        _d = data;
        Magic = Encoding.ASCII.GetString(data, 0, 4);
        if (Magic is not ("CON " or "LIVE" or "PIRS")) throw new SaveFormatException("Not an STFS package.");
        HeaderSize = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0x340));
        ContentType = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x344));
        TitleId = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x360));
        ProfileId = Convert.ToHexString(data, 0x371, 8);
        const int vd = 0x379;
        BlockSeparation = data[vd + 2];
        FileTableBlockCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(vd + 3));
        FileTableBlockNumber = I24Le(data, vd + 5);
        AllocatedBlocks = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(vd + 0x1C));
        UnallocatedBlocks = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(vd + 0x20));
        _sex = (~BlockSeparation) & 1;
        _step = _sex == 0 ? [0xAB, 0x718F] : [0xAC, 0x723A];
        FirstTable = (HeaderSize + 0xFFF) & ~0xFFF;
        TopLevel = AllocatedBlocks <= 0xAA ? 0 : AllocatedBlocks <= 0x70E4 ? 1 : 2;
        TopTableOffset = (HashBlock(0, TopLevel) << 12) + FirstTable + ((BlockSeparation & 2) << 11);
        DisplayName = Utf16Be(data, 0x411, 0x80);
        TitleName = Utf16Be(data, 0x1691, 0x80);
    }

    public string Magic { get; }
    public int HeaderSize { get; }
    public uint ContentType { get; }
    public uint TitleId { get; }
    public string ProfileId { get; }
    public byte BlockSeparation { get; }
    public int FileTableBlockCount { get; }
    public int FileTableBlockNumber { get; }
    public int AllocatedBlocks { get; }
    public int UnallocatedBlocks { get; }
    public int FirstTable { get; }
    public int TopLevel { get; }
    public int TopTableOffset { get; }
    public string DisplayName { get; }
    public string TitleName { get; }
    public byte[] Raw => _d;

    public byte[] Thumbnail
    {
        get
        {
            int size = BinaryPrimitives.ReadInt32BigEndian(_d.AsSpan(0x1712));
            return size is > 0 and <= 0x3D00 ? _d.AsSpan(0x171A, size).ToArray() : [];
        }
    }

    static int I24Le(byte[] b, int p) => b[p] | (b[p + 1] << 8) | (b[p + 2] << 16);
    static int I24Be(byte[] b, int p) => (b[p] << 16) | (b[p + 1] << 8) | b[p + 2];
    static string Utf16Be(byte[] b, int at, int len) => Encoding.BigEndianUnicode.GetString(b, at, len).Split('\0')[0];

    // ------------------------------------------------------------------ block math (Free60 / Velocity)

    static int BackingDataBlock(int n, int sex)
    {
        int r = (((n + 0xAA) / 0xAA) << sex) + n;
        if (n < 0xAA) return r;
        if (n < 0x70E4) return r + (((n + 0x70E4) / 0x70E4) << sex);
        return (1 << sex) + r + (((n + 0x70E4) / 0x70E4) << sex);
    }

    static int HashBlock(int n, int level, int sex, int[] step)
    {
        if (level == 0)
        {
            if (n < 0xAA) return 0;
            int num = (n / 0xAA) * step[0] + (((n / 0x70E4) + 1) << sex);
            return n / 0x70E4 == 0 ? num : num + (1 << sex);
        }
        if (level == 1) return n < 0x70E4 ? step[0] : (1 << sex) + (n / 0x70E4) * step[1];
        return step[1];
    }

    int HashBlock(int n, int level) => HashBlock(n, level, _sex, _step);
    int BlockOffset(int n) => (BackingDataBlock(n, _sex) << 12) + FirstTable;

    int HashEntryOffset(int n)
    {
        int addr = (HashBlock(n, 0) << 12) + FirstTable + (n % 0xAA) * 0x18;
        if (TopLevel == 0) addr += (BlockSeparation & 2) << 11;
        else if (TopLevel == 1) addr += (_d[TopTableOffset + (n / 0xAA) * 0x18 + 0x14] & 0x40) << 6;
        else
        {
            int st2 = _d[TopTableOffset + (n / 0x70E4) * 0x18 + 0x14];
            int l1 = (HashBlock(n, 1) << 12) + FirstTable + ((st2 & 0x40) << 6);
            int st1 = _d[l1 + ((n / 0xAA) % 0xAA) * 0x18 + 0x14];
            addr += (st1 & 0x40) << 6;
        }
        return addr;
    }

    (byte[] Hash, int Status, int Next) HashEntry(int n)
    {
        int a = HashEntryOffset(n);
        return (_d.AsSpan(a, 0x14).ToArray(), _d[a + 0x14], I24Be(_d, a + 0x15));
    }

    ReadOnlySpan<byte> Block(int n)
    {
        int a = BlockOffset(n);
        if (a + 0x1000 > _d.Length) throw new SaveFormatException($"STFS block {n} lies outside the package.");
        return _d.AsSpan(a, 0x1000);
    }

    public List<Entry> Files()
    {
        var list = new List<Entry>();
        int blk = FileTableBlockNumber;
        for (int i = 0; i < FileTableBlockCount; i++)
        {
            var data = Block(blk);
            for (int k = 0; k < 0x40; k++)
            {
                var e = data.Slice(k * 0x40, 0x40);
                if (e[0] == 0) continue;
                byte flags = e[0x28];
                list.Add(new Entry
                {
                    Name = Encoding.ASCII.GetString(e[..(flags & 0x3F)]),
                    Flags = flags,
                    Blocks = e[0x29] | (e[0x2A] << 8) | (e[0x2B] << 16),
                    StartBlock = e[0x2F] | (e[0x30] << 8) | (e[0x31] << 16),
                    Parent = BinaryPrimitives.ReadInt16BigEndian(e[0x32..]),
                    Size = BinaryPrimitives.ReadInt32BigEndian(e[0x34..]),
                    UpdateTime = BinaryPrimitives.ReadUInt32BigEndian(e[0x38..]),
                    AccessTime = BinaryPrimitives.ReadUInt32BigEndian(e[0x3C..]),
                });
            }
            blk = HashEntry(blk).Next;
        }
        return list;
    }

    /// <summary>Extracts a file; <paramref name="badBlocks"/> counts blocks whose SHA-1 does not match.</summary>
    public byte[] Extract(Entry f, out int badBlocks)
    {
        var w = new ByteWriter(f.Blocks * 0x1000);
        badBlocks = 0;
        int blk = f.StartBlock;
        for (int i = 0; i < f.Blocks; i++)
        {
            var data = Block(blk);
            var (hash, _, next) = HashEntry(blk);
            if (!SHA1.HashData(data).AsSpan().SequenceEqual(hash)) badBlocks++;
            w.Bytes(data);
            blk = f.Consecutive ? blk + 1 : next;
        }
        return w.WrittenSpan[..Math.Min(f.Size, w.Length)].ToArray();
    }

    public bool HeaderHashValid => SHA1.HashData(_d.AsSpan(0x344, FirstTable - 0x344)).AsSpan().SequenceEqual(_d.AsSpan(0x32C, 0x14));

    public bool TopHashValid => TopTableOffset + 0x1000 <= _d.Length && SHA1.HashData(_d.AsSpan(TopTableOffset, 0x1000)).AsSpan().SequenceEqual(_d.AsSpan(0x381, 0x14));

    // ------------------------------------------------------------------ rebuilding

    /// <summary>
    /// Builds a new package with the same header (certificate, IDs, names, thumbnails) and the given root files.
    /// Data blocks are laid out consecutively and every hash table, the top hash and the header hash are recomputed.
    /// </summary>
    public byte[] Rebuild(IReadOnlyList<(string Name, byte[] Data, uint UpdateTime, uint AccessTime)> files)
    {
        const int sex = 1;                // blockSeparation 0 = two copies of every table, copy 0 active
        int[] step = [0xAC, 0x723A];
        int firstTable = FirstTable;
        if (files.Count > 64) throw new SaveFormatException("The rebuilder writes at most 64 files.");

        // Data block numbers: 0 = file table, then each file consecutively.
        var starts = new List<int>();
        int next = 1;
        foreach (var f in files)
        {
            starts.Add(next);
            next += Math.Max(1, (f.Data.Length + 0xFFF) / 0x1000);
        }
        int alloc = next;
        int topLevel = alloc <= 0xAA ? 0 : alloc <= 0x70E4 ? 1 : 2;
        if (topLevel == 2) throw new SaveFormatException("Saves larger than about 113 MB are not supported by the rebuilder.");

        // Highest backing block used (data blocks and hash tables).
        int maxBacking = BackingDataBlock(alloc - 1, sex);
        int groups = (alloc + 0xA9) / 0xAA;
        for (int g = 0; g < groups; g++) maxBacking = Math.Max(maxBacking, HashBlock(g * 0xAA, 0, sex, step) + 1);
        if (topLevel == 1) maxBacking = Math.Max(maxBacking, HashBlock(0, 1, sex, step) + 1);
        var pkg = new byte[firstTable + (maxBacking + 1) * 0x1000];
        _d.AsSpan(0, firstTable).CopyTo(pkg);

        int DataOffset(int n) => (BackingDataBlock(n, sex) << 12) + firstTable;
        int L0Offset(int n) => (HashBlock(n, 0, sex, step) << 12) + firstTable;

        // File table block.
        var table = new byte[0x1000];
        for (int i = 0; i < files.Count; i++)
        {
            var e = table.AsSpan(i * 0x40, 0x40);
            var nameBytes = Encoding.ASCII.GetBytes(files[i].Name);
            if (nameBytes.Length > 0x28) throw new SaveFormatException($"File name {files[i].Name} is too long for STFS.");
            nameBytes.CopyTo(e);
            int blocks = Math.Max(1, (files[i].Data.Length + 0xFFF) / 0x1000);
            e[0x28] = (byte)(0x40 | nameBytes.Length);
            WriteI24Le(e[0x29..], blocks);
            WriteI24Le(e[0x2C..], blocks);
            WriteI24Le(e[0x2F..], starts[i]);
            BinaryPrimitives.WriteInt16BigEndian(e[0x32..], -1);
            BinaryPrimitives.WriteInt32BigEndian(e[0x34..], files[i].Data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(e[0x38..], files[i].UpdateTime);
            BinaryPrimitives.WriteUInt32BigEndian(e[0x3C..], files[i].AccessTime);
        }
        table.CopyTo(pkg, DataOffset(0));
        for (int i = 0; i < files.Count; i++)
        {
            int blocks = Math.Max(1, (files[i].Data.Length + 0xFFF) / 0x1000);
            for (int b = 0; b < blocks; b++)
            {
                int len = Math.Min(0x1000, files[i].Data.Length - b * 0x1000);
                if (len > 0) files[i].Data.AsSpan(b * 0x1000, len).CopyTo(pkg.AsSpan(DataOffset(starts[i] + b)));
            }
        }

        // Level-0 hash tables (both copies identical).
        for (int n = 0; n < alloc; n++)
        {
            int at = L0Offset(n) + (n % 0xAA) * 0x18;
            SHA1.HashData(pkg.AsSpan(DataOffset(n), 0x1000)).CopyTo(pkg, at);
            pkg[at + 0x14] = 0x80;
            int chainNext = 0xFFFFFF;
            for (int i = 0; i < files.Count; i++)
            {
                int blocks = Math.Max(1, (files[i].Data.Length + 0xFFF) / 0x1000);
                if (n >= starts[i] && n < starts[i] + blocks - 1) chainNext = n + 1;
            }
            WriteI24Be(pkg.AsSpan(at + 0x15), chainNext);
        }
        for (int g = 0; g < groups; g++)
        {
            int at = L0Offset(g * 0xAA);
            pkg.AsSpan(at, 0x1000).CopyTo(pkg.AsSpan(at + 0x1000));
        }

        int topOffset;
        if (topLevel == 0) topOffset = L0Offset(0);
        else
        {
            topOffset = (HashBlock(0, 1, sex, step) << 12) + firstTable;
            for (int g = 0; g < groups; g++)
            {
                int at = topOffset + g * 0x18;
                SHA1.HashData(pkg.AsSpan(L0Offset(g * 0xAA), 0x1000)).CopyTo(pkg, at);
                int used = Math.Min(0xAA, alloc - g * 0xAA);
                uint dword = 0x80000000u | ((uint)(0xAA - used) << 15);
                BinaryPrimitives.WriteUInt32BigEndian(pkg.AsSpan(at + 0x14), dword);
            }
            BinaryPrimitives.WriteInt32BigEndian(pkg.AsSpan(topOffset + 0xFF0), alloc);
            pkg.AsSpan(topOffset, 0x1000).CopyTo(pkg.AsSpan(topOffset + 0x1000));
        }

        // Volume descriptor and hashes.
        const int vd = 0x379;
        pkg[vd + 2] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(pkg.AsSpan(vd + 3), 1);
        WriteI24Le(pkg.AsSpan(vd + 5), 0);
        SHA1.HashData(pkg.AsSpan(topOffset, 0x1000)).CopyTo(pkg, vd + 8);
        BinaryPrimitives.WriteInt32BigEndian(pkg.AsSpan(vd + 0x1C), alloc);
        BinaryPrimitives.WriteInt32BigEndian(pkg.AsSpan(vd + 0x20), 0);
        SHA1.HashData(pkg.AsSpan(0x344, firstTable - 0x344)).CopyTo(pkg, 0x32C);
        return pkg;
    }

    static void WriteI24Le(Span<byte> s, int v) { s[0] = (byte)v; s[1] = (byte)(v >> 8); s[2] = (byte)(v >> 16); }
    static void WriteI24Be(Span<byte> s, int v) { s[0] = (byte)(v >> 16); s[1] = (byte)(v >> 8); s[2] = (byte)v; }

    public static DateTime? FatTime(uint v)
    {
        int d = (int)(v >> 16), t = (int)(v & 0xFFFF);
        try { return new DateTime(((d >> 9) & 0x7F) + 1980, (d >> 5) & 0xF, d & 0x1F, (t >> 11) & 0x1F, (t >> 5) & 0x3F, (t & 0x1F) * 2); }
        catch { return null; }
    }
}
