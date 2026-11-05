// MinecraftStudio.Core/Models/EntityAnimation.cs: a mob animation as Bedrock writes it (animations/*.json), read and
// written back.
// Owns: KeyLerp, AnimKey, AnimChannel, BoneAnimation, EntityAnimation and EntityAnimations — per bone, rotation, position
// and scale as one value for the whole animation or as keyframes (each axis a number or a Molang formula; a key may jump
// with "pre"/"post"; linear or smooth (catmull-rom)); loop / hold on the last frame / once, length, the time formula.
// Whatever else an animation has (sounds, particles, timeline, bone "relative_to" …) is kept as it was and written back.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinecraftStudio.Core.Models;

public enum KeyLerp { Linear, Smooth }

/// <summary>One keyframe: its time, the value from it on (and the value up to it, when it jumps), and how it blends.</summary>
public sealed class AnimKey
{
    public double Time { get; set; }
    /// <summary>x, y and z from this key on: numbers or Molang formulas.</summary>
    public string[] Post { get; set; } = ["0", "0", "0"];
    /// <summary>x, y and z just before this key (a jump), or null when the same as <see cref="Post"/>.</summary>
    public string[]? Pre { get; set; }
    public KeyLerp Lerp { get; set; }

    public AnimKey Clone() => new() { Time = Time, Post = (string[])Post.Clone(), Pre = (string[]?)Pre?.Clone(), Lerp = Lerp };
}

/// <summary>A bone's rotation, position or scale: one value for the whole animation, or keyframes.</summary>
public sealed class AnimChannel
{
    /// <summary>The value for the whole animation (x, y, z), when it is not keyframed.</summary>
    public string[]? Constant { get; set; }
    /// <summary>Keyframes in time order (none when <see cref="Constant"/> is used).</summary>
    public List<AnimKey> Keys { get; set; } = [];
    public bool IsKeyed => Keys.Count > 0;

    public AnimChannel Clone() => new() { Constant = (string[]?)Constant?.Clone(), Keys = Keys.Select(k => k.Clone()).ToList() };

    public void Sort() => Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
}

/// <summary>What an animation does to one bone.</summary>
public sealed class BoneAnimation
{
    public required string Bone { get; set; }
    public AnimChannel? Rotation { get; set; }
    public AnimChannel? Position { get; set; }
    public AnimChannel? Scale { get; set; }
    /// <summary>The bone's other settings as read (kept, written back).</summary>
    public JsonObject Extra { get; set; } = [];

    public IEnumerable<(string Name, AnimChannel Channel)> Channels()
    {
        if (Rotation != null) yield return ("rotation", Rotation);
        if (Position != null) yield return ("position", Position);
        if (Scale != null) yield return ("scale", Scale);
    }

    public BoneAnimation Clone() => new()
    {
        Bone = Bone, Rotation = Rotation?.Clone(), Position = Position?.Clone(), Scale = Scale?.Clone(), Extra = (JsonObject)Extra.DeepClone(),
    };
}

/// <summary>One animation: how it plays and what it does to each bone.</summary>
public sealed class EntityAnimation
{
    public required string Id { get; set; }
    /// <summary>"false" (once), "true" (loops) or "hold_on_last_frame".</summary>
    public string Loop { get; set; } = "false";
    /// <summary>The length in seconds, when the animation says it; otherwise its last keyframe.</summary>
    public double? Length { get; set; }
    /// <summary>The formula for the animation's time (walks run on distance moved), or null for real time.</summary>
    public string? AnimTimeUpdate { get; set; }
    public List<BoneAnimation> Bones { get; set; } = [];
    /// <summary>The animation's other settings as read (sounds, particles, timeline …; kept, written back).</summary>
    public JsonObject Extra { get; set; } = [];

    public bool Loops => Loop == "true";
    public bool Holds => Loop == "hold_on_last_frame";

    /// <summary>How long it plays: its length, else its last keyframe, else one second (for an endless formula).</summary>
    public double PlayLength
    {
        get
        {
            if (Length is > 0) return Length.Value;
            double last = Bones.SelectMany(b => b.Channels()).SelectMany(c => c.Channel.Keys).Select(k => k.Time).DefaultIfEmpty(0).Max();
            return last > 0 ? last : 1;
        }
    }

