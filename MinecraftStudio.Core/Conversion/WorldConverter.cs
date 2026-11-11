// MinecraftStudio.Core/Conversion/WorldConverter.cs: Converts any supported world to any supported target.
// Owns: WorldConverter.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>Converts any supported world to any supported target.</summary>
public static class WorldConverter
{
    public static IWorldWriter CreateWriter(ConversionTarget target, string output, ConversionOptions options) => target.Kind switch
    {
        TargetKind.Java => new JavaWorldWriter(output, target, options),
        TargetKind.Bedrock => new BedrockWorldWriter(output, target, options),
        TargetKind.LegacyConsole => new ConsoleWorldWriter(output, target, options),
        TargetKind.PocketLegacy => new PocketWorldWriter(output, target, options),
        TargetKind.Classic or TargetKind.Indev => new FiniteWorldWriter(output, target, options),
        _ => throw new NotSupportedException($"Converting to {target.Name} is not available yet."),
    };

    /// <summary>Whether a target can hold a dimension (canonical id), without creating a writer.</summary>
    public static bool SupportsDimension(ConversionTarget target, string dimensionId) => target.Kind switch
    {
        TargetKind.Java => JavaWorldWriter.Supports(target, dimensionId),
        TargetKind.Bedrock => BedrockWorldWriter.Supports(target, dimensionId),
        TargetKind.LegacyConsole => dimensionId is "minecraft:overworld" or "minecraft:the_nether" or "minecraft:the_end",
        _ => dimensionId == "minecraft:overworld",
    };

    /// <summary>Lowest and highest block y a target stores in a dimension (target coordinates).</summary>
    public static (int Min, int Max) TargetHeight(ConversionTarget t, string dimension) => t.Kind switch
    {
        TargetKind.Java when t.JavaFormat == JavaFormat.AnvilContainers => dimension == "minecraft:overworld" ? (-64, 319) : (0, 255),
        TargetKind.Java when t.JavaFormat is JavaFormat.Alpha or JavaFormat.McRegion => (0, 127),
        TargetKind.Java => (0, 255),
        TargetKind.Bedrock when t.BedrockTerrain == BedrockTerrain.LegacyTerrain => (0, 127),
        TargetKind.Bedrock when t.BedrockChunkVersion >= 39 => dimension switch { "minecraft:overworld" => (-64, 319), "minecraft:the_nether" => (0, 127), _ => (0, 255) },
        TargetKind.Bedrock => (0, 255),
        TargetKind.LegacyConsole => (0, 255),
        TargetKind.Classic => (0, 63),
        _ => (0, 127),
    };

    /// <summary>Targets that can hold the rescue chests (Classic and Indev levels are written with blocks only).</summary>
    /// <summary>
    /// Whether chunks a Java world had not finished making are written as they are. Java and Bedrock make a missing chunk
    /// themselves, so a conversion leaves those out (written as finished, they were holes and blank chunks — a Bedrock world
    /// full of those would not open). A fixed-size target (Pocket 0.x, Classic, Indev) can't make one, and a console save
    /// is fixed-size too and not proven to: they take them as they are, as does an edit into a copy of the world itself.
    /// </summary>
    public static bool KeepsUnfinished(ConversionTarget target, ConversionOptions options) =>
        options.KeepUnfinishedChunks || target.Kind is TargetKind.LegacyConsole or TargetKind.PocketLegacy or TargetKind.Classic or TargetKind.Indev;

    public static bool CanRescue(ConversionTarget t) => t.Kind is TargetKind.Java or TargetKind.Bedrock or TargetKind.LegacyConsole or TargetKind.PocketLegacy;

