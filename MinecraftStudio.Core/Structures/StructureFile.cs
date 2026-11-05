// Structures/StructureFile.cs: save/load a Structure by file type, safely.
// Owns: StructureFormat, Save/Load for every structure file the program knows — the Java structure .nbt (StructureNbt),
// Bedrock's .mcstructure (McStructure), WorldEdit's .schem (SpongeSchematic), Litematica's .litematic
// (LitematicSchematic) and the MCEdit .schematic of 1.12 and older (McEditSchematic): saved by extension, loaded by what
// the file holds (so a renamed file still loads); the file-dialog filters; and ReadRoot — the one way structure files
// are read: they come from anywhere (downloads), so the size is capped and decompression stops at the cap, so a hostile
// file cannot exhaust memory.
using System.IO.Compression;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Structures;

public enum StructureFormat { JavaStructure, BedrockStructure, WorldEdit, Litematica, McEdit }

public static class StructureFile
{
    public const string OpenFilter = "Structures and schematics (*.nbt;*.mcstructure;*.schem;*.litematic;*.schematic)|*.nbt;*.mcstructure;*.schem;*.litematic;*.schematic" +
        "|Java structure (*.nbt)|*.nbt|Bedrock structure (*.mcstructure)|*.mcstructure|WorldEdit schematic (*.schem)|*.schem|Litematica schematic (*.litematic)|*.litematic" +
        "|MCEdit / Schematica schematic, 1.12 and older (*.schematic)|*.schematic|All files|*.*";
    public const string SaveFilter = "Java structure, for structure blocks (*.nbt)|*.nbt|Bedrock structure (*.mcstructure)|*.mcstructure|WorldEdit schematic (*.schem)|*.schem" +
        "|Litematica schematic (*.litematic)|*.litematic|MCEdit / Schematica schematic, 1.12 and older (*.schematic)|*.schematic";

    /// <summary>The most a structure file may hold once decompressed (a real building is a few megabytes).</summary>
    public const long MaxBytes = 1L << 30;

