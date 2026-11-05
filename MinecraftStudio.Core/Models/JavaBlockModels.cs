// MinecraftStudio.Core/Models/JavaBlockModels.cs: Java Edition block and item models (models/*.json) — read from a pack
// and from the game installed on this PC (never bundled), resolved through their parents, written back, and turned into
// a mesh with the textures they name.
// Owns: JavaModelFace, JavaModelElement (one box: from/to in sixteenths, a turn about one axis, its faces), JavaBlockModel
// (one file as read; everything it holds that is not edited here — display, overrides, gui_light … — kept as it was),
// ResolvedJavaModel (the textures merged down the parents, the elements of the nearest model that has any, a flat item
// for item/generated, nothing for builtin/entity), JavaModelLibrary (models by id: the pack's own first, then the game's)
// and JavaModelMesh (elements → ModelQuads with the game's default UVs, face turns and element turns with rescale,
// tinted faces in plains colours, the textures used side by side in one picture).
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Rendering;

namespace MinecraftStudio.Core.Models;

/// <summary>One face of a model box: the texture variable (#side) or id, the part of it (uv, sixteenths), a quarter-turn count.</summary>
public sealed class JavaModelFace
{
    public string Texture { get; set; } = "#all";
    /// <summary>u1, v1, u2, v2 in sixteenths of the texture; null: the game's default for the box's size and place.</summary>
    public double[]? Uv { get; set; }
    /// <summary>0, 90, 180 or 270: the texture turned clockwise on the face.</summary>
    public int Rotation { get; set; }
    public int? TintIndex { get; set; }
    public string? CullFace { get; set; }

    public JavaModelFace Clone() => new() { Texture = Texture, Uv = Uv?.ToArray(), Rotation = Rotation, TintIndex = TintIndex, CullFace = CullFace };
}

/// <summary>One box of a model, in sixteenths of a block (0–16; the game allows −16 to 32).</summary>
public sealed class JavaModelElement
{
    public double[] From { get; set; } = [0, 0, 0];
    public double[] To { get; set; } = [16, 16, 16];
    /// <summary>"x", "y" or "z", or null for no turn.</summary>
    public string? Axis { get; set; }
    /// <summary>−45 to 45 in steps of 22.5 (the game's own rule).</summary>
    public double Angle { get; set; }
    public double[] Origin { get; set; } = [8, 8, 8];
    public bool Rescale { get; set; }
    public bool Shade { get; set; } = true;
    /// <summary>The faces it has, by side: down, up, north, south, west, east.</summary>
    public Dictionary<string, JavaModelFace> Faces { get; } = new(StringComparer.Ordinal);

    public static readonly string[] Sides = ["down", "up", "north", "south", "west", "east"];

    public JavaModelElement Clone()
    {
        var e = new JavaModelElement { From = From.ToArray(), To = To.ToArray(), Axis = Axis, Angle = Angle, Origin = Origin.ToArray(), Rescale = Rescale, Shade = Shade };
        foreach (var (side, f) in Faces) e.Faces[side] = f.Clone();
        return e;
    }

    /// <summary>What is wrong with it for the game, or null: sizes within −16…32, an allowed turn, known sides.</summary>
    public string? Problem()
    {
        if (From.Concat(To).Concat(Origin).Any(v => v < -16 || v > 32 || double.IsNaN(v))) return "a corner is outside −16 to 32 (the game refuses the model)";
        if (Axis != null && Axis is not ("x" or "y" or "z")) return $"the turn's axis \"{Axis}\" is not x, y or z";
        if (Axis != null && !(Angle is -45 or -22.5 or 0 or 22.5 or 45)) return $"the turn {Angle.ToString(CultureInfo.InvariantCulture)}° is not −45, −22.5, 0, 22.5 or 45 (the game refuses the model)";
        if (Faces.Values.Any(f => f.Rotation is not (0 or 90 or 180 or 270))) return "a face is turned by something other than 0, 90, 180 or 270";
        return null;
    }
}

