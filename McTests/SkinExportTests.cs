// McTests/SkinExportTests.cs: Part of "skins": Java pack formats against the installed jars, every skin export
// read back, and installing packs into temporary game folders (updates replace older copies; other people's
// files are never touched).
// Owns: SkinExportTests.
using System.IO.Compression;
using System.Text.Json;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;

/// <summary>
/// Part of "skins": Java pack formats against the installed jars, every skin export read back, and installing packs
/// into temporary game folders (updates replace older copies; other people's files are never touched).
/// </summary>
static class SkinExportTests
{
    public static void Run()
    {
        Console.WriteLine("== Java pack formats");
        PackFormats();
        Console.WriteLine("== Skin exports, read back");
        Exports();
        Console.WriteLine("== Installing packs (temporary game folders)");
        Installs();
    }

    static JavaRelease R(string first) => JavaReleases.All[JavaReleases.Index(first)];

    static void PackFormats()
    {
        // The table against the pack format each installed jar declares in its version.json.
        int jars = 0;
        foreach (var (version, jar) in GameFolders.JavaJars())
        {
            using var zip = ZipFile.OpenRead(jar);
            var e = zip.GetEntry("version.json");
            if (e == null) continue;
            using var doc = JsonDocument.Parse(e.Open());
            if (!doc.RootElement.TryGetProperty("pack_version", out var pv)) continue;
            int major, minor = 0;
            if (pv.ValueKind == JsonValueKind.Number) major = pv.GetInt32();
            else if (pv.TryGetProperty("resource_major", out var rm)) { major = rm.GetInt32(); minor = pv.TryGetProperty("resource_minor", out var rn) ? rn.GetInt32() : 0; }
            else major = pv.GetProperty("resource").GetInt32();
            var r = JavaReleases.Of(version);
            Program.Check(r != null && r.Major == major && r.Minor == minor, $"{version}: the jar says resource pack format {major}.{minor}, the table {r?.Major}.{r?.Minor}");
            jars++;
        }
        Program.Check(jars >= 5, $"{jars} installed jars declare a pack format");
        Program.Info($"{jars} installed jars agree with the pack format table");
        Program.Check(JavaReleases.Of("1.20.4")?.First == "1.20.3" && JavaReleases.Of("1.7.10")?.First == "1.7.2" && JavaReleases.Of("1.6.4")?.First == "1.6.1" && JavaReleases.Of("1.2.5")?.Kind == JavaPackKind.TerrainAtlas
                      && JavaReleases.Of("26.3")?.Major == 97 && JavaReleases.Of("27.0") == null, "version numbers find their release line");

        string Meta(string from, string to) => string.Join(" ", JavaReleases.PackSection(R(from), R(to), "d")["pack"]!.AsObject()
            .Where(kv => kv.Key != "description").Select(kv => $"{kv.Key}={kv.Value!.ToJsonString()}"));
        void Expect(string from, string to, string want) => Program.Check(Meta(from, to) == want, $"pack.mcmeta for {from}–{to}: {Meta(from, to)} (expected {want})");
        Expect("1.20", "1.20", "pack_format=15");
        Expect("1.6.1", "1.11", "pack_format=3");
        Expect("1.19.3", "1.21.7", "pack_format=64 supported_formats=[12,64]");
        Expect("1.21.11", "1.21.11", "min_format=75 max_format=75");
        Expect("26.3", "26.3", "min_format=[97,1] max_format=97");
        Expect("1.8", "26.3", "pack_format=64 supported_formats=[1,97] min_format=1 max_format=97");
    }

    static Skin TestSkin(SkinModel model, int size = 64)
    {
        var img = new RgbaImage(64, 64);
        var rng = new Random(model == SkinModel.Slim ? 11 : 12);
        foreach (var box in SkinLayout.Boxes(model))
            foreach (var f in box.Faces())
                for (int j = 0; j < f.H; j++)
                    for (int i = 0; i < f.W; i++)
                        img[f.U + i, f.V + j] = RgbaImage.Argb(box.Overlay ? (rng.Next(2) == 0 ? 0 : 255) : 255, rng.Next(256), rng.Next(256), rng.Next(256));
        return new Skin(size == 64 ? img : img.Scaled(size, size), model) { Name = model == SkinModel.Slim ? "Slim Test" : "Classic Test" };
    }

