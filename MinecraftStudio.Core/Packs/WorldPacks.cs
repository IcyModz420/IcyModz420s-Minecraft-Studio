// MinecraftStudio.Core/Packs/WorldPacks.cs: seeing and changing the packs a PC world uses.
// Owns: which worlds this works for (Java Edition (PC) and Bedrock Edition (Windows PC)), Read, Install, SetActive,
// Move, Uninstall (the edition-specific halves: WorldPacks.Java.cs, WorldPacks.Bedrock.cs) and the shared safe archive
// helpers. Packs come from anywhere, so every archive entry must land inside its folder and the unpacked size is
// capped; uninstalling never deletes — the pack's files move to a "removed packs" folder the caller names.
using System.IO.Compression;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Packs;

public static partial class WorldPacks
{
    public const string JavaLabel = "Java Edition (PC)";
    public const string BedrockLabel = "Bedrock Edition (Windows PC)";

    /// <summary>The most a pack archive may unpack to (big texture packs are a few hundred megabytes).</summary>
    public const long MaxUnpackedBytes = 4L << 30;

    /// <summary>"Java Edition (PC)" or "Bedrock Edition (Windows PC)", or null when packs are not managed for this world.</summary>
    public static string? EditionLabel(SaveWorld world) => world switch
    {
        JavaWorld => JavaLabel,
        BedrockWorld => BedrockLabel,
        _ => null,
    };

    /// <summary>Why this world's packs can't be changed here, or null when they can.</summary>
    public static string? WhyNot(SaveWorld world) =>
        world.OpenedForRescue ? "This world was opened for rescue, so it is only read."
        : world is JavaWorld or BedrockWorld ? null
        : "Packs are managed for PC worlds: data packs and the world resource pack of Java Edition (PC), and behavior and resource packs of Bedrock Edition (Windows PC). This world is " + world.FormatDescription + ".";

