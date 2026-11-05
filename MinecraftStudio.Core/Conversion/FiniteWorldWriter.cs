// MinecraftStudio.Core/Conversion/FiniteWorldWriter.cs: Writes a finite Classic or Indev level centred on the
// source spawn.
// Owns: FiniteWorldWriter.
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Writes a finite Classic or Indev level centred on the source spawn. Classic: level.dat in the version-1 header format
/// every Classic build loads (0.0.13a – 0.30), plus a ClassicWorld .cw for ClassiCube and Classic servers. Indev: an
/// .mclevel (NBT MinecraftLevel with Map, Environment and About).
/// </summary>
public sealed class FiniteWorldWriter : IWorldWriter, IAreaLimitedWriter
{
    readonly string _folder;
    readonly ConversionTarget _target;
    readonly ConversionOptions _options;
    readonly int _width, _length, _height;
    /// <summary>Source y of level y 0 (Classic: sea level 63 lands on the Classic water line, height/2).</summary>
    readonly int _baseY;
    readonly byte[] _blocks;
    readonly byte[] _data;
    readonly Dictionary<int, (int Id, int Data)> _cache = [];
    readonly List<string> _notes = [];
    WorldInfo? _info;
    (int X, int Z) _origin; // source block coordinates of level (0, 0)
    int _chunks;

    /// <summary>The source heights a level keeps: Classic the 64 blocks around sea level (y 31–94), Indev y 0–127.</summary>
    public static (int Min, int Max) SourceHeights(ConversionTarget target) => target.Kind == TargetKind.Classic ? (63 - 32, 63 + 31) : (0, 127);

    public FiniteWorldWriter(string folder, ConversionTarget target, ConversionOptions options)
    {
        _folder = folder;
        _target = target;
        _options = options;
        _width = Math.Clamp(options.FiniteWidth / 16 * 16, 64, 1024);
        _length = Math.Clamp(options.FiniteLength / 16 * 16, 64, 1024);
        (_baseY, int top) = SourceHeights(target);
        _height = top - _baseY + 1;
        _blocks = new byte[_width * _length * _height];
        _data = new byte[_blocks.Length];
        Directory.CreateDirectory(folder);
    }

    public IReadOnlyList<string> Notes => _notes;
    /// <summary>The level file written (what to open afterwards).</summary>
    public string? PrimaryFile { get; private set; }
    public bool SupportsDimension(string id) => id == "minecraft:overworld";
    int Index(int x, int y, int z) => (y * _length + z) * _width + x;

    /// <summary>Source block coordinates of level (0, 0): centred on the spawn, on a chunk boundary.</summary>
    (int X, int Z) Origin(WorldInfo info) =>
        ((((_options.FiniteCenterX ?? info.SpawnX) >> 4) - _width / 32) * 16, (((_options.FiniteCenterZ ?? info.SpawnZ) >> 4) - _length / 32) * 16);

    public (int MinX, int MinZ, int MaxX, int MaxZ)? SourceArea(string dimensionId, WorldInfo info)
    {
        _info = info;
        var (ox, oz) = Origin(info);
        return (ox >> 4, oz >> 4, (ox + _width - 1) >> 4, (oz + _length - 1) >> 4);
    }

    public void BeginDimension(string id) => _origin = Origin(_info ?? new WorldInfo());
    public void EndDimension() { }

    (int Id, int Data) Block(BlockState java)
    {
        if (_cache.TryGetValue(java.Id, out var b)) return b;
        b = java.IsAir ? (0, 0)
            // Indev kept Classic's coloured cloth (ids 21–36); everything else uses the Indev block ids.
            : _target.Kind == TargetKind.Classic || java.BaseName.EndsWith("_wool", StringComparison.Ordinal) ? (JavaBlocks.ToClassic(java), 0)
            : JavaBlocks.ToLegacy(java, LegacyLevel.Indev);
        _cache[java.Id] = b;
        return b;
    }

    public void WriteChunk(CanonicalChunk c)
    {
        // This writer keeps blocks only: say what that leaves behind.
        foreach (var be in c.BlockEntities)
            if (BlockEntities.CarriedItems(be).Count > 0) BlockEntities.Drop(be, $"{_target.Name} levels are written with blocks only");
        foreach (var e in c.Entities) Entities.Drop(e, $"{_target.Name} levels are written with blocks only");
        int x0 = c.X * 16 - _origin.X, z0 = c.Z * 16 - _origin.Z;
        bool any = false;
        foreach (var (sy, s) in c.Sections)
        {
            var mapped = s.Palette.Select(Block).ToArray();
            for (int i = 0; i < 4096; i++)
            {
                int lx = x0 + (i & 15), lz = z0 + ((i >> 4) & 15), ly = sy * 16 + (i >> 8) - _baseY;
                if (lx < 0 || lx >= _width || lz < 0 || lz >= _length || ly < 0 || ly >= _height) continue;
                var (id, data) = mapped[s.Indices?[i] ?? 0];
                int at = Index(lx, ly, lz);
                _blocks[at] = (byte)id;
                _data[at] = (byte)((data & 15) << 4);
                any = true;
            }
        }
        if (any) _chunks++;
    }

    /// <summary>Classic and Indev have no bedrock layer of their own: the bottom becomes bedrock where it was left open.</summary>
    void FinishTerrain()
    {
        for (int z = 0; z < _length; z++)
            for (int x = 0; x < _width; x++)
            {
                int at = Index(x, 0, z);
                if (_blocks[at] == 0 || _blocks[at] is 8 or 9 or 10 or 11) _blocks[at] = 7;
                // Indev light: full daylight down to the first block that stops it.
                if (_target.Kind != TargetKind.Indev) continue;
                for (int y = _height - 1; y >= 0; y--)
                {
                    int i = Index(x, y, z);
                    _data[i] = (byte)((_data[i] & 0xF0) | 15);
                    if (_blocks[i] is not (0 or 8 or 9 or 18 or 20)) break;
                }
            }
    }

