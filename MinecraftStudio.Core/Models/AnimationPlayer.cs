// MinecraftStudio.Core/Models/AnimationPlayer.cs: where every bone is at a moment of an animation.
// Owns: BonePose and AnimationPlayer — the time inside an animation (looping, held on the last frame, or once), each
// channel's value then (a formula worked out with query.anim_time and "this"; keyframes blended linearly or smoothly,
// with jumps), and several animations together (turns and moves add up, scales multiply), as the game layers them.
using System.Numerics;

namespace MinecraftStudio.Core.Models;

/// <summary>What animations do to a bone: added turn (degrees) and move (pixels), and a scale.</summary>
public readonly record struct BonePose(Vector3 Rotation, Vector3 Position, Vector3 Scale)
{
    public static BonePose None => new(Vector3.Zero, Vector3.Zero, Vector3.One);

    public BonePose Then(BonePose other) => new(Rotation + other.Rotation, Position + other.Position, Scale * other.Scale);
}

/// <summary>Works out animations at a moment.</summary>
public static class AnimationPlayer
{
    /// <summary>The time inside an animation after playing for some seconds.</summary>
    public static double TimeIn(EntityAnimation a, double playing)
    {
        double len = a.PlayLength;
        if (playing <= 0 || len <= 0) return 0;
        if (a.Loops) return playing % len;
        return Math.Min(playing, len);
    }

    /// <summary>
    /// Every bone's pose at a time of the animation (not wrapped: use <see cref="TimeIn"/>), with query.anim_time set to
    /// it. "this" is the bone's own rotation in the model (0 for moves, 1 for scale).
    /// </summary>
    public static Dictionary<string, BonePose> Pose(EntityAnimation a, double time, MolangContext ctx, EntityModel? model = null)
    {
        var result = new Dictionary<string, BonePose>(StringComparer.OrdinalIgnoreCase);
        ctx["query.anim_time"] = time;
        foreach (var b in a.Bones)
        {
            var bone = model?.Bones.FirstOrDefault(x => x.Name.Equals(b.Bone, StringComparison.OrdinalIgnoreCase));
            var own = bone?.Rotation ?? Vector3.Zero;
            var pose = BonePose.None;
            if (b.Rotation != null) pose = pose with { Rotation = Value(b.Rotation, time, ctx, own, 0) };
            if (b.Position != null) pose = pose with { Position = Value(b.Position, time, ctx, Vector3.Zero, 0) };
            if (b.Scale != null) pose = pose with { Scale = Value(b.Scale, time, ctx, Vector3.One, 1) };
            result[b.Bone] = result.TryGetValue(b.Bone, out var had) ? had.Then(pose) : pose;
        }
        return result;
    }

    /// <summary>Several animations' poses together (turns and moves added, scales multiplied).</summary>
    public static Dictionary<string, BonePose> Combine(IEnumerable<Dictionary<string, BonePose>> poses)
    {
        var all = new Dictionary<string, BonePose>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in poses)
            foreach (var (bone, pose) in p)
                all[bone] = all.TryGetValue(bone, out var had) ? had.Then(pose) : pose;
        return all;
    }

    /// <summary>A channel's value at a time.</summary>
    public static Vector3 Value(AnimChannel ch, double time, MolangContext ctx, Vector3 self, double neutral)
    {
        if (!ch.IsKeyed) return Eval(ch.Constant ?? [EntityAnimations.Format(neutral), EntityAnimations.Format(neutral), EntityAnimations.Format(neutral)], ctx, self);
        var keys = ch.Keys;
        if (time <= keys[0].Time) return Eval(keys[0].Pre ?? keys[0].Post, ctx, self);
        if (time >= keys[^1].Time) return Eval(keys[^1].Post, ctx, self);
        int i = 0;
        while (i + 1 < keys.Count && keys[i + 1].Time <= time) i++;
        var k0 = keys[i];
        var k1 = keys[i + 1];
        double span = k1.Time - k0.Time;
        float f = span <= 0 ? 1 : (float)((time - k0.Time) / span);
        var a = Eval(k0.Post, ctx, self);
        var b = Eval(k1.Pre ?? k1.Post, ctx, self);
        if (k0.Lerp != KeyLerp.Smooth && k1.Lerp != KeyLerp.Smooth) return Vector3.Lerp(a, b, f);
        // Catmull-rom through the keys around the segment (the ends repeat when there are none).
        var before = i > 0 ? Eval(keys[i - 1].Post, ctx, self) : a;
        var after = i + 2 < keys.Count ? Eval(keys[i + 2].Pre ?? keys[i + 2].Post, ctx, self) : b;
        return CatmullRom(before, a, b, after, f);
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    static Vector3 Eval(string[] v, MolangContext ctx, Vector3 self)
    {
        float Axis(int i, float s)
        {
            ctx.This = s;
            return (float)Molang.Eval(v[i], ctx);
        }
        return new Vector3(Axis(0, self.X), Axis(1, self.Y), Axis(2, self.Z));
    }
}
