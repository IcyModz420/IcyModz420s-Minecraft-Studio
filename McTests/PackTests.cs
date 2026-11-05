// McTests/PackTests.cs: packs: seeing, installing, switching, ordering and uninstalling a world's packs — Java (PC)
// data packs and world resource pack, Bedrock (Windows PC) behavior and resource packs — on copies of real test
// worlds, with hostile packs (zip-slip) refused and nothing ever deleted (uninstalled packs move aside).
// Owns: PackTests.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.World;

static class PackTests
{
    static readonly string Root = Path.Combine(TestPaths.Output, "packs");

    public static void Run(string[] args)
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
        Directory.CreateDirectory(Root);
        Java();
        Bedrock();
    }

    // ---------------------------------------------------------------- Java

    static void Java()
    {
        string src = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(src)) { Program.Info("(skipped Java: no 1.16.5 test world)"); return; }
        Console.WriteLine("== Java Edition (PC) world packs");
        var before = Hashes(src);
        string world = Path.Combine(Root, "java-world");
        CopyDir(src, world);
        string removed = Path.Combine(Root, "removed-java");

        using (var w = WorldLoader.Open(world))
        {
            Program.Check(WorldPacks.EditionLabel(w) == WorldPacks.JavaLabel, $"the world is labelled {WorldPacks.EditionLabel(w)}");
            var packs = WorldPacks.Read(w);
            Program.Check(packs.Any(p => p.Id == "file/inf-dungeons-datapack-e1060.zip" && p.InWorld), "the world's own data pack is listed");
            Program.Check(packs.Any(p => p.Id == "vanilla" && p.BuiltIn && p.Active), "vanilla is listed as built in and on");
            foreach (var p in packs) Program.Info($"   {p.KindText}: {p.Name} [{(p.Active ? "on" : "off")}] — {p.Where}");

            // Install a data pack: it lands in datapacks/ and is turned on (last = on top).
            string dp = MakeJavaPack(Path.Combine(Root, "Test Data Pack.zip"), data: true);
            var added = WorldPacks.Install(w, dp, removed).Single();
            Program.Check(added.Kind == WorldPackKind.DataPack && added.Active && File.Exists(added.Location), "a data pack installs into datapacks/ and is on");
            Program.Check(EnabledList(w).LastOrDefault() == added.Id, "a new data pack is last in level.dat's Enabled list (applied on top)");

            // Off, reopen: it stays off.
            WorldPacks.SetActive(w, added, false);
        }
        using (var w = WorldLoader.Open(world))
        {
            var test = WorldPacks.Read(w).First(p => p.Name == "Test Data Pack.zip");
            Program.Check(!test.Active && DisabledList(w).Contains(test.Id), "turning a data pack off is saved in level.dat (Disabled) and survives reopening");
            Program.Check(Refused(() => WorldPacks.SetActive(w, WorldPacks.Read(w).First(p => p.Id == "vanilla"), false)), "vanilla cannot be turned off");

            // Order: turn it back on and move it below the other pack.
            WorldPacks.SetActive(w, test, true);
            var on = EnabledList(w);
            test = WorldPacks.Read(w).First(p => p.Id == test.Id);
            WorldPacks.Move(w, test, -1);
            var after = EnabledList(w);
            Program.Check(after.IndexOf(test.Id) == on.IndexOf(test.Id) - 1 && after[0] == "vanilla", $"a data pack moves one place lower and vanilla stays first ({string.Join(", ", after)})");

            // Uninstall: out of the lists, the file moved aside (not deleted).
            string? moved = WorldPacks.Uninstall(w, test, removed);
            Program.Check(moved != null && File.Exists(moved) && !File.Exists(test.Location!), "uninstalling moves the data pack out of the world into the removed-packs folder");
            Program.Check(!EnabledList(w).Contains(test.Id) && !DisabledList(w).Contains(test.Id), "an uninstalled data pack is no longer in level.dat");

            // A resource pack becomes the world resource pack; uninstalling moves resources.zip aside.
            var rp = WorldPacks.Install(w, MakeJavaPack(Path.Combine(Root, "Textures.zip"), data: false), removed).Single();
            Program.Check(rp.Kind == WorldPackKind.WorldResourcePack && File.Exists(Path.Combine(world, "resources.zip")), "a resource pack installs as the world's resources.zip");
            Program.Check(WorldPacks.Uninstall(w, rp, removed) is string r && File.Exists(r) && !File.Exists(Path.Combine(world, "resources.zip")), "uninstalling the world resource pack moves resources.zip aside");

            // Hostile and wrong packs.
            string slip = MakeSlipZip(Path.Combine(Root, "slip.zip"), "evilpack/pack.mcmeta", "evilpack/data/x.txt", "evilpack/../../escaped.txt");
            Program.Check(Refused(() => WorldPacks.Install(w, slip, removed)), "a zip-slip data pack is refused");
            Program.Check(!File.Exists(Path.Combine(world, "escaped.txt")) && !File.Exists(Path.Combine(Root, "escaped.txt")), "nothing from it landed outside the pack folder");
            Program.Check(!Directory.EnumerateFileSystemEntries(Path.Combine(world, "datapacks")).Any(e => Path.GetFileName(e).StartsWith(".installing")), "no half-installed pack is left in datapacks/");
            Program.Check(Refused(() => WorldPacks.Install(w, MakeBedrockPack(Path.Combine(Root, "bedrock.mcpack"), Guid.NewGuid(), "resources", "B"), removed)), "a Bedrock pack is refused for a Java world, with a reason");
        }
        Program.Check(before.SequenceEqual(Hashes(src)), "the test world in TestData is byte-for-byte unchanged");
    }

    static List<string> EnabledList(SaveWorld w) => ((JavaWorld)w).Data!.GetCompound("DataPacks")!.GetList("Enabled")!.OfType<MinecraftStudio.Core.Nbt.NbtString>().Select(t => t.Value).ToList();
    static List<string> DisabledList(SaveWorld w) => ((JavaWorld)w).Data!.GetCompound("DataPacks")!.GetList("Disabled")!.OfType<MinecraftStudio.Core.Nbt.NbtString>().Select(t => t.Value).ToList();

    // ---------------------------------------------------------------- Bedrock

    static void Bedrock()
    {
        string src = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16");
        if (!Directory.Exists(src)) { Program.Info("(skipped Bedrock: no 1.16 test world)"); return; }
        Console.WriteLine("== Bedrock Edition (Windows PC) world packs");
        var before = Hashes(src);
        string world = Path.Combine(Root, "bedrock-world");
        CopyDir(src, world);
        string removed = Path.Combine(Root, "removed-bedrock");
        Guid r1 = Guid.NewGuid(), r2 = Guid.NewGuid(), bp = Guid.NewGuid();

        using var w = WorldLoader.Open(world);
        Program.Check(WorldPacks.EditionLabel(w) == WorldPacks.BedrockLabel, $"the world is labelled {WorldPacks.EditionLabel(w)}");

        // A resource pack: into resource_packs/, on at the top, its name from texts/en_US.lang.
        var res = WorldPacks.Install(w, MakeBedrockPack(Path.Combine(Root, "textures.mcpack"), r1, "resources", "Test Textures"), removed).Single();
        Program.Check(res.Kind == WorldPackKind.ResourcePack && res.Active && res.InWorld && res.Name == "Test Textures", $"a .mcpack resource pack installs into the world and is on ({res.Name})");
        Program.Check(ListIds(world, "world_resource_packs.json").FirstOrDefault() == r1.ToString(), "it is first in world_resource_packs.json (highest priority)");

        // An add-on with a behavior pack and a second resource pack.
        var addon = WorldPacks.Install(w, MakeAddon(Path.Combine(Root, "addon.mcaddon"), bp, r2), removed);
        Program.Check(addon.Count == 2 && addon.Any(p => p.Kind == WorldPackKind.BehaviorPack) && addon.Any(p => p.Kind == WorldPackKind.ResourcePack), "an .mcaddon installs both of its packs");
        Program.Check(ListIds(world, "world_behavior_packs.json").Contains(bp.ToString()), "the behavior pack is in world_behavior_packs.json");

        // Unknown fields in the list are kept through a reorder.
        var listFile = Path.Combine(world, "world_resource_packs.json");
        var list = JsonNode.Parse(File.ReadAllText(listFile))!.AsArray();
        foreach (var e in list) if (e!["pack_id"]!.ToString() == r1.ToString()) e["subpack"] = "high";
        File.WriteAllText(listFile, list.ToJsonString());
        var first = WorldPacks.Read(w).First(p => p.Id == r1.ToString());
        WorldPacks.Move(w, first, -1);
        var ids = ListIds(world, "world_resource_packs.json");
        Program.Check(ids.IndexOf(r1.ToString()) == 1, $"a resource pack moves one place down ({string.Join(", ", ids)})");
        Program.Check(File.ReadAllText(listFile).Contains("\"subpack\""), "fields this program does not know (subpack) are kept");

        // Off: off the list, still in the world.
        WorldPacks.SetActive(w, WorldPacks.Read(w).First(p => p.Id == r1.ToString()), false);
        var off = WorldPacks.Read(w).First(p => p.Id == r1.ToString());
        Program.Check(!off.Active && off.InWorld && !ListIds(world, "world_resource_packs.json").Contains(r1.ToString()), "turning a pack off takes it off the list and keeps it in the world");

        // Uninstall: the folder moves aside, never deleted.
        string? moved = WorldPacks.Uninstall(w, off, removed);
        Program.Check(moved != null && Directory.Exists(moved) && !Directory.Exists(off.Location!), "uninstalling moves the pack folder out of the world into the removed-packs folder");
        Program.Check(!WorldPacks.Read(w).Any(p => p.Id == r1.ToString()), "the uninstalled pack is gone from the world's packs");

        // Hostile and wrong packs.
        string slip = MakeSlipZip(Path.Combine(Root, "slip.mcpack"), "manifest.json", "textures/a.png", "../../escaped2.txt", manifestUuid: Guid.NewGuid());
        Program.Check(Refused(() => WorldPacks.Install(w, slip, removed)), "a zip-slip .mcpack is refused");
        Program.Check(!File.Exists(Path.Combine(world, "escaped2.txt")) && !File.Exists(Path.Combine(Root, "escaped2.txt")), "nothing from it landed outside the pack folder");
        Program.Check(!Directory.EnumerateDirectories(Path.Combine(world, "resource_packs")).Any(d => Path.GetFileName(d).StartsWith(".installing")), "no half-installed pack is left in resource_packs/");
        Program.Check(Refused(() => WorldPacks.Install(w, MakeJavaPack(Path.Combine(Root, "javadp.zip"), data: true), removed)), "a Java data pack is refused for a Bedrock world, with a reason");

        Program.Check(before.SequenceEqual(Hashes(src)), "the test world in TestData is byte-for-byte unchanged");
    }

    static List<string> ListIds(string world, string file)
    {
        string path = Path.Combine(world, file);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsArray().Select(e => e!["pack_id"]!.ToString()).ToList() : [];
    }

    // ---------------------------------------------------------------- pack makers

    static string MakeJavaPack(string path, bool data)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "pack.mcmeta", "{\"pack\":{\"pack_format\":6,\"description\":\"A test pack\"}}");
        Add(zip, data ? "data/test/functions/hello.mcfunction" : "assets/minecraft/textures/block/stone.png", data ? "say hello" : "png");
        return path;
    }

    static string MakeBedrockPack(string path, Guid uuid, string module, string name)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddBedrock(zip, "", uuid, module, name);
        return path;
    }

    static string MakeAddon(string path, Guid behavior, Guid resource)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddBedrock(zip, "Behavior/", behavior, "data", "Test Behavior");
        AddBedrock(zip, "Resources/", resource, "resources", "Test Resources");
        return path;
    }

    static void AddBedrock(ZipArchive zip, string root, Guid uuid, string module, string name)
    {
        Add(zip, root + "manifest.json", $"{{\"format_version\":2,\"header\":{{\"name\":\"pack.name\",\"description\":\"pack.description\",\"uuid\":\"{uuid}\",\"version\":[1,2,3]}},\"modules\":[{{\"type\":\"{module}\",\"uuid\":\"{Guid.NewGuid()}\",\"version\":[1,0,0]}}]}}");
        Add(zip, root + "texts/en_US.lang", $"pack.name={name}\npack.description=Made for the test\n");
        Add(zip, root + (module == "resources" ? "textures/blocks/stone.png" : "entities/test.json"), "x");
    }

    static string MakeSlipZip(string path, string meta, string inside, string escape, Guid? manifestUuid = null)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, meta, manifestUuid is Guid u
            ? $"{{\"header\":{{\"name\":\"Evil\",\"uuid\":\"{u}\",\"version\":[1,0,0]}},\"modules\":[{{\"type\":\"resources\"}}]}}"
            : "{\"pack\":{\"pack_format\":6,\"description\":\"evil\"}}");
        Add(zip, inside, "x");
        Add(zip, escape, "escaped");
        return path;
    }

    static void Add(ZipArchive zip, string name, string text)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(text));
    }

    // ---------------------------------------------------------------- helpers

    static bool Refused(Action a)
    {
        try { a(); return false; }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException or NotSupportedException) { return true; }
    }

    static void CopyDir(string src, string dest)
    {
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dest, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
    }

    static List<string> Hashes(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Select(f =>
        {
            using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Path.GetRelativePath(dir, f) + " " + Convert.ToHexString(SHA256.HashData(s));
        }).ToList();
}