    static void Exports()
    {
        var classic = TestSkin(SkinModel.Classic);
        var slimHd = TestSkin(SkinModel.Slim, 128);
        slimHd.Name = "HD Slim Ünïcode #1";

        var png = SkinExport.JavaPng(classic);
        Program.Check(png.Problems.Count == 0 && Png.Decode(png.Data).SamePixels(classic.Image), "Java PNG holds the skin");
        var hdPng = SkinExport.JavaPng(slimHd);
        Program.Check(Png.Decode(hdPng.Data).Width == 64 && hdPng.Notes.Any(n => n.Contains("scaled down")), "an HD skin is scaled to 64x64 for Java, with a note");
        var legacy = SkinExport.LegacyPng(slimHd);
        var legacyImg = Png.Decode(legacy.Data);
        Program.Check(legacyImg.Width == 64 && legacyImg.Height == 32 && legacy.Problems.Count == 0 && legacy.Notes.Count >= 2, "64x32 PNG for old versions, with notes");
        var bedrockPng = SkinExport.BedrockPng(slimHd);
        Program.Check(Png.Decode(bedrockPng.Data).SamePixels(slimHd.Image), "Bedrock PNG keeps 128x128");

        // Skin pack: two skins, read back by the same reader an import uses.
        var when = new DateTime(2026, 11, 1, 16, 45, 30);
        var pack = SkinExport.BedrockSkinPack([classic, slimHd], "Test Pack", when);
        Program.Check(pack.Problems.Count == 0, "skin pack reads back exactly: " + string.Join("; ", pack.Problems));
        var back = SkinExport.ReadBedrockSkinPack(pack.Data, out _);
        Program.Check(back.Count == 2 && back[0].Skin.Model == SkinModel.Classic && back[1].Skin.Model == SkinModel.Slim && back[1].Skin.Image.Width == 128
                      && back[1].Name == "HD Slim Ünïcode #1", "skins, arm shapes, HD size and names survive");
        using (var zip = new ZipArchive(new MemoryStream(pack.Data)))
        {
            var manifest = PackInstaller.ReadManifest(ReadAll(zip.GetEntry("manifest.json")!));
            Program.Check(manifest.Modules.SequenceEqual(["skin_pack"]) && manifest.Version == "2026.1101.164530", $"manifest: {string.Join(",", manifest.Modules)} {manifest.Version}");
            string lang = System.Text.Encoding.UTF8.GetString(ReadAll(zip.GetEntry("texts/en_US.lang")!));
            Program.Check(lang.Contains("skinpack.Test_Pack_") && lang.Contains(".HD_Slim_Unicode_1=HD Slim Ünïcode #1"), "language file keys: " + lang.Replace("\n", " | "));
        }
        var again = SkinExport.BedrockSkinPack([classic], "test pack", when.AddMinutes(5));
        using (var a = new ZipArchive(new MemoryStream(pack.Data)))
        using (var b = new ZipArchive(new MemoryStream(again.Data)))
        {
            var ma = PackInstaller.ReadManifest(ReadAll(a.GetEntry("manifest.json")!));
            var mb = PackInstaller.ReadManifest(ReadAll(b.GetEntry("manifest.json")!));
            Program.Check(ma.Uuid == mb.Uuid && mb.Version == "2026.1101.165030", "the same pack name keeps its id and gets a newer version");
        }
        var other = SkinExport.BedrockSkinPack([classic], "Another Pack", when);
        using (var c = new ZipArchive(new MemoryStream(other.Data)))
        using (var a = new ZipArchive(new MemoryStream(pack.Data)))
            Program.Check(PackInstaller.ReadManifest(ReadAll(c.GetEntry("manifest.json")!)).Uuid != PackInstaller.ReadManifest(ReadAll(a.GetEntry("manifest.json")!)).Uuid, "another pack name gets another id");

        // The game's own skin pack, zipped in memory, through the same reader.
        string vanilla = Path.Combine(SkinTests.BedrockData, "skin_packs", "vanilla");
        if (Directory.Exists(vanilla))
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var f in Directory.GetFiles(vanilla))
                {
                    using var w = zip.CreateEntry(Path.GetFileName(f)).Open();
                    w.Write(File.ReadAllBytes(f));
                }
            var skins = SkinExport.ReadBedrockSkinPack(ms.ToArray(), out var notes);
            Program.Check(skins.Count == 10 && skins.Count(x => x.Skin.Model == SkinModel.Slim) == 5 && !notes.Any(n => n.StartsWith("Problem")),
                $"the game's own skin pack: {skins.Count} skins, {skins.Count(x => x.Skin.Model == SkinModel.Slim)} slim ({string.Join("; ", notes)})");
        }

        // Java packs that replace the default skins.
        var modern = SkinExport.JavaDefaultSkinPack(classic, R("1.19.3"), R("26.3"), "Default Swap");
        Program.Check(modern.Problems.Count == 0 && Entries(modern.Data).Count(e => e.Contains("/player/")) == 18 && !Entries(modern.Data).Any(e => e.EndsWith("entity/steve.png")),
            "1.19.3+: all nine default skins, wide and slim");
        var span = SkinExport.JavaDefaultSkinPack(slimHd, R("1.8"), R("26.3"), "Default Swap");
        Program.Check(span.Problems.Count == 0 && Entries(span.Data).Contains("assets/minecraft/textures/entity/alex.png") && Entries(span.Data).Count(e => e.Contains("/player/")) == 18,
            "1.8 to 26.3: Steve/Alex and the nine skins");
        using (var z = new ZipArchive(new MemoryStream(span.Data)))
        {
            var alex = Png.Decode(ReadAll(z.GetEntry("assets/minecraft/textures/entity/alex.png")!));
            var steve = Png.Decode(ReadAll(z.GetEntry("assets/minecraft/textures/entity/steve.png")!));
            Program.Check(alex.Width == 64 && Skin.DetectModel(alex) == SkinModel.Slim && Skin.DetectModel(steve) == SkinModel.Classic, "Alex gets the slim copy, Steve the classic one");
            using var meta = JsonDocument.Parse(ReadAll(z.GetEntry("pack.mcmeta")!));
            Program.Check(meta.RootElement.GetProperty("pack").GetProperty("description").GetString()!.Contains(PackInstaller.Signature), "the pack is marked as made here");
        }
        var old = SkinExport.JavaDefaultSkinPack(classic, R("1.6.1"), R("1.6.1"), "Old");
        using (var z = new ZipArchive(new MemoryStream(old.Data)))
            Program.Check(old.Problems.Count == 0 && Png.Decode(ReadAll(z.GetEntry("assets/minecraft/textures/entity/steve.png")!)).Height == 32, "1.6.1–1.6.4: a 64x32 steve.png");
        var tp = SkinExport.JavaDefaultSkinPack(classic, R("Alpha 1.2.2"), R("1.5"), "Older");
        Program.Check(tp.Problems.Count == 0 && Entries(tp.Data).Contains("mob/char.png") && Entries(tp.Data).Contains("pack.txt") && tp.FileName.Contains("texture pack"),
            "up to 1.5.2: a texture pack with mob/char.png");
        var span17 = SkinExport.JavaDefaultSkinPack(classic, R("1.6.1"), R("1.7.2"), "Old");
        Program.Check(span17.Problems.Count == 0, "1.6.1 to 1.7.10 in one pack (both read a 64x32 steve.png)");
        Program.Check(ThrowsArg(() => SkinExport.JavaDefaultSkinPack(classic, R("1.7.2"), R("1.8"), "x")) && ThrowsArg(() => SkinExport.JavaDefaultSkinPack(classic, R("1.5"), R("1.6.1"), "x")),
            "packs that would need two different files at one path are refused");
    }

    static void Installs()
    {
        string root = Path.Combine(TestPaths.Output, "install");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string game = Path.Combine(root, "java");
        Directory.CreateDirectory(game);
        var skin = TestSkin(SkinModel.Classic);
        var v1 = SkinExport.JavaDefaultSkinPack(skin, R("1.21.11"), R("1.21.11"), "Swap");
        string p1 = PackInstaller.InstallJava(v1.FileName, v1.Data, game, false);
        Program.Check(File.ReadAllBytes(p1).AsSpan().SequenceEqual(v1.Data) && Path.GetFileName(Path.GetDirectoryName(p1)) == "resourcepacks", "Java pack goes into resourcepacks");
        skin.Image[8, 8] = RgbaImage.Argb(255, 9, 9, 9);
        var v2 = SkinExport.JavaDefaultSkinPack(skin, R("1.21.11"), R("1.21.11"), "Swap");
        string p2 = PackInstaller.InstallJava(v2.FileName, v2.Data, game, false);
        Program.Check(p2 == p1 && File.ReadAllBytes(p2).AsSpan().SequenceEqual(v2.Data), "our own older copy is updated in place");
        string foreign = Path.Combine(game, "resourcepacks", "Other.zip");
        File.WriteAllBytes(foreign, [1, 2, 3]);
        string p3 = PackInstaller.InstallJava("Other.zip", v2.Data, game, false);
        Program.Check(File.ReadAllBytes(foreign).SequenceEqual(new byte[] { 1, 2, 3 }) && Path.GetFileName(p3) == "Other (2).zip", "a different file of the same name is kept; the new one is numbered");
        var tp = SkinExport.JavaDefaultSkinPack(skin, R("1.5"), R("1.5"), "Swap");
        Program.Check(Path.GetFileName(Path.GetDirectoryName(PackInstaller.InstallJava(tp.FileName, tp.Data, game, true))) == "texturepacks", "texture packs go into texturepacks");

        string mojang = Path.Combine(root, "com.mojang");
        var when = new DateTime(2026, 11, 1, 10, 0, 0);
        var pack1 = SkinExport.BedrockSkinPack([skin], "My Skins", when);
        string f1 = PackInstaller.InstallBedrock(pack1.Data, mojang);
        Program.Check(Path.GetFileName(Path.GetDirectoryName(f1)) == "skin_packs" && File.Exists(Path.Combine(f1, "manifest.json")) && File.Exists(Path.Combine(f1, "skins.json")),
            "skin pack unpacks into skin_packs");
        var pack2 = SkinExport.BedrockSkinPack([skin, TestSkin(SkinModel.Slim)], "My Skins", when.AddHours(1));
        string f2 = PackInstaller.InstallBedrock(pack2.Data, mojang);
        Program.Check(f2 == f1 && Directory.GetDirectories(Path.Combine(mojang, "skin_packs")).Length == 1 && File.Exists(Path.Combine(f2, "Slim_Test.png")),
            "a newer copy of the same pack replaces the old one");
        string foreignPack = Path.Combine(mojang, "skin_packs", "Someone else");
        Directory.CreateDirectory(foreignPack);
        string foreignManifest = """{"format_version":2,"header":{"name":"x","uuid":"00000000-0000-0000-0000-000000000001","version":[1,0,0]},"modules":[{"type":"skin_pack","uuid":"00000000-0000-0000-0000-000000000002","version":[1,0,0]}]}""";
        File.WriteAllText(Path.Combine(foreignPack, "manifest.json"), foreignManifest);
        PackInstaller.InstallBedrock(SkinExport.BedrockSkinPack([skin], "Someone else", when).Data, mojang);
        Program.Check(File.ReadAllText(Path.Combine(foreignPack, "manifest.json")) == foreignManifest && Directory.GetDirectories(Path.Combine(mojang, "skin_packs")).Length == 3,
            "a different pack in a folder of the same name is kept");
        // A pack that tries to write outside its folder is refused and leaves nothing behind.
        using var evil = new MemoryStream();
        using (var z = new ZipArchive(evil, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(z.CreateEntry("manifest.json").Open()))
                w.Write("""{"format_version":2,"header":{"name":"e","uuid":"00000000-0000-0000-0000-000000000003","version":[1,0,0]},"modules":[{"type":"resources","uuid":"00000000-0000-0000-0000-000000000004","version":[1,0,0]}]}""");
            using (var w = new StreamWriter(z.CreateEntry("../../escaped.txt").Open())) w.Write("x");
        }
        bool refused;
        try { PackInstaller.InstallBedrock(evil.ToArray(), mojang); refused = false; }
        catch (InvalidDataException) { refused = true; }
        string rp = Path.Combine(mojang, "resource_packs");
        Program.Check(refused && !File.Exists(Path.Combine(root, "escaped.txt")) && (!Directory.Exists(rp) || Directory.GetFileSystemEntries(rp).Length == 0),
            "a pack with a path leading outside its folder is refused and leaves nothing behind");
    }

    static bool ThrowsArg(Action a)
    {
        try { a(); return false; }
        catch (ArgumentException) { return true; }
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
}
