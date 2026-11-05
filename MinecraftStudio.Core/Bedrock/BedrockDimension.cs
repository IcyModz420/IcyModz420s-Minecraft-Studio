// MinecraftStudio.Core/Bedrock/BedrockDimension.cs: one dimension of a Bedrock (LevelDB) world: enumerates chunk keys and reads/writes chunk NBT.
// Owns: BedrockDimension.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Bedrock;

public sealed class BedrockDimension : WorldDimension
{
    readonly BedrockWorld _world;
    Dictionary<ChunkPos, List<byte[]>> _keys;
    HashSet<ChunkPos> _generated;

    internal BedrockDimension(BedrockWorld world, int dim, string id, string name, Dictionary<ChunkPos, List<byte[]>> keys, HashSet<ChunkPos> generated) : base(id, name)
    {
        _world = world;
        Dimension = dim;
        _keys = keys;
        _generated = generated;
    }

    internal void Reset(Dictionary<ChunkPos, List<byte[]>> keys, HashSet<ChunkPos> generated)
    {
        _keys = keys;
        _generated = generated;
    }

    public int Dimension { get; }
    public override bool HasSky => Dimension != 1;
    /// <summary>Generated chunks (those with a version record).</summary>
    public override IReadOnlyCollection<ChunkPos> ChunkPositions => _generated;
    /// <summary>Every position that has any chunk record (includes structure pre-placement data).</summary>
    public IReadOnlyCollection<ChunkPos> AllPositions => _keys.Keys;
    public override bool IsModified => _world.Db.HasChanges;

    public IReadOnlyList<byte[]> KeysAt(ChunkPos pos) => _keys.TryGetValue(pos, out var l) ? l : [];

    byte[]? Get(ChunkPos pos, ChunkTag tag, sbyte? sub = null) => _world.Db.Get(BedrockKeys.Make(pos.X, pos.Z, Dimension, tag, sub));

    /// <summary>Chunk version (Version or LegacyVersion record).</summary>
    public int? ChunkVersion(ChunkPos pos)
    {
        var v = Get(pos, ChunkTag.Version) ?? Get(pos, ChunkTag.LegacyVersion);
        return v is { Length: > 0 } ? v[0] : null;
    }

    public override ChunkColumn? LoadChunk(ChunkPos pos)
    {
        if (!_keys.TryGetValue(pos, out var keys)) return null;
        // 1.18+ overworld chunks use sub-chunks -4..19; Data3D biomes start at the lowest one.
        bool caves = Dimension == 0 && keys.Any(k => BedrockKeys.TryParseChunkKey(k, out var c) && (c.Tag == ChunkTag.Data3D || (c.Tag == ChunkTag.SubChunkPrefix && c.SubChunk < 0)));
        int minSection = caves ? -4 : 0;
        int maxSection = Dimension switch { 0 when caves => 19, 1 => 7, _ => 15 };
        var col = new ChunkColumn(pos.X, pos.Z, minSection, maxSection) { BiomeScheme = BiomeIdScheme.Bedrock };
        foreach (var key in keys)
        {
            if (!BedrockKeys.TryParseChunkKey(key, out var ck)) continue;
            switch (ck.Tag)
            {
                case ChunkTag.SubChunkPrefix:
                    {
                        var v = _world.Db.Get(key);
                        if (v == null) break;
                        var s = BedrockChunk.DecodeSubChunk(v, ck.SubChunk);
                        if (s != null) col.Sections[s.Y] = s;
                        break;
                    }
                case ChunkTag.LegacyTerrain:
                    {
                        var v = _world.Db.Get(key);
                        if (v != null) BedrockChunk.DecodeLegacyTerrain(v, col);
                        break;
                    }
                case ChunkTag.Data3D:
                    {
                        var v = _world.Db.Get(key);
                        if (v != null) BedrockChunk.DecodeData3D(v, col, minSection);
                        break;
                    }
                case ChunkTag.Data2D:
                case ChunkTag.Data2DLegacy:
                    {
                        if (col.BiomeIds != null) break;
                        var v = _world.Db.Get(key);
                        if (v != null) BedrockChunk.DecodeData2D(v, col, ck.Tag == ChunkTag.Data2DLegacy);
                        break;
                    }
                case ChunkTag.BlockEntity:
                    col.BlockEntityCount = CountRoots(_world.Db.Get(key));
                    break;
                case ChunkTag.Entity:
                    col.EntityCount += CountRoots(_world.Db.Get(key));
                    break;
            }
        }
        return col;
    }

    static int CountRoots(byte[]? v)
    {
        if (v == null) return 0;
        try { return NbtBinary.ReadAll(v, NbtFormat.Bedrock).Count; } catch { return 0; }
    }

    public override ChunkInfo? GetChunkInfo(ChunkPos pos)
    {
        if (!_keys.TryGetValue(pos, out var keys)) return null;
        long bytes = keys.Sum(k => (long)_world.Db.ValueLength(k));
        return new ChunkInfo { Pos = pos, StoredBytes = bytes, DataVersion = ChunkVersion(pos) ?? 0, Status = FinalizedName(pos) };
    }

