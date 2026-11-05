// MinecraftStudio.Core/Conversion/CanonicalReader.cs: Which naming the decoded block states of a dimension use.
// Owns: StateSource, CanonicalReader.
using System.Collections.Concurrent;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>Which naming the decoded block states of a dimension use.</summary>
public enum StateSource
{
    /// <summary>Java 1.13+ palette names of some data version (canonicalized on read).</summary>
    JavaPalette,
    /// <summary>Already canonical (numeric Java / console / Classic ids decoded through the flattening table).</summary>
    JavaNumeric,
    /// <summary>Current Bedrock states (palettes, upgraded) or Pocket/3DS numeric ids decoded to current Bedrock states.</summary>
    Bedrock,
}

/// <summary>
/// Reads chunks of any supported world as <see cref="CanonicalChunk"/>s. Keeps a cache of recently read chunks so
/// neighbour-dependent fixes (fence links, stair corners, double chests) can look across chunk borders.
/// </summary>
public sealed partial class CanonicalReader
{
    readonly SaveWorld _world;
    readonly WorldDimension _dim;
    readonly HashSet<ChunkPos> _positions;
    readonly Dictionary<ChunkPos, (CanonicalChunk? Chunk, LinkedListNode<ChunkPos> Node)> _cache = [];
    readonly LinkedList<ChunkPos> _lru = new();
    const int CacheSize = 400;
    ChunkPos _lastPos = new(int.MinValue, int.MinValue);
    CanonicalChunk? _lastChunk;

    public CanonicalReader(SaveWorld world, WorldDimension dim)
    {
        _world = world;
        _dim = dim;
        _positions = [.. dim.ChunkPositions];
    }

    public WorldDimension Dimension => _dim;
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Names for the biome numbers the game itself doesn't have — a mod's, from Forge's table in level.dat — for a world that
    /// stores biomes as numbers (before 1.18). Only for looking (the mods report): a converting reader never has them, so
    /// no target is given a biome it doesn't know (those still read as plains).
    /// </summary>
    public IReadOnlyDictionary<int, string>? ModBiomeNumbers { get; init; }

    string JavaBiome(int id) => BiomeNames.KnowsJavaId(id) || ModBiomeNumbers == null || !ModBiomeNumbers.TryGetValue(id, out var name) ? BiomeNames.FromJavaId(id) : name;

    /// <summary>Canonical dimension id (minecraft:overworld / the_nether / the_end or a custom id).</summary>
    public static string DimensionId(WorldDimension d) => d.Id switch
    {
        "0" => "minecraft:overworld",
        "1" or "-1" or "DIM-1" => "minecraft:the_nether",
        "2" or "DIM1" => "minecraft:the_end",
        _ => d.Id,
    };

    public bool Exists(ChunkPos pos) => _positions.Contains(pos);

    /// <summary>The chunk (cached), or null when it is not stored or cannot be read.</summary>
    public CanonicalChunk? Get(ChunkPos pos)
    {
        if (pos == _lastPos) return _lastChunk;
        if (_cache.TryGetValue(pos, out var hit))
        {
            _lru.Remove(hit.Node);
            _lru.AddFirst(hit.Node);
            _lastPos = pos;
            _lastChunk = hit.Chunk;
            return hit.Chunk;
        }
        CanonicalChunk? c = null;
        if (_positions.Contains(pos))
        {
            try { c = ReadUncached(pos); }
            catch (Exception ex) { Warnings.Add($"Chunk {pos}: {ex.Message}"); }
        }
        _cache[pos] = (c, _lru.AddFirst(pos));
        while (_lru.Count > CacheSize)
        {
            var last = _lru.Last!;
            _cache.Remove(last.Value);
            _lru.RemoveLast();
            if (last.Value == _lastPos) { _lastPos = new(int.MinValue, int.MinValue); _lastChunk = null; }
        }
        _lastPos = pos;
        _lastChunk = c;
        return c;
    }

    /// <summary>
    /// Converts one entity or block entity; a damaged one is left out and reported instead of losing the whole chunk.
    /// </summary>
    void Keep(ChunkPos pos, string what, Action convert)
    {
        try { convert(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Warnings.Add($"Chunk {pos}: a damaged {what} could not be read ({ex.Message}) and was left out; the rest of the chunk was kept.");
        }
    }

    /// <summary>Block at absolute coordinates, or null when that chunk does not exist.</summary>
    public BlockState? BlockAt(int x, int y, int z) => Get(new ChunkPos(x >> 4, z >> 4))?.GetBlock(x & 15, y, z & 15);

    CanonicalChunk? ReadUncached(ChunkPos pos) => _dim switch
    {
        JavaRegionDimension or AlphaDimension => ReadJava(pos),
        BedrockDimension b => ReadBedrock(b, pos),
        ConsoleDimension con => ReadConsole(con, pos),
        PocketChunksDimension or ThreeDsDimension => ReadBedrockNumeric(pos),
        FiniteDimension => ReadFinite(pos),
        _ => ReadGeneric(pos),
    };
}
