// MinecraftStudio.Core/Bedrock/PocketLegacyWorld.cs: Raw binary level.dat of Pocket Edition 0.1.x.
// Owns: PocketRawLevel, PocketLegacyWorld, PocketChunksDimension.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Bedrock;

/// <summary>Raw binary level.dat of Pocket Edition 0.1.x.</summary>
public sealed class PocketRawLevel
{
    public int Seed { get; set; }
    public int SpawnX { get; set; }
    public int SpawnY { get; set; }
    public int SpawnZ { get; set; }
    public long EstimatedSize { get; set; }
    public int LastPlayed { get; set; }
    public string Name { get; set; } = "";

    public static PocketRawLevel Parse(byte[] d)
    {
        var r = new ByteReader(d);
        r.Skip(8);
        var l = new PocketRawLevel
        {
            Seed = r.I32BE(), SpawnX = r.I32BE(), SpawnY = r.I32BE(), SpawnZ = r.I32BE(),
            EstimatedSize = r.I64BE(), LastPlayed = r.I32BE(),
        };
        int n = r.U16BE();
        l.Name = Encoding.Latin1.GetString(r.Span(Math.Min(n, r.Remaining)));
        return l;
    }

    public byte[] ToBytes()
    {
        var w = new ByteWriter(64);
        w.I32LE(1);
        w.I32LE(0);
        w.I32BE(Seed); w.I32BE(SpawnX); w.I32BE(SpawnY); w.I32BE(SpawnZ);
        w.I64BE(EstimatedSize); w.I32BE(LastPlayed);
        var name = Encoding.Latin1.GetBytes(Name);
        w.U16BE((ushort)name.Length);
        w.Bytes(name);
        w.PatchI32LE(4, w.Length - 8);
        return w.ToArray();
    }
}

/// <summary>
/// Pocket Edition 0.1–0.8 and Pi Edition worlds: level.dat (raw in 0.1, NBT from 0.2), chunks.dat
/// (16×16 chunks of 16×128×16) and entities.dat. Limited to 256×256 blocks.
/// </summary>
public sealed class PocketLegacyWorld : SaveWorld
{
    readonly List<WorldDimension> _dims = [];

    PocketLegacyWorld(string folder) { Folder = folder; }

    public string Folder { get; }
    public NbtFile? Level { get; private set; }
    public PocketRawLevel? RawLevel { get; private set; }
    public NbtFile? Entities { get; private set; }
    public byte[]? EntitiesHeader { get; private set; }
    public PocketChunksDimension Overworld { get; private set; } = null!;

    public override Edition Edition => Edition.PocketLegacy;
    public override string Name => Level?.RootCompound.GetString("LevelName") ?? RawLevel?.Name ?? Path.GetFileName(Folder);
    public override string Location => Folder;
    public override NbtFile? LevelData => Level;
    public override IReadOnlyList<WorldDimension> Dimensions => _dims;

    public override string FormatDescription
    {
        get
        {
            if (RawLevel != null) return "Pocket Edition 0.1 (chunks.dat, raw level.dat)";
            int v = Level?.HeaderVersion ?? 0;
            return v <= 2 ? "Pocket Edition 0.2–0.7 (chunks.dat)" : "Pocket Edition 0.8 / Pi Edition (chunks.dat, storage version 3)";
        }
    }

    public static bool LooksLikePocketLegacy(string folder) =>
        File.Exists(Path.Combine(folder, "chunks.dat")) && File.Exists(Path.Combine(folder, "level.dat")) && !Directory.Exists(Path.Combine(folder, "db"));

    public static PocketLegacyWorld Open(string folder)
    {
        var w = new PocketLegacyWorld(folder);
        byte[] level = File.ReadAllBytes(Path.Combine(folder, "level.dat"));
        int ver = level.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(level) : 0;
        if (ver == 1 && level.Length > 8 && level[8] != (byte)NbtType.Compound) w.RawLevel = PocketRawLevel.Parse(level);
        else w.Level = NbtFile.Parse(level);

        string ent = Path.Combine(folder, "entities.dat");
        if (File.Exists(ent))
        {
            byte[] e = File.ReadAllBytes(ent);
            if (e.Length >= 12 && e[0] == (byte)'E' && e[1] == (byte)'N' && e[2] == (byte)'T' && e[3] == 0)
            {
                w.EntitiesHeader = e[..12];
                w.Entities = new NbtFile { Format = NbtFormat.Bedrock };
                foreach (var t in NbtBinary.ReadAll(e, NbtFormat.Bedrock, 12)) w.Entities.Roots.Add(t);
            }
        }
        w.Overworld = new PocketChunksDimension(Path.Combine(folder, "chunks.dat"));
        w._dims.Add(w.Overworld);
        return w;
    }

