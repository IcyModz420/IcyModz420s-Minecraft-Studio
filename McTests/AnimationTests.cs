// McTests/AnimationTests.cs: "animations" — Molang, animation files and playback for the Animation Studio.
// Owns: AnimationTests — Molang (operators, functions in degrees, queries and variables, statements, bad input), reading
// and writing animations losslessly (every vanilla Bedrock animation round-trips), keyframe blending (linear, smooth,
// jumps, loop and hold), and pictures of mobs mid-animation written to the test output (animations/*.png) to look at.
using System.Numerics;
using System.Text;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;

static class AnimationTests
{
    public static void Run(string[] args)
    {
        Console.WriteLine("== Molang");
        MolangChecks();
        Console.WriteLine("== Animation files");
        Files();
        Console.WriteLine("== Playback");
        Playback();
        Console.WriteLine("== The installed game's animations");
        Installed();
        Console.WriteLine("== Models written back, projects and packs");
        Projects();
    }

    static void Projects()
    {
        var bedrock = MobCatalog.Bedrock();
        if (bedrock == null) { Console.WriteLine("   (Bedrock is not installed: skipped)"); return; }
        // Every model of the game, written and read back, draws the same.
        int models = 0, differ = 0;
        var bad = new List<string>();
        foreach (var m in bedrock.Models.Values.Where(m => !m.Id.Contains('@')))
        {
            models++;
            var back = EntityModels.Read([("x.geo.json", EntityModels.File([m]))]).GetValueOrDefault(m.Id);
            // Bind poses the game adds itself are not written; the model read back gets them from the game's again.
            if (back != null)
                foreach (var b in back.Bones)
                    if (m.Bones.FirstOrDefault(x => x.Name == b.Name) is { BindPoseInherited: true } o) b.BindPose = o.BindPose;
            var a = ModelMesh.Build(m);
            var c = back == null ? null : ModelMesh.Build(back);
            bool same = c != null && a.Quads.Count == c.Quads.Count && Vector3.Distance(a.Min, c.Min) < 0.01f && Vector3.Distance(a.Max, c.Max) < 0.01f &&
                        a.Quads.Zip(c.Quads).All(q => Vector3.Distance(q.First.Origin, q.Second.Origin) < 0.01f && q.First.TexRect == q.Second.TexRect);
            if (!same) { differ++; if (bad.Count < 4) bad.Add(m.Id); }
        }
        Program.Check(differ == 0, $"all {models} models written back draw the same ({differ} differ: {string.Join(", ", bad)})");

        // A project: change, undo, redo, a new animation; the game's own copies never change.
        string key = "entity/cow/cow_v2";
        var info = bedrock.Describe(key);
        var project = new AnimationProject("cow", bedrock.ModelFor(key), bedrock.Animations, bedrock.AnimationsFor(key));
        var walkId = project.Names.First(n => n.Name == "walk").Id;
        string gameWalk = EntityAnimations.ToJson(bedrock.Animations[walkId]).ToJsonString();
        project.Change("Faster walk", () => project.Animations[walkId].Length = 0.5, animations: walkId);
        project.Change("Bigger head", () => project.Model!.Bones.First(b => b.Name == "head").Pivot += new Vector3(0, 1, 0), model: true);
        Program.Check(project.IsDirty && project.ModelChanged && project.ChangedAnimations.Contains(walkId) && project.UndoName == "Bigger head", "changes are steps and are marked");
        Program.Check(EntityAnimations.ToJson(bedrock.Animations[walkId]).ToJsonString() == gameWalk && bedrock.ModelFor(key)!.Bones.First(b => b.Name == "head").Pivot != project.Model!.Bones.First(b => b.Name == "head").Pivot,
            "the game's animation and model are not touched (the project edits copies)");
        project.Undo();
        Program.Check(project.Model!.Bones.First(b => b.Name == "head").Pivot == bedrock.ModelFor(key)!.Bones.First(b => b.Name == "head").Pivot && !project.ModelChanged && project.Animations[walkId].Length == 0.5, "undo the model change");
        project.Undo();
        Program.Check(project.Animations[walkId].Length != 0.5 && !project.CanUndo && project.CanRedo, "undo the walk change");
        project.Redo();
        project.Redo();
        Program.Check(project.Animations[walkId].Length == 0.5 && project.ModelChanged, "redo both");
        string fresh = project.NewAnimation("Happy dance");
        Program.Check(fresh == "animation.cow.happy_dance" && project.Names.Any(n => n.Name == "happy_dance") && project.NewIds.Contains(fresh), $"a new animation ({fresh})");
        project.Change("Dance key", () => project.Animations[fresh].Bones.Add(new BoneAnimation { Bone = "head", Rotation = new AnimChannel { Keys = [new AnimKey { Time = 0 }, new AnimKey { Time = 0.5, Post = ["0", "45", "0"] }] } }), animations: fresh);

        // The pack: what changed, under the game's ids, read back exactly.
        var pack = AnimationPack.Bedrock(project, "Dancing cows", "A test pack", new DateTime(2026, 11, 3));
        Program.Check(pack.Problems.Count == 0 && pack.Written.Contains(walkId) && pack.Written.Contains(fresh) && pack.Written.Contains(project.Model!.Id), $"the pack holds the changed walk, the new dance and the model ({string.Join("; ", pack.Problems)})");
        Program.Check(pack.Notes.Any(n => n.Contains("/playanimation") && n.Contains(fresh)) && pack.Notes.Any(n => n.Contains("Java")), "it says how to play the new animation, and why Java cannot use it");
        string file = Path.Combine(TestPaths.Output, "animations", "Dancing cows.mcpack");
        File.WriteAllBytes(file, pack.Data);
        var files = TexturePackReader.Files(file);
        Program.Check(files.ContainsKey("manifest.json") && files.Keys.Any(k => k.StartsWith("animations/")) && files.Keys.Any(k => k.StartsWith("models/entity/")), "manifest, animations and model in the pack");
        var readBack = MobCatalog.FromFiles(files, PackEdition.Bedrock, "test pack");
        Program.Check(readBack.Animations.ContainsKey(walkId) && readBack.Animations[walkId].Length == 0.5 && readBack.Models.ContainsKey(project.Model!.Id), "read as a pack again, it has the changes");
        var empty = new AnimationProject("cow", bedrock.ModelFor(key), bedrock.Animations, bedrock.AnimationsFor(key));
        Program.Check(AnimationPack.Bedrock(empty, "Nothing", "", DateTime.Now).Problems.Any(p => p.Contains("Nothing has been changed")), "an unchanged project is not saved as an empty pack without saying so");
    }

