// Structures/StructurePlacement.cs: putting a structure into a copy of any world, keeping that world exactly as it was.
// Owns: PlaceIntoCopy — copies the world byte for byte, then rewrites only the chunks the structure touches (re-encoded
// by the same matrix-tested writers, in the world's own format and layout) and checks the result; SameFormat (target +
// options that re-save a world as itself); Height; PlacementResult. Everything outside those chunks — level.dat,
// players, scoreboards, advancements, other chunks and dimensions — stays byte-identical. The original is only read.
// Per-format work (Java/Bedrock/console/Pocket transplants, Classic/Indev direct edits): StructurePlacement.Formats.cs;
// why 3DS worlds are refused (cropping from them works): StructurePlacement.ThreeDs.cs.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

/// <summary>What placing a structure did: where the copy is, how much changed, and what the check found.</summary>
public sealed class PlacementResult
{
    /// <summary>The copied world with the structure in it: a folder, or the save file inside the output folder.</summary>
    public string OpenPath { get; set; } = "";
    public string Format { get; set; } = "";
    public int ChunksChanged { get; set; }
    public long BlocksPlaced { get; set; }
    /// <summary>Structure blocks that fell outside the world's height or where it has no chunks yet.</summary>
    public long BlocksLeftOut { get; set; }
    public List<string> Notes { get; } = [];
    /// <summary>What the check of the finished copy found wrong (empty when it passed).</summary>
    public List<string> Problems { get; } = [];
    public bool Verified => Problems.Count == 0;
    public TimeSpan Elapsed { get; set; }
}

public static partial class StructurePlacement
{
    /// <summary>
    /// The conversion target and options that re-save <paramref name="world"/> in its own format with its own layout.
    /// Java and Bedrock already keep their coordinates; console and Pocket Edition 0.2–0.8 writers otherwise centre on
    /// the spawn and pick a size, so their centre and size are pinned to the world's own.
    /// </summary>
    public static (ConversionTarget Target, ConversionOptions Options) SameFormat(SaveWorld world)
    {
        var target = TargetCatalog.ClosestTo(world);
        var options = new ConversionOptions();
        switch (world)
        {
            case ConsoleWorld console:
                // Console worlds are stored centred on chunk 0, 0: an offset of 0 keeps every chunk where it is.
                options.FiniteCenterX = 0;
                options.FiniteCenterZ = 0;
                options.ConsoleWorldChunks = (int)(console.Level?.RootCompound.GetCompound("Data")?.GetLongOrNull("XZSize") ?? 54);
                // An Xbox 360 world opened from its save package comes back as that package (rehashed), not a bare file.
                if (console.Package != null) options.ConsoleTemplatePackage = console.Location;
                break;
            case PocketLegacyWorld:
                // Pocket Edition 0.2–0.8 worlds are the fixed 16×16 chunks 0..15: centre chunk 8 means no shift.
                options.FiniteCenterX = 128;
                options.FiniteCenterZ = 128;
                break;
        }
        return (target, options);
    }

    /// <summary>The build height of a dimension in the world's own format (cells outside it are not placed).</summary>
    public static (int Min, int Max) Height(SaveWorld world, string dimensionId)
    {
        if (world is FiniteWorld && world.Dimensions.FirstOrDefault() is FiniteDimension fd) return (0, fd.Height - 1);
        if (world is PocketLegacyWorld or ThreeDsWorld) return (0, 127);
        return WorldConverter.TargetHeight(SameFormat(world).Target, dimensionId);
    }

    /// <summary>The chunks a structure with its low corner at (<paramref name="ox"/>, *, <paramref name="oz"/>) covers.</summary>
    public static List<ChunkPos> Footprint(Structure s, int ox, int oz)
    {
        var list = new List<ChunkPos>();
        for (int cz = oz >> 4; cz <= (oz + s.SizeZ - 1) >> 4; cz++)
            for (int cx = ox >> 4; cx <= (ox + s.SizeX - 1) >> 4; cx++)
                list.Add(new ChunkPos(cx, cz));
        return list;
    }