    public override IReadOnlyList<PlayerRecord> Players
    {
        get
        {
            if (Level?.RootCompound.GetCompound("Player") is NbtCompound p) return [new PlayerRecord("Player (level.dat)", Path.Combine(Folder, "level.dat"), p)];
            return [];
        }
    }

    public override IEnumerable<(string Key, string Value)> Summary()
    {
        yield return ("Name", Name);
        yield return ("Format", FormatDescription);
        if (RawLevel != null)
        {
            yield return ("Seed", RawLevel.Seed.ToString());
            yield return ("Spawn", $"{RawLevel.SpawnX}, {RawLevel.SpawnY}, {RawLevel.SpawnZ}");
            if (RawLevel.LastPlayed > 0) yield return ("Last played", DateTimeOffset.FromUnixTimeSeconds(RawLevel.LastPlayed).LocalDateTime.ToString("g"));
        }
        else if (Level != null)
        {
            var d = Level.RootCompound;
            if (d.GetLongOrNull("RandomSeed") is long seed) yield return ("Seed", seed.ToString());
            if (d.GetLongOrNull("GameType") is long gt) yield return ("Game mode", gt == 1 ? "Creative" : "Survival");
            if (d.GetLongOrNull("SpawnX") is long sx) yield return ("Spawn", $"{sx}, {(d.GetLong("SpawnY") == 32767 ? "surface" : d.GetLong("SpawnY").ToString())}, {d.GetLong("SpawnZ")}");
            if (d.GetLongOrNull("LastPlayed") is long lp && lp > 0) yield return ("Last played", DateTimeOffset.FromUnixTimeSeconds(lp).LocalDateTime.ToString("g"));
            if (d.GetLongOrNull("Time") is long t) yield return ("World age", $"{t / 19200.0:0.#} days");
        }
        yield return ("Chunks", Overworld.ChunkPositions.Count.ToString());
        if (Entities?.Roots.FirstOrDefault()?.Tag is NbtCompound ec)
            yield return ("Entities", $"{ec.GetList("Entities")?.Count ?? 0} entities, {ec.GetList("TileEntities")?.Count ?? 0} tile entities");
    }

    public override void Save()
    {
        if (Overworld.IsModified) Overworld.SaveChanges();
    }

    public void SaveLevel()
    {
        string path = Path.Combine(Folder, "level.dat");
        if (File.Exists(path)) File.Copy(path, path + "_old", true);
        SafeFile.WriteAllBytes(path, RawLevel != null ? RawLevel.ToBytes() : Level!.ToBytes());
    }

    /// <summary>Starts an empty entities.dat (no entities, no block entities) with the given header; SaveEntities writes it.</summary>
    public void CreateEntities(byte[] header)
    {
        EntitiesHeader = header[..12];
        Entities = new NbtFile { Format = NbtFormat.Bedrock };
        Entities.Roots.Add(new NamedTag("", new NbtCompound { ["Entities"] = new NbtList(NbtType.Compound), ["TileEntities"] = new NbtList(NbtType.Compound) }));
    }

    public void SaveEntities()
    {
        if (Entities == null || EntitiesHeader == null) return;
        var body = Entities.ToUncompressedBytes();
        var w = new ByteWriter(body.Length + 12);
        w.Bytes(EntitiesHeader.AsSpan(0, 8));
        w.I32LE(body.Length);
        w.Bytes(body);
        SafeFile.WriteAllBytes(Path.Combine(Folder, "entities.dat"), w.WrittenSpan);
    }
}

/// <summary>chunks.dat: 32×32 location table (only 16×16 used), 21-sector slots of 16×128×16 chunks.</summary>
public sealed class PocketChunksDimension : WorldDimension
{
    public const int ChunkBytes = 32768 + 16384 * 3 + 256;
    const int SectorSize = 4096;
    readonly string _path;
    readonly byte[] _data;
    readonly Dictionary<ChunkPos, int> _offsets = [];
    readonly HashSet<ChunkPos> _deleted = [];
    readonly Dictionary<ChunkPos, byte[]> _edited = [];

