// McTests/PackCheckTests.cs: "packcheck" — the pack checker finds what is wrong, and nothing in what is right.
// Owns: PackCheckTests — a Java pack (1.21) with one texture of each mistake (old folder, a capital letter, stretched,
// an animation asking for a frame it lacks, broken model JSON) beside good ones; the same pack zipped inside a folder;
// a Bedrock pack with a bad uuid, a uuid used twice, no min_engine_version, a texture list pointing nowhere and a capital
// letter; and a clean pack of each edition, which must show no problems or warnings. Packs are made in the test folder.
using System.IO.Compression;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;

static class PackCheckTests
{
    public static void Run(string[] args)
    {
        // packcheck jar: the newest installed Java game's block and item textures as a resource pack (made in the test
        // folder, removed afterwards) — the game's own textures must pass without a problem or warning.
        if (args.Length > 0 && args[0] == "jar")
        {
            var jars = GameFolders.JavaJars();
            if (jars.Count == 0) { Program.Info("(no Java game installed)"); return; }
            var (jarPath, version) = (jars[0].Jar, jars[0].Version);
            var release = JavaReleases.Of(version) ?? JavaReleases.Newest;
            string tmp = Path.Combine(TestPaths.Output, "packcheck", "game-textures.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var src = ZipFile.OpenRead(jarPath))
            using (var dst = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                var meta = dst.CreateEntry("pack.mcmeta");
                using (var w = new StreamWriter(meta.Open())) w.Write(JavaReleases.PackSection(release, release, "Game textures").ToJsonString());
                foreach (var e in src.Entries.Where(e => e.FullName.StartsWith("assets/minecraft/textures/block/") || e.FullName.StartsWith("assets/minecraft/textures/item/")))
                {
                    using var from = e.Open();
                    using var to = dst.CreateEntry(e.FullName).Open();
                    from.CopyTo(to);
                }
            }
            var game = PackCheck.Run(tmp);
            File.Delete(tmp);
            Program.Info($"{version}: {game.Kind}; {game.Files:N0} files");
            foreach (var f in game.Findings) Program.Info($"{f.Severity}: {f.Message}");
            Program.Check(game.Problems == 0 && game.Warnings == 0, $"the game's own textures ({version}) pass: {game.Problems} problems, {game.Warnings} warnings");
            return;
        }
        // packcheck <pack>: the report on that pack (only read), to look at.
        if (args.Length > 0)
        {
            var report = PackCheck.Run(args[0]);
            Console.WriteLine(report.Text());
            return;
        }
        string dir = Path.Combine(TestPaths.Output, "packcheck", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(dir);

        Console.WriteLine("== A Java pack with mistakes");
        string java = Path.Combine(dir, "java");
        Write(java, "pack.mcmeta", "{\"pack\": {\"pack_format\": 34, \"description\": \"Test\"}}");
        Picture(java, "pack.png", 64, 64);
        Picture(java, "assets/minecraft/textures/block/grass_block_top.png", 16, 16);
        Picture(java, "assets/minecraft/textures/blocks/stone.png", 16, 16);
        Picture(java, "assets/minecraft/textures/block/Dirt.png", 16, 16);
        Picture(java, "assets/minecraft/textures/block/oak_planks.png", 16, 32);
        Picture(java, "assets/minecraft/textures/block/lava_still.png", 16, 64);
        Write(java, "assets/minecraft/textures/block/lava_still.png.mcmeta", "{\"animation\": {\"frametime\": 2, \"frames\": [0, 1, 2, 3, 7]}}");
        Write(java, "assets/minecraft/models/block/broken.json", "{\"parent\": \"block/cube_all\", \"textures\": {\"all\": \"block/stone\"");
        Write(java, "assets/minecraft/models/block/fine.json", "{\"parent\": \"block/cube_all\", \"textures\": {\"all\": \"block/stone\"}}");
        var r = PackCheck.Run(java);
        Show(r);
        Expect(r, PackSeverity.Problem, "textures/blocks", "the 1.12 folder in a 1.21 pack");
        Expect(r, PackSeverity.Problem, "capital letters", "a capital letter in a name");
        Expect(r, PackSeverity.Warning, "stretches", "a 16 × 32 texture without animation data");
        Expect(r, PackSeverity.Problem, "frame 7", "an animation asking for a frame the picture lacks");
        Expect(r, PackSeverity.Problem, "Broken JSON", "a model that is not JSON");
        Program.Check(r.Kind.Contains("1.21"), $"the version is named ({r.Kind})");
        Program.Check(!r.Findings.Any(f => (f.File ?? "").Contains("fine.json") || f.Message.Contains("grass_block_top")), "the good files are not reported");

        Console.WriteLine("== The same pack zipped inside a folder");
        string zip = Path.Combine(dir, "nested.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            foreach (var f in Directory.EnumerateFiles(java, "*", SearchOption.AllDirectories))
                z.CreateEntryFromFile(f, "My Pack/" + Path.GetRelativePath(java, f).Replace('\\', '/'));
        var nested = PackCheck.Run(zip);
        Expect(nested, PackSeverity.Problem, "pack.mcmeta is inside", "pack.mcmeta not at the top of the zip");

        Console.WriteLine("== A Bedrock pack with mistakes");
        string bedrock = Path.Combine(dir, "bedrock");
        Write(bedrock, "manifest.json", """
            {
              // comments are allowed, as in the game
              "format_version": 2,
              "header": { "name": "Test", "description": "x", "uuid": "not-a-uuid", "version": [1, 0, 0] },
              "modules": [
                { "type": "resources", "uuid": "11111111-2222-3333-4444-555555555555", "version": [1, 0, 0] },
                { "type": "resources", "uuid": "11111111-2222-3333-4444-555555555555", "version": [1, 0, 0] }
              ]
            }
            """);
        Picture(bedrock, "textures/blocks/stone.png", 16, 16);
        Picture(bedrock, "textures/blocks/Dirt.png", 16, 16);
        Write(bedrock, "textures/terrain_texture.json", "{\"texture_data\": {\"stone\": {\"textures\": \"textures/blocks/stone\"}, \"mystery\": {\"textures\": \"textures/blocks/nothing_here\"}}}");
        var b = PackCheck.Run(bedrock);
        Show(b);
        Expect(b, PackSeverity.Problem, "not a valid uuid", "a bad uuid");
        Expect(b, PackSeverity.Problem, "used twice", "a uuid used twice");
        Expect(b, PackSeverity.Problem, "min_engine_version", "no min_engine_version in format 2");
        Expect(b, PackSeverity.Warning, "nothing_here", "a texture list pointing nowhere");
        Expect(b, PackSeverity.Warning, "capital letters", "a capital letter in a name");
        Program.Check(!b.Findings.Any(f => f.Message.Contains("stone →")), "a texture the pack has is not reported");

        Console.WriteLine("== A data pack");
        string dataPack = Path.Combine(dir, "datapack");
        Write(dataPack, "pack.mcmeta", "{\"pack\": {\"pack_format\": 48, \"description\": \"Data\"}}");
        Write(dataPack, "data/mypack/recipe/good.json", "{\"type\": \"minecraft:crafting_shapeless\", \"ingredients\": [\"minecraft:dirt\"], \"result\": {\"id\": \"minecraft:stone\"}}");
        Write(dataPack, "data/mypack/loot_table/bad.json", "{\"pools\": [");
        Write(dataPack, "data/mypack/function/Start.mcfunction", "say hi");
        var dp = PackCheck.Run(dataPack);
        Show(dp);
        Program.Check(dp.Kind.StartsWith("Java data pack") && !dp.Findings.Any(f => f.Message.Contains("assets folder")), $"a data pack is recognised as one ({dp.Kind})");
        Expect(dp, PackSeverity.Problem, "bad.json", "a loot table that is not JSON");
        Expect(dp, PackSeverity.Problem, "capital letters", "a capital letter in a data pack's names");
        Program.Check(!dp.Findings.Any(f => (f.File ?? "").Contains("good.json")), "the good recipe is not reported");

        // Data pack mistakes: a function line with a slash, a misspelt command, a folder of the wrong era, a tick tag naming
        // a function the pack doesn't have; a good function and a load tag that names one it has are not reported.
        string mistakes = Path.Combine(dir, "datapack-mistakes");
        Write(mistakes, "pack.mcmeta", "{\"pack\": {\"pack_format\": 48, \"description\": \"Mistakes\"}}");
        Write(mistakes, "data/mypack/function/ok.mcfunction", "# fine\nsay hello\nexecute as @a run tellraw @s \"hi\"\n$say $(word)\n");
        Write(mistakes, "data/mypack/function/slash.mcfunction", "/say hi\n");
        Write(mistakes, "data/mypack/function/typo.mcfunction", "tellrw @a \"hi\"\n");
        Write(mistakes, "data/mypack/functions/old.mcfunction", "say old\n");
        Write(mistakes, "data/minecraft/tags/function/load.json", "{\"values\": [\"mypack:ok\"]}");
        Write(mistakes, "data/minecraft/tags/function/tick.json", "{\"values\": [\"mypack:missing\"]}");
        var dm = PackCheck.Run(mistakes);
        Show(dm);
        Program.Check(dm.Kind.Contains("made for 1.21 – 1.21.1"), $"the data pack's version is named from its format ({dm.Kind})");
        Expect(dm, PackSeverity.Problem, "starting with /", "a function line starting with /");
        Expect(dm, PackSeverity.Warning, "\"tellrw\" is not a command", "a misspelt command");
        Expect(dm, PackSeverity.Problem, "Folders named as before 1.21", "a 1.21 pack's functions/ folder");
        Expect(dm, PackSeverity.Problem, "mypack:missing", "the tick tag naming a missing function");
        Program.Check(!dm.Findings.Any(f => f.Message.Contains("ok.mcfunction") || f.Message.Contains("mypack:ok")), "the good function and the load tag naming it are not reported");

        Console.WriteLine("== Clean packs");
        string goodJava = Path.Combine(dir, "good-java");
        Write(goodJava, "pack.mcmeta", "{\"pack\": {\"pack_format\": 15, \"description\": \"Good\"}}");
        Picture(goodJava, "pack.png", 32, 32);
        Picture(goodJava, "assets/minecraft/textures/block/stone.png", 16, 16);
        Picture(goodJava, "assets/minecraft/textures/block/water_still.png", 16, 512);
        Write(goodJava, "assets/minecraft/textures/block/water_still.png.mcmeta", "{\"animation\": {\"frametime\": 2}}");
        var gj = PackCheck.Run(goodJava);
        Program.Check(gj.Problems == 0 && gj.Warnings == 0, $"a clean Java pack: no problems or warnings ({string.Join("; ", gj.Findings.Where(f => f.Severity != PackSeverity.Note).Select(f => f.Message))})");
        string goodBedrock = Path.Combine(dir, "good-bedrock");
        Write(goodBedrock, "manifest.json", $$"""
            { "format_version": 2,
              "header": { "name": "Good", "description": "x", "uuid": "{{Guid.NewGuid()}}", "version": [1, 0, 0], "min_engine_version": [1, 20, 0] },
              "modules": [ { "type": "resources", "uuid": "{{Guid.NewGuid()}}", "version": [1, 0, 0] } ] }
            """);
        Picture(goodBedrock, "pack_icon.png", 64, 64);
        Picture(goodBedrock, "textures/blocks/stone.png", 16, 16);
        var gb = PackCheck.Run(goodBedrock);
        Program.Check(gb.Problems == 0 && gb.Warnings == 0, $"a clean Bedrock pack: no problems or warnings ({string.Join("; ", gb.Findings.Where(f => f.Severity != PackSeverity.Note).Select(f => f.Message))})");

        // A real pack written by the Texture Studio's own writer is clean too.
        var made = new TexturePack { Name = "Made here" };
        // Keys are the shared names of the texture table ("stone"), not file paths.
        made.Textures["stone"] = new PackTexture { Key = "stone", Image = new RgbaImage(16, 16) };
        var built = TexturePackWriter.Java(made, JavaReleases.Newest, TextureNames.Default);
        Program.Check(built.Written.Count == 1, $"the Texture Studio writes the texture for {JavaReleases.Newest.Label} ({string.Join(", ", built.Written)})");
        string madeZip = Path.Combine(dir, built.FileName);
        File.WriteAllBytes(madeZip, built.Data);
        var m = PackCheck.Run(madeZip);
        Program.Check(m.Problems == 0 && m.Warnings == 0, $"a pack the Texture Studio writes passes ({m.Kind}; {string.Join("; ", m.Findings.Where(f => f.Severity != PackSeverity.Note).Select(f => f.Message))})");
    }

    static void Write(string root, string rel, string text)
    {
        string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    static void Picture(string root, string rel, int w, int h)
    {
        string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var img = new RgbaImage(w, h);
        Array.Fill(img.Pixels, 0xFF808080u);
        File.WriteAllBytes(p, Png.Encode(img));
    }

    static void Expect(PackReport r, PackSeverity s, string words, string what) =>
        Program.Check(r.Findings.Any(f => f.Severity == s && (f.Message.Contains(words, StringComparison.OrdinalIgnoreCase) || (f.File ?? "").Contains(words, StringComparison.OrdinalIgnoreCase))),
            $"found: {what}");

    static void Show(PackReport r)
    {
        foreach (var f in r.Findings) Program.Info($"{f.Severity}: {f.Message}{(f.File != null ? $" ({f.File})" : "")}");
    }
}
