// MinecraftStudio.Core/Packs/MobCatalog.Bedrock.cs: the mobs of the installed Bedrock game (Windows PC).
// Part of MobCatalog (main part in MobCatalog.cs).
// Owns: reading the game's vanilla packs (loose files and the "__brarchive" archives, later packs over earlier ones,
// the way the game stacks them): every client entity (its textures and models), every model file, every texture
// under textures/entity, and the English names. A texture takes its entity's only model, or the model named like the
// texture, else the default one.
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.Models;

namespace MinecraftStudio.Core.Packs;

public sealed partial class MobCatalog
{
    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The installed Bedrock game's mobs, or null when the game is not installed.</summary>
    public static MobCatalog? Bedrock(string? dataFolder = null)
    {
        dataFolder ??= GameFolders.BedrockGameData();
        if (dataFolder == null || !Directory.Exists(Path.Combine(dataFolder, "resource_packs"))) return null;
        var layers = Directory.EnumerateDirectories(Path.Combine(dataFolder, "resource_packs"), "vanilla*")
            .Select(d => (Dir: d, Version: Path.GetFileName(d) == "vanilla" ? "0.0" : Path.GetFileName(d)["vanilla_".Length..]))
            .Where(l => VersionOrder.Key(l.Version) != null)
            .OrderBy(l => l.Version, Comparer<string>.Create(VersionOrder.Compare))
            .ToList();
        var merged = new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        // Model files of every layer, oldest first: a newer definition of a model replaces an older one by its id.
        var modelLayers = new List<(string Name, Func<byte[]> Read)>();
        var notes = new List<string>();
        foreach (var (dir, _) in layers)
        {
            foreach (var (path, read) in BrArchive.PackFiles(dir, notes))
            {
                if (path.StartsWith("models/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    modelLayers.Add((Path.GetFileName(dir) + "/" + path, read));
                    continue;
                }
                if (!(path.StartsWith("entity/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("models/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("animations/", StringComparison.OrdinalIgnoreCase) ||
                      path.StartsWith("textures/entity/", StringComparison.OrdinalIgnoreCase) || path.Equals("texts/en_US.lang", StringComparison.OrdinalIgnoreCase))) continue;
                // A PNG in a later layer replaces a TGA of the same texture in an earlier one, and the other way round.
                if (path.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                {
                    string stem = path[..^Path.GetExtension(path).Length];
                    merged.Remove(stem + ".png");
                    merged.Remove(stem + ".tga");
                }
                merged[path] = read;
            }
        }
        var catalog = FromFiles(merged, PackEdition.Bedrock, "Bedrock Edition (Windows PC)", modelLayers);
        catalog.Notes.InsertRange(0, notes);
        return catalog;
    }

    /// <summary>
    /// Mobs from a Bedrock resource pack's files (the game's, or a pack's own: an add-on brings its entities, models and
    /// textures the same way). Paths are relative to the pack.
    /// </summary>
    /// <param name="modelLayers">Model files of several stacked packs, oldest first (else the models among <paramref name="files"/>).</param>
    public static MobCatalog FromFiles(IReadOnlyDictionary<string, Func<byte[]>> files, PackEdition edition, string source, IEnumerable<(string Name, Func<byte[]> Read)>? modelLayers = null)
    {
        var catalog = new MobCatalog { Edition = edition, Source = source };
        catalog.Textures.Edition = edition;
        catalog.Textures.Name = source;
        var models = EntityModels.Read((modelLayers ?? files.Where(f => f.Key.StartsWith("models/", StringComparison.OrdinalIgnoreCase) && f.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(f => (f.Key, f.Value))).Select(f => (f.Item1, Safe(f.Item2))), catalog.Notes);
        foreach (var (id, m) in models) catalog.Models[id] = m;
        var animationFiles = files.Where(f => f.Key.StartsWith("animations/", StringComparison.OrdinalIgnoreCase) && f.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(f => (f.Key, Safe(f.Value))).ToList();
        foreach (var (id, pose) in ModelPose.Read(animationFiles)) catalog.Poses[id] = pose;
        foreach (var (id, anim) in EntityAnimations.Read(animationFiles, catalog.Notes)) catalog.Animations[id] = anim;

        var lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (files.TryGetValue("texts/en_US.lang", out var readLang))
            foreach (var line in Encoding.UTF8.GetString(Safe(readLang)).Split('\n'))
            {
                string l = line.Trim('\r', ' ', '﻿');
                int eq = l.IndexOf('=');
                if (eq <= 0 || l.StartsWith('#')) continue;
                string value = l[(eq + 1)..];
                int comment = value.IndexOf("\t#", StringComparison.Ordinal);
                lang[l[..eq]] = (comment >= 0 ? value[..comment] : value).Trim();
            }
        string? Name(string entityId) => lang.GetValueOrDefault($"entity.{entityId[(entityId.IndexOf(':') + 1)..]}.name");

        // Each client entity: which texture uses which model. Older definitions kept for old content ("cow.v1.0.entity.json")
        // come first, so the current one decides for the textures both use.
        static bool Older(string path) => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"\.v\d+(\.\d+)*\.entity\.json$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var (path, read) in files.Where(f => f.Key.StartsWith("entity/", StringComparison.OrdinalIgnoreCase) && f.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => Older(f.Key) ? 0 : 1).ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(Safe(read), Lenient);
                if (!doc.RootElement.TryGetProperty("minecraft:client_entity", out var ce) || !ce.TryGetProperty("description", out var d)) continue;
                string id = d.TryGetProperty("identifier", out var idv) && idv.ValueKind == JsonValueKind.String ? idv.GetString() ?? "" : "";
                var textures = Map(d, "textures");
                var geometry = Map(d, "geometry");
                var animations = Map(d, "animations");
                string? setup = animations.GetValueOrDefault("setup");
                if (id.Length > 0 && animations.Count > 0)
                {
                    if (!catalog.AnimationsOf.TryGetValue(id, out var names)) catalog.AnimationsOf[id] = names = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var (name, animId) in animations) names[name] = animId;
                }
                if (id.Length > 0)
                {
                    if (!catalog.ModelsOf.TryGetValue(id, out var list)) catalog.ModelsOf[id] = list = [];
                    foreach (var g in geometry.Values) if (catalog.Models.ContainsKey(g) && !list.Contains(g, StringComparer.OrdinalIgnoreCase)) list.Add(g);
                }
                string mob = Name(id) ?? Pretty(id[(id.IndexOf(':') + 1)..]);
                foreach (var (name, texPath) in textures)
                {
                    if (!texPath.StartsWith("textures/" + Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    string key = texPath["textures/".Length..].Replace('\\', '/');
                    string? geo = geometry.Count == 1 ? geometry.Values.First() : GeometryFor(name, geometry);
                    if (geo != null && !catalog.Models.ContainsKey(geo)) geo = null;
                    // A texture used by several entities belongs to the one it is filed under (entity/cow/… to the cow).
                    if (catalog.Info.TryGetValue(key, out var had) && had.GeometryId != null &&
                        !key.Contains("/" + id[(id.IndexOf(':') + 1)..] + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    string variant = textures.Count == 1 || name == "default" ? "" : Pretty(name);
                    catalog.Info[key] = new MobTexture(key, mob, variant, id, geo, setup);
                }
            }
            catch (JsonException e) { catalog.Notes.Add($"{path}: {e.Message.Split('\n')[0]}"); }
        }

        // The pictures (entity textures no entity file names stay editable, filed under their folder).
        foreach (var (path, read) in files.Where(f => f.Key.StartsWith("textures/" + Prefix, StringComparison.OrdinalIgnoreCase)))
        {
            string ext = Path.GetExtension(path);
            if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".tga", StringComparison.OrdinalIgnoreCase)) continue;
            string key = path["textures/".Length..^ext.Length].Replace('\\', '/');
            if (IsMaterialMap(key) || Decode(path, Safe(read), catalog.Notes) is not { } img) continue;
            catalog.Textures.Textures[key] = new PackTexture { Key = key, Image = img, SourcePath = path };
            if (!catalog.Info.ContainsKey(key)) catalog.Info[key] = Guess(key, folder => lang.GetValueOrDefault($"entity.{folder}.name"));
        }
        // Entities whose textures are not in these files keep no entry.
        foreach (var key in catalog.Info.Keys.Where(k => !catalog.Textures.Textures.ContainsKey(k)).ToList()) catalog.Info.Remove(key);
        return catalog;
    }

    /// <summary>
    /// Which of an entity's models a texture of that name uses: the model of the same name, else one its name starts with
    /// ("baby_warm" → "baby"), else one its name has as a word, else the default.
    /// </summary>
    static string? GeometryFor(string texture, Dictionary<string, string> geometry)
    {
        if (geometry.TryGetValue(texture, out var same)) return same;
        var words = texture.Split('_', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (key, id) in geometry.OrderByDescending(g => g.Key.Length))
            if (key != "default" && texture.StartsWith(key + "_", StringComparison.OrdinalIgnoreCase)) return id;
        foreach (var (key, id) in geometry.OrderByDescending(g => g.Key.Length))
            if (key != "default" && words.Contains(key, StringComparer.OrdinalIgnoreCase)) return id;
        return geometry.GetValueOrDefault("default") ?? geometry.Values.FirstOrDefault();
    }

    /// <summary>Material maps of Bedrock's texture sets (metalness, emissive, roughness, normals, height), not pictures to paint.</summary>
    public static bool IsMaterialMap(string key) =>
        key.EndsWith("_mers", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_mer", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("_normal", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_heightmap", StringComparison.OrdinalIgnoreCase);

    static byte[] Safe(Func<byte[]> read)
    {
        try { return read(); }
        catch (IOException) { return []; }
    }

    static Dictionary<string, string> Map(JsonElement description, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (description.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } v) map[p.Name] = v;
        return map;
    }
}
