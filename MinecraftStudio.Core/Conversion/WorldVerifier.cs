// MinecraftStudio.Core/Conversion/WorldVerifier.cs: One check of a converted world: what was checked, whether
// it passed and what was found.
// Owns: VerifyCheck, WorldVerifier.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>One check of a converted world: what was checked, whether it passed and what was found.</summary>
public sealed record VerifyCheck(string Name, bool Passed, string Detail);

/// <summary>
/// Reopens a converted world with the readers and checks that it is a valid save of the target: the files the game
/// needs, storage integrity (region sector tables and every chunk's data, the LevelDB, the console archive), that every
/// converted chunk is there and loads, and how blocks, biomes, containers, entities and the player compare with the
/// source. Differences go into <see cref="ConversionReport.Issues"/> so the user sees them.
/// </summary>
public static class WorldVerifier
{
    /// <summary>Chunks compared per dimension (spread over the converted area).</summary>
    const int SampleChunks = 2000;

    public static void Run(SaveWorld source, string outputPath, ConversionTarget target, ConversionOptions options, WorldInfo info,
        ConversionReport report, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        void Check(string name, bool ok, string detail) => report.Checks.Add(new VerifyCheck(name, ok, detail));
        progress?.Report(new ConversionProgress { Stage = "Checking: reopening the converted world", Done = 0, Total = 1 });
        SaveWorld output;
        try { output = WorldLoader.Open(outputPath); }
        catch (Exception ex)
        {
            Check("Opens as a world", false, ex.Message);
            return;
        }
        using (output)
        {
            Check("Opens as a world", ExpectedKind(output, target), output.FormatDescription);
            Files(output, target, Check);
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new ConversionProgress { Stage = "Checking: storage integrity", Done = 0, Total = 1 });
            Storage(output, Check, cancel);
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new ConversionProgress { Stage = "Checking: chunks, blocks, containers and entities", Done = 0, Total = 1 });
            Content(source, output, target, options, info, report, Check, progress, cancel);
            Players(info, output, report, Check);
        }
    }

    static bool ExpectedKind(SaveWorld w, ConversionTarget t) => t.Kind switch
    {
        TargetKind.Java => w is JavaWorld,
        TargetKind.Bedrock when t.BedrockTerrain == BedrockTerrain.LegacyTerrain => w is BedrockWorld or PocketLegacyWorld,
        TargetKind.Bedrock => w is BedrockWorld,
        TargetKind.LegacyConsole => w is ConsoleWorld,
        TargetKind.PocketLegacy => w is PocketLegacyWorld,
        _ => w is FiniteWorld,
    };

    // ---------------------------------------------------------------- files the game needs

    static void Files(SaveWorld w, ConversionTarget t, Action<string, bool, string> check)
    {
        switch (w)
        {
            case JavaWorld j:
                {
                    var data = j.Data;
                    bool ok = data != null;
                    string detail = ok ? $"level.dat for \"{data!.GetString("LevelName")}\"" : "level.dat is missing or unreadable";
                    if (ok && t.DataVersion > 0)
                    {
                        int dv = data!.GetInt("DataVersion");
                        // Worlds for 1.20.5+ get the 1.20.4 level.dat layout on purpose: the game upgrades it.
                        ok &= dv == JavaLevelData.LevelDataVersion(t);
                        detail += $", data version {dv}";
                    }
                    int regions = j.Dimensions.OfType<JavaRegionDimension>().Sum(d => RegionFiles(d).Count());
                    check("World files", ok && (regions > 0 || t.JavaFormat == JavaFormat.Alpha), $"{detail}; {regions} region files");
                    break;
                }
            case BedrockWorld b:
                {
                    string db = Path.Combine(b.Folder, "db");
                    bool ok = File.Exists(Path.Combine(b.Folder, "level.dat")) && File.Exists(Path.Combine(b.Folder, "levelname.txt"))
                              && File.Exists(Path.Combine(db, "CURRENT")) && Directory.EnumerateFiles(db, "MANIFEST-*").Any();
                    int header = b.Level.HeaderVersion ?? 0;
                    var opened = b.LevelRoot.GetList("lastOpenedWithVersion");
                    string version = opened == null ? "?" : string.Join(".", opened.Select(x => x.AsLong));
                    check("World files", ok && header == t.BedrockStorageVersion,
                        $"level.dat (storage version {header}), levelname.txt, db/CURRENT and the manifest; made for {version}");
                    break;
                }
            case ConsoleWorld c:
                check("World files", c.Archive.Find("level.dat") != null, $"{Path.GetFileName(c.FilePath)}: {c.Archive.Files.Count} files in the save archive");
                break;
            case PocketLegacyWorld p:
                check("World files", File.Exists(Path.Combine(p.Folder, "level.dat")), "level.dat and the chunk data");
                break;
            default:
                check("World files", File.Exists(w.Location), Path.GetFileName(w.Location));
                break;
        }
    }

    static IEnumerable<string> RegionFiles(JavaRegionDimension d) =>
        Directory.Exists(d.RegionFolder) ? Directory.EnumerateFiles(d.RegionFolder, "*" + d.Extension) : [];

    // ---------------------------------------------------------------- storage integrity

    static void Storage(SaveWorld w, Action<string, bool, string> check, CancellationToken cancel)
    {
        switch (w)
        {
            case JavaWorld j:
                {
                    int files = 0, problems = 0;
                    var first = new List<string>();
                    foreach (var d in j.Dimensions.OfType<JavaRegionDimension>())
                        foreach (var f in RegionFiles(d))
                        {
                            cancel.ThrowIfCancellationRequested();
                            files++;
                            foreach (var issue in RegionFile.Open(f).Validate(true))
                            {
                                problems++;
                                if (first.Count < 3) first.Add($"{Path.GetFileName(f)} slot {issue.Index}: {issue.Message}");
                            }
                        }
                    check("Region files", problems == 0, problems == 0
                        ? $"{files} region files: sector tables are consistent and every chunk decompresses and parses"
                        : $"{problems} problems: {string.Join("; ", first)}");
                    break;
                }
            case BedrockWorld b:
                {
                    bool ok = b.Db.Warnings.Count == 0 && b.Db.CorruptLogFragments == 0;
                    check("LevelDB database", ok, ok
                        ? $"{b.Db.Count:N0} records; table checksums and the manifest are valid"
                        : string.Join("; ", b.Db.Warnings.Take(3)));
                    break;
                }
            case ConsoleWorld c:
                check("Console save archive", c.Warnings.Count == 0, c.Warnings.Count == 0 ? "the archive and its region files read back without errors" : string.Join("; ", c.Warnings.Take(3)));
                break;
        }
    }

    // ---------------------------------------------------------------- chunks, blocks, biomes, containers, entities

    static void Content(SaveWorld source, SaveWorld output, ConversionTarget target, ConversionOptions options, WorldInfo info,
        ConversionReport report, Action<string, bool, string> check, IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        int outChunks = output.Dimensions.Sum(d => d.ChunkPositions.Count);
        bool finiteTarget = target.Kind is TargetKind.PocketLegacy or TargetKind.Classic or TargetKind.Indev;
        // Finite targets always hold their whole area (missing chunks become air), so only other targets count chunks.
        check("Every converted chunk is there", finiteTarget || outChunks >= report.Chunks,
            $"{report.Chunks:N0} chunks converted, {outChunks:N0} in the converted world" + (report.FailedChunks > 0 ? $"; {report.FailedChunks} damaged source chunks could not be read" : ""));

        long compared = 0, same = 0;
        int loadErrors = 0, missing = 0, sampled = 0, containers = 0, containerProblems = 0;
        var changes = new Dictionary<string, long>(StringComparer.Ordinal);
        var biomeChanges = new Dictionary<string, long>(StringComparer.Ordinal);
        var srcEntities = new Dictionary<string, int>(StringComparer.Ordinal);
        var outEntities = new Dictionary<string, int>(StringComparer.Ordinal);
        var rescueChunks = report.RescueChests.Select(r => (r.X >> 4, r.Z >> 4)).ToHashSet();
        int toCompare = 0, comparedChunks = 0;
        bool entitiesKept = options.ConvertEntities && target.Kind is not (TargetKind.Classic or TargetKind.Indev or TargetKind.PocketLegacy);
        foreach (var dim in source.Dimensions)
        {
            string id = CanonicalReader.DimensionId(dim);
            if (options.Dimensions != null && !options.Dimensions.Contains(id)) continue;
            if (!WorldConverter.SupportsDimension(target, id)) continue;
            // Java writes mod dimensions under valid names (DIM7 → minecraft:dim7).
            string outId = target.Kind == TargetKind.Java ? JavaWorldWriter.OutputDimensionId(id) : id;
            var outDim = output.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == outId);
            var (offset, half, yShift) = Window(target, options, info, id);
            var positions = dim.ChunkPositions
                .Where(p => options.Area is not var (a, b, c, d) || (p.X >= a && p.X <= c && p.Z >= b && p.Z <= d))
                .Where(p => finiteTarget ? p.X - offset.X is >= 0 and <= 15 && p.Z - offset.Z is >= 0 and <= 15
                                         : Math.Abs((long)p.X - offset.X) <= half - 1 && Math.Abs((long)p.Z - offset.Z) <= half - 1)
                .Where(p => !rescueChunks.Contains((p.X, p.Z)))
                .OrderBy(p => (p.X * 7919 + p.Z * 104729) & 0xFFFF).Take(SampleChunks).ToList();
            if (positions.Count == 0) continue;
            if (outDim == null) { missing += positions.Count; continue; }
            toCompare += positions.Count;
            var srcReader = new CanonicalReader(source, dim);
            var outReader = new CanonicalReader(output, outDim);
            // The user's block rules changed some blocks on purpose: the source is compared as they made it.
            var blockMap = options.BlockRules is { Count: > 0 } rules ? Structures.BlockReplace.Compile(rules) : null;
            foreach (var pos in positions)
            {
                cancel.ThrowIfCancellationRequested();
                if ((++comparedChunks & 15) == 0)
                    progress?.Report(new ConversionProgress { Stage = "Checking: comparing chunks with the original", Done = comparedChunks, Total = Math.Max(toCompare, comparedChunks) });
                CanonicalChunk? s, o;
                try { s = srcReader.Get(pos); } catch { continue; }
                if (s == null) continue;
                try { o = outReader.Get(new ChunkPos(pos.X - offset.X, pos.Z - offset.Z)); }
                catch { loadErrors++; continue; }
                if (o == null) { if (!s.IsEmpty) missing++; continue; }
                sampled++;
                if (entitiesKept)
                {
                    foreach (var e in s.Entities) Count(srcEntities, Entities.CanonicalId(e.GetString("id") ?? "?"));
                    foreach (var e in o.Entities) Count(outEntities, Entities.CanonicalId(e.GetString("id") ?? "?"));
                }
                if (s.NeedsShapeUpdate) ShapeFixer.Fix(s, srcReader, walls: dim is not BedrockDimension);
                if (o.NeedsShapeUpdate) ShapeFixer.Fix(o, outReader, walls: outDim is not BedrockDimension);
                if (s.Sections.Count == 0) continue;
                var (minY, maxY) = HeightWindow(target, s, yShift, id);
                for (int y = minY; y <= maxY; y++)
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                        {
                            var a0 = s.GetBlock(x, y, z);
                            var a = blockMap?.Invoke(a0) ?? a0;
                            var b = o.GetBlock(x, y - yShift, z);
                            if (a.IsAir && b.IsAir) continue;
                            compared++;
                            if (a.Name == b.Name) same++;
                            else Count(changes, $"{a.BaseName} → {b.BaseName}");
                        }
                if (ColumnBiomes(target))
                {
                    // One biome per column: the one at the surface (caves underground cannot be kept).
                    var heights = s.Heights();
                    for (int z = 0; z < 16; z += 4)
                        for (int x = 0; x < 16; x += 4)
                        {
                            string a = s.SurfaceBiome(x, z, heights), b = o.BiomeAt(x, 64, z);
                            if (a != b) Count(biomeChanges, $"{Bare(a)} → {Bare(b)}");
                        }
                }
                else if (target.Kind is not (TargetKind.Classic or TargetKind.Indev))
                    for (int y = (minY + 3) & ~3; y <= maxY; y += 4)
                        for (int z = 0; z < 16; z += 4)
                            for (int x = 0; x < 16; x += 4)
                            {
                                string a = s.BiomeAt(x, y, z), b = o.BiomeAt(x, y - yShift, z);
                                if (a != b) Count(biomeChanges, $"{Bare(a)} → {Bare(b)}");
                            }
                if (!options.ConvertBlockEntities) continue;
                // Containers that exist in both worlds keep every stack (a stack the version lacks becomes a placeholder).
                int dx = offset.X * 16, dz = offset.Z * 16;
                var outBe = o.BlockEntities.GroupBy(be => (be.GetInt("x"), be.GetInt("y"), be.GetInt("z"))).ToDictionary(g => g.Key, g => g.First());
                foreach (var be in s.BlockEntities)
                {
                    int stacks = be.GetList("Items")?.OfType<NbtCompound>().Count(i => !ConversionLog.IsEmptyItem(i)) ?? 0;
                    if (stacks == 0) continue;
                    if (!outBe.TryGetValue((be.GetInt("x") - dx, be.GetInt("y") - yShift, be.GetInt("z") - dz), out var ob)) continue;
                    containers++;
                    int kept = ob.GetList("Items")?.OfType<NbtCompound>().Count(i => !ConversionLog.IsEmptyItem(i)) ?? 0;
                    if (kept < stacks)
                    {
                        containerProblems++;
                        if (containerProblems <= 10)
                            report.Issues.Add($"Container at {be.GetInt("x")}, {be.GetInt("y")}, {be.GetInt("z")}: {stacks - kept} of {stacks} item stacks did not arrive");
                    }
                }
            }
        }
        check("Chunks load", loadErrors == 0 && missing == 0,
            $"{sampled:N0} sampled chunks read back" + (loadErrors > 0 ? $"; {loadErrors} failed to load" : "") + (missing > 0 ? $"; {missing} are missing" : ""));
        double pct = compared == 0 ? 100 : same * 100.0 / compared;
        check("Blocks", true, $"{pct:0.###}% of {compared:N0} compared blocks are the same block; the rest became the closest block the version has");
        foreach (var (change, n) in changes.OrderByDescending(k => k.Value).Take(12))
            report.Issues.Add($"Block changed: {change} ({n:N0} in the sampled chunks)");
        foreach (var (change, n) in biomeChanges.OrderByDescending(k => k.Value).Take(8))
            report.Issues.Add($"Biome changed: {change} ({n:N0} areas in the sampled chunks)");
        if (containers > 0 || containerProblems > 0)
            check("Containers", containerProblems == 0, containerProblems == 0
                ? $"{containers:N0} containers in the sampled chunks kept every item stack"
                : $"{containerProblems} of {containers} sampled containers lost item stacks");
        // Entities: a type that did not arrive must be explained by the conversion log (a type the version lacks).
        var lost = srcEntities.Where(kv => outEntities.GetValueOrDefault(kv.Key) < kv.Value)
            .Select(kv => (Type: kv.Key, Lost: kv.Value - outEntities.GetValueOrDefault(kv.Key))).ToList();
        bool Explained(string type) => report.Losses.Any(l => l.Kind == LossKind.Entity && l.What.StartsWith(ConversionLog.Friendly(type), StringComparison.Ordinal));
        int srcTotal = srcEntities.Values.Sum(), outTotal = outEntities.Values.Sum();
        if (srcTotal > 0)
            check("Entities", lost.All(l => Explained(l.Type)), $"{srcTotal:N0} entities in the sampled source chunks, {outTotal:N0} in the converted world"
                + (lost.Count > 0 && lost.All(l => Explained(l.Type)) ? "; the missing ones are types the version does not have (listed in the report)" : ""));
        foreach (var (type, n) in lost.Where(l => !Explained(l.Type)).OrderByDescending(l => l.Lost).Take(12))
            report.Issues.Add($"Entity did not arrive: {n} × {ConversionLog.Friendly(type)} (in the sampled chunks)");
    }

    /// <summary>Targets that keep one biome per column (Java before 1.15, Bedrock before 1.18, consoles, Pocket Edition).</summary>
    static bool ColumnBiomes(ConversionTarget t) => t.Kind switch
    {
        TargetKind.Java => t.DataVersion < 2203,
        TargetKind.Bedrock => t.BedrockChunkVersion < 39,
        TargetKind.LegacyConsole or TargetKind.PocketLegacy => true,
        _ => false,
    };

    static string Bare(string id) => id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;
    static void Count(Dictionary<string, int> d, string key) => d[key] = d.GetValueOrDefault(key) + 1;
    static void Count(Dictionary<string, long> d, string key) => d[key] = d.GetValueOrDefault(key) + 1;

    /// <summary>Where source chunks land in the output: console worlds and finite levels are centred on the spawn.</summary>
    static ((int X, int Z) Offset, int Half, int YShift) Window(ConversionTarget target, ConversionOptions options, WorldInfo info, string dimension)
    {
        int cx = (options.FiniteCenterX ?? info.SpawnX) >> 4, cz = (options.FiniteCenterZ ?? info.SpawnZ) >> 4;
        int shift = dimension == "minecraft:overworld" && VerticalShift.Applies(target) ? options.VerticalShift : 0;
        return target.Kind switch
        {
            TargetKind.LegacyConsole => (dimension switch { "minecraft:the_nether" => (cx / 8, cz / 8), "minecraft:the_end" => (0, 0), _ => (cx, cz) },
                dimension == "minecraft:overworld" ? Math.Clamp(options.ConsoleWorldChunks, 16, 320) / 2 : 9, 0),
            TargetKind.PocketLegacy or TargetKind.Indev => ((cx - 8, cz - 8), int.MaxValue, 0),
            TargetKind.Classic => ((cx - 8, cz - 8), int.MaxValue, 31),
            _ => ((0, 0), int.MaxValue, -shift),
        };
    }

    /// <summary>The heights the target holds, in source coordinates (the bottom layer may have become bedrock).</summary>
    static (int Min, int Max) HeightWindow(ConversionTarget target, CanonicalChunk s, int yShift, string dimension)
    {
        int minY = s.Sections.Keys.Min() * 16, maxY = s.Sections.Keys.Max() * 16 + 15;
        var (low, top) = WorldConverter.TargetHeight(target, dimension);
        if (low < 0) return (minY, maxY);
        // Output y 0…top holds source y yShift…yShift + top; its bottom layer may be the new bedrock floor.
        return (Math.Max(minY, yShift + 1), Math.Min(maxY, yShift + top));
    }

    // ---------------------------------------------------------------- players

    static void Players(WorldInfo info, SaveWorld output, ConversionReport report, Action<string, bool, string> check)
    {
        if (info.Players.Count == 0 || !report.HostWritten) return;
        var outInfo = WorldInfoReader.Read(output);
        var host = outInfo.Players.FirstOrDefault();
        int before = Stacks(info.Players[0].Data), after = host == null ? 0 : Stacks(host.Data);
        check("Your player", host != null && after >= before,
            host == null ? "the player is missing from the converted world"
                : $"inventory, armour and ender chest: {before} stacks to keep, {after} in the converted world"
                  + (report.RescueChests.Any(r => r.Label.StartsWith("Your items", StringComparison.Ordinal)) ? " (the rest is in the rescue chest at the spawn)" : ""));
    }

    static int Stacks(NbtCompound player) =>
        (player.GetList("Inventory")?.OfType<NbtCompound>().Count(i => !ConversionLog.IsEmptyItem(i)) ?? 0)
        + (player.GetList("EnderItems")?.OfType<NbtCompound>().Count(i => !ConversionLog.IsEmptyItem(i)) ?? 0);
}
