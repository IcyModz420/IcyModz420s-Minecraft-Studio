// MinecraftStudio.Core/World/WorldRescue.cs: Opens a damaged world for rescue, reading whatever can still be
// read: the damaged world itself is never changed.
// Owns: WorldRescue.
using System.IO.Compression;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;

namespace MinecraftStudio.Core.World;

/// <summary>
/// Opens a damaged world for rescue, reading whatever can still be read: the damaged world itself is never changed.
/// Java and Bedrock worlds get the deep reading (level.dat backups, region files searched for lost chunks, a damaged
/// database read file by file); other saves are read the normal way, where damaged chunks are left out one by one.
/// The rescued world is then converted into a new, clean world, which is checked like any conversion.
/// </summary>
public static class WorldRescue
{
    /// <summary>What is wrong with damaged data in plain words (the libraries' own messages talk about archives and streams).</summary>
    public static string Describe(Exception e) => e switch
    {
        InvalidDataException => "its compressed data is broken",
        EndOfStreamException => "it ends too early",
        _ => e.Message.TrimEnd('.'),
    };

    /// <summary>"1 chunk was" / "3 chunks were": a count with the words that go with it.</summary>
    public static string Count(int n, string one, string many) => n == 1 ? "1 " + one : $"{n:N0} {many}";

    public static SaveWorld Open(string path, List<string> findings, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        // A file of the world stands for its folder: level.dat (or its copy), levelname.txt, a region file, a file of
        // a Bedrock world's database.
        if (File.Exists(path) && Path.GetFileName(path).ToLowerInvariant() is "level.dat" or "level.dat_old" or "levelname.txt")
            path = Path.GetDirectoryName(Path.GetFullPath(path))!;
        else if (File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".mca" or ".mcr" && WorldOfRegionFile(path) is string folder)
            path = folder;
        else if (File.Exists(path) && Path.GetDirectoryName(Path.GetFullPath(path)) is string db && Path.GetFileName(db).Equals("db", StringComparison.OrdinalIgnoreCase) &&
                 Path.GetDirectoryName(db) is string bedrock && LooksBedrock(bedrock))
            path = bedrock;
        if (File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".mcworld" or ".mctemplate" or ".zip")
        {
            progress?.Report("Unpacking " + Path.GetFileName(path));
            path = Unpack(path, findings);
        }
        if (Directory.Exists(path))
        {
            if (LooksBedrock(path))
            {
                var bedrock = BedrockWorld.OpenForRescue(path, findings, progress);
                ShapeFindings(bedrock, findings);
                return bedrock;
            }
            if (LooksJava(path)) return JavaWorld.OpenForRescue(path, findings, progress, cancel);
        }
        var world = WorldLoader.Open(path, progress);
        findings.Add($"{world.FormatDescription}: read the normal way. Chunks that cannot be read are left out (and listed) when it is rescued into a new world.");
        return world;
    }

    /// <summary>Chunks stored in a way the game can't open: a repaired copy leaves the blank ones out; converting writes the rest again.</summary>
    static void ShapeFindings(BedrockWorld world, List<string> findings)
    {
        List<ChunkShapeProblem> problems;
        try { problems = BedrockChunkShape.Check(world); }
        catch (Exception e) when (e is IOException or InvalidDataException or IO.SaveFormatException) { return; }
        foreach (var p in problems)
            findings.Add(p.LostVersion
                ? $"{Count(p.Count, "chunk has", "chunks have")} lost its Version record (the first: {p.First}): the game wouldn't see them and would make them again over what was built there. The repaired copy writes it again, as the world's other chunks of their kind have it (one whose heights were damaged too shows no kind: it is left for the game to make again, and listed as lost)."
                : p.Problem == BedrockChunkShape.HeightsOnlyProblem
                ? $"{Count(p.Count, "chunk is", "chunks are")} left with only heights by the damage (the first: {p.First}): nothing of yours is in them. The repaired copy leaves them out, and the game makes them again."
                : p.Blank
                ? $"{Count(p.Count, "chunk is", "chunks are")} blank and stored old-style (the first: {p.First}): the game can't open the world with them. The repaired copy leaves them out, and the game makes them again."
                : $"{Count(p.Count, "chunk has", "chunks have")} {p.Problem} (the first: {p.First}): the game may not open the world. Convert it to the same Bedrock version to write every chunk again the way the game stores them.");
    }