    /// <summary>The format a file name says.</summary>
    public static StructureFormat FormatOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mcstructure" => StructureFormat.BedrockStructure,
        ".schem" => StructureFormat.WorldEdit,
        ".litematic" => StructureFormat.Litematica,
        ".schematic" => StructureFormat.McEdit,
        _ => StructureFormat.JavaStructure,
    };

    /// <summary>Whether the format holds blocks by number (Minecraft 1.12 and older) rather than by name.</summary>
    public static bool IsNumbered(StructureFormat f) => f == StructureFormat.McEdit;

    /// <summary>Whether the format is a Java one that can be made for a chosen Java version.</summary>
    public static bool HasJavaVersion(StructureFormat f) => f is StructureFormat.JavaStructure or StructureFormat.WorldEdit or StructureFormat.Litematica;

    /// <summary>
    /// Saves for the format the file name says; Java formats for Java version <paramref name="dataVersion"/> (0 = the
    /// newest; the .schematic is always 1.12).
    /// </summary>
    public static void Save(Structure s, string path, int dataVersion = 0)
    {
        switch (FormatOf(path))
        {
            case StructureFormat.BedrockStructure: McStructure.Save(s, path); break;
            case StructureFormat.WorldEdit: SpongeSchematic.Save(s, path, dataVersion); break;
            case StructureFormat.Litematica: LitematicSchematic.Save(s, path, dataVersion); break;
            case StructureFormat.McEdit: McEditSchematic.Save(s, path); break;
            default: StructureNbt.Save(s, path, dataVersion); break;
        }
    }

    /// <summary>
    /// Saves like <see cref="Save"/>, then reads the file back and compares it block by block: what came back as another
    /// block (a block the format or version doesn't have, written as the closest one it has) is listed, most first.
    /// </summary>
    public static List<(string From, string To, long Count)> SaveChecked(Structure s, string path, int dataVersion = 0)
    {
        Save(s, path, dataVersion);
        var back = Load(path);
        if (back.SizeX != s.SizeX || back.SizeY != s.SizeY || back.SizeZ != s.SizeZ)
            throw new IOException($"The saved file reads back as {back.SizeX}×{back.SizeY}×{back.SizeZ}, not {s.SizeX}×{s.SizeY}×{s.SizeZ}.");
        var changed = new Dictionary<(string, string), long>();
        for (int i = 0; i < s.AllBlocks.Count; i++)
        {
            var (x, y) = (s.AllBlocks[i], back.AllBlocks[i]);
            if (x.Name != y.Name && !(x.IsAir && y.IsAir)) changed[(x.Name, y.Name)] = changed.GetValueOrDefault((x.Name, y.Name)) + 1;
        }
        return changed.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).OrderByDescending(c => c.Value).ToList();
    }

    /// <summary>
    /// Whether the file holds a structure in one of the formats (read by contents, so a level.dat-like .nbt is not one).
    /// A file that cannot be read is not one either.
    /// </summary>
    public static bool IsStructure(string path)
    {
        try
        {
            var root = ReadRoot(path);
            return LitematicSchematic.Looks(root) || SpongeSchematic.Looks(root) || McEditSchematic.Looks(root) || StructureNbt.Looks(root)
                || root.GetCompound("structure") != null && root.ContainsKey("format_version");
        }
        // Only a probe: whatever stops the file from reading as NBT means it is not a structure.
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
    }

    /// <summary>Loads any structure file, by what it holds (its name decides only when the contents say nothing clear).</summary>
    public static Structure Load(string path)
    {
        var root = ReadRoot(path);
        if (LitematicSchematic.Looks(root)) return LitematicSchematic.FromNbt(root);
        if (SpongeSchematic.Looks(root)) return SpongeSchematic.FromNbt(root);
        if (McEditSchematic.Looks(root)) return McEditSchematic.FromNbt(root);
        if (StructureNbt.Looks(root)) return StructureNbt.FromNbt(root);
        if (root.GetCompound("structure") != null && root.ContainsKey("format_version")) return McStructure.FromNbt(root);
        return FormatOf(path) switch
        {
            StructureFormat.BedrockStructure => McStructure.FromNbt(root),
            StructureFormat.WorldEdit => SpongeSchematic.FromNbt(root),
            StructureFormat.Litematica => LitematicSchematic.FromNbt(root),
            StructureFormat.McEdit => McEditSchematic.FromNbt(root),
            _ => StructureNbt.FromNbt(root),
        };
    }

    /// <summary>
    /// Reads a structure file's root compound: refuses files (and decompressed contents) over <see cref="MaxBytes"/>,
    /// and anything that is not NBT with a compound at its root.
    /// </summary>
    public static NbtCompound ReadRoot(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The structure file does not exist.", path);
        if (info.Length > MaxBytes) throw new SaveFormatException("That file is far too large to be a structure.");
        byte[] data = File.ReadAllBytes(path);
        var kind = IO.Compression.Detect(data);
        if (kind is CompressionKind.Gzip or CompressionKind.Zlib) data = Inflate(data, kind);
        var file = NbtFile.Parse(data);
        return file.Roots.Count > 0 && file.Root is NbtCompound root ? root : throw new SaveFormatException("This is not a structure file.");
    }

    /// <summary>Decompresses gzip or zlib, stopping with an error past <see cref="MaxBytes"/>.</summary>
    static byte[] Inflate(byte[] data, CompressionKind kind)
    {
        using var input = new MemoryStream(data);
        using Stream z = kind == CompressionKind.Gzip ? new GZipStream(input, CompressionMode.Decompress) : new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        try
        {
            while ((n = z.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + n > MaxBytes) throw new SaveFormatException("That file unpacks to far more than a structure could be, so it was not read.");
                output.Write(buffer, 0, n);
            }
        }
        catch (InvalidDataException e) { throw new SaveFormatException("The structure file is damaged: " + e.Message, e); }
        return output.ToArray();
    }
}