    /// <summary>
    /// A world folder's edition label and packs without opening the whole world (for the list of every world on this
    /// PC): Java (level.dat and datapacks) and Bedrock (the pack lists). Null for anything else (3DS and old Pocket
    /// Edition worlds also have a level.dat, but they are not PC worlds).
    /// </summary>
    public static (string Edition, List<WorldPack> Packs)? ReadFolder(string folder)
    {
        if (!File.Exists(Path.Combine(folder, "level.dat")) || ThreeDsWorld.LooksLike3ds(folder) || File.Exists(Path.Combine(folder, "chunks.dat"))) return null;
        try
        {
            if (Directory.Exists(Path.Combine(folder, "db"))) return (BedrockLabel, ReadBedrock(folder));
            var data = (Nbt.NbtFile.Load(Path.Combine(folder, "level.dat")).Root as Nbt.NbtCompound)?.GetCompound("Data");
            if (data == null) return null;
            return (JavaLabel, ReadJava(folder, data, data.GetLongOrNull("DataVersion") is long v ? (int)v : null));
        }
        catch (Exception e) when (e is IOException or SaveFormatException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    /// <summary>Every pack the world has or uses, in the order the game applies them.</summary>
    public static List<WorldPack> Read(SaveWorld world) => world switch
    {
        JavaWorld j => ReadJava(j),
        BedrockWorld b => ReadBedrock(b),
        _ => [],
    };

    /// <summary>
    /// Installs a pack (file or folder) into the world and turns it on: Java data packs go into datapacks/ and Java
    /// resource packs become the world resource pack; Bedrock .mcpack / .mcaddon / .zip packs go into the world's own
    /// behavior_packs or resource_packs. An older copy of the same pack is replaced (moved to
    /// <paramref name="removedFolder"/>). Returns what was installed (an add-on can hold several packs).
    /// </summary>
    public static List<WorldPack> Install(SaveWorld world, string path, string removedFolder)
    {
        Guard(world);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The pack file or folder does not exist.", path);
        return world switch
        {
            JavaWorld j => InstallJava(j, path, removedFolder),
            BedrockWorld b => InstallBedrock(b, path, removedFolder),
            _ => throw new NotSupportedException(WhyNot(world)),
        };
    }

    /// <summary>Turns a pack on or off for the world (its files stay where they are).</summary>
    public static void SetActive(SaveWorld world, WorldPack pack, bool active)
    {
        Guard(world);
        switch (world)
        {
            case JavaWorld j: SetActiveJava(j, pack, active); break;
            case BedrockWorld b: SetActiveBedrock(b, pack, active); break;
            default: throw new NotSupportedException(WhyNot(world));
        }
    }

    /// <summary>Moves a turned-on pack earlier or later in the order the game applies packs (later wins for Java, higher wins for Bedrock).</summary>
    public static void Move(SaveWorld world, WorldPack pack, int delta)
    {
        Guard(world);
        switch (world)
        {
            case JavaWorld j: MoveJava(j, pack, delta); break;
            case BedrockWorld b: MoveBedrock(b, pack, delta); break;
            default: throw new NotSupportedException(WhyNot(world));
        }
    }

    /// <summary>
    /// Takes a pack out of the world: it is turned off and, when its files are inside the world, they move to
    /// <paramref name="removedFolder"/> (never deleted). Returns where the files went, or null when none moved (a
    /// Bedrock pack installed in the game itself is only taken off this world's list; other worlds keep using it).
    /// </summary>
    public static string? Uninstall(SaveWorld world, WorldPack pack, string removedFolder)
    {
        Guard(world);
        if (pack.BuiltIn) throw new InvalidOperationException($"{pack.Name} is part of the game and can only be turned off, not uninstalled.");
        return world switch
        {
            JavaWorld j => UninstallJava(j, pack, removedFolder),
            BedrockWorld b => UninstallBedrock(b, pack, removedFolder),
            _ => throw new NotSupportedException(WhyNot(world)),
        };
    }

    static void Guard(SaveWorld world)
    {
        if (WhyNot(world) is string why) throw new NotSupportedException(why);
        if (WorldUse.InUse(world) is string busy) throw new IOException(busy + " Close it first, then change its packs.");
    }

    // ---------------------------------------------------------------- shared helpers

    /// <summary>
    /// Unpacks the entries of <paramref name="zip"/> under <paramref name="root"/> (a folder prefix inside the archive,
    /// "" for the top) into <paramref name="destination"/>: each entry must land inside it, the total is capped at
    /// <see cref="MaxUnpackedBytes"/>, and every file is checked to have been written whole.
    /// </summary>
    static void Unpack(ZipArchive zip, string root, string destination)
    {
        Directory.CreateDirectory(destination);
        long total = 0;
        foreach (var e in zip.Entries)
        {
            string name = e.FullName.Replace('\\', '/');
            if (!name.StartsWith(root, StringComparison.Ordinal) || name.EndsWith('/')) continue;
            string rel = name[root.Length..];
            if (rel.Length == 0) continue;
            string target = SafeName.Inside(destination, rel);   // throws for "..", ":", reserved names, absolute paths
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            long written = 0;
            using (var src = e.Open())
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += n;
                    if ((total += n) > MaxUnpackedBytes) throw new InvalidDataException("The pack unpacks to far more than any pack could be, so it was not installed.");
                    dst.Write(buffer, 0, n);
                }
            }
            if (written != e.Length) throw new IOException($"{rel} was not unpacked completely.");
        }
    }

    /// <summary>Copies a folder (every file and subfolder) into <paramref name="destination"/>.</summary>
    static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, d)));
        foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(destination, Path.GetRelativePath(source, f)));
    }

    /// <summary>Moves a pack's file or folder into <paramref name="removedFolder"/> (a fresh name there) and returns its new path.</summary>
    static string MoveAside(string path, string removedFolder)
    {
        Directory.CreateDirectory(removedFolder);
        bool folder = Directory.Exists(path);
        string target = Unique(removedFolder, Path.GetFileName(path.TrimEnd('\\', '/')));
        if (!folder) { File.Move(path, target); return target; }
        try { Directory.Move(path, target); }
        catch (IOException)
        {
            // Another drive: copy, check, then take the original away.
            CopyFolder(path, target);
            if (Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Count() != Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count())
                throw new IOException("The pack could not be moved completely, so it was left where it was.");
            Directory.Delete(path, recursive: true);
        }
        return target;
    }

    /// <summary>A path in <paramref name="parent"/> for <paramref name="name"/> that is not taken (adds " (2)", " (3)"…).</summary>
    static string Unique(string parent, string name)
    {
        // A .zip keeps its extension last ("Pack (2).zip"); a folder name is used whole.
        bool zip = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string stem = zip ? name[..^4] : name, ext = zip ? name[^4..] : "";
        string path = Path.Combine(parent, name);
        for (int n = 2; File.Exists(path) || Directory.Exists(path); n++) path = Path.Combine(parent, $"{stem} ({n}){ext}");
        return path;
    }
}
