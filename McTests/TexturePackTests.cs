// McTests/TexturePackTests.cs: textures: texture packs read and written for every Java era and Bedrock, with a
// small hand-made name table that covers the hard cases (a rename inside one release line, 1.12 names,
// terrain.png tiles, animations, textures the table does not know); then, when the shipped table is there, real
// packs made from the installed games.
// Owns: TexturePackTests.
using System.IO.Compression;
using System.Text.Json;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;

/// <summary>
/// textures: texture packs read and written for every Java era and Bedrock, with a small hand-made name table that
/// covers the hard cases (a rename inside one release line, 1.12 names, terrain.png tiles, animations, textures the
/// table does not know); then, when the shipped table is there, real packs made from the installed games.
/// </summary>
static class TexturePackTests
{
    const string Table = """
        canonical,platform,name,from,to,evidence,note
        stone,java,block/stone,1.13,,pixels,
        stone,java,blocks/stone,1.7.2,1.12.2,pixels,
        stone,java_atlas,terrain:1,a1.2.2,1.4.7,pixels,
        stone,bedrock,blocks/stone,,,pixels,
        smooth_stone,java,block/stone_slab_top,1.13,1.13.2,manual,
        smooth_stone,java,block/smooth_stone,1.14,,manual,
        smooth_stone,java,blocks/stone_slab_top,1.7.2,1.12.2,manual,
        smooth_stone,bedrock,blocks/stone_slab_top,,,manual,
        water_still,java,block/water_still,1.13,,pixels,
        water_still,bedrock,blocks/water_still_grey,,,pixels,
        sea_lantern,java,block/sea_lantern,1.13,,pixels,
        sea_lantern,bedrock,blocks/sea_lantern,,,pixels,
        """;