/// <summary>One model file as read: its parent, its own textures and elements, and the rest of its JSON kept as it was.</summary>
public sealed class JavaBlockModel
{
    public required string Id { get; init; }
    public string? Parent { get; set; }
    public Dictionary<string, string> Textures { get; } = new(StringComparer.Ordinal);
    /// <summary>Its own elements, or null when it takes its parent's.</summary>
    public List<JavaModelElement>? Elements { get; set; }
    /// <summary>The file's JSON (display, overrides, ambientocclusion … stay as they were when written back).</summary>
    public JsonObject Json { get; init; } = [];
    /// <summary>Read from the pack (else the game's own).</summary>
    public bool FromPack { get; init; }

    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    /// <summary>Reads a model file; throws InvalidDataException for one that is not a model.</summary>
    public static JavaBlockModel Read(string id, byte[] json, bool fromPack)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json, NodeOptions, Lenient) as JsonObject ?? throw new InvalidDataException($"{id}: not a model (no JSON object)."); }
        catch (JsonException e) { throw new InvalidDataException($"{id}: the JSON can't be read ({e.Message})."); }
        // A value of the wrong kind (a number where a name goes …) is a model the game can't read either.
        try { return Parse(id, root, fromPack); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or JsonException)
        {
            throw new InvalidDataException($"{id}: {e.Message}");
        }
    }

    static JavaBlockModel Parse(string id, JsonObject root, bool fromPack)
    {
        var m = new JavaBlockModel { Id = id, Json = root, FromPack = fromPack, Parent = root["parent"]?.GetValue<string>() };
        if (root["textures"] is JsonObject tex)
            foreach (var (k, v) in tex)
                if (v is JsonValue jv && jv.TryGetValue(out string? s)) m.Textures[k] = s;
        if (root["elements"] is JsonArray elements)
        {
            m.Elements = [];
            foreach (var node in elements.OfType<JsonObject>()) m.Elements.Add(Element(node));
        }
        return m;
    }

    static double[] Vec(JsonNode? n, double[] fallback) =>
        n is JsonArray a && a.Count >= fallback.Length ? a.Take(fallback.Length).Select(x => x?.GetValue<double>() ?? 0).ToArray() : fallback.ToArray();

    static JavaModelElement Element(JsonObject o)
    {
        var e = new JavaModelElement { From = Vec(o["from"], [0, 0, 0]), To = Vec(o["to"], [16, 16, 16]), Shade = o["shade"]?.GetValue<bool>() ?? true };
        if (o["rotation"] is JsonObject r)
        {
            e.Axis = r["axis"]?.GetValue<string>();
            e.Angle = r["angle"]?.GetValue<double>() ?? 0;
            e.Origin = Vec(r["origin"], [8, 8, 8]);
            e.Rescale = r["rescale"]?.GetValue<bool>() ?? false;
        }
        if (o["faces"] is JsonObject faces)
            foreach (var (side, node) in faces)
                if (node is JsonObject f)
                    e.Faces[side] = new JavaModelFace
                    {
                        Texture = f["texture"]?.GetValue<string>() ?? "#missing",
                        Uv = f["uv"] is JsonArray uv && uv.Count >= 4 ? Vec(uv, [0, 0, 0, 0]) : null,
                        Rotation = f["rotation"]?.GetValue<int>() ?? 0,
                        TintIndex = f["tintindex"]?.GetValue<int>(),
                        CullFace = f["cullface"]?.GetValue<string>(),
                    };
        return e;
    }

    /// <summary>The model as JSON: the file's own JSON with the edited parent, textures and elements put back.</summary>
    public byte[] Write()
    {
        var root = (JsonObject)Json.DeepClone();
        if (Parent != null) root["parent"] = Parent; else root.Remove("parent");
        if (Textures.Count > 0)
        {
            var tex = new JsonObject();
            foreach (var (k, v) in Textures) tex[k] = v;
            root["textures"] = tex;
        }
        else root.Remove("textures");
        if (Elements != null)
        {
            var list = new JsonArray();
            foreach (var e in Elements)
            {
                var o = new JsonObject { ["from"] = Arr(e.From), ["to"] = Arr(e.To) };
                if (e.Axis != null)
                {
                    var r = new JsonObject { ["origin"] = Arr(e.Origin), ["axis"] = e.Axis, ["angle"] = Num(e.Angle) };
                    if (e.Rescale) r["rescale"] = true;
                    o["rotation"] = r;
                }
                if (!e.Shade) o["shade"] = false;
                var faces = new JsonObject();
                foreach (var side in JavaModelElement.Sides)
                {
                    if (!e.Faces.TryGetValue(side, out var f)) continue;
                    var fo = new JsonObject();
                    if (f.Uv != null) fo["uv"] = Arr(f.Uv);
                    fo["texture"] = f.Texture;
                    if (f.CullFace != null) fo["cullface"] = f.CullFace;
                    if (f.Rotation != 0) fo["rotation"] = f.Rotation;
                    if (f.TintIndex is int t) fo["tintindex"] = t;
                    faces[side] = fo;
                }
                o["faces"] = faces;
                list.Add(o);
            }
            root["elements"] = list;
        }
        else root.Remove("elements");
        return Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    static JsonNode Num(double v) => v == Math.Floor(v) && Math.Abs(v) < 1e9 ? JsonValue.Create((long)v) : JsonValue.Create(v);
    static JsonArray Arr(double[] v) => new(v.Select(Num).ToArray());
}

