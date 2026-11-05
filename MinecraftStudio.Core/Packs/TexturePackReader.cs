// MinecraftStudio.Core/Packs/TexturePackReader.cs: Reads texture packs of every kind into a TexturePack: Java
// resource packs (any version, folder or zip), old Java texture packs (1.5 textures/blocks, and terrain.png
// before that), a Java game jar, Bedrock resource packs (.mcpack, .zip or folder) and the installed Bedrock
// game's own textures.
// Owns: TexturePackReader.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Packs;

/// <summary>
/// Reads texture packs of every kind into a <see cref="TexturePack"/>: Java resource packs (any version, folder or zip),
/// old Java texture packs (1.5 textures/blocks, and terrain.png before that), a Java game jar, Bedrock resource packs
/// (.mcpack, .zip or folder) and the installed Bedrock game's own textures. Block and item textures get their shared
/// names; everything else is kept aside as other files.
/// </summary>
public static partial class TexturePackReader
{
    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly string[] TextureFolders = ["block", "blocks", "item", "items"];

    /// <summary>The files of a folder or archive (.zip, .mcpack, .jar) by path with '/' separators.</summary>
    public static Dictionary<string, Func<byte[]>> Files(string path, Func<string, bool>? keep = null, List<string>? notes = null)
    {
        var files = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(path))
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                if (keep != null && !keep(rel)) continue;
                string full = f;
                files[rel] = () => File.ReadAllBytes(full);
            }
            return files;
        }
        using var zip = ZipFile.OpenRead(path);
        foreach (var e in zip.Entries)
        {
            string name = e.FullName.Replace('\\', '/');
            if (name.EndsWith('/') || keep != null && !keep(name)) continue;
            // Never carry paths that would point outside the pack.
            if (name.StartsWith('/') || name.Split('/').Any(part => part == "..")) { notes?.Add($"Skipped \"{name}\": its path leads outside the pack."); continue; }
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var data = ms.ToArray();
            files[name] = () => data;
        }
        return files;
    }

    static int Depth(string path) => path.Count(c => c == '/');
    static string Dir(string path) => path[..(path.LastIndexOf('/') + 1)];
    static string? Shallowest(IEnumerable<string> paths, string fileName) =>
        paths.Where(p => p.Equals(fileName, StringComparison.OrdinalIgnoreCase) || p.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)).OrderBy(Depth).FirstOrDefault();

    /// <summary>Reads a pack from a folder or file; the kind is found from what is inside.</summary>
    public static TexturePack Read(string path, TextureNames names)
    {
        string name = Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\', '/')) : Path.GetFileNameWithoutExtension(path);
        var notes = new List<string>();
        bool jar = File.Exists(path) && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        var files = Files(path, jar ? p => p.StartsWith("assets/minecraft/textures/") || p.StartsWith("textures/") || p is "version.json" or "pack.png" or "terrain.png" : null, notes);
        var keys = files.Keys.ToList();
        string? manifest = Shallowest(keys, "manifest.json"), mcmeta = Shallowest(keys, "pack.mcmeta"), packTxt = Shallowest(keys, "pack.txt");
        TexturePack pack;
        // Game jars before 1.6 keep their textures at the top (terrain.png up to 1.4.7, textures/blocks in 1.5).
        if (jar && !keys.Any(k => k.StartsWith("assets/minecraft/textures/")) && (files.ContainsKey("terrain.png") || keys.Any(k => k.StartsWith("textures/blocks/"))))
        {
            pack = ReadOldJava(files, "", names, name);
            pack.Name = "Minecraft " + name;
            pack.Description = $"The textures of Java Edition {name}";
            pack.OtherFiles.Clear();
        }
        else if (manifest != null && (mcmeta == null || Depth(manifest) <= Depth(mcmeta))) pack = ReadBedrock(files, Dir(manifest), names, name);
        else if (mcmeta != null) pack = ReadJava(files, Dir(mcmeta), names, name, null);
        else if (jar) pack = ReadJava(files, "", names, name, JarVersion(files) ?? name);
        else if (packTxt != null) pack = ReadOldJava(files, Dir(packTxt), names, name);
        else if (keys.Any(k => k.Contains("assets/minecraft/textures/", StringComparison.OrdinalIgnoreCase)))
        {
            string k0 = keys.First(k => k.Contains("assets/minecraft/textures/", StringComparison.OrdinalIgnoreCase));
            pack = ReadJava(files, k0[..k0.IndexOf("assets/minecraft/textures/", StringComparison.OrdinalIgnoreCase)], names, name, null);
            pack.Notes.Add("The pack has no pack.mcmeta; its version was guessed from its folders.");
        }
        else if (Shallowest(keys, "terrain.png") is { } terrain) pack = ReadOldJava(files, Dir(terrain), names, name);
        else throw new InvalidDataException("This is not a texture pack: there is no pack.mcmeta (Java), manifest.json (Bedrock) or pack.txt (old Java) in it.");
        pack.Notes.InsertRange(0, notes);
        return pack;
    }

    static string? JarVersion(Dictionary<string, Func<byte[]>> files)
    {
        if (!files.TryGetValue("version.json", out var read)) return null;
        try
        {
            using var doc = JsonDocument.Parse(read(), Lenient);
            return doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : doc.RootElement.TryGetProperty("id", out var i) ? i.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