    public static void Run(string[] args)
    {
        var names = TextureNames.Load(new StringReader(Table));
        Console.WriteLine("== Names across versions");
        JavaRelease R(string first) => JavaReleases.All[JavaReleases.Index(first)];
        Program.Check(names.JavaNames("smooth_stone", R("1.13")).OrderBy(n => n).SequenceEqual(["block/smooth_stone", "block/stone_slab_top"]),
            "1.13–1.14.4 spans the 1.14 rename: both names");
        Program.Check(names.JavaNames("smooth_stone", R("1.16.2")).SequenceEqual(["block/smooth_stone"]) && names.JavaNames("smooth_stone", R("1.11")).SequenceEqual(["blocks/stone_slab_top"]),
            "one name where there was no rename");
        Program.Check(names.FromJava("blocks/stone", R("1.11")) == "stone" && names.FromBedrock("blocks/water_still_grey") == "water_still" && names.AtlasIndex("stone", R("Alpha 1.2.2")) == 1,
            "names lead back to the shared name");
        Program.Check(names.JavaNames("stone", R("1.6.1")).Count == 0, "no names are made up for 1.6 (it used older names)");

        Console.WriteLine("== Java and Bedrock packs, written and read back");
        var pack = new TexturePack { Name = "Test Pack", Description = "A test", Icon = Solid(64, 0xFF336699) };
        pack.Textures["stone"] = new PackTexture { Key = "stone", Image = Noise(16, 16, 1) };
        pack.Textures["smooth_stone"] = new PackTexture { Key = "smooth_stone", Image = Noise(16, 16, 2) };
        pack.Textures["water_still"] = new PackTexture
        {
            Key = "water_still", Image = Noise(16, 64, 3),
            Animation = new TextureAnimation { FrameTime = 2, Interpolate = true, Frames = [(0, null), (2, 5), (1, null), (3, null)] },
        };
        pack.Textures["sea_lantern"] = new PackTexture { Key = "sea_lantern", Image = Noise(16, 80, 4), Animation = new TextureAnimation { FrameTime = 5 } };
        pack.Textures["java:block/my_mod_block"] = new PackTexture { Key = "java:block/my_mod_block", Image = Noise(16, 16, 5) };

        var j113 = TexturePackWriter.Java(pack, R("1.13"), names);
        var e113 = Entries(j113.Data);
        Program.Check(j113.Problems.Count == 0 && e113.Contains("assets/minecraft/textures/block/smooth_stone.png") && e113.Contains("assets/minecraft/textures/block/stone_slab_top.png")
                      && e113.Contains("assets/minecraft/textures/block/water_still.png.mcmeta") && e113.Contains("assets/minecraft/textures/block/my_mod_block.png"),
            "1.13–1.14.4 pack: both smooth stone names, animations, the mod texture kept " + string.Join("; ", j113.Problems));
        Program.Check(Meta(j113.Data) == "pack_format=4", "pack format 4");
        var back = TexturePackReader.Read(Save(j113, "j113.zip"), names);
        Program.Check(back.Textures["water_still"].Animation is { FrameTime: 2, Interpolate: true, Frames: { Count: 4 } f } && f[1] == (2, 5) && back.Textures["water_still"].Image.SamePixels(pack.Textures["water_still"].Image),
            "the animation reads back exactly");
        Program.Check(back.Textures.ContainsKey("java:block/my_mod_block") && back.Textures["stone"].Image.SamePixels(pack.Textures["stone"].Image) && back.JavaRelease?.First == "1.13",
            "textures and version read back");

        var j112 = TexturePackWriter.Java(pack, R("1.11"), names);
        var e112 = Entries(j112.Data);
        Program.Check(j112.Problems.Count == 0 && e112.Contains("assets/minecraft/textures/blocks/stone.png") && e112.Contains("assets/minecraft/textures/blocks/stone_slab_top.png")
                      && !e112.Any(e => e.Contains("my_mod_block")) && j112.NotIncluded.Any(n => n.StartsWith("My mod block")) && j112.NotIncluded.Any(n => n.StartsWith("Water still")),
            "1.11–1.12.2 pack: old names; the mod texture and water (no 1.12 name in this table) are listed as not included");
        Program.Check(Meta(j112.Data) == "pack_format=3", "pack format 3");

        var j26 = TexturePackWriter.Java(pack, R("26.3"), names);
        Program.Check(j26.Problems.Count == 0 && Meta(j26.Data) == "min_format=[97,1] max_format=97" && Entries(j26.Data).Contains("assets/minecraft/textures/block/smooth_stone.png")
                      && !Entries(j26.Data).Contains("assets/minecraft/textures/block/stone_slab_top.png"), "26.3 pack: new format fields, only the new name");

        var empty16 = TexturePackWriter.Java(pack, R("1.6.1"), names);
        Program.Check(empty16.Problems.Any(p => p.Contains("1.5 to 1.6.4")), "1.6: refused with the reason (names unknown), never an empty pack passed off as done");

        var alpha = TexturePackWriter.Java(pack, R("Alpha 1.2.2"), names);
        using (var z = new ZipArchive(new MemoryStream(alpha.Data)))
        {
            var atlas = Png.Decode(ReadAll(z.GetEntry("terrain.png")!));
            Program.Check(alpha.Problems.Count == 0 && atlas.Width == 256 && atlas.Crop(16, 0, 16, 16).SamePixels(pack.Textures["stone"].Image) && alpha.Notes.Any(n => n.Contains("see-through")),
                "terrain.png: stone in tile 1; with nothing to fill the rest, the player is told");
        }
        var fill = new TexturePack { Name = "Java b1.0" };
        fill.Textures["stone"] = new PackTexture { Key = "stone", Image = Solid(16, 0xFF777777) };
        var filledAtlas = TexturePackWriter.Java(new TexturePack { Name = "Only Water" }, R("Alpha 1.2.2"), names, fill);
        Program.Check(filledAtlas.Problems.Any(p => p.Contains("nothing to put")), "an atlas with none of the pack's textures is refused");

        var animated = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "textures/blocks/water_still_grey" };
        var bed = TexturePackWriter.Bedrock(pack, names, new DateTime(2026, 11, 1, 12, 0, 0), animated);
        var eb = Entries(bed.Data);
        Program.Check(bed.Problems.Count == 0 && eb.Contains("manifest.json") && eb.Contains("pack_icon.png") && eb.Contains("textures/blocks/stone_slab_top.png") && eb.Contains("textures/blocks/water_still_grey.png"),
            "Bedrock pack: manifest, icon, Bedrock names " + string.Join("; ", bed.Problems));
        using (var z = new ZipArchive(new MemoryStream(bed.Data)))
        {
            var water = Png.Decode(ReadAll(z.GetEntry("textures/blocks/water_still_grey.png")!));
            var lantern = Png.Decode(ReadAll(z.GetEntry("textures/blocks/sea_lantern.png")!));
            Program.Check(water.Height == 64 && lantern.Height == 16 && bed.Notes.Any(n => n.Contains("sea_lantern")), "frames kept where Bedrock animates, first frame elsewhere (with a note)");
            using var m = JsonDocument.Parse(ReadAll(z.GetEntry("manifest.json")!));
            Program.Check(m.RootElement.GetProperty("modules")[0].GetProperty("type").GetString() == "resources" && m.RootElement.GetProperty("format_version").GetInt32() == 2, "resources module");
        }
        Program.Check(bed.NotIncluded.Any(n => n.StartsWith("My mod block")), "the mod texture is listed as not included in Bedrock");
        var bback = TexturePackReader.Read(Save(bed, "bed.mcpack"), names);
        Program.Check(bback.Edition == PackEdition.Bedrock && bback.Name == "Test Pack" && bback.Textures["water_still"].Image.SamePixels(pack.Textures["water_still"].Image) && bback.Icon != null,
            "the Bedrock pack reads back");

