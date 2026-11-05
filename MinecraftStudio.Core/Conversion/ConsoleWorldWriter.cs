// MinecraftStudio.Core/Conversion/ConsoleWorldWriter.cs: A writer that can only hold part of a world (finite
// worlds): which source chunks to read.
// Owns: IAreaLimitedWriter, ConsoleWorldWriter.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>A writer that can only hold part of a world (finite worlds): which source chunks to read.</summary>
public interface IAreaLimitedWriter
{
    /// <summary>Source chunk area (inclusive) for a dimension, or null for everything.</summary>
    (int MinX, int MinZ, int MaxX, int MaxZ)? SourceArea(string dimensionId, WorldInfo info);
}

/// <summary>
/// Writes a Legacy Console Edition save: 4J archive (save version 8 = TU17 layout, loaded by every later update),
/// McRegion-style region files with LZX/zlib/deflate-compressed, RLE'd "grid-128" v8 chunks, console level.dat, and
/// the platform's outer wrapper. The world is a finite square centred on the source spawn.
/// </summary>
public sealed partial class ConsoleWorldWriter : IWorldWriter, IAreaLimitedWriter
{
    const ushort ArchiveVersion = 8;
    const ushort ChunkVersion = 8;

    readonly string _output;
    readonly ConversionTarget _target;
    readonly ConversionOptions _options;
    readonly List<string> _notes = [];
    readonly Dictionary<string, ConsoleRegion> _regions = new(StringComparer.Ordinal);
    readonly int _size;
    string _dim = "minecraft:overworld";
    (int X, int Z) _offset;          // console chunk = source chunk - offset
    int _chunks;
    WorldInfo? _infoForArea;

    public ConsoleWorldWriter(string output, ConversionTarget target, ConversionOptions options)
    {
        _output = output;
        _target = target;
        _options = options;
        _size = Math.Clamp(options.ConsoleWorldChunks, 16, 320);
        Directory.CreateDirectory(output);
    }

    public IReadOnlyList<string> Notes => _notes;
    ConsolePlatform Platform => _target.Platform;
    int HellScale => _size switch { <= 64 => 3, <= 192 => 6, _ => 8 };
    int DimSize => _dim == "minecraft:overworld" ? _size : _dim == "minecraft:the_nether" ? Math.Max(18, (int)Math.Ceiling(_size / (double)HellScale)) : 18;

    public bool SupportsDimension(string id) => id is "minecraft:overworld" or "minecraft:the_nether" or "minecraft:the_end";

    /// <summary>Source chunks that land inside the console world (centred on the spawn; the Nether on spawn/8).</summary>
    public (int MinX, int MinZ, int MaxX, int MaxZ)? SourceArea(string dimensionId, WorldInfo info)
    {
        _infoForArea = info;
        var (ox, oz) = OffsetFor(dimensionId, info);
        string saved = _dim;
        _dim = dimensionId;
        int half = DimSize / 2;
        _dim = saved;
        return (ox - half, oz - half, ox + half - 1, oz + half - 1);
    }

    (int, int) OffsetFor(string dim, WorldInfo info)
    {
        int cx = (_options.FiniteCenterX ?? info.SpawnX) >> 4, cz = (_options.FiniteCenterZ ?? info.SpawnZ) >> 4;
        return dim switch
        {
            "minecraft:the_nether" => (cx / 8, cz / 8),
            "minecraft:the_end" => (0, 0),
            _ => (cx, cz),
        };
    }

    public void BeginDimension(string id)
    {
        _dim = id;
        _offset = OffsetFor(id, _infoForArea ?? new WorldInfo());
    }

    public void EndDimension() { }

    string RegionName(int rx, int rz) => (_dim switch { "minecraft:the_nether" => "DIM-1", "minecraft:the_end" => "DIM1/", _ => "" }) + $"r.{rx}.{rz}.mcr";

    public void WriteChunk(CanonicalChunk source)
    {
        int cx = source.X - _offset.X, cz = source.Z - _offset.Z;
        int half = DimSize / 2;
        if (cx < -half || cx >= half || cz < -half || cz >= half) return;
        var c = source.FitHeight(0, 15, bedrockFloor: _dim != "minecraft:the_end");
        byte[] payload = EncodeChunk(c, cx, cz, source.X * 16 - cx * 16, source.Z * 16 - cz * 16);
        string name = RegionName(cx >> 5, cz >> 5);
        if (!_regions.TryGetValue(name, out var region)) _regions[name] = region = ConsoleRegion.Parse([], Platform);
        uint timestamp = (uint)(ConsoleArchive.Now1601Ms() / 1000);
        region.Set(RegionFileIndex(cx, cz), payload, 0x80000000u, timestamp);
        _chunks++;
    }

    static int RegionFileIndex(int cx, int cz) => (cx & 31) + (cz & 31) * 32;
}
