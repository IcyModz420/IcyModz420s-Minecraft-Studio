// MinecraftStudio.Core/Legacy/FiniteWorld.cs: An Indev .mclevel file (gzip NBT, root "MinecraftLevel").
// Owns: IndevLevel, FiniteWorld, FiniteDimension.
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Legacy;

/// <summary>An Indev .mclevel file (gzip NBT, root "MinecraftLevel").</summary>
public sealed class IndevLevel
{
    IndevLevel(NbtFile file) { File = file; }

    public NbtFile File { get; }
    public NbtCompound Root => File.RootCompound;
    public NbtCompound Map => Root.GetCompound("Map") ?? throw new SaveFormatException("The level has no Map compound.");
    public int Width => Map.GetInt("Width");
    public int Length => Map.GetInt("Length");
    public int Height => Map.GetInt("Height");
    public byte[] Blocks => Map.GetBytes("Blocks") ?? [];
    public byte[] Data => Map.GetBytes("Data") ?? [];
    public string Name => Root.GetCompound("About")?.GetString("Name") ?? "Indev level";

    public int Index(int x, int y, int z) => (y * Length + z) * Width + x;

    public static bool LooksLikeIndev(NbtFile f) => f.Roots.Count > 0 && f.RootName == "MinecraftLevel" && f.Root is NbtCompound c && c.ContainsKey("Map");

    public static IndevLevel Load(string path)
    {
        var f = NbtFile.Load(path);
        if (!LooksLikeIndev(f)) throw new SaveFormatException("Not an Indev level (root is not MinecraftLevel).");
        return new IndevLevel(f);
    }

    public (int X, int Y, int Z) Spawn
    {
        get
        {
            var s = Map.GetList("Spawn");
            return s is { Count: 3 } ? ((int)s[0].AsLong, (int)s[1].AsLong, (int)s[2].AsLong) : (Width / 2, Height, Length / 2);
        }
    }
}

/// <summary>
/// Classic and Indev worlds: one finite block array exposed as 16×16 chunks so the map, statistics and
/// converters work the same way as for chunked worlds. "Deleting" a chunk clears it to air.
/// </summary>
public sealed class FiniteWorld : SaveWorld
{
    readonly List<WorldDimension> _dims = [];

    FiniteWorld(string path) { Location_ = path; }

    string Location_ { get; }
    public ClassicLevel? Classic { get; private set; }
    public IndevLevel? Indev { get; private set; }

    public override Edition Edition => Edition.Java;
    public override string Name => Classic != null ? (string.IsNullOrEmpty(Classic.Name) ? Path.GetFileNameWithoutExtension(Location_) : Classic.Name) : Indev!.Name;
    public override string Location => Location_;
    public override string FormatDescription => Classic != null
        ? $"Java {Classic.Describe()}, {Classic.Width}×{Classic.Length}×{Classic.Height}"
        : $"Java Indev level, {Indev!.Width}×{Indev.Length}×{Indev.Height}";
    public override IReadOnlyList<WorldDimension> Dimensions => _dims;
    public override NbtFile? LevelData => Indev?.File;