    static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    static void MolangChecks()
    {
        var ctx = new MolangContext();
        (string Expr, double Want)[] cases =
        [
            ("1 + 2 * 3", 7), ("(1 + 2) * 3", 9), ("-2 * -3", 6), ("10 % 4", 2), ("7 / 2", 3.5), ("1 / 0", 0),
            ("1 > 0 ? 5 : 6", 5), ("0 ? 5", 0), ("2 ? 5", 5), ("1 && 0", 0), ("1 || 0", 1), ("!0", 1), ("3 == 3", 1), ("3 != 3", 0),
            ("math.sin(90)", 1), ("math.cos(180)", -1), ("math.abs(-3)", 3), ("math.clamp(5, 0, 3)", 3), ("math.lerp(0, 10, 0.25)", 2.5),
            ("math.lerprotate(350, 10, 0.5)", 360), ("math.min(2, -1)", -1), ("math.pow(2, 10)", 1024), ("math.sqrt(16)", 4), ("math.floor(-1.5)", -2),
            ("math.mod(7, 3)", 1), ("math.pi", Math.PI), ("1.5f", 1.5), (".5", 0.5), ("2e2", 200), ("query.unknown_thing", 0), ("", 0),
            ("variable.not_set ?? 7", 7), ("'text' ? 1 : 2", 2), ("query.position(0)", 0), ("geometry.default", 0),
        ];
        foreach (var (expr, want) in cases)
            Program.Check(Near(Molang.Eval(expr, ctx), want), $"\"{expr}\" = {want} ({Molang.Eval(expr, ctx)})");
        ctx["query.anim_time"] = 1.5;
        Program.Check(Near(Molang.Eval("query.anim_time * 2", ctx), 3) && Near(Molang.Eval("q.anim_time * 2", ctx), 3), "queries, also by their short names");
        Program.Check(Near(Molang.Eval("v.x = 3; v.y = v.x * 2; return v.y;", ctx), 6) && Near(ctx["variable.y"], 6), "statements: variables set and returned");
        Program.Check(Near(Molang.Eval("v.x = 3; v.x * 2;", ctx), 0), "several statements without a return are 0, as in the game");
        Program.Check(Near(Molang.Eval("{ v.a = 2; return v.a * 3; }", ctx), 6), "a block of statements");
        ctx.This = 5;
        Program.Check(Near(Molang.Eval("this * 2", ctx), 10) && Near(Molang.Eval("90 - this", ctx), 85), "\"this\"");
        Program.Check(!Molang.IsValid("1 +") && !Molang.IsValid("(1") && !Molang.IsValid("math.sin(") && Molang.Eval("1 +", ctx) == 0, "unreadable formulas are found (and count as 0)");
        Program.Check(Near(Molang.Eval("math.cos(query.anim_time * 38.17) * 80.0", ctx), Math.Cos(1.5 * 38.17 * Math.PI / 180) * 80), "the walk formula of four-legged mobs");
    }

