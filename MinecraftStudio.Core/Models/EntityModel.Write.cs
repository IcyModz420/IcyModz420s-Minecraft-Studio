// MinecraftStudio.Core/Models/EntityModel.Write.cs: writing a model back as a Bedrock .geo.json, and copying one to edit.
// Part of EntityModels (main part in EntityModel.cs).
// Owns: Clone (a deep copy, so editing never changes the game's model in the catalog), ToJson / File (format 1.12.0,
// with description.identifier, texture size and visible bounds; bones with pivot, rotation, the bind pose the file
// itself had (not one carried over from an older definition: the game adds that itself), mirror, inflate, cubes with
// box or per-face UV). Read back, the model is the same.
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinecraftStudio.Core.Models;

public static partial class EntityModels
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>A deep copy (bones and cubes too).</summary>
    public static EntityModel Clone(EntityModel m) => new()
    {
        Id = m.Id, TextureWidth = m.TextureWidth, TextureHeight = m.TextureHeight,
        Bones = m.Bones.Select(b => new ModelBone
        {
            Name = b.Name, Parent = b.Parent, Pivot = b.Pivot, Rotation = b.Rotation, BindPose = b.BindPose, BindPoseInherited = b.BindPoseInherited,
            Mirror = b.Mirror, Inflate = b.Inflate, NeverRender = b.NeverRender,
            Cubes = b.Cubes.Select(c => new ModelCube
            {
                Origin = c.Origin, Size = c.Size, Pivot = c.Pivot, Rotation = c.Rotation, Inflate = c.Inflate, Mirror = c.Mirror, BoxUv = c.BoxUv,
                Faces = c.Faces?.ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase),
            }).ToList(),
        }).ToList(),
    };

    // Plain JSON numbers (a bracket list of doubles would make values that need the reflection serializer to write).
    static JsonArray Vec(Vector3 v) => new(JsonValue.Create(Round(v.X)), JsonValue.Create(Round(v.Y)), JsonValue.Create(Round(v.Z)));
    static double Round(float f) => Math.Round(f, 5);

    /// <summary>The model as one "minecraft:geometry" entry (format 1.12.0).</summary>
    public static JsonObject ToJson(EntityModel m)
    {
        var mesh = ModelMesh.Build(m);
        var size = (mesh.Max - mesh.Min) / 16;
        var centre = mesh.Centre / 16;
        var description = new JsonObject
        {
            ["identifier"] = m.Id, ["texture_width"] = m.TextureWidth, ["texture_height"] = m.TextureHeight,
            ["visible_bounds_width"] = Math.Round(Math.Max(1, Math.Max(size.X, size.Z) + 0.5), 2),
            ["visible_bounds_height"] = Math.Round(Math.Max(1, size.Y + 0.5), 2),
            ["visible_bounds_offset"] = new JsonArray(0, Math.Round(centre.Y, 2), 0),
        };
        var bones = new JsonArray();
        foreach (var b in m.Bones)
        {
            var bone = new JsonObject { ["name"] = b.Name };
            if (b.Parent != null) bone["parent"] = b.Parent;
            bone["pivot"] = Vec(b.Pivot);
            if (b.Rotation != Vector3.Zero) bone["rotation"] = Vec(b.Rotation);
            if (b.BindPose != Vector3.Zero && !b.BindPoseInherited) bone["bind_pose_rotation"] = Vec(b.BindPose);
            if (b.Mirror) bone["mirror"] = true;
            if (b.Inflate != 0) bone["inflate"] = Round(b.Inflate);
            if (b.NeverRender) bone["neverRender"] = true;
            if (b.Cubes.Count > 0)
            {
                var cubes = new JsonArray();
                foreach (var c in b.Cubes)
                {
                    var cube = new JsonObject { ["origin"] = Vec(c.Origin), ["size"] = Vec(c.Size) };
                    if (c.Rotation != Vector3.Zero) cube["rotation"] = Vec(c.Rotation);
                    if (c.Pivot is { } p) cube["pivot"] = Vec(p);
                    if (c.Inflate != b.Inflate) cube["inflate"] = Round(c.Inflate);
                    if (c.Mirror is bool mirror && mirror != b.Mirror) cube["mirror"] = mirror;
                    if (c.Faces != null)
                    {
                        var faces = new JsonObject();
                        foreach (var (face, f) in c.Faces)
                        {
                            var fo = new JsonObject { ["uv"] = new JsonArray(Math.Round(f.U, 5), Math.Round(f.V, 5)), ["uv_size"] = new JsonArray(Math.Round(f.W, 5), Math.Round(f.H, 5)) };
                            if (f.Rotation != 0) fo["uv_rotation"] = f.Rotation;
                            faces[face] = fo;
                        }
                        cube["uv"] = faces;
                    }
                    else cube["uv"] = new JsonArray(Math.Round((c.BoxUv ?? Vector2.Zero).X, 5), Math.Round((c.BoxUv ?? Vector2.Zero).Y, 5));
                    cubes.Add(cube);
                }
                bone["cubes"] = cubes;
            }
            bones.Add(bone);
        }
        return new JsonObject { ["description"] = description, ["bones"] = bones };
    }

    /// <summary>A model file holding the given models.</summary>
    public static byte[] File(IEnumerable<EntityModel> models)
    {
        var list = new JsonArray();
        foreach (var m in models) list.Add(ToJson(m));
        var root = new JsonObject { ["format_version"] = "1.12.0", ["minecraft:geometry"] = list };
        return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(Indented) + "\n");
    }
}
