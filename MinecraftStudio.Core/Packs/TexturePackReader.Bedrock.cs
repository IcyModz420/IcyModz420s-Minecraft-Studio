// MinecraftStudio.Core/Packs/TexturePackReader.Bedrock.cs: bedrock for TexturePackReader.
// Part of TexturePackReader (main part in TexturePackReader.cs).
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Packs;

public static partial class TexturePackReader
{
    // ------------------------------------------------------------------ Bedrock

    static readonly string[] BedrockTextureFolders = ["textures/blocks/", "textures/items/"];

    /// <summary>A Bedrock resource pack.</summary>
    static TexturePack ReadBedrock(Dictionary<string, Func<byte[]>> files, string root, TextureNames names, string fallbackName)
    {
        var pack = new TexturePack { Edition = PackEdition.Bedrock, Name = fallbackName };
        var lang = ReadLang(files, root + "texts/en_US.lang");
        try
        {
            using var doc = JsonDocument.Parse(files[root + "manifest.json"](), Lenient);
            pack.OriginalMetadata = files[root + "manifest.json"]();
            var header = doc.RootElement.GetProperty("header");
            string Resolve(string s) => lang.TryGetValue(s, out var v) ? v : s;
            if (header.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nm) pack.Name = Resolve(nm);
            if (header.TryGetProperty("description", out var d) && d.GetString() is { } desc) pack.Description = Resolve(desc);
            bool resources = doc.RootElement.TryGetProperty("modules", out var mods) && mods.ValueKind == JsonValueKind.Array &&
                             mods.EnumerateArray().Any(m => m.TryGetProperty("type", out var t) && t.GetString() == "resources");
            if (!resources) pack.Notes.Add("The manifest has no resources module: this may be a behaviour or skin pack rather than a texture pack.");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            pack.Notes.Add("manifest.json could not be read: " + e.Message);
        }
        ReadIcon(files, root + "pack_icon.png", pack);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root + "manifest.json", root + "pack_icon.png" };
        var flipbooks = ReadFlipbooks(files, root + "textures/flipbook_textures.json", pack.Notes);
        // The animation list stays among the other files too, so a Bedrock pack saved as Bedrock keeps it exactly.
        AddBedrockTextures(files, root, names, pack, flipbooks, used);
        foreach (var (path, read) in files)
            if (!used.Contains(path) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) pack.OtherFiles[path[root.Length..]] = read();
        return pack;
    }

    static void AddBedrockTextures(IReadOnlyDictionary<string, Func<byte[]>> files, string root, TextureNames names, TexturePack pack,
        Dictionary<string, TextureAnimation> flipbooks, HashSet<string>? used)
    {
        // PNG before TGA when a pack has both of one file.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, read) in files.OrderBy(f => f.Key.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            string rel = path[root.Length..];
            bool png = rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase), tga = rel.EndsWith(".tga", StringComparison.OrdinalIgnoreCase);
            if ((!png && !tga) || !rel.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)) continue;
            string name = rel["textures/".Length..^4];
            // Block and item textures, and other files the table links to Java's (the block-breaking stages …).
            if (!BedrockTextureFolders.Any(f => rel.StartsWith(f, StringComparison.OrdinalIgnoreCase)) && !names.IsKnownBedrock(name)) continue;
            used?.Add(path);
            if (!seen.Add(name)) continue;
            RgbaImage img;
            try { img = png ? Png.Decode(read()) : Tga.Decode(read()); }
            catch (InvalidDataException e) { pack.Notes.Add($"{rel} could not be read ({e.Message}); it was left out."); continue; }
            var anim = flipbooks.TryGetValue("textures/" + name, out var a) ? a.Clone() : null;
            void Put(string key) => pack.Textures[key] = new PackTexture { Key = key, Image = img, SourcePath = name, Animation = anim };
            // Inventory and opaque pictures, files two textures claim equally, and files of no known texture keep their
            // own Bedrock name: they go back into Bedrock packs as they are, and are never taken for a block's texture.
            string? shared = names.FromBedrock(name);
            if (shared == null) { Put("bedrock:" + name); continue; }
            if (!pack.Textures.TryGetValue(shared, out var have)) { Put(shared); continue; }
            // Bedrock reuses a few pictures for several files of one texture: the texture's own main file holds it, the
            // other keeps its own name so a Bedrock pack keeps both as they were.
            string? main = names.BedrockTargets(shared).FirstOrDefault()?.Name;
            if (main != null && main.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                pack.Textures["bedrock:" + have.SourcePath] = new PackTexture { Key = "bedrock:" + have.SourcePath, Image = have.Image, SourcePath = have.SourcePath, Animation = have.Animation };
                Put(shared);
            }
            else Put("bedrock:" + name);
        }
    }

    /// <summary>Bedrock's flipbook_textures.json: animated textures by path ("textures/blocks/…", no extension).</summary>
    public static Dictionary<string, TextureAnimation> ReadFlipbooks(IReadOnlyDictionary<string, Func<byte[]>> files, string path, List<string> notes)
    {
        var map = new Dictionary<string, TextureAnimation>(StringComparer.OrdinalIgnoreCase);
        if (!files.TryGetValue(path, out var read)) return map;
        try
        {
            using var doc = JsonDocument.Parse(read(), Lenient);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("flipbook_texture", out var tex) || tex.GetString() is not { Length: > 0 } t) continue;
                var anim = new TextureAnimation
                {
                    FrameTime = Int(e, "ticks_per_frame") is int tp && tp > 0 ? tp : 1,
                    // Bedrock blends frames unless told not to.
                    Interpolate = !(e.TryGetProperty("blend_frames", out var b) && b.ValueKind == JsonValueKind.False),
                };
                if (e.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
                    anim.Frames = fr.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Number).Select(f => ((int)f.GetDouble(), (int?)null)).ToList();
                map[t] = anim;
            }
        }
        catch (JsonException e) { notes.Add("flipbook_textures.json could not be read: " + e.Message); }
        return map;
    }

    static Dictionary<string, string> ReadLang(Dictionary<string, Func<byte[]>> files, string path)
    {
        var map = new Dictionary<string, string>();
        if (!files.TryGetValue(path, out var read)) return map;
        foreach (var line in Encoding.UTF8.GetString(read()).Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith('#')) continue;
            string value = line[(eq + 1)..].TrimEnd('\r');
            int c = value.IndexOf("\t#", StringComparison.Ordinal);
            map[line[..eq].Trim()] = (c >= 0 ? value[..c] : value).Trim();
        }
        return map;
    }

    /// <summary>
    /// The installed Bedrock game's own textures: its vanilla pack and every versioned pack after it (vanilla_1.14 …),
    /// newest last, the way the game stacks them. Returns null when the game is not installed.
    /// </summary>
    public static TexturePack? ReadBedrockGame(TextureNames names, string? dataFolder = null)
    {
        dataFolder ??= GameFolders.BedrockGameData();
        if (dataFolder == null) return null;
        string packs = Path.Combine(dataFolder, "resource_packs");
        var layers = Directory.EnumerateDirectories(packs, "vanilla*")
            .Select(d => (Dir: d, Version: Path.GetFileName(d) == "vanilla" ? "0.0" : Path.GetFileName(d)["vanilla_".Length..]))
            .Where(l => VersionOrder.Key(l.Version) != null)
            .OrderBy(l => l.Version, Comparer<string>.Create(VersionOrder.Compare))
            .ToList();
        var pack = new TexturePack { Edition = PackEdition.Bedrock, Name = "Minecraft Bedrock", Description = "The textures of the installed Bedrock game" };
        // The layers' files as the game sees them (a later layer replaces a file of an earlier one), then read once.
        var flipbooks = new Dictionary<string, TextureAnimation>(StringComparer.OrdinalIgnoreCase);
        var merged = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, _) in layers)
        {
            var problems = new List<string>();
            var files = BrArchive.PackFiles(dir, problems);
            foreach (var p in problems) pack.Notes.Add(p);
            foreach (var (k, v) in ReadFlipbooks(files, "textures/flipbook_textures.json", pack.Notes)) flipbooks[k] = v;
            foreach (var (path, read) in files)
            {
                if (!path.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)) continue;
                // A PNG in a later layer replaces a TGA of the same file in an earlier one, and the other way round.
                string stem = path[..^Path.GetExtension(path).Length];
                foreach (var ext in new[] { ".png", ".tga" }) merged.Remove(stem + ext);
                merged[path] = read;
            }
        }
        AddBedrockTextures(merged, "", names, pack, flipbooks, null);
        return pack;
    }

    /// <summary>The textures of the installed Bedrock game that it animates (flipbook paths, "textures/blocks/…").</summary>
    public static HashSet<string> BedrockAnimated(string? dataFolder = null)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        dataFolder ??= GameFolders.BedrockGameData();
        if (dataFolder == null) return set;
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(dataFolder, "resource_packs"), "vanilla*"))
        {
            var files = BrArchive.PackFiles(dir);
            foreach (var k in ReadFlipbooks(files, "textures/flipbook_textures.json", []).Keys) set.Add(k);
        }
        return set;
    }
}