    /// <summary>
    /// The world a region file belongs to: the nearest folder above it with level.dat (or its copy), up to the four
    /// levels of dimensions/namespace/name/region; else the folder holding its region folder. Null when it is not in one.
    /// </summary>
    static string? WorldOfRegionFile(string path)
    {
        string? regionFolder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (regionFolder == null || Path.GetDirectoryName(regionFolder) is not string dimension) return null;
        string? folder = dimension;
        for (int up = 0; up < 4 && folder != null; up++, folder = Path.GetDirectoryName(folder))
            if (File.Exists(Path.Combine(folder, "level.dat")) || File.Exists(Path.Combine(folder, "level.dat_old"))) return folder;
        return LooksJava(dimension) ? dimension : null;
    }

    /// <summary>A folder that still looks like a Java or Bedrock world, even if it is too damaged to be recognised as one.</summary>
    public static bool LooksLikeWorld(string folder) => Directory.Exists(folder) && (LooksBedrock(folder) || LooksJava(folder));

    static bool LooksBedrock(string folder)
    {
        string db = Path.Combine(folder, "db");
        return Directory.Exists(db) && Directory.EnumerateFiles(db).Any(f => Path.GetExtension(f).ToLowerInvariant() is ".ldb" or ".log" or ".sst");
    }

    static bool LooksJava(string folder) =>
        Directory.Exists(Path.Combine(folder, "region")) || Directory.Exists(Path.Combine(folder, "DIM-1", "region")) ||
        Directory.Exists(Path.Combine(folder, "DIM1", "region")) || Directory.Exists(Path.Combine(folder, "dimensions")) ||
        File.Exists(Path.Combine(folder, "level.dat")) && !Directory.Exists(Path.Combine(folder, "db"));

    /// <summary>
    /// Unpacks a world package entry by entry into a new folder, leaving out (and listing) entries that are damaged
    /// instead of giving up on the whole package. Returns the world folder.
    /// </summary>
    /// <summary>Where damaged packages are unpacked: Documents\Minecraft Studio\Rescued\Unpacked unless set.</summary>
    public static string UnpackFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Rescued", "Unpacked");

    static string Unpack(string package, List<string> findings)
    {
        string root = UnpackFolder;
        string name = IO.SafeName.For(Path.GetFileNameWithoutExtension(package), "World");
        string dest = Path.Combine(root, name);
        for (int i = 2; Directory.Exists(dest) || File.Exists(dest); i++) dest = Path.Combine(root, $"{name} ({i})");
        Directory.CreateDirectory(dest);
        string destFull = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        int damaged = 0;
        using (var zip = ZipFile.OpenRead(package))
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue;
                string target = Path.GetFullPath(Path.Combine(dest, e.FullName));
                if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) { findings.Add($"Skipped \"{e.FullName}\": its path leads outside the world."); continue; }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, overwrite: true);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    damaged++;
                    if (damaged <= 10) findings.Add($"\"{e.FullName}\" in the package is damaged and was left out ({ex.Message}).");
                    try { File.Delete(target); } catch (IOException) { }
                }
            }
        if (damaged > 10) findings.Add($"{damaged - 10} more damaged files in the package were left out.");
        findings.Add($"The package was unpacked into {dest} (the package itself is not changed).");
        return File.Exists(Path.Combine(dest, "level.dat")) || Directory.Exists(Path.Combine(dest, "db")) || Directory.Exists(Path.Combine(dest, "region")) ? dest
            : Directory.GetDirectories(dest).FirstOrDefault(d => File.Exists(Path.Combine(d, "level.dat")) || Directory.Exists(Path.Combine(d, "db")) || Directory.Exists(Path.Combine(d, "region"))) ?? dest;
    }
}
