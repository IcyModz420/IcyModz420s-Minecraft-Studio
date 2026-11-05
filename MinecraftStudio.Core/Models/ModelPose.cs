// MinecraftStudio.Core/Models/ModelPose.cs: the rest pose an entity's "setup" animation gives its model.
// Owns: ModelPose — reading animation files for the turns that do not change over time (a cow's body laid level with
// "90 - this"), and working them out per bone ("this" is the bone's own rotation). Anything that depends on the game
// (queries, variables, maths functions, keyframes) is left out: the model shows its plain rest pose there.
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace MinecraftStudio.Core.Models;

/// <summary>Constant bone turns from a setup animation, added to each bone's own (as the game adds them).</summary>
public sealed class ModelPose
{
    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Per bone: the x, y and z turn as written (a number or a formula), or null.</summary>
    public Dictionary<string, string?[]> Rotation { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the pose adds to a bone's turn.</summary>
    public Vector3 RotationFor(ModelBone bone)
    {
        if (!Rotation.TryGetValue(bone.Name, out var r)) return Vector3.Zero;
        float A(int i, float self) => r[i] is { } e && Eval(e, self) is float v ? v : 0;
        return new Vector3(A(0, bone.Rotation.X), A(1, bone.Rotation.Y), A(2, bone.Rotation.Z));
    }

    /// <summary>The poses in animation files, by animation id ("animation.cow.setup.v1.0").</summary>
    public static Dictionary<string, ModelPose> Read(IEnumerable<(string Name, byte[] Data)> files)
    {
        var poses = new Dictionary<string, ModelPose>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, data) in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(data, Lenient);
                if (!doc.RootElement.TryGetProperty("animations", out var anims) || anims.ValueKind != JsonValueKind.Object) continue;
                foreach (var a in anims.EnumerateObject())
                {
                    if (a.Value.ValueKind != JsonValueKind.Object || !a.Value.TryGetProperty("bones", out var bones) || bones.ValueKind != JsonValueKind.Object) continue;
                    var pose = new ModelPose();
                    foreach (var b in bones.EnumerateObject())
                    {
                        // Only a plain [x, y, z] (keyframes change over time).
                        if (b.Value.ValueKind != JsonValueKind.Object || !b.Value.TryGetProperty("rotation", out var rot) || rot.ValueKind != JsonValueKind.Array || rot.GetArrayLength() < 3) continue;
                        pose.Rotation[b.Name] = [.. Enumerable.Range(0, 3).Select(i => rot[i].ValueKind switch
                        {
                            JsonValueKind.Number => rot[i].GetDouble().ToString(CultureInfo.InvariantCulture),
                            JsonValueKind.String => rot[i].GetString(),
                            _ => null,
                        })];
                    }
                    if (pose.Rotation.Count > 0) poses[a.Name] = pose;
                }
            }
            catch (JsonException) { }
        }
        return poses;
    }

    /// <summary>
    /// A formula of numbers, "this", + − × ÷ and brackets; null when it uses anything else (queries, variables …),
    /// because then the pose depends on the game.
    /// </summary>
    public static float? Eval(string text, float self)
    {
        int pos = 0;
        string s = text.Replace(" ", "").ToLowerInvariant();
        double? Expr()
        {
            var left = Term();
            while (left != null && pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
            {
                char op = s[pos++];
                var right = Term();
                if (right == null) return null;
                left = op == '+' ? left + right : left - right;
            }
            return left;
        }
        double? Term()
        {
            var left = Factor();
            while (left != null && pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
            {
                char op = s[pos++];
                var right = Factor();
                if (right == null || op == '/' && right == 0) return null;
                left = op == '*' ? left * right : left / right;
            }
            return left;
        }
        double? Factor()
        {
            if (pos >= s.Length) return null;
            if (s[pos] == '-') { pos++; return -Factor(); }
            if (s[pos] == '+') { pos++; return Factor(); }
            if (s[pos] == '(')
            {
                pos++;
                var v = Expr();
                if (pos >= s.Length || s[pos] != ')') return null;
                pos++;
                return v;
            }
            if (s.AsSpan(pos).StartsWith("this")) { pos += 4; return self; }
            int start = pos;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            if (pos == start) return null;
            return double.TryParse(s.AsSpan(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : null;
        }
        var result = Expr();
        return result != null && pos == s.Length && double.IsFinite(result.Value) ? (float)result.Value : null;
    }
}
