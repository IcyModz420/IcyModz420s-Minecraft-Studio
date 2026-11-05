// McTests/RescueTests.cs: rescue: damaged copies of real test worlds (a wiped region header, broken chunks with
// and without an older copy in the file, a garbled or missing level.dat, a deleted database index, a damaged
// file inside a .mcworld, a big chunk left only in its .mcc file, a misplaced chunk, damaged player and raid
// files) are opened for rescue, converted into new worlds and written as repaired copies: everything readable
// must come through (the repaired copies byte for byte), everything else must be reported, and the damaged
// world must not be changed.
// Owns: RescueTests.
using System.IO.Compression;
using System.Security.Cryptography;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

/// <summary>
/// rescue: damaged copies of real test worlds (a wiped region header, broken chunks with and without an older copy in
/// the file, a garbled or missing level.dat, a deleted database index, a damaged file inside a .mcworld, a big chunk
/// left only in its .mcc file, a misplaced chunk, damaged player and raid files) are opened for rescue, converted into
/// new worlds and written as repaired copies: everything readable must come through (the repaired copies byte for byte),
/// everything else must be reported, and the damaged world must not be changed.
/// </summary>
static class RescueTests
{
    public static void Run(string[] args)
    {
        string temp = Path.Combine(TestPaths.Output, "rescue-damaged");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        WorldRescue.UnpackFolder = Path.Combine(temp, "unpacked");
        Console.WriteLine("== Java: wiped region header, broken chunks, garbled level.dat");
        Java(temp);
        Console.WriteLine("== Java 1.16.5: no level.dat, wiped header in a custom dimension, a big chunk only in its .mcc, a misplaced chunk, damaged player and raid files");
        JavaRepair(temp);
        Console.WriteLine("== Java before Beta 1.3 (one file per chunk): a chunk file emptied by a crash");
        AlphaRepair(temp);
        Console.WriteLine("== Bedrock: deleted database index, garbled level.dat");
        Bedrock(temp);
        Console.WriteLine("== Bedrock without any level.dat");
        BedrockWithoutLevelDat(temp);
        Console.WriteLine("== A .mcworld with a damaged file inside");
        Package(temp);
        Console.WriteLine("== The version a rescued world is written for");
        Targets();
    }

    static string Copy(string src, string dest)
    {
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dest, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        return dest;
    }