    string? FinalizedName(ChunkPos pos)
    {
        var v = Get(pos, ChunkTag.FinalizedState);
        if (v is not { Length: >= 4 }) return null;
        return BinaryPrimitives.ReadInt32LittleEndian(v) switch { 0 => "needs ticking", 1 => "needs population", 2 => "full", var n => n.ToString() };
    }

    /// <summary>The bytes stored under one of this dimension's chunk keys (null when there are none).</summary>
    public byte[]? ReadValue(byte[] key) => _world.Db.Get(key);

    /// <summary>The NBT records stored under one of this dimension's chunk keys (none when there are none or they cannot be read).</summary>
    public IReadOnlyList<NbtCompound> ReadRecords(byte[] key)
    {
        var v = _world.Db.Get(key);
        if (v == null) return [];
        try { return NbtBinary.ReadAll(v, NbtFormat.Bedrock).Select(t => t.Tag).OfType<NbtCompound>().ToList(); }
        catch (Exception e) when (e is not OutOfMemoryException) { return []; } // a damaged record reads as none, like LoadChunkNbt
    }

    /// <summary>The Bedrock "chunk" as editable NBT: one compound per record that holds NBT.</summary>
    public override NbtCompound? LoadChunkNbt(ChunkPos pos)
    {
        if (!_keys.TryGetValue(pos, out var keys)) return null;
        var root = new NbtCompound();
        foreach (var key in keys)
        {
            if (!BedrockKeys.TryParseChunkKey(key, out var ck)) continue;
            if (ck.Tag is ChunkTag.BlockEntity or ChunkTag.Entity or ChunkTag.PendingTicks or ChunkTag.RandomTicks)
            {
                var v = _world.Db.Get(key);
                if (v == null) continue;
                var list = new NbtList(NbtType.Compound);
                try { foreach (var t in NbtBinary.ReadAll(v, NbtFormat.Bedrock)) list.Add(t.Tag); } catch { }
                root[ck.Tag.ToString()] = list;
            }
        }
        return root;
    }

    public override bool CanSaveChunkNbt => true;

    /// <summary>Writes the record lists of <see cref="LoadChunkNbt"/> back (BlockEntity, Entity, PendingTicks, RandomTicks) and commits to the log.</summary>
    public override void SaveChunkNbt(ChunkPos pos, NbtCompound nbt)
    {
        var db = _world.Db;
        foreach (var tag in new[] { ChunkTag.BlockEntity, ChunkTag.Entity, ChunkTag.PendingTicks, ChunkTag.RandomTicks })
        {
            var key = BedrockKeys.Make(pos.X, pos.Z, Dimension, tag);
            if (nbt[tag.ToString()] is not NbtList list)
            {
                if (db.ContainsKey(key)) db.Delete(key);
                continue;
            }
            if (list.Count == 0) { if (db.ContainsKey(key)) db.Delete(key); continue; }
            db.Put(key, NbtBinary.SerializeAll(list.Select(t => new NamedTag("", t)), NbtFormat.Bedrock));
            if (!_keys.TryGetValue(pos, out var keys)) _keys[pos] = keys = [];
            if (!keys.Any(k => k.AsSpan().SequenceEqual(key))) keys.Add(key);
        }
        _world.Save(LevelDb.CommitMode.AppendLog);
    }

    readonly Dictionary<ChunkPos, (List<byte[]> Keys, List<byte[]> Actors, bool Generated)> _deleted = [];

    public override void DeleteChunks(IEnumerable<ChunkPos> chunks)
    {
        var db = _world.Db;
        foreach (var pos in chunks)
        {
            bool generated = _generated.Remove(pos);
            if (!_keys.Remove(pos, out var keys)) continue;
            foreach (var k in keys) db.Delete(k);
            // Entities stored separately since 1.18.30: digp lists actor ids, each actor has its own key.
            var actors = new List<byte[]>();
            var digp = BedrockKeys.DigestKey(pos.X, pos.Z, Dimension);
            var ids = db.Get(digp);
            if (ids != null)
            {
                for (int i = 0; i + 8 <= ids.Length; i += 8) actors.Add(BedrockKeys.ActorKey(ids.AsSpan(i, 8)));
                actors.Add(digp);
                foreach (var a in actors) db.Delete(a);
            }
            _deleted[pos] = (keys, actors, generated);
        }
    }

    public override bool CanRestoreChunks => true;

    public override IReadOnlyList<ChunkPos> RestoreChunks(IEnumerable<ChunkPos> chunks)
    {
        var db = _world.Db;
        var restored = new List<ChunkPos>();
        foreach (var pos in chunks)
        {
            if (!_deleted.Remove(pos, out var d)) continue;
            // Only deletions that were not saved yet can be taken back.
            bool any = false;
            foreach (var k in d.Keys.Concat(d.Actors)) any |= db.Undelete(k);
            if (!any) continue;
            _keys[pos] = d.Keys;
            if (d.Generated) _generated.Add(pos);
            restored.Add(pos);
        }
        return restored;
    }

    public override void SaveChanges() { _world.Save(CommitMode.Compact); _deleted.Clear(); }
    public override void DiscardChanges() { _world.Db.DiscardChanges(); _deleted.Clear(); }
}
