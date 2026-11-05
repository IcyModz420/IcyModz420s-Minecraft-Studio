// MinecraftStudio.Core/Skins/SkinExport.cs: A file made from one or more skins, what had to change on the way,
// and the result of reading it back.
// Owns: SkinFile, SkinExport.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;

namespace MinecraftStudio.Core.Skins;

/// <summary>A file made from one or more skins, what had to change on the way, and the result of reading it back.</summary>
public sealed class SkinFile
{
    public required string FileName { get; init; }
    public required byte[] Data { get; init; }
    /// <summary>What changed or could not be kept (shown to the player before anything is saved).</summary>
    public List<string> Notes { get; } = [];
    /// <summary>Problems found when the written file was read back; empty means it holds exactly what was meant.</summary>
    public List<string> Problems { get; } = [];
}

/// <summary>
/// Skins for every edition: PNGs for Java (1.8+ and older), Bedrock skin packs (.mcpack), and Java packs that replace
/// the default skins. Every file is read back after it is made and compared with what it should hold.
/// </summary>
public static class SkinExport
{
    /// <summary>Bedrock takes 64 × 32, 64 × 64 and 128 × 128 skins.</summary>
    public const int BedrockMaxSize = 128;

    /// <summary>The nine default skins of Java 1.19.3 and newer (each has a wide and a slim version).</summary>
    public static readonly string[] JavaDefaultNames = ["alex", "ari", "efe", "kai", "makena", "noor", "steve", "sunny", "zuri"];

    static string ModelName(SkinModel m) => m == SkinModel.Slim ? "Slim (Alex, 3-pixel arms)" : "Classic (Steve, 4-pixel arms)";

    /// <summary>The skin's image at most <paramref name="size"/> wide, with a note if it had to be scaled down.</summary>
    static RgbaImage AtMost(Skin skin, int size, string why, List<string> notes)
    {
        if (skin.Image.Width <= size) return skin.Image.Clone();
        var c = skin.Copy();
        c.SetSize(size);
        notes.Add($"The {skin.Image.Width} × {skin.Image.Width} HD skin was scaled down to {size} × {size}: {why}. Fine details are averaged.");
        return c.Image;
    }

    // ------------------------------------------------------------------ PNGs

    /// <summary>Java Edition 1.8 and newer: a 64 × 64 PNG for the Minecraft Launcher's skin page or minecraft.net.</summary>
    public static SkinFile JavaPng(Skin skin)
    {
        var notes = new List<string>();
        var img = AtMost(skin, 64, "Java Edition only accepts 64 × 64 skins", notes);
        var file = new SkinFile { FileName = SafeFileName(skin.Name) + ".png", Data = img.ToPng() };
        file.Notes.AddRange(notes);
        file.Notes.Add($"A PNG does not store the arm shape. When you add it in the Minecraft Launcher (Skins, New skin) or on minecraft.net, choose {ModelName(skin.Model)}.");
        VerifyPng(file, img);
        return file;
    }

    /// <summary>Java Edition 1.7.10 and older (and Pocket and console versions from before 64 × 64 skins): a 64 × 32 PNG.</summary>
    public static SkinFile LegacyPng(Skin skin)
    {
        var notes = new List<string>();
        var img = AtMost(skin, 64, "64 × 32 skins are never HD", notes);
        var legacy = SkinConvert.ModernToLegacy(img, skin.Model, out var more);
        notes.AddRange(more);
        var file = new SkinFile { FileName = SafeFileName(skin.Name) + " (64x32).png", Data = legacy.ToPng() };
        file.Notes.AddRange(notes);
        VerifyPng(file, legacy);
        return file;
    }