    static string Hash(string folder) => Convert.ToHexString(SHA1.HashData(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
        .SelectMany(f => File.ReadAllBytes(f).Concat(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, f)))).ToArray()));

    static string Failed(ConversionReport r) => string.Join("; ", r.Checks.Where(c => !c.Passed).Select(c => c.Name + " " + c.Detail));

    static void Print(ConversionReport r)
    {
        foreach (var n in r.Notes) Program.Info("note: " + n);
        foreach (var l in r.Losses) Program.Info("lost: " + l);
        foreach (var w in r.Warnings) Program.Info("warning: " + w);
    }

    static void Java(string temp)
    {
        string src = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_13");
        if (!Directory.Exists(src)) { Program.Info("Java test world missing: skipped"); return; }
        string w = Copy(src, Path.Combine(temp, "java"));
        string pristinePath = Copy(src, Path.Combine(temp, "java-pristine"));
        int original;
        using (var ok = WorldLoader.Open(w)) original = ok.Dimensions.Sum(d => d.ChunkPositions.Count);

        var regions = Directory.GetFiles(Path.Combine(w, "region"), "*.mca").OrderByDescending(f => new FileInfo(f).Length).ToList();
        // 1. A wiped header: the chunks are still in the file, but nothing points to them.
        var wiped = File.ReadAllBytes(regions[0]);
        int wipedChunks = RegionFile.FromBytes(wiped, 0, 0).ChunkCount;
        Array.Clear(wiped, 0, 4096);
        File.WriteAllBytes(regions[0], wiped);
        // 2. Chunk a: an older copy is left at the end of the file (as the game leaves them), then its current copy is
        //    broken. Chunk b: broken, with no copy.
        var r1 = RegionFile.Open(regions[1]);
        var slots = r1.ChunkIndexes().ToList();
        int a = slots[0], b = slots[1];
        var file = File.ReadAllBytes(regions[1]).ToList();
        while (file.Count % 4096 != 0) file.Add(0);
        int sa = r1.StoredSectorOffset(a), na = r1.StoredSectorCount(a), sb = r1.StoredSectorOffset(b);
        file.AddRange(file.Skip(sa * 4096).Take(na * 4096).ToList());
        var bytes = file.ToArray();
        bytes[sa * 4096 + 5] = 0; bytes[sa * 4096 + 6] = 0;   // the zlib header of a's current copy
        bytes[sb * 4096 + 5] = 0; bytes[sb * 4096 + 6] = 0;   // and of b
        File.WriteAllBytes(regions[1], bytes);
        // 3. level.dat garbled; the game's copy (level.dat_old) is fine.
        File.WriteAllBytes(Path.Combine(w, "level.dat"), [0x1F, 0x8B, 1, 2, 3, 4, 5]);
        string before = Hash(w);

        // The normal reader loses the wiped region's chunks and cannot open the world's settings.
        bool normalFails;
        string? normalError = null;
        try { using var n = WorldLoader.Open(w); normalFails = n.Dimensions.Sum(d => d.ChunkPositions.Count) < original; }
        catch (Exception e) { normalFails = true; normalError = e.Message; }
        Program.Check(normalFails, "the damage really defeats the normal reader");
        Program.Check(normalError == null || normalError.Contains("level.dat is damaged"), $"and its error names the damaged file ({normalError})");

        var findings = new List<string>();
        using (var rescued = WorldRescue.Open(w, findings))
        {
            foreach (var f in findings) Program.Info("finding: " + f);
            int listed = rescued.Dimensions.Sum(d => d.ChunkPositions.Count);
            Program.Check(listed == original, $"rescue lists {listed} chunks, the world had {original}");
            Program.Check(findings.Any(f => f.Contains("level.dat_old")), "level.dat_old was used");
            Program.Check(findings.Any(f => f.Contains($"{wipedChunks} chunks the file's header had lost were found")), $"all {wipedChunks} chunks of the wiped region were found");
            Program.Check(findings.Any(f => f.Contains("1 damaged chunk was replaced by an older copy")), "the broken chunk with an older copy was rescued from it");
            Program.Check(findings.Any(f => f.Contains("1 damaged chunk has no readable copy")), "the broken chunk without a copy is reported lost");
            Program.Check(findings.Any(f => f.Contains("level.dat is damaged (its compressed data is broken)")), "the damage is described in plain words");
            var target = TargetCatalog.All.Where(t => t.Kind == TargetKind.Java).OrderBy(t => Math.Abs(t.DataVersion - 1519)).First();
            Program.Check(TargetCatalog.ClosestTo(rescued) == target, $"the rescue is offered for the world's own version ({TargetCatalog.ClosestTo(rescued).Name})");
            string outDir = Path.Combine(temp, "java-rescued");
            var report = WorldConverter.Run(rescued, target, outDir, new ConversionOptions());
            Program.Check(report.Chunks == original - 1 && report.FailedChunks == 1, $"{report.Chunks} chunks rescued into {target.Name} (expected {original - 1}), {report.FailedChunks} lost (expected 1)");
            Program.Check(report.Verified, "the rescued world passes the checks: " + Failed(report));
            Program.Check(ThrowsAny(() => rescued.Save()) && ThrowsAny(() => rescued.Dimensions[0].SaveChanges()), "saving the damaged world itself is refused");
            Program.Check(report.Losses.Any(l => l.Lost && l.ToString().Contains("Damaged chunk")), "the lost chunk is in the report");

            // The repaired copy: nothing converted, every readable chunk byte for byte.
            string repairedDir = Path.Combine(temp, "java-repaired");
            var repair = WorldRepair.Write(rescued, repairedDir, "Repaired");
            Print(repair);
            Program.Check(repair.Verified, "the repaired copy passes its checks: " + Failed(repair));
            Program.Check(repair.Chunks == original - 1 && repair.FailedChunks == 1, $"{repair.Chunks} chunks repaired (expected {original - 1}), {repair.FailedChunks} lost (expected 1)");
            Program.Check(repair.Losses.Count(l => l.Lost) == 1 && repair.Losses[0].Count == 1, "the lost chunk is the only loss");
            using var copy = WorldLoader.Open(repairedDir);
            using var pristine = WorldLoader.Open(pristinePath);
            // Every chunk equals the undamaged world's (the older copy of chunk a holds the same data).
            int same = 0, differ = 0;
            foreach (var d in pristine.Dimensions)
            {
                var cd = copy.Dimensions.First(x => x.Id == d.Id);
                foreach (var pos in cd.ChunkPositions)
                {
                    var x = d.LoadChunkNbt(pos);
                    var y = cd.LoadChunkNbt(pos);
                    if (x != null && y != null && x.ContentEquals(y)) same++; else differ++;
                }
            }
            Program.Check(differ == 0 && same == original - 1, $"{same} repaired chunks equal the undamaged world's, {differ} differ");
            Program.Check(copy.Name == "Repaired" && ((JavaWorld)copy).Data?.GetLong("RandomSeed") == ((JavaWorld)pristine).Data?.GetLong("RandomSeed"),
                "level.dat comes from level.dat_old with the new name");
            Program.Check(Directory.EnumerateFiles(Path.Combine(src, "playerdata")).All(p => File.ReadAllBytes(p).SequenceEqual(File.ReadAllBytes(Path.Combine(repairedDir, "playerdata", Path.GetFileName(p))))),
                "the players are copied exactly");
        }
        Program.Check(Hash(w) == before, "the damaged world itself is not changed");
    }

    static void JavaRepair(string temp)
    {
        string src = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(src)) { Program.Info("Java 1.16.5 test world missing: skipped"); return; }
        string w = Copy(src, Path.Combine(temp, "java-dims"));
        Dictionary<string, int> counts;
        using (var ok = WorldLoader.Open(w)) counts = ok.Dimensions.ToDictionary(d => d.Id, d => d.ChunkPositions.Count);

        // 1. The custom dimension's biggest region loses its header.
        var custom = Directory.GetFiles(Path.Combine(w, "dimensions", "inf", "inf_dim", "region"), "*.mca").OrderByDescending(f => new FileInfo(f).Length).First();
        int customChunks = RegionFile.Open(custom).ChunkCount;
        var cb = File.ReadAllBytes(custom);
        Array.Clear(cb, 0, 4096);
        File.WriteAllBytes(custom, cb);
        // 2. A misplaced chunk: slot j points to slot i's data.
        string over = Directory.GetFiles(Path.Combine(w, "region"), "*.mca").OrderByDescending(f => new FileInfo(f).Length).First();
        var overRegion = RegionFile.Open(over);
        var slots = overRegion.ChunkIndexes().ToList();
        int i = slots[0], j = slots[1];
        var ob = File.ReadAllBytes(over);
        Array.Copy(ob, i * 4, ob, j * 4, 4);
        File.WriteAllBytes(over, ob);
        // 3. A chunk too big for its region file (kept in c.161.161.mcc) whose region header is gone.
        string bigPath = Path.Combine(w, "region", "r.5.5.mca");
        var noise = new byte[1_300_000];
        new Random(5).NextBytes(noise);
        var bigChunk = new NbtCompound
        {
            { "DataVersion", new NbtInt(2586) },
            { "Level", new NbtCompound { { "xPos", new NbtInt(161) }, { "zPos", new NbtInt(161) }, { "LastUpdate", new NbtLong(1) }, { "Noise", new NbtByteArray(noise) } } },
        };
        var big = RegionFile.CreateEmpty(bigPath, 5, 5);
        int bigIndex = RegionFile.Index(161 & 31, 161 & 31);
        big.SetChunk(bigIndex, bigChunk, CompressionKind.Zlib, 1234);
        big.Save();
        string mcc = Path.Combine(w, "region", "c.161.161.mcc");
        Program.Check(File.Exists(mcc), "the big chunk was written to its own .mcc file");
        var mccBytes = File.ReadAllBytes(mcc);
        var bb = File.ReadAllBytes(bigPath);
        Array.Clear(bb, 0, 4096);
        File.WriteAllBytes(bigPath, bb);
        // 4. A 1.17-style entities file whose header is gone.
        string entPath = Path.Combine(w, "entities", "r.0.0.mca");
        var ent = RegionFile.CreateEmpty(entPath, 0, 0);
        foreach (var (x, z) in new[] { (3, 4), (7, 9) })
            ent.SetChunk(RegionFile.Index(x, z), new NbtCompound { { "DataVersion", new NbtInt(2730) }, { "Position", new NbtIntArray([x, z]) } }, CompressionKind.Zlib, 1);
        ent.Save();
        var entOriginal = RegionFile.Open(entPath);
        var eb = File.ReadAllBytes(entPath);
        Array.Clear(eb, 0, 4096);
        File.WriteAllBytes(entPath, eb);
        // 5. A damaged player file (the game's .dat_old is fine) and a damaged raids.dat with no copy.
        string player = Directory.GetFiles(Path.Combine(w, "playerdata"), "*.dat").First();
        var playerOld = File.ReadAllBytes(player + "_old");
        File.WriteAllBytes(player, [0x1F, 0x8B, 8, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3]);
        File.WriteAllBytes(Path.Combine(w, "data", "raids.dat"), [0x1F, 0x8B, 8, 0, 9, 9, 9]);
        // A scoreboard emptied by a crash (all zeros, no copy), a server's uid.dat (not NBT at all), and a region
        // file that is nothing but zeros.
        File.WriteAllBytes(Path.Combine(w, "data", "scoreboard.dat"), new byte[512]);
        var uid = new byte[16];
        new Random(7).NextBytes(uid);
        File.WriteAllBytes(Path.Combine(w, "uid.dat"), uid);
        File.WriteAllBytes(Path.Combine(w, "region", "r.7.7.mca"), new byte[12288]);
        // 6. An old McRegion file left from before 1.2 (the game keeps them after converting to Anvil).
        string oldRegion = Directory.GetFiles(Path.Combine(w, "region"), "*.mca").Where(f => f != over && f != bigPath).OrderByDescending(f => new FileInfo(f).Length).First();
        string mcr = Path.ChangeExtension(oldRegion, ".mcr");
        File.Copy(oldRegion, mcr);
        // 7. No level.dat at all.
        File.Delete(Path.Combine(w, "level.dat"));
        File.Delete(Path.Combine(w, "level.dat_old"));
        string before = Hash(w);

        var findings = new List<string>();
        using (var rescued = WorldRescue.Open(w, findings))
        {
            foreach (var f in findings) Program.Info("finding: " + f);
            var target = TargetCatalog.ClosestTo(rescued);
            Program.Check(target.Name == "Java 1.16.2 – 1.16.5", $"without level.dat the chunks say which version wrote them ({target.Name})");
            var customDim = rescued.Dimensions.OfType<JavaRegionDimension>().First(d => d.RegionFolder.Contains("inf_dim"));
            Program.Check(customDim.ChunkPositions.Count == counts[customDim.Id], $"the custom dimension's chunks were all found ({customDim.ChunkPositions.Count} of {counts[customDim.Id]})");
            var bigDim = rescued.Dimensions.FirstOrDefault(d => d.ChunkPositions.Contains(new ChunkPos(161, 161)));
            Program.Check(bigDim?.LoadChunkNbt(new ChunkPos(161, 161))?.GetCompound("Level")?.GetBytes("Noise")?.AsSpan().SequenceEqual(noise) == true,
                "the big chunk in its own .mcc file was found and loads with all its data");

            string outDir = Path.Combine(temp, "java-dims-repaired");
            var report = WorldRepair.Write(rescued, outDir);
            Print(report);
            Program.Check(report.Verified, "the repaired copy passes its checks: " + Failed(report));
            using var copy = WorldLoader.Open(outDir);
            var copyCustom = copy.Dimensions.First(d => d.Id == customDim.Id);
            Program.Check(copyCustom.ChunkPositions.Count == counts[customDim.Id], $"the repaired custom dimension has all {counts[customDim.Id]} chunks ({customChunks} of them from the wiped region)");
            // Misplaced data is never kept in the wrong slot; the slot it belongs to is unchanged.
            var repairedOver = RegionFile.Open(Path.Combine(outDir, "region", Path.GetFileName(over)));
            var (jx, jz) = overRegion.ChunkCoords(j);
            Program.Check(!repairedOver.HasChunk(j) || RegionFile.PositionOf(repairedOver.GetChunkNbt(j)!) == (jx, jz), "the misplaced chunk was not kept in the wrong place");
            Program.Check(repairedOver.GetRaw(i)!.Payload.AsSpan().SequenceEqual(overRegion.GetRaw(i)!.Payload), "the chunk whose data it pointed to is unchanged");
            // The big chunk goes back into its own .mcc file, byte for byte.
            var repairedBig = RegionFile.Open(Path.Combine(outDir, "region", "r.5.5.mca"));
            Program.Check(repairedBig.IsExternal(bigIndex) && repairedBig.GetRaw(bigIndex)!.Payload.AsSpan().SequenceEqual(mccBytes), "the big chunk is in its .mcc file again, byte for byte");
            // Entities found by their Position.
            var repairedEnt = RegionFile.Open(Path.Combine(outDir, "entities", "r.0.0.mca"));
            Program.Check(repairedEnt.ChunkCount == 2 && new[] { (3, 4), (7, 9) }.All(p => repairedEnt.GetRaw(RegionFile.Index(p.Item1, p.Item2))!.Payload.AsSpan().SequenceEqual(entOriginal.GetRaw(RegionFile.Index(p.Item1, p.Item2))!.Payload)),
                "both entity chunks of the wiped entities file were found by their Position");
            // Points of interest and the data pack are kept.
            bool poiSame = Directory.GetFiles(Path.Combine(src, "poi")).All(p =>
            {
                var x = RegionFile.Open(p);
                string other = Path.Combine(outDir, "poi", Path.GetFileName(p));
                if (x.ChunkCount == 0) return !File.Exists(other);
                var y = RegionFile.Open(other);
                return x.ChunkIndexes().SequenceEqual(y.ChunkIndexes()) && x.ChunkIndexes().All(k => x.GetRaw(k)!.Payload.AsSpan().SequenceEqual(y.GetRaw(k)!.Payload));
            });
            Program.Check(poiSame, "the points of interest are kept byte for byte");
            string pack = Directory.GetFiles(Path.Combine(src, "datapacks")).First();
            Program.Check(File.ReadAllBytes(pack).SequenceEqual(File.ReadAllBytes(Path.Combine(outDir, "datapacks", Path.GetFileName(pack)))), "the data pack is kept");
            // The damaged player came back from the game's copy; raids.dat could not be saved and is listed.
            Program.Check(File.ReadAllBytes(Path.Combine(outDir, "playerdata", Path.GetFileName(player))).SequenceEqual(playerOld), "the damaged player file was replaced by the game's copy of it");
            Program.Check(!File.Exists(Path.Combine(outDir, "data", "raids.dat")) && report.Losses.Any(l => l.Lost && l.What.Contains("raids.dat")), "the damaged raids.dat was left out and listed");
            Program.Check(!File.Exists(Path.Combine(outDir, "data", "scoreboard.dat")) && report.Losses.Any(l => l.Lost && l.What.Contains("scoreboard.dat")), "the zeroed scoreboard.dat was left out and listed");
            Program.Check(File.ReadAllBytes(Path.Combine(outDir, "uid.dat")).SequenceEqual(uid), "a .dat file that is not NBT is copied as it is");
            Program.Check(!File.Exists(Path.Combine(outDir, "region", "r.7.7.mca")) && report.Warnings.Any(x => x.Contains("r.7.7.mca") && x.Contains("no readable chunk")), "the zeroed region file is reported");
            Program.Check(((JavaWorld)copy).Data != null && report.Notes.Any(n => n.Contains("No readable level.dat")), "a new level.dat was written");
            Program.Check(((JavaWorld)copy).DataVersion == target.DataVersion, $"for the version the chunks came from (data version {((JavaWorld)copy).DataVersion})");
            Program.Check(!File.Exists(Path.Combine(outDir, "session.lock")), "session.lock is left for the game to make");
            Program.Check(File.Exists(Path.Combine(outDir, "region", Path.GetFileName(mcr))) && report.Notes.Any(n => n.Contains("region files the game does not read")),
                "the old McRegion file is kept without being counted as the dimension's chunks");
        }
        Program.Check(Hash(w) == before, "the damaged world itself is not changed");
    }

    static void AlphaRepair(string temp)
    {
        // pymclevel's chunk files laid out the way worlds from before Beta 1.3 keep them (base-36 folders).
        string chunks = Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "TileTicks_chunks");
        if (!Directory.Exists(chunks)) { Program.Info("pymclevel chunk files missing: skipped"); return; }
        string w = Path.Combine(temp, "alpha");
        Directory.CreateDirectory(w);
        var level = new NbtFile { Format = NbtFormat.Java, Compression = CompressionKind.Gzip };
        level.Roots.Add(new NamedTag("", new NbtCompound { { "Data", new NbtCompound { { "LevelName", new NbtString("Alpha") }, { "RandomSeed", new NbtLong(42) }, { "SpawnX", new NbtInt(0) }, { "SpawnY", new NbtInt(64) }, { "SpawnZ", new NbtInt(0) } } } }));
        level.Save(Path.Combine(w, "level.dat"));
        int count = 0;
        foreach (var f in Directory.GetFiles(chunks, "c.*.dat"))
        {
            var parts = Path.GetFileName(f).Split('.');
            if (parts.Length != 4 || !Base36.TryParse(parts[1], out int x) || !Base36.TryParse(parts[2], out int z)) continue;
            string to = AlphaDimension.ChunkPath(w, x, z);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
            count++;
        }
        // One chunk file emptied by a crash.
        string zeroed = Directory.GetFiles(w, "c.*.dat", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).First();
        File.WriteAllBytes(zeroed, new byte[new FileInfo(zeroed).Length]);
        string before = Hash(w);
        var findings = new List<string>();
        using (var rescued = WorldRescue.Open(w, findings))
        {
            foreach (var f in findings) Program.Info("finding: " + f);
            int listed = rescued.Dimensions.OfType<AlphaDimension>().Sum(d => d.ChunkPositions.Count);
            Program.Check(listed == count, $"the rescue lists all {count} chunk files ({listed})");
            string outDir = Path.Combine(temp, "alpha-repaired");
            var report = WorldRepair.Write(rescued, outDir);
            Print(report);
            Program.Check(report.Verified, "the repaired copy passes its checks: " + Failed(report));
            Program.Check(report.Chunks == count - 1 && report.FailedChunks == 1, $"{report.Chunks} chunks kept (expected {count - 1}), {report.FailedChunks} lost (expected 1)");
            Program.Check(!File.Exists(Path.Combine(outDir, Path.GetRelativePath(w, zeroed))) && report.Losses.Any(l => l.Lost && l.What.StartsWith("Damaged chunk")), "the emptied chunk file was left out and listed");
            using var copy = WorldLoader.Open(outDir);
            Program.Check(copy.Dimensions.OfType<AlphaDimension>().Sum(d => d.ChunkPositions.Count) == count - 1, "the repaired copy opens with the other chunks");
        }
        Program.Check(Hash(w) == before, "the damaged world itself is not changed");
    }

    static void Bedrock(string temp)
    {
        string src = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla");
        if (!Directory.Exists(src)) { Program.Info("Bedrock test world missing: skipped"); return; }
        string w = Copy(src, Path.Combine(temp, "bedrock"));
        int original;
        using (var ok = WorldLoader.Open(w)) original = ok.Dimensions.Sum(d => d.ChunkPositions.Count);
        File.Delete(Path.Combine(w, "db", "CURRENT"));
        foreach (var m in Directory.GetFiles(Path.Combine(w, "db"), "MANIFEST-*")) File.Delete(m);
        File.WriteAllBytes(Path.Combine(w, "level.dat"), [9, 0, 0, 0, 1, 2]);
        string before = Hash(w);
        bool normalFails;
        try { using var n = WorldLoader.Open(w); normalFails = false; }
        catch (Exception) { normalFails = true; }
        Program.Check(normalFails, "the normal reader cannot open it");
        var findings = new List<string>();
        using (var rescued = WorldRescue.Open(w, findings))
        {
            foreach (var f in findings) Program.Info("finding: " + f);
            int listed = rescued.Dimensions.Sum(d => d.ChunkPositions.Count);
            Program.Check(listed == original, $"rescue reads {listed} chunks of {original}");
            Program.Check(findings.Any(f => f.Contains("every data file was read directly")) && findings.Any(f => f.Contains("level.dat_old")), "the findings say what was used instead");
            var target = TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock);
            string outDir = Path.Combine(temp, "bedrock-rescued");
            var report = WorldConverter.Run(rescued, target, outDir, new ConversionOptions());
            Program.Check(report.Chunks == original && report.FailedChunks == 0 && report.Verified, $"{report.Chunks} of {original} chunks rescued into {target.Name}; verified {report.Verified}: {Failed(report)}");
            // A database opened for rescue is never written to.
            Program.Check(Throws(() => ((BedrockWorld)rescued).Db.Commit(CommitMode.Compact)), "saving the damaged world itself is refused");

            // The repaired copy: every record of the undamaged world, byte for byte.
            string repairedDir = Path.Combine(temp, "bedrock-repaired");
            var repair = WorldRepair.Write(rescued, repairedDir, "Repaired");
            Print(repair);
            Program.Check(repair.Verified, "the repaired copy passes its checks: " + Failed(repair));
            string pristinePath = Copy(src, Path.Combine(temp, "bedrock-pristine"));
            using var pristine = LevelDatabase.Open(Path.Combine(pristinePath, "db"));
            using var copyDb = LevelDatabase.Open(Path.Combine(repairedDir, "db"));
            int same = 0, differ = 0;
            foreach (var key in pristine.Keys())
            {
                var x = pristine.Get(key);
                var y = copyDb.Get(key);
                if (x != null && y != null && x.AsSpan().SequenceEqual(y)) same++; else differ++;
            }
            Program.Check(differ == 0 && copyDb.Count == pristine.Count, $"{same} records equal the undamaged world's, {differ} differ, {copyDb.Count} in the copy for {pristine.Count}");
            using var reopened = WorldLoader.Open(repairedDir);
            Program.Check(reopened.Name == "Repaired" && File.ReadAllText(Path.Combine(repairedDir, "levelname.txt")) == "Repaired", "the repaired world has its new name");
            Program.Check(reopened.Dimensions.Sum(d => d.ChunkPositions.Count) == original, "and all its chunks");
        }
        Program.Check(Hash(w) == before, "the damaged world itself is not changed");
    }

    static void BedrockWithoutLevelDat(string temp)
    {
        string src = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla");
        if (!Directory.Exists(src)) return;
        string w = Copy(src, Path.Combine(temp, "bedrock-nolevel"));
        File.Delete(Path.Combine(w, "level.dat"));
        File.Delete(Path.Combine(w, "level.dat_old"));
        var findings = new List<string>();
        using var rescued = WorldRescue.Open(w, findings);
        foreach (var f in findings) Program.Info("finding: " + f);
        var target = TargetCatalog.ClosestTo(rescued);
        Program.Info("written for " + target.Name);
        Program.Check(target.Kind == TargetKind.Bedrock && target.BedrockChunkVersion >= 39, "the chunks' format picks a 1.18+ Bedrock version");
        string outDir = Path.Combine(temp, "bedrock-nolevel-repaired");
        var repair = WorldRepair.Write(rescued, outDir);
        Print(repair);
        Program.Check(repair.Verified && repair.Notes.Any(n => n.Contains("No readable level.dat")), "the repaired copy gets a new level.dat and passes its checks: " + Failed(repair));
        using var reopened = (BedrockWorld)WorldLoader.Open(outDir);
        Program.Check(reopened.LevelRoot.GetList("lastOpenedWithVersion")?.Select(t => (int)t.AsLong).SequenceEqual(target.BedrockVersion) == true, $"its level.dat says {reopened.LastOpenedVersion}");
    }

    static void Package(string temp)
    {
        string src = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla");
        if (!Directory.Exists(src)) return;
        string package = Path.Combine(temp, "damaged.mcworld");
        ZipFile.CreateFromDirectory(src, package);
        // Damage the stored data of the biggest table inside the package.
        long dataOffset;
        using (var z = ZipFile.OpenRead(package))
        {
            var e = z.Entries.Where(x => x.FullName.EndsWith(".ldb")).OrderByDescending(x => x.CompressedLength).First();
            dataOffset = LocalDataOffset(package, e);
        }
        var bytes = File.ReadAllBytes(package);
        for (int i = 0; i < 64; i++) bytes[dataOffset + 100 + i] ^= 0x5A;
        File.WriteAllBytes(package, bytes);
        var findings = new List<string>();
        using var rescued = WorldRescue.Open(package, findings);
        foreach (var f in findings) Program.Info("finding: " + f);
        Program.Check(rescued.Dimensions.Sum(d => d.ChunkPositions.Count) > 0, "the world opens from the damaged package");
        Program.Check(findings.Any(f => f.StartsWith("Database:") || f.Contains("left out")), "the damage is reported");
        Program.Check(findings.Any(f => f.Contains(Path.Combine(temp, "unpacked"))), "it was unpacked where it was told to");
        var repair = WorldRepair.Write(rescued, Path.Combine(temp, "package-repaired"));
        Program.Check(repair.Verified, "what could be read is written as a repaired copy that passes its checks: " + Failed(repair));
    }

    static void Targets()
    {
        foreach (var (folder, expected) in new[] { ("1_12_2", "Java 1.12 – 1.12.2"), ("1_13", "Java 1.13 – 1.13.1"), ("1_16_5_dimension", "Java 1.16.2 – 1.16.5") })
        {
            string p = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", folder);
            if (!Directory.Exists(p)) continue;
            using var world = WorldLoader.Open(p);
            var t = TargetCatalog.ClosestTo(world);
            Program.Check(t.Name == expected, $"{folder} → {t.Name}");
        }
        var bedrock = TargetCatalog.All.Where(t => t.Kind == TargetKind.Bedrock).ToList();
        foreach (var level in Directory.EnumerateFiles(Path.Combine(TestPaths.Data, "amulet", "bedrock"), "level.dat", SearchOption.AllDirectories))
        {
            string p = Path.GetDirectoryName(level)!;
            if (!Directory.Exists(Path.Combine(p, "db"))) continue;
            using var world = (BedrockWorld)WorldLoader.Open(p);
            var t = TargetCatalog.ClosestTo(world);
            var opened = world.LevelRoot.GetList("lastOpenedWithVersion")?.Select(x => (int)x.AsLong).ToArray() ?? [];
            // The newest target that is not newer than the version that last opened the world.
            var expected = bedrock.First(x => Compare(x.BedrockVersion, opened) <= 0);
            Program.Check(t == expected, $"{Path.GetRelativePath(TestPaths.Data, p)} (last opened with {world.LastOpenedVersion}) → {t.Name}");
        }
    }

    static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>Where an entry's stored data starts (after its local file header).</summary>
    static long LocalDataOffset(string zipPath, ZipArchiveEntry entry)
    {
        var data = File.ReadAllBytes(zipPath);
        // Find the local header by the entry name (unique in the test package).
        var name = System.Text.Encoding.UTF8.GetBytes(entry.FullName);
        for (int i = 0; i + 30 + name.Length < data.Length; i++)
        {
            if (data[i] != 0x50 || data[i + 1] != 0x4B || data[i + 2] != 3 || data[i + 3] != 4) continue;
            int nameLen = BitConverter.ToUInt16(data, i + 26), extraLen = BitConverter.ToUInt16(data, i + 28);
            if (nameLen == name.Length && data.AsSpan(i + 30, nameLen).SequenceEqual(name)) return i + 30 + nameLen + extraLen;
        }
        throw new InvalidOperationException("entry not found");
    }

    static bool ThrowsAny(Action a)
    {
        try { a(); return false; }
        catch (InvalidOperationException e) { return e.Message.Contains("opened for rescue"); }
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (InvalidOperationException) { return true; }
    }
}