/// <summary>A model with its parents worked out: what the game draws.</summary>
public sealed class ResolvedJavaModel
{
    public required string Id { get; init; }
    /// <summary>The model and its parents, the model first.</summary>
    public required IReadOnlyList<JavaBlockModel> Chain { get; init; }
    /// <summary>Texture variables merged down the parents (the model's own win).</summary>
    public required Dictionary<string, string> Textures { get; init; }
    public required List<JavaModelElement> Elements { get; init; }
    /// <summary>The model the elements come from (null when none has any).</summary>
    public JavaBlockModel? ElementsFrom { get; init; }
    /// <summary>item/generated: a flat item made from its layer0, layer1 … pictures.</summary>
    public bool Generated { get; init; }
    /// <summary>builtin/entity: drawn by the game's own code (chests, banners, shields …).</summary>
    public bool BuiltIn { get; init; }

    /// <summary>The texture id a variable (#side) or id stands for, following references; null when it leads nowhere.</summary>
    public string? Texture(string name)
    {
        for (int i = 0; i < 16 && name.StartsWith('#'); i++)
            if (!Textures.TryGetValue(name[1..], out name!)) return null;
        return name.StartsWith('#') ? null : name;
    }
}

/// <summary>Models by id ("block/stone", "minecraft:item/apple"): the pack's own first, then the game's.</summary>
public sealed class JavaModelLibrary
{
    readonly Dictionary<string, Func<byte[]>> _pack, _game;
    readonly Dictionary<string, JavaBlockModel?> _read = new(StringComparer.Ordinal);
    Dictionary<string, List<string>>? _byTexture;

    /// <param name="packFiles">The pack's files by path in the pack (assets/…/models/…json).</param>
    /// <param name="gameFiles">The installed game's (a jar's) model files, or null when Java is not installed.</param>
    public JavaModelLibrary(IReadOnlyDictionary<string, byte[]> packFiles, Dictionary<string, Func<byte[]>>? gameFiles, string? gameVersion)
    {
        _pack = packFiles.Where(f => IsModel(f.Key)).ToDictionary(f => IdOf(f.Key)!, f => { var d = f.Value; return (Func<byte[]>)(() => d); }, StringComparer.Ordinal);
        _game = gameFiles?.Where(f => IsModel(f.Key)).ToDictionary(f => IdOf(f.Key)!, f => f.Value, StringComparer.Ordinal) ?? [];
        GameVersion = gameVersion;
    }

    /// <summary>The installed game's version the models come from, or null when none was found.</summary>
    public string? GameVersion { get; }

    static bool IsModel(string path) => IdOf(path) != null;