    static void Files()
    {
        string json = """
            { "format_version": "1.8.0", "animations": {
                "animation.test.a": { "loop": true, "animation_length": 2.0, "anim_time_update": "query.anim_time + query.delta_time",
                    "bones": { "head": { "rotation": [10, "math.sin(query.anim_time * 90) * 20", 0], "relative_to": { "rotation": "entity" } },
                               "body": { "position": { "0.0": [0, 0, 0], "1.0": { "pre": [0, 1, 0], "post": [0, 3, 0] }, "2.0": { "post": [0, 0, 0], "lerp_mode": "catmullrom" } } },
                               "leg": { "scale": 2 } },
                    "sound_effects": { "0.5": { "effect": "step" } } },
                "animation.test.b": { "loop": "hold_on_last_frame", "bones": { "tail": { "rotation": { "0.5": [0, 45, 0] } } } } } }
            """;
        var read = EntityAnimations.Read([("test.json", Encoding.UTF8.GetBytes(json))]);
        var a = read["animation.test.a"];
        Program.Check(read.Count == 2 && a.Loops && a.Length == 2 && a.AnimTimeUpdate != null && a.Bones.Count == 3, "an animation file: two animations, loop, length, time formula");
        Program.Check(a.Bone("head")!.Rotation!.Constant![1].StartsWith("math.sin") && a.Bone("head")!.Extra.ContainsKey("relative_to"), "a formula stays a formula; other bone settings are kept");
        var body = a.Bone("body")!.Position!;
        Program.Check(body.Keys.Count == 3 && body.Keys[1].Pre![1] == "1" && body.Keys[1].Post[1] == "3" && body.Keys[2].Lerp == KeyLerp.Smooth, "keyframes with a jump and smooth blending");
        Program.Check(a.Bone("leg")!.Scale!.Constant!.All(v => v == "2"), "one value scales all three axes");
        Program.Check(a.Extra.ContainsKey("sound_effects") && read["animation.test.b"].Holds && read["animation.test.b"].PlayLength == 0.5, "sounds kept; hold on the last frame; length from the last key");
        var again = EntityAnimations.Read([("again.json", EntityAnimations.File(read.Values))]);
        Program.Check(read.Keys.All(k => EntityAnimations.ToJson(read[k]).ToJsonString() == EntityAnimations.ToJson(again[k]).ToJsonString()), "written and read back: the same");
        Program.Check(EntityAnimations.Read([("bad.json", Encoding.UTF8.GetBytes("{ nope"))]).Count == 0, "a damaged file gives no animations (and no crash)");
    }

