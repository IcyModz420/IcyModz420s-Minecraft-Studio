// MinecraftStudio.Core/Bedrock/ThreeDsWorld.cs: Minecraft: New Nintendo 3DS Edition world (decrypted extdata
// dump): Bedrock-style level.dat plus the "slt" chunk database in db/cdb and the value store in db/vdb (the player, maps,
// biome data — Bedrock-format values under Bedrock's keys). Terrain and values are read-only; level.dat can be edited.
// Owns: ThreeDsWorld, ThreeDsDimension.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Bedrock;

/// <summary>
/// Minecraft: New Nintendo 3DS Edition world (decrypted extdata dump): Bedrock-style level.dat plus the
/// "slt" chunk database in db/cdb. Terrain is read-only; level.dat can be edited.
/// </summary>
public sealed class ThreeDsWorld : SaveWorld
{
    public const uint ChunkMagic = 0xABCDEF98;
    public const uint RecordMagic = 0xABCDEF99;
    readonly List<WorldDimension> _dims = [];

    ThreeDsWorld(string folder, NbtFile level)
    {
        Folder = folder;
        Level = level;
    }

    public string Folder { get; }
    public NbtFile Level { get; }
    public List<string> Warnings { get; } = [];

    // db/vdb: each slt*.vdb holds records after a 24-byte header (RecordMagic at 20): u32 key length, two u32s, the key,
    // u32 value length, the value — read from the samples (1.0 - 1.9), one record a file there. Values are Bedrock's
    // (little-endian NBT): "~local_player", "map_<id>", "AutonomousEntities", "BiomeData".
    Dictionary<string, (string File, long Offset, int Length)>? _values;
    List<PlayerRecord>? _players;

    /// <summary>The value store's keys with where each value is (file, offset, length); read once, values on demand.</summary>
    public IReadOnlyDictionary<string, (string File, long Offset, int Length)> Values => _values ??= ReadValues();