    /// <summary>"assets/minecraft/models/block/stone.json" → "block/stone" (other namespaces keep theirs: "mymod:block/x").</summary>
    public static string? IdOf(string path)
    {
        var p = path.Replace('\\', '/');
        if (!p.StartsWith("assets/", StringComparison.Ordinal) || !p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = p.Split('/');
        if (parts.Length < 4 || parts[2] != "models") return null;
        string rest = string.Join('/', parts[3..])[..^5];
        return parts[1] == "minecraft" ? rest : parts[1] + ":" + rest;
    }

    /// <summary>The path in a pack of a model id.</summary>
    public static string PathOf(string id)
    {
        string ns = "minecraft", rest = Normal(id);
        int colon = rest.IndexOf(':');
        if (colon >= 0) { ns = rest[..colon]; rest = rest[(colon + 1)..]; }
        return $"assets/{ns}/models/{rest}.json";
    }

    /// <summary>An id as the library keys it: without "minecraft:".</summary>
    public static string Normal(string id) => id.StartsWith("minecraft:", StringComparison.Ordinal) ? id["minecraft:".Length..] : id;

    /// <summary>Every model id there is, the pack's and the game's.</summary>
    public IEnumerable<string> Ids => _pack.Keys.Union(_game.Keys).OrderBy(k => k, StringComparer.Ordinal);

    public bool InPack(string id) => _pack.ContainsKey(Normal(id));

    /// <summary>The model as the pack has it, else the game's; null when there is none (or it can't be read).</summary>
    public JavaBlockModel? Get(string id)
    {
        id = Normal(id);
        lock (_read)
        {
            if (_read.TryGetValue(id, out var hit)) return hit;
            JavaBlockModel? m = null;
            try
            {
                if (_pack.TryGetValue(id, out var p)) m = JavaBlockModel.Read(id, p(), true);
                else if (_game.TryGetValue(id, out var g)) m = JavaBlockModel.Read(id, g(), false);
            }
            catch (InvalidDataException) { m = null; }
            return _read[id] = m;
        }
    }

    /// <summary>The game's own model (what "use the game's model again" brings back), not the pack's.</summary>
    public JavaBlockModel? GameModel(string id)
    {
        id = Normal(id);
        try { return _game.TryGetValue(id, out var g) ? JavaBlockModel.Read(id, g(), false) : null; }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>The pack now holds this model (as written), or no longer does (null).</summary>
    public void SetPackModel(string id, byte[]? json)
    {
        id = Normal(id);
        lock (_read)
        {
            if (json == null) _pack.Remove(id); else _pack[id] = () => json;
            _read.Clear();
            _byTexture = null;
        }
    }

    /// <summary>A model with its parents (16 deep at most; a loop stops it), or null when it isn't there.</summary>
    public ResolvedJavaModel? Resolve(string id, JavaBlockModel? replace = null)
    {
        var chain = new List<JavaBlockModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? at = Normal(id);
        bool generated = false, builtIn = false;
        while (at != null && chain.Count < 16 && seen.Add(at))
        {
            if (at is "builtin/generated" or "item/generated") generated = true;
            if (at == "builtin/entity") { builtIn = true; break; }
            if (at == "builtin/generated") break;
            var m = replace != null && chain.Count == 0 ? replace : Get(at);
            if (m == null) break;
            chain.Add(m);
            at = m.Parent == null ? null : Normal(m.Parent);
        }
        if (chain.Count == 0) return null;
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in chain.AsEnumerable().Reverse())
            foreach (var (k, v) in m.Textures) textures[k] = v;
        var from = chain.FirstOrDefault(m => m.Elements != null);
        return new ResolvedJavaModel
        {
            Id = Normal(id), Chain = chain, Textures = textures, Elements = from?.Elements ?? [], ElementsFrom = from,
            Generated = generated && from == null, BuiltIn = builtIn && from == null,
        };
    }

    /// <summary>The ids of the models whose textures (through their parents) use a texture id — the ones to show it on.</summary>
    public List<string> UsingTexture(string textureId, int max = 40)
    {
        string want = Normal(textureId);
        Dictionary<string, List<string>> index;
        lock (_read)
        {
            // Every model read once, by the textures it names itself (its children name theirs).
            if (_byTexture == null)
            {
                _byTexture = new(StringComparer.Ordinal);
                foreach (var id in Ids)
                    if (Get(id) is { } m)
                        foreach (var t in m.Textures.Values.Where(v => !v.StartsWith('#')).Select(Normal).Distinct())
                        {
                            if (!_byTexture.TryGetValue(t, out var list)) _byTexture[t] = list = [];
                            list.Add(id);
                        }
            }
            index = _byTexture;
        }
        var found = index.TryGetValue(want, out var ids) ? ids.Take(max).ToList() : [];
        // The model named like the texture first (block/stone for block/stone), blocks before items.
        return found.OrderBy(i => i == want ? 0 : 1).ThenBy(i => i.StartsWith("block/", StringComparison.Ordinal) ? 0 : 1).ThenBy(i => i.Length).ToList();
    }
}

/// <summary>A resolved model turned into faces for the 3D view, with its textures side by side in one picture.</summary>
public static class JavaModelMesh
{
    /// <param name="texture">The picture of a texture id ("block/stone"), its first frame; null when there is none.</param>
    public static (ModelMesh Mesh, RgbaImage Atlas, List<string> Missing) Build(ResolvedJavaModel model, Func<string, RgbaImage?> texture)
    {
        var missing = new List<string>();
        // The pictures used, tinted where a face asks for it, side by side.
        var slots = new Dictionary<(string Id, uint Tint), (int X, RgbaImage Img)>();
        int atlasW = 0, atlasH = 1;
        (int X, RgbaImage Img)? Slot(string id, uint tint)
        {
            if (slots.TryGetValue((id, tint), out var s)) return s;
            var img = texture(JavaModelLibrary.Normal(id));
            if (img == null) { if (!missing.Contains(id)) missing.Add(id); return null; }
            if (tint != 0xFFFFFF) img = Tinted(img, tint);
            s = (atlasW, img);
            atlasW += img.Width;
            atlasH = Math.Max(atlasH, img.Height);
            return slots[(id, tint)] = s;
        }
        uint tintOf = model.Id.Contains("leaves", StringComparison.Ordinal) || model.Id.Contains("vine", StringComparison.Ordinal) ? Biomes.Default.Foliage
            : model.Id.Contains("water", StringComparison.Ordinal) ? Biomes.Default.Water : Biomes.Default.Grass;
        var quads = new List<(ModelQuad Q, string Tex, uint Tint, double[] Uv)>();
        var elements = model.Generated ? Flat(model) : model.Elements;
        for (int ei = 0; ei < elements.Count; ei++)
        {
            var e = elements[ei];
            var f = new Vector3((float)e.From[0], (float)e.From[1], (float)e.From[2]);
            var t = new Vector3((float)e.To[0], (float)e.To[1], (float)e.To[2]);
            var lo = Vector3.Min(f, t);
            var hi = Vector3.Max(f, t);
            foreach (var (side, face) in e.Faces)
            {
                if (model.Texture(face.Texture) is not string tex) { if (!missing.Contains(face.Texture)) missing.Add(face.Texture); continue; }
                var uv = face.Uv ?? DefaultUv(side, lo, hi);
                var d = hi - lo;
                var (origin, u, v, normal) = side switch
                {
                    "down" => (new Vector3(lo.X, lo.Y, hi.Z), new Vector3(d.X, 0, 0), new Vector3(0, 0, -d.Z), -Vector3.UnitY),
                    "up" => (new Vector3(lo.X, hi.Y, lo.Z), new Vector3(d.X, 0, 0), new Vector3(0, 0, d.Z), Vector3.UnitY),
                    "north" => (new Vector3(hi.X, hi.Y, lo.Z), new Vector3(-d.X, 0, 0), new Vector3(0, -d.Y, 0), -Vector3.UnitZ),
                    "south" => (new Vector3(lo.X, hi.Y, hi.Z), new Vector3(d.X, 0, 0), new Vector3(0, -d.Y, 0), Vector3.UnitZ),
                    "west" => (new Vector3(lo.X, hi.Y, lo.Z), new Vector3(0, 0, d.Z), new Vector3(0, -d.Y, 0), -Vector3.UnitX),
                    "east" => (new Vector3(hi.X, hi.Y, hi.Z), new Vector3(0, 0, -d.Z), new Vector3(0, -d.Y, 0), Vector3.UnitX),
                    _ => (Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero),
                };
                if (normal == Vector3.Zero) continue;
                (origin, u, v, normal) = Turn(e, origin, u, v, normal);
                var q = new ModelQuad("element " + (ei + 1), ei, side, origin, u, v, normal, 0, 0, 0, 0, (face.Rotation / 90) & 3);
                quads.Add((q, tex, face.TintIndex != null ? tintOf : 0xFFFFFF, uv));
            }
        }
        // Now that every picture has its place, point each face at its part of the picture.
        var finished = new List<ModelQuad>();
        foreach (var (q, tex, tint, uv) in quads)
        {
            if (Slot(tex, tint) is not var (x, img)) continue;
            double sx = img.Width / 16.0, sy = Math.Min(img.Height, img.Width) / 16.0; // an animation strip: the first frame
            finished.Add(q with { TexU = (float)(x + uv[0] * sx), TexV = (float)(uv[1] * sy), TexW = (float)((uv[2] - uv[0]) * sx), TexH = (float)((uv[3] - uv[1]) * sy) });
        }
        var atlas = new RgbaImage(Math.Max(1, atlasW), atlasH);
        foreach (var (_, (x, img)) in slots)
            for (int y = 0; y < img.Height; y++)
                Array.Copy(img.Pixels, y * img.Width, atlas.Pixels, y * atlas.Width + x, img.Width);
        var entity = new EntityModel { Id = model.Id, TextureWidth = atlas.Width, TextureHeight = atlas.Height };
        var min = finished.Count == 0 ? Vector3.Zero : finished.SelectMany(Corners).Aggregate(Vector3.Min);
        var max = finished.Count == 0 ? new Vector3(16) : finished.SelectMany(Corners).Aggregate(Vector3.Max);
        return (new ModelMesh { Model = entity, Quads = finished, Min = min, Max = max }, atlas, missing);
    }

