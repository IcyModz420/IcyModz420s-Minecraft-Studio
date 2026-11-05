// Structures/Structure.cs: a cuboid of blocks lifted out of a world (a "building").
// Owns: the Structure model — a grid of canonical block states, the block entities (chest contents, signs) and
// entities inside it, and the source data version. Blocks use canonical Java names, so a structure cropped from any
// edition can be placed into any other. StructureNbt saves/loads it as a Java structure .nbt; StructureClip does the
// crop out of a world and the place back into one.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

/// <summary>An entity captured in a structure, positioned relative to the structure's low corner.</summary>
public sealed record StructureEntity(double X, double Y, double Z, NbtCompound Nbt);

/// <summary>
/// A box of blocks lifted out of a world, independent of edition and version: a grid of block states plus the block
/// entities (chest contents, signs) and entities inside it. Blocks use canonical Java names, so a structure cropped
/// from one edition can be placed into any other. Saved and loaded as a Java structure .nbt (see <see cref="StructureNbt"/>).
/// </summary>
public sealed class Structure
{
    /// <summary>64 million blocks: far past any hand-built structure, a guard against a wild size.</summary>
    public const long MaxVolume = 64L * 1024 * 1024;

    readonly BlockState[] _blocks;

    public Structure(int sizeX, int sizeY, int sizeZ)
    {
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) throw new ArgumentException("A structure must be at least 1×1×1.");
        long volume = (long)sizeX * sizeY * sizeZ;
        if (volume > MaxVolume) throw new ArgumentException($"That area is too large for a structure ({volume:N0} blocks; the limit is {MaxVolume:N0}).");
        SizeX = sizeX; SizeY = sizeY; SizeZ = sizeZ;
        _blocks = new BlockState[volume];
        Array.Fill(_blocks, BlockState.Air);
    }

    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }
    public long Volume => (long)SizeX * SizeY * SizeZ;

    /// <summary>Block entity NBT (canonical Java form) by block index; positions are set when placed.</summary>
    public Dictionary<int, NbtCompound> BlockEntities { get; } = [];
    /// <summary>Entities inside the structure, positioned relative to its low corner.</summary>
    public List<StructureEntity> Entities { get; } = [];
    /// <summary>Source Java data version, written into the .nbt so a structure block loads it at the right version.</summary>
    public int DataVersion { get; set; }
    public string Author { get; set; } = AppInfo.Name;
    /// <summary>What the structure was cropped from, for the user (not part of the vanilla format).</summary>
    public string? Origin { get; set; }

    public int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;
    public bool InRange(int x, int y, int z) => (uint)x < (uint)SizeX && (uint)y < (uint)SizeY && (uint)z < (uint)SizeZ;
    public BlockState Get(int x, int y, int z) => _blocks[Index(x, y, z)];
    public void Set(int x, int y, int z, BlockState state) => _blocks[Index(x, y, z)] = state;

    /// <summary>All blocks in index order (y, then z, then x), for the writer.</summary>
    public IReadOnlyList<BlockState> AllBlocks => _blocks;

    public int NonAirCount
    {
        get { int n = 0; foreach (var b in _blocks) if (!b.IsAir) n++; return n; }
    }

    /// <summary>A short line for the user: size and how much is in it.</summary>
    public string Describe() => $"{SizeX}×{SizeY}×{SizeZ} ({NonAirCount:N0} blocks" +
        (BlockEntities.Count > 0 ? $", {BlockEntities.Count:N0} block entities" : "") +
        (Entities.Count > 0 ? $", {Entities.Count:N0} entities" : "") + ")";
}
