// McTests/MobTests.cs: "mobs" — the Mob Studio's catalog and models, against the games installed on this PC.
// Owns: MobTests — model files of every form (hand-written ones checked exactly: box UV layout, mirror, inflate,
// per-face UV, inheritance, turns), the installed Bedrock game's entities/models/textures, a Java jar's textures with
// models borrowed from Bedrock, painting through the 3D picker landing on the right texture pixel, and pictures of
// mobs written to the test output folder (mobs/*.png) to look at.
using System.Numerics;
using System.Text;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;

static class MobTests
{
    public static void Run(string[] args)
    {
        Console.WriteLine("== Model files");
        Files();
        Console.WriteLine("== Faces and turns");
        Faces();
        Console.WriteLine("== Mob packs: saved and read back");
        Packs();
        Console.WriteLine("== Installed games");
        Installed(args);
    }

    static void Packs()
    {
        var names = TextureNames.Default;
        var img = new RgbaImage(64, 32);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = 0xFF000000 | (uint)(i * 2654435761u >> 8);
        foreach (var edition in new[] { PackEdition.Java, PackEdition.Bedrock })
        {
            var pack = new TexturePack { Name = "Mob test " + edition, Edition = edition };
            pack.Textures["entity/cow/cow"] = new PackTexture { Key = "entity/cow/cow", Image = img.Clone() };
            pack.Textures["entity/my_mob/odd"] = new PackTexture { Key = "entity/my_mob/odd", Image = img.Clone() };
            var result = edition == PackEdition.Java
                ? TexturePackWriter.Java(pack, JavaReleases.Newest, names, null, new HashSet<string> { "entity/cow/cow" })
                : TexturePackWriter.Bedrock(pack, names, new DateTime(2026, 11, 3), null, new HashSet<string> { "entity/cow/cow" });
            Program.Check(result.Problems.Count == 0 && result.Written.Contains("entity/cow/cow"), $"{edition}: a mob pack is written ({string.Join("; ", result.Problems)})");
            Program.Check(result.Notes.Any(n => n.Contains("entity/my_mob/odd")), $"{edition}: a mob name the game does not use is pointed out");
            string file = Path.Combine(TestPaths.Output, "mobs", "pack_" + edition + (edition == PackEdition.Java ? ".zip" : ".mcpack"));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, result.Data);
            var back = TexturePackReader.Read(file, names);
            int taken = MobCatalog.TakeMobTextures(back);
            Program.Check(taken == 2 && back.Textures.TryGetValue("entity/cow/cow", out var cow) && cow.Image.SamePixels(img) && !back.OtherFiles.Keys.Any(k => k.Contains("entity/")),
                $"{edition}: read back, the mob textures are editable again, pixel for pixel ({taken})");
        }
    }

    static Dictionary<string, EntityModel> Parse(string json) => EntityModels.Read([("test.geo.json", Encoding.UTF8.GetBytes(json))]);

    static void Files()
    {
        var old = Parse("""
            { "format_version": "1.8.0",
              "geometry.base": { "texturewidth": 64, "textureheight": 32, "bones": [ { "name": "body", "pivot": [0, 12, 0], "cubes": [ { "origin": [-4, 0, -2], "size": [8, 12, 4], "uv": [16, 16] } ] },
                                                                              { "name": "head", "parent": "body", "pivot": [0, 12, 0], "cubes": [ { "origin": [-4, 12, -4], "size": [8, 8, 8], "uv": [0, 0] } ] } ] },
              "geometry.child:geometry.base": { "bones": [ { "name": "head", "pivot": [0, 12, 0], "cubes": [ { "origin": [-3, 12, -3], "size": [6, 6, 6], "uv": [0, 0], "inflate": 0.5 } ] },
                                                           { "name": "hat", "parent": "head", "pivot": [0, 12, 0], "rotation": ["10", 0, 0], "cubes": [] } ] } }
            """);
        Program.Check(old.Count == 2 && old["geometry.base"].TextureHeight == 32 && old["geometry.base"].Bones.Count == 2, "1.8 format: two models, texture size");
        var child = old["geometry.child"];
        Program.Check(child.TextureWidth == 64 && child.TextureHeight == 32 && child.Bones.Count == 3 && child.Bones.First(b => b.Name == "head").Cubes[0].Size == new Vector3(6, 6, 6) &&
                      child.Bones.First(b => b.Name == "head").Cubes[0].Inflate == 0.5f, "inheritance: the parent's bones, a bone of the same name replaced, the texture size inherited");
        Program.Check(child.Bones.First(b => b.Name == "hat").Rotation == new Vector3(10, 0, 0), "numbers written as text are read");

        var modern = Parse("""
            { "format_version": "1.12.0", "minecraft:geometry": [ { "description": { "identifier": "geometry.thing", "texture_width": 32, "texture_height": 16 },
              "bones": [ { "name": "b", "pivot": [0, 0, 0], "mirror": true, "cubes": [ { "origin": [0, 0, 0], "size": [2, 3, 4], "uv": [0, 0] },
                                                                                   { "origin": [0, 0, 0], "size": [2, 2, 2], "uv": { "north": { "uv": [4, 4], "uv_size": [-2, 2] }, "up": { "uv": [0, 0], "uv_size": [2, 2], "uv_rotation": 90 } } } ] } ] } ] }
            """);
        var thing = modern["geometry.thing"];
        Program.Check(thing.TextureWidth == 32 && thing.TextureHeight == 16 && thing.Bones[0].Mirror && thing.Bones[0].Cubes[1].Faces!.Count == 2, "1.12 format: description, mirror, per-face UV");
        Program.Check(Parse("{ not json").Count == 0, "a damaged file gives no models (and no crash)");
    }

    static void Faces()
    {
        // A cube with box UV at (0, 0), size 2 × 3 × 4: the game's layout.
        var model = new EntityModel
        {
            Id = "t", TextureWidth = 32, TextureHeight = 16,
            Bones = [new ModelBone { Name = "b", Cubes = [new ModelCube { Origin = new(0, 0, 0), Size = new(2, 3, 4), BoxUv = new(0, 0) }] }],
        };
        var mesh = ModelMesh.Build(model);
        var f = mesh.Quads.ToDictionary(q => q.Face);
        Program.Check(mesh.Quads.Count == 6, "six faces");
        Program.Check(f["east"].TexRect == (0, 4, 4, 3) && f["north"].TexRect == (4, 4, 2, 3) && f["west"].TexRect == (6, 4, 4, 3) && f["south"].TexRect == (10, 4, 2, 3) &&
                      f["up"].TexRect == (4, 0, 2, 4) && f["down"].TexRect == (6, 0, 2, 4), "box UV: east, north, west, south in a row; up and down above");
        // North face: its texture's top-left is the top of the cube's east edge (the mob's right, seen from the front).
        var n = f["north"];
        Program.Check(n.Origin == new Vector3(2, 3, 0) && n.U == new Vector3(-2, 0, 0) && n.V == new Vector3(0, -3, 0), "north face runs from east to west, top to bottom");
        Program.Check(Vector3.Dot(Vector3.Cross(n.V, n.U), n.Normal) > 0 || Vector3.Dot(Vector3.Cross(n.U, n.V), n.Normal) > 0, "north face points north");

        var mirrored = ModelMesh.Build(new EntityModel
        {
            Id = "m", TextureWidth = 32, TextureHeight = 16,
            Bones = [new ModelBone { Name = "b", Mirror = true, Cubes = [new ModelCube { Origin = new(0, 0, 0), Size = new(2, 3, 4), BoxUv = new(0, 0) }] }],
        }).Quads.ToDictionary(q => q.Face);
        Program.Check(mirrored["east"].TexRect == (6, 4, 4, 3) && mirrored["west"].TexRect == (0, 4, 4, 3) && mirrored["north"].TexW < 0, "mirror: east and west swap, pictures flip");

        var inflated = ModelMesh.Build(new EntityModel
        {
            Id = "i", Bones = [new ModelBone { Name = "b", Cubes = [new ModelCube { Origin = new(0, 0, 0), Size = new(2, 2, 2), BoxUv = new(0, 0), Inflate = 0.5f }] }],
        });
        Program.Check(inflated.Min == new Vector3(-0.5f) && inflated.Max == new Vector3(2.5f) && inflated.Quads.First(q => q.Face == "north").TexRect == (2, 2, 2, 2), "inflate grows the box, not its texture");

        // Turns: Bedrock's +90° about x lays an upright body forward (its top toward the head at −z), as the cow needs.
        var cowBody = ModelMesh.Build(new EntityModel
        {
            Id = "c", Bones = [new ModelBone { Name = "body", Pivot = new(0, 19, 2), Rotation = new(90, 0, 0), Cubes = [new ModelCube { Origin = new(-6, 11, -5), Size = new(12, 18, 10), BoxUv = new(18, 4) }] }],
        });
        Program.Check(Math.Abs(cowBody.Min.Z - -8) < 0.01f && Math.Abs(cowBody.Max.Z - 10) < 0.01f && Math.Abs(cowBody.Min.Y - 12) < 0.01f && Math.Abs(cowBody.Max.Y - 22) < 0.01f,
            $"a cow's body lies level from z −8 (the head) to 10, y 12 to 22 ({cowBody.Min} … {cowBody.Max})");
        // An old bind pose turns only the bone's own cubes; a bone hanging from it stays where it is written.
        var legacy = ModelMesh.Build(new EntityModel
        {
            Id = "l", Bones =
            [
                new ModelBone { Name = "body", Pivot = new(0, 19, 2), BindPose = new(90, 0, 0), Cubes = [new ModelCube { Origin = new(-6, 11, -5), Size = new(12, 18, 10), BoxUv = new(18, 4) }] },
                new ModelBone { Name = "head", Parent = "body", Pivot = new(0, 20, -8), Cubes = [new ModelCube { Origin = new(-4, 16, -14), Size = new(8, 8, 6), BoxUv = new(0, 0) }] },
            ],
        });
        var head = legacy.Quads.Where(q => q.Bone == "head").SelectMany(q => new[] { q.Origin, q.Origin + q.U + q.V }).ToList();
        var body = legacy.Quads.Where(q => q.Bone == "body").SelectMany(q => new[] { q.Origin, q.Origin + q.U + q.V }).ToList();
        Program.Check(head.Min(p => p.Z) == -14 && head.Max(p => p.Y) == 24 && Math.Abs(body.Min(p => p.Z) - -8) < 0.01f, "a bind pose lays the body level and leaves the head (hanging from it) where it is");
        // A cube turned without a pivot turns around its middle (the new pig's body).
        var pig = ModelMesh.Build(new EntityModel
        {
            Id = "p", Bones = [new ModelBone { Name = "body", Cubes = [new ModelCube { Origin = new(-5, 2, -5), Size = new(10, 16, 8), Rotation = new(90, 0, 0), BoxUv = new(28, 8) }] }],
        });
        Program.Check(Math.Abs(pig.Min.Y - 6) < 0.01f && Math.Abs(pig.Max.Y - 14) < 0.01f, $"a cube without a pivot turns around its middle ({pig.Min} … {pig.Max})");
        // "neverRender" hides a bone's own cubes, not the bones hanging from it.
        var waist = ModelMesh.Build(new EntityModel
        {
            Id = "w", Bones =
            [
                new ModelBone { Name = "waist", NeverRender = true, Cubes = [new ModelCube { Origin = new(0, 0, 0), Size = new(1, 1, 1), BoxUv = new(0, 0) }] },
                new ModelBone { Name = "body", Parent = "waist", Cubes = [new ModelCube { Origin = new(0, 0, 0), Size = new(2, 2, 2), BoxUv = new(0, 0) }] },
            ],
        });
        Program.Check(waist.Quads.Count == 6 && waist.Quads.All(q => q.Bone == "body"), "neverRender hides the waist's cube but not the body hanging from it");
        Program.Check(ModelMesh.Build(waist.Model, new HashSet<string> { "waist" }).Quads.Count == 0, "a part the player hides hides what hangs from it");
        var t = ModelMesh.Turn(new Vector3(0, 90, 0));
        Program.Check(Vector3.Distance(Vector3.Transform(Vector3.UnitX, t), -Vector3.UnitZ) < 1e-5f, "a +90° turn about y takes east to north");
        t = ModelMesh.Turn(new Vector3(0, 0, 90));
        Program.Check(Vector3.Distance(Vector3.Transform(Vector3.UnitX, t), -Vector3.UnitY) < 1e-5f, "a +90° turn about z takes east down");

        // Painting through the 3D picker lands on the texture pixel the face shows.
        var tex = new RgbaImage(32, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++) tex[x, y] = RgbaImage.Argb(255, x * 8, y * 16, 128);
        var renderer = new ModelRenderer();
        var cube = ModelMesh.Build(model);
        var cam = new OrbitCamera(180, 0, 40, cube.Centre);
        renderer.Render(cube, tex, cam, new ModelViewOptions(), 200, 200);
        // Seen from the front (north), the screen's middle is the north face's middle: texture (5, 5).
        Program.Check(renderer.Pick(100, 100) == (5, 5), $"the middle of the front shows texture pixel 5, 5 ({renderer.Pick(100, 100)})");
        // Left of the middle on screen is the cube's east side (+x is the viewer's left when facing it): column 4 of the face.
        var left = renderer.Pick(100 - 6, 100);
        Program.Check(left == (4, 5), $"the viewer's left of the front shows the face's first column, the mob's right ({left})");
    }

    static void Installed(string[] args)
    {
        string outDir = Path.Combine(TestPaths.Output, "mobs");
        Directory.CreateDirectory(outDir);
        var bedrock = MobCatalog.Bedrock();
        if (bedrock == null) { Console.WriteLine("   (Bedrock is not installed: skipped)"); return; }
        int withModel = bedrock.Info.Values.Count(i => i.GeometryId != null);
        Console.WriteLine($"   Bedrock: {bedrock.Models.Count} models, {bedrock.Textures.Textures.Count} entity textures, {withModel} with a model, {bedrock.Notes.Count} notes");
        foreach (var note in bedrock.Notes.Take(5)) Console.WriteLine("     note: " + note);
        Program.Check(bedrock.Models.Count > 80 && bedrock.Textures.Textures.Count > 300, "Bedrock: models and textures found");
        Program.Check(!bedrock.Textures.Textures.Keys.Any(MobCatalog.IsMaterialMap), "material maps (_mers …) are not listed as pictures");
        Program.Check(bedrock.Describe("entity/pig/pig_temperate_baby").GeometryId is { } baby && baby.Contains("baby"), $"a baby texture uses the baby model ({bedrock.Describe("entity/pig/pig_temperate_baby").GeometryId})");
        foreach (var id in new[] { "entity/cow/cow", "entity/pig/pig", "entity/creeper/creeper", "entity/zombie/zombie", "entity/skeleton/skeleton", "entity/spider/spider", "entity/chicken", "entity/sheep/sheep", "entity/wolf/wolf" })
        {
            var info = bedrock.Info.GetValueOrDefault(id);
            Program.Check(info?.GeometryId != null && bedrock.ModelFor(id) != null, $"Bedrock {id}: {info?.Display} → {info?.GeometryId}");
        }
        string[] wanted = args.Length > 0 ? args : ["entity/cow/cow", "entity/pig/pig", "entity/chicken", "entity/creeper/creeper", "entity/spider/spider", "entity/wolf/wolf", "entity/cat/tabby",
            "entity/horse/horse_brown", "entity/villager2/villager", "entity/zombie/zombie", "entity/sheep/sheep", "entity/squid/squid", "entity/bat", "entity/ghast/ghast", "entity/fox/fox", "entity/rabbit/brown",
            "entity/cow/cow_v2", "entity/pig/pig_v3", "entity/cat/allblackcat", "entity/panda/panda", "entity/pig/pig_temperate_baby", "entity/cow/cow_cold_baby", "entity/llama/llama_creamy", "entity/axolotl/axolotl_lucy", "entity/bee/bee", "entity/panda/panda", "entity/allay/allay", "entity/frog/temperate_frog", "entity/camel/camel"];
        foreach (var key in wanted)
        {
            var model = bedrock.ModelFor(key);
            if (model == null || !bedrock.Textures.Textures.TryGetValue(key, out var t)) { Console.WriteLine($"   {key}: no model or texture"); continue; }
            Picture(Path.Combine(outDir, "bedrock_" + key.Replace('/', '_') + ".png"), ModelMesh.Build(model, null, bedrock.PoseFor(key)), t.Image);
        }
        Console.WriteLine("   pictures: " + outDir);

        // Java: textures from the newest jar, models borrowed from Bedrock.
        var jars = GameFolders.JavaJars();
        if (jars.Count == 0) { Console.WriteLine("   (no Java jar installed: skipped)"); return; }
        var (version, jar) = jars[0];
        var java = MobCatalog.Java(jar, version);
        java.BorrowModels(bedrock);
        int javaModels = java.Info.Values.Count(i => i.GeometryId != null);
        Console.WriteLine($"   Java {version}: {java.Textures.Textures.Count} entity textures, {javaModels} drawn on a Bedrock model");
        Program.Check(java.Textures.Textures.Count > 300 && javaModels > 150, $"Java {version}: textures found, most with a model");
        Program.Check(java.Describe("entity/creeper/creeper").Mob == "Creeper", $"Java names come from the game ({java.Describe("entity/creeper/creeper").Mob})");
        int mismatched = java.Info.Keys.Count(k => java.ModelFor(k) is { } jm && (long)jm.TextureWidth * java.Textures.Textures[k].Image.Height != (long)jm.TextureHeight * java.Textures.Textures[k].Image.Width);
        Program.Check(mismatched == 0, $"every borrowed model is made for a texture of that shape ({mismatched} are not)");
        Program.Check(java.Describe("entity/zombie/zombie").GeometryId is { } jz && jz.StartsWith("geometry.zombie") && java.ModelFor("entity/zombie/zombie")!.TextureHeight == 64,
            $"Java's 64 × 64 zombie (the 64 × 32 layout on a taller canvas) is drawn on the zombie model, pixel for pixel ({java.Describe("entity/zombie/zombie").GeometryId})");
        Program.Check(java.ModelFor("entity/fox/fox") == null, "Java's fox (laid out differently from Bedrock's) has no borrowed model: it is painted flat");
        foreach (var key in new[] { "entity/creeper/creeper", "entity/zombie/zombie", "entity/pig/pig_temperate", "entity/cow/cow_temperate", "entity/chicken/chicken_temperate", "entity/pig/pig_temperate_baby", "entity/cow/cow_temperate_baby" })
        {
            if (java.ModelFor(key) is not { } m || !java.Textures.Textures.TryGetValue(key, out var t)) { Console.WriteLine($"   Java {key}: no model"); continue; }
            Picture(Path.Combine(outDir, "java_" + key.Replace('/', '_') + ".png"), ModelMesh.Build(m, null, java.PoseFor(key)), t.Image);
        }
    }

    static void Picture(string path, ModelMesh mesh, RgbaImage texture)
    {
        var r = new ModelRenderer();
        var cam = ModelRenderer.CameraFor(mesh);
        r.Render(mesh, texture, cam, new ModelViewOptions { ShowGrid = false }, 320, 320);
        var img = new RgbaImage(320, 320, r.Frame.Select(p => unchecked((uint)p)).ToArray());
        File.WriteAllBytes(path, img.ToPng());
    }
}