    static IEnumerable<Vector3> Corners(ModelQuad q) => [q.Origin, q.Origin + q.U, q.Origin + q.V, q.Origin + q.U + q.V];

    /// <summary>The part of the texture the game gives a face when the model doesn't say (sixteenths).</summary>
    public static double[] DefaultUv(string side, Vector3 lo, Vector3 hi) => side switch
    {
        "down" => [lo.X, 16 - hi.Z, hi.X, 16 - lo.Z],
        "up" => [lo.X, lo.Z, hi.X, hi.Z],
        "north" => [16 - hi.X, 16 - hi.Y, 16 - lo.X, 16 - lo.Y],
        "south" => [lo.X, 16 - hi.Y, hi.X, 16 - lo.Y],
        "west" => [lo.Z, 16 - hi.Y, hi.Z, 16 - lo.Y],
        _ => [16 - hi.Z, 16 - hi.Y, 16 - lo.Z, 16 - lo.Y],
    };

    // The element's turn about its origin (and the rescale that keeps a turned plane as wide as the block).
    static (Vector3 O, Vector3 U, Vector3 V, Vector3 N) Turn(JavaModelElement e, Vector3 o, Vector3 u, Vector3 v, Vector3 n)
    {
        if (e.Axis == null || e.Angle == 0) return (o, u, v, n);
        float a = (float)(e.Angle * Math.PI / 180);
        var turn = e.Axis switch { "x" => Matrix4x4.CreateRotationX(a), "y" => Matrix4x4.CreateRotationY(a), _ => Matrix4x4.CreateRotationZ(a) };
        float k = e.Rescale ? 1 / MathF.Cos(MathF.Abs(a)) : 1;
        var scale = e.Axis switch { "x" => new Vector3(1, k, k), "y" => new Vector3(k, 1, k), _ => new Vector3(k, k, 1) };
        var origin = new Vector3((float)e.Origin[0], (float)e.Origin[1], (float)e.Origin[2]);
        Vector3 P(Vector3 p) => Vector3.Transform(p - origin, turn) * scale + origin;
        var p0 = P(o);
        return (p0, P(o + u) - p0, P(o + v) - p0, Vector3.Normalize(Vector3.TransformNormal(n, turn)));
    }