    (int X, int Y, int Z) Spawn(WorldInfo info)
    {
        int x = Math.Clamp(info.SpawnX - _origin.X, 0, _width - 1), z = Math.Clamp(info.SpawnZ - _origin.Z, 0, _length - 1);
        int y = _height - 1;
        while (y > 0 && _blocks[Index(x, y - 1, z)] == 0) y--;
        return (x, Math.Min(_height - 1, y + 1), z);
    }

    public void Finish(WorldInfo info, IProgress<string>? progress = null)
    {
        FinishTerrain();
        var (sx, sy, sz) = Spawn(info);
        if (_target.Kind == TargetKind.Classic) WriteClassic(info, sx, sy, sz);
        else WriteIndev(info, sx, sy, sz);
        _notes.Add($"Wrote a {_width} × {_height} × {_length} {(_target.Kind == TargetKind.Classic ? "Classic" : "Indev")} level from {_chunks:N0} chunks around the spawn" +
                   (_target.Kind == TargetKind.Classic ? $" (source y {_baseY}–{_baseY + _height - 1}; the water line is at sea level)." : $" (source y 0–{_height - 1})."));
        _notes.Add("Blocks the version did not have became the closest block it had; chests, signs and mobs are not carried over.");
    }

    static string FileName(string name) => SafeName.For(name, "level");

    void WriteClassic(WorldInfo info, int sx, int sy, int sz)
    {
        var level = new ClassicLevel
        {
            Width = _width, Length = _length, Height = _height, Blocks = _blocks, Name = info.Name, Creator = AppInfo.Name,
            SpawnX = sx, SpawnY = sy, SpawnZ = sz,
        };
        // Not "level.dat": that name means a Java world to every tool that finds the folder.
        PrimaryFile = Path.Combine(_folder, FileName(info.Name) + ".dat");
        SafeFile.WriteAllBytes(PrimaryFile, level.BuildHeaderFormat());

        // ClassicWorld (ClassiCube, MCGalaxy and other Classic servers): gzip NBT, X/Y/Z dimensions and a YZX block array.
        var cw = new NbtCompound();
        cw.SetByte("FormatVersion", 1);
        cw.SetString("Name", info.Name);
        cw["UUID"] = new NbtByteArray(Guid.NewGuid().ToByteArray());
        cw.SetShort("X", (short)_width);
        cw.SetShort("Y", (short)_height);
        cw.SetShort("Z", (short)_length);
        cw["Spawn"] = new NbtCompound { { "X", new NbtShort((short)sx) }, { "Y", new NbtShort((short)sy) }, { "Z", new NbtShort((short)sz) }, { "H", new NbtByte(0) }, { "P", new NbtByte(0) } };
        cw["BlockArray"] = new NbtByteArray(_blocks);
        cw["Metadata"] = new NbtCompound();
        var file = new NbtFile { Format = NbtFormat.Java, Compression = CompressionKind.Gzip };
        file.Roots.Add(new NamedTag("ClassicWorld", cw));
        file.Save(Path.Combine(_folder, FileName(info.Name) + ".cw"));
    }

    void WriteIndev(WorldInfo info, int sx, int sy, int sz)
    {
        var map = new NbtCompound();
        map.SetShort("Width", (short)_width);
        map.SetShort("Length", (short)_length);
        map.SetShort("Height", (short)_height);
        var spawn = new NbtList(NbtType.Short);
        foreach (int v in new[] { sx, sy, sz }) spawn.Add(new NbtShort((short)v));
        map["Spawn"] = spawn;
        map["Blocks"] = new NbtByteArray(_blocks);
        map["Data"] = new NbtByteArray(_data);
        var env = new NbtCompound();
        env.SetShort("TimeOfDay", (short)(info.DayTime % 24000 / 1000 * 1000));
        env.SetByte("SkyBrightness", 15);
        env.SetInt("SkyColor", 0x99CCFF);
        env.SetInt("FogColor", 0xFFFFFF);
        env.SetInt("CloudColor", 0xFFFFFF);
        env.SetShort("CloudHeight", (short)Math.Min(_height + 2, 32767));
        env.SetByte("SurroundingGroundType", 2);
        env.SetShort("SurroundingGroundHeight", (short)Math.Clamp(62 - _baseY, 1, _height - 1));
        env.SetByte("SurroundingWaterType", 8);
        env.SetShort("SurroundingWaterHeight", (short)Math.Clamp(63 - _baseY, 1, _height - 1));
        var about = new NbtCompound();
        about.SetLong("CreatedOn", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        about.SetString("Name", info.Name);
        about.SetString("Author", AppInfo.Name);
        var root = new NbtCompound
        {
            { "About", about }, { "Environment", env }, { "Map", map },
            { "Entities", new NbtList(NbtType.Compound) }, { "TileEntities", new NbtList(NbtType.Compound) },
        };
        var file = new NbtFile { Format = NbtFormat.Java, Compression = CompressionKind.Gzip };
        file.Roots.Add(new NamedTag("MinecraftLevel", root));
        PrimaryFile = Path.Combine(_folder, FileName(info.Name) + ".mclevel");
        file.Save(PrimaryFile);
    }

    public void Dispose() { }
}
