// MinecraftStudio.Core/Packs/MobCatalog.cs: every mob (and other entity: chests, beds, signs, boats …) texture of a
// game installed on this PC, with its name, its variant and the model it is drawn on.
// Owns: MobTexture and MobCatalog — the textures as a TexturePack keyed "entity/…" (the path under textures/, which
// Java and Bedrock share), the models by geometry id, and which model each texture uses. Reading the installed Bedrock
// game is in MobCatalog.Bedrock.cs, a Java game jar in MobCatalog.Java.cs. Nothing is bundled: all of it is read from the
// player's own installs at run time.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;

namespace MinecraftStudio.Core.Packs;

/// <summary>One entity texture: what it is called and which model draws it (null when there is none).</summary>
public sealed record MobTexture(string Key, string Mob, string Variant, string? EntityId, string? GeometryId, string? Setup = null)
{
    public string Display => Variant.Length == 0 ? Mob : $"{Mob} — {Variant}";
}

/// <summary>The entity textures and models of one installed game.</summary>
public sealed partial class MobCatalog
{
    public const string Prefix = "entity/";

    public required PackEdition Edition { get; init; }
    /// <summary>Which game it was read from ("Java Edition 1.21.11", "Bedrock Edition (this PC)").</summary>
    public required string Source { get; init; }
    public TexturePack Textures { get; init; } = new();
    public Dictionary<string, EntityModel> Models { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MobTexture> Info { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Every model each entity has (adult, baby, sheared …), by entity id.</summary>
    public Dictionary<string, List<string>> ModelsOf { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Every animation, by id ("animation.cow.setup").</summary>
    public Dictionary<string, EntityAnimation> Animations { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Each entity's animations by the names it gives them ("walk" → "animation.quadruped.walk"), by entity id.</summary>
    public Dictionary<string, Dictionary<string, string>> AnimationsOf { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Rest poses from the entities' setup animations, by animation id.</summary>
    public Dictionary<string, ModelPose> Poses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Notes { get; } = [];
    /// <summary>The mob texture names of the other edition's installed game (to point out names only one edition has), or null.</summary>
    public HashSet<string>? OtherEdition { get; set; }

    public static bool IsMobKey(string key) => key.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>What a texture is, also for one the catalog does not know (a pack's own).</summary>
    public MobTexture Describe(string key) => Info.TryGetValue(key, out var m) ? m : Guess(key, null);

    /// <summary>The model a texture is drawn on (from the given geometry id when chosen by hand), or null.</summary>
    public EntityModel? ModelFor(string key, string? chosen = null)
    {
        if (chosen != null) return Models.GetValueOrDefault(chosen);
        return Describe(key).GeometryId is { } id ? Models.GetValueOrDefault(id) : null;
    }

    /// <summary>The animations of a texture's entity (its own names and the animation ids), controllers left out.</summary>
    public List<(string Name, string Id)> AnimationsFor(string key) =>
        Describe(key).EntityId is { } e && AnimationsOf.TryGetValue(e, out var map)
            ? map.Where(m => Animations.ContainsKey(m.Value)).Select(m => (m.Key, m.Value)).OrderBy(m => m.Key == "setup" ? 0 : 1).ThenBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>The rest pose a texture's entity gives its model (its setup animation), or null.</summary>
    public ModelPose? PoseFor(string key) => Describe(key).Setup is { } id ? Poses.GetValueOrDefault(id) : null;

    /// <summary>A name and variant from the path alone: "entity/cat/calico" → Cat, Calico.</summary>
    internal static MobTexture Guess(string key, Func<string, string?>? lang)
    {
        var parts = key[Prefix.Length..].Split('/');
        string folder = parts.Length > 1 ? parts[0] : parts[0];
        string file = parts[^1];
        string mob = lang?.Invoke(folder) ?? Pretty(folder);
        string variant = parts.Length > 1 && !file.Equals(folder, StringComparison.OrdinalIgnoreCase) ? Pretty(file) : "";
        return new MobTexture(key, mob, variant, null, null);
    }

    /// <summary>
    /// Takes a read pack's mob textures out of its other files and makes them editable textures keyed "entity/…" (Java:
    /// assets/minecraft/textures/entity, with any .png.mcmeta; Bedrock: textures/entity, PNG or TGA). Returns how many.
    /// </summary>
    public static int TakeMobTextures(TexturePack pack)
    {
        string root = pack.Edition == PackEdition.Java ? "assets/minecraft/textures/" : "textures/";
        int taken = 0;
        foreach (var path in pack.OtherFiles.Keys.Where(k => k.StartsWith(root + Prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            string ext = Path.GetExtension(path);
            if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".tga", StringComparison.OrdinalIgnoreCase)) continue;
            string key = path[root.Length..^ext.Length].Replace('\\', '/');
            if (IsMaterialMap(key) || pack.Textures.ContainsKey(key) || Decode(path, pack.OtherFiles[path], pack.Notes) is not { } img) continue;
            var t = new PackTexture { Key = key, Image = img, SourcePath = path };
            if (pack.OtherFiles.Remove(path + ".mcmeta", out var meta))
            {
                t.Mcmeta = System.Text.Encoding.UTF8.GetString(meta);
                t.Animation = TexturePackReader.ParseMcmeta(meta, pack.Notes, key);
            }
            pack.OtherFiles.Remove(path);
            pack.Textures[key] = t;
            taken++;
        }
        return taken;
    }

    internal static string Pretty(string name)
    {
        string n = name.Replace('_', ' ').Replace('-', ' ').Trim();
        return n.Length == 0 ? name : char.ToUpperInvariant(n[0]) + n[1..];
    }

    internal static RgbaImage? Decode(string path, byte[] data, List<string> notes)
    {
        try
        {
            if (Png.IsPng(data)) return Png.Decode(data);
            if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) return Tga.Decode(data);
        }
        catch (InvalidDataException e) { notes.Add($"{path}: {e.Message}"); }
        return null;
    }

    /// <summary>
    /// Gives this catalog's textures the models of another (Java textures get the installed Bedrock game's models): the
    /// same texture path first, otherwise a texture of the same mob and size. A model is only taken when it is made for a
    /// texture of the same shape (Java and Bedrock lay some mobs out differently): else another model of the same mob that
    /// is, or for a humanoid the player model; failing that the texture keeps no model (painting on the flat picture
    /// still works) rather than a wrong one.
    /// </summary>
    public void BorrowModels(MobCatalog from)
    {
        foreach (var (id, m) in from.Models) Models.TryAdd(id, m);
        foreach (var (id, pose) in from.Poses) Poses.TryAdd(id, pose);
        foreach (var (id, a) in from.Animations) Animations.TryAdd(id, a);
        foreach (var (id, map) in from.AnimationsOf) AnimationsOf.TryAdd(id, map);
        foreach (var key in Info.Keys.ToList())
        {
            var mine = Info[key];
            if (mine.GeometryId != null || !Textures.Textures.TryGetValue(key, out var t)) continue;
            var img = t.Image;
            bool Fits(string? geo) => geo != null && from.Models.TryGetValue(geo, out var m) && (long)m.TextureWidth * img.Height == (long)m.TextureHeight * img.Width;
            var lender = from.Info.GetValueOrDefault(key);
            string? geo = lender?.GeometryId;
            if (geo == null)
            {
                string folder = key[..(key.LastIndexOf('/') + 1)];
                lender = from.Info.Values
                    .Where(o => o.GeometryId != null && o.Key.StartsWith(folder, StringComparison.Ordinal) && from.Textures.Textures.TryGetValue(o.Key, out var ot) &&
                                ot.Image.Width == img.Width && ot.Image.Height == img.Height)
                    .OrderBy(o => o.Key.Length).FirstOrDefault();
                geo = lender?.GeometryId;
            }
            if (geo != null && !Fits(geo))
            {
                string? entity = lender?.EntityId;
                // A baby's model only for a baby's texture.
                bool baby = key.Contains("baby", StringComparison.OrdinalIgnoreCase);
                string? other = entity != null && from.ModelsOf.TryGetValue(entity, out var all)
                    ? all.FirstOrDefault(g => Fits(g) && g.Contains("baby", StringComparison.OrdinalIgnoreCase) == baby) : null;
                geo = other ?? Extended(geo, img) ?? (IsHumanoid(from.Models[geo]) && Fits("geometry.humanoid.custom") ? "geometry.humanoid.custom" : null);
            }
            if (geo == null) continue;
            Info[key] = mine with { GeometryId = geo, EntityId = mine.EntityId ?? lender?.EntityId, Setup = lender?.GeometryId == geo ? lender.Setup : null };
        }
    }

    /// <summary>
    /// For a texture that is a model's picture on a larger canvas (Java's 64 × 64 zombie is the 64 × 32 layout with an
    /// empty lower half): the model, read pixel for pixel on that canvas. Null when the texture is not like that.
    /// </summary>
    string? Extended(string geo, Imaging.RgbaImage img)
    {
        var m = Models[geo];
        if (img.Width % m.TextureWidth != 0) return null;
        int k = img.Width / m.TextureWidth;
        if (k < 1 || img.Height < m.TextureHeight * k || img.Height % k != 0) return null;
        string id = $"{geo}@{m.TextureWidth}x{img.Height / k}";
        if (!Models.ContainsKey(id))
            Models[id] = new EntityModel { Id = id, TextureWidth = m.TextureWidth, TextureHeight = img.Height / k, Bones = m.Bones };
        return id;
    }

    static bool IsHumanoid(EntityModel m) =>
        new[] { "head", "body", "rightArm", "leftArm", "rightLeg", "leftLeg" }.All(n => m.Bones.Any(b => b.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
}
