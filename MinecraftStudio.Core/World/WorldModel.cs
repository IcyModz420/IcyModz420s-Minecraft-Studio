// MinecraftStudio.Core/World/WorldModel.cs: the edition-neutral world model every reader/writer shares — a loaded
// save (SaveWorld) made of dimensions (WorldDimension) of chunks, plus players, with read/save and rescue hooks.
// Owns: Edition, ChunkPos, ChunkInfo, WorldDimension, PlayerRecord, SaveWorld.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public enum Edition
{
    Java,
    Bedrock,
    PocketLegacy,
    LegacyConsole,
    Nintendo3DS,
}

public readonly record struct ChunkPos(int X, int Z)
{
    public override string ToString() => $"{X}, {Z}";
    public int RegionX => X >> 5;
    public int RegionZ => Z >> 5;
}

/// <summary>Per-chunk facts used by the map, the pruner and statistics.</summary>
public sealed class ChunkInfo
{
    public ChunkPos Pos { get; init; }
    public long InhabitedTime { get; set; } = -1;
    public long LastUpdate { get; set; } = -1;
    public string? Status { get; set; }
    public int DataVersion { get; set; }
    public long StoredBytes { get; set; }
    public DateTime? Timestamp { get; set; }
    public bool IsFull => Status is null or "full" or "minecraft:full" or "populated" or "postprocessed";
}

/// <summary>One dimension of a world, as a set of chunks.</summary>
public abstract class WorldDimension
{
    protected WorldDimension(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    /// <summary>e.g. minecraft:overworld, minecraft:the_nether, or a Bedrock dimension number.</summary>
    public string Id { get; }
    public string DisplayName { get; }
    public virtual bool HasSky => !Id.Contains("nether", StringComparison.OrdinalIgnoreCase) && Id != "-1" && Id != "1";

    /// <summary>All stored chunk positions.</summary>
    public abstract IReadOnlyCollection<ChunkPos> ChunkPositions { get; }

    /// <summary>Decoded blocks of a chunk, or null if it is not stored.</summary>
    public abstract ChunkColumn? LoadChunk(ChunkPos pos);

    /// <summary>Cheap metadata for a chunk (may parse the chunk).</summary>
    public abstract ChunkInfo? GetChunkInfo(ChunkPos pos);

    /// <summary>Raw chunk NBT for the NBT editor (null if the format has no single chunk compound).</summary>
    public virtual NbtCompound? LoadChunkNbt(ChunkPos pos) => null;

    /// <summary>True when <see cref="SaveChunkNbt"/> can write an edited chunk back.</summary>
    public virtual bool CanSaveChunkNbt => false;

    /// <summary>Writes an edited chunk compound (as returned by <see cref="LoadChunkNbt"/>) straight back to disk.</summary>
    public virtual void SaveChunkNbt(ChunkPos pos, NbtCompound nbt) => throw new NotSupportedException("This format does not support editing chunk NBT.");

    /// <summary>Removes chunks. Changes are written on <see cref="SaveChanges"/>.</summary>
    public abstract void DeleteChunks(IEnumerable<ChunkPos> chunks);

    /// <summary>True when a deletion can be taken back until the next save (undo of a prune).</summary>
    public virtual bool CanRestoreChunks => false;

    /// <summary>Puts back chunks deleted since the last save, exactly as they were. Returns the chunks restored.</summary>
    public virtual IReadOnlyList<ChunkPos> RestoreChunks(IEnumerable<ChunkPos> chunks) => [];

    public virtual bool IsModified => false;
    public abstract void SaveChanges();
    public virtual void DiscardChanges() { }
}

/// <summary>A player record (inventory, position) stored somewhere in a world.</summary>
public sealed class PlayerRecord
{
    public PlayerRecord(string name, string source, NbtCompound data)
    {
        Name = name;
        Source = source;
        Data = data;
    }

    /// <summary>UUID, name or "Local player".</summary>
    public string Name { get; }
    /// <summary>Where it lives (file path or database key).</summary>
    public string Source { get; }
    public NbtCompound Data { get; set; }
}

/// <summary>A loaded save of any edition.</summary>
public abstract class SaveWorld : IDisposable
{
    public abstract Edition Edition { get; }
    public abstract string Name { get; }
    /// <summary>Folder or file the world was opened from.</summary>
    public abstract string Location { get; }
    /// <summary>Human-readable format/version, e.g. "Java 1.20.1 (Anvil, data version 3465)".</summary>
    public abstract string FormatDescription { get; }
    public abstract IReadOnlyList<WorldDimension> Dimensions { get; }
    public virtual IReadOnlyList<PlayerRecord> Players => [];
    /// <summary>The main level data (level.dat) if there is one.</summary>
    public virtual NbtFile? LevelData => null;
    public virtual bool IsModified => Dimensions.Any(d => d.IsModified);
    public abstract void Save();
    public virtual void Dispose() { GC.SuppressFinalize(this); }

    /// <summary>Opened for rescue: a damaged world is only read (its rescue is written as a new world), never saved.</summary>
    public bool OpenedForRescue { get; protected set; }

    /// <summary>The message for anything that would write to a world opened for rescue.</summary>
    public const string RescueReadOnly = "This world was opened for rescue, so it is only read and never changed. Rescue it into a new world to keep working on it.";

    /// <summary>Key/value facts shown in the world summary.</summary>
    public virtual IEnumerable<(string Key, string Value)> Summary() => [];
}
