// MinecraftStudio.Core/LegacyConsole/ConsoleDimension.cs: one dimension of a Legacy Console world.
// Owns: ConsoleDimension.
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.LegacyConsole;

public sealed class ConsoleDimension : WorldDimension
{
    readonly ConsoleWorld _world;
    readonly Dictionary<(int, int), string> _regionNames;
    readonly Dictionary<(int, int), ConsoleRegion> _regions = [];
    readonly Dictionary<ChunkPos, ((int, int) Region, int Index)> _index = [];

    internal ConsoleDimension(ConsoleWorld world, string id, string name, Dictionary<(int, int), string> regionNames) : base(id, name)
    {
        _world = world;
        _regionNames = regionNames;
        foreach (var (key, file) in regionNames)
        {
            var r = ConsoleRegion.Parse(world.ReadRegionBytes(file), world.Platform);
            _regions[key] = r;
            foreach (int i in r.Indexes())
                _index[new ChunkPos(key.Item1 * 32 + (i & 31), key.Item2 * 32 + (i >> 5))] = (key, i);
        }
    }

    public override bool HasSky => !Id.Contains("nether");
    public override IReadOnlyCollection<ChunkPos> ChunkPositions => _index.Keys;
    public override bool IsModified => _regions.Values.Any(r => r.Modified);

    public byte[]? ChunkBytes(ChunkPos pos) => _index.TryGetValue(pos, out var loc) ? _regions[loc.Region].Decode(loc.Index) : null;

    /// <summary>The chunk's stored record (compressed as on disk, with its platform flags), or null.</summary>
    internal ConsoleRegion.Chunk? RawChunk(ChunkPos pos) => _index.TryGetValue(pos, out var loc) ? _regions[loc.Region][loc.Index] : null;

    /// <summary>Replaces a stored chunk record (must be encoded for this world's platform); written on the next save.</summary>
    internal bool ReplaceChunk(ChunkPos pos, ConsoleRegion.Chunk chunk)
    {
        if (!_index.TryGetValue(pos, out var loc)) return false;
        _regions[loc.Region].Restore(loc.Index, chunk);
        return true;
    }

    /// <summary>
    /// Writes decoded chunk bytes over a stored chunk, compressed for this world's own platform (and RLE-encoded when
    /// <paramref name="flags"/> asks for it); written on the next save. False when the chunk is not stored here.
    /// </summary>
    internal bool WriteChunk(ChunkPos pos, byte[] data, uint flags)
    {
        if (!_index.TryGetValue(pos, out var loc)) return false;
        _regions[loc.Region].Set(loc.Index, data, flags);
        return true;
    }

    public ConsoleChunkData? LoadConsoleChunk(ChunkPos pos)
    {
        var bytes = ChunkBytes(pos);
        return bytes == null ? null : ConsoleChunk.Decode(bytes, pos.X, pos.Z);
    }

    public override ChunkColumn? LoadChunk(ChunkPos pos) => LoadConsoleChunk(pos)?.Column;

    public override NbtCompound? LoadChunkNbt(ChunkPos pos) => LoadConsoleChunk(pos)?.Nbt;

    public override bool CanSaveChunkNbt => !_world.OpenedForRescue;

