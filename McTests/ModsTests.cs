// McTests/ModsTests.cs: "mods [world …]" — which mods, add-ons and data packs a world holds things of.
// Owns: ModsTests — the two Forge Twilight Forest samples (1.16.5 by name, 1.12.2's numbered blocks named through Forge's
// table): the loader and the mod list, the mod's dimension (1.12's DIM7 told by what is in it), block, mob and item counts
// equal to Find's, the mod's biomes from Forge's biome numbers (and a converting reader still reading them as plains),
// Forge's tables; samples of every other edition: nothing found; mod jars of every loader made in the test folder (Fabric,
// Quilt, Forge with a ${file.jarVersion}, NeoForge, mcmod.info in both shapes, a broken fabric.mod.json, a switched-off and a
// damaged jar) read right and matched — by id first, then by assets, then by data; a data pack's and a behaviour pack's
// namespaces; the words; nothing written into the worlds. Worlds given are only reported (with the mods on this PC).
using System.IO.Compression;
using System.Text;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.World;

static class ModsTests
{
    public static void Run(string[] args)
    {
        if (args.Length > 0)
        {
            var installed = InstalledMods.Find();
            Program.Info($"{installed.Count:N0} mods on this PC");
            foreach (var path in args)
            {
                using var w = WorldLoader.Open(path);
                var near = InstalledMods.Find(WorldMods.NearbyGameFolders(w));
                Console.WriteLine(WorldMods.Read(w, near).Text(false));
            }
            return;
        }
        Words();
        Jars();
        PackNamespaces();
        Forge116();
        Forge112();
        Vanilla();
    }

    static void Words()
    {
        Program.Check(ModUse.Count(1, "chunks") == "1 chunk" && ModUse.Count(2, "chunks") == "2 chunks" && ModUse.Count(1, "kinds of entity") == "1 kind of entity"
            && ModUse.Count(1, "jobs for villagers") == "1 job for villagers" && ModUse.Count(1, "entities") == "1 entity" && ModUse.Count(1_234, "blocks") == "1,234 blocks"
            && ModUse.Count(1, "data packs") == "1 data pack" && ModUse.Count(1, "add-ons") == "1 add-on" && ModUse.Count(1, "kinds of block entity") == "1 kind of block entity",
            "counts read right: 1 chunk, 2 chunks, 1 kind of entity, 1 job for villagers, 1 entity, 1,234 blocks, 1 data pack, 1 add-on");
        Program.Check(ModUse.Short("byg:lush_dirt") == "lush dirt" && ModUse.Short(":block 256") == "block 256", "names are shown without their namespace");
    }

