// MinecraftStudio.Core/Models/ModelMesh.cs: a model turned into textured faces in its rest pose, ready to draw and to
// tell which texture pixel each point of the model shows.
// Owns: ModelQuad and ModelMesh — every cube's six faces (box UV laid out the way the game does it, with mirroring and
// inflation; per-face UV with flips and quarter turns), each cube turned around its pivot and each bone around its
// own, through all the bones it hangs from (an old bind pose turns only the bone's own cubes). Bedrock turns by −x, +y,
// −z degrees (x first) in its y-up, north-facing space — what Blockbench writes, and what puts a cow's body level with
// its head; a cube turned without a pivot turns around its middle.
using System.Numerics;

namespace MinecraftStudio.Core.Models;

/// <summary>
/// One textured face: point(a, b) = Origin + a·U + b·V for a, b in 0..1 shows texture coordinate
/// (TexU + a·TexW, TexV + b·TexH) of the model's texture units (sizes may be negative: flipped).
/// </summary>
public sealed record ModelQuad(string Bone, int Cube, string Face, Vector3 Origin, Vector3 U, Vector3 V, Vector3 Normal,
    float TexU, float TexV, float TexW, float TexH, int Turns = 0)
{
    /// <summary>The texture coordinate at a point of the face.</summary>
    public Vector2 TexAt(float a, float b)
    {
        // A quarter turn of the texture rectangle (uv_rotation) swaps which way the face's sides run through it.
        (a, b) = Turns switch { 1 => (b, 1 - a), 2 => (1 - a, 1 - b), 3 => (1 - b, a), _ => (a, b) };
        return new Vector2(TexU + a * TexW, TexV + b * TexH);
    }

    /// <summary>The texture rectangle the face uses (positive sizes).</summary>
    public (float X, float Y, float W, float H) TexRect => (Math.Min(TexU, TexU + TexW), Math.Min(TexV, TexV + TexH), Math.Abs(TexW), Math.Abs(TexH));
}