    Dictionary<string, (string File, long Offset, int Length)> ReadValues()
    {
        var values = new Dictionary<string, (string, long, int)>(StringComparer.Ordinal);
        string dir = Path.Combine(Folder, "db", "vdb");
        if (!Directory.Exists(dir)) return values;
        foreach (var file in Directory.EnumerateFiles(dir, "slt*.vdb").Order(StringComparer.Ordinal))
        {
            try
            {
                byte[] d = File.ReadAllBytes(file);
                if (d.Length < 24 || BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(20)) != RecordMagic) continue;
                int p = 24;
                while (p + 16 <= d.Length)
                {
                    int keyLength = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p));
                    if (keyLength is <= 0 or > 256 || p + 12 + keyLength + 4 > d.Length) break;
                    string key = System.Text.Encoding.ASCII.GetString(d, p + 12, keyLength);
                    int valueLength = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 12 + keyLength));
                    int at = p + 16 + keyLength;
                    if (valueLength < 0 || at + valueLength > d.Length) break;
                    values.TryAdd(key, (file, at, valueLength));
                    p = at + valueLength;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Warnings.Add($"{Path.GetFileName(file)} could not be read: {e.Message}"); }
        }
        return values;
    }

    /// <summary>One value of the store, or null when it has none by that key.</summary>
    public byte[]? Value(string key)
    {
        if (!Values.TryGetValue(key, out var v)) return null;
        using var f = new FileStream(v.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        f.Position = v.Offset;
        var data = new byte[v.Length];
        f.ReadExactly(data);
        return data;
    }

    /// <summary>The world's player (~local_player in the value store), read-only: nothing is written into a 3DS world.</summary>
    public override IReadOnlyList<PlayerRecord> Players => _players ??= ReadPlayers();

    List<PlayerRecord> ReadPlayers()
    {
        try
        {
            if (Value("~local_player") is byte[] v) return [new PlayerRecord("Player", "~local_player", NbtBinary.ReadCompound(v, NbtFormat.Bedrock))];
        }
        catch (Exception e) when (e is IOException or InvalidDataException or SaveFormatException or EndOfStreamException) { Warnings.Add("The player could not be read: " + e.Message); }
        return [];
    }
    public override Edition Edition => Edition.Nintendo3DS;
    public override string Name => Level.RootCompound.GetString("LevelName") ?? Path.GetFileName(Folder);
    public override string Location => Folder;
    public override NbtFile? LevelData => Level;
    public override IReadOnlyList<WorldDimension> Dimensions => _dims;
    public override string FormatDescription => $"New Nintendo 3DS Edition (storage version {Level.HeaderVersion ?? 0})";

    public static bool LooksLike3ds(string folder) =>
        File.Exists(Path.Combine(folder, "level.dat")) && Directory.Exists(Path.Combine(folder, "db", "cdb"));

    public sealed class ChunkRef
    {
        public required string SltFile { get; init; }
        public required long SubfileOffset { get; init; }
        public required int SubfileSize { get; init; }
        public required uint Position { get; init; }
    }

    public static (int X, int Z, int Dim) DecodePosition(uint raw)
    {
        int xr = (int)(raw & 0x3FFF), zr = (int)((raw >> 14) & 0x3FFF);
        return (xr > 4096 ? xr - 8192 : xr, zr > 4096 ? zr - 8192 : zr, (int)(raw >> 28));
    }

    public static ThreeDsWorld Open(string folder)
    {
        var w = new ThreeDsWorld(folder, NbtFile.Load(Path.Combine(folder, "level.dat")));
        string cdb = Path.Combine(folder, "db", "cdb");
        string index = File.Exists(Path.Combine(cdb, "newindex.cdb")) ? Path.Combine(cdb, "newindex.cdb") : Path.Combine(cdb, "index.cdb");
        var byDim = new Dictionary<int, Dictionary<ChunkPos, ChunkRef>>();
        var slts = new Dictionary<int, (int Count, int Size)?>();
        byte[] idx = File.ReadAllBytes(index);
        var r = new ByteReader(idx);
        uint indexVersion = r.U32LE(); // 1 up to 3DS 1.7, 2 from 1.8/1.9
        if (indexVersion is not (1 or 2)) w.Warnings.Add($"Unexpected index version {indexVersion}.");
        int entries = r.I32LE();
        r.U32LE();
        int entrySize = r.I32LE();
        int pointers = r.I32LE();
        r.U32LE();
        r.Skip(pointers * 4);
        for (int i = 0; i < entries && r.Remaining >= entrySize; i++)
        {
            int start = r.Position;
            uint position = r.U32LE();
            int slot = r.U16LE();
            int subfile = r.U16LE();
            r.Position = start + entrySize;
            string slt = Path.Combine(cdb, $"slt{slot}.cdb");
            if (!slts.TryGetValue(slot, out var hdr))
            {
                hdr = ReadSltHeader(slt);
                slts[slot] = hdr;
            }
            if (hdr == null) continue;
            var (x, z, dim) = DecodePosition(position);
            if (dim > 2) { w.Warnings.Add($"Index entry {i} has dimension {dim}."); continue; }
            if (!byDim.TryGetValue(dim, out var d)) byDim[dim] = d = [];
            d[new ChunkPos(x, z)] = new ChunkRef { SltFile = slt, SubfileOffset = 20 + (long)subfile * hdr.Value.Size, SubfileSize = hdr.Value.Size, Position = position };
        }
        foreach (var dim in byDim.Keys.OrderBy(k => k))
        {
            string id = dim switch { 0 => "minecraft:overworld", 1 => "minecraft:the_nether", _ => "minecraft:the_end" };
            string name = dim switch { 0 => "Overworld", 1 => "The Nether", _ => "The End" };
            w._dims.Add(new ThreeDsDimension(w, id, name, byDim[dim]));
        }
        return w;
    }

    static (int Count, int Size)? ReadSltHeader(string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = File.OpenRead(path);
        var h = new byte[20];
        if (fs.Read(h) != 20) return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(4));
        int size = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(12));
        if (size <= 0 || 20 + (long)count * size > fs.Length) return null;
        return (count, size);
    }

    /// <summary>Reads a chunk subfile and returns its decompressed sections (index → data).</summary>
    public Dictionary<int, byte[]> ReadSections(ChunkRef c)
    {
        var sub = new byte[c.SubfileSize];
        using (var fs = File.OpenRead(c.SltFile))
        {
            fs.Position = c.SubfileOffset;
            fs.ReadExactly(sub);
        }
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(sub);
        // 1.0–1.6 chunk subfiles use 0xABCDEF99 (later the VDB record magic); 1.7+ use 0xABCDEF98.
        if (magic != ChunkMagic && magic != RecordMagic) throw new SaveFormatException($"Subfile is not a chunk (magic {magic:X8}).");
        uint pos = BinaryPrimitives.ReadUInt32LittleEndian(sub.AsSpan(4));
        if (pos != c.Position) Warnings.Add($"Chunk header position {pos:X8} differs from the index ({c.Position:X8}).");
        var result = new Dictionary<int, byte[]>();
        for (int s = 0; s < 6; s++)
        {
            int at = 0x10 + s * 16;
            int index = BinaryPrimitives.ReadInt32LittleEndian(sub.AsSpan(at));
            int position = BinaryPrimitives.ReadInt32LittleEndian(sub.AsSpan(at + 4));
            int csize = BinaryPrimitives.ReadInt32LittleEndian(sub.AsSpan(at + 8));
            int dsize = BinaryPrimitives.ReadInt32LittleEndian(sub.AsSpan(at + 12));
            if (index == -1 || position == -1 || csize <= 0) continue;
            if (position + csize > sub.Length) { Warnings.Add("A chunk section runs past its subfile."); continue; }
            var data = Compression.ZlibDecompress(sub.AsSpan(position, csize), dsize);
            if (data.Length != dsize) Warnings.Add($"Section {index} decompressed to {data.Length} bytes, expected {dsize}.");
            result[index] = data;
        }
        return result;
    }

    public override IEnumerable<(string Key, string Value)> Summary()
    {
        var d = Level.RootCompound;
        yield return ("Name", Name);
        yield return ("Format", FormatDescription);
        if (d.GetLongOrNull("RandomSeed") is long seed) yield return ("Seed", seed.ToString());
        if (d.GetLongOrNull("GameType") is long gt) yield return ("Game mode", gt == 1 ? "Creative" : gt == 2 ? "Adventure" : "Survival");
        if (d.GetLongOrNull("SpawnX") is long sx) yield return ("Spawn", $"{sx}, {(d.GetLong("SpawnY") == 32767 ? "surface" : d.GetLong("SpawnY").ToString())}, {d.GetLong("SpawnZ")}");
        if (d.GetList("lastOpenedWithVersion") is { Count: > 0 } v) yield return ("Last opened with", string.Join(".", v.Select(t => t.AsLong)));
        yield return ("Dimensions", string.Join(", ", _dims.Select(x => $"{x.DisplayName} ({x.ChunkPositions.Count} chunks)")));
        if (Warnings.Count > 0) yield return ("Warnings", string.Join("; ", Warnings.Distinct().Take(5)));
    }

    public void SaveLevelDat()
    {
        string path = Path.Combine(Folder, "level.dat");
        File.Copy(path, Path.Combine(Folder, "level.dat_old"), true);
        Level.Save(path);
    }

    public override void Save() { }
}