    /// <summary>
    /// Places <paramref name="s"/> into a NEW copy of <paramref name="world"/> at <paramref name="output"/>, with its low
    /// corner at world (<paramref name="ox"/>, <paramref name="oy"/>, <paramref name="oz"/>) in
    /// <paramref name="dimensionId"/>. The copy keeps the world's edition, version and layout; only the chunks the
    /// structure touches change. With <paramref name="pasteAir"/> false the structure's air is skipped (the terrain
    /// around the building stays). The original world is never written to. Throws (leaving nothing behind) when the
    /// structure cannot be placed; otherwise the result says what the check of the copy found.
    /// </summary>
    public static PlacementResult PlaceIntoCopy(SaveWorld world, Structure s, string dimensionId, int ox, int oy, int oz, bool pasteAir,
        string output, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        if (world.OpenedForRescue) throw new InvalidOperationException("This world was opened for rescue. Rescue it into a new world first, then place the structure into that.");
        // Refused before anything is copied: changed 3DS chunks make the game delete the world.
        if (world is ThreeDsWorld) throw new NotSupportedException(ThreeDsNotWritable);
        if (WorldUse.InUse(world) is string busy) throw new IOException(busy + " Close it first, so the copy is made from a world that is not being written to.");

        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("A file with that name already exists; choose another folder.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("That folder is not empty; choose a new folder name.");
        string source = Path.GetFullPath(world.Location).TrimEnd('\\');
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The copy cannot go inside the world being placed into.");

        var dim = world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId)
            ?? throw new ArgumentException($"The world has no {dimensionId}.");
        var stored = dim.ChunkPositions.ToHashSet();
        var footprint = Footprint(s, ox, oz);
        var present = footprint.Where(stored.Contains).ToList();
        if (present.Count == 0)
            throw new IOException("The structure would land entirely where the world has no chunks yet. Choose a spot inside the explored world, or explore there in the game first.");

        EnsureRoomForCopy(world.Location, output);
        var result = new PlacementResult { Format = world.FormatDescription };
        if (present.Count < footprint.Count)
        {
            var missing = footprint.Except(present).ToHashSet();
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                    if (missing.Contains(new ChunkPos((ox + x) >> 4, (oz + z) >> 4)))
                        for (int y = 0; y < s.SizeY; y++) if (pasteAir || !s.Get(x, y, z).IsAir) result.BlocksLeftOut++;
            result.Notes.Add($"{missing.Count} of the {footprint.Count} chunks under the structure are not generated in this world yet, so the parts of the structure there were left out. Explore there in the game first to place all of it.");
        }

        progress?.Report(new ConversionProgress { Stage = "Copying the world", Done = 0, Total = 1 });
        result.OpenPath = CopyWorld(world.Location, output, progress, cancel);
        try
        {
            var (minY, maxY) = Height(world, dimensionId);
            switch (world)
            {
                case FiniteWorld:
                    PlaceFinite(result.OpenPath, s, ox, oy, oz, pasteAir, result, cancel);
                    break;
                default:
                    PlaceByTransplant(result.OpenPath, s, dimensionId, ox, oy, oz, minY, maxY, pasteAir, present, result, progress, cancel);
                    break;
            }
            CheckUntouched(world, result.OpenPath, dimensionId, present, result, cancel);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        result.Elapsed = clock.Elapsed;
        return result;
    }

    /// <summary>Refuses up front when the output drive cannot hold a full copy of the world (plus a margin).</summary>
    internal static void EnsureRoomForCopy(string location, string output)
    {
        long size = LongRun.EstimateSize(location);
        if (size <= 0) return;
        try
        {
            var root = Path.GetPathRoot(output);
            if (string.IsNullOrEmpty(root)) return;
            var drive = new DriveInfo(root);
            long needed = size + (256L << 20);
            if (drive.IsReady && drive.AvailableFreeSpace < needed)
                throw new IOException($"Not enough free space on {drive.Name} for the copy of the world: it needs about {needed / (1024.0 * 1024 * 1024):0.0} GB and {drive.AvailableFreeSpace / (1024.0 * 1024 * 1024):0.0} GB is free.");
        }
        catch (ArgumentException) { }
    }

    /// <summary>
    /// Copies a world byte for byte: a folder with everything in it (empty folders too), or a single save file into
    /// a new folder. Returns the path of the copied world (the folder, or the copied file).
    /// </summary>
    internal static string CopyWorld(string location, string output, IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        if (File.Exists(location))
        {
            Directory.CreateDirectory(output);
            string to = Path.Combine(output, Path.GetFileName(location));
            CopyFile(location, to);
            return to;
        }
        var files = Directory.EnumerateFiles(location, "*", SearchOption.AllDirectories).ToList();
        Directory.CreateDirectory(output);
        foreach (var d in Directory.EnumerateDirectories(location, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(output, Path.GetRelativePath(location, d)));
        int done = 0;
        foreach (var f in files)
        {
            cancel.ThrowIfCancellationRequested();
            CopyFile(f, Path.Combine(output, Path.GetRelativePath(location, f)));
            if ((++done & 63) == 0) progress?.Report(new ConversionProgress { Stage = "Copying the world", Done = done, Total = files.Count });
        }
        return output;
    }

    static void CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var dst = new FileStream(to, FileMode.CreateNew, FileAccess.Write))
            src.CopyTo(dst);
        File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
    }

    /// <summary>
    /// Checks a sample of chunks the structure does not touch still read exactly as in the original (proof nothing
    /// else changed), and that the copy opens.
    /// </summary>
    static void CheckUntouched(SaveWorld original, string copyPath, string dimensionId, List<ChunkPos> changed, PlacementResult result, CancellationToken cancel)
    {
        using var copy = WorldLoader.Open(copyPath);
        var a = original.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimensionId);
        var b = copy.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId);
        if (b == null) { result.Problems.Add("the copy no longer has this dimension"); return; }
        var changedSet = changed.ToHashSet();
        var sample = a.ChunkPositions.Where(p => !changedSet.Contains(p)).OrderBy(p => (p.X * 7919 + p.Z * 104729) & 0xFFFF).Take(40).ToList();
        var ra = new CanonicalReader(original, a);
        var rb = new CanonicalReader(copy, b);
        int differ = 0;
        foreach (var pos in sample)
        {
            cancel.ThrowIfCancellationRequested();
            var ca = ra.Get(pos);
            var cb = rb.Get(pos);
            if (ca == null) continue;
            if (cb == null || !SameBlocks(ca, cb)) differ++;
        }
        if (differ > 0) result.Problems.Add($"{differ} chunk(s) away from the structure read differently in the copy");
        if (copy.Dimensions.Sum(d => d.ChunkPositions.Count) != original.Dimensions.Sum(d => d.ChunkPositions.Count))
            result.Problems.Add("the copy does not have the same chunks as the world");
    }

    static bool SameBlocks(CanonicalChunk a, CanonicalChunk b)
    {
        foreach (var sy in a.Sections.Keys.Union(b.Sections.Keys))
            for (int y = sy * 16; y < sy * 16 + 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        if (a.GetBlock(x, y, z) != b.GetBlock(x, y, z)) return false;
        return true;
    }

    internal static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