        // Bedrock to Java: a flipbook becomes a .mcmeta animation.
        var bpack = new TexturePack { Name = "From Bedrock", Edition = PackEdition.Bedrock };
        bpack.Textures["water_still"] = new PackTexture { Key = "water_still", Image = Noise(16, 32, 6), Animation = new TextureAnimation { FrameTime = 3, Interpolate = true } };
        bpack.Textures["bedrock:blocks/camera_top"] = new PackTexture { Key = "bedrock:blocks/camera_top", Image = Noise(16, 16, 7) };
        var toJava = TexturePackWriter.Java(bpack, R("1.21.11"), names);
        using (var z = new ZipArchive(new MemoryStream(toJava.Data)))
        {
            using var mc = JsonDocument.Parse(ReadAll(z.GetEntry("assets/minecraft/textures/block/water_still.png.mcmeta")!));
            var a = mc.RootElement.GetProperty("animation");
            Program.Check(toJava.Problems.Count == 0 && a.GetProperty("frametime").GetInt32() == 3 && a.GetProperty("interpolate").GetBoolean() && toJava.NotIncluded.Any(n => n.Contains("only Bedrock")),
                "Bedrock → Java: animation timing kept; Bedrock-only textures listed");
        }

        Console.WriteLine("== Someone's own pack, saved again, keeps what this program does not edit");
        {
            string dir = Path.Combine(TestPaths.Output, "textures", "own-java");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(Path.Combine(dir, "assets", "minecraft", "textures", "block"));
            string meta = """{"pack":{"pack_format":15,"description":"Mine"},"language":{"xx_yy":{"name":"X","region":"Y","bidirectional":false}},"filter":{"block":[{"namespace":"foo"}]}}""";
            File.WriteAllText(Path.Combine(dir, "pack.mcmeta"), meta);
            File.WriteAllBytes(Path.Combine(dir, "assets", "minecraft", "textures", "block", "stone.png"), Noise(16, 16, 9).ToPng());
            File.WriteAllText(Path.Combine(dir, "assets", "minecraft", "textures", "block", "stone.png.mcmeta"), """{"texture":{"blur":true}}""");
            var own = TexturePackReader.Read(dir, names);
            var saved = TexturePackWriter.Java(own, own.JavaRelease!, names);
            using var z = new ZipArchive(new MemoryStream(saved.Data));
            string savedMeta = System.Text.Encoding.UTF8.GetString(ReadAll(z.GetEntry("pack.mcmeta")!));
            using var tm = JsonDocument.Parse(ReadAll(z.GetEntry("assets/minecraft/textures/block/stone.png.mcmeta")!));
            Program.Check(own.JavaRelease?.First == "1.20" && savedMeta == meta && tm.RootElement.GetProperty("texture").GetProperty("blur").GetBoolean() && saved.Problems.Count == 0,
                "pack.mcmeta kept byte for byte; the texture's blur setting kept");
            own.Textures["stone"].Animation = new TextureAnimation { FrameTime = 4 };
            own.Textures["stone"].Image = Noise(16, 32, 10);
            var animatedSave = TexturePackWriter.Java(own, own.JavaRelease!, names);
            using var z2 = new ZipArchive(new MemoryStream(animatedSave.Data));
            using var tm2 = JsonDocument.Parse(ReadAll(z2.GetEntry("assets/minecraft/textures/block/stone.png.mcmeta")!));
            Program.Check(tm2.RootElement.GetProperty("texture").GetProperty("blur").GetBoolean() && tm2.RootElement.GetProperty("animation").GetProperty("frametime").GetInt32() == 4,
                "an added animation joins the kept settings");
            var other = TexturePackWriter.Java(own, R("26.3"), names);
            Program.Check(Meta(other.Data) == "min_format=[97,1] max_format=97", "for another version a new pack.mcmeta is written");
        }
        {
            string dir = Path.Combine(TestPaths.Output, "textures", "own-bedrock");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(Path.Combine(dir, "textures", "blocks"));
            File.WriteAllText(Path.Combine(dir, "manifest.json"), """
                {"format_version":2,"header":{"name":"pack.name","description":"pack.description","uuid":"11111111-2222-3333-4444-555555555555","version":[1,0,3],"min_engine_version":[1,20,0]},
                 "modules":[{"type":"resources","uuid":"66666666-7777-8888-9999-000000000000","version":[1,0,3]}]}
                """);
            Directory.CreateDirectory(Path.Combine(dir, "texts"));
            File.WriteAllText(Path.Combine(dir, "texts", "en_US.lang"), "pack.name=Cool Pack\npack.description=Very cool\n");
            File.WriteAllBytes(Path.Combine(dir, "textures", "blocks", "stone.png"), Noise(16, 16, 11).ToPng());
            var own = TexturePackReader.Read(dir, names);
            var saved = TexturePackWriter.Bedrock(own, names, DateTime.Now);
            using var z = new ZipArchive(new MemoryStream(saved.Data));
            using var m = JsonDocument.Parse(ReadAll(z.GetEntry("manifest.json")!));
            var h = m.RootElement.GetProperty("header");
            Program.Check(own.Name == "Cool Pack" && h.GetProperty("uuid").GetString() == "11111111-2222-3333-4444-555555555555" && h.GetProperty("version")[2].GetInt32() == 4
                          && m.RootElement.GetProperty("modules")[0].GetProperty("version")[2].GetInt32() == 4 && z.GetEntry("texts/en_US.lang") != null && saved.Problems.Count == 0,
                "a Bedrock pack keeps its name, id and texts; its version goes up by one");
        }

