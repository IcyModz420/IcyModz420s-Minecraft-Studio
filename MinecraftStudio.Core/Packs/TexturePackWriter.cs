// MinecraftStudio.Core/Packs/TexturePackWriter.cs: A pack made for one edition and version: the file, and an
// honest account of what is in it and what is not.
// Owns: PackResult, TexturePackWriter.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;

namespace MinecraftStudio.Core.Packs;

/// <summary>A pack made for one edition and version: the file, and an honest account of what is in it and what is not.</summary>
public sealed class PackResult
{
    public required string FileName { get; init; }
    public byte[] Data { get; set; } = [];
    /// <summary>Textures written (by shared name).</summary>
    public List<string> Written { get; } = [];
    /// <summary>Textures that do not exist in the target edition or version, with why.</summary>
    public List<string> NotIncluded { get; } = [];
    /// <summary>Things changed on the way (animations, sizes, renamed files …).</summary>
    public List<string> Notes { get; } = [];
    /// <summary>Problems found when the written pack was read back; empty means it holds exactly what was meant.</summary>
    public List<string> Problems { get; } = [];
}

/// <summary>
/// Writes a <see cref="TexturePack"/> for any Java release (resource pack, 1.5 texture pack or terrain.png before that)
/// or for Bedrock (.mcpack), then reads the result back and compares every texture, by pixels and by name.
/// </summary>
public static class TexturePackWriter
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>What the pack must hold at a path: the picture, and the texture it is (null where no name applies).</summary>
    sealed record Expect(RgbaImage Image, string? Key);

    // ------------------------------------------------------------------ Java

    /// <summary>
    /// A Java pack for one release line. Textures the release does not have are listed, not written. For terrain.png
    /// (Java up to 1.4.7) the whole atlas must be filled: tiles the pack does not change come from
    /// <paramref name="atlasFill"/> (the game's own textures, read from an installed jar), or stay empty with a note.
    /// </summary>
    /// <param name="targetMobs">The mob texture names the target game has (from its install), to list the pack's others; or null.</param>
    public static PackResult Java(TexturePack pack, JavaRelease target, TextureNames names, TexturePack? atlasFill = null, IReadOnlySet<string>? targetMobs = null)
    {
        var notInTarget = new List<string>();
        var result = new PackResult { FileName = SkinExport.SafeFileName(pack.Name) + $" ({target.Label}).zip" };
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var expected = new Dictionary<string, Expect>(StringComparer.Ordinal);
        string description = pack.Description.Length > 0 ? pack.Description : pack.Name;
        description += (description.EndsWith('.') ? " " : ". ") + PackInstaller.Signature + ".";
        // Textures the table knows (also old-only "java:…" ones), and the pack's own others.
        var known = pack.Textures.Values.Where(t => names.IsKnown(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();

        // Where each texture would go. Old versions used one file for several of today's textures: when a pack has
        // different pictures for them, the one the file is named after wins and the others are listed.
        var planned = new Dictionary<string, List<(PackTexture T, RgbaImage Image, int Score)>>(StringComparer.Ordinal);
        void Plan(string path, PackTexture t, RgbaImage image, string name)
        {
            string file = name[(name.LastIndexOf('/') + 1)..], own = t.Key[(t.Key.LastIndexOf('/') + 1)..];
            int score = (file.Equals(own, StringComparison.OrdinalIgnoreCase) ? 100 : 0) + names.Confidence(t.Key, "java", name);
            if (!planned.TryGetValue(path, out var list)) planned[path] = list = [];
            list.Add((t, image, score));
        }
        switch (target.Kind)
        {
            case JavaPackKind.ResourcePack:
                files["pack.mcmeta"] = pack.Edition == PackEdition.Java && pack.JavaRelease == target && pack.OriginalMetadata != null
                    ? pack.OriginalMetadata
                    : Encoding.UTF8.GetBytes(JavaReleases.PackSection(target, target, description).ToJsonString(Indented) + "\n");
                foreach (var t in known)
                {
                    var targetNames = names.JavaNames(t.Key, target);
                    if (targetNames.Count == 0) { result.NotIncluded.Add(NotIn(t.Key, target)); continue; }
                    foreach (var n in targetNames) Plan("assets/minecraft/textures/" + n + ".png", t, t.Image, n);
                    if (targetNames.Count > 1) result.Notes.Add($"{TextureNames.Display(t.Key)} was renamed within {target.Label}: it was saved under every name ({string.Join(", ", targetNames)}).");
                }
                break;
            case JavaPackKind.TexturePack:
                files["pack.txt"] = Encoding.UTF8.GetBytes(description + "\n");
                foreach (var t in known)
                {
                    var targetNames = names.JavaNames(t.Key, target);
                    if (targetNames.Count == 0) { result.NotIncluded.Add(NotIn(t.Key, target)); continue; }
                    if (t.Animation != null) result.Notes.Add($"{TextureNames.Display(t.Key)}: only the first frame was kept (this program writes no Java 1.5 animations).");
                    foreach (var n in targetNames) Plan("textures/" + n + ".png", t, t.FirstFrame(), n);
                }
                break;
            case JavaPackKind.TerrainAtlas:
                files["pack.txt"] = Encoding.UTF8.GetBytes(description + "\n");
                var atlas = BuildAtlas(known, target, names, atlasFill, result);
                if (atlas != null)
                {
                    files["terrain.png"] = atlas.ToPng();
                    expected["terrain.png"] = new Expect(atlas, null);
                }
                break;
        }
        var written = new HashSet<string>(StringComparer.Ordinal);
        var lostTo = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, candidates) in planned.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var best = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.T.Key, StringComparer.Ordinal).First();
            files[path] = best.Image.ToPng();
            // The read-back check expects the file's own texture where the file stands for several.
            expected[path] = new Expect(best.Image, candidates.Count == 1 ? best.T.Key : null);
            if (target.Kind == JavaPackKind.ResourcePack && TextureMcmeta(best.T) is { } meta) files[path + ".mcmeta"] = meta;
            written.Add(best.T.Key);
            foreach (var c in candidates.Where(c => c.T != best.T && !c.Image.SamePixels(best.Image)))
                lostTo.TryAdd(c.T.Key, TextureNames.Display(best.T.Key));
        }
        foreach (var t in known.Where(t => written.Contains(t.Key))) result.Written.Add(t.Key);
        foreach (var (key, winner) in lostTo.Where(l => !written.Contains(l.Key)))
            result.NotIncluded.Add($"{TextureNames.Display(key)}: in {target.Label} it shares one file with {winner}, whose picture was used");

        // Textures the table does not know ("java:…" from mods or custom models) go back where they came from when the
        // target uses the same folder names; Bedrock-only textures have no place in Java.
        foreach (var t in pack.Textures.Values.Where(t => !names.IsKnown(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (MobCatalog.IsMobKey(t.Key))
            {
                // Mob textures keep their path under textures/ (Java and Bedrock share it); resource packs have them from 1.6.
                if (target.Kind != JavaPackKind.ResourcePack) { result.NotIncluded.Add($"{t.Key}: Java 1.5 and older keep mob textures under other names"); continue; }
                string mobPath = "assets/minecraft/textures/" + t.Key + ".png";
                files[mobPath] = t.Image.ToPng();
                expected[mobPath] = new Expect(t.Image, null);
                if (TextureMcmeta(t) is { } mobMeta) files[mobPath + ".mcmeta"] = mobMeta;
                result.Written.Add(t.Key);
                if (targetMobs != null && !targetMobs.Contains(t.Key)) notInTarget.Add(t.Key);
                continue;
            }
            if (t.Key.StartsWith("bedrock:")) { result.NotIncluded.Add($"{TextureNames.Display(t.Key)}: only Bedrock has it"); continue; }
            if (!t.Key.StartsWith("java:")) continue;
            string rel = t.Key[5..];
            bool flat = rel.Split('/')[0] is "block" or "item";
            string path = "assets/minecraft/textures/" + rel + ".png";
            if (target.Kind != JavaPackKind.ResourcePack || flat != target.FlatNames || expected.ContainsKey(path))
            {
                result.NotIncluded.Add($"{TextureNames.Display(t.Key)}: not a texture of the game, and {target.Label} uses other folder names");
                continue;
            }
            files[path] = t.Image.ToPng();
            expected[path] = new Expect(t.Image, null);
            if (TextureMcmeta(t) is { } meta) files[path + ".mcmeta"] = meta;
            result.Written.Add(t.Key);
        }
        if (notInTarget.Count > 0) result.Notes.Add(MobNote(notInTarget, target.Label));
        if (result.Written.Count == 0 && pack.Textures.Count > 0)
            result.Problems.Add($"None of the pack's textures have a known name in {target.Label}" +
                (target.Kind == JavaPackKind.TexturePack || target.First == "1.6.1" ? ": Java 1.5 to 1.6.4 used older file names that this program does not know yet." : "."));

        if (pack.Icon != null) files["pack.png"] = pack.Icon.ToPng();
        // Other files (sounds, models, texts …) only make sense for the version the pack was made for.
        if (pack.Edition == PackEdition.Java && pack.JavaRelease == target)
        {
            foreach (var (path, data) in pack.OtherFiles) files.TryAdd(path, data);
        }
        else if (pack.OtherFiles.Count > 0)
        {
            result.Notes.Add($"{pack.OtherFiles.Count} other files of the pack (sounds, models, texts …) were left out: they are made for " +
                $"{(pack.Edition == PackEdition.Bedrock ? "Bedrock" : pack.JavaRelease?.Label ?? "another version")}, not {target.Label}.");
        }

        result.Data = Zip(files);
        Verify(result, expected, names, PackEdition.Java);
        return result;
    }

    /// <summary>
    /// A texture's .png.mcmeta: its own as read, with the animation section replaced by the current animation (or
    /// removed); a new one when it had none. Null when there is nothing to say.
    /// </summary>
    static byte[]? TextureMcmeta(PackTexture t)
    {
        JsonObject root;
        try { root = t.Mcmeta != null ? JsonNode.Parse(t.Mcmeta, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? [] : []; }
        catch (JsonException) { root = []; }
        root.Remove("animation");
        if (t.Animation != null) root["animation"] = Mcmeta(t.Animation)["animation"]!.DeepClone();
        return root.Count == 0 ? null : Encoding.UTF8.GetBytes(root.ToJsonString(Indented) + "\n");
    }

    static string NotIn(string key, JavaRelease target) =>
        key.StartsWith("bedrock:") ? $"{TextureNames.Display(key)}: only Bedrock has it" : $"{TextureNames.Display(key)}: not in {target.Label}";

    static JsonObject Mcmeta(TextureAnimation a)
    {
        var anim = new JsonObject();
        if (a.FrameTime != 1) anim["frametime"] = a.FrameTime;
        if (a.Interpolate) anim["interpolate"] = true;
        if (a.FrameWidth is int w) anim["width"] = w;
        if (a.FrameHeight is int h) anim["height"] = h;
        if (a.Frames != null)
        {
            var frames = new JsonArray();
            foreach (var (index, time) in a.Frames) frames.Add(time is int t ? new JsonObject { ["index"] = index, ["time"] = t } : JsonValue.Create(index));
            anim["frames"] = frames;
        }
        return new JsonObject { ["animation"] = anim };
    }

    /// <summary>terrain.png: 16 × 16 tiles, all the same size (the pack's most common size; others are scaled).</summary>
    static RgbaImage? BuildAtlas(List<PackTexture> textures, JavaRelease target, TextureNames names, TexturePack? fill, PackResult result)
    {
        var tiles = new Dictionary<int, RgbaImage>();
        foreach (var t in textures)
        {
            if (names.AtlasIndex(t.Key, target) is int i) { tiles[i] = t.FirstFrame(); result.Written.Add(t.Key); }
            else result.NotIncluded.Add($"{TextureNames.Display(t.Key)}: not in {target.Label}");
        }
        if (tiles.Count == 0)
        {
            result.Problems.Add($"None of the pack's textures exist in {target.Label}, so there is nothing to put in terrain.png.");
            return null;
        }
        int size = tiles.Values.GroupBy(t => t.Width).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        if (textures.Any(t => t.Animation != null && names.AtlasIndex(t.Key, target) != null))
            result.Notes.Add("terrain.png cannot animate: animated textures keep their first frame.");
        var atlas = new RgbaImage(size * 16, size * 16);
        int filled = 0, empty = 0;
        for (int i = 0; i < 256; i++)
        {
            RgbaImage? tile = tiles.GetValueOrDefault(i);
            if (tile == null && fill != null && names.FromAtlas(i, target) is { } key && fill.Textures.TryGetValue(key, out var f)) { tile = f.FirstFrame(); filled++; }
            if (tile == null) { empty++; continue; }
            if (tile.Width != size || tile.Height != size)
            {
                if (tiles.ContainsKey(i)) result.Notes.Add($"{TextureNames.Display(names.FromAtlas(i, target) ?? "tile " + i)} was {tile.Width} × {tile.Height}; it was scaled to {size} × {size} to fit the atlas.");
                tile = tile.Width > size ? Shrink(tile, size) : tile.Scaled(size, size);
            }
            atlas.CopyFrom(tile, 0, 0, size, size, i % 16 * size, i / 16 * size);
        }
        if (filled > 0) result.Notes.Add($"terrain.png replaces every block texture at once: {filled} tiles the pack does not change were filled with the game's own textures from {fill!.Name}.");
        if (empty > 0 && fill == null) result.Notes.Add($"{empty} tiles of terrain.png are empty because no game textures were found to fill them: blocks using them would look see-through.");
        return atlas;
    }

    static RgbaImage Shrink(RgbaImage img, int size)
    {
        while (img.Width / 2 >= size && img.Width % 2 == 0) img = img.Halved();
        return img.Width == size ? img : img.Scaled(size, size);
    }

    // ------------------------------------------------------------------ Bedrock

    /// <summary>
    /// A Bedrock resource pack (.mcpack). Animated textures keep their frames where the game animates that texture
    /// itself (<paramref name="bedrockAnimated"/>, read from the installed game), with the game's own timing; a Bedrock
    /// pack's own animation list is kept as it was. Elsewhere the first frame is kept, with a note.
    /// </summary>
    /// <param name="targetMobs">The mob texture names the installed Bedrock game has, to list the pack's others; or null.</param>
    public static PackResult Bedrock(TexturePack pack, TextureNames names, DateTime when, IReadOnlySet<string>? bedrockAnimated = null, IReadOnlySet<string>? targetMobs = null)
    {
        var notInTarget = new List<string>();
        var result = new PackResult { FileName = SkinExport.SafeFileName(pack.Name) + ".mcpack" };
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var expected = new Dictionary<string, Expect>(StringComparer.Ordinal);
        string headerId = SkinExport.NameUuid("resource pack header:" + pack.Name.ToLowerInvariant());
        string moduleId = SkinExport.NameUuid("resource pack module:" + pack.Name.ToLowerInvariant());
        var version = new JsonArray(when.Year, when.Month * 100 + when.Day, when.Hour * 10000 + when.Minute * 100 + when.Second);
        string description = pack.Description.Length > 0 ? pack.Description : pack.Name;
        var manifest = new JsonObject
        {
            ["format_version"] = 2,
            ["header"] = new JsonObject
            {
                ["name"] = pack.Name, ["description"] = description, ["uuid"] = headerId, ["version"] = version.DeepClone(),
                ["min_engine_version"] = new JsonArray(1, 16, 0),
            },
            ["modules"] = new JsonArray(new JsonObject { ["type"] = "resources", ["uuid"] = moduleId, ["version"] = version.DeepClone() }),
        };
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString(Indented) + "\n");
        // Saved as Bedrock again, a pack keeps its own manifest and id; its version goes up so the game takes it as
        // an update of the installed copy.
        if (pack.Edition == PackEdition.Bedrock && pack.OriginalMetadata != null && NewerManifest(pack.OriginalMetadata) is { } own) files["manifest.json"] = own;
        bool fromBedrock = pack.Edition == PackEdition.Bedrock;
        // Files the pack has under their own Bedrock name (its own inventory pictures, pictures Bedrock shares between
        // blocks, files of no known texture) are written as they are; nothing derived from another texture replaces them.
        var ownFiles = pack.Textures.Values.Where(t => t.Key.StartsWith("bedrock:") && !names.IsKnown(t.Key)).Select(t => t.Key[8..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int keptInventory = 0;
        var byLook = new List<string>();
        foreach (var t in pack.Textures.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (MobCatalog.IsMobKey(t.Key) && !names.IsKnown(t.Key))
            {
                // Mob textures keep their path under textures/ (Java and Bedrock share it). Bedrock does not animate them.
                // (Entity textures the name table knows — chests, conduits, pottery … — go by the table, below.)
                string mobPath = "textures/" + t.Key + ".png";
                var mob = t.FirstFrame();
                if (t.Animation != null) result.Notes.Add($"{t.Key}: Bedrock does not animate mob textures; the first frame was kept.");
                files[mobPath] = mob.ToPng();
                expected[mobPath] = new Expect(mob, null);
                result.Written.Add(t.Key);
                if (targetMobs != null && !targetMobs.Contains(t.Key)) notInTarget.Add(t.Key);
                continue;
            }
            if (!names.IsKnown(t.Key) && t.Key.StartsWith("java:")) { result.NotIncluded.Add($"{TextureNames.Display(t.Key)}: not a texture of the game"); continue; }
            if (t.Key.StartsWith("bedrock:") && !names.IsKnown(t.Key))
            {
                string path = "textures/" + t.Key[8..] + ".png";
                files[path] = t.Image.ToPng();
                expected[path] = new Expect(t.Image, null);
                result.Written.Add(t.Key);
                continue;
            }
            var targets = names.BedrockTargets(t.Key).Where(x => !ownFiles.Contains(x.Name)).ToList();
            bool wrote = false;
            foreach (var target in targets)
            {
                RgbaImage img;
                switch (target.Role)
                {
                    case "carried":
                        // The inventory picture: the block's texture times one colour where that is how Bedrock's own is
                        // made; otherwise Bedrock's own picture stays.
                        if (target.Tint is not uint tint) { keptInventory++; continue; }
                        img = Multiply(t.FirstFrame(), tint);
                        break;
                    case "opaque":
                        // Leaves for fast graphics: the same picture made solid, as Java draws them in fast mode.
                        img = Solid(t.FirstFrame());
                        break;
                    default:
                        img = t.Image;
                        if (t.Animation != null && !fromBedrock && !(bedrockAnimated?.Contains("textures/" + target.Name) ?? false))
                        {
                            img = t.FirstFrame();
                            result.Notes.Add($"{TextureNames.Display(t.Key)}: Bedrock does not animate textures/{target.Name}; the first frame was kept.");
                        }
                        if (target.Evidence == "uncertain") byLook.Add($"{TextureNames.Display(t.Key)} (textures/{target.Name})");
                        break;
                }
                string path = "textures/" + target.Name + ".png";
                files[path] = img.ToPng();
                expected[path] = new Expect(img, target.Role.Length > 0 ? null : t.Key);
                wrote = true;
            }
            if (wrote) result.Written.Add(t.Key);
            else result.NotIncluded.Add($"{TextureNames.Display(t.Key)}: Bedrock has no such texture");
        }
        if (keptInventory > 0)
            result.Notes.Add($"{keptInventory} inventory picture(s) keep Bedrock's own: Bedrock draws them differently from the block, so they cannot be made from it.");
        if (byLook.Count > 0)
            result.Notes.Add("Matched by how they look rather than by the game's block data, so check them in the game: " + string.Join(", ", byLook) + ".");
        if (pack.Icon != null) files["pack_icon.png"] = pack.Icon.ToPng();
        if (pack.Edition == PackEdition.Bedrock)
        {
            foreach (var (path, data) in pack.OtherFiles) files.TryAdd(path, data);
        }
        else if (pack.OtherFiles.Count > 0)
        {
            result.Notes.Add($"{pack.OtherFiles.Count} other files of the pack (sounds, models, texts …) were left out: they are made for Java, not Bedrock.");
        }
        if (notInTarget.Count > 0) result.Notes.Add(MobNote(notInTarget, "Bedrock"));
        if (result.Written.Count == 0 && pack.Textures.Count > 0) result.Problems.Add("None of the pack's textures exist in Bedrock.");
        result.Notes.Add("Turn the pack on in Minecraft: Settings, Global Resources (or a world's Resource Packs).");
        result.Data = Zip(files);
        Verify(result, expected, names, PackEdition.Bedrock);
        return result;
    }

    /// <summary>Each colour times a tint (how Bedrock pre-colours some inventory pictures).</summary>
    static RgbaImage Multiply(RgbaImage img, uint tint)
    {
        var r = img.Clone();
        int tr = RgbaImage.R(tint), tg = RgbaImage.G(tint), tb = RgbaImage.B(tint);
        for (int i = 0; i < r.Pixels.Length; i++)
        {
            uint c = r.Pixels[i];
            r.Pixels[i] = RgbaImage.Argb(RgbaImage.A(c), RgbaImage.R(c) * tr / 255, RgbaImage.G(c) * tg / 255, RgbaImage.B(c) * tb / 255);
        }
        return r;
    }

    /// <summary>Every pixel made fully opaque, keeping its colour.</summary>
    static RgbaImage Solid(RgbaImage img)
    {
        var r = img.Clone();
        for (int i = 0; i < r.Pixels.Length; i++) r.Pixels[i] |= 0xFF000000u;
        return r;
    }

    /// <summary>The manifest with the last number of the header's and every module's version raised by one, or null.</summary>
    static byte[]? NewerManifest(byte[] manifest)
    {
        try
        {
            if (JsonNode.Parse(manifest, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject root) return null;
            void Bump(JsonNode? node)
            {
                if (node?["version"] is JsonArray v && v.Count > 0 && v[^1] is JsonValue last && last.TryGetValue(out int n)) v[^1] = n + 1;
            }
            Bump(root["header"]);
            if (root["modules"] is JsonArray mods) foreach (var m in mods) Bump(m);
            return Encoding.UTF8.GetBytes(root.ToJsonString(Indented) + "\n");
        }
        catch (JsonException) { return null; }
    }

    // ------------------------------------------------------------------ shared

    static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, data) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                using var s = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    /// <summary>
    /// Reads the written pack back: every texture must hold the intended pixels, and its file name must lead back to
    /// the same texture; the metadata must parse.
    /// </summary>
    /// <summary>Mob textures the target game has no texture of that name for (it will not use them).</summary>
    static string MobNote(List<string> keys, string target) =>
        $"{keys.Count} mob texture(s) have names {target} does not use, so it will not show them: " + string.Join(", ", keys.Take(12)) + (keys.Count > 12 ? " …" : "") +
        ". (The editions name some mobs differently.)";

    static void Verify(PackResult result, Dictionary<string, Expect> expected, TextureNames names, PackEdition edition)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(result.Data), ZipArchiveMode.Read);
            foreach (var (path, want) in expected)
            {
                var e = zip.GetEntry(path);
                if (e == null) { result.Problems.Add($"{path} is missing from the pack."); continue; }
                using (var s = e.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    if (!Png.Decode(ms.ToArray()).SamePixels(want.Image)) result.Problems.Add($"{path} does not hold the intended pixels.");
                }
                if (want.Key == null) continue;
                string name = path.StartsWith("assets/minecraft/textures/") ? path["assets/minecraft/textures/".Length..^4] : path["textures/".Length..^4];
                string platform = edition == PackEdition.Java ? "java" : "bedrock";
                if (!names.Rows.Any(r => r.Platform == platform && r.Shared == want.Key && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    result.Problems.Add($"{path} would be read back as another texture.");
            }
            foreach (var m in new[] { "pack.mcmeta", "manifest.json" })
                if (zip.GetEntry(m) is { } meta)
                {
                    using var r = new StreamReader(meta.Open());
                    JsonDocument.Parse(r.ReadToEnd()).Dispose();
                }
            if (edition == PackEdition.Java && zip.GetEntry("pack.mcmeta") == null && zip.GetEntry("pack.txt") == null) result.Problems.Add("The pack has no pack.mcmeta or pack.txt.");
            if (edition == PackEdition.Bedrock && zip.GetEntry("manifest.json") == null) result.Problems.Add("The pack has no manifest.json.");
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            result.Problems.Add("The pack cannot be read back: " + e.Message);
        }
    }
}
