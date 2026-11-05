// MinecraftStudio.Core/Packs/TexturePackReader.Java.cs: java for TexturePackReader.
// Part of TexturePackReader (main part in TexturePackReader.cs).
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Packs;

public static partial class TexturePackReader
{
    // ------------------------------------------------------------------ Java

    /// <summary>A Java resource pack (or a game jar when <paramref name="jarVersion"/> is given).</summary>
    static TexturePack ReadJava(Dictionary<string, Func<byte[]>> files, string root, TextureNames names, string fallbackName, string? jarVersion)
    {
        var pack = new TexturePack { Edition = PackEdition.Java, Name = fallbackName };
        if (jarVersion != null)
        {
            pack.JavaRelease = JavaReleases.Of(jarVersion);
            pack.Name = "Minecraft " + jarVersion;
            pack.Description = $"The textures of Java Edition {jarVersion}";
        }
        else if (files.TryGetValue(root + "pack.mcmeta", out var meta))
        {
            try
            {
                pack.OriginalMetadata = meta();
                using var doc = JsonDocument.Parse(pack.OriginalMetadata, Lenient);
                if (doc.RootElement.TryGetProperty("pack", out var p))
                {
                    if (p.TryGetProperty("description", out var d)) pack.Description = PlainText(d);
                    int? format = Format(p, "max_format") ?? Format(p, "pack_format") ?? Format(p, "min_format");
                    if (format is int f) pack.JavaRelease = JavaReleases.All.LastOrDefault(r => r.Kind == JavaPackKind.ResourcePack && r.Major == f)
                        ?? JavaReleases.All.LastOrDefault(r => r.Kind == JavaPackKind.ResourcePack && r.Major <= f);
                }
            }
            catch (JsonException e) { pack.Notes.Add("pack.mcmeta could not be read: " + e.Message); }
        }
        ReadIcon(files, root + "pack.png", pack);

        string texRoot = root + "assets/minecraft/textures/";
        bool flatFolders = files.Keys.Any(k => k.StartsWith(texRoot + "block/", StringComparison.OrdinalIgnoreCase) || k.StartsWith(texRoot + "item/", StringComparison.OrdinalIgnoreCase));
        // Without a pack format, the folder names tell the era.
        pack.JavaRelease ??= flatFolders ? JavaReleases.Newest : JavaReleases.All[JavaReleases.Index("1.12")] ?? JavaReleases.All[JavaReleases.Index("1.11")];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, read) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!path.StartsWith(texRoot, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            string rel = path[texRoot.Length..^4];
            string folder = rel.Split('/')[0].ToLowerInvariant();
            // Block and item textures, and other textures the table links to Bedrock's (decorated pot patterns …).
            if (!TextureFolders.Contains(folder) && !names.IsKnownJava(rel)) continue;
            used.Add(path);
            RgbaImage img;
            try { img = Png.Decode(read()); }
            catch (InvalidDataException e) { pack.Notes.Add($"{rel}.png could not be read ({e.Message}); it was left out."); continue; }
            TextureAnimation? anim = null;
            string? mcmetaText = null;
            if (files.TryGetValue(path + ".mcmeta", out var mc))
            {
                used.Add(path + ".mcmeta");
                var mcData = mc();
                anim = ParseMcmeta(mcData, pack.Notes, rel);
                mcmetaText = Encoding.UTF8.GetString(mcData);
            }
            var keys = names.FromJavaAll(rel, pack.JavaRelease);
            if (keys.Count == 0)
            {
                keys = ["java:" + rel];
                pack.Notes.Add($"{rel} is not a texture of the game: it is kept for Java versions with the same folder names.");
            }
            bool flat = folder is "block" or "item";
            foreach (var key in keys)
            {
                // One old file can stand for several of today's textures; each gets it, unless the pack also has a
                // file that is that texture's own name. A pack for several versions can hold old and new names of one
                // texture: the one for the pack's own version wins.
                if (pack.Textures.TryGetValue(key, out var existing))
                {
                    bool existingFlat = existing.SourcePath.Split('/')[0] is "block" or "item";
                    bool ownName(string n) => n[(n.LastIndexOf('/') + 1)..].Equals(key[(key.LastIndexOf('/') + 1)..], StringComparison.OrdinalIgnoreCase);
                    bool better = existingFlat != flat ? flat == (pack.JavaRelease?.FlatNames ?? true) : ownName(rel) && !ownName(existing.SourcePath);
                    if (!better) continue;
                }
                pack.Textures[key] = new PackTexture { Key = key, Image = img, Animation = anim, SourcePath = rel, Mcmeta = mcmetaText };
            }
        }
        foreach (var (path, read) in files)
        {
            if (used.Contains(path) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            string rel = path[root.Length..];
            if (rel.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase) || rel.Equals("pack.png", StringComparison.OrdinalIgnoreCase)) continue;
            pack.OtherFiles[rel] = read();
        }
        return pack;
    }

    static int? Format(JsonElement pack, string name)
    {
        if (!pack.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 && v[0].TryGetInt32(out int j)) return j;
        return null;
    }

    /// <summary>A text component (a string, a list, or an object with "text"/"translate" and "extra") as plain text.</summary>
    public static string PlainText(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Array => string.Concat(e.EnumerateArray().Select(PlainText)),
        JsonValueKind.Object => (e.TryGetProperty("text", out var t) ? PlainText(t) : e.TryGetProperty("translate", out var tr) ? tr.GetString() ?? "" : "")
                               + (e.TryGetProperty("extra", out var x) ? PlainText(x) : ""),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => e.ToString(),
        _ => "",
    };

    /// <summary>The "animation" section of a .png.mcmeta file.</summary>
    public static TextureAnimation? ParseMcmeta(byte[] data, List<string> notes, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(data, Lenient);
            if (!doc.RootElement.TryGetProperty("animation", out var a) || a.ValueKind != JsonValueKind.Object) return null;
            var anim = new TextureAnimation();
            if (Int(a, "frametime") is int t && t > 0) anim.FrameTime = t;
            if (a.TryGetProperty("interpolate", out var ip) && ip.ValueKind is JsonValueKind.True or JsonValueKind.False) anim.Interpolate = ip.GetBoolean();
            if (Int(a, "width") is int w && w > 0) anim.FrameWidth = w;
            if (Int(a, "height") is int h && h > 0) anim.FrameHeight = h;
            if (a.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
            {
                anim.Frames = [];
                foreach (var f in fr.EnumerateArray())
                {
                    if (f.ValueKind == JsonValueKind.Number && f.TryGetDouble(out double d)) anim.Frames.Add(((int)d, null));
                    else if (f.ValueKind == JsonValueKind.Object && Int(f, "index") is int idx) anim.Frames.Add((idx, Int(f, "time")));
                }
            }
            return anim;
        }
        catch (JsonException e)
        {
            notes.Add($"{name}.png.mcmeta could not be read ({e.Message}): the texture is treated as not animated.");
            return null;
        }
    }

    static int? Int(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? (int)d : null;

    static void ReadIcon(Dictionary<string, Func<byte[]>> files, string path, TexturePack pack)
    {
        if (!files.TryGetValue(path, out var read)) return;
        try { pack.Icon = Png.Decode(read()); }
        catch (InvalidDataException) { pack.Notes.Add($"{Path.GetFileName(path)} could not be read; the pack has no icon."); }
    }

    /// <summary>Java 1.5 (one file per block in textures/blocks) and older (terrain.png) texture packs.</summary>
    static TexturePack ReadOldJava(Dictionary<string, Func<byte[]>> files, string root, TextureNames names, string fallbackName)
    {
        var pack = new TexturePack { Edition = PackEdition.Java, Name = fallbackName };
        if (files.TryGetValue(root + "pack.txt", out var txt)) pack.Description = Encoding.UTF8.GetString(txt()).Trim();
        ReadIcon(files, root + "pack.png", pack);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root + "pack.txt", root + "pack.png" };
        bool perFile = files.Keys.Any(k => k.StartsWith(root + "textures/blocks/", StringComparison.OrdinalIgnoreCase));
        if (perFile)
        {
            pack.JavaRelease = JavaReleases.All[JavaReleases.Index("1.5")];
            foreach (var (path, read) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                if (!path.StartsWith(root + "textures/", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = path[(root.Length + "textures/".Length)..^4];
                if (rel.Split('/')[0].ToLowerInvariant() is not ("blocks" or "items")) continue;
                used.Add(path);
                try
                {
                    var img = Png.Decode(read());
                    string key = names.FromJava(rel, pack.JavaRelease) ?? "java:" + rel;
                    if (key.StartsWith("java:")) pack.Notes.Add($"{rel} is not a known texture of Java 1.5; it is kept only for 1.5.");
                    pack.Textures[key] = new PackTexture { Key = key, Image = img, SourcePath = rel };
                }
                catch (InvalidDataException e) { pack.Notes.Add($"{rel}.png could not be read ({e.Message}); it was left out."); }
            }
        }
        else if (files.TryGetValue(root + "terrain.png", out var terrain))
        {
            used.Add(root + "terrain.png");
            pack.JavaRelease = JavaReleases.All[0];
            try
            {
                var atlas = Png.Decode(terrain());
                if (atlas.Width != atlas.Height || atlas.Width % 16 != 0) throw new InvalidDataException($"terrain.png is {atlas.Width} × {atlas.Height}; it should be a square grid of 16 × 16 tiles.");
                int tile = atlas.Width / 16, unknown = 0;
                for (int i = 0; i < 256; i++)
                {
                    var img = atlas.Crop(i % 16 * tile, i / 16 * tile, tile, tile);
                    if (img.AllTransparent(0, 0, tile, tile)) continue;
                    if (names.FromAtlas(i, pack.JavaRelease) is { } key) pack.Textures[key] = new PackTexture { Key = key, Image = img, SourcePath = $"terrain.png tile {i}" };
                    else unknown++;
                }
                if (unknown > 0) pack.Notes.Add($"{unknown} tiles of terrain.png are not linked to a block texture (unused tiles or blocks of no later version); they were left out.");
            }
            catch (InvalidDataException e) { pack.Notes.Add("terrain.png could not be read: " + e.Message); }
        }
        foreach (var (path, read) in files)
            if (!used.Contains(path) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) pack.OtherFiles[path[root.Length..]] = read();
        return pack;
    }
}
