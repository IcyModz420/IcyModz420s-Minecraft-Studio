// MinecraftStudio.Core/Conversion/BedrockWorldWriter.Chunks.cs: chunks for BedrockWorldWriter.
// Part of BedrockWorldWriter (main part in BedrockWorldWriter.cs).
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public partial class BedrockWorldWriter
{
    // ---------------------------------------------------------------- chunks

    /// <summary>Java item frame entities become Bedrock frame blocks with an ItemFrame block entity.</summary>
    static List<NbtCompound> PlaceItemFrames(CanonicalChunk c)
    {
        var frameEntities = new List<NbtCompound>();
        foreach (var e in c.Entities.ToList())
        {
            string id = e.GetString("id") ?? "";
            if (id is not ("minecraft:item_frame" or "minecraft:glow_item_frame")) continue;
            c.Entities.Remove(e);
            int x = e.GetInt("TileX"), y = e.GetInt("TileY"), z = e.GetInt("TileZ");
            if (!e.ContainsKey("TileX") && Entities.Position(e) is var (px, py, pz)) { x = (int)Math.Floor(px); y = (int)Math.Floor(py); z = (int)Math.Floor(pz); }
            if (x >> 4 != c.X || z >> 4 != c.Z) { Entities.Drop(e, "its block is in another chunk", itselfAsItem: true); continue; }
            var here = c.GetBlock(x & 15, y, z & 15);
            if (!here.IsAir && here.BaseName != "water")
            {
                Entities.Drop(e, $"on Bedrock a frame is a block, and its place is taken by {here.BaseName.Replace('_', ' ')}", itselfAsItem: true);
                continue;
            }
            int facing = Math.Clamp(e.GetInt("Facing", 2), 0, 5);
            bool glow = id == "minecraft:glow_item_frame";
            bool map = Items.Id(e.GetCompound("Item") ?? []) is "minecraft:filled_map";
            c.SetBlock(x & 15, y, z & 15, BlockState.Get(glow ? "minecraft:glow_frame" : "minecraft:frame",
                $"facing_direction={facing},item_frame_map_bit={(map ? "true" : "false")},item_frame_photo_bit=false"));
            var be = new NbtCompound();
            be.SetString("id", glow ? "GlowItemFrame" : "ItemFrame");
            be.SetByte("isMovable", 1);
            be.SetInt("x", x); be.SetInt("y", y); be.SetInt("z", z);
            if (e.GetCompound("Item") is NbtCompound item)
            {
                be["Item"] = Items.ToBedrock(item);
                be.SetFloat("ItemRotation", e.GetInt("ItemRotation") * 45f);
                be.SetFloat("ItemDropChance", (float)e.GetDouble("ItemDropChance", 1));
            }
            frameEntities.Add(be);
        }
        return frameEntities;
    }

    public void WriteChunk(CanonicalChunk c)
    {
        var frames = _options.ConvertEntities ? PlaceItemFrames(c) : [];
        _pendingFrames = frames;
        // Below 1.18 the world is 0–255 (0–127 before 1.0): cut to that height, with a bedrock floor where the ground goes on.
        if (!ModernHeight) c = c.FitHeight(0, Range(false).Max, bedrockFloor: _dim != 2);
        bool oldStyle = ModernHeight && _dim == 0 && !c.HasExtendedHeight && _options.LetGameExtendOldTerrain;
        bool modern = ModernHeight && !oldStyle;
        int version = oldStyle ? OldChunkVersion : ChunkVersion;
        bool wroteAny = Terrain == BedrockTerrain.LegacyTerrain ? WriteLegacyTerrain(c) : WriteSubChunks(c, modern);
        _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, version <= 20 ? ChunkTag.LegacyVersion : ChunkTag.Version), [(byte)version]);
        if (Terrain != BedrockTerrain.LegacyTerrain)
        {
            if (modern) _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.Data3D), EncodeData3D(c, Range(true).Min, Range(true).Max + (ChunkVersion == 39 ? 1 : 0)));
            else _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.Data2D), EncodeData2D(c));
            var finalized = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(finalized, 2);
            _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.FinalizedState), finalized);
        }
        if (_options.ConvertBlockEntities) WriteBlockEntities(c);
        // Actor storage (actorprefix/digp) from 1.18.30; older versions keep entities in the chunk's Entity record.
        bool actorStorage = modern && ChunkVersion >= 40;
        if (_options.ConvertEntities) WriteActors(c, actorStorage);
        else if (actorStorage) _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.ActorDigestVersion), [0]);
        if (wroteAny) _chunks++;
    }

    bool WriteSubChunks(CanonicalChunk c, bool modern)
    {
        var (minS, maxS) = Range(modern);
        int subVersion = Terrain switch
        {
            BedrockTerrain.Numeric => 0,
            BedrockTerrain.NameValSingle => 1,
            _ => modern ? 9 : 8,
        };
        bool wroteAny = false;
        for (int sy = minS; sy <= maxS; sy++)
        {
            if (!c.Sections.TryGetValue(sy, out var s) || s.IsEmpty) continue;
            var value = subVersion == 0 ? EncodeNumericSubChunk(s) : EncodeSubChunk(s, sy, subVersion);
            if (value == null) continue;
            _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.SubChunkPrefix, (sbyte)sy), value);
            wroteAny = true;
        }
        return wroteAny;
    }

    static int StorageBits(int paletteSize)
    {
        if (paletteSize <= 1) return 0;
        int needed = BinaryUtil.BitsFor(paletteSize);
        foreach (int b in new[] { 1, 2, 3, 4, 5, 6, 8, 16 }) if (b >= needed) return b;
        return 16;
    }

    byte[]? EncodeSubChunk(BlockSection s, int sy, int subVersion)
    {
        // Two layers: blocks, and water where the Java block is waterlogged (one layer for sub-chunk version 1).
        var mainPal = new List<Entry>();
        var mainMap = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var mainIdx = new ushort[4096];
        var waterIdx = new ushort[4096];
        bool anyWater = false, anyBlock = false;
        var converted = s.Palette.Select(Convert).ToArray();
        for (int i = 0; i < 4096; i++)
        {
            var e = converted[s.Indices?[i] ?? 0];
            if (!mainMap.TryGetValue(e.Key, out ushort pi))
            {
                pi = (ushort)mainPal.Count;
                mainPal.Add(e);
                mainMap[e.Key] = pi;
            }
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            int bi = (x << 8) | (z << 4) | y;
            mainIdx[bi] = pi;
            if (!e.Air) anyBlock = true;
            if (e.Water && subVersion != 1) { waterIdx[bi] = 1; anyWater = true; }
        }
        if (!anyBlock && !anyWater) return null;
        var o = new ByteWriter(8192);
        o.U8((byte)subVersion);
        if (subVersion != 1) o.U8((byte)(anyWater ? 2 : 1));
        if (subVersion >= 9) o.U8((byte)(sbyte)sy);
        WriteStorage(o, mainPal, mainIdx, allowZeroBits: subVersion >= 9);
        if (anyWater) WriteStorage(o, [AirEntry, WaterLayerEntry], waterIdx, allowZeroBits: subVersion >= 9);
        return o.ToArray();
    }

    static void WriteStorage(ByteWriter o, List<Entry> palette, ushort[] indices, bool allowZeroBits)
    {
        int bits = StorageBits(palette.Count);
        if (bits == 0 && !allowZeroBits) bits = 1;
        o.U8((byte)(bits << 1));
        if (bits == 0)
        {
            o.Bytes(palette[0].Palette);
            return;
        }
        int perWord = 32 / bits;
        int words = (4096 + perWord - 1) / perWord;
        for (int w = 0; w < words; w++)
        {
            uint word = 0;
            for (int k = 0; k < perWord; k++)
            {
                int i = w * perWord + k;
                if (i >= 4096) break;
                word |= (uint)indices[i] << (k * bits);
            }
            o.U32LE(word);
        }
        o.I32LE(palette.Count);
        foreach (var p in palette) o.Bytes(p.Palette);
    }

    /// <summary>Sub-chunk version 0 (1.1 – 1.2.10): 4096 block ids and 2048 bytes of data nibbles, index x·256 + z·16 + y.</summary>
    byte[]? EncodeNumericSubChunk(BlockSection s)
    {
        var converted = s.Palette.Select(Convert).ToArray();
        if (converted.All(e => e.Air)) return null;
        var o = new byte[1 + 4096 + 2048];
        o[0] = 0;
        for (int i = 0; i < 4096; i++)
        {
            var e = converted[s.Indices?[i] ?? 0];
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            int bi = (x << 8) | (z << 4) | y;
            o[1 + bi] = (byte)e.Id;
            SetNibble(o, 1 + 4096, bi, e.Data);
        }
        return o;
    }

    /// <summary>Pocket Edition 0.9–0.16 LegacyTerrain: one 83,200-byte record per chunk, 128 blocks high.</summary>
    bool WriteLegacyTerrain(CanonicalChunk c)
    {
        var data = new byte[83200];
        const int MetaAt = 32768, SkyAt = MetaAt + 16384, BlockLightAt = SkyAt + 16384, HeightAt = BlockLightAt + 16384, BiomeAt = HeightAt + 256;
        bool any = false;
        var sky = ChunkPacking.SkyLight(c, 0, 7, _dim != 1);
        var blockLight = ChunkPacking.BlockLight(c, 0, 7);
        foreach (var (sy, s) in c.Sections)
        {
            if (sy is < 0 or > 7) continue;
            var converted = s.Palette.Select(Convert).ToArray();
            for (int i = 0; i < 4096; i++)
            {
                var e = converted[s.Indices?[i] ?? 0];
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                int li = (x << 11) | (z << 7) | y;
                data[li] = (byte)e.Id;
                if (e.Id != 0) any = true;
                SetNibble(data, MetaAt, li, e.Data);
            }
        }
        for (int sy = 0; sy <= 7; sy++)
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                int li = (x << 11) | (z << 7) | y;
                SetNibble(data, SkyAt, li, sky[sy][i]);
                SetNibble(data, BlockLightAt, li, blockLight[sy][i]);
            }
        var heights = c.Heights();
        for (int i = 0; i < 256; i++)
        {
            data[HeightAt + i] = (byte)(heights[i] == int.MinValue ? 0 : Math.Clamp(heights[i] + 1, 0, 127));
            // Biome id first, then the grass colour (left black: the game recolours from the biome).
            data[BiomeAt + i * 4] = (byte)Math.Clamp(BiomeId(c.SurfaceBiome(i & 15, i >> 4, heights)), 0, 255);
        }
        _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.LegacyTerrain), data);
        return any;
    }

    /// <summary>Nibble arrays: low nibble = even index.</summary>
    static void SetNibble(byte[] arr, int offset, int index, int value)
    {
        int at = offset + (index >> 1);
        arr[at] = (index & 1) == 0 ? (byte)((arr[at] & 0xF0) | (value & 15)) : (byte)((arr[at] & 0x0F) | ((value & 15) << 4));
    }

    int BiomeId(string canonical) => BiomeNames.ToBedrockId(canonical, _game);

    byte[] EncodeData3D(CanonicalChunk c, int minS, int maxS)
    {
        var o = new ByteWriter(1024);
        var heights = c.Heights();
        int minY = minS * 16;
        for (int i = 0; i < 256; i++) o.I16LE((short)(heights[i] == int.MinValue ? 0 : Math.Clamp(heights[i] + 1 - minY, 0, (maxS - minS + 1) * 16)));
        int[]? previous = null;
        for (int sy = minS; sy <= maxS; sy++)
        {
            var ids = new int[4096];
            for (int cy = 0; cy < 4; cy++)
                for (int cz = 0; cz < 4; cz++)
                    for (int cx = 0; cx < 4; cx++)
                    {
                        int id = BiomeId(c.BiomeAt(cx * 4, sy * 16 + cy * 4, cz * 4));
                        for (int dx = 0; dx < 4; dx++)
                            for (int dz = 0; dz < 4; dz++)
                                for (int dy = 0; dy < 4; dy++)
                                    ids[((cx * 4 + dx) << 8) | ((cz * 4 + dz) << 4) | (cy * 4 + dy)] = id;
                    }
            if (previous != null && ids.AsSpan().SequenceEqual(previous)) { o.U8(0xFF); continue; }
            previous = ids;
            var palette = ids.Distinct().ToList();
            if (palette.Count == 1)
            {
                o.U8(1);
                o.I32LE(palette[0]);
                continue;
            }
            int bits = StorageBits(palette.Count);
            o.U8((byte)((bits << 1) | 1));
            var map = palette.Select((v, i) => (v, i)).ToDictionary(t => t.v, t => t.i);
            int perWord = 32 / bits, words = (4096 + perWord - 1) / perWord;
            for (int w = 0; w < words; w++)
            {
                uint word = 0;
                for (int k = 0; k < perWord; k++)
                {
                    int i = w * perWord + k;
                    if (i >= 4096) break;
                    word |= (uint)map[ids[i]] << (k * bits);
                }
                o.U32LE(word);
            }
            o.I32LE(palette.Count);
            foreach (var v in palette) o.I32LE(v);
        }
        return o.ToArray();
    }

    byte[] EncodeData2D(CanonicalChunk c)
    {
        var o = new ByteWriter(768);
        var heights = c.Heights();
        for (int i = 0; i < 256; i++) o.I16LE((short)(heights[i] == int.MinValue ? 0 : Math.Clamp(heights[i] + 1, 0, 256)));
        var surface = c.Heights();
        for (int i = 0; i < 256; i++) o.U8((byte)Math.Clamp(BiomeId(c.SurfaceBiome(i & 15, i >> 4, surface)), 0, 255));
        return o.ToArray();
    }

    List<NbtCompound> _pendingFrames = [];

    void WriteBlockEntities(CanonicalChunk c)
    {
        var roots = new List<NamedTag>();
        var have = new HashSet<(int, int, int)>();
        foreach (var f in _pendingFrames) { roots.Add(new NamedTag("", f)); have.Add((f.GetInt("x") & 15, f.GetInt("y"), f.GetInt("z") & 15)); }
        foreach (var be in c.BlockEntities)
        {
            if (StateEdit.Local(c, be) is not var (x, y, z)) continue;
            var block = c.GetBlock(x, y, z);
            if (BlockEntities.ToBedrock(be, block, c, NewActorId) is NbtCompound b) { roots.Add(new NamedTag("", b)); have.Add((x, y, z)); }
        }
        // Beds, banners, skulls, pots and note blocks need a block entity on Bedrock even when Java had none.
        foreach (var (sy, s) in c.Sections)
        {
            if (!s.Palette.Any(p => BlockEntities.ImpliedForBedrock(p, 0, 0, 0) != null)) continue;
            for (int i = 0; i < 4096; i++)
            {
                var b = s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]];
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                if (have.Contains((x, y, z))) continue;
                if (BlockEntities.ImpliedForBedrock(b, c.X * 16 + x, y, c.Z * 16 + z) is not NbtCompound implied) continue;
                if (BlockEntities.ToBedrock(implied, b, c) is NbtCompound be) { roots.Add(new NamedTag("", be)); have.Add((x, y, z)); }
            }
        }
        foreach (var r in roots) DowngradeItems(r.Tag);
        if (roots.Count > 0) _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.BlockEntity), NbtBinary.SerializeAll(roots, NbtFormat.Bedrock));
    }

    /// <summary>Older targets: item stacks get the names that version used (later versions upgrade them themselves).</summary>
    void DowngradeItems(NbtTag tag)
    {
        if (_game == 0) return;
        switch (tag)
        {
            case NbtCompound c:
                if (c["Name"] is NbtString && c.ContainsKey("Count"))
                {
                    Items.DowngradeBedrock(c);
                    // Before 1.2.13 (and in Pocket Edition 0.9–0.16) items are kept by number.
                    if (Terrain is BedrockTerrain.Numeric or BedrockTerrain.LegacyTerrain) Items.NumberBedrock(c);
                }
                foreach (var (_, v) in c) DowngradeItems(v);
                break;
            case NbtList l:
                foreach (var v in l) DowngradeItems(v);
                break;
        }
    }

    static string RawString(ReadOnlySpan<byte> bytes)
    {
        // Bytes outside ASCII are kept exactly through the NBT layer's surrogate escapes (U+DC80–U+DCFF).
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes) sb.Append(b < 0x80 ? (char)b : (char)(0xDC00 + b));
        return sb.ToString();
    }

    /// <summary>
    /// One entity and, as their own actors linked to it, the entities riding it. Returns its id, or null when the target
    /// can't have it; its riders are written either way (without it they stand where it was).
    /// </summary>
    long? AddActor(NbtCompound e, List<NbtCompound> actors)
    {
        string id = e.GetString("id") ?? "";
        var riders = e.GetList("Passengers")?.OfType<NbtCompound>().ToList() ?? [];
        // Converted without its riders: they are written on their own below, whatever becomes of it.
        var self = riders.Count > 0 ? Entities.Without(e, "Passengers") : e;
        NbtCompound? a = null;
        long unique = 0;
        if (!Entities.JavaKnown(id, Content)) Entities.Drop(self, $"{_target.Name} has no {ConversionLog.Friendly(id)}");
        else if (Entities.ToBedrock(self, unique = NewActorId(), OwnerId) is not NbtCompound made) _actorCounter--;
        else
        {
            a = made;
            DowngradeItems(a);
            actors.Add(a);
            // What a mob was holding in its inventory (villager food, a piglin's gold) has no place on a Bedrock mob.
            if (e.GetList("Inventory")?.OfType<NbtCompound>().Where(i => !ConversionLog.IsEmptyItem(i)).ToList() is { Count: > 0 } held)
            {
                bool rescued = ConversionLog.Rescue("Items mobs were holding", held);
                ConversionLog.Note(LossKind.Entity, ConversionLog.Friendly(id) + " inventory", rescued ? "moved to the rescue chests: Bedrock mobs keep no inventory" : "lost: Bedrock mobs keep no inventory", held.Count, lost: !rescued);
            }
        }
        if (riders.Count > 0)
        {
            var links = new NbtList(NbtType.Compound);
            foreach (var r in riders)
            {
                // Riders sit where their vehicle is.
                if (r.GetList("Pos") is not { Count: 3 } && e.GetList("Pos") is NbtList pos) r["Pos"] = pos.Clone();
                if (AddActor(r, actors) is long riderId && a != null)
                    links.Add(new NbtCompound { { "entityID", new NbtLong(riderId) }, { "LinkID", new NbtInt(links.Count) } });
            }
            if (a == null) ConversionLog.Note(LossKind.Entity, "riders", $"got off and stand where it was: {_target.Name} has no {ConversionLog.Friendly(id)}", riders.Count);
            else if (links.Count > 0) a["LinksTag"] = links;
        }
        return a != null ? unique : null;
    }

    void WriteActors(CanonicalChunk c, bool actorStorage)
    {
        var actors = new List<NbtCompound>();
        foreach (var e in c.Entities) AddActor(e, actors);
        if (!actorStorage)
        {
            if (actors.Count > 0) _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.Entity), NbtBinary.SerializeAll(actors.Select(a => new NamedTag("", a)), NbtFormat.Bedrock));
            return;
        }
        _db.Put(BedrockKeys.Make(c.X, c.Z, _dim, ChunkTag.ActorDigestVersion), [0]);
        var digest = new ByteWriter(actors.Count * 8);
        foreach (var a in actors)
        {
            long unique = a.GetLong("UniqueID");
            var key = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(key, -(int)(unique >> 32));
            BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(4), (int)unique);
            a["internalComponents"] = new NbtCompound
            {
                { "EntityStorageKeyComponent", new NbtCompound { { "StorageKey", new NbtString(RawString(key)) } } },
            };
            _db.Put(BedrockKeys.ActorKey(key), NbtBinary.Serialize(a, NbtFormat.Bedrock));
            digest.Bytes(key);
        }
        _db.Put(BedrockKeys.DigestKey(c.X, c.Z, _dim), digest.ToArray());
    }
}