        Editing();
        RealGames(args);
    }

    static void Editing()
    {
        Console.WriteLine("== Texture editor");
        var game = new TexturePack { Name = "Game" };
        game.Textures["stone"] = new PackTexture { Key = "stone", Image = Solid(16, 0xFF808080) };
        var waterImage = new RgbaImage(16, 64);
        waterImage.Fill(0, 0, 16, 64, 0xFF2040C0);
        game.Textures["water_still"] = new PackTexture { Key = "water_still", Image = waterImage, Animation = new TextureAnimation { FrameTime = 2 } };
        var pack = new TexturePack { Name = "Mine" };
        var ed = new MinecraftStudio.Core.Packs.TextureEditor(pack, game) { Primary = 0xFFFF0000, Secondary = 0xFF00FF00 };
        Program.Check(ed.AllKeys().SequenceEqual(["stone", "water_still"]) && !ed.IsDirty, "every game texture is listed; nothing changed yet");

        ed.Select("stone");
        ed.BeginStroke(5, 5, false);
        ed.EndStroke();
        Program.Check(ed.InPack("stone") && pack.Textures["stone"].Image[5, 5] == 0xFFFF0000 && game.Textures["stone"].Image[5, 5] == 0xFF808080 && ed.IsDirty,
            "the first change copies the game's texture into the pack; the game's own stays as it was");
        ed.Undo();
        Program.Check(!ed.InPack("stone") && !ed.IsDirty, "undo takes it back out of the pack");
        ed.Redo();
        Program.Check(ed.InPack("stone") && pack.Textures["stone"].Image[5, 5] == 0xFFFF0000, "redo");

        // A fill that changes nothing leaves a game texture out of the pack.
        ed.Select("water_still");
        ed.Tool = MinecraftStudio.Core.Skins.PaintTool.Fill;
        ed.Primary = 0xFF2040C0;
        ed.BeginStroke(3, 3, false);
        ed.EndStroke();
        Program.Check(!ed.InPack("water_still"), "filling with the same colour adds nothing");
        ed.Primary = 0xFFFF0000;
        ed.SelectFrame(2);
        ed.BeginStroke(3, 3, false);
        ed.EndStroke();
        var water = pack.Textures["water_still"].Image;
        Program.Check(water[0, 32] == 0xFFFF0000 && water[15, 47] == 0xFFFF0000 && water[0, 31] == 0xFF2040C0 && water[0, 48] == 0xFF2040C0, "fill stays inside the frame in view");

        // Mirror, frames, timing.
        ed.Tool = MinecraftStudio.Core.Skins.PaintTool.Pencil;
        ed.MirrorX = true;
        ed.SelectFrame(0);
        ed.BeginStroke(1, 3, true);
        ed.EndStroke();
        Program.Check(water[1, 3] == 0xFF00FF00 && water[14, 3] == 0xFF00FF00, "mirror paints both halves");
        ed.MirrorX = false;
        ed.AddFrame();
        var t = pack.Textures["water_still"];
        Program.Check(MinecraftStudio.Core.Packs.TextureEditor.FrameCount(t) == 5 && ed.Frame == 1 && t.Image[1, 19] == 0xFF00FF00, "a new frame is a copy of the one in view, put after it");
        ed.RemoveFrame();
        Program.Check(MinecraftStudio.Core.Packs.TextureEditor.FrameCount(pack.Textures["water_still"]) == 4, "removing a frame");
        ed.SetTiming(7, true);
        Program.Check(pack.Textures["water_still"].Animation is { FrameTime: 7, Interpolate: true }, "timing");
        ed.Undo();
        Program.Check(pack.Textures["water_still"].Animation is { FrameTime: 2, Interpolate: false }, "timing undone");

        // Use the game's texture again.
        ed.Select("stone");
        ed.Revert();
        Program.Check(!ed.InPack("stone"), "the game's texture again");
        ed.Undo();
        Program.Check(ed.InPack("stone") && pack.Textures["stone"].Image[5, 5] == 0xFFFF0000, "and back");
        // Eyedropper in a frame.
        ed.Tool = MinecraftStudio.Core.Skins.PaintTool.Picker;
        uint picked = 0;
        ed.ColorPicked += (c, _) => picked = c;
        ed.BeginStroke(5, 5, false);
        Program.Check(picked == 0xFFFF0000 && ed.Tool == MinecraftStudio.Core.Skins.PaintTool.Pencil, "the eyedropper picks and hands back the pencil");
    }

    /// <summary>With the shipped table: the installed games' own textures, converted both ways and read back.</summary>
    static void RealGames(string[] args)
    {
        var names = TextureNames.Default;
        if (names.Rows.Count == 0) { Program.Info("The shipped name table is not there yet: real-game checks skipped."); return; }
        Console.WriteLine($"== Real games ({names.Rows.Count} name rows)");
        var animated = TexturePackReader.BedrockAnimated();
        var jar = SkinTests.NewestJar();
        TexturePack? java = null;
        if (jar != null)
        {
            java = TexturePackReader.Read(jar, names);
            int known = java.Textures.Keys.Count(k => !k.Contains(':')), unknown = java.Textures.Count - known;
            Program.Info($"{Path.GetFileName(jar)}: {java.Textures.Count} block and item textures, {known} known to the table, {unknown} not");
            Program.Check(known > 500, $"{known} of the jar's textures are known");
            foreach (var first in new[] { JavaReleases.Newest.First, "1.21", "1.20.3", "1.16.2", "1.13", "1.11", "1.8", "1.7.2" })
            {
                var target = JavaReleases.All[JavaReleases.Index(first)];
                var r = TexturePackWriter.Java(java, target, names);
                Program.Check(r.Problems.Count == 0, $"jar → {target.Label}: problems: {string.Join("; ", r.Problems.Take(5))}");
                Program.Info($"jar → {target.Label}: {r.Written.Count} textures written, {r.NotIncluded.Count} not in that version");
            }
            // A Java pack for an old version, read back as that version: every texture comes back as itself.
            var old = TexturePackWriter.Java(java, JavaReleases.All[JavaReleases.Index("1.11")], names);
            string oldPath = Save(old, "jar-1.11.zip");
            var oldBack = TexturePackReader.Read(oldPath, names);
            int oldSame = old.Written.Count(k => oldBack.Textures.TryGetValue(k, out var t) && t.Image.SamePixels(java.Textures[k].Image));
            Program.Check(oldSame == old.Written.Count, $"1.11 pack read back: {oldSame} of {old.Written.Count} textures come back as themselves");

            var b = TexturePackWriter.Bedrock(java, names, DateTime.Now, animated);
            Program.Check(b.Problems.Count == 0, $"jar → Bedrock: problems: {string.Join("; ", b.Problems.Take(5))}");
            Program.Info($"jar → Bedrock: {b.Written.Count} written, {b.NotIncluded.Count} not in Bedrock");
            // Java → Bedrock → Java: what both editions have comes back pixel for pixel (first frames where Bedrock
            // does not animate).
            var bBack = TexturePackReader.Read(Save(b, "jar-bedrock.mcpack"), names);
            int both = 0, same = 0;
            var differ = new List<string>();
            foreach (var k in b.Written.Where(k => !k.Contains(':')))
            {
                if (!bBack.Textures.TryGetValue(k, out var t)) { differ.Add(k + " (missing)"); continue; }
                both++;
                var orig = java.Textures[k];
                if (t.Image.SamePixels(orig.Image) || t.Image.SamePixels(orig.FirstFrame())) same++;
                else differ.Add(k);
            }
            Program.Check(differ.Count == 0, $"Java → Bedrock → back: {same} of {both} textures identical; differ: {string.Join(", ", differ.Take(8))}");
            Program.Info($"Java → Bedrock → back: {same} of {both} textures identical");
        }
        var bedrock = TexturePackReader.ReadBedrockGame(names);
        if (bedrock != null)
        {
            int known = bedrock.Textures.Keys.Count(k => !k.Contains(':'));
            Program.Info($"Bedrock game: {bedrock.Textures.Count} textures, {known} known to the table");
            var r = TexturePackWriter.Java(bedrock, JavaReleases.Newest, names);
            Program.Check(r.Problems.Count == 0, $"Bedrock game → {JavaReleases.Newest.Label}: problems: {string.Join("; ", r.Problems.Take(5))}");
            Program.Info($"Bedrock game → {JavaReleases.Newest.Label}: {r.Written.Count} written, {r.NotIncluded.Count} not in Java");
            // Bedrock → Bedrock loses nothing: every file comes back with the same pixels under the same name.
            var bb = TexturePackWriter.Bedrock(bedrock, names, DateTime.Now, animated);
            Program.Check(bb.Problems.Count == 0 && bb.NotIncluded.Count == 0, $"Bedrock → Bedrock: {bb.NotIncluded.Count} not included; problems: {string.Join("; ", bb.Problems.Take(5))}");
            using var z = new ZipArchive(new MemoryStream(bb.Data));
            int files = 0, lost = 0;
            foreach (var t in bedrock.Textures.Values)
            {
                files++;
                var e = z.GetEntry("textures/" + t.SourcePath + ".png");
                if (e == null || !Png.Decode(ReadAll(e)).SamePixels(t.Image)) { lost++; if (lost <= 5) Program.Info("changed or missing: " + t.SourcePath); }
            }
            Program.Check(lost == 0, $"Bedrock → Bedrock: {files - lost} of {files} files come back unchanged");
            Program.Info($"Bedrock → Bedrock: {files - lost} of {files} texture files come back unchanged");
        }
        // terrain.png (Java up to 1.4.7), filled from the b1.0 jar.
        if (java != null && SkinTests.Jar("b1.0") is { } beta)
        {
            var fill = TexturePackReader.Read(beta, names);
            var alpha = TexturePackWriter.Java(java, JavaReleases.All[0], names, fill);
            Program.Check(alpha.Problems.Count == 0 && fill.Textures.Count > 50, $"jar → terrain.png: {alpha.Written.Count} tiles from the pack, filled from b1.0 ({fill.Textures.Count} tiles); problems: {string.Join("; ", alpha.Problems)}");
            Program.Info($"jar → {JavaReleases.All[0].Label} (terrain.png): {alpha.Written.Count} textures placed; b1.0 gives {fill.Textures.Count} tiles to fill the rest");
        }
    }

    static RgbaImage Solid(int size, uint c)
    {
        var img = new RgbaImage(size, size);
        img.Fill(0, 0, size, size, c);
        return img;
    }

    static RgbaImage Noise(int w, int h, int seed)
    {
        var rng = new Random(seed);
        var img = new RgbaImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = (uint)rng.Next() | 0xFF000000u;
        return img;
    }

    static string Save(PackResult r, string name)
    {
        string dir = Path.Combine(TestPaths.Output, "textures");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, r.Data);
        return path;
    }

    static List<string> Entries(byte[] zip)
    {
        using var z = new ZipArchive(new MemoryStream(zip));
        return z.Entries.Select(e => e.FullName).ToList();
    }

    static byte[] ReadAll(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static string Meta(byte[] zip)
    {
        using var z = new ZipArchive(new MemoryStream(zip));
        using var doc = JsonDocument.Parse(ReadAll(z.GetEntry("pack.mcmeta")!));
        return string.Join(" ", doc.RootElement.GetProperty("pack").EnumerateObject().Where(p => p.Name != "description").Select(p => $"{p.Name}={p.Value.GetRawText().Replace(" ", "").Replace("\n", "").Replace("\r", "")}"));
    }
}
