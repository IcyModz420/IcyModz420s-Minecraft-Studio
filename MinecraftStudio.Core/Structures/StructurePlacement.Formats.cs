// Structures/StructurePlacement.Formats.cs: the per-format half of placing a structure into a copy of a world.
// Owns: PlaceByTransplant (re-encode just the structure's chunks with the world's own writer into a temp folder, then
// move those chunk records into the copy — Java region/entities/Alpha files, Bedrock LevelDB keys and actors with fresh
// ids, console region records re-compressed for the world's platform, Pocket Edition 0.2–0.8 chunk slots and
// entities.dat) and PlaceFinite (Classic/Indev: set the blocks in the copied level directly). Each checks every placed
// block reads back as written. 3DS: StructurePlacement.ThreeDs.cs.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static partial class StructurePlacement
{
    static void PlaceByTransplant(string copyPath, Structure s, string dimensionId, int ox, int oy, int oz, int minY, int maxY, bool pasteAir,
        List<ChunkPos> present, PlacementResult result, IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        string temp = Path.Combine(Path.GetTempPath(), "MinecraftStudio-place-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // 1. Re-encode the structure's chunks, read from the copy itself (the original was only read by the copy step).
            ConversionReport report;
            using (var src = WorldLoader.Open(copyPath))
            {
                var (target, same) = SameFormat(src);
                var options = new ConversionOptions
                {
                    FiniteCenterX = same.FiniteCenterX,
                    FiniteCenterZ = same.FiniteCenterZ,
                    ConsoleWorldChunks = same.ConsoleWorldChunks,
                    Area = (present.Min(p => p.X), present.Min(p => p.Z), present.Max(p => p.X), present.Max(p => p.Z)),
                    Dimensions = new HashSet<string> { dimensionId },
                    StampChunk = StructureClip.Stamp(s, dimensionId, ox, oy, oz, minY, maxY, pasteAir),
                    Verify = false,
                    ConvertPlayers = false,
                    KeepUnfinishedChunks = true,
                };
                report = WorldConverter.Run(src, target, temp, options, progress, cancel);
            }
            // Players are deliberately not re-encoded (they stay exactly as they are in the copy), so they are no loss.
            // What the re-encode lost in those chunks; the world's own files (level.dat, raids, scoreboard …) are in the copy byte
            // for byte, so what it says about world files does not apply.
            foreach (var l in report.Losses.Where(l => l.Lost && l.Kind is not (LossKind.Player or LossKind.World)).Take(10)) result.Notes.Add("In the changed chunks: " + l);
            if (report.FailedChunks > 0) result.Notes.Add($"{report.FailedChunks} chunk(s) under the structure could not be read, so they were left as they were.");

            // 2. Move those chunks into the copy.
            progress?.Report(new ConversionProgress { Stage = "Writing the structure into the copy", Done = 0, Total = 1 });
            using (var built = WorldLoader.Open(report.OpenPath ?? temp))
            using (var copy = WorldLoader.Open(copyPath))
            {
                switch (copy)
                {
                    case JavaWorld j when built is JavaWorld bj: TransplantJava(bj, j, dimensionId, present); break;
                    case BedrockWorld b when built is BedrockWorld bb: TransplantBedrock(bb, b, dimensionId, present); break;
                    case ConsoleWorld c when built is ConsoleWorld bc: TransplantConsole(bc, c, dimensionId, present, result); break;
                    case PocketLegacyWorld p when built is PocketLegacyWorld bp: TransplantPocket(bp, p, present, result); break;
                    default: throw new IOException($"The structure was encoded as {built.FormatDescription}, which does not match {copy.FormatDescription}.");
                }
            }
            result.ChunksChanged = present.Count;

            // 3. Every placed block reads back in the copy exactly as the writer encoded it.
            using (var built = WorldLoader.Open(report.OpenPath ?? temp))
            using (var copy = WorldLoader.Open(copyPath))
                CheckPlaced(built, copy, s, dimensionId, ox, oy, oz, minY, maxY, pasteAir, present, result, cancel);
        }
        finally { TryDelete(temp); }
    }

    /// <summary>Compares every placed cell of the copy with the re-encoded chunks (what the world's own writer made of it).</summary>
    static void CheckPlaced(SaveWorld built, SaveWorld copy, Structure s, string dimensionId, int ox, int oy, int oz, int minY, int maxY, bool pasteAir,
        List<ChunkPos> present, PlacementResult result, CancellationToken cancel)
    {
        var bd = built.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimensionId);
        var cd = copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimensionId);
        var rb = new CanonicalReader(built, bd);
        var rc = new CanonicalReader(copy, cd);
        var have = present.ToHashSet();
        long wrong = 0;
        var cacheB = new Dictionary<ChunkPos, CanonicalChunk?>();
        var cacheC = new Dictionary<ChunkPos, CanonicalChunk?>();
        for (int z = 0; z < s.SizeZ; z++)
            for (int x = 0; x < s.SizeX; x++)
            {
                cancel.ThrowIfCancellationRequested();
                var pos = new ChunkPos((ox + x) >> 4, (oz + z) >> 4);
                if (!have.Contains(pos)) continue;
                if (!cacheB.TryGetValue(pos, out var b)) cacheB[pos] = b = rb.Get(pos);
                if (!cacheC.TryGetValue(pos, out var c)) cacheC[pos] = c = rc.Get(pos);
                for (int y = 0; y < s.SizeY; y++)
                {
                    int wy = oy + y;
                    if (wy < minY || wy > maxY) { if (pasteAir || !s.Get(x, y, z).IsAir) result.BlocksLeftOut++; continue; }
                    if (!pasteAir && s.Get(x, y, z).IsAir) continue;
                    result.BlocksPlaced++;
                    var expected = b?.GetBlock((ox + x) & 15, wy, (oz + z) & 15);
                    var actual = c?.GetBlock((ox + x) & 15, wy, (oz + z) & 15);
                    if (expected == null || actual == null || expected != actual) wrong++;
                }
            }
        if (wrong > 0) result.Problems.Add($"{wrong:N0} placed block(s) did not read back as written");

        // What the writer kept of chest contents, signs and the like, and of mobs, must be in the copy too (Pocket
        // Edition 0.1 has nowhere to keep them; that is said in the notes).
        if (copy is PocketLegacyWorld { RawLevel: not null }) return;
        bool Inside(int x, int y, int z) => x >= ox && x < ox + s.SizeX && y >= oy && y < oy + s.SizeY && z >= oz && z < oz + s.SizeZ;
        int Tiles(CanonicalChunk? c) => c?.BlockEntities.Count(e => Inside(e.GetInt("x"), e.GetInt("y"), e.GetInt("z"))) ?? 0;
        int Mobs(CanonicalChunk? c) => c?.Entities.Count(e => e.GetList("Pos") is { Count: 3 } p
            && Inside((int)Math.Floor(p[0].AsDouble), (int)Math.Floor(p[1].AsDouble), (int)Math.Floor(p[2].AsDouble))) ?? 0;
        int tilesBuilt = 0, tilesCopy = 0, mobsBuilt = 0, mobsCopy = 0;
        foreach (var pos in present)
        {
            var b = cacheB.TryGetValue(pos, out var cb) ? cb : rb.Get(pos);
            var c = cacheC.TryGetValue(pos, out var cc) ? cc : rc.Get(pos);
            tilesBuilt += Tiles(b); tilesCopy += Tiles(c); mobsBuilt += Mobs(b); mobsCopy += Mobs(c);
        }
        if (tilesCopy < tilesBuilt) result.Problems.Add($"{tilesBuilt - tilesCopy} block entit{(tilesBuilt - tilesCopy == 1 ? "y" : "ies")} (chest contents, signs …) did not read back in the copy");
        if (mobsCopy < mobsBuilt) result.Problems.Add($"{mobsBuilt - mobsCopy} entit{(mobsBuilt - mobsCopy == 1 ? "y" : "ies")} did not read back in the copy");
    }

    // ---------------------------------------------------------------- Java

    static void TransplantJava(JavaWorld built, JavaWorld copy, string dimensionId, List<ChunkPos> chunks)
    {
        var from = built.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId);
        var to = copy.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId);
        switch (to)
        {
            case JavaRegionDimension cr when from is JavaRegionDimension br:
                foreach (var group in chunks.GroupBy(p => (p.RegionX, p.RegionZ)))
                    foreach (var (sub, ext) in new[] { ("region", cr.Extension), ("entities", ".mca") })
                    {
                        string fromFile = Path.Combine(br.Folder, sub, $"r.{group.Key.RegionX}.{group.Key.RegionZ}{ext}");
                        string toFile = Path.Combine(cr.Folder, sub, $"r.{group.Key.RegionX}.{group.Key.RegionZ}{ext}");
                        // A re-encoded chunk without entities has no entities record (or file): the copy's old one goes,
                        // or a chunk whose mobs were all taken out would keep them (1.17+ keeps mobs in entities/).
                        bool staleMobs = sub == "entities" && File.Exists(toFile);
                        if (!File.Exists(fromFile) && !staleMobs) continue;
                        var source = File.Exists(fromFile) ? RegionFile.Open(fromFile) : null;
                        RegionFile? target = null;
                        RegionFile Target()
                        {
                            if (target != null) return target;
                            Directory.CreateDirectory(Path.GetDirectoryName(toFile)!);
                            return target = File.Exists(toFile) ? RegionFile.Open(toFile) : RegionFile.CreateEmpty(toFile, group.Key.RegionX, group.Key.RegionZ);
                        }
                        foreach (var pos in group)
                        {
                            int index = RegionFile.Index(pos.X & 31, pos.Z & 31);
                            if (source?.GetChunkNbt(index) is NbtCompound nbt) Target().SetChunk(index, nbt);
                            else if (staleMobs && Target().HasChunk(index)) target!.DeleteChunk(index);
                        }
                        target?.Save(toFile, deleteIfEmpty: false);
                    }
                break;
            case AlphaDimension ca when from is AlphaDimension ba:
                foreach (var pos in chunks)
                {
                    string f = AlphaDimension.ChunkPath(ba.Folder, pos.X, pos.Z);
                    if (!File.Exists(f)) continue;
                    string t = AlphaDimension.ChunkPath(ca.Folder, pos.X, pos.Z);
                    Directory.CreateDirectory(Path.GetDirectoryName(t)!);
                    File.Copy(f, t, overwrite: true);
                }
                break;
            default:
                throw new IOException("The world's chunk storage could not be matched for placing the structure.");
        }
    }

    // ---------------------------------------------------------------- Bedrock

    /// <summary>
    /// Replaces each chunk's records in the copy's database with the re-encoded ones: terrain, biomes and heights,
    /// block entities, ticks and entities. Generation metadata the new chunk does not carry (blending data, seeds) is
    /// kept; stale checksums are dropped. Actors get ids no other actor in the world has.
    /// </summary>
    static void TransplantBedrock(BedrockWorld built, BedrockWorld copy, string dimensionId, List<ChunkPos> chunks)
    {
        var from = built.Dimensions.OfType<BedrockDimension>().FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId)
            ?? throw new IOException("The structure could not be encoded for this world.");
        var to = copy.Dimensions.OfType<BedrockDimension>().First(d => CanonicalReader.DimensionId(d) == dimensionId);
        int dim = to.Dimension;
        var db = copy.Db;

        // Every actor storage key already in the world, so a new one never lands on an existing actor.
        var usedKeys = new HashSet<ulong>();
        foreach (var k in db.Keys())
            if (BedrockKeys.IsActorKey(k)) usedKeys.Add(BinaryPrimitives.ReadUInt64BigEndian(k.AsSpan(k.Length - 8)));
        uint nextLow = 0x40000000;

        var replaced = new HashSet<ChunkTag>
        {
            ChunkTag.SubChunkPrefix, ChunkTag.LegacyTerrain, ChunkTag.BlockEntity, ChunkTag.Entity, ChunkTag.PendingTicks,
            ChunkTag.RandomTicks, ChunkTag.Data3D, ChunkTag.Data2D, ChunkTag.Data2DLegacy, ChunkTag.Checksums, ChunkTag.BlockExtraData,
        };
        foreach (var pos in chunks)
        {
            var newKeys = from.KeysAt(pos).ToList();
            var newTags = new HashSet<ChunkTag>();
            foreach (var k in newKeys) if (BedrockKeys.TryParseChunkKey(k, out var ck)) newTags.Add(ck.Tag);

            // Out with the copy's own records for this chunk (and its actors).
            foreach (var k in to.KeysAt(pos))
                if (BedrockKeys.TryParseChunkKey(k, out var ck) && (replaced.Contains(ck.Tag) || newTags.Contains(ck.Tag)))
                    db.Delete(k);
            var oldDigest = BedrockKeys.DigestKey(pos.X, pos.Z, dim);
            if (db.Get(oldDigest) is byte[] oldIds)
            {
                for (int i = 0; i + 8 <= oldIds.Length; i += 8) db.Delete(BedrockKeys.ActorKey(oldIds.AsSpan(i, 8)));
                db.Delete(oldDigest);
            }

            // In with the re-encoded ones.
            foreach (var k in newKeys)
                if (built.Db.Get(k) is byte[] v) db.Put(k, v);
            if (built.Db.Get(BedrockKeys.DigestKey(pos.X, pos.Z, dim)) is byte[] newIds)
            {
                var digest = new List<byte>(newIds.Length);
                for (int i = 0; i + 8 <= newIds.Length; i += 8)
                {
                    if (built.Db.Get(BedrockKeys.ActorKey(newIds.AsSpan(i, 8))) is not byte[] raw) continue;
                    var actor = NbtBinary.ReadCompound(raw, NbtFormat.Bedrock);
                    // Writer convention: high half −1 (the game's own ids never reach it); a low half no actor uses.
                    byte[] key;
                    long id;
                    do
                    {
                        id = (-1L << 32) | nextLow++;
                        key = ActorStorageKey(id);
                    } while (!usedKeys.Add(BinaryPrimitives.ReadUInt64BigEndian(key)));
                    actor.SetLong("UniqueID", id);
                    actor["internalComponents"] = new NbtCompound
                    {
                        { "EntityStorageKeyComponent", new NbtCompound { { "StorageKey", new NbtString(RawString(key)) } } },
                    };
                    db.Put(BedrockKeys.ActorKey(key), NbtBinary.Serialize(actor, NbtFormat.Bedrock));
                    digest.AddRange(key);
                }
                if (digest.Count > 0) db.Put(BedrockKeys.DigestKey(pos.X, pos.Z, dim), digest.ToArray());
            }
        }
        db.Commit(LevelDb.CommitMode.AppendLog);
    }

    /// <summary>The 8-byte actor storage key Bedrock uses for an actor id (as the converter writes it).</summary>
    static byte[] ActorStorageKey(long id)
    {
        var key = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(key, -(int)(id >> 32));
        BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(4), (int)id);
        return key;
    }

    /// <summary>Bytes as an NBT string that keeps them exactly (non-ASCII bytes through the surrogate escapes U+DC80–U+DCFF).</summary>
    static string RawString(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes) sb.Append(b < 0x80 ? (char)b : (char)(0xDC00 + b));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- Legacy Console

    static void TransplantConsole(ConsoleWorld built, ConsoleWorld copy, string dimensionId, List<ChunkPos> chunks, PlacementResult result)
    {
        var from = built.Dimensions.OfType<ConsoleDimension>().FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId)
            ?? throw new IOException("The structure could not be encoded for this world.");
        var to = copy.Dimensions.OfType<ConsoleDimension>().First(d => CanonicalReader.DimensionId(d) == dimensionId);
        foreach (var pos in chunks)
        {
            if (from.RawChunk(pos) is not ConsoleRegion.Chunk chunk || from.ChunkBytes(pos) is not byte[] data) continue;
            // Re-compressed for the copy's own platform, so the record always matches the save it goes into.
            if (!to.WriteChunk(pos, data, chunk.Flags)) result.Notes.Add($"Chunk {pos} is not stored in the world, so it was left out.");
        }
        copy.Save();
        result.Notes.Add("Console worlds: the changed chunks are written in the console chunk format this program writes; blocks newer than it (such as coral or kelp) in those chunks become their closest older equivalents. Everything else in the save is unchanged.");
    }

    // ---------------------------------------------------------------- Pocket Edition 0.2–0.8

    static void TransplantPocket(PocketLegacyWorld built, PocketLegacyWorld copy, List<ChunkPos> chunks, PlacementResult result)
    {
        foreach (var pos in chunks)
            if (built.Overworld.RawChunk(pos) is byte[] raw) copy.Overworld.SetRawChunk(pos, raw);
        copy.Overworld.SaveChanges();

        // entities.dat holds every entity and block entity of the world: swap the ones in the changed chunks.
        if (built.Entities?.Roots.FirstOrDefault()?.Tag is not NbtCompound from || built.EntitiesHeader == null) return;
        var set = chunks.ToHashSet();
        bool InChanged(NbtCompound e, bool tile)
        {
            int x, z;
            if (tile) { x = e.GetInt("x"); z = e.GetInt("z"); }
            else if (e.GetList("Pos") is { Count: 3 } p) { x = (int)Math.Floor(p[0].AsDouble); z = (int)Math.Floor(p[2].AsDouble); }
            else return false;
            return set.Contains(new ChunkPos(x >> 4, z >> 4));
        }
        if (copy.Entities == null)
        {
            // Pocket Edition 0.1 keeps no entities.dat at all (no chest contents, signs or mobs): say what stays out. A
            // later world that has none yet (nothing was ever in it) gets one.
            if (copy.RawLevel != null)
            {
                int tiles = from.GetList("TileEntities")?.OfType<NbtCompound>().Count(e => InChanged(e, true)) ?? 0;
                int mobs = from.GetList("Entities")?.OfType<NbtCompound>().Count(e => InChanged(e, false)) ?? 0;
                if (tiles + mobs > 0)
                    result.Notes.Add($"Pocket Edition 0.1 worlds keep no chest contents, sign text or mobs, so {tiles} block entit{(tiles == 1 ? "y" : "ies")} and {mobs} entit{(mobs == 1 ? "y" : "ies")} of the structure were left out (the blocks were placed).");
                return;
            }
            copy.CreateEntities(built.EntitiesHeader);
        }
        if (copy.Entities?.Roots.FirstOrDefault()?.Tag is NbtCompound to)
        {
            foreach (var (name, tile) in new[] { ("Entities", false), ("TileEntities", true) })
            {
                var list = to.GetList(name) ?? new NbtList(NbtType.Compound);
                var kept = list.OfType<NbtCompound>().Where(e => !InChanged(e, tile)).Cast<NbtTag>().ToList();
                foreach (var e in (from.GetList(name) ?? new NbtList()).OfType<NbtCompound>()) if (InChanged(e, tile)) kept.Add(e.Clone());
                var merged = new NbtList(NbtType.Compound);
                foreach (var e in kept) merged.Add(e);
                to[name] = merged;
            }
            copy.SaveEntities();
        }
    }

    // ---------------------------------------------------------------- Classic / Indev

    /// <summary>Classic and Indev levels are one block array: the blocks are set straight into the copied level.</summary>
    static void PlaceFinite(string copyPath, Structure s, int ox, int oy, int oz, bool pasteAir, PlacementResult result, CancellationToken cancel)
    {
        using var world = WorldLoader.Open(copyPath);
        if (world is not FiniteWorld level || level.Dimensions.FirstOrDefault() is not FiniteDimension dim)
            throw new IOException("The copied level could not be opened for placing the structure.");
        bool classic = level.Classic != null;
        var placed = new List<(int X, int Y, int Z, BlockState Expected)>();
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int x = 0; x < s.SizeX; x++)
                {
                    var state = s.Get(x, y, z);
                    if (!pasteAir && state.IsAir) continue;
                    int wx = ox + x, wy = oy + y, wz = oz + z;
                    if ((uint)wx >= (uint)dim.Width || (uint)wy >= (uint)dim.Height || (uint)wz >= (uint)dim.Length) { result.BlocksLeftOut++; continue; }
                    var (id, data) = FiniteBlock(state, classic);
                    dim.SetBlock(wx, wy, wz, id, data);
                    placed.Add((wx, wy, wz, LegacyBlocks.Get(id, data, LegacyIdScheme.Classic)));
                }
            }
        level.Save();
        result.BlocksPlaced = placed.Count;
        result.ChunksChanged = placed.Select(p => (p.X >> 4, p.Z >> 4)).Distinct().Count();
        if (s.BlockEntities.Count > 0 || s.Entities.Count > 0)
            result.Notes.Add("Classic and Indev levels: the structure's blocks were placed; chest contents, signs and entities are not carried into these old levels.");

        // Read the copy back and check every placed block.
        using var check = WorldLoader.Open(copyPath);
        var cd = check.Dimensions[0];
        var cache = new Dictionary<ChunkPos, ChunkColumn?>();
        long wrong = 0;
        foreach (var (x, y, z, expected) in placed)
        {
            var pos = new ChunkPos(x >> 4, z >> 4);
            if (!cache.TryGetValue(pos, out var col)) cache[pos] = col = cd.LoadChunk(pos);
            if (col?.GetBlock(x & 15, y, z & 15) != expected) wrong++;
        }
        if (wrong > 0) result.Problems.Add($"{wrong:N0} placed block(s) did not read back as written");
    }

    /// <summary>The block id and data a Classic or Indev level stores for a canonical block (as the level writer maps it).</summary>
    static (int Id, int Data) FiniteBlock(BlockState java, bool classic) =>
        java.IsAir ? (0, 0)
        : classic || java.BaseName.EndsWith("_wool", StringComparison.Ordinal) ? (JavaBlocks.ToClassic(java), 0)
        : JavaBlocks.ToLegacy(java, LegacyLevel.Indev);
}
