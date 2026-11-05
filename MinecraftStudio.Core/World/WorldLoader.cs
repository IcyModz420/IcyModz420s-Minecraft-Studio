// MinecraftStudio.Core/World/WorldLoader.cs: opens a save of any edition from a path (detects the format; unpacks packaged worlds).
// Owns: WorldKind, WorldLoader.
using System.IO.Compression;
﻿using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;

namespace MinecraftStudio.Core.World;

public enum WorldKind
{
    Unknown,
    Java,
    JavaFinite,
    Bedrock,
    PocketLegacy,
    ThreeDS,
    LegacyConsole,
}

/// <summary>Detects what a path is and opens it with the right reader.</summary>
public static class WorldLoader
{
    static readonly Dictionary<string, string> Unpacked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A world packed in a zip: Bedrock .mcworld / .mctemplate exports and zipped Java worlds (map downloads).</summary>
    public static bool IsWorldPackage(string path)
    {
        if (!File.Exists(path) || Path.GetExtension(path).ToLowerInvariant() is not (".mcworld" or ".mctemplate" or ".zip")) return false;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => e.FullName.Replace('\\', '/').Split('/') is var parts && parts[^1].Equals("level.dat", StringComparison.OrdinalIgnoreCase) && parts.Length <= 2);
        }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>
    /// Unpacks a world package into a new folder of its own (Documents\Minecraft Studio\Imported) and returns the
    /// world folder; the package itself is never changed. A package is unpacked once per session.
    /// </summary>
    public static string ExtractPackage(string path)
    {
        string full = Path.GetFullPath(path);
        lock (Unpacked)
            if (Unpacked.TryGetValue(full, out var done) && Directory.Exists(done)) return done;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Imported");
        string name = IO.SafeName.For(Path.GetFileNameWithoutExtension(full), "World");
        string dest = Path.Combine(root, name);
        for (int i = 2; Directory.Exists(dest) || File.Exists(dest); i++) dest = Path.Combine(root, $"{name} ({i})");
        Directory.CreateDirectory(dest);
        // ExtractToDirectory refuses entries that would land outside the folder.
        try { ZipFile.ExtractToDirectory(full, dest); }
        catch { try { Directory.Delete(dest, true); } catch { } throw; }
        string world = File.Exists(Path.Combine(dest, "level.dat")) ? dest
            : Directory.GetDirectories(dest).FirstOrDefault(d => File.Exists(Path.Combine(d, "level.dat"))) ?? dest;
        lock (Unpacked) Unpacked[full] = world;
        return world;
    }

    public static WorldKind Detect(string path)
    {
        if (IsWorldPackage(path))
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Any(e => e.FullName.Replace('\\', '/').Contains("db/", StringComparison.OrdinalIgnoreCase)) ? WorldKind.Bedrock : WorldKind.Java;
        }
        if (Directory.Exists(path))
        {
            if (BedrockWorld.LooksLikeBedrockWorld(path)) return WorldKind.Bedrock;
            if (ThreeDsWorld.LooksLike3ds(path)) return WorldKind.ThreeDS;
            if (PocketLegacyWorld.LooksLikePocketLegacy(path)) return WorldKind.PocketLegacy;
            if (JavaWorld.LooksLikeJavaWorld(path)) return WorldKind.Java;
            if (LegacyConsole.ConsoleSave.LooksLikeConsoleFolder(path)) return WorldKind.LegacyConsole;
            return WorldKind.Unknown;
        }
        if (File.Exists(path))
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            if (name == "level.dat") return Detect(Path.GetDirectoryName(path)!);
            if (LegacyConsole.ConsoleSave.LooksLikeConsoleFile(path)) return WorldKind.LegacyConsole;
            if (FiniteWorld.CanOpen(path)) return WorldKind.JavaFinite;
        }
        return WorldKind.Unknown;
    }

    public static SaveWorld Open(string path, IProgress<string>? progress = null)
    {
        if (File.Exists(path) && Path.GetFileName(path).Equals("level.dat", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path)!;
        if (IsWorldPackage(path))
        {
            progress?.Report("Unpacking " + Path.GetFileName(path));
            path = ExtractPackage(path);
        }
        return Detect(path) switch
        {
            WorldKind.Bedrock => BedrockWorld.Open(path, progress),
            WorldKind.ThreeDS => ThreeDsWorld.Open(path),
            WorldKind.PocketLegacy => PocketLegacyWorld.Open(path),
            WorldKind.Java => JavaWorld.Open(path),
            WorldKind.JavaFinite => FiniteWorld.Open(path),
            WorldKind.LegacyConsole => LegacyConsole.ConsoleSave.Open(path, progress),
            _ => throw new IO.SaveFormatException($"\"{path}\" is not a Minecraft world this tool recognises."),
        };
    }
}