    public static bool CanOpen(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            byte[] raw = File.ReadAllBytes(path);
            if (ClassicLevel.LooksLikeClassic(raw)) return true;
            var f = NbtFile.Parse(raw);
            return IndevLevel.LooksLikeIndev(f);
        }
        catch { return false; }
    }

    public static FiniteWorld Open(string path)
    {
        var w = new FiniteWorld(path);
        byte[] raw = File.ReadAllBytes(path);
        if (ClassicLevel.LooksLikeClassic(raw))
        {
            var c = ClassicLevel.Parse(raw);
            w.Classic = c;
            w._dims.Add(new FiniteDimension("minecraft:overworld", "Level", c.Width, c.Length, c.Height,
                (x, y, z) => (c.Blocks[c.Index(x, y, z)], 0), (x, y, z, id, _) => c.Blocks[c.Index(x, y, z)] = (byte)id,
                LegacyIdScheme.Classic, () => SafeFile.WriteAllBytes(path, c.ToBytes())));
        }
        else
        {
            var lvl = IndevLevel.Load(path);
            w.Indev = lvl;
            var blocks = lvl.Blocks;
            var data = lvl.Data;
            w._dims.Add(new FiniteDimension("minecraft:overworld", "Level", lvl.Width, lvl.Length, lvl.Height,
                (x, y, z) =>
                {
                    int i = lvl.Index(x, y, z);
                    return (blocks[i], i < data.Length ? data[i] >> 4 : 0);
                },
                (x, y, z, id, value) => { int i = lvl.Index(x, y, z); blocks[i] = (byte)id; if (i < data.Length) data[i] = (byte)((data[i] & 0x0F) | ((value & 15) << 4)); },
                LegacyIdScheme.Classic, () => lvl.File.Save(path)));
        }
        return w;
    }

    public override IEnumerable<(string Key, string Value)> Summary()
    {
        yield return ("Name", Name);
        yield return ("Format", FormatDescription);
        if (Classic != null)
        {
            if (Classic.Creator.Length > 0) yield return ("Creator", Classic.Creator);
            if (Classic.CreateTime > 0) yield return ("Created", DateTimeOffset.FromUnixTimeMilliseconds(Classic.CreateTime).LocalDateTime.ToString("g"));
            yield return ("Spawn", $"{Classic.SpawnX}, {Classic.SpawnY}, {Classic.SpawnZ}");
            if (Classic.Warning != null) yield return ("Warning", Classic.Warning);
        }
        else if (Indev != null)
        {
            var about = Indev.Root.GetCompound("About");
            if (about?.GetString("Author") is string a) yield return ("Author", a);
            if (about?.GetLongOrNull("CreatedOn") is long c) yield return ("Created", DateTimeOffset.FromUnixTimeMilliseconds(c).LocalDateTime.ToString("g"));
            var sp = Indev.Spawn;
            yield return ("Spawn", $"{sp.X}, {sp.Y}, {sp.Z}");
            yield return ("Entities", (Indev.Root.GetList("Entities")?.Count ?? 0).ToString());
            yield return ("Tile entities", (Indev.Root.GetList("TileEntities")?.Count ?? 0).ToString());
        }
    }

    public override void Save()
    {
        foreach (var d in _dims) if (d.IsModified) d.SaveChanges();
    }

    /// <summary>The Indev level's player — the LocalPlayer entity, with its inventory (Classic levels keep no player).</summary>
    public override IReadOnlyList<PlayerRecord> Players =>
        LocalPlayer() is NbtCompound p ? [new PlayerRecord("Local player", "Entities/LocalPlayer", p)] : [];

    NbtCompound? LocalPlayer() => Indev?.Root.GetList("Entities")?.OfType<NbtCompound>().FirstOrDefault(e => e.GetString("id") == "LocalPlayer");

    /// <summary>
    /// Whether the level keeps a spawn point that can be changed: Indev's Map.Spawn, and a Classic level the game saved from
    /// 0.0.14a on (<see cref="ClassicLevel.KeepsSpawn"/>).
    /// </summary>
    public bool HasSpawn => Indev != null ? Indev.Map.GetList("Spawn") is { Count: 3 } : Classic?.KeepsSpawn == true;

    /// <summary>
    /// Puts the spawn point at the block given (kept inside the level) and saves the level file — the whole level is one
    /// file, so the caller makes sure nothing else is waiting to be saved, and backs it up first.
    /// </summary>
    public void SetSpawn(int x, int y, int z)
    {
        if (OpenedForRescue) throw new InvalidOperationException(RescueReadOnly);
        if (!HasSpawn) throw new InvalidOperationException("This level keeps no spawn point (the oldest Classic levels are only blocks).");
        if (Indev != null)
        {
            x = Math.Clamp(x, 0, Indev.Width - 1); y = Math.Clamp(y, 0, Indev.Height); z = Math.Clamp(z, 0, Indev.Length - 1);
            // In the list's own number type (shorts).
            var old = Indev.Map.GetList("Spawn")!;
            var spawn = new NbtList(old.ElementType);
            foreach (int v in (int[])[x, y, z]) spawn.Add(old.ElementType switch { NbtType.Int => new NbtInt(v), NbtType.Byte => new NbtByte((sbyte)v), _ => new NbtShort((short)v) });
            Indev.Map["Spawn"] = spawn;
            Indev.File.Save(Location_);
            return;
        }
        var c = Classic!;
        c.SpawnX = Math.Clamp(x, 0, c.Width - 1); c.SpawnY = Math.Clamp(y, 0, c.Height); c.SpawnZ = Math.Clamp(z, 0, c.Length - 1);
        SafeFile.WriteAllBytes(Location_, c.ToBytes());
    }

    /// <summary>
    /// Writes an edited Indev player back over the level's LocalPlayer entity and saves the level file (Indev keeps
    /// the whole level, player included, in that one file).
    /// </summary>
    public void SavePlayer(PlayerRecord player)
    {
        if (OpenedForRescue) throw new InvalidOperationException(RescueReadOnly);
        if (Indev?.Root.GetList("Entities") is not NbtList entities) throw new InvalidOperationException("This level keeps no player.");
        int i = -1;
        for (int k = 0; k < entities.Count && i < 0; k++)
            if (entities[k] is NbtCompound e && e.GetString("id") == "LocalPlayer") i = k;
        if (i < 0) throw new InvalidOperationException("The level's player is no longer in it.");
        if (!ReferenceEquals(entities[i], player.Data)) entities[i] = player.Data;
        Indev.File.Save(Location_);
    }
}