    public PocketChunksDimension(string path) : base("minecraft:overworld", "Overworld")
    {
        _path = path;
        _data = File.ReadAllBytes(path);
        for (int i = 0; i < 1024 && i * 4 + 4 <= _data.Length; i++)
        {
            uint e = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(i * 4));
            if (e == 0) continue;
            int offset = (int)(e >> 8) * SectorSize;
            if (offset + ChunkBytes > _data.Length) continue;
            int start = offset;
            int len = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset));
            if (len is 82180 or 82176) start += 4;
            _offsets[new ChunkPos(i & 31, i >> 5)] = start;
        }
    }

    public override IReadOnlyCollection<ChunkPos> ChunkPositions => _offsets.Keys;
    public override bool IsModified => _deleted.Count > 0 || _edited.Count > 0;

    public byte[]? RawChunk(ChunkPos pos)
    {
        if (_edited.TryGetValue(pos, out var e)) return e;
        return _offsets.TryGetValue(pos, out int off) ? _data.AsSpan(off, ChunkBytes).ToArray() : null;
    }

    public override ChunkColumn? LoadChunk(ChunkPos pos)
    {
        var raw = RawChunk(pos);
        if (raw == null) return null;
        var col = new ChunkColumn(pos.X, pos.Z, 0, 7);
        DecodeTerrain(raw, col);
        return col;
    }

    /// <summary>Shared by chunks.dat and the 3DS/LegacyTerrain layouts: index (x&lt;&lt;11)|(z&lt;&lt;7)|y.</summary>
    public static void DecodeTerrain(byte[] raw, ChunkColumn col)
    {
        for (int sy = 0; sy < 8; sy++)
        {
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    for (int y = 0; y < 16; y++)
                    {
                        int i = (x << 11) | (z << 7) | (sy * 16 + y);
                        int id = raw[i];
                        byte b = raw[32768 + (i >> 1)];
                        int d = (i & 1) == 0 ? b & 15 : b >> 4;
                        int key = (id << 4) | d;
                        if (!map.TryGetValue(key, out ushort p))
                        {
                            p = (ushort)palette.Count;
                            palette.Add(LegacyBlocks.Get(id, d, LegacyIdScheme.Bedrock));
                            map[key] = p;
                        }
                        idx[(y << 8) | (z << 4) | x] = p;
                    }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            col.Sections[sy] = new BlockSection(sy, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
    }

    public override ChunkInfo? GetChunkInfo(ChunkPos pos) => _offsets.ContainsKey(pos) ? new ChunkInfo { Pos = pos, StoredBytes = ChunkBytes } : null;

    /// <summary>Removed chunks are regenerated by the game when the world is loaded.</summary>
    readonly Dictionary<ChunkPos, (int Offset, byte[]? Edited)> _deletedInfo = [];

    public override void DeleteChunks(IEnumerable<ChunkPos> chunks)
    {
        foreach (var c in chunks)
            if (_offsets.Remove(c, out int offset))
            {
                _deleted.Add(c);
                _deletedInfo[c] = (offset, _edited.Remove(c, out var edited) ? edited : null);
            }
    }

    public override bool CanRestoreChunks => true;

    public override IReadOnlyList<ChunkPos> RestoreChunks(IEnumerable<ChunkPos> chunks)
    {
        var restored = new List<ChunkPos>();
        foreach (var c in chunks)
        {
            if (!_deleted.Contains(c) || !_deletedInfo.Remove(c, out var info)) continue;
            _deleted.Remove(c);
            _offsets[c] = info.Offset;
            if (info.Edited != null) _edited[c] = info.Edited;
            restored.Add(c);
        }
        return restored;
    }

    public void SetRawChunk(ChunkPos pos, byte[] data)
    {
        if (data.Length != ChunkBytes) throw new ArgumentException("A chunks.dat chunk is exactly 82176 bytes.");
        _edited[pos] = data;
    }

    public override void SaveChanges()
    {
        _deletedInfo.Clear();
        var copy = (byte[])_data.Clone();
        foreach (var c in _deleted)
            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan((c.X + c.Z * 32) * 4), 0);
        foreach (var (pos, raw) in _edited)
        {
            if (_offsets.TryGetValue(pos, out int off)) raw.CopyTo(copy, off);
        }
        SafeFile.WriteAllBytes(_path, copy);
        copy.CopyTo(_data, 0);
        _deleted.Clear();
        _edited.Clear();
    }
}