    static void Zip(string path, params (string Name, string Text)[] files)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(Encoding.UTF8.GetBytes(text));
        }
    }

    /// <summary>One jar of each kind in a game folder of the test's own; each read as its loader says.</summary>
    static void Jars()
    {
        string game = Path.Combine(TestPaths.Output, "mods-test", "game"), mods = Path.Combine(game, "mods");
        if (Directory.Exists(game)) Directory.Delete(game, true);
        Zip(Path.Combine(mods, "fabric.jar"),
            ("fabric.mod.json", "{\"schemaVersion\": 1, \"id\": \"testfab\", \"name\": \"Test Fabric\", \"version\": \"1.2.3\", \"depends\": {\"minecraft\": \"1.19.x\", \"fabricloader\": \">=0.14\"}}"),
            ("assets/testfab/lang/en_us.json", "{}"), ("data/testfab_extra/tags/blocks/x.json", "{}"), ("data/minecraft/tags/blocks/logs.json", "{}"));
        Zip(Path.Combine(mods, "quilt.jar"),
            ("quilt.mod.json", "{\"schema_version\": 1, \"quilt_loader\": {\"id\": \"testquilt\", \"version\": \"2.0\", \"metadata\": {\"name\": \"Test Quilt\"}, \"depends\": [\"fabric-api\", {\"id\": \"minecraft\", \"versions\": \">=1.20\"}]}}"));
        Zip(Path.Combine(mods, "forge.jar"),
            ("META-INF/mods.toml", "modLoader=\"javafml\" # the loader\nloaderVersion=\"[36,)\"\nlicense='MIT'\n[[mods]]\nmodId=\"testforge\"\nversion=\"${file.jarVersion}\"\ndisplayName=\"Test Forge\" # its name\ndescription='''\nA mod.\nmodId=\"notamod\"\n'''\n[[dependencies.testforge]]\n    modId=\"forge\"\n    versionRange=\"[36,)\"\n[[dependencies.testforge]]\n    modId=\"minecraft\"\n    versionRange=\"[1.16.5,1.17)\"\n"),
            ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\nImplementation-Version: 4.5.6\r\n"), ("assets/testforge/blockstates/thing.json", "{}"));
        Zip(Path.Combine(mods, "neoforge.jar"),
            ("META-INF/neoforge.mods.toml", "modLoader=\"javafml\"\n[[mods]]\nmodId=\"testneo\"\nversion=\"7.8\"\ndisplayName=\"Test \\\"Neo\\\" C:\\Users\"\n[[dependencies.testneo]]\nmodId=\"minecraft\"\ntype=\"required\"\nversionRange=\"[1.21,1.22)\"\n"));
        Zip(Path.Combine(mods, "1.12.2", "old.jar"),
            ("mcmod.info", "[\n{\n  \"modid\": \"oldmod\", \"name\": \"Old Mod\", \"version\": \"0.9\", \"mcversion\": \"1.12.2\",\n}\n]"));
        Zip(Path.Combine(mods, "old2.jar"), ("mcmod.info", "{\"modListVersion\": 2, \"modList\": [{\"modid\": \"oldtwo\", \"name\": \"Old Two\", \"version\": \"${version}\", \"mcversion\": \"${mcversion}\"}]}"));
        Zip(Path.Combine(mods, "broken-json.jar"), ("fabric.mod.json", "{\"id\": \"brokenjson\", \"name\": \"Broken\", \"version\": \"1\", \"description\": \"two\nlines\"}"));
        Zip(Path.Combine(mods, "off.jar.disabled"), ("fabric.mod.json", "{\"id\": \"offmod\", \"version\": \"1\"}"));
        File.WriteAllBytes(Path.Combine(mods, "damaged.jar"), [0x50, 0x4B, 3, 4, 1, 2, 3]);
        File.WriteAllText(Path.Combine(mods, "readme.txt"), "not a jar");

        var found = InstalledMods.Find([("Test launcher", game)]).Where(m => m.GameFolder == game).ToList();
        InstalledMod? Of(string id) => found.FirstOrDefault(m => m.Id == id);
        Program.Check(found.Count == 8, $"8 mods read from the test's jars, the damaged jar and the text file passed over (read {found.Count}: {string.Join(", ", found.Select(m => m.Id))})");
        Program.Check(Of("testfab") is { Name: "Test Fabric", Version: "1.2.3", Loader: "Fabric", GameVersion: "1.19.x", Disabled: false } f
            && f.Namespaces.SequenceEqual(["testfab"]) && f.DataNamespaces.SequenceEqual(["testfab_extra"]),
            "Fabric: id, name, version, game version; assets and data namespaces (minecraft's left out)");
        Program.Check(Of("testquilt") is { Name: "Test Quilt", Version: "2.0", Loader: "Quilt", GameVersion: ">=1.20" }, "Quilt: id, name, version, game version");
        Program.Check(Of("testforge") is { Name: "Test Forge", Version: "4.5.6", Loader: "Forge", GameVersion: "[1.16.5,1.17)" } && Of("notamod") == null,
            "Forge mods.toml: ${file.jarVersion} from the manifest, comments after values, the minecraft dependency, a multi-line description passed over");
        Program.Check(Of("testneo") is { Name: "Test \"Neo\" C:\\Users", Version: "7.8", Loader: "NeoForge", GameVersion: "[1.21,1.22)" },
            "NeoForge: neoforge.mods.toml, an escaped quote and a backslash in a name");
        Program.Check(Of("oldmod") is { Name: "Old Mod", Version: "0.9", Loader: "Forge", GameVersion: "1.12.2" } o && o.File.Contains(Path.Combine("mods", "1.12.2")),
            "Forge 1.12 mcmod.info (a list, a trailing comma), in mods/1.12.2/");
        Program.Check(Of("oldtwo") is { Name: "Old Two", Version: "", GameVersion: null }, "mcmod.info's modList shape; unfilled ${…} left out");
        Program.Check(Of("brokenjson") is { Name: "Broken", Version: "1" }, "a fabric.mod.json that is not quite JSON is still read");
        Program.Check(Of("offmod") is { Disabled: true } off && off.Describe().Contains("switched off"), "a switched-off jar (.jar.disabled) is listed as switched off");
        var again = InstalledMods.Find([("Test launcher", game)]).Where(m => m.GameFolder == game).ToList();
        Program.Check(again.Count == found.Count && again.Zip(found).All(p => ReferenceEquals(p.First, p.Second)), "a jar that hasn't changed is read once");
        var real = InstalledMods.Find();
        Program.Info($"{real.Count:N0} mods on this PC" + (real.Count > 0 ? ": " + string.Join(", ", real.Take(6).Select(m => m.Describe())) + (real.Count > 6 ? " …" : "") : ""));
    }

    static void PackNamespaces()
    {
        string root = Path.Combine(TestPaths.Output, "mods-test", "packs");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string folder = Path.Combine(root, "folderpack");
        foreach (var d in new[] { "data/alpha/worldgen/biome", "data/minecraft/tags", "data/Bad Name/x" }) Directory.CreateDirectory(Path.Combine(folder, d));
        Zip(Path.Combine(root, "zipped.zip"), ("Zipped/pack.mcmeta", "{}"), ("Zipped/data/beta/worldgen/biome/x.json", "{}"), ("Zipped/data/gamma/function/f.mcfunction", "say hi"));
        string bp = Path.Combine(root, "bp");
        Directory.CreateDirectory(Path.Combine(bp, "blocks", "deep"));
        File.WriteAllText(Path.Combine(bp, "blocks", "deep", "ruby.json"), "{ \"format_version\": \"1.20.0\", \"minecraft:block\": { \"description\": { \"identifier\": \"delta:ruby_block\" } } }");
        Directory.CreateDirectory(Path.Combine(bp, "entities"));
        File.WriteAllText(Path.Combine(bp, "entities", "pig.json"), "// a comment\n{ \"minecraft:entity\": { \"description\": { \"identifier\": \"minecraft:pig\" } } }\n{\"identifier\":\"epsilon:yeti\"}");
        Program.Check(WorldMods.PackNamespaces(folder, false).SequenceEqual(["alpha"]), "a data pack folder's namespaces (minecraft and a name the game refuses left out)");
        Program.Check(WorldMods.PackNamespaces(Path.Combine(root, "zipped.zip"), false).SequenceEqual(["beta", "gamma"]), "a zipped data pack's, its files in a folder inside the zip");
        Program.Check(WorldMods.PackNamespaces(bp, true).SequenceEqual(["delta", "epsilon"]), "a behaviour pack's, from its blocks and entities (vanilla mobs it changes left out)");
    }

    static (Dictionary<string, long> Blocks, Dictionary<string, long> Entities, Dictionary<string, long> Items) Find(SaveWorld w, string query)
    {
        var blocks = new Dictionary<string, long>();
        var entities = new Dictionary<string, long>();
        var items = new Dictionary<string, long>();
        void Add(Dictionary<string, long> to, Dictionary<string, long> from) { foreach (var (k, v) in from) to[k] = to.GetValueOrDefault(k) + v; }
        bool first = true;
        foreach (var dim in w.Dimensions)
        {
            var r = WorldSearch.Run(w, dim, new SearchOptions { Query = query, Text = false, Players = first, MaxBlockHits = 0, MaxHits = 0 });
            Add(blocks, r.BlockCounts);
            Add(entities, r.EntityCounts);
            Add(items, r.ItemCounts);
            first = false;
        }
        return (blocks, entities, items);
    }

    static bool Same(Dictionary<string, long> a, Dictionary<string, long> b) => a.Count == b.Count && a.All(kv => b.GetValueOrDefault(kv.Key) == kv.Value);

    static void Forge116()
    {
        string path = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_16_5_twilight_forest");
        if (!Directory.Exists(path)) { Program.Info("(skipped the 1.16.5 Twilight Forest world: not in TestData)"); return; }
        var before = CensusTests.Hashes(path);
        using (var w = WorldLoader.Open(path))
        {
            var mine = new InstalledMod("twilightforest", "The Twilight Forest", "4.0.490", "Forge", "[1.16.5,1.17)", "tf.jar", path, "Test", ["twilightforest"], [], false);
            var assets = mine with { Id = "tfcompanion", Name = "Companion", File = "companion.jar" };
            var data = mine with { Id = "tfdata", Name = "Data", File = "data.jar", Namespaces = [], DataNamespaces = ["twilightforest"] };
            var r = WorldMods.Read(w, [data, assets, mine]);
            var tf = r.Mods.FirstOrDefault(m => m.Namespace == "twilightforest");
            Program.Check(r.Loader == "Forge 36.1.23" && r.Brands.SequenceEqual(["forge"]), $"1.16.5: saved by Forge 36.1.23 (read: {r.Loader}; brands {string.Join(", ", r.Brands)})");
            Program.Check(r.Found.Select(m => m.Namespace).SequenceEqual(["twilightforest"]) && tf is { Listed: true },
                $"1.16.5: only the Twilight Forest found, and it is in the mod list (found: {string.Join(", ", r.Found.Select(m => m.Namespace))})");
            if (tf == null) return;
            Program.Check(tf.Dimensions.SequenceEqual(["twilightforest:twilightforest"]), "1.16.5: its own dimension, twilightforest:twilightforest");
            var (blocks, entities, items) = Find(w, "twilightforest:*");
            Program.Check(Same(tf.Blocks, blocks) && tf.BlockCount == 364_265, $"1.16.5: {tf.BlockCount:N0} of its blocks in {tf.Blocks.Count} kinds, each as many as Find counts ({blocks.Values.Sum():N0})");
            Program.Check(Same(tf.Entities, entities) && tf.Entities.Count > 5, $"1.16.5: its mobs as Find counts them ({string.Join(", ", tf.Entities.Take(4).Select(kv => $"{kv.Value} {kv.Key}"))} …)");
            Program.Check(Same(tf.Items, items) && tf.Items.ContainsKey("twilightforest:canopy_sapling"), $"1.16.5: its items, in chests and with the players, as Find counts them ({tf.Items.Values.Sum():N0})");
            Program.Check(tf.BlockEntities.GetValueOrDefault("twilightforest:firefly") == 151, "1.16.5: its block entities (151 fireflies)");
            Program.Check(tf.Biomes.Count >= 3 && tf.Biomes.Keys.All(b => b.StartsWith("twilightforest:", StringComparison.Ordinal)) && tf.Biomes.ContainsKey("twilightforest:forest"),
                $"1.16.5: its biomes, from Forge's numbers ({string.Join(", ", tf.Biomes.Select(kv => $"{ModUse.Short(kv.Key)} {kv.Value}"))})");
            var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "twilightforest:twilightforest");
            var plain = new CanonicalReader(w, dim);
            var c = dim.ChunkPositions.Select(plain.Get).First(x => x != null)!;
            Program.Check(c.Biomes.Values.SelectMany(x => x).All(b => b == null || b.StartsWith("minecraft:", StringComparison.Ordinal)),
                "1.16.5: a converting reader still reads the mod's biome numbers as the game's own (no target gets a biome it doesn't know)");
            Program.Check(tf.Registered.GetValueOrDefault("blocks") == 312 && tf.Registered.GetValueOrDefault("items") == 441 && tf.Registered.ContainsKey("kinds of entity"),
                "1.16.5: Forge's tables in level.dat: 312 blocks, 441 items, its kinds of entity");
            Program.Check(tf.OnThisPc.SequenceEqual([mine]) && tf.Source.Contains("The Twilight Forest 4.0.490 (Forge, for [1.16.5,1.17))"),
                $"1.16.5: the mod on this PC whose id it is comes before those that only have its assets or data ({tf.Source})");
            var byAssets = WorldMods.Read(w, [data, assets]).Mods.First(m => m.Namespace == "twilightforest");
            Program.Check(byAssets.OnThisPc.SequenceEqual([assets]), "1.16.5: without it, the mod with its assets comes before one with only its data");
            string text = r.Text();
            Program.Check(text.Contains("Saved by Forge 36.1.23.") && text.Contains("1 mod found in 1,560 chunks.") && text.Contains("Opened without a mod"),
                "1.16.5: the report says what saved it, how much was read, and what happens without the mod");
        }
        Program.Check(before.SequenceEqual(CensusTests.Hashes(path)), "1.16.5: nothing in the world changed");
    }

    static void Forge112()
    {
        string path = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_12_2_twilight_forest");
        if (!Directory.Exists(path)) { Program.Info("(skipped the 1.12.2 Twilight Forest world: not in TestData)"); return; }
        var before = CensusTests.Hashes(path);
        using (var w = WorldLoader.Open(path))
        {
            var r = WorldMods.Read(w);
            var tf = r.Mods.FirstOrDefault(m => m.Namespace == "twilightforest");
            Program.Check(r.Loader == "Forge 14.23.5.2847" && tf is { Listed: true, ListedVersion: "3.11.1021" }, $"1.12.2: saved by Forge 14.23.5.2847, Twilight Forest 3.11.1021 in the mod list (read: {r.Loader}, {tf?.ListedVersion})");
            Program.Check(!r.Mods.Any(m => m.Namespace is "mcp" or "FML" or "fml" or "forge" or "minecraft"), "1.12.2: the game and the loader are not listed as mods");
            if (tf == null) return;
            Program.Check(tf.LikelyDimensions.SequenceEqual(["DIM7"]) && tf.Dimensions.Count == 0, "1.12.2: DIM7 is told to be the mod's by what is in it");
            // Find knows the numbered blocks only by their numbers: the same blocks, named through Forge's table.
            var (blocks, _, _) = Find(w, "legacy_block_*");
            long numbered = blocks.Values.Sum();
            Program.Check(numbered > 0 && tf.BlockCount == numbered && !r.Mods.Any(m => m.Namespace.Length == 0) && tf.Blocks.ContainsKey("twilightforest:twilight_leaves"),
                $"1.12.2: every numbered block named through Forge's table ({tf.BlockCount:N0} of {numbered:N0}: {string.Join(", ", tf.Blocks.OrderByDescending(kv => kv.Value).Take(3).Select(kv => ModUse.Short(kv.Key)))} …)");
            var (_, entities, _) = Find(w, "twilightforest:*");
            Program.Check(Same(tf.Entities, entities) && tf.BlockEntities.ContainsKey("twilightforest:firefly"), "1.12.2: its mobs as Find counts them; its block entities");
            Program.Check(tf.Registered.GetValueOrDefault("blocks") == 171 && tf.Registered.GetValueOrDefault("biomes") == 20, "1.12.2: Forge's tables: 171 blocks, 20 biomes");
        }
        Program.Check(before.SequenceEqual(CensusTests.Hashes(path)), "1.12.2: nothing in the world changed");
    }

    static void Vanilla()
    {
        // A world without mods whose data pack adds a dimension: the pack's namespace, said to come from the pack.
        string dp = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (Directory.Exists(dp))
        {
            using var w = WorldLoader.Open(dp);
            var r = WorldMods.Read(w);
            var found = r.Found.ToList();
            string text = r.Text();
            Program.Check(found is [{ Namespace: "inf", Pack: "inf-dungeons-datapack-e1060.zip" } inf] && inf.Dimensions.SequenceEqual(["inf:inf_dim"]) && inf.Source.Contains("data pack")
                && text.Contains("1 data pack found") && !text.Contains("Opened without a mod") && r.Brands.SequenceEqual(["vanilla"]) && r.Loader == null,
                $"Java 1.16.5 with a data pack's dimension: the pack's namespace, from that pack, its dimension; no mod (found: {string.Join(", ", found.Select(m => $"{m.Namespace} from {m.Pack}"))})");
        }
        foreach (var (label, path) in new[]
        {
            ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
            ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
            ("Pocket 0.x", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")),
            ("Indev", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "hell.mclevel")),
        })
        {
            if (!Directory.Exists(path) && !File.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            using var w = WorldLoader.Open(path);
            var r = WorldMods.Read(w);
            Program.Check(!r.Found.Any() && r.Text().Contains("every block, mob, item and biome is the game's own"),
                $"{label}: nothing from a mod or an add-on ({r.ChunksRead:N0} chunks read){(r.Found.Any() ? ": " + string.Join(", ", r.Found.Select(m => m.Namespace)) : "")}");
        }
    }
}