/// <summary>A model's faces in its rest pose.</summary>
public sealed class ModelMesh
{
    public required EntityModel Model { get; init; }
    public List<ModelQuad> Quads { get; init; } = [];
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }
    public Vector3 Centre => (Min + Max) / 2;

    /// <summary>The turn of a bone or cube, as Bedrock means its degrees.</summary>
    public static Matrix4x4 Turn(Vector3 degrees)
    {
        if (degrees == Vector3.Zero) return Matrix4x4.Identity;
        const float k = MathF.PI / 180;
        return Matrix4x4.CreateRotationX(-degrees.X * k) * Matrix4x4.CreateRotationY(degrees.Y * k) * Matrix4x4.CreateRotationZ(-degrees.Z * k);
    }

    /// <param name="pose">Turns from the entity's setup animation (added to the bones' own), or null.</param>
    /// <param name="animation">Bones' poses at a moment of an animation (turns and moves added, scale), or null.</param>
    public static ModelMesh Build(EntityModel model, IReadOnlySet<string>? hiddenBones = null, ModelPose? pose = null, IReadOnlyDictionary<string, BonePose>? animation = null)
    {
        var bones = model.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        // Each bone's transform: its own turn around its pivot, then its parent's, up to the root.
        var cache = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        Matrix4x4 World(ModelBone b, int depth)
        {
            if (cache.TryGetValue(b.Name, out var m)) return m;
            var turn = b.Rotation + (pose?.RotationFor(b) ?? Vector3.Zero);
            var moved = Vector3.Zero;
            var scale = Vector3.One;
            if (animation != null && animation.TryGetValue(b.Name, out var ap)) { turn += ap.Rotation; moved = ap.Position; scale = ap.Scale; }
            // Scaled and turned around the pivot, then moved (in the parent's space).
            var own = Matrix4x4.CreateTranslation(-b.Pivot) * Matrix4x4.CreateScale(scale) * Turn(turn) * Matrix4x4.CreateTranslation(b.Pivot) * Matrix4x4.CreateTranslation(moved);
            if (depth < 64 && b.Parent != null && bones.TryGetValue(b.Parent, out var parent) && parent != b) own *= World(parent, depth + 1);
            return cache[b.Name] = own;
        }
        // A part hidden by the player hides what hangs from it; the game's "neverRender" hides only the bone's own cubes
        // (a zombie hangs everything from an invisible waist).
        bool HiddenByPlayer(ModelBone b, int depth) =>
            hiddenBones?.Contains(b.Name) == true || depth < 64 && b.Parent != null && bones.TryGetValue(b.Parent, out var p) && p != b && HiddenByPlayer(p, depth + 1);
        bool Hidden(ModelBone b) => b.NeverRender || HiddenByPlayer(b, 0);

        var quads = new List<ModelQuad>();
        foreach (var bone in model.Bones)
        {
            if (Hidden(bone)) continue;
            var world = World(bone, 0);
            for (int ci = 0; ci < bone.Cubes.Count; ci++)
            {
                var cube = bone.Cubes[ci];
                // The cube's own turn (around its pivot, or its middle when it names none), then the bone's bind pose.
                var m = world;
                if (bone.BindPose != Vector3.Zero) m = Matrix4x4.CreateTranslation(-bone.Pivot) * Turn(bone.BindPose) * Matrix4x4.CreateTranslation(bone.Pivot) * m;
                if (cube.Rotation != Vector3.Zero)
                {
                    var pivot = cube.Pivot ?? cube.Origin + cube.Size / 2;
                    m = Matrix4x4.CreateTranslation(-pivot) * Turn(cube.Rotation) * Matrix4x4.CreateTranslation(pivot) * m;
                }
                foreach (var q in Faces(bone, ci, cube)) quads.Add(Apply(q, m));
            }
        }
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var q in quads)
            foreach (var p in new[] { q.Origin, q.Origin + q.U, q.Origin + q.V, q.Origin + q.U + q.V })
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        if (quads.Count == 0) min = max = Vector3.Zero;
        return new ModelMesh { Model = model, Quads = quads, Min = min, Max = max };
    }

    static ModelQuad Apply(ModelQuad q, Matrix4x4 m)
    {
        var o = Vector3.Transform(q.Origin, m);
        var u = Vector3.TransformNormal(q.U, m);
        var v = Vector3.TransformNormal(q.V, m);
        var n = Vector3.TransformNormal(q.Normal, m);
        return q with { Origin = o, U = u, V = v, Normal = n.LengthSquared() > 0 ? Vector3.Normalize(n) : n };
    }

    /// <summary>
    /// The six faces of an unturned cube. Seen from outside, each face's texture runs left to right and top to bottom
    /// the way the game lays it out: north (front) from east to west, the sides from back to front, up and down from
    /// east to west with rows from south (back) to north.
    /// </summary>
    static IEnumerable<ModelQuad> Faces(ModelBone bone, int ci, ModelCube c)
    {
        float g = c.Inflate;
        var lo = c.Origin - new Vector3(g);
        var hi = c.Origin + c.Size + new Vector3(g);
        float x0 = lo.X, y0 = lo.Y, z0 = lo.Z, x1 = hi.X, y1 = hi.Y, z1 = hi.Z;
        // Corner of the texture's top-left, and the directions its columns and rows run, per face.
        var geometry = new (string Face, Vector3 Origin, Vector3 U, Vector3 V, Vector3 Normal)[]
        {
            ("north", new(x1, y1, z0), new(x0 - x1, 0, 0), new(0, y0 - y1, 0), -Vector3.UnitZ),
            ("south", new(x0, y1, z1), new(x1 - x0, 0, 0), new(0, y0 - y1, 0), Vector3.UnitZ),
            ("east", new(x1, y1, z1), new(0, 0, z0 - z1), new(0, y0 - y1, 0), Vector3.UnitX),
            ("west", new(x0, y1, z0), new(0, 0, z1 - z0), new(0, y0 - y1, 0), -Vector3.UnitX),
            ("up", new(x1, y1, z1), new(x0 - x1, 0, 0), new(0, 0, z0 - z1), Vector3.UnitY),
            ("down", new(x1, y0, z1), new(x0 - x1, 0, 0), new(0, 0, z0 - z1), -Vector3.UnitY),
        };
        if (c.BoxUv is { } box)
        {
            float u = box.X, v = box.Y, w = c.Size.X, h = c.Size.Y, d = c.Size.Z;
            bool mirror = c.Mirror ?? bone.Mirror;
            var rects = new Dictionary<string, (float U, float V, float W, float H)>
            {
                ["north"] = (u + d, v + d, w, h), ["south"] = (u + d + w + d, v + d, w, h),
                ["east"] = (u, v + d, d, h), ["west"] = (u + d + w, v + d, d, h),
                ["up"] = (u + d, v, w, d), ["down"] = (u + d + w, v, w, d),
            };
            // Mirrored: east and west swap pictures, and every face's picture is flipped left to right.
            if (mirror) (rects["east"], rects["west"]) = (rects["west"], rects["east"]);
            foreach (var (face, o, uu, vv, n) in geometry)
            {
                var r = rects[face];
                if (r.W == 0 && r.H == 0) continue;
                yield return mirror
                    ? new ModelQuad(bone.Name, ci, face, o, uu, vv, n, r.U + r.W, r.V, -r.W, r.H)
                    : new ModelQuad(bone.Name, ci, face, o, uu, vv, n, r.U, r.V, r.W, r.H);
            }
            yield break;
        }
        if (c.Faces == null) yield break;
        foreach (var (face, o, uu, vv, n) in geometry)
            if (c.Faces.TryGetValue(face, out var f) && (f.W != 0 || f.H != 0))
                yield return new ModelQuad(bone.Name, ci, face, o, uu, vv, n, f.U, f.V, f.W, f.H, ((f.Rotation / 90) % 4 + 4) % 4);
    }
}