/// <summary>A dimension backed by accessor functions over a finite block array.</summary>
public sealed class FiniteDimension : WorldDimension
{
    readonly int _w, _l, _h;
    readonly Func<int, int, int, (int Id, int Data)> _get;
    readonly Action<int, int, int, int, int> _set;
    readonly Dictionary<ChunkPos, (int Id, int Data)[]> _cleared = [];
    bool _cleanBeforeClear;
    readonly LegacyIdScheme _scheme;
    readonly Action _save;
    readonly List<ChunkPos> _positions = [];
    bool _modified;

    public FiniteDimension(string id, string name, int width, int length, int height,
        Func<int, int, int, (int, int)> get, Action<int, int, int, int, int> set, LegacyIdScheme scheme, Action save) : base(id, name)
    {
        _w = width; _l = length; _h = height;
        _get = get; _set = set; _scheme = scheme; _save = save;
        for (int cz = 0; cz < (length + 15) / 16; cz++)
            for (int cx = 0; cx < (width + 15) / 16; cx++)
                _positions.Add(new ChunkPos(cx, cz));
    }

    public int Width => _w;
    public int Length => _l;
    public int Height => _h;
    public override IReadOnlyCollection<ChunkPos> ChunkPositions => _positions;
    public override bool IsModified => _modified;

    public override ChunkColumn? LoadChunk(ChunkPos pos)
    {
        if (pos.X < 0 || pos.Z < 0 || pos.X * 16 >= _w || pos.Z * 16 >= _l) return null;
        var col = new ChunkColumn(pos.X, pos.Z, 0, (_h - 1) >> 4);
        for (int sy = 0; sy * 16 < _h; sy++)
        {
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int y = 0; y < 16; y++)
            {
                int wy = sy * 16 + y;
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        int wx = pos.X * 16 + x, wz = pos.Z * 16 + z;
                        (int id, int data) b = wy < _h && wx < _w && wz < _l ? _get(wx, wy, wz) : (0, 0);
                        int key = (b.id << 4) | (b.data & 15);
                        if (!map.TryGetValue(key, out ushort p))
                        {
                            p = (ushort)palette.Count;
                            palette.Add(LegacyBlocks.Get(b.id, b.data, _scheme));
                            map[key] = p;
                        }
                        idx[BlockSection.Index(x, y, z)] = p;
                    }
            }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            col.Sections[sy] = new BlockSection(sy, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
        return col;
    }

    public override ChunkInfo? GetChunkInfo(ChunkPos pos) =>
        pos.X < 0 || pos.Z < 0 || pos.X * 16 >= _w || pos.Z * 16 >= _l ? null : new ChunkInfo { Pos = pos, StoredBytes = 16L * 16 * _h };

    /// <summary>Finite worlds cannot lose chunks; the area is cleared to air instead.</summary>
    public override void DeleteChunks(IEnumerable<ChunkPos> chunks)
    {
        foreach (var pos in chunks)
        {
            // The blocks before the first clearing are kept, so the clearing can be undone until saved.
            if (_cleared.Count == 0) _cleanBeforeClear = !_modified;
            var before = _cleared.ContainsKey(pos) ? null : new (int, int)[16 * 16 * _h];
            int i = 0;
            for (int y = 0; y < _h; y++)
                for (int z = pos.Z * 16; z < pos.Z * 16 + 16; z++)
                    for (int x = pos.X * 16; x < pos.X * 16 + 16; x++, i++)
                    {
                        if (z >= _l || x >= _w || x < 0 || z < 0) continue;
                        if (before != null) before[i] = _get(x, y, z);
                        _set(x, y, z, 0, 0);
                        _modified = true;
                    }
            if (before != null) _cleared[pos] = before;
        }
    }

    public override bool CanRestoreChunks => true;

    public override IReadOnlyList<ChunkPos> RestoreChunks(IEnumerable<ChunkPos> chunks)
    {
        var restored = new List<ChunkPos>();
        foreach (var pos in chunks)
        {
            if (!_cleared.Remove(pos, out var before)) continue;
            int i = 0;
            for (int y = 0; y < _h; y++)
                for (int z = pos.Z * 16; z < pos.Z * 16 + 16; z++)
                    for (int x = pos.X * 16; x < pos.X * 16 + 16; x++, i++)
                        if (z < _l && x < _w && x >= 0 && z >= 0) _set(x, y, z, before[i].Id, before[i].Data);
            restored.Add(pos);
        }
        // Every clearing taken back and nothing else changed: the level is as it was on disk.
        if (_cleared.Count == 0 && _cleanBeforeClear) _modified = false;
        return restored;
    }

    public void SetBlock(int x, int y, int z, int id) => SetBlock(x, y, z, id, 0);

    /// <summary>Sets one block (with its data value) inside the level; ignored outside it. Written on <see cref="SaveChanges"/>.</summary>
    public void SetBlock(int x, int y, int z, int id, int data)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h || (uint)z >= (uint)_l) return;
        _set(x, y, z, id, data);
        _modified = true;
        _cleanBeforeClear = false;
    }

    public override void SaveChanges()
    {
        _save();
        _modified = false;
        _cleared.Clear();
    }
}
