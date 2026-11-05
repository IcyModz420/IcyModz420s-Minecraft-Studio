// MinecraftStudio.Core/Models/EntityModel.cs: a mob's model as Bedrock describes it (its .geo.json), read from the
// player's own installed game or from a pack.
// Owns: EntityModel, ModelBone, ModelCube, ModelFaceUv and EntityModels (reading every form of geometry file: the old
// "geometry.name" objects with inheritance ("geometry.zombie:geometry.humanoid") and texturewidth, and the newer
// "minecraft:geometry" lists with description.texture_width; box UV and per-face UV; numbers written as text).
// Units are model pixels; y is up and the mob faces north (−z).
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace MinecraftStudio.Core.Models;

/// <summary>Where one face of a cube takes its texture from (in the model's texture units); a negative size is flipped.</summary>
public sealed record ModelFaceUv(float U, float V, float W, float H, int Rotation = 0);

/// <summary>A box of a bone: its corner and size, its own turn around a pivot, and how its faces are textured.</summary>
public sealed class ModelCube
{
    public Vector3 Origin { get; set; }
    public Vector3 Size { get; set; }
    public Vector3? Pivot { get; set; }
    public Vector3 Rotation { get; set; }
    public float Inflate { get; set; }
    /// <summary>Mirrored texture (box UV): null means the bone's setting.</summary>
    public bool? Mirror { get; set; }
    /// <summary>The box UV corner (the whole cube's faces laid out from it), or null when faces are given one by one.</summary>
    public Vector2? BoxUv { get; set; }
    /// <summary>Per-face texture places ("north", "east" …); a face that is not listed is not drawn.</summary>
    public IReadOnlyDictionary<string, ModelFaceUv>? Faces { get; set; }
}

/// <summary>A movable part of a model: cubes turned together around a pivot, and the part it hangs from.</summary>
public sealed class ModelBone
{
    public required string Name { get; set; }
    public string? Parent { get; set; }
    public Vector3 Pivot { get; set; }
    /// <summary>The bone's turn, in degrees (bones hanging from it turn with it; animations call it "this").</summary>
    public Vector3 Rotation { get; set; }
    /// <summary>
    /// The old format's bind-pose turn: it turns only this bone's own cubes (around its pivot), not the bones hanging
    /// from it — how the game lays a cow's or panda's body level while its head and legs stay where they are written.
    /// </summary>
    public Vector3 BindPose { get; set; }
    /// <summary>The bind pose came from an older definition the game still applies (not written back: the game adds it itself).</summary>
    public bool BindPoseInherited { get; set; }
    public bool Mirror { get; set; }
    public float Inflate { get; set; }
    public bool NeverRender { get; set; }
    public List<ModelCube> Cubes { get; set; } = [];
}

/// <summary>A mob's model: its bones, and the texture size its texture coordinates are written for.</summary>
public sealed class EntityModel
{
    public required string Id { get; set; }
    public int TextureWidth { get; set; } = 64;
    public int TextureHeight { get; set; } = 64;
    public List<ModelBone> Bones { get; set; } = [];
    public int CubeCount => Bones.Sum(b => b.Cubes.Count);
}

/// <summary>Reads Bedrock geometry files into models, by geometry id ("geometry.cow").</summary>
public static partial class EntityModels
{
    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    const int MaxBones = 512, MaxCubes = 4096;

