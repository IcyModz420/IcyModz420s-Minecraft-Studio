// McTests/CompareTests.cs: "compare" — what differs between two worlds, and bringing chunks back from one.
// Owns: CompareTests — a world compared with itself has no differences; with a copy where ores were replaced, the
// differences are exactly the replaced blocks (same count, "gold ore → stone" and the like) and only in the box's chunks;
// a Java 1.16.5 world compared with its own conversion to Bedrock (another edition) is nearly all the same blocks.
// Restoring (StructurePlacement.RestoreIntoCopy): the changed chunks brought back from the original make the copy the
// same as the original again (Java and Bedrock); a mob that walked out of the area is not brought back twice; a new named
// mob there stays only when asked; a backup in another edition is re-encoded. The worlds are only read.
using System.Security.Cryptography;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class CompareTests
{
    /// <summary>
    /// A Classic level (Dojo): a chunk of a copy dug out, compared (only that chunk differs), brought back from the untouched
    /// level into a new copy, compared again (no difference left); the dug-out level itself not changed.
    /// </summary>
    static void ClassicLevel(string work)
    {
        string dojo = Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "Dojo_64_64_128.dat");
        if (!File.Exists(dojo)) { Program.Info("(skipped the Classic level: not in TestData)"); return; }
        string dir = Path.Combine(work, "classic");
        Directory.CreateDirectory(dir);
        string backup = Path.Combine(dir, "backup.dat"), world = Path.Combine(dir, "world.dat");
        File.Copy(dojo, backup);
        File.Copy(dojo, world);
        var hole = new ChunkPos(1, 1);
        using (var w = (MinecraftStudio.Core.Legacy.FiniteWorld)WorldLoader.Open(world))
        {
            var dim = (MinecraftStudio.Core.Legacy.FiniteDimension)w.Dimensions[0];
            for (int y = 40; y < 100; y++)
                for (int z = 16; z < 32; z++)
                    for (int x = 16; x < 32; x++) dim.SetBlock(x, y, z, 0, 0);
            w.Save();
        }
        byte[] griefed = File.ReadAllBytes(world);
        using (var w = WorldLoader.Open(world))
        using (var b = WorldLoader.Open(backup))
        {
            var cmp = WorldCompare.Run(b, Overworld(b), w, Overworld(w));
            Program.Check(cmp.Chunks.Select(c => c.Pos).SequenceEqual([hole]) && cmp.BlocksChanged > 0, $"Classic: the dug-out chunk is the only difference ({cmp.BlocksChanged:N0} blocks in {string.Join(" ", cmp.Chunks.Select(c => c.Pos))})");
            var r = StructurePlacement.RestoreIntoCopy(w, b, "minecraft:overworld", [hole], Path.Combine(dir, "restored"));
            Program.Check(r.Verified && r.BlocksPlaced > 0, $"Classic: the chunk brought back from the level as it was, in a copy, checked ({r.BlocksPlaced:N0} blocks; {string.Join("; ", r.Problems)})");
            using var restored = WorldLoader.Open(r.OpenPath);
            var again = WorldCompare.Run(b, Overworld(b), restored, Overworld(restored));
            Program.Check(again.Chunks.Count == 0 && again.Compared > 0, $"Classic: the restored copy is the level as it was again ({again.Chunks.Count} chunks differ)");
        }
        Program.Check(File.ReadAllBytes(world).AsSpan().SequenceEqual(griefed), "Classic: the dug-out level itself is not changed");
    }

    public static void Run(string[] args)
    {
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) { Program.Info("(skipped: the Java 1.16.5 sample is not in TestData)"); return; }
        var before = Hashes(java);
        string replaced = Path.Combine(TestPaths.Output, "compare", "replaced"), bedrock = Path.Combine(TestPaths.Output, "compare", "bedrock");
        string work = Path.Combine(TestPaths.Output, "compare", "restore");
        foreach (var d in new[] { replaced, bedrock, work }) if (Directory.Exists(d)) Directory.Delete(d, true);
        try
        {
            using (var a = WorldLoader.Open(java))
            using (var b = WorldLoader.Open(java))
            {
                var r = WorldCompare.Run(a, Overworld(a), b, Overworld(b));
                Program.Check(r.Chunks.Count == 0 && r.Same == r.Compared && r.Compared > 0, $"a world compared with itself: no differences ({r.Compared:N0} chunks the same)");
            }

            // Ores replaced in a box of a copy: the differences are exactly those blocks.
            ReplaceSummary summary;
            int minX, minZ;
            using (var w = WorldLoader.Open(java))
            {
                var dim = Overworld(w);
                var c = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).First();
                (minX, minZ) = (c.X * 16, c.Z * 16);
                var (low, _) = StructurePlacement.Height(w, "minecraft:overworld");
                (_, summary) = BlockReplace.ReplaceIntoCopy(w, "minecraft:overworld", minX, low, minZ, minX + 31, low + 80, minZ + 31, [new("*_ore", "stone")], replaced);
            }
            using (var a = WorldLoader.Open(java))
            using (var b = WorldLoader.Open(replaced))
            {
                var r = WorldCompare.Run(a, Overworld(a), b, Overworld(b));
                var boxChunks = new HashSet<ChunkPos> { new(minX >> 4, minZ >> 4), new((minX >> 4) + 1, minZ >> 4), new(minX >> 4, (minZ >> 4) + 1), new((minX >> 4) + 1, (minZ >> 4) + 1) };
                bool exact = summary.Changes.All(ch => r.Changes.GetValueOrDefault((ch.From, ch.To)) == ch.Count) && r.BlocksChanged == summary.Total;
                Program.Check(exact, $"compared with a copy where ores became stone: exactly the replaced blocks differ ({r.BlocksChanged:N0} of {summary.Total:N0}: {string.Join(", ", r.Changes.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Value} {kv.Key.From.Replace("minecraft:", "")} → {kv.Key.To.Replace("minecraft:", "")}"))})");
                Program.Check(r.Chunks.All(ch => boxChunks.Contains(ch.Pos)) && r.Chunks.Count > 0, $"and only in the box's chunks ({string.Join(" ", r.Chunks.Select(ch => ch.Pos))})");
                Program.Check(r.Chunks.Zip(r.Chunks.Skip(1)).All(p => p.First.Total >= p.Second.Total), "the most changed chunks come first");
            }
            Restored(java, replaced, Path.Combine(work, "java-back"), "Java");
            ClassicLevel(work);
            var (mobsBackup, area) = JavaMobs(java, work);

            // Another edition: the world and its own conversion to Bedrock are nearly all the same blocks.
            using (var w = WorldLoader.Open(java))
            {
                var target = TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock);
                var report = WorldConverter.Run(w, target, bedrock, new ConversionOptions { Dimensions = ["minecraft:overworld"] }, new Progress<ConversionProgress>(_ => { }));
                using var b = WorldLoader.Open(report.OpenPath ?? bedrock);
                var r = WorldCompare.Run(w, Overworld(w), b, Overworld(b));
                long cells = (long)r.Compared * 16 * 16 * 256;
                double changed = r.BlocksChanged / (double)Math.Max(1, cells);
                Program.Check(r.Compared > 0 && changed < 0.01, $"a Java world and its Bedrock conversion: {r.Compared:N0} chunks compared across editions, {changed:P2} of the blocks differ (the converter's stand-ins: {string.Join(", ", r.Changes.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Value} {kv.Key.From.Replace("minecraft:", "")} → {kv.Key.To.Replace("minecraft:", "")}"))})");
            }
            BedrockRestore(java, bedrock, work, minX, minZ, mobsBackup, area);
        }
        catch (Exception ex) { Program.Check(false, $"threw {ex.GetType().Name}: {ex.Message}"); }
        finally { foreach (var d in new[] { replaced, bedrock, work }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { } }
        Program.Check(before.SequenceEqual(Hashes(java)), "the world is byte for byte unchanged");
    }

    static WorldDimension Overworld(SaveWorld w) => w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");

    const string Ow = "minecraft:overworld";

    /// <summary>The chunks where <paramref name="changedWorld"/> differs from <paramref name="backupPath"/> brought back: then none differ.</summary>
    static void Restored(string backupPath, string changedWorld, string output, string what)
    {
        List<ChunkPos> changed;
        PlacementResult res;
        using (var backup = WorldLoader.Open(backupPath))
        using (var world = WorldLoader.Open(changedWorld))
        {
            changed = WorldCompare.Run(world, Overworld(world), backup, Overworld(backup)).Chunks.Select(c => c.Pos).ToList();
            res = StructurePlacement.RestoreIntoCopy(world, backup, Ow, changed, output);
        }
        Program.Check(res.Verified && res.ChunksChanged == changed.Count, $"{what}: the {changed.Count} changed chunks restored from the original into a copy, checked" + (res.Verified ? "" : ": " + string.Join("; ", res.Problems)));
        using (var backup = WorldLoader.Open(backupPath))
        using (var back = WorldLoader.Open(res.OpenPath))
        {
            var r = WorldCompare.Run(back, Overworld(back), backup, Overworld(backup));
            Program.Check(r.Chunks.Count == 0, $"{what}: the restored copy is the same as the original again ({r.Compared:N0} chunks compared, {r.Chunks.Count} differ)");
        }
    }

    /// <summary>
    /// Java 1.16.5 (mobs inside the chunks): a pig that walked out of the area since the backup is not brought back a
    /// second time; a new named wolf in the area goes, unless the mobs that are there now are kept.
    /// </summary>
    static (string Backup, ChunkPos Area) JavaMobs(string java, string work)
    {
        string backupPath = Path.Combine(work, "java-backup"), worldPath = Path.Combine(work, "java-world");
        CopyFolder(java, backupPath);
        ChunkPos area, away;
        using (var w = WorldLoader.Open(backupPath))
        {
            var chunks = Overworld(w).ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).ToList();
            (area, away) = (chunks[0], chunks[^1]);
        }
        // The backup: two pigs in the area chunk.
        EditJavaChunk(backupPath, area, list => { list.Add(Mob("minecraft:pig", area, 1, null)); list.Add(Mob("minecraft:pig", area, 2, null)); });
        int backupMobs = JavaMobsIn(backupPath, area);
        // Today: the first pig walked away; a new wolf called Rex came.
        CopyFolder(backupPath, worldPath);
        NbtCompound? walker = null;
        EditJavaChunk(worldPath, area, list =>
        {
            walker = list.OfType<NbtCompound>().First(e => e.GetInts("UUID") is { Length: 4 } u && u[0] == 0x1234 && u[3] == 1);
            list.Remove(walker);
            list.Add(Mob("minecraft:wolf", area, 3, "Rex"));
        });
        EditJavaChunk(worldPath, away, list => list.Add(walker!));
        foreach (var (keep, name) in new[] { (false, "java-back-mobs"), (true, "java-back-keep") })
        {
            PlacementResult res;
            using (var world = WorldLoader.Open(worldPath))
            using (var backup = WorldLoader.Open(backupPath))
                res = StructurePlacement.RestoreIntoCopy(world, backup, Ow, [area], Path.Combine(work, name), keepNewMobs: keep);
            int mobs = JavaMobsIn(res.OpenPath, area);
            var here = JavaMobIds(res.OpenPath, area);
            bool rex = here.Contains(3), pigBack = here.Contains(1), pigAway = JavaMobIds(res.OpenPath, away).Contains(1);
            Program.Check(res.Verified && !pigBack && pigAway && rex == keep && mobs == backupMobs - 1 + (keep ? 1 : 0),
                $"Java mobs{(keep ? ", keeping today's" : "")}: the pig that walked away is not brought back twice (it is still where it went), Rex the wolf {(keep ? "stays" : "goes")} ({mobs} mobs in the chunk; notes: {string.Join(" | ", res.Notes)}){(res.Verified ? "" : " PROBLEMS: " + string.Join("; ", res.Problems))}");
            if (keep) Program.Check(res.Notes.Any(n => n.Contains("Rex")), "the notes name the mob that stayed");
        }
        return (backupPath, area);
    }

    /// <summary>Bedrock (actors in records of their own): restored copy = original; an actor that walked away is not doubled; another edition's backup is re-encoded.</summary>
    static void BedrockRestore(string java, string bedrock, string work, int minX, int minZ, string javaWithPigs, ChunkPos area)
    {
        string changedPath = Path.Combine(work, "bedrock-replaced");
        using (var w = WorldLoader.Open(bedrock))
        {
            var (low, _) = StructurePlacement.Height(w, Ow);
            BlockReplace.ReplaceIntoCopy(w, Ow, minX, low, minZ, minX + 31, low + 80, minZ + 31, [new("*_ore", "stone")], changedPath);
        }
        Restored(bedrock, changedPath, Path.Combine(work, "bedrock-back"), "Bedrock");

        // Another edition: the Java original restored into the Bedrock copy is re-encoded.
        using (var world = WorldLoader.Open(changedPath))
        using (var backup = WorldLoader.Open(java))
        {
            var chunks = WorldCompare.Run(world, Overworld(world), backup, Overworld(backup)).Chunks.Where(c => c.Presence == ChunkPresence.Both).Select(c => c.Pos).Take(4).ToList();
            var re = StructurePlacement.RestoreIntoCopy(world, backup, Ow, chunks, Path.Combine(work, "bedrock-from-java"));
            Program.Check(chunks.Count > 0 && re.Verified && re.Notes.Any(n => n.Contains("re-encoded")), $"a Java backup restored into a Bedrock world: {chunks.Count} chunks re-encoded, checked{(re.Verified ? "" : ": " + string.Join("; ", re.Problems))}");
        }

        // An actor moved out of the area (its digest entry and position) is not brought back a second time. The backup:
        // the Java copy with two pigs in the area chunk, converted to Bedrock (each mob an actor record of its own).
        string backupPath = Path.Combine(work, "bedrock-pigs");
        ConversionReport pigsReport;
        using (var w = WorldLoader.Open(javaWithPigs))
            pigsReport = WorldConverter.Run(w, TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock), backupPath, new ConversionOptions { Dimensions = [Ow] }, new Progress<ConversionProgress>(_ => { }));
        WalkAway(backupPath, Path.Combine(work, "bedrock-world"), area, Path.Combine(work, "bedrock-back-mobs"), pigsReport);
        // The same with the mobs as actor records of their own (how the game keeps them since 1.18.30).
        string modern = Path.Combine(work, "bedrock-pigs-actors");
        CopyFolder(backupPath, modern);
        ToActorRecords(modern);
        WalkAway(modern, Path.Combine(work, "bedrock-world-actors"), area, Path.Combine(work, "bedrock-back-actors"), pigsReport);
    }

    /// <summary>A pig moved out of <paramref name="area"/> in a copy of the backup; the area restored from the backup: the pig is there once.</summary>
    static void WalkAway(string backupPath, string worldPath, ChunkPos area, string output, ConversionReport pigsReport)
    {
        CopyFolder(backupPath, worldPath);
        ChunkPos away;
        long movedId;
        int backupMobs;
        bool records;
        using (var w = (BedrockWorld)WorldLoader.Open(worldPath))
        {
            var dim = (BedrockDimension)Overworld(w);
            var mobs = BedrockMobs(w, area);
            if (mobs.Count(m => m.Mob.GetString("identifier") == "minecraft:pig") < 2)
            {
                Program.Check(false, $"the two pigs are mobs of chunk {area} in the Bedrock conversion ({mobs.Count} there; losses: {string.Join(" | ", pigsReport.Losses.Take(4))})");
                return;
            }
            away = dim.ChunkPositions.First(p => p != area && Math.Abs(p.X - area.X) + Math.Abs(p.Z - area.Z) > 4);
            backupMobs = mobs.Count;
            var (pig, key) = mobs.First(m => m.Mob.GetString("identifier") == "minecraft:pig");
            movedId = pig.GetLong("UniqueID");
            pig["Pos"] = new NbtList(NbtType.Float) { new NbtFloat(away.X * 16 + 8), new NbtFloat(100), new NbtFloat(away.Z * 16 + 8) };
            records = key != null;
            if (key != null)
            {
                // An actor of its own: its record says where it is, the chunks' digests list it.
                w.Db.Put(BedrockKeys.ActorKey(key), NbtBinary.Serialize(pig, NbtFormat.Bedrock));
                var ids = w.Db.Get(BedrockKeys.DigestKey(area.X, area.Z, 0))!;
                int at = Enumerable.Range(0, ids.Length / 8).First(i => ids.AsSpan(i * 8, 8).SequenceEqual(key));
                w.Db.Put(BedrockKeys.DigestKey(area.X, area.Z, 0), [.. ids[..(at * 8)], .. ids[(at * 8 + 8)..]]);
                w.Db.Put(BedrockKeys.DigestKey(away.X, away.Z, 0), [.. w.Db.Get(BedrockKeys.DigestKey(away.X, away.Z, 0)) ?? [], .. key]);
            }
            else
            {
                // In the chunks' own mob lists: out of one, into the other.
                var here = BedrockKeys.Make(area.X, area.Z, 0, ChunkTag.Entity);
                var there = BedrockKeys.Make(away.X, away.Z, 0, ChunkTag.Entity);
                var left = NbtBinary.ReadAll(w.Db.Get(here)!, NbtFormat.Bedrock).Select(t => t.Tag).OfType<NbtCompound>().Where(e => e.GetLong("UniqueID") != movedId);
                w.Db.Put(here, left.SelectMany(e => NbtBinary.Serialize(e, NbtFormat.Bedrock)).ToArray());
                w.Db.Put(there, [.. w.Db.Get(there) ?? [], .. NbtBinary.Serialize(pig, NbtFormat.Bedrock)]);
            }
            w.Db.Commit(MinecraftStudio.Core.LevelDb.CommitMode.AppendLog);
        }
        PlacementResult res;
        using (var world = WorldLoader.Open(worldPath))
        using (var backup = WorldLoader.Open(backupPath))
            res = StructurePlacement.RestoreIntoCopy(world, backup, Ow, [area], output);
        using (var back = (BedrockWorld)WorldLoader.Open(res.OpenPath))
        {
            var dim = (BedrockDimension)Overworld(back);
            var here = BedrockMobs(back, area).Select(m => m.Mob.GetLong("UniqueID")).ToList();
            int everywhere = dim.ChunkPositions.Sum(p => BedrockMobs(back, p).Count(m => m.Mob.GetLong("UniqueID") == movedId));
            Program.Check(res.Verified && !here.Contains(movedId) && here.Count == backupMobs - 1 && everywhere == 1,
                $"Bedrock mobs ({(records ? "actor records" : "in-chunk lists")}): the pig that walked away is not brought back twice ({here.Count} of the backup's {backupMobs} back in the chunk, that pig {everywhere} time(s) in the world){(res.Verified ? "" : " PROBLEMS: " + string.Join("; ", res.Problems))}");
        }
    }

    /// <summary>Every in-chunk mob list of the overworld turned into actor records with a digest per chunk (the game's newer storage).</summary>
    static void ToActorRecords(string world)
    {
        using var w = (BedrockWorld)WorldLoader.Open(world);
        foreach (var k in w.Db.Keys())
        {
            if (!BedrockKeys.TryParseChunkKey(k, out var ck) || ck.Tag != ChunkTag.Entity || ck.Dimension != 0) continue;
            var digest = new List<byte>();
            foreach (var t in NbtBinary.ReadAll(w.Db.Get(k)!, NbtFormat.Bedrock))
            {
                if (t.Tag is not NbtCompound e) continue;
                long id = e.GetLong("UniqueID");
                var key = new byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(key, -(int)(id >> 32));
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(4), (int)id);
                e["internalComponents"] = new NbtCompound
                {
                    { "EntityStorageKeyComponent", new NbtCompound { { "StorageKey", new NbtString(new string(key.Select(b => b < 0x80 ? (char)b : (char)(0xDC00 + b)).ToArray())) } } },
                };
                w.Db.Put(BedrockKeys.ActorKey(key), NbtBinary.Serialize(e, NbtFormat.Bedrock));
                digest.AddRange(key);
            }
            w.Db.Delete(k);
            if (digest.Count > 0) w.Db.Put(BedrockKeys.DigestKey(ck.X, ck.Z, 0), digest.ToArray());
        }
        w.Db.Commit(MinecraftStudio.Core.LevelDb.CommitMode.AppendLog);
    }

    /// <summary>A Bedrock chunk's mobs: actor records listed in its digest (with their keys), and its own in-chunk list.</summary>
    static List<(NbtCompound Mob, byte[]? Key)> BedrockMobs(BedrockWorld w, ChunkPos pos)
    {
        var list = new List<(NbtCompound, byte[]?)>();
        if (w.Db.Get(BedrockKeys.DigestKey(pos.X, pos.Z, 0)) is byte[] ids)
            for (int i = 0; i + 8 <= ids.Length; i += 8)
                if (w.Db.Get(BedrockKeys.ActorKey(ids.AsSpan(i, 8))) is byte[] v) list.Add((NbtBinary.ReadCompound(v, NbtFormat.Bedrock), ids.AsSpan(i, 8).ToArray()));
        if (w.Db.Get(BedrockKeys.Make(pos.X, pos.Z, 0, ChunkTag.Entity)) is byte[] inChunk)
            foreach (var t in NbtBinary.ReadAll(inChunk, NbtFormat.Bedrock)) if (t.Tag is NbtCompound c) list.Add((c, null));
        return list;
    }

    static NbtCompound Mob(string id, ChunkPos at, int n, string? name)
    {
        var e = new NbtCompound
        {
            { "id", new NbtString(id) },
            { "Pos", new NbtList(NbtType.Double) { new NbtDouble(at.X * 16 + 4 + n), new NbtDouble(120), new NbtDouble(at.Z * 16 + 8) } },
            { "Motion", new NbtList(NbtType.Double) { new NbtDouble(0), new NbtDouble(0), new NbtDouble(0) } },
            { "Rotation", new NbtList(NbtType.Float) { new NbtFloat(0), new NbtFloat(0) } },
            { "Health", new NbtFloat(10) },
            { "UUID", new NbtIntArray([0x1234, 0x5678, 0x9abc, n]) },
        };
        if (name != null) e.SetString("CustomName", "{\"text\":\"" + name + "\"}");
        return e;
    }

    static void EditJavaChunk(string world, ChunkPos pos, Action<NbtList> edit)
    {
        string file = Path.Combine(world, "region", $"r.{pos.RegionX}.{pos.RegionZ}.mca");
        var region = RegionFile.Open(file);
        int index = RegionFile.Index(pos.X & 31, pos.Z & 31);
        var nbt = region.GetChunkNbt(index)!;
        var level = nbt.GetCompound("Level")!;
        var list = level.GetList("Entities") ?? new NbtList(NbtType.Compound);
        edit(list);
        level["Entities"] = list;
        region.SetChunk(index, nbt);
        region.Save(file);
    }

    static List<NbtCompound> JavaMobList(string world, ChunkPos pos)
    {
        var region = RegionFile.Open(Path.Combine(world, "region", $"r.{pos.RegionX}.{pos.RegionZ}.mca"));
        return region.GetChunkNbt(RegionFile.Index(pos.X & 31, pos.Z & 31))?.GetCompound("Level")?.GetList("Entities")?.OfType<NbtCompound>().ToList() ?? [];
    }

    static int JavaMobsIn(string world, ChunkPos pos) => JavaMobList(world, pos).Count;

    static HashSet<int> JavaMobIds(string world, ChunkPos pos) =>
        JavaMobList(world, pos).Where(e => e.GetInts("UUID") is { Length: 4 } u && u[0] == 0x1234).Select(e => e.GetInts("UUID")![3]).ToHashSet();

    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)));
    }

    static List<string> Hashes(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => Path.GetRelativePath(folder, f) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToList();
}