    /// <summary>
    /// Converts a world. The source is only read; the output goes to an empty folder. Everything the target cannot hold
    /// unchanged is reported in <see cref="ConversionReport.Losses"/>, items that would be lost go into rescue chests at
    /// the spawn, and the converted world is reopened and checked (<see cref="ConversionReport.Checks"/>).
    /// </summary>
    public static ConversionReport Run(SaveWorld source, ConversionTarget target, string output, ConversionOptions options,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new ConversionReport();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException($"The output folder is not empty: {output}");
        // Huge worlds: refuse up front if the drive plainly can't hold the copy, and keep the PC awake for a long run.
        // Only for whole-world conversions — an area/radius-limited one writes far less than the source.
        if (options.Area == null) LongRun.EnsureRoom(source.Location, output);
        using var awake = LongRun.KeepAwake();
        var scope = ConversionLog.Start(target, out var log);
        WorldInfo info;
        try
        {
            info = Convert(source, target, output, options, report, log, progress, cancel);
        }
        finally { scope.Dispose(); }
        report.Losses.AddRange(log.Entries);
        if (options.Verify)
        {
            try { WorldVerifier.Run(source, report.OpenPath ?? output, target, options, info, report, progress, cancel); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { report.Checks.Add(new VerifyCheck("Verification", false, "the check could not finish: " + ex.Message)); }
        }
        report.Elapsed = sw.Elapsed;
        return report;
    }

    static WorldInfo Convert(SaveWorld source, ConversionTarget target, string output, ConversionOptions options, ConversionReport report,
        ConversionLog log, IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        // Land from a heightmap is made for the target's heights, so nothing below or above them has to be cut away.
        if (source is HeightmapWorld land && land.FitTo(target) is string fitted) report.Notes.Add(fitted);
        var info = WorldInfoReader.Read(source);
        if (!string.IsNullOrWhiteSpace(options.WorldName)) info.Name = options.WorldName!;
        // The user's block rules, checked before anything is written (an unknown block name stops here).
        var blockMap = options.BlockRules is { Count: > 0 } rules ? Structures.BlockReplace.Compile(rules) : null;
        var ruled = new Dictionary<(string From, string To), long>();
        info.Maps.AddRange(Maps.Read(source));
        // The overworld last, and its rescue chunk last of all, so everything rescued on the way goes into its chests.
        var dims = source.Dimensions.Where(d => options.Dimensions == null || options.Dimensions.Contains(CanonicalReader.DimensionId(d)))
            .OrderBy(d => CanonicalReader.DimensionId(d) == "minecraft:overworld" ? 1 : 0).ToList();
        log.AcceptsRescues = CanRescue(target) && dims.Any(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
        log.SourceIsBedrock = source is BedrockWorld;
        log.KeepAddOns = source is BedrockWorld && target.Kind == TargetKind.Bedrock && target.BedrockTerrain == BedrockTerrain.States;
        PlayerPlan.Prepare(source, target, info, options, report);
        int shift = options.VerticalShift != 0 && VerticalShift.Applies(target) ? options.VerticalShift : 0;
        if (shift != 0)
        {
            VerticalShift.Apply(info, shift);
            log.Add(LossKind.World, "Height", $"the overworld was moved {(shift > 0 ? "up" : "down")} {Math.Abs(shift)} blocks to fit {target.Name}: y {-shift} becomes y 0");
        }
        bool rescuePlaced = false;
        // What players rode, set down in the world where it stands (PlayerPlan): added to its chunk as that is read.
        var loose = LooseByChunk(info);
        using (var writer = CreateWriter(target, output, options))
        {
            writer.Begin(info);
            // Finite targets (console worlds, Pocket 0.x, Classic) read only the part of each dimension they can hold.
            var areas = dims.ToDictionary(d => d, d =>
            {
                var o = options;
                if (writer is IAreaLimitedWriter limited && limited.SourceArea(CanonicalReader.DimensionId(d), info) is var (a, b, c2, d2))
                {
                    var (minX, minZ, maxX, maxZ) = options.Area ?? (int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
                    o = new ConversionOptions { Area = (Math.Max(a, minX), Math.Max(b, minZ), Math.Min(c2, maxX), Math.Min(d2, maxZ)) };
                }
                return o;
            });
            int total = dims.Sum(d => Filter(d.ChunkPositions, areas[d]).Count()), done = 0;
            foreach (var dim in dims)
            {
                string dimId = CanonicalReader.DimensionId(dim);
                var dimOptions = areas[dim];
                var positions = Filter(dim.ChunkPositions, dimOptions).ToList();
                if (!writer.SupportsDimension(dimId))
                {
                    report.Notes.Add($"{dim.DisplayName} was skipped: {target.Name} has no such dimension.");
                    if (positions.Count > 0) log.Add(LossKind.World, dim.DisplayName, $"not converted: {target.Name} has no such dimension ({positions.Count:N0} chunks)", positions.Count, lost: true);
                    done += positions.Count;
                    continue;
                }
                // Finite targets keep a fixed area; a radius limit is the user's choice. Say what stays behind either way.
                int outside = Filter(dim.ChunkPositions, options).Count() - positions.Count;
                if (outside > 0)
                    log.Add(LossKind.World, dim.DisplayName, $"{outside:N0} chunks outside the area {target.Name} can hold were not converted", outside, lost: true);
                if (writer is IAreaLimitedWriter lw) lw.SourceArea(dimId, info);
                writer.BeginDimension(dimId);
                var reader = new CanonicalReader(source, dim);
                bool keepWalls = dim is BedrockDimension;
                var fixedShapes = new HashSet<ChunkPos>();
                bool overworld = dimId == "minecraft:overworld";
                var ordered = Ordered(positions).ToList();
                ChunkPos? rescueAt = overworld && log.AcceptsRescues ? RescueChests.PickChunk(positions, info) : null;
                if (rescueAt is ChunkPos ra) { ordered.Remove(ra); ordered.Add(ra); }
                bool keepUnfinished = KeepsUnfinished(target, options);
                int unfinished = 0;
                foreach (var pos in ordered)
                {
                    cancel.ThrowIfCancellationRequested();
                    log.Where = $"{dim.DisplayName}, chunk {pos.X}, {pos.Z} (x {pos.X * 16}…{pos.X * 16 + 15}, z {pos.Z * 16}…{pos.Z * 16 + 15})";
                    var c = reader.Get(pos);
                    if (c == null)
                    {
                        report.FailedChunks++; done++;
                        log.Add(LossKind.Block, "Damaged chunk", "could not be read in the source and was not converted", lost: true);
                        continue;
                    }
                    if (c.Unfinished && !keepUnfinished && pos != rescueAt && c.Entities.Count == 0 && !loose.ContainsKey((dimId, pos)))
                    {
                        unfinished++; done++;
                        continue;
                    }
                    if (loose.Remove((dimId, pos), out var setDown)) c.Entities.AddRange(setDown);
                    if (c.NeedsShapeUpdate && fixedShapes.Add(pos)) ShapeFixer.Fix(c, reader, walls: !keepWalls);
                    if (overworld && shift != 0) VerticalShift.Apply(c, shift);
                    CutHeight(c, target, dimId, log, overworld ? shift : 0);
                    if (pos == rescueAt)
                    {
                        PlaceRescue(c, log, info, target, report);
                        rescuePlaced = true;
                    }
                    if (blockMap != null) MapBlocks(c, blockMap, ruled);
                    options.StampChunk?.Invoke(c, dimId);
                    writer.WriteChunk(c);
                    report.Chunks++;
                    if ((++done & 31) == 0) progress?.Report(new ConversionProgress { Stage = $"Converting {dim.DisplayName}", Done = done, Total = total });
                }
                writer.EndDimension();
                if (unfinished > 0)
                    report.Notes.Add($"{dim.DisplayName}: {unfinished:N0} chunks the game had not finished making were left for {target.Name} to make (nobody has been in them).");
                foreach (var w in reader.Warnings.Take(50)) report.Warnings.Add($"{dim.DisplayName}: {w}");
                if (reader.Warnings.Count > 50) report.Warnings.Add($"{dim.DisplayName}: {reader.Warnings.Count - 50} more warnings.");
            }
            log.AcceptsRescues = false;
            foreach (var ((dimension, at), set) in loose)
                foreach (var e in set)
                    log.Add(LossKind.Entity, ConversionLog.Friendly(e.GetString("id") ?? "mount"),
                        $"lost: a player was riding it in a chunk that was not converted ({dimension}, chunk {at.X}, {at.Z})", lost: true, where: "");
            log.Where = "players and world data";
            progress?.Report(new ConversionProgress { Stage = "Finishing", Done = total, Total = total });
            writer.Finish(info, new Progress<string>(s => progress?.Report(new ConversionProgress { Stage = s, Done = total, Total = total })));
            report.Notes.AddRange(writer.Notes);
            report.OpenPath = writer is FiniteWorldWriter f && f.PrimaryFile != null ? f.PrimaryFile : output;
        }
        if (log.KeepAddOns && source is BedrockWorld bedrockSource) CopyPacks(bedrockSource.Folder, output, report);
        // Bedrock to Bedrock: the world's picture in the worlds list.
        if (source is BedrockWorld fromBedrock && target.Kind == TargetKind.Bedrock && File.Exists(Path.Combine(fromBedrock.Folder, "world_icon.jpeg"))
            && Directory.Exists(output) && !File.Exists(Path.Combine(output, "world_icon.jpeg")))
            File.Copy(Path.Combine(fromBedrock.Folder, "world_icon.jpeg"), Path.Combine(output, "world_icon.jpeg"));
        // Java to Java: advancements, statistics, scoreboard, data packs, the icon and level.dat's border and the rest.
        if (source is JavaWorld javaSource) JavaWorldFiles.Copy(javaSource, output, target, options, log);
        if (!rescuePlaced)
            foreach (var g in log.Rescued)
                log.Add(LossKind.World, g.Label, CanRescue(target)
                    ? "lost: no overworld chunk was converted to put a rescue chest in"
                    : $"lost: {target.Name} levels have no chests to keep them in", g.Items.Count, lost: true, where: "");
        // What the user's block rules changed, most first.
        foreach (var ((from, to), n) in ruled.OrderByDescending(kv => kv.Value).Take(12))
            report.Notes.Add($"Your block rules changed {n:N0} {from.Replace('_', ' ')} to {to.Replace('_', ' ')}.");
        return info;
    }

    /// <summary>The loose entities (what players rode, PlayerPlan) by dimension and the chunk they stand in.</summary>
    static Dictionary<(string Dimension, ChunkPos At), List<NbtCompound>> LooseByChunk(WorldInfo info)
    {
        var byChunk = new Dictionary<(string, ChunkPos), List<NbtCompound>>();
        foreach (var (dimension, e) in info.LooseEntities)
        {
            double Axis(int i) => e.GetList("Pos") is { Count: 3 } p && double.IsFinite(p[i].AsDouble) ? Math.Clamp(p[i].AsDouble, -3.0e7, 3.0e7) : 0;
            var at = new ChunkPos((int)Math.Floor(Axis(0) / 16), (int)Math.Floor(Axis(2) / 16));
            if (!byChunk.TryGetValue((dimension, at), out var list)) byChunk[(dimension, at)] = list = [];
            list.Add(e);
        }
        return byChunk;
    }

    /// <summary>
    /// Block entities and entities above or below the heights the target stores: their items go to the rescue chests and
    /// they are reported; the blocks there are cut by the writer, which is reported once per chunk.
    /// </summary>
    /// <summary>
    /// The user's block rules applied to a chunk: each section's palette entries replaced (indices stay), the block
    /// entities of changed blocks taken out (a chest made stone keeps no items), the changes counted.
    /// </summary>
    static void MapBlocks(CanonicalChunk c, Func<BlockState, BlockState?> map, Dictionary<(string, string), long> counts)
    {
        var gone = new HashSet<(int, int, int)>();
        foreach (var (sy, section) in c.Sections)
        {
            var palette = section.Palette;
            BlockState[]? changed = null;
            for (int i = 0; i < palette.Length; i++)
                if (map(palette[i]) is { } to) (changed ??= (BlockState[])palette.Clone())[i] = to;
            if (changed == null) continue;
            for (int y = sy * 16; y < sy * 16 + 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        var before = c.GetBlock(x, y, z);
                        if (map(before) is not { } after) continue;
                        lock (counts) counts[(before.BaseName, after.BaseName)] = counts.GetValueOrDefault((before.BaseName, after.BaseName)) + 1;
                        if (before.BaseName != after.BaseName) gone.Add((c.X * 16 + x, y, c.Z * 16 + z));
                    }
            section.Palette = changed;
        }
        if (gone.Count > 0) c.BlockEntities.RemoveAll(be => gone.Contains((be.GetInt("x"), be.GetInt("y"), be.GetInt("z"))));
    }

    static void CutHeight(CanonicalChunk c, ConversionTarget target, string dimension, ConversionLog log, int shift)
    {
        if (target.Kind is TargetKind.Classic or TargetKind.Indev) return; // these keep a fixed block window only
        var (min, max) = TargetHeight(target, dimension);
        string range = $"{target.Name} stores y {min} to {max}";
        foreach (var be in c.BlockEntities.ToList())
        {
            int y = be.GetInt("y");
            if (y >= min && y <= max) continue;
            c.BlockEntities.Remove(be);
            BlockEntities.Drop(be, $"outside the heights {range}");
        }
        foreach (var e in c.Entities.ToList())
        {
            if (Entities.Position(e) is not var (_, y, _) || y >= min && y < max + 1) continue;
            c.Entities.Remove(e);
            Entities.Drop(e, $"outside the heights {range}");
        }
        if (c.Sections.Any(kv => (kv.Key * 16 + 15 < min || kv.Key * 16 > max) && !kv.Value.IsEmpty))
            log.Add(LossKind.Block, "Blocks outside the height range", $"cut: {range}"
                + (shift != 0 ? $" (the world was raised {shift} blocks, so the tops above y {max - shift} were cut)"
                    : min == 0 && dimension == "minecraft:overworld" && VerticalShift.Applies(target) ? " (choose “Raise the world 64 blocks” to keep the deep caves instead of the top)" : "")
                + "; counted in chunks", 1, lost: true);
    }

    /// <summary>A Bedrock world's add-on packs (behaviour and resource packs and the lists of active packs) go with it.</summary>
    static void CopyPacks(string from, string to, ConversionReport report)
    {
        int packs = 0;
        foreach (var dir in new[] { "behavior_packs", "resource_packs" })
        {
            string src = Path.Combine(from, dir);
            if (!Directory.Exists(src)) continue;
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(to, dir, Path.GetRelativePath(src, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: false);
            }
            packs += Directory.GetDirectories(src).Length;
        }
        foreach (var file in new[] { "world_behavior_packs.json", "world_resource_packs.json", "world_behavior_pack_history.json", "world_resource_pack_history.json" })
            if (File.Exists(Path.Combine(from, file))) File.Copy(Path.Combine(from, file), Path.Combine(to, file), overwrite: false);
        if (packs > 0) report.Notes.Add($"Copied the world's {packs} add-on pack{(packs == 1 ? "" : "s")}; add-on blocks, items and mobs were kept as they are.");
    }

    /// <summary>Puts everything rescued so far into chests on the surface of the rescue chunk.</summary>
    static void PlaceRescue(CanonicalChunk c, ConversionLog log, WorldInfo info, ConversionTarget target, ConversionReport report)
    {
        var groups = log.Rescued;
        log.AcceptsRescues = false;
        if (groups.Count == 0) return;
        var (min, max) = TargetHeight(target, "minecraft:overworld");
        var (placed, unplaced) = RescueChests.Place(c, groups, info.SpawnX, info.SpawnZ, min, max);
        report.RescueChests.AddRange(placed);
        int stacks = groups.Sum(g => g.Items.Count);
        if (placed.Count > 0)
            log.Add(LossKind.World, "Rescue chests", $"{placed.Count} named chest{(placed.Count == 1 ? "" : "s")} near the spawn hold {stacks - unplaced} item stacks that had nowhere else to go",
                placed.Count, where: $"{placed[0].X}, {placed[0].Y}, {placed[0].Z}");
        if (unplaced > 0) log.Add(LossKind.World, "Rescued items", "lost: no room left for more chests near the spawn", unplaced, lost: true);
    }

    static IEnumerable<ChunkPos> Filter(IEnumerable<ChunkPos> all, ConversionOptions o) =>
        o.Area is var (minX, minZ, maxX, maxZ) ? all.Where(p => p.X >= minX && p.X <= maxX && p.Z >= minZ && p.Z <= maxZ) : all;

    /// <summary>Region by region, rows inside a region: keeps neighbours in the reader's cache and one region file open.</summary>
    static IEnumerable<ChunkPos> Ordered(IEnumerable<ChunkPos> all) =>
        all.OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5).ThenBy(p => p.Z).ThenBy(p => p.X);
}
