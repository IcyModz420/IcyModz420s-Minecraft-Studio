// Structures/StructurePlacement.Edit.cs: any edit of some chunks (a biome painted, …) into a copy of a world, the way a
// structure is placed — the world copied byte for byte, only those chunks rewritten in its own format and layout by its
// own writer, moved into the copy, and checked: they read back exactly as written (blocks and biomes), the rest the same.
// Part of StructurePlacement (main part in StructurePlacement.cs; the per-format transplants in StructurePlacement.Formats.cs).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static partial class StructurePlacement
{
    /// <summary>
    /// Applies <paramref name="edit"/> to each of <paramref name="chunks"/> (those the world has) of dimension
    /// <paramref name="dimensionId"/> in a NEW copy of the world at <paramref name="output"/>; the world itself is only read.
    /// Not for 3DS worlds (rewriting their chunks makes the game delete the world). Classic and Indev levels are one block
    /// array: their blocks are changed directly (StructurePlacement.Finite.cs).
    /// </summary>
    public static PlacementResult EditIntoCopy(SaveWorld world, string dimensionId, IEnumerable<ChunkPos> chunks, Action<CanonicalChunk> edit,
        string output, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (world.OpenedForRescue) throw new InvalidOperationException("This world was opened for rescue. Rescue it into a new world first, then edit that.");
        if (world is ThreeDsWorld) throw new NotSupportedException(ThreeDsNotWritable);
        if (WorldUse.InUse(world) is string busy) throw new IOException(busy + " Close it first, so the copy is made from a world that is not being written to.");
        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("A file with that name already exists; choose another folder.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("That folder is not empty; choose a new folder name.");
        string source = Path.GetFullPath(world.Location).TrimEnd('\\');
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The copy cannot go inside the world being edited.");
        var dim = world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId) ?? throw new ArgumentException($"The world has no {dimensionId}.");
        var stored = dim.ChunkPositions.ToHashSet();
        var present = chunks.Distinct().Where(stored.Contains).ToList();
        if (present.Count == 0) throw new IOException("None of those chunks is generated in this world yet: there is nothing there to change.");

        if (world is FiniteWorld)
        {
            var finite = EditFinite(world, dimensionId, present, edit, output, new PlacementResult { Format = world.FormatDescription }, progress, cancel);
            finite.Elapsed = clock.Elapsed;
            return finite;
        }
        EnsureRoomForCopy(world.Location, output);
        var result = new PlacementResult { Format = world.FormatDescription };
        progress?.Report(new ConversionProgress { Stage = "Copying the world", Done = 0, Total = 1 });
        result.OpenPath = CopyWorld(world.Location, output, progress, cancel);
        string temp = Path.Combine(Path.GetTempPath(), "MinecraftStudio-edit-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // 1. Re-encode just those chunks from the copy, edited, in the world's own format.
            var want = present.ToHashSet();
            ConversionReport report;
            using (var src = WorldLoader.Open(result.OpenPath))
            {
                var (target, same) = SameFormat(src);
                var options = new ConversionOptions
                {
                    FiniteCenterX = same.FiniteCenterX, FiniteCenterZ = same.FiniteCenterZ, ConsoleWorldChunks = same.ConsoleWorldChunks,
                    Area = (present.Min(p => p.X), present.Min(p => p.Z), present.Max(p => p.X), present.Max(p => p.Z)),
                    Dimensions = new HashSet<string> { dimensionId },
                    StampChunk = (c, d) => { if (d == dimensionId && want.Contains(new ChunkPos(c.X, c.Z))) edit(c); },
                    Verify = false,
                    ConvertPlayers = false,
                };
                report = WorldConverter.Run(src, target, temp, options, progress, cancel);
            }
            // What the re-encode lost in those chunks; the world's own files (level.dat, raids, scoreboard …) are in the copy byte
            // for byte, so what it says about world files does not apply.
            foreach (var l in report.Losses.Where(l => l.Lost && l.Kind is not (LossKind.Player or LossKind.World)).Take(10)) result.Notes.Add("In the changed chunks: " + l);
            if (report.FailedChunks > 0) result.Notes.Add($"{report.FailedChunks} chunk(s) could not be read, so they were left as they were.");

            // 2. Move those chunks into the copy.
            progress?.Report(new ConversionProgress { Stage = "Writing the changed chunks into the copy", Done = 0, Total = 1 });
            using (var built = WorldLoader.Open(report.OpenPath ?? temp))
            using (var copy = WorldLoader.Open(result.OpenPath))
            {
                switch (copy)
                {
                    case JavaWorld j when built is JavaWorld bj: TransplantJava(bj, j, dimensionId, present); break;
                    case BedrockWorld b when built is BedrockWorld bb: TransplantBedrock(bb, b, dimensionId, present); break;
                    case ConsoleWorld c when built is ConsoleWorld bc: TransplantConsole(bc, c, dimensionId, present, result); break;
                    case PocketLegacyWorld p when built is PocketLegacyWorld bp: TransplantPocket(bp, p, present, result); break;
                    default: throw new IOException($"The chunks were encoded as {built.FormatDescription}, which does not match {copy.FormatDescription}.");
                }
            }
            result.ChunksChanged = present.Count;

            // 3. The copy's changed chunks read back exactly as the writer encoded them: every block and every biome.
            using (var built = WorldLoader.Open(report.OpenPath ?? temp))
            using (var copy = WorldLoader.Open(result.OpenPath))
            {
                var rb = new CanonicalReader(built, built.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimensionId));
                var rc = new CanonicalReader(copy, copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimensionId));
                int wrong = 0;
                foreach (var pos in present)
                {
                    cancel.ThrowIfCancellationRequested();
                    var b = rb.Get(pos);
                    var c = rc.Get(pos);
                    if (b == null || c == null || !SameBlocks(b, c) || !SameBiomes(b, c)) wrong++;
                }
                if (wrong > 0) result.Problems.Add($"{wrong} changed chunk(s) did not read back as written");
            }
            CheckUntouched(world, result.OpenPath, dimensionId, present, result, cancel);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        finally { TryDelete(temp); }
        result.Elapsed = clock.Elapsed;
        return result;
    }

    static bool SameBiomes(CanonicalChunk a, CanonicalChunk b)
    {
        foreach (var sy in a.Biomes.Keys.Union(b.Biomes.Keys))
        {
            if (!a.Biomes.TryGetValue(sy, out var x) || !b.Biomes.TryGetValue(sy, out var y)) continue;
            if (!x.SequenceEqual(y)) return false;
        }
        return a.ColumnBiomes == null || b.ColumnBiomes == null || a.ColumnBiomes.SequenceEqual(b.ColumnBiomes);
    }
}