public sealed class ThreeDsDimension : WorldDimension
{
    const int LegacyTerrainSize = 32768 + 16384 * 3 + 256 + 1024;
    readonly ThreeDsWorld _world;
    readonly Dictionary<ChunkPos, ThreeDsWorld.ChunkRef> _chunks;

    internal ThreeDsDimension(ThreeDsWorld world, string id, string name, Dictionary<ChunkPos, ThreeDsWorld.ChunkRef> chunks) : base(id, name)
    {
        _world = world;
        _chunks = chunks;
    }

    public override IReadOnlyCollection<ChunkPos> ChunkPositions => _chunks.Keys;

    public ThreeDsWorld.ChunkRef? Ref(ChunkPos pos) => _chunks.GetValueOrDefault(pos);

    public override ChunkColumn? LoadChunk(ChunkPos pos)
    {
        if (!_chunks.TryGetValue(pos, out var c)) return null;
        var sections = _world.ReadSections(c);
        var col = new ChunkColumn(pos.X, pos.Z, 0, 7) { BiomeScheme = BiomeIdScheme.Bedrock };
        if (!sections.TryGetValue(0, out var terrain) || terrain.Length < 1) return col;
        if (terrain.Length == LegacyTerrainSize)
        {
            // 1.0–1.6: the PE 0.9–0.16 LegacyTerrain layout (16×128×16, XZY, plus light, heights and biome colours).
            BedrockChunk.DecodeLegacyTerrain(terrain, col);
            return col;
        }
        int count = Math.Min(8, (int)terrain[0]);
        int p = 1;
        for (int sy = 0; sy < count && p + 10241 <= terrain.Length; sy++, p += 10241)
        {
            var sub = new byte[1 + 4096 + 2048];
            Array.Copy(terrain, p, sub, 0, sub.Length);
            var s = BedrockChunk.DecodeSubChunk(sub, sy);
            if (s != null && !s.IsEmpty) col.Sections[sy] = s;
        }
        int tail = 1 + count * 10241;
        if (terrain.Length >= tail + 512 + 256)
        {
            var heights = new int[256];
            for (int i = 0; i < 256; i++) heights[i] = BinaryPrimitives.ReadUInt16LittleEndian(terrain.AsSpan(tail + i * 2));
            col.HeightMap = heights;
            col.BiomeIds = terrain.AsSpan(tail + 512, 256).ToArray().Select(b => (int)b).ToArray();
        }
        foreach (var (index, data) in sections)
        {
            if (index == 0 || data.Length == 0 || data[0] != (byte)NbtType.Compound) continue;
            try { col.BlockEntityCount += NbtBinary.ReadAll(data, NbtFormat.Bedrock).Count; } catch { }
        }
        return col;
    }

