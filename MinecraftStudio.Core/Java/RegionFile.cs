// MinecraftStudio.Core/Java/RegionFile.cs: McRegion (.mcr) and Anvil (.mca) region files, also used for
// entities/ and poi/ data. Unchanged chunks are copied byte-for-byte when the file is saved.
// Owns: RegionFile, RegionIssue.
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Java;

/// <summary>
/// McRegion (.mcr) and Anvil (.mca) region files, also used for entities/ and poi/ data.
/// Unchanged chunks are copied byte-for-byte when the file is saved.
/// </summary>
public sealed class RegionFile
{
    public const int SectorSize = 4096;
    public const int MaxSectors = 255;
    static readonly Regex NamePattern = new(@"^r\.(-?\d+)\.(-?\d+)\.(mca|mcr)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    readonly byte[] _data;
    readonly uint[] _locations = new uint[1024];
    readonly int[] _timestamps = new int[1024];
    readonly Dictionary<int, RawChunk?> _changes = [];

    RegionFile(string? path, byte[] data, int rx, int rz)
    {
        FilePath = path;
        _data = data;
        RegionX = rx;
        RegionZ = rz;
        if (data.Length >= 8192)
        {
            for (int i = 0; i < 1024; i++)
            {
                _locations[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i * 4));
                _timestamps[i] = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4096 + i * 4));
            }
        }
        else if (data.Length > 0)
        {
            HeaderTruncated = true;
            int n = Math.Min(1024, data.Length / 4);
            for (int i = 0; i < n; i++) _locations[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i * 4));
        }
    }

    public string? FilePath { get; private set; }
    public int RegionX { get; }
    public int RegionZ { get; }
    public bool HeaderTruncated { get; }
    public long FileLength => _data.Length;
    public bool IsModified => _changes.Count > 0;

    public static bool TryParseName(string fileName, out int rx, out int rz)
    {
        var m = NamePattern.Match(fileName);
        rx = rz = 0;
        if (!m.Success) return false;
        rx = int.Parse(m.Groups[1].Value);
        rz = int.Parse(m.Groups[2].Value);
        return true;
    }

    public static RegionFile Open(string path)
    {
        TryParseName(Path.GetFileName(path), out int rx, out int rz);
        return new RegionFile(path, File.ReadAllBytes(path), rx, rz);
    }

    public static RegionFile FromBytes(byte[] data, int rx, int rz) => new(null, data, rx, rz);
    public static RegionFile CreateEmpty(string path, int rx, int rz) => new(path, [], rx, rz);

    public static int Index(int localX, int localZ) => (localX & 31) | ((localZ & 31) << 5);
    public (int X, int Z) ChunkCoords(int index) => (RegionX * 32 + (index & 31), RegionZ * 32 + (index >> 5));

    public bool HasChunk(int index) => _changes.TryGetValue(index, out var c) ? c != null : _locations[index] != 0 && (_locations[index] >> 8) != 0;

    public IEnumerable<int> ChunkIndexes()
    {
        for (int i = 0; i < 1024; i++) if (HasChunk(i)) yield return i;
    }

    public int ChunkCount => ChunkIndexes().Count();

    public int GetTimestamp(int index) => _changes.TryGetValue(index, out var c) ? c?.Timestamp ?? 0 : _timestamps[index];

    public int StoredSectorOffset(int index) => (int)(_locations[index] >> 8);
    public int StoredSectorCount(int index) => (int)(_locations[index] & 0xFF);

    string ExternalPath(int index)
    {
        var (x, z) = ChunkCoords(index);
        return Path.Combine(Path.GetDirectoryName(FilePath ?? ".")!, $"c.{x}.{z}.mcc");
    }

    /// <summary>Reads the stored chunk without decompressing it. Returns null for an empty slot.</summary>
    public RawChunk? GetRaw(int index)
    {
        if (_changes.TryGetValue(index, out var changed)) return changed;
        uint loc = _locations[index];
        if (loc == 0) return null;
        long offset = (long)(loc >> 8) * SectorSize;
        int sectors = (int)(loc & 0xFF);
        if (offset == 0) return null;
        if (offset < 8192 || offset + 5 > _data.Length)
            throw new SaveFormatException($"Chunk {index} points outside the file (sector {loc >> 8}).");
        int length = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan((int)offset));
        byte type = _data[offset + 4];
        bool external = (type & 0x80) != 0;
        type &= 0x7F;
        if (length <= 0) throw new SaveFormatException($"Chunk {index} has length {length}.");
        if (offset + 4 + length > _data.Length) throw new SaveFormatException($"Chunk {index} runs past the end of the file.");
        if (!external && 4 + length > sectors * SectorSize && sectors != 0)
        {
            // Some tools wrote chunks longer than their sector count; the data is still readable.
            OversizedSlots.Add(index);
        }
        int dataStart = (int)offset + 5;
        int dataLen = length - 1;
        string? custom = null;
        if (type == 127)
        {
            int nameLen = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(dataStart));
            custom = Encoding.UTF8.GetString(_data, dataStart + 2, nameLen);
            dataStart += 2 + nameLen;
            dataLen -= 2 + nameLen;
        }
        byte[] payload;
        if (external)
        {
            string mcc = ExternalPath(index);
            if (!File.Exists(mcc)) throw new SaveFormatException($"Chunk {index} is stored in {Path.GetFileName(mcc)}, which is missing.");
            payload = File.ReadAllBytes(mcc);
        }
        else payload = _data.AsSpan(dataStart, dataLen).ToArray();
        return new RawChunk(type, payload, _timestamps[index], custom);
    }

    /// <summary>
    /// Opens a region file even when parts of it cannot be read from the disk (those parts read as zeros, so the chunks
    /// there show up as damaged). <paramref name="unreadable"/> counts the 4 KB pieces that could not be read.
    /// </summary>
    public static RegionFile OpenTolerant(string path, out int unreadable)
    {
        TryParseName(Path.GetFileName(path), out int rx, out int rz);
        return new RegionFile(path, SafeFile.ReadAllBytesTolerant(path, out unreadable), rx, rz);
    }

    /// <summary>
    /// Rescue: finds a readable copy of every chunk in the file. A chunk whose data where the header points decompresses
    /// (and does not name another position) is kept as it is. For the rest, every other sector of the file is searched
    /// for chunk data: the game writes a chunk that grew to new sectors and leaves the old copy behind, and a damaged
    /// header no longer points to chunks that are still in the file. Lone .mcc files next to the region (chunks too big
    /// for it) are read too. A copy must decompress and name its own position inside this region; the newest copy by
    /// the chunk's own LastUpdate wins. Points-of-interest data names no position, so only its listed chunks are kept.
    /// </summary>
    public RegionRecovery Recover(RegionContent content, CancellationToken cancel = default)
    {
        var result = new RegionRecovery();
        int sectors = _data.Length / SectorSize;
        var used = new bool[Math.Max(sectors, 0)];
        var damaged = new List<int>();
        for (int i = 0; i < 1024; i++)
        {
            if ((i & 63) == 0) cancel.ThrowIfCancellationRequested();
            if (_locations[i] == 0 || (_locations[i] >> 8) == 0) continue;
            NbtCompound? chunk = null;
            try { chunk = GetChunkNbt(i); }
            catch (Exception e) when (e is not OperationCanceledException) { }
            var (x, z) = ChunkCoords(i);
            if (chunk == null || PositionOf(chunk) is (int px, int pz) && (px != x || pz != z))
            {
                damaged.Add(i);
                continue;
            }
            result.Chunks[i] = ChunkCopy.Listed;
            result.NoteVersion(chunk);
            int start = StoredSectorOffset(i), count = Math.Max(1, StoredSectorCount(i));
            for (int s = start; s < start + count && s < used.Length; s++) used[s] = true;
        }

        var found = new Dictionary<int, (ChunkCopy Copy, long LastUpdate)>();
        if (content != RegionContent.PointsOfInterest)
        {
            for (int s = 2; s < sectors; s++)
            {
                if ((s & 255) == 0) cancel.ThrowIfCancellationRequested();
                // Every sector is tried (a damaged length must not hide the chunks after it); the checks before
                // decompressing turn away the inside of other chunks' data almost always.
                if (used[s] || !TryReadAt(s, out var chunk)) continue;
                if (PositionOf(chunk) is not (int x, int z) || x >> 5 != RegionX || z >> 5 != RegionZ) continue;
                int index = Index(x & 31, z & 31);
                long update = LastUpdateOf(chunk);
                if (!result.Chunks.ContainsKey(index) && (!found.TryGetValue(index, out var have) || update > have.LastUpdate))
                {
                    found[index] = (new ChunkCopy(s, 0), update);
                    result.NoteVersion(chunk);
                }
            }
            foreach (var (i, cx, cz) in ExternalFiles())
            {
                if (result.Chunks.ContainsKey(i) || found.ContainsKey(i)) continue;
                if (TryReadExternal(i, out byte type, out var chunk) && PositionOf(chunk) is (int x, int z) && x == cx && z == cz)
                {
                    found[i] = (new ChunkCopy(0, type), LastUpdateOf(chunk));
                    result.NoteVersion(chunk);
                }
            }
        }

        foreach (int i in damaged)
        {
            if (!found.Remove(i, out var copy)) { result.Lost++; continue; }
            result.Chunks[i] = copy.Copy;
            // A big chunk's own .mcc file is its current data (only the pointer to it was damaged).
            if (copy.Copy.IsExternal) result.Unlisted++;
            else result.Older++;
        }
        foreach (var (i, copy) in found)
        {
            result.Chunks[i] = copy.Copy;
            result.Unlisted++;
        }
        return result;
    }

    /// <summary>The stored data of a chunk copy found by <see cref="Recover"/>, as it is in the file (not decompressed).</summary>
    public RawChunk ReadCopy(int index, ChunkCopy copy)
    {
        if (copy.IsListed) return GetRaw(index) ?? throw new SaveFormatException($"Chunk {index} is no longer in the file.");
        if (copy.IsExternal) return new RawChunk(copy.ExternalType, File.ReadAllBytes(ExternalPath(index)), _timestamps[index]);
        long off = (long)copy.Sector * SectorSize;
        if (off + 5 > _data.Length) throw new SaveFormatException($"Sector {copy.Sector} is outside the file.");
        int length = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan((int)off));
        if (length < 2 || off + 4 + length > _data.Length) throw new SaveFormatException($"The chunk copy at sector {copy.Sector} runs past the end of the file.");
        return new RawChunk(_data[off + 4], _data.AsSpan((int)off + 5, length - 1).ToArray(), _timestamps[index]);
    }

    /// <summary>Reads chunk data starting at a sector: false when there is none there, or it does not decompress to NBT.</summary>
    bool TryReadAt(int sector, out NbtCompound chunk)
    {
        chunk = null!;
        long off = (long)sector * SectorSize;
        if (off + 6 > _data.Length) return false;
        int length = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan((int)off));
        byte type = _data[off + 4];
        if (length < 2 || off + 4 + length > _data.Length || type is not (1 or 2 or 3 or 4)) return false;
        // Cheap checks before decompressing: zlib and gzip streams and uncompressed NBT start with known bytes.
        byte b0 = _data[off + 5], b1 = length > 2 ? _data[off + 6] : (byte)0;
        if (type == 2 && (b0 != 0x78 || (b0 << 8 | b1) % 31 != 0)) return false;
        if (type == 1 && (b0 != 0x1F || b1 != 0x8B)) return false;
        if (type == 3 && b0 != 10) return false;
        try
        {
            var raw = new RawChunk(type, _data.AsSpan((int)off + 5, length - 1).ToArray(), 0);
            chunk = NbtBinary.ReadCompound(raw.Decompress(), NbtFormat.Java);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException) { return false; }
    }

    /// <summary>The .mcc files next to this region that belong to it (slot and chunk position), listed once.</summary>
    List<(int Index, int X, int Z)> ExternalFiles()
    {
        var list = new List<(int, int, int)>();
        if (FilePath == null) return list;
        try
        {
            foreach (var mcc in Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(FilePath))!, "c.*.mcc"))
            {
                var parts = Path.GetFileNameWithoutExtension(mcc).Split('.');
                if (parts.Length == 3 && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int z) && x >> 5 == RegionX && z >> 5 == RegionZ)
                    list.Add((Index(x & 31, z & 31), x, z));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>Reads a lone .mcc file: its compression is written in the region file, so each kind is tried.</summary>
    bool TryReadExternal(int index, out byte type, out NbtCompound chunk)
    {
        byte[] data;
        try { data = File.ReadAllBytes(ExternalPath(index)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { type = 0; chunk = null!; return false; }
        foreach (byte t in new byte[] { 2, 1, 4, 3 })
        {
            try
            {
                chunk = NbtBinary.ReadCompound(new RawChunk(t, data, 0).Decompress(), NbtFormat.Java);
                type = t;
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException) { }
        }
        type = 0;
        chunk = null!;
        return false;
    }

    /// <summary>The position a chunk names: xPos/zPos (terrain, also under Level before 1.18) or Position (entities, 1.17+).</summary>
    public static (int X, int Z)? PositionOf(NbtCompound chunk)
    {
        var level = chunk.GetCompound("Level") ?? chunk;
        if (level.GetLongOrNull("xPos") is long x && level.GetLongOrNull("zPos") is long z) return ((int)x, (int)z);
        if (chunk.GetInts("Position") is { Length: 2 } p) return (p[0], p[1]);
        return null;
    }

    static long LastUpdateOf(NbtCompound chunk) => (chunk.GetCompound("Level") ?? chunk).GetLongOrNull("LastUpdate") ?? 0;

    /// <summary>Slots whose data is longer than the sector count in the header says.</summary>
    public HashSet<int> OversizedSlots { get; } = [];

    public bool IsExternal(int index)
    {
        if (_changes.ContainsKey(index)) return false;
        uint loc = _locations[index];
        long offset = (long)(loc >> 8) * SectorSize;
        return loc != 0 && offset + 5 <= _data.Length && offset >= 8192 && (_data[offset + 4] & 0x80) != 0;
    }

    public NbtCompound? GetChunkNbt(int index)
    {
        var raw = GetRaw(index);
        if (raw == null) return null;
        return NbtBinary.ReadCompound(raw.Decompress(), NbtFormat.Java);
    }

    public void SetChunk(int index, NbtCompound chunk, CompressionKind? compression = null, int? timestamp = null)
    {
        var old = SafeGetRaw(index);
        var kind = compression ?? (old != null && old.CompressionType is >= 1 and <= 4 ? RawChunk.KindOf(old.CompressionType) : CompressionKind.Zlib);
        _changes[index] = RawChunk.FromNbt(chunk, kind, timestamp ?? (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public void SetRaw(int index, RawChunk raw) => _changes[index] = raw;
    public void DeleteChunk(int index) => _changes[index] = null;
    public void DiscardChanges() => _changes.Clear();

    RawChunk? SafeGetRaw(int index)
    {
        try { return GetRaw(index); } catch { return null; }
    }

    /// <summary>Checks every slot: overlapping sectors, bad offsets, unreadable data.</summary>
    public List<RegionIssue> Validate(bool decompress)
    {
        var issues = new List<RegionIssue>();
        if (HeaderTruncated) issues.Add(new RegionIssue(-1, "The region header is truncated."));
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < 1024; i++)
        {
            if (_changes.ContainsKey(i) || _locations[i] == 0) continue;
            int start = (int)(_locations[i] >> 8), count = (int)(_locations[i] & 0xFF);
            if (start < 2) { issues.Add(new RegionIssue(i, $"Chunk points into the header (sector {start}).")); continue; }
            if ((long)start * SectorSize >= _data.Length) { issues.Add(new RegionIssue(i, "Chunk points past the end of the file.")); continue; }
            for (int s = start; s < start + count; s++)
            {
                if (owner.TryGetValue(s, out int other)) { issues.Add(new RegionIssue(i, $"Chunk shares sector {s} with chunk {other}.")); break; }
                owner[s] = i;
            }
            try
            {
                var raw = GetRaw(i);
                if (raw != null && decompress)
                {
                    if (raw.CompressionType == 127) continue;
                    var nbt = NbtBinary.ReadCompound(raw.Decompress(), NbtFormat.Java);
                    var (x, z) = ChunkCoords(i);
                    var level = nbt.GetCompound("Level") ?? nbt;
                    if (level.GetLongOrNull("xPos") is long px && level.GetLongOrNull("zPos") is long pz && (px != x || pz != z))
                        issues.Add(new RegionIssue(i, $"Chunk says it is at {px},{pz} but is stored in the slot for {x},{z}.", Fatal: false));
                }
            }
            catch (Exception ex) { issues.Add(new RegionIssue(i, ex.Message)); }
        }
        return issues;
    }

    /// <summary>Writes the region compactly (no free sectors). Returns false when the region became empty and was deleted.</summary>
    public bool Save(string? path = null, bool deleteIfEmpty = true)
    {
        path ??= FilePath ?? throw new InvalidOperationException("No path to save to.");
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var w = new ByteWriter(Math.Max(8192, _data.Length));
        w.Zeros(8192);
        var externals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        for (int i = 0; i < 1024; i++)
        {
            RawChunk? raw;
            bool copyOriginal = false;
            if (_changes.TryGetValue(i, out raw)) { if (raw == null) continue; }
            else
            {
                if (_locations[i] == 0) continue;
                raw = SafeGetRaw(i);
                if (raw == null) continue; // unreadable slot: dropped (the caller validated first)
                copyOriginal = !IsExternal(i);
            }
            int start = w.Length / SectorSize;
            byte[]? nameBytes = raw.CustomAlgorithm != null ? Encoding.UTF8.GetBytes(raw.CustomAlgorithm) : null;
            int bodyLen = 1 + (nameBytes != null ? 2 + nameBytes.Length : 0) + raw.Payload.Length;
            bool external = 4 + bodyLen > MaxSectors * SectorSize;
            var (cx, cz) = ChunkCoords(i);
            if (external)
            {
                string mcc = Path.Combine(dir, $"c.{cx}.{cz}.mcc");
                SafeFile.WriteAllBytes(mcc, raw.Payload);
                externals.Add(mcc);
                w.I32BE(1);
                w.U8((byte)(raw.CompressionType | 0x80));
            }
            else if (copyOriginal)
            {
                int off = (int)(_locations[i] >> 8) * SectorSize;
                int length = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(off));
                w.Bytes(_data.AsSpan(off, 4 + length));
            }
            else
            {
                w.I32BE(bodyLen);
                w.U8(raw.CompressionType);
                if (nameBytes != null) { w.U16BE((ushort)nameBytes.Length); w.Bytes(nameBytes); }
                w.Bytes(raw.Payload);
            }
            w.Align(SectorSize);
            int sectors = w.Length / SectorSize - start;
            w.PatchI32BE(i * 4, (start << 8) | sectors);
            w.PatchI32BE(4096 + i * 4, raw.Timestamp);
            count++;
        }

        // Remove .mcc files for chunks of this region that are no longer external.
        for (int i = 0; i < 1024; i++)
        {
            var (cx, cz) = ChunkCoords(i);
            string mcc = Path.Combine(dir, $"c.{cx}.{cz}.mcc");
            if (!externals.Contains(mcc) && File.Exists(mcc)) File.Delete(mcc);
        }

        if (count == 0 && deleteIfEmpty)
        {
            if (File.Exists(path)) File.Delete(path);
            return false;
        }
        SafeFile.WriteAllBytes(path, w.WrittenSpan);
        FilePath = path;
        return true;
    }

    /// <summary>Bytes that a compact rewrite would save.</summary>
    public long WastedBytes()
    {
        long used = 8192;
        for (int i = 0; i < 1024; i++)
        {
            if (_locations[i] == 0) continue;
            long off = (long)(_locations[i] >> 8) * SectorSize;
            if (off + 4 > _data.Length) continue;
            int length = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan((int)off));
            used += (length + 4 + SectorSize - 1) / SectorSize * SectorSize;
        }
        return Math.Max(0, _data.Length - used);
    }
}

public sealed record RegionIssue(int Index, string Message, bool Fatal = true);