    /// <summary>
    /// Reads every model in the given files (name, bytes) — the game's models/entity folder, models/mobs.json, or a
    /// pack's models — resolving models that build on others. Files that cannot be read are listed in notes. A later
    /// definition of a model replaces an earlier one (give the game's packs oldest first); where it rewrote a bone of the
    /// old format the same but without its bind-pose turn, the turn is kept (the game still applies it to those mobs).
    /// </summary>
    public static Dictionary<string, EntityModel> Read(IEnumerable<(string Name, byte[] Data)> files, List<string>? notes = null)
    {
        // First every definition as written (with what it builds on), then the inheritance.
        var raw = new Dictionary<string, (string? Parent, int? W, int? H, List<ModelBone> Bones)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, data) in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(data, Lenient);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Name == "minecraft:geometry" && p.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var g in p.Value.EnumerateArray())
                        {
                            if (g.ValueKind != JsonValueKind.Object || !g.TryGetProperty("description", out var d) || Str(d, "identifier") is not { } id) continue;
                            raw[id] = (null, Int(d, "texture_width"), Int(d, "texture_height"), KeepBindPoses(raw.GetValueOrDefault(id).Bones, Bones(g)));
                        }
                    }
                    else if (p.Name.StartsWith("geometry.", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object)
                    {
                        // "geometry.child:geometry.parent" builds on the parent.
                        string[] parts = p.Name.Split(':', 2);
                        raw[parts[0]] = (parts.Length > 1 ? parts[1] : null, Int(p.Value, "texturewidth"), Int(p.Value, "textureheight"), KeepBindPoses(raw.GetValueOrDefault(parts[0]).Bones, Bones(p.Value)));
                    }
                }
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
            {
                notes?.Add($"{name}: not a model file this program can read ({e.Message.Split('\n')[0]})");
            }
        }
        var models = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);
        EntityModel? Resolve(string id, int depth)
        {
            if (models.TryGetValue(id, out var done)) return done;
            if (depth > 16 || !raw.TryGetValue(id, out var r)) return null;
            var bones = new List<ModelBone>();
            int? w = r.W, h = r.H;
            if (r.Parent != null && Resolve(r.Parent, depth + 1) is { } parent)
            {
                bones.AddRange(parent.Bones);
                w ??= parent.TextureWidth;
                h ??= parent.TextureHeight;
            }
            foreach (var b in r.Bones)
            {
                // A bone of the same name replaces the inherited one.
                int i = bones.FindIndex(x => x.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) bones[i] = b; else bones.Add(b);
            }
            var m = new EntityModel { Id = id, TextureWidth = Math.Clamp(w ?? 64, 1, 4096), TextureHeight = Math.Clamp(h ?? 64, 1, 4096), Bones = bones };
            models[id] = m;
            return m;
        }
        foreach (var id in raw.Keys.ToList()) Resolve(id, 0);
        return models;
    }

    /// <summary>A newer definition's bones, with the bind-pose turns of the same bones in the older one.</summary>
    static List<ModelBone> KeepBindPoses(List<ModelBone>? older, List<ModelBone> newer)
    {
        if (older == null) return newer;
        for (int i = 0; i < newer.Count; i++)
        {
            var n = newer[i];
            if (n.Rotation != Vector3.Zero || n.BindPose != Vector3.Zero) continue;
            var o = older.FirstOrDefault(b => b.Name.Equals(n.Name, StringComparison.OrdinalIgnoreCase));
            if (o == null || o.BindPose == Vector3.Zero || o.Pivot != n.Pivot || o.Cubes.Count != n.Cubes.Count) continue;
            if (!o.Cubes.Zip(n.Cubes).All(c => c.First.Origin == c.Second.Origin && c.First.Size == c.Second.Size)) continue;
            newer[i] = new ModelBone
            {
                Name = n.Name, Parent = n.Parent, Pivot = n.Pivot, Rotation = n.Rotation, BindPose = o.BindPose, BindPoseInherited = true, Mirror = n.Mirror,
                Inflate = n.Inflate, NeverRender = n.NeverRender, Cubes = n.Cubes,
            };
        }
        return newer;
    }

    static List<ModelBone> Bones(JsonElement geometry)
    {
        var list = new List<ModelBone>();
        if (!geometry.TryGetProperty("bones", out var bones) || bones.ValueKind != JsonValueKind.Array) return list;
        int cubes = 0;
        foreach (var b in bones.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object || Str(b, "name") is not { } name || list.Count >= MaxBones) continue;
            bool mirror = Bool(b, "mirror") ?? false;
            var bone = new ModelBone
            {
                Name = name, Parent = Str(b, "parent"), Pivot = Vec(b, "pivot"),
                Rotation = Vec(b, "rotation"), BindPose = Vec(b, "bind_pose_rotation"),
                Mirror = mirror, Inflate = Num(b, "inflate") ?? 0, NeverRender = Bool(b, "neverRender") ?? false,
            };
            if (b.TryGetProperty("cubes", out var cs) && cs.ValueKind == JsonValueKind.Array)
                foreach (var c in cs.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object || ++cubes > MaxCubes) continue;
                    Vector2? box = null;
                    Dictionary<string, ModelFaceUv>? faces = null;
                    if (c.TryGetProperty("uv", out var uv))
                    {
                        if (uv.ValueKind == JsonValueKind.Array) box = new Vector2(At(uv, 0), At(uv, 1));
                        else if (uv.ValueKind == JsonValueKind.Object)
                        {
                            faces = new Dictionary<string, ModelFaceUv>(StringComparer.OrdinalIgnoreCase);
                            foreach (var f in uv.EnumerateObject())
                            {
                                if (f.Value.ValueKind != JsonValueKind.Object || !f.Value.TryGetProperty("uv", out var at) || at.ValueKind != JsonValueKind.Array) continue;
                                var size = f.Value.TryGetProperty("uv_size", out var us) && us.ValueKind == JsonValueKind.Array ? new Vector2(At(us, 0), At(us, 1)) : Vector2.Zero;
                                faces[f.Name] = new ModelFaceUv(At(at, 0), At(at, 1), size.X, size.Y, Int(f.Value, "uv_rotation") ?? 0);
                            }
                        }
                    }
                    bone.Cubes.Add(new ModelCube
                    {
                        Origin = Vec(c, "origin"), Size = Vec(c, "size"), Pivot = c.TryGetProperty("pivot", out _) ? Vec(c, "pivot") : null,
                        Rotation = Vec(c, "rotation"), Inflate = Num(c, "inflate") ?? bone.Inflate, Mirror = Bool(c, "mirror"),
                        BoxUv = faces == null ? box ?? Vector2.Zero : null, Faces = faces,
                    });
                }
            list.Add(bone);
        }
        return list;
    }

    // ------------------------------------------------------------------ JSON values (numbers may be written as text)

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static float? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? Number(v) : null;

    static int? Int(JsonElement e, string name) => Num(e, name) is float f ? (int)Math.Round(f) : null;

    static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    static float? Number(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.TryGetSingle(out float f) && float.IsFinite(f) ? f : null,
        JsonValueKind.String => float.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && float.IsFinite(s) ? s : null,
        _ => null,
    };

    static float At(JsonElement array, int i) => i < array.GetArrayLength() ? Number(array[i]) ?? 0 : 0;

    static Vector3 Vec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? new Vector3(At(v, 0), At(v, 1), At(v, 2)) : Vector3.Zero;
}