    /// <summary>
    /// Writes an edited chunk NBT back into the save: for the old NBT chunks the whole record; for the binary formats the
    /// NBT after the blocks (mobs, chests, ticks), spliced in with the blocks kept byte for byte. The new record is decoded
    /// first and must hold the same blocks (binary formats) and exactly the NBT given, else nothing is written; then it is
    /// compressed for the save's own platform and the save is written (an Xbox 360 package is rehashed, as every save is).
    /// Refused while the world has other unsaved changes: a console save is written as one file.
    /// </summary>
    public override void SaveChunkNbt(ChunkPos pos, NbtCompound nbt)
    {
        if (_world.OpenedForRescue) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        if (_world.Dimensions.Any(d => d.IsModified)) throw new InvalidOperationException("Save the world's other changes first: a console save is written as one file.");
        var raw = ChunkBytes(pos) ?? throw new IOException($"Chunk {pos} is no longer in the save.");
        var before = ConsoleChunk.Decode(raw, pos.X, pos.Z);
        if (before.NbtStart < 0 || RawChunk(pos) is not ConsoleRegion.Chunk stored) throw new NotSupportedException("This chunk keeps no NBT to change.");
        NbtBinary.ReadCompound(raw, NbtFormat.Java, out string rootName, before.NbtStart);
        byte[] tail = NbtBinary.Serialize(nbt, NbtFormat.Java, rootName);
        byte[] record = [.. raw.AsSpan(0, before.NbtStart), .. tail, .. raw.AsSpan(before.NbtEnd)];
        var after = ConsoleChunk.Decode(record, pos.X, pos.Z);
        bool nbtFormat = before.Format is ConsoleChunkFormat.Nbt128 or ConsoleChunkFormat.Nbt256;
        if (after.Nbt == null || !after.Nbt.ContentEquals(nbt) || after.Format != before.Format || !nbtFormat && !SameBlocks(before.Column, after.Column))
            throw new IOException($"Chunk {pos} did not read back as edited, so nothing was written.");
        if (!WriteChunk(pos, record, stored.Flags)) throw new IOException($"Chunk {pos} is no longer in the save.");
        _world.Save();
    }

    static bool SameBlocks(ChunkColumn a, ChunkColumn b)
    {
        for (int sy = Math.Min(a.MinSection, b.MinSection); sy <= Math.Max(a.MaxSection, b.MaxSection); sy++)
            for (int y = sy * 16; y < sy * 16 + 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        if (a.GetBlock(x, y, z) != b.GetBlock(x, y, z)) return false;
        return true;
    }

    public override ChunkInfo? GetChunkInfo(ChunkPos pos)
    {
        if (!_index.TryGetValue(pos, out var loc)) return null;
        var c = _regions[loc.Region][loc.Index]!;
        var data = LoadConsoleChunk(pos);
        return new ChunkInfo
        {
            Pos = pos,
            StoredBytes = c.Payload.Length,
            InhabitedTime = data?.InhabitedTime ?? -1,
            LastUpdate = data?.LastUpdate ?? -1,
            DataVersion = data?.Version ?? 0,
        };
    }

    readonly Dictionary<ChunkPos, (((int, int) Region, int Index) Location, ConsoleRegion.Chunk Chunk)> _deleted = [];
    /// <summary>Regions that had no unsaved changes before chunks were deleted from them.</summary>
    readonly HashSet<(int, int)> _cleanBeforeDelete = [];

    public override void DeleteChunks(IEnumerable<ChunkPos> chunks)
    {
        foreach (var pos in chunks)
            if (_index.Remove(pos, out var loc))
            {
                if (_regions[loc.Region][loc.Index] is ConsoleRegion.Chunk chunk) _deleted[pos] = (loc, chunk);
                if (!_regions[loc.Region].Modified) _cleanBeforeDelete.Add(loc.Region);
                _regions[loc.Region].Delete(loc.Index);
            }
    }

    public override bool CanRestoreChunks => true;

    public override IReadOnlyList<ChunkPos> RestoreChunks(IEnumerable<ChunkPos> chunks)
    {
        var restored = new List<ChunkPos>();
        foreach (var pos in chunks)
        {
            if (_index.ContainsKey(pos) || !_deleted.Remove(pos, out var d)) continue;
            _regions[d.Location.Region].Restore(d.Location.Index, d.Chunk);
            _index[pos] = d.Location;
            restored.Add(pos);
        }
        // A region whose deletions were all taken back is as it was: nothing to save there.
        foreach (var region in _cleanBeforeDelete.ToList())
            if (!_deleted.Values.Any(d => d.Location.Region == region)) { _regions[region].MarkSaved(); _cleanBeforeDelete.Remove(region); }
        return restored;
    }

    internal void FlushRegions()
    {
        _deleted.Clear();
        _cleanBeforeDelete.Clear();
        foreach (var (key, region) in _regions)
        {
            if (!region.Modified) continue;
            _world.WriteRegionBytes(_regionNames[key], region.ToBytes());
            region.MarkSaved();
        }
    }

    public override void SaveChanges() => _world.Save();
}