    // item/generated: each layer's picture as a thin flat slab, front and back.
    static List<JavaModelElement> Flat(ResolvedJavaModel model)
    {
        var list = new List<JavaModelElement>();
        for (int i = 0; i < 5; i++)
        {
            if (!model.Textures.ContainsKey("layer" + i)) continue;
            var e = new JavaModelElement { From = [0, 0, 7.5 - i * 0.01], To = [16, 16, 8.5 + i * 0.01] };
            e.Faces["south"] = new JavaModelFace { Texture = "#layer" + i, Uv = [0, 0, 16, 16] };
            e.Faces["north"] = new JavaModelFace { Texture = "#layer" + i, Uv = [16, 0, 0, 16] };
            list.Add(e);
        }
        return list;
    }

    static RgbaImage Tinted(RgbaImage img, uint tint)
    {
        var copy = img.Clone();
        int tr = (int)(tint >> 16) & 255, tg = (int)(tint >> 8) & 255, tb = (int)tint & 255;
        for (int i = 0; i < copy.Pixels.Length; i++)
        {
            uint c = copy.Pixels[i];
            copy.Pixels[i] = RgbaImage.Argb(RgbaImage.A(c), RgbaImage.R(c) * tr / 255, RgbaImage.G(c) * tg / 255, RgbaImage.B(c) * tb / 255);
        }
        return copy;
    }
}
