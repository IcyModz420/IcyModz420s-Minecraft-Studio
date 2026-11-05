// Structures/StructurePlacement.Restore.cs: chunks brought back from a backup (another copy of the same world) into a
// copy of the world, the way a structure is placed — the world copied byte for byte, only those chunks' records replaced
// by the backup's own bytes (nothing re-encoded: blocks, chests, item frames, mobs, ticks, points of interest as saved),
// then checked: they read back as in the backup, everything else as in the world.
// Owns: RestoreIntoCopy. A mob, pet or minecart from the backup that is still alive somewhere else in the world today is
// not brought back a second time; with keepNewMobs the mobs in the area now that the backup doesn't have stay. A backup
// in another format (an older version's layout, another edition) is re-encoded through EditIntoCopy instead.
// Part of StructurePlacement (main part in StructurePlacement.cs; the transplants it reuses in StructurePlacement.Formats.cs).
using System.Buffers.Binary;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static partial class StructurePlacement
{
    /// <summary>
    /// Brings <paramref name="chunks"/> of dimension <paramref name="dimensionId"/> back from <paramref name="backup"/>
    /// into a NEW copy of <paramref name="world"/> at <paramref name="output"/>. Both worlds are only read. Chunks the
    /// backup does not have are left as they are (said in the notes). Throws (leaving nothing behind) when nothing can be
    /// restored; otherwise the result says what the check of the copy found.
    /// </summary>
    public static PlacementResult RestoreIntoCopy(SaveWorld world, SaveWorld backup, string dimensionId, IEnumerable<ChunkPos> chunks, string output,
        bool keepNewMobs = false, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (world.OpenedForRescue) throw new InvalidOperationException("This world was opened for rescue. Rescue it into a new world first, then restore into that.");
        if (world is ThreeDsWorld) throw new NotSupportedException(ThreeDsNotWritable);
        // A Classic or Indev level (or backup) goes the re-encoding way: its blocks set straight into the copied level.
        if (WorldUse.InUse(world) is string busy) throw new IOException(busy + " Close it first, so the copy is made from a world that is not being written to.");
        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("A file with that name already exists; choose another folder.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("That folder is not empty; choose a new folder name.");
        foreach (var inside in new[] { world.Location, backup.Location })
        {
            string source = Path.GetFullPath(inside).TrimEnd('\\');
            if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The copy cannot go inside the world or the backup.");
        }
        var dim = world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId) ?? throw new ArgumentException($"The world has no {dimensionId}.");
        var fromDim = BackupDimension(backup, dimensionId) ?? throw new IOException($"The backup has no {dim.DisplayName}.");
        var inBackup = fromDim.ChunkPositions.ToHashSet();
        var wanted = chunks.Distinct().ToList();
        var restore = wanted.Where(inBackup.Contains).ToList();
        if (restore.Count == 0) throw new IOException("The backup has none of those chunks, so there is nothing to bring back from it.");
        int notInBackup = wanted.Count - restore.Count;

        PlacementResult result;
        if (!SameStorage(world, dim, backup, fromDim))
        {
            // Another format: the backup's chunks re-encoded by the world's own writer (only chunks the world has can be).
            var stored = dim.ChunkPositions.ToHashSet();
            var present = restore.Where(stored.Contains).ToList();
            if (present.Count == 0) throw new IOException("The backup is in another format, and none of those chunks is generated in this world, so they cannot be re-encoded into it.");
            result = RestoreReencoded(world, backup, fromDim, dimensionId, present, output, keepNewMobs, progress, cancel);
            if (present.Count < restore.Count)
                result.Notes.Add($"{restore.Count - present.Count} of the chunks are only in the backup (not generated in this world any more), and a backup in another format can only be brought into chunks the world has, so they were left out.");
        }
        else
        {
            EnsureRoomForCopy(world.Location, output);
            result = new PlacementResult { Format = world.FormatDescription };
            progress?.Report(new ConversionProgress { Stage = "Copying the world", Done = 0, Total = 1 });
            result.OpenPath = CopyWorld(world.Location, output, progress, cancel);
            try
            {
                int expectedMobs;
                progress?.Report(new ConversionProgress { Stage = "Bringing the chunks back from the backup", Done = 0, Total = 1 });
                using (var copy = WorldLoader.Open(result.OpenPath))
                {
                    expectedMobs = copy switch
                    {
                        JavaWorld j when backup is JavaWorld bj => RestoreJava(bj, j, dimensionId, restore, keepNewMobs, result, progress, cancel),
                        BedrockWorld b when backup is BedrockWorld bb => RestoreBedrock(bb, b, dimensionId, restore, keepNewMobs, result, progress, cancel),
                        ConsoleWorld c when backup is ConsoleWorld bc => RestoreConsole(bc, c, dimensionId, restore, result),
                        PocketLegacyWorld p when backup is PocketLegacyWorld bp => RestorePocket(bp, p, restore, result),
                        _ => throw new IOException($"The backup is {backup.FormatDescription}, which does not match {copy.FormatDescription}."),
                    };
                }
                result.ChunksChanged = restore.Count;
                CheckRestored(backup, result.OpenPath, dimensionId, restore, expectedMobs, result, cancel);
                CheckUntouched(world, result.OpenPath, dimensionId, restore, result, cancel);
            }
            catch
            {
                TryDelete(output);
                throw;
            }
        }
        if (notInBackup > 0)
            result.Notes.Add($"{notInBackup:N0} of the chunks chosen are not in the backup (the land was not generated yet when it was made), so they were left as they are.");
        result.Elapsed = clock.Elapsed;
        return result;
    }

    /// <summary>The other world's dimension that is <paramref name="dimensionId"/> (a mod dimension under its Java output name too).</summary>
    public static WorldDimension? MatchingDimension(SaveWorld other, string dimensionId) =>
        other.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId)
        ?? other.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == JavaWorldWriter.OutputDimensionId(dimensionId));

    static WorldDimension? BackupDimension(SaveWorld backup, string dimensionId) => MatchingDimension(backup, dimensionId);

    /// <summary>Whether the backup keeps its chunks the way the world does, so their records can be moved over as they are.</summary>
    static bool SameStorage(SaveWorld world, WorldDimension dim, SaveWorld backup, WorldDimension fromDim) => (dim, fromDim) switch
    {
        // A backup newer than the world could not be read by the game that plays it: that one is re-encoded instead.
        (JavaRegionDimension a, JavaRegionDimension b) => a.Extension.Equals(b.Extension, StringComparison.OrdinalIgnoreCase)
            && ((backup as JavaWorld)?.DataVersion is not int bv || bv <= ((world as JavaWorld)?.DataVersion ?? int.MaxValue)),
        (AlphaDimension, AlphaDimension) => true,
        (BedrockDimension, BedrockDimension) => true,
        (ConsoleDimension, ConsoleDimension) => world is ConsoleWorld cw && backup is ConsoleWorld cb && cw.Platform == cb.Platform,
        _ => world is PocketLegacyWorld && backup is PocketLegacyWorld,
    };

    // ---------------------------------------------------------------- Java

    /// <summary>
    /// Region, entities and points-of-interest records of each chunk copied from the backup as they are stored (same
    /// compression and timestamp). Returns how many top-level mobs the restored chunks should hold.
    /// </summary>
    static int RestoreJava(JavaWorld backup, JavaWorld copy, string dimensionId, List<ChunkPos> chunks, bool keepNewMobs, PlacementResult result,
        IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        var to = copy.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId);
        var from = BackupDimension(backup, dimensionId);
        if (to is AlphaDimension && from is AlphaDimension)
        {
            // Infdev/Alpha: one file per chunk, its mobs inside it.
            TransplantJava(backup, copy, dimensionId, chunks);
            result.Notes.Add("Infdev and Alpha chunks were brought back whole, mobs included.");
            return -1;
        }
        if (to is not JavaRegionDimension cr || from is not JavaRegionDimension br) throw new IOException("The world's chunk storage could not be matched with the backup's.");
        var set = chunks.ToHashSet();
        progress?.Report(new ConversionProgress { Stage = "Looking for the world's mobs outside the area", Done = 0, Total = 1 });
        var elsewhere = JavaEntityIds(copy, cr, set, cancel);
        int expected = 0, stillThere = 0, keptNew = 0;
        var keptNames = new List<string>();
        foreach (var group in chunks.GroupBy(p => (p.RegionX, p.RegionZ)))
        {
            cancel.ThrowIfCancellationRequested();
            foreach (var (sub, ext) in new[] { ("region", cr.Extension), ("entities", ".mca"), ("poi", ".mca") })
            {
                string name = $"r.{group.Key.RegionX}.{group.Key.RegionZ}{ext}";
                string fromFile = Path.Combine(br.Folder, sub, name), toFile = Path.Combine(cr.Folder, sub, name);
                var source = File.Exists(fromFile) ? RegionFile.Open(fromFile) : null;
                var target = File.Exists(toFile) ? RegionFile.Open(toFile) : null;
                if (source == null && target == null) continue;
                bool changed = false;
                RegionFile Target()
                {
                    if (target != null) return target;
                    Directory.CreateDirectory(Path.GetDirectoryName(toFile)!);
                    return target = RegionFile.CreateEmpty(toFile, group.Key.RegionX, group.Key.RegionZ);
                }
                foreach (var pos in group)
                {
                    int index = RegionFile.Index(pos.X & 31, pos.Z & 31);
                    var raw = source?.GetRaw(index);
                    bool mobsHere = sub == "entities" || sub == "region" && raw != null && HoldsEntities(source!.GetChunkNbt(index));
                    if (!mobsHere)
                    {
                        // Blocks without mobs, or points of interest: exactly the backup's record (none: the game rebuilds it).
                        if (raw != null) { Target().SetRaw(index, raw); changed = true; }
                        else if (target?.HasChunk(index) == true) { target.DeleteChunk(index); changed = true; }
                        continue;
                    }
                    var nbt = raw != null ? source!.GetChunkNbt(index) : null;
                    var list = nbt == null ? null : sub == "entities" ? nbt.GetList("Entities") : nbt.GetCompound("Level")?.GetList("Entities");
                    var today = target?.HasChunk(index) == true ? target.GetChunkNbt(index) : null;
                    var todayList = today == null ? null : sub == "entities" ? today.GetList("Entities") : today.GetCompound("Level")?.GetList("Entities");
                    var merged = MergeMobs(list, todayList, elsewhere, keepNewMobs, ref stillThere, ref keptNew, keptNames, JavaEntityId);
                    expected += merged.Count;
                    if (nbt == null)
                    {
                        // The backup has no mobs record here: only today's kept mobs stay (else the record goes).
                        if (merged.Count > 0 && today != null) { today["Entities"] = merged; Target().SetChunk(index, today); changed = true; }
                        else if (target?.HasChunk(index) == true && !keepNewMobs) { target.DeleteChunk(index); changed = true; }
                        continue;
                    }
                    if (list != null && !SameList(list, merged))
                    {
                        if (sub == "entities") nbt["Entities"] = merged; else nbt.GetCompound("Level")!["Entities"] = merged;
                        Target().SetChunk(index, nbt, RawChunk.KindOf(raw!.CompressionType), raw.Timestamp);
                    }
                    else Target().SetRaw(index, raw!);
                    changed = true;
                }
                if (changed) target!.Save(toFile, deleteIfEmpty: false);
            }
        }
        MobNotes(result, stillThere, keptNew, keptNames, keepNewMobs);
        return expected;
    }

    static bool HoldsEntities(NbtCompound? chunk) => chunk?.GetCompound("Level")?.GetList("Entities") != null;

    /// <summary>A Java entity's id: UUID (int[4], 1.16+), UUIDMost/UUIDLeast before that, a console's "ent…" string.</summary>
    static string? JavaEntityId(NbtCompound e)
    {
        if (e.GetInts("UUID") is { Length: 4 } u) return $"{u[0]:x8}{u[1]:x8}{u[2]:x8}{u[3]:x8}";
        if (e.GetLongOrNull("UUIDMost") is long most && e.GetLongOrNull("UUIDLeast") is long least) return $"{most:x16}{least:x16}";
        return e.GetString("UUID");
    }

    /// <summary>Ids of every entity (riders too) the world keeps outside <paramref name="area"/> of <paramref name="dim"/>, in every dimension.</summary>
    static HashSet<string> JavaEntityIds(JavaWorld world, JavaRegionDimension dim, HashSet<ChunkPos> area, CancellationToken cancel)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in world.Dimensions.OfType<JavaRegionDimension>())
        {
            bool same = ReferenceEquals(d, dim);
            string entities = Path.Combine(d.Folder, "entities");
            bool separate = Directory.Exists(entities) && Directory.EnumerateFiles(entities, "r.*.mca").Any();
            string folder = separate ? entities : d.RegionFolder;
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "r.*" + (separate ? ".mca" : d.Extension)))
            {
                cancel.ThrowIfCancellationRequested();
                if (!RegionFile.TryParseName(Path.GetFileName(file), out _, out _)) continue;
                RegionFile region;
                try { region = RegionFile.Open(file); } catch (Exception ex) when (ex is IOException or SaveFormatException or InvalidDataException) { continue; }
                foreach (int index in region.ChunkIndexes())
                {
                    var (x, z) = region.ChunkCoords(index);
                    if (same && area.Contains(new ChunkPos(x, z))) continue;
                    NbtCompound? nbt;
                    try { nbt = region.GetChunkNbt(index); } catch (Exception ex) when (ex is IOException or SaveFormatException or InvalidDataException) { continue; }
                    var list = separate ? nbt?.GetList("Entities") : nbt?.GetCompound("Level")?.GetList("Entities");
                    if (list != null) foreach (var e in list.OfType<NbtCompound>()) AddIds(e, ids, JavaEntityId);
                }
            }
        }
        return ids;
    }

    static void AddIds(NbtCompound e, HashSet<string> ids, Func<NbtCompound, string?> idOf)
    {
        if (idOf(e) is string id) ids.Add(id);
        if (e.GetList("Passengers") is NbtList riders) foreach (var r in riders.OfType<NbtCompound>()) AddIds(r, ids, idOf);
    }

    /// <summary>
    /// The backup's mobs of one chunk without those still alive elsewhere (riders checked one by one), plus with
    /// <paramref name="keepNewMobs"/> today's mobs there that the backup doesn't have.
    /// </summary>
    static NbtList MergeMobs(NbtList? backup, NbtList? today, HashSet<string> elsewhere, bool keepNewMobs, ref int stillThere, ref int keptNew,
        List<string> keptNames, Func<NbtCompound, string?> idOf)
    {
        var merged = new NbtList(NbtType.Compound);
        var inBackup = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in backup?.OfType<NbtCompound>() ?? [])
        {
            AddIds(e, inBackup, idOf);
            if (idOf(e) is string id && elsewhere.Contains(id)) { stillThere++; continue; }
            var kept = (NbtCompound)e.Clone();
            stillThere += DropRiders(kept, elsewhere, idOf);
            merged.Add(kept);
        }
        if (keepNewMobs)
            foreach (var e in today?.OfType<NbtCompound>() ?? [])
                if (idOf(e) is not string id || !inBackup.Contains(id))
                {
                    merged.Add(e.Clone());
                    keptNew++;
                    if (keptNames.Count < 8 && Named(e) is string n) keptNames.Add(n);
                }
        return merged;
    }

    static int DropRiders(NbtCompound e, HashSet<string> elsewhere, Func<NbtCompound, string?> idOf)
    {
        if (e.GetList("Passengers") is not NbtList riders) return 0;
        int dropped = 0;
        for (int i = riders.Count - 1; i >= 0; i--)
        {
            if (riders[i] is not NbtCompound r) continue;
            if (idOf(r) is string id && elsewhere.Contains(id)) { riders.RemoveAt(i); dropped++; }
            else dropped += DropRiders(r, elsewhere, idOf);
        }
        if (riders.Count == 0) e.Remove("Passengers");
        return dropped;
    }

    /// <summary>A mob's own name (a name tag) or its kind, for the notes.</summary>
    static string? Named(NbtCompound e)
    {
        string kind = (e.GetString("id") ?? e.GetString("identifier") ?? "").Replace("minecraft:", "").Replace('_', ' ');
        string? name = e["CustomName"] switch
        {
            NbtString { Value.Length: 0 } => null,
            NbtString t when t.Value[0] is '{' or '[' or '"' => TextComponents.ToPlain(t.Value),
            NbtString t => t.Value,
            NbtTag t => TextComponents.ToPlain(TextComponents.FromJavaTag(t)),
            null => null,
        };
        if (string.IsNullOrEmpty(name)) return kind.Length > 0 ? kind : null;
        return $"\"{name}\" ({kind})";
    }

    static bool SameList(NbtList a, NbtList b) => a.Count == b.Count && a.ContentEquals(b);

    static void MobNotes(PlacementResult result, int stillThere, int keptNew, List<string> keptNames, bool keepNewMobs)
    {
        if (stillThere > 0)
            result.Notes.Add($"{stillThere:N0} mob{(stillThere == 1 ? "" : "s")} (or minecarts, boats, item frames …) from the backup {(stillThere == 1 ? "is" : "are")} still alive somewhere else in the world, so {(stillThere == 1 ? "it was" : "they were")} not brought back a second time.");
        if (keptNew > 0)
            result.Notes.Add($"{keptNew:N0} mob{(keptNew == 1 ? "" : "s")} that {(keptNew == 1 ? "is" : "are")} in the area now but not in the backup stayed" + (keptNames.Count > 0 ? ": " + string.Join(", ", keptNames) + (keptNew > keptNames.Count ? " …" : "") : "") + ".");
        else if (!keepNewMobs)
            result.Notes.Add("The mobs in the area are the backup's: any that came there since are not in the copy (keep the mobs that are there now to have them too).");
    }

    // ---------------------------------------------------------------- Bedrock

    /// <summary>
    /// Every record of each chunk replaced by the backup's (terrain, biomes, chests, ticks, generation data), and its
    /// actors by the backup's. Returns how many actors the restored chunks should hold.
    /// </summary>
    static int RestoreBedrock(BedrockWorld backup, BedrockWorld copy, string dimensionId, List<ChunkPos> chunks, bool keepNewMobs, PlacementResult result,
        IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        var from = BackupDimension(backup, dimensionId) as BedrockDimension ?? throw new IOException("The backup has no such dimension.");
        var to = copy.Dimensions.OfType<BedrockDimension>().First(d => CanonicalReader.DimensionId(d) == dimensionId);
        int dim = to.Dimension, fromDim = from.Dimension;
        var db = copy.Db;
        var set = chunks.ToHashSet();

        // Today's actors in the area (by their storage keys), and the ids of every other actor in the world.
        progress?.Report(new ConversionProgress { Stage = "Looking for the world's mobs outside the area", Done = 0, Total = 1 });
        var todayKeys = new HashSet<ulong>();
        foreach (var pos in set)
            if (db.Get(BedrockKeys.DigestKey(pos.X, pos.Z, dim)) is byte[] ids)
                for (int i = 0; i + 8 <= ids.Length; i += 8) todayKeys.Add(BinaryPrimitives.ReadUInt64BigEndian(ids.AsSpan(i, 8)));
        var elsewhere = new HashSet<string>(StringComparer.Ordinal);
        var usedKeys = new HashSet<ulong>();
        foreach (var k in db.Keys())
        {
            cancel.ThrowIfCancellationRequested();
            if (BedrockKeys.IsActorKey(k))
            {
                ulong storage = BinaryPrimitives.ReadUInt64BigEndian(k.AsSpan(k.Length - 8));
                if (todayKeys.Contains(storage)) continue;
                usedKeys.Add(storage);
                if (db.Get(k) is byte[] v && TryActor(v) is NbtCompound a) AddIds(a, elsewhere, BedrockActorId);
            }
            else if (BedrockKeys.TryParseChunkKey(k, out var ck) && ck.Tag == ChunkTag.Entity && !(ck.Dimension == dim && set.Contains(new ChunkPos(ck.X, ck.Z))))
                foreach (var a in Records(db.Get(k))) AddIds(a, elsewhere, BedrockActorId);
        }

        int expected = 0, stillThere = 0, keptNew = 0;
        var keptNames = new List<string>();
        foreach (var pos in chunks)
        {
            cancel.ThrowIfCancellationRequested();
            // Today's actors there, read before they go.
            var todayActors = new List<NbtCompound>();
            var oldDigest = BedrockKeys.DigestKey(pos.X, pos.Z, dim);
            if (db.Get(oldDigest) is byte[] oldIds)
            {
                for (int i = 0; i + 8 <= oldIds.Length; i += 8)
                {
                    var key = BedrockKeys.ActorKey(oldIds.AsSpan(i, 8));
                    if (db.Get(key) is byte[] v && TryActor(v) is NbtCompound a) todayActors.Add(a);
                    db.Delete(key);
                }
                db.Delete(oldDigest);
            }
            var todayLegacy = new List<NbtCompound>();
            foreach (var k in to.KeysAt(pos))
            {
                if (BedrockKeys.TryParseChunkKey(k, out var ck) && ck.Tag == ChunkTag.Entity) todayLegacy.AddRange(Records(db.Get(k)));
                db.Delete(k);
            }

            // The backup's records, as they are (legacy in-chunk mobs filtered like actors).
            foreach (var k in from.KeysAt(pos))
            {
                if (backup.Db.Get(k) is not byte[] v || !BedrockKeys.TryParseChunkKey(k, out var ck)) continue;
                var key = fromDim == dim ? k : BedrockKeys.Make(ck.X, ck.Z, dim, ck.Tag, ck.Tag == ChunkTag.SubChunkPrefix ? ck.SubChunk : null);
                if (ck.Tag == ChunkTag.Entity)
                {
                    var list = new NbtList(NbtType.Compound);
                    foreach (var a in Records(v)) list.Add(a);
                    var today = new NbtList(NbtType.Compound);
                    foreach (var a in todayLegacy) today.Add(a);
                    var merged = MergeMobs(list, today, elsewhere, keepNewMobs, ref stillThere, ref keptNew, keptNames, BedrockActorId);
                    todayLegacy.Clear();
                    expected += merged.Count;
                    if (merged.Count > 0) db.Put(key, Concat(merged));
                    continue;
                }
                db.Put(key, v);
            }
            if (keepNewMobs && todayLegacy.Count > 0)
            {
                // The backup kept no in-chunk mobs here: today's stay.
                var today = new NbtList(NbtType.Compound);
                foreach (var a in todayLegacy) today.Add(a);
                var merged = MergeMobs(null, today, elsewhere, true, ref stillThere, ref keptNew, keptNames, BedrockActorId);
                expected += merged.Count;
                db.Put(BedrockKeys.Make(pos.X, pos.Z, dim, ChunkTag.Entity), Concat(merged));
            }

            // Actors: the backup's (without those alive elsewhere), with today's new ones when kept.
            var backupActors = new NbtList(NbtType.Compound);
            if (backup.Db.Get(BedrockKeys.DigestKey(pos.X, pos.Z, fromDim)) is byte[] backupIds)
                for (int i = 0; i + 8 <= backupIds.Length; i += 8)
                    if (backup.Db.Get(BedrockKeys.ActorKey(backupIds.AsSpan(i, 8))) is byte[] raw && TryActor(raw) is NbtCompound a) backupActors.Add(a);
            var todayList = new NbtList(NbtType.Compound);
            foreach (var a in todayActors) todayList.Add(a);
            var actors = MergeMobs(backupActors, todayList, elsewhere, keepNewMobs, ref stillThere, ref keptNew, keptNames, BedrockActorId);
            expected += actors.Count;
            var digest = new List<byte>(actors.Count * 8);
            foreach (var a in actors.OfType<NbtCompound>())
            {
                byte[] key = StorageKeyOf(a) is byte[] own && usedKeys.Add(BinaryPrimitives.ReadUInt64BigEndian(own)) ? own : FreshStorageKey(usedKeys);
                a["internalComponents"] = new NbtCompound
                {
                    { "EntityStorageKeyComponent", new NbtCompound { { "StorageKey", new NbtString(RawString(key)) } } },
                };
                db.Put(BedrockKeys.ActorKey(key), NbtBinary.Serialize(a, NbtFormat.Bedrock));
                digest.AddRange(key);
            }
            if (digest.Count > 0) db.Put(BedrockKeys.DigestKey(pos.X, pos.Z, dim), digest.ToArray());
        }
        db.Commit(LevelDb.CommitMode.AppendLog);
        MobNotes(result, stillThere, keptNew, keptNames, keepNewMobs);
        return expected;
    }

    static string? BedrockActorId(NbtCompound a) => a.GetLongOrNull("UniqueID") is long id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    static NbtCompound? TryActor(byte[] value)
    {
        try { return NbtBinary.ReadCompound(value, NbtFormat.Bedrock); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SaveFormatException or EndOfStreamException) { return null; }
    }

    /// <summary>The compounds one after another in a record (Bedrock's in-chunk mob and block entity lists).</summary>
    static List<NbtCompound> Records(byte[]? value)
    {
        if (value == null) return [];
        try { return NbtBinary.ReadAll(value, NbtFormat.Bedrock).Select(t => t.Tag).OfType<NbtCompound>().ToList(); }
        catch (Exception e) when (e is not OutOfMemoryException) { return []; } // a damaged record reads as none, like ReadRecords
    }

    static byte[] Concat(NbtList list)
    {
        using var ms = new MemoryStream();
        foreach (var a in list.OfType<NbtCompound>()) ms.Write(NbtBinary.Serialize(a, NbtFormat.Bedrock));
        return ms.ToArray();
    }

    /// <summary>The 8-byte storage key an actor carries (internalComponents), or none.</summary>
    static byte[]? StorageKeyOf(NbtCompound a)
    {
        if (a.GetCompound("internalComponents")?.GetCompound("EntityStorageKeyComponent")?.GetString("StorageKey") is not { Length: 8 } s) return null;
        var key = new byte[8];
        for (int i = 0; i < 8; i++) key[i] = (byte)(s[i] >= 0xDC80 && s[i] <= 0xDCFF ? s[i] - 0xDC00 : s[i]);
        return key;
    }

    static byte[] FreshStorageKey(HashSet<ulong> used)
    {
        uint low = 0x50000000;
        byte[] key;
        do key = ActorStorageKey((-1L << 32) | low++); while (!used.Add(BinaryPrimitives.ReadUInt64BigEndian(key)));
        return key;
    }

    // ---------------------------------------------------------------- Legacy Console, Pocket Edition

    static int RestoreConsole(ConsoleWorld backup, ConsoleWorld copy, string dimensionId, List<ChunkPos> chunks, PlacementResult result)
    {
        var from = BackupDimension(backup, dimensionId) as ConsoleDimension ?? throw new IOException("The backup has no such dimension.");
        var to = copy.Dimensions.OfType<ConsoleDimension>().First(d => CanonicalReader.DimensionId(d) == dimensionId);
        foreach (var pos in chunks)
            if (from.RawChunk(pos) is ConsoleRegion.Chunk chunk && from.ChunkBytes(pos) is byte[] data && !to.WriteChunk(pos, data, chunk.Flags))
                result.Notes.Add($"Chunk {pos} is not stored in the world, so it was left out.");
        copy.Save();
        result.Notes.Add("Console worlds: the chunks were brought back whole, their mobs as they were in the backup.");
        return -1;
    }

    static int RestorePocket(PocketLegacyWorld backup, PocketLegacyWorld copy, List<ChunkPos> chunks, PlacementResult result)
    {
        TransplantPocket(backup, copy, chunks, result);
        result.Notes.Add("Pocket Edition: the chunks and their mobs were brought back as they were in the backup.");
        return -1;
    }

    // ---------------------------------------------------------------- another format

    /// <summary>A backup in another format: each chunk's blocks, biomes, block entities and mobs taken from it, re-encoded by the world's writer.</summary>
    static PlacementResult RestoreReencoded(SaveWorld world, SaveWorld backup, WorldDimension fromDim, string dimensionId, List<ChunkPos> chunks, string output,
        bool keepNewMobs, IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        var reader = new CanonicalReader(backup, fromDim);
        var gate = new object();
        var (min, max) = Height(backup, CanonicalReader.DimensionId(fromDim));
        int minSection = min >> 4, maxSection = max >> 4;
        void Edit(CanonicalChunk c)
        {
            CanonicalChunk? b;
            lock (gate) b = reader.Get(new ChunkPos(c.X, c.Z));
            if (b == null) return;
            // Only the heights the backup's format holds come from it (a newer world keeps its deeper or higher land).
            foreach (var sy in c.Sections.Keys.Where(s => s >= minSection && s <= maxSection).ToList()) c.Sections.Remove(sy);
            foreach (var (sy, s) in b.Sections) if (sy >= minSection && sy <= maxSection) c.Sections[sy] = s;
            foreach (var sy in c.Biomes.Keys.Where(s => s >= minSection && s <= maxSection).ToList()) c.Biomes.Remove(sy);
            foreach (var (sy, s) in b.Biomes) if (sy >= minSection && sy <= maxSection) c.Biomes[sy] = s;
            if (b.ColumnBiomes != null) c.ColumnBiomes = b.ColumnBiomes;
            c.BlockEntities.Clear();
            c.BlockEntities.AddRange(b.BlockEntities.Select(e => (NbtCompound)e.Clone()));
            var today = c.Entities.ToList();
            c.Entities.Clear();
            c.Entities.AddRange(b.Entities.Select(e => (NbtCompound)e.Clone()));
            if (keepNewMobs)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in b.Entities) AddIds(e, ids, JavaEntityId);
                c.Entities.AddRange(today.Where(e => JavaEntityId(e) is not string id || !ids.Contains(id)));
            }
            c.NeedsShapeUpdate = true;
        }
        var result = EditIntoCopy(world, dimensionId, chunks, Edit, output, progress, cancel);
        // A Classic or Indev level takes the backup's blocks straight into its block array (its own note says so).
        if (world is not FiniteWorld)
            result.Notes.Insert(0, $"The backup is {backup.FormatDescription}, so its chunks were re-encoded as {world.FormatDescription} by this program's writer (blocks, biomes, chests, signs and mobs; a mob that has since moved elsewhere may now be there twice).");
        return result;
    }

    // ---------------------------------------------------------------- the check

    /// <summary>Each restored chunk reads back as in the backup: every block and biome, its block entities, and (where counted) its mobs.</summary>
    static void CheckRestored(SaveWorld backup, string copyPath, string dimensionId, List<ChunkPos> chunks, int expectedMobs, PlacementResult result, CancellationToken cancel)
    {
        using var copy = WorldLoader.Open(copyPath);
        var fromDim = BackupDimension(backup, dimensionId)!;
        var toDim = copy.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId);
        if (toDim == null) { result.Problems.Add("the copy no longer has this dimension"); return; }
        var rb = new CanonicalReader(backup, fromDim);
        var rc = new CanonicalReader(copy, toDim);
        int wrong = 0, tilesWrong = 0, mobs = copy is BedrockWorld bw ? StoredActors(bw, (BedrockDimension)toDim, chunks) : 0;
        foreach (var pos in chunks)
        {
            cancel.ThrowIfCancellationRequested();
            var b = rb.Get(pos);
            var c = rc.Get(pos);
            if (b == null) continue;
            if (c == null || !SameBlocks(b, c) || !SameBiomes(b, c)) { wrong++; continue; }
            if (c.BlockEntities.Count != b.BlockEntities.Count) tilesWrong++;
            if (copy is not BedrockWorld) mobs += c.Entities.Count;
        }
        if (wrong > 0) result.Problems.Add($"{wrong} restored chunk(s) did not read back as they are in the backup");
        if (tilesWrong > 0) result.Problems.Add($"{tilesWrong} restored chunk(s) lost chests, signs or other block entities");
        if (expectedMobs >= 0 && mobs != expectedMobs) result.Problems.Add($"the restored chunks hold {mobs:N0} mobs, not the {expectedMobs:N0} brought back");
    }

    /// <summary>The actors a Bedrock dimension stores in these chunks: in their actor records, and in the old in-chunk lists.</summary>
    static int StoredActors(BedrockWorld world, BedrockDimension dim, List<ChunkPos> chunks)
    {
        int n = 0;
        foreach (var pos in chunks)
        {
            if (world.Db.Get(BedrockKeys.DigestKey(pos.X, pos.Z, dim.Dimension)) is byte[] ids)
                for (int i = 0; i + 8 <= ids.Length; i += 8) if (world.Db.ContainsKey(BedrockKeys.ActorKey(ids.AsSpan(i, 8)))) n++;
            n += Records(world.Db.Get(BedrockKeys.Make(pos.X, pos.Z, dim.Dimension, ChunkTag.Entity))).Count;
        }
        return n;
    }
}