    /// <summary>The non-terrain sections that hold NBT, for the NBT editor (read-only).</summary>
    public override NbtCompound? LoadChunkNbt(ChunkPos pos)
    {
        if (!_chunks.TryGetValue(pos, out var c)) return null;
        var root = new NbtCompound();
        foreach (var (index, data) in _world.ReadSections(c))
        {
            if (index == 0) { root[$"Section {index} (terrain)"] = new NbtByteArray(data); continue; }
            if (data.Length > 0 && data[0] == (byte)NbtType.Compound)
            {
                var list = new NbtList(NbtType.Compound);
                try { foreach (var t in NbtBinary.ReadAll(data, NbtFormat.Bedrock)) list.Add(t.Tag); } catch { }
                root[$"Section {index}"] = list;
            }
            else root[$"Section {index}"] = new NbtByteArray(data);
        }
        return root;
    }

    public override ChunkInfo? GetChunkInfo(ChunkPos pos) => _chunks.TryGetValue(pos, out var c) ? new ChunkInfo { Pos = pos, StoredBytes = c.SubfileSize } : null;

    public override void DeleteChunks(IEnumerable<ChunkPos> chunks) =>
        throw new NotSupportedException("Deleting chunks is not supported for 3DS worlds yet (the chunk index format is only partly known).");

    public override void SaveChanges() { }
}