    static void Playback()
    {
        var ch = new AnimChannel { Keys = [new AnimKey { Time = 0, Post = ["0", "0", "0"] }, new AnimKey { Time = 1, Post = ["10", "20", "30"] }] };
        var ctx = new MolangContext();
        Program.Check(AnimationPlayer.Value(ch, 0.5, ctx, Vector3.Zero, 0) == new Vector3(5, 10, 15), "linear blend halfway");
        Program.Check(AnimationPlayer.Value(ch, -1, ctx, Vector3.Zero, 0) == Vector3.Zero && AnimationPlayer.Value(ch, 9, ctx, Vector3.Zero, 0) == new Vector3(10, 20, 30), "before the first and after the last key");
        var jump = new AnimChannel { Keys = [new AnimKey { Time = 0, Post = ["0", "0", "0"] }, new AnimKey { Time = 1, Pre = ["1", "1", "1"], Post = ["5", "5", "5"] }, new AnimKey { Time = 2, Post = ["5", "5", "5"] }] };
        Program.Check(AnimationPlayer.Value(jump, 0.999, ctx, Vector3.Zero, 0).X < 1.01f && AnimationPlayer.Value(jump, 1.0, ctx, Vector3.Zero, 0).X == 5, "a jump: up to the key the value before it, from it the value after");
        var smooth = new AnimChannel
        {
            Keys = [new AnimKey { Time = 0, Post = ["0", "0", "0"], Lerp = KeyLerp.Smooth }, new AnimKey { Time = 1, Post = ["10", "0", "0"], Lerp = KeyLerp.Smooth },
                    new AnimKey { Time = 2, Post = ["0", "0", "0"], Lerp = KeyLerp.Smooth }],
        };
        Program.Check(AnimationPlayer.Value(smooth, 1, ctx, Vector3.Zero, 0).X == 10 && AnimationPlayer.Value(smooth, 0.5, ctx, Vector3.Zero, 0).X > 5, "smooth passes through its keys and bulges between");
        var formula = new AnimChannel { Constant = ["query.anim_time * 10", "this", "0"] };
        ctx["query.anim_time"] = 2;
        Program.Check(AnimationPlayer.Value(formula, 2, ctx, new Vector3(0, 7, 0), 0) == new Vector3(20, 7, 0), "a formula with the time and \"this\"");
        var loop = new EntityAnimation { Id = "l", Loop = "true", Length = 2 };
        var hold = new EntityAnimation { Id = "h", Loop = "hold_on_last_frame", Length = 2 };
        Program.Check(Near(AnimationPlayer.TimeIn(loop, 5.5), 1.5) && Near(AnimationPlayer.TimeIn(hold, 5.5), 2), "looping wraps; holding stops at the end");
        var p1 = new Dictionary<string, BonePose> { ["a"] = new(new Vector3(10, 0, 0), Vector3.Zero, new Vector3(2)) };
        var p2 = new Dictionary<string, BonePose> { ["a"] = new(new Vector3(5, 0, 0), new Vector3(0, 1, 0), new Vector3(3)) };
        var both = AnimationPlayer.Combine([p1, p2])["a"];
        Program.Check(both.Rotation.X == 15 && both.Position.Y == 1 && both.Scale.X == 6, "animations together: turns and moves add, scales multiply");

        // A posed bone: turned 90° about x around its pivot, then moved up 2.
        var model = new EntityModel { Id = "m", Bones = [new ModelBone { Name = "a", Pivot = new(0, 0, 0), Cubes = [new ModelCube { Origin = new(0, 0, 0), Size = new(1, 4, 1), BoxUv = new(0, 0) }] }] };
        var posed = ModelMesh.Build(model, null, null, new Dictionary<string, BonePose> { ["a"] = new(new Vector3(90, 0, 0), new Vector3(0, 2, 0), Vector3.One) });
        Program.Check(Math.Abs(posed.Min.Z - -4) < 0.01f && Math.Abs(posed.Min.Y - 2) < 0.01f && Math.Abs(posed.Max.Y - 3) < 0.01f, $"an animation pose turns and moves the bone ({posed.Min} … {posed.Max})");
        var big = ModelMesh.Build(model, null, null, new Dictionary<string, BonePose> { ["a"] = new(Vector3.Zero, Vector3.Zero, new Vector3(2)) });
        Program.Check(Math.Abs(big.Max.Y - 8) < 0.01f, "scale grows the bone around its pivot");
    }