    /// <summary>Bedrock Edition: a PNG for the Dressing Room's Classic Skins import (Windows, Android, iOS).</summary>
    public static SkinFile BedrockPng(Skin skin)
    {
        var notes = new List<string>();
        var img = AtMost(skin, BedrockMaxSize, "Bedrock takes skins up to 128 × 128", notes);
        var file = new SkinFile { FileName = SafeFileName(skin.Name) + ".png", Data = img.ToPng() };
        file.Notes.AddRange(notes);
        file.Notes.Add($"A PNG does not store the arm shape: choose {ModelName(skin.Model)} when the game asks. Xbox, PlayStation and Switch cannot import skin files; use a skin pack on Windows or a phone instead.");
        VerifyPng(file, img);
        return file;
    }

    static void VerifyPng(SkinFile file, RgbaImage expected)
    {
        try
        {
            if (!Png.Decode(file.Data).SamePixels(expected)) file.Problems.Add("The saved PNG does not hold the same pixels.");
        }
        catch (InvalidDataException e) { file.Problems.Add("The saved PNG cannot be read back: " + e.Message); }
    }

    // ------------------------------------------------------------------ Bedrock skin packs

    /// <summary>
    /// A Bedrock skin pack (.mcpack) holding the skins: manifest.json, skins.json and the names in texts/en_US.lang,
    /// laid out like the game's own skin packs. Its id comes from the pack name, so exporting a pack of the same
    /// name again gives an update that replaces the old one; <paramref name="when"/> sets the version number.
    /// </summary>
    public static SkinFile BedrockSkinPack(IReadOnlyList<Skin> skins, string packName, DateTime when)
    {
        if (skins.Count == 0) throw new ArgumentException("A skin pack needs at least one skin.");
        packName = CleanText(packName, "My Skins");
        var notes = new List<string>();
        string headerId = NameUuid("skin pack header:" + packName.ToLowerInvariant()), moduleId = NameUuid("skin pack module:" + packName.ToLowerInvariant());
        string packKey = Identifier(packName) + "_" + headerId[..6];
        var version = new JsonArray(when.Year, when.Month * 100 + when.Day, when.Hour * 10000 + when.Minute * 100 + when.Second);

        var manifest = new JsonObject
        {
            ["format_version"] = 2,
            ["header"] = new JsonObject { ["name"] = packName, ["uuid"] = headerId, ["version"] = version.DeepClone() },
            ["modules"] = new JsonArray(new JsonObject { ["type"] = "skin_pack", ["uuid"] = moduleId, ["version"] = version.DeepClone() }),
        };
        var list = new JsonArray();
        var lang = new StringBuilder();
        lang.Append("skinpack.").Append(packKey).Append('=').Append(packName).Append('\n');
        var textures = new List<(string File, RgbaImage Image, SkinModel Model, string Name)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skin in skins)
        {
            string key = Identifier(skin.Name);
            for (int n = 2; !used.Add(key); n++) key = Identifier(skin.Name) + "_" + n;
            var img = AtMost(skin, BedrockMaxSize, "Bedrock takes skins up to 128 × 128", notes);
            string name = CleanText(skin.Name, key);
            textures.Add((key + ".png", img, skin.Model, name));
            list.Add(new JsonObject
            {
                ["localization_name"] = key,
                ["geometry"] = skin.Model == SkinModel.Slim ? "geometry.humanoid.customSlim" : "geometry.humanoid.custom",
                ["texture"] = key + ".png",
                ["type"] = "free",
            });
            lang.Append("skin.").Append(packKey).Append('.').Append(key).Append('=').Append(name).Append('\n');
        }
        var skinsJson = new JsonObject { ["skins"] = list, ["serialize_name"] = packKey, ["localization_name"] = packKey };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "manifest.json", Json(manifest));
            Add(zip, "skins.json", Json(skinsJson));
            Add(zip, "texts/en_US.lang", Encoding.UTF8.GetBytes(lang.ToString()));
            Add(zip, "texts/languages.json", Encoding.UTF8.GetBytes("[\n  \"en_US\"\n]\n"));
            foreach (var t in textures) Add(zip, t.File, t.Image.ToPng());
        }
        var file = new SkinFile { FileName = SafeFileName(packName) + ".mcpack", Data = ms.ToArray() };
        file.Notes.AddRange(notes.Distinct());
        file.Notes.Add("Skin packs work on Windows, Android and iOS: they appear in the Dressing Room under Classic Skins. Xbox, PlayStation and Switch cannot import them.");

        // Read it back the way an importer would.
        try
        {
            var back = ReadBedrockSkinPack(file.Data, out var readNotes);
            if (back.Count != textures.Count) file.Problems.Add($"The pack holds {back.Count} skins instead of {textures.Count}.");
            for (int i = 0; i < Math.Min(back.Count, textures.Count); i++)
            {
                var (name, skin) = back[i];
                if (!skin.Image.SamePixels(textures[i].Image)) file.Problems.Add($"\"{textures[i].Name}\" does not hold the same pixels.");
                if (skin.Model != textures[i].Model) file.Problems.Add($"\"{textures[i].Name}\" has the wrong arm shape.");
                if (name != textures[i].Name) file.Problems.Add($"\"{textures[i].Name}\" is named \"{name}\" in the pack.");
            }
            file.Problems.AddRange(readNotes.Where(n => n.StartsWith("Problem:")));
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException)
        {
            file.Problems.Add("The pack cannot be read back: " + e.Message);
        }
        return file;
    }

    /// <summary>
    /// The skins in a Bedrock skin pack (.mcpack or .zip, with the pack at the top or in one folder), with their names
    /// from the pack's English texts. Skins drawn for other models than the standard ones are listed in the notes.
    /// </summary>
    public static List<(string Name, Skin Skin)> ReadBedrockSkinPack(byte[] zipData, out List<string> notes)
    {
        notes = [];
        using var zip = new ZipArchive(new MemoryStream(zipData), ZipArchiveMode.Read);
        var manifest = zip.Entries.Where(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName.Count(c => c == '/')).FirstOrDefault()
            ?? throw new InvalidDataException("There is no manifest.json in this file, so it is not a Bedrock pack.");
        string root = manifest.FullName[..^manifest.Name.Length];
        byte[] Read(string path)
        {
            var e = zip.GetEntry(root + path) ?? zip.Entries.FirstOrDefault(x => x.FullName.Equals(root + path, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"\"{path}\" is missing from the pack.");
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        using var m = JsonDocument.Parse(Read("manifest.json"), JsonOptions);
        bool isSkinPack = m.RootElement.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array &&
                          modules.EnumerateArray().Any(x => x.TryGetProperty("type", out var t) && t.GetString() == "skin_pack");
        if (!isSkinPack) throw new InvalidDataException("This pack is not a skin pack (its manifest has no skin_pack module).");
        using var doc = JsonDocument.Parse(Read("skins.json"), JsonOptions);
        string packKey = doc.RootElement.TryGetProperty("localization_name", out var ln) ? ln.GetString() ?? "" : "";
        var names = ReadLang(zip, root);
        var result = new List<(string, Skin)>();
        foreach (var s in doc.RootElement.GetProperty("skins").EnumerateArray())
        {
            string key = s.TryGetProperty("localization_name", out var k) ? k.GetString() ?? "" : "";
            string texture = s.GetProperty("texture").GetString() ?? "";
            string geometry = s.TryGetProperty("geometry", out var g) ? g.GetString() ?? "" : "";
            string name = names.TryGetValue($"skin.{packKey}.{key}", out var n) ? n : key;
            byte[] data;
            try { data = Read(texture); }
            catch (KeyNotFoundException) { notes.Add($"Problem: \"{name}\" uses \"{texture}\", which is missing."); continue; }
            SkinModel? model = geometry switch
            {
                "geometry.humanoid.customSlim" => SkinModel.Slim,
                "geometry.humanoid.custom" or "geometry.humanoid" or "geometry.humanoid.customStandard" => SkinModel.Classic,
                _ => null,
            };
            if (model == null) notes.Add($"\"{name}\" uses its own model ({geometry}); it was loaded on the standard body, so parts may not line up.");
            try { result.Add((name, Skin.Load(data, model, name))); }
            catch (InvalidDataException e) { notes.Add($"Problem: \"{name}\" cannot be loaded: {e.Message}"); }
        }
        return result;
    }

    static Dictionary<string, string> ReadLang(ZipArchive zip, string root)
    {
        var map = new Dictionary<string, string>();
        var e = zip.Entries.FirstOrDefault(x => x.FullName.Equals(root + "texts/en_US.lang", StringComparison.OrdinalIgnoreCase));
        if (e == null) return map;
        using var r = new StreamReader(e.Open(), Encoding.UTF8);
        while (r.ReadLine() is { } line)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith('#')) continue;
            string value = line[(eq + 1)..];
            int comment = value.IndexOf("\t#", StringComparison.Ordinal);
            if (comment >= 0) value = value[..comment];
            map[line[..eq].Trim()] = value.Trim();
        }
        return map;
    }

    // ------------------------------------------------------------------ Java default-skin packs

    /// <summary>
    /// A Java pack that replaces the default skins, so the skin shows in single player and on LAN without an account
    /// (every player without a skin of their own gets it). Covers the releases <paramref name="from"/> to
    /// <paramref name="to"/>: a texture pack (mob/char.png) up to 1.5.2, a resource pack from 1.6.1.
    /// </summary>
    public static SkinFile JavaDefaultSkinPack(Skin skin, JavaRelease from, JavaRelease to, string packName)
    {
        if (to.Index < from.Index) (from, to) = (to, from);
        bool texturePack = from.Kind != JavaPackKind.ResourcePack;
        if (texturePack != (to.Kind != JavaPackKind.ResourcePack))
            throw new ArgumentException("Up to 1.5.2 Java uses texture packs and from 1.6.1 resource packs: make one pack for each.");
        if (!from.ModernSkins && to.ModernSkins)
            throw new ArgumentException("Up to 1.7.10 Java reads 64 × 32 skins and from 1.8 64 × 64 ones from the same file: make one pack for each.");
        packName = CleanText(packName, "My Skin");
        var notes = new List<string>();
        var img = AtMost(skin, 64, "Java Edition only reads 64 × 64 skins", notes);
        var files = new List<(string Path, RgbaImage Image)>();
        if (!from.ModernSkins)
        {
            var legacy = SkinConvert.ModernToLegacy(img, skin.Model, out var more);
            notes.AddRange(more);
            files.Add((texturePack ? "mob/char.png" : "assets/minecraft/textures/entity/steve.png", legacy));
        }
        else
        {
            var classic = skin.Model == SkinModel.Classic ? img : SkinConvert.SlimToClassic(img);
            var slim = skin.Model == SkinModel.Slim ? img : SkinConvert.ClassicToSlim(img);
            notes.Add(skin.Model == SkinModel.Classic
                ? "Players the game gives slim arms see a slim copy of the skin: one column of each arm is left out."
                : "Players the game gives classic arms see a widened copy of the skin: the middle column of each arm is doubled.");
            if (!to.NineDefaultSkins || !from.NineDefaultSkins)
            {
                files.Add(("assets/minecraft/textures/entity/steve.png", classic));
                files.Add(("assets/minecraft/textures/entity/alex.png", slim));
            }
            if (to.NineDefaultSkins)
                foreach (var n in JavaDefaultNames)
                {
                    files.Add(($"assets/minecraft/textures/entity/player/wide/{n}.png", classic));
                    files.Add(($"assets/minecraft/textures/entity/player/slim/{n}.png", slim));
                }
        }
        string description = $"{packName}: replaces the default skins. {PackInstaller.Signature}.";
        var icon = Icon(img);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (texturePack) Add(zip, "pack.txt", Encoding.UTF8.GetBytes(description + "\n"));
            else Add(zip, "pack.mcmeta", Json(JavaReleases.PackSection(from, to, description)));
            Add(zip, "pack.png", icon.ToPng());
            foreach (var (path, image) in files) Add(zip, path, image.ToPng());
        }
        var file = new SkinFile { FileName = SafeFileName(packName) + (texturePack ? " (texture pack).zip" : ".zip"), Data = ms.ToArray() };
        file.Notes.AddRange(notes.Distinct());
        file.Notes.Add(texturePack
            ? "Put it in the texturepacks folder and pick it under Mods and Texture Packs."
            : "Put it in the resourcepacks folder and turn it on under Options, Resource Packs. It changes how players without a skin of their own look, which is you when you play offline.");

        // Read it back: the metadata parses and every skin file holds the intended pixels.
        try
        {
            using var zip = new ZipArchive(new MemoryStream(file.Data), ZipArchiveMode.Read);
            if (!texturePack)
            {
                using var meta = JsonDocument.Parse(ReadEntry(zip, "pack.mcmeta"));
                if (!meta.RootElement.TryGetProperty("pack", out var pack) || !pack.TryGetProperty("description", out _))
                    file.Problems.Add("pack.mcmeta has no pack section.");
            }
            foreach (var (path, image) in files)
                if (!Png.Decode(ReadEntry(zip, path)).SamePixels(image)) file.Problems.Add($"{path} does not hold the same pixels.");
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException)
        {
            file.Problems.Add("The pack cannot be read back: " + e.Message);
        }
        return file;
    }

    /// <summary>A pack icon: the skin's face with its hat, 8 × 8 texels drawn at 16 pixels each.</summary>
    public static RgbaImage Icon(RgbaImage skin)
    {
        int s = skin.Width / 64;
        var face = new RgbaImage(8 * s, 8 * s);
        for (int y = 0; y < 8 * s; y++)
            for (int x = 0; x < 8 * s; x++)
                face[x, y] = SkinConvert.Blend(skin[8 * s + x, 8 * s + y] | 0xFF000000u, skin[40 * s + x, 8 * s + y]);
        return face.Scaled(128, 128);
    }

    // ------------------------------------------------------------------ helpers

    static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    static byte[] Json(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

    static void Add(ZipArchive zip, string path, byte[] data)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(data);
    }

    static byte[] ReadEntry(ZipArchive zip, string path)
    {
        var e = zip.GetEntry(path) ?? throw new KeyNotFoundException($"\"{path}\" is missing.");
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Letters, digits and underscores only (Bedrock text keys), never starting with a digit.</summary>
    public static string Identifier(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.Normalize(NormalizationForm.FormKD))
        {
            if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_' && (char.IsWhiteSpace(c) || c is '_' or '-' or '.')) sb.Append('_');
        }
        string id = sb.ToString().Trim('_');
        if (id.Length == 0 || char.IsDigit(id[0])) id = "Skin" + id;
        return id.Length > 40 ? id[..40] : id;
    }

    /// <summary>Text for names: one line, no control characters, never empty.</summary>
    static string CleanText(string text, string fallback)
    {
        var t = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return t.Length == 0 ? fallback : t.Length > 80 ? t[..80] : t;
    }

    /// <summary>A name that is valid as a file name on Windows.</summary>
    public static string SafeFileName(string name) => IO.SafeName.For(CleanText(name, "skin"), "skin");

    /// <summary>A UUID made from a name (RFC 4122 version 5 style), so the same name always gives the same id.</summary>
    public static string NameUuid(string name)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes("minecraft-studio:" + name));
        hash[6] = (byte)(hash[6] & 0x0F | 0x50);
        hash[8] = (byte)(hash[8] & 0x3F | 0x80);
        string h = Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        return $"{h[..8]}-{h[8..12]}-{h[12..16]}-{h[16..20]}-{h[20..32]}";
    }
}