    public BoneAnimation? Bone(string name) => Bones.FirstOrDefault(b => b.Bone.Equals(name, StringComparison.OrdinalIgnoreCase));

    public EntityAnimation Clone() => new()
    {
        Id = Id, Loop = Loop, Length = Length, AnimTimeUpdate = AnimTimeUpdate, Bones = Bones.Select(b => b.Clone()).ToList(), Extra = (JsonObject)Extra.DeepClone(),
    };
}

/// <summary>Reads and writes animation files.</summary>
public static class EntityAnimations
{
    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    static readonly HashSet<string> Known = new(StringComparer.Ordinal) { "loop", "animation_length", "anim_time_update", "bones" };

    /// <summary>Every animation in the files (name, bytes), by id; a later file's animation replaces an earlier one's.</summary>
    public static Dictionary<string, EntityAnimation> Read(IEnumerable<(string Name, byte[] Data)> files, List<string>? notes = null)
    {
        var all = new Dictionary<string, EntityAnimation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, data) in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(data, Lenient);
                if (Tree(doc.RootElement) is not JsonObject root || root["animations"] is not JsonObject anims) continue;
                foreach (var (id, node) in anims)
                    if (node is JsonObject a && Parse(id, a) is { } anim) all[id] = anim;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
            {
                notes?.Add($"{name}: not an animation file this program can read ({e.Message.Split('\n')[0]})");
            }
        }
        return all;
    }

    /// <summary>
    /// A JSON tree as nodes, where a name written twice in one object keeps its last value — as the game reads it (some
    /// of the game's own files name a bone twice).
    /// </summary>
    static JsonNode? Tree(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var p in e.EnumerateObject()) obj[p.Name] = Tree(p.Value);
                return obj;
            case JsonValueKind.Array:
                var arr = new JsonArray();
                foreach (var item in e.EnumerateArray()) arr.Add(Tree(item));
                return arr;
            case JsonValueKind.String:
                return JsonValue.Create(e.GetString());
            case JsonValueKind.Number:
                return JsonValue.Create(e.GetDouble());
            case JsonValueKind.True:
                return JsonValue.Create(true);
            case JsonValueKind.False:
                return JsonValue.Create(false);
            default:
                return null;
        }
    }

    static EntityAnimation? Parse(string id, JsonObject a)
    {
        var anim = new EntityAnimation { Id = id };
        if (a["loop"] is JsonValue loop)
            anim.Loop = loop.TryGetValue(out bool b) ? (b ? "true" : "false") : loop.TryGetValue(out string? s) && s == "hold_on_last_frame" ? "hold_on_last_frame" : "false";
        if (a["animation_length"] is JsonValue len && Number(len) is double l) anim.Length = l;
        if (a["anim_time_update"] is JsonValue upd) anim.AnimTimeUpdate = Text(upd);
        if (a["bones"] is JsonObject bones)
            foreach (var (bone, node) in bones)
            {
                if (node is not JsonObject bo) continue;
                var ba = new BoneAnimation { Bone = bone };
                foreach (var (channel, value) in bo)
                {
                    switch (channel)
                    {
                        case "rotation": ba.Rotation = Channel(value, 0); break;
                        case "position": ba.Position = Channel(value, 0); break;
                        case "scale": ba.Scale = Channel(value, 1); break;
                        default: ba.Extra[channel] = value?.DeepClone(); break;
                    }
                }
                anim.Bones.Add(ba);
            }
        foreach (var (k, v) in a) if (!Known.Contains(k)) anim.Extra[k] = v?.DeepClone();
        return anim;
    }

    /// <summary>A channel as written: [x, y, z], one value for all three, or keyframes by time.</summary>
    static AnimChannel? Channel(JsonNode? node, double neutral)
    {
        if (node is JsonObject keys && !keys.ContainsKey("pre") && !keys.ContainsKey("post"))
        {
            var ch = new AnimChannel();
            foreach (var (time, value) in keys)
            {
                if (!double.TryParse(time, NumberStyles.Float, CultureInfo.InvariantCulture, out double t)) continue;
                var key = new AnimKey { Time = t };
                if (value is JsonObject prepost)
                {
                    if (prepost["post"] is { } post) key.Post = Vector(post, neutral);
                    if (prepost["pre"] is { } pre) key.Pre = Vector(pre, neutral);
                    if (prepost["post"] == null && key.Pre != null) key.Post = (string[])key.Pre.Clone();
                    if (prepost["lerp_mode"] is JsonValue mode && Text(mode) == "catmullrom") key.Lerp = KeyLerp.Smooth;
                }
                else key.Post = Vector(value, neutral);
                ch.Keys.Add(key);
            }
            ch.Sort();
            return ch;
        }
        if (node is JsonObject single)
        {
            // A lone {pre, post} without a time: the value at 0.
            var key = new AnimKey { Time = 0, Post = Vector(single["post"] ?? single["pre"], neutral) };
            return new AnimChannel { Keys = [key] };
        }
        return node == null ? null : new AnimChannel { Constant = Vector(node, neutral) };
    }

    /// <summary>x, y, z from [x, y, z] or one value; missing axes are the neutral value.</summary>
    static string[] Vector(JsonNode? node, double neutral)
    {
        string n = Format(neutral);
        if (node is JsonArray arr)
            return [arr.Count > 0 ? Text(arr[0]) ?? n : n, arr.Count > 1 ? Text(arr[1]) ?? n : n, arr.Count > 2 ? Text(arr[2]) ?? n : n];
        string one = Text(node) ?? n;
        return [one, one, one];
    }

    static string? Text(JsonNode? node) => node is JsonValue v
        ? v.TryGetValue(out double d) ? Format(d) : v.TryGetValue(out string? s) ? s : v.TryGetValue(out bool b) ? (b ? "1" : "0") : null
        : null;

    static double? Number(JsonValue v) => v.TryGetValue(out double d) ? d : v.TryGetValue(out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : null;

    public static string Format(double v) => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ writing

    /// <summary>An axis value as JSON: a number when it is one, else the formula as text.</summary>
    static JsonNode AxisJson(string v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? JsonValue.Create(Math.Round(d, 6))! : JsonValue.Create(v)!;

    static JsonArray VectorJson(string[] v) => [AxisJson(v[0]), AxisJson(v[1]), AxisJson(v[2])];

    static JsonNode ChannelJson(AnimChannel ch)
    {
        if (!ch.IsKeyed) return VectorJson(ch.Constant ?? ["0", "0", "0"]);
        var obj = new JsonObject();
        foreach (var k in ch.Keys.OrderBy(k => k.Time))
        {
            string time = Format(k.Time);
            if (k.Pre == null && k.Lerp == KeyLerp.Linear) { obj[time] = VectorJson(k.Post); continue; }
            var key = new JsonObject();
            if (k.Pre != null) key["pre"] = VectorJson(k.Pre);
            key["post"] = VectorJson(k.Post);
            if (k.Lerp == KeyLerp.Smooth) key["lerp_mode"] = "catmullrom";
            obj[time] = key;
        }
        return obj;
    }

    public static JsonObject ToJson(EntityAnimation a)
    {
        var obj = new JsonObject();
        if (a.Loop == "true") obj["loop"] = true;
        else if (a.Loop == "hold_on_last_frame") obj["loop"] = "hold_on_last_frame";
        if (a.Length is double len) obj["animation_length"] = Math.Round(len, 6);
        if (a.AnimTimeUpdate != null) obj["anim_time_update"] = a.AnimTimeUpdate;
        var bones = new JsonObject();
        foreach (var b in a.Bones)
        {
            var bo = new JsonObject();
            foreach (var (name, ch) in b.Channels()) bo[name] = ChannelJson(ch);
            foreach (var (k, v) in b.Extra) bo[k] = v?.DeepClone();
            if (bo.Count > 0) bones[b.Bone] = bo;
        }
        obj["bones"] = bones;
        foreach (var (k, v) in a.Extra) obj[k] = v?.DeepClone();
        return obj;
    }

    /// <summary>An animation file holding the given animations.</summary>
    public static byte[] File(IEnumerable<EntityAnimation> animations)
    {
        var anims = new JsonObject();
        foreach (var a in animations) anims[a.Id] = ToJson(a);
        var root = new JsonObject { ["format_version"] = "1.8.0", ["animations"] = anims };
        return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(Indented) + "\n");
    }
}