    static void Installed()
    {
        var bedrock = MobCatalog.Bedrock();
        if (bedrock == null) { Console.WriteLine("   (Bedrock is not installed: skipped)"); return; }
        Console.WriteLine($"   {bedrock.Animations.Count} animations, {bedrock.AnimationsOf.Count} entities with animations");
        Program.Check(bedrock.Animations.Count > 300, "the game's animations are read");
        // Every formula in every animation can be read.
        int formulas = 0, bad = 0;
        var badOnes = new List<string>();
        foreach (var a in bedrock.Animations.Values)
            foreach (var b in a.Bones)
                foreach (var (_, ch) in b.Channels())
                    foreach (var v in (ch.Constant ?? []).Concat(ch.Keys.SelectMany(k => k.Post.Concat(k.Pre ?? []))))
                    {
                        formulas++;
                        if (!Molang.IsValid(v)) { bad++; if (badOnes.Count < 5) badOnes.Add($"{a.Id}/{b.Bone}: {v}"); }
                    }
        Program.Check(bad == 0, $"all {formulas} animation formulas can be read ({bad} cannot: {string.Join(" | ", badOnes)})");
        // Every animation round-trips through writing.
        int changed = bedrock.Animations.Values.Count(a => EntityAnimations.ToJson(a).ToJsonString() !=
            EntityAnimations.ToJson(EntityAnimations.Read([("x", EntityAnimations.File([a]))])[a.Id]).ToJsonString());
        Program.Check(changed == 0, $"every animation is written back the same ({changed} differ)");
        var cow = bedrock.AnimationsFor("entity/cow/cow_v2");
        Program.Check(cow.Any(c => c.Name == "walk") && cow.Any(c => c.Name == "setup"), $"a cow's animations: {string.Join(", ", cow.Select(c => c.Name))}");

        // Pictures mid-animation.
        string outDir = Path.Combine(TestPaths.Output, "animations");
        Directory.CreateDirectory(outDir);
        foreach (var f in Directory.EnumerateFiles(outDir, "*.png")) File.Delete(f);
        foreach (var (key, name, times) in new (string, string, double[])[]
        {
            ("entity/cow/cow_v2", "walk", [0.1, 0.3]), ("entity/chicken/chicken", "general", [0.1]), ("entity/creeper/creeper", "creeper_legs", [0.2]),
            ("entity/zombie/zombie", "humanoid_base_pose", [0]), ("entity/bee/bee", "flying", [0.05]), ("entity/allay/allay", "idle", [0.5]),
        })
        {
            var anims = bedrock.AnimationsFor(key);
            var chosen = anims.FirstOrDefault(a => a.Name == name);
            if (chosen.Id == null) { Console.WriteLine($"   {key}: no '{name}' animation ({string.Join(", ", anims.Select(a => a.Name))})"); continue; }
            var model = bedrock.ModelFor(key)!;
            var tex = bedrock.Textures.Textures[key].Image;
            foreach (var t in times)
            {
                var ctx = new MolangContext();
                ctx["query.modified_move_speed"] = 1;
                ctx["query.modified_distance_moved"] = t * 8;
                ctx["query.life_time"] = t;
                var poses = new List<Dictionary<string, BonePose>>();
                if (anims.FirstOrDefault(a => a.Name == "setup").Id is { } setup) poses.Add(AnimationPlayer.Pose(bedrock.Animations[setup], 0, ctx, model));
                poses.Add(AnimationPlayer.Pose(bedrock.Animations[chosen.Id], t, ctx, model));
                var mesh = ModelMesh.Build(model, null, null, AnimationPlayer.Combine(poses));
                var r = new ModelRenderer();
                r.Render(mesh, tex, ModelRenderer.CameraFor(mesh) with { Yaw = 270 }, new ModelViewOptions { ShowGrid = false }, 320, 320);
                File.WriteAllBytes(Path.Combine(outDir, $"{key.Replace('/', '_')}_{name}_{t:0.00}.png"), new RgbaImage(320, 320, r.Frame.Select(p => unchecked((uint)p)).ToArray()).ToPng());
            }
        }
        Console.WriteLine("   pictures: " + outDir);
    }
}
