// McTests/StructureMapTests.cs: "structuremap" — the structures on the map (StructureMap, StructureKinds).
// Owns: StructureMapTests — saved ids of every version, loot tables of both editions and spawner mobs to kinds and names; the
// sample worlds as they are (the Java 1.16 world's saved villages, mineshafts, ruined portals and ocean ruin with their
// unopened chests, its dungeons told by spawners; the Bedrock world's fortresses joined from their spawn-area pieces and its
// buried treasure told by unopened chests); and written into copies: a Java 1.20 start (structures.starts, box from its
// pieces) with a vault inside counting for it and a lone buried-treasure chest beside, data/Village.dat and Temple.dat of
// a world from before 1.13 (a "Temple" told apart by its first piece), a Bedrock village record. A console save (Xbox 360
// TU70): its own data/*.dat structures (4J's byte layout) with their boxes, ocean ruins at their chests' height, none past
// the world's edge; its Nether 1:3, the portal pair leading both ways. The worlds are copies; nothing outside the test
// folder changes.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class StructureMapTests
{
    public static void Run(string[] args)
    {
        string temp = Path.Combine(TestPaths.Output, "structuremap", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(temp);
        Kinds();
        Samples();
        Java120(Path.Combine(temp, "java120"));
        OldJava(Path.Combine(temp, "java112"));
        BedrockVillage(Path.Combine(temp, "bedrock"));
        PortalLinks(Path.Combine(temp, "portals"));
        ConsoleSave();
    }

    /// <summary>The Xbox 360 TU70 sample as it is: its data/*.dat structures and its portals (the save is only read).</summary>
    static void ConsoleSave()
    {
        Console.WriteLine("== A console save's structures and portals (Xbox 360 TU70)");
        string x360 = Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");
        if (!File.Exists(x360)) { Console.WriteLine("   (not found: skipped)"); return; }
        using var w = WorldLoader.Open(x360);
        var sites = StructureMap.Find(w, w.Dimensions).Sites;
        int Saved(string key, string dim) => sites.Count(s => s.Key == key && s.Saved && s.DimensionId == w.Dimensions.First(d => CanonicalReader.DimensionId(d) == dim).Id);
        const string Ow = "minecraft:overworld";
        Program.Check(Saved("village", Ow) == 3 && Saved("mansion", Ow) == 2 && Saved("stronghold", Ow) == 1 && Saved("swamp_hut", Ow) == 1 && Saved("mineshaft", Ow) == 4 && Saved("buried_treasure", Ow) == 1,
            $"its own records: 3 villages, 2 mansions, the stronghold, a witch hut (a Temple whose first piece is TeSH), 4 mineshafts, buried treasure ({Saved("village", Ow)}, {Saved("mansion", Ow)}, {Saved("stronghold", Ow)}, {Saved("swamp_hut", Ow)}, {Saved("mineshaft", Ow)}, {Saved("buried_treasure", Ow)})");
        Program.Check(Saved("fortress", "minecraft:the_nether") == 1 && Saved("end_city", "minecraft:the_end") == 1, "the Nether's fortress and the End's city, kept in the same folder as the Overworld's");
        var ruins = sites.Where(s => s.Key == "ocean_ruin").ToList();
        Program.Check(ruins.Count == 5 && ruins.All(r => r.Saved && r.Unlooted == 1 && r.Y is >= 20 and <= 50),
            $"5 ocean ruins, each with its chest and standing at its height (the save keeps their template's, y 0 – 6): {string.Join(", ", ruins.Select(r => $"{r.X} {r.Y} {r.Z}"))}");
        var have = w.Dimensions.ToDictionary(d => d.Id, d => d.ChunkPositions.ToHashSet());
        bool OnLand(StructureSite s)
        {
            for (int cz = s.Box.MinZ >> 4; cz <= s.Box.MaxZ >> 4; cz++)
                for (int cx = s.Box.MinX >> 4; cx <= s.Box.MaxX >> 4; cx++)
                    if (have[s.DimensionId].Contains(new ChunkPos(cx, cz))) return true;
            return false;
        }
        Program.Check(sites.Where(s => s.Saved).All(OnLand), "none past the world's edge (the files also keep starts out there)");
        Program.Check(Portals.Scale(w) == 3, "its Nether is 1:3 (level.dat's HellScale)");
        var portals = sites.Where(s => s.Key == "nether_portal").ToList();
        Program.Check(portals.Count == 2 && portals.All(p => p.Notes.Any(n => n.Contains("which leads back here"))),
            $"its portal pair leads both ways: {string.Join(" | ", portals.Select(p => string.Join("; ", p.Notes)))}");
    }

    /// <summary>
    /// Built portals (Portals): into copies of the Bedrock 1.18 sample, a portal in the Nether, its partner in the Overworld
    /// at eight times its place, and a lone Overworld portal far off. The partners lead to each other; the lone one makes a
    /// new portal (none within 16 blocks of its spot in the Nether); the sites carry it all.
    /// </summary>
    static void PortalLinks(string temp)
    {
        Console.WriteLine("== Built portals and where they lead");
        string bedrock = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla");
        if (!Directory.Exists(bedrock)) return;
        const string Ow = "minecraft:overworld", Ne = "minecraft:the_nether";
        ChunkPos netherChunk = default, owChunk = default, lone = default;
        List<Portal> ow0, ne0;
        using (var w = WorldLoader.Open(bedrock))
        {
            var owDim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == Ow);
            var neDim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == Ne);
            // The sample's own portals (its author went to the Nether): counted first, and their link told.
            (ow0, ne0) = (Portals.Find(w, owDim), Portals.Find(w, neDim));
            foreach (var l in Portals.Links(ow0, ne0, Portals.NewRules(w))) Program.Info($"the sample's own {(l.From.DimensionId == Ow ? "Overworld" : "Nether")} portal at {l.From.X}, {l.From.Y}, {l.From.Z} {l.Words}");
            var ow = owDim.ChunkPositions.ToHashSet();
            // A Nether chunk away from the sample's portals whose spot × 8 is a generated Overworld chunk.
            netherChunk = neDim.ChunkPositions.Where(p => ne0.All(q => Math.Abs(q.X - p.X * 16) + Math.Abs(q.Z - p.Z * 16) > 48))
                .First(p => ow.Contains(new ChunkPos((p.X * 16 + 8) * 8 >> 4, (p.Z * 16 + 8) * 8 >> 4)));
            owChunk = new ChunkPos((netherChunk.X * 16 + 8) * 8 >> 4, (netherChunk.Z * 16 + 8) * 8 >> 4);
            // The lone one: an Overworld chunk whose spot ÷ 8 is far from every Nether portal (the sample's and the new one).
            var netherSpots = ne0.Select(q => (q.X, q.Z)).Append((netherChunk.X * 16 + 8, netherChunk.Z * 16 + 8)).ToList();
            lone = ow.OrderByDescending(p => netherSpots.Min(q => Math.Abs((p.X * 16 + 8) / 8 - q.Item1) + Math.Abs((p.Z * 16 + 8) / 8 - q.Item2))).First();
        }
        // A 2-wide, 3-tall portal (with its obsidian frame) at a chunk's middle, at y 70.
        Action<CanonicalChunk> Portal(int localX) => c =>
        {
            for (int y = 69; y <= 73; y++)
                for (int x = localX - 1; x <= localX + 2; x++)
                {
                    bool frame = y is 69 or 73 || x == localX - 1 || x == localX + 2;
                    c.SetBlock(x, y, 8, BlockState.Get(frame ? "minecraft:obsidian" : "minecraft:nether_portal", frame ? "" : "axis=x"));
                }
        };
        string step1 = Path.Combine(temp, "nether"), step2 = Path.Combine(temp, "overworld");
        using (var w = WorldLoader.Open(bedrock))
            step1 = MinecraftStudio.Core.Structures.StructurePlacement.EditIntoCopy(w, Ne, [netherChunk], Portal(8), step1).OpenPath;
        using (var w = WorldLoader.Open(step1))
            step2 = MinecraftStudio.Core.Structures.StructurePlacement.EditIntoCopy(w, Ow, [owChunk, lone], Portal(8), step2).OpenPath;
        using (var w = WorldLoader.Open(step2))
        {
            var owDim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == Ow);
            var neDim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == Ne);
            var owPortals = Portals.Find(w, owDim);
            var nePortals = Portals.Find(w, neDim);
            bool Mine(Portal p, ChunkPos c) => p.X >> 4 == c.X && p.Z >> 4 == c.Z && p.Blocks == 6;
            Program.Check(owPortals.Count == ow0.Count + 2 && nePortals.Count == ne0.Count + 1 && owPortals.Count(p => Mine(p, owChunk) || Mine(p, lone)) == 2 && nePortals.Count(p => Mine(p, netherChunk)) == 1,
                $"built portals found by their blocks: the sample's {ow0.Count} + 2 in the Overworld, {ne0.Count} + 1 in the Nether, 6 portal blocks each");
            var links = Portals.Links(owPortals, nePortals, Portals.NewRules(w));
            var partner = links.Single(l => Mine(l.From, owChunk));
            var far = links.Single(l => Mine(l.From, lone));
            var back = links.Single(l => Mine(l.From, netherChunk));
            Program.Check(partner.To == back.From && partner.Words.Contains("leads back here") && back.To == partner.From,
                $"the partners lead to each other (\"{partner.Words}\")");
            Program.Check(far.To == null && far.Words.Contains("makes a new portal") && far.Words.Contains("16 blocks"), $"the lone portal makes a new one (\"{far.Words}\")");
            var sites = StructureMap.Find(w, [owDim]).Sites.Where(s => s.Key == "nether_portal").ToList();
            Program.Check(sites.Count == ow0.Count + 2 && sites.All(s => s.Notes.Count == 1) && StructureKinds.Name("nether_portal") == "Nether portal (built)",
                $"the Overworld's structures list both, each with where it leads ({sites.Count})");
        }
    }

    static void Kinds()
    {
        Console.WriteLine("== Kinds and names");
        foreach (var (id, key, name) in new[]
        {
            ("minecraft:village_desert", "village", "Desert village"), ("minecraft:village", "village", "Village"), ("Village", "village", "Village"),
            ("Jungle_Pyramid", "jungle_pyramid", "Jungle temple"), ("TeDP", "desert_pyramid", "Desert temple"), ("TeSH", "swamp_hut", "Witch hut"),
            ("Iglu", "igloo", "Igloo"), ("EndCity", "end_city", "End city"), ("minecraft:ruined_portal_ocean", "ruined_portal", "Ruined portal (ocean)"),
            ("minecraft:mineshaft_mesa", "mineshaft", "Mineshaft (badlands)"), ("minecraft:ocean_ruin_warm", "ocean_ruin", "Ocean ruins (warm)"),
            ("minecraft:trial_chambers", "trial_chambers", "Trial chambers"), ("minecraft:buried_treasure", "buried_treasure", "Buried treasure"),
            ("repurposed_structures:mansion_birch", "repurposed_structures:mansion_birch", "Repurposed structures mansion birch"),
        })
        {
            var k = StructureKinds.FromStructureId(id);
            Program.Check(k is { } got && got.Key == key && got.Name == name, $"{id} → {key}, \"{name}\" (got {k})");
        }
        Program.Check(StructureKinds.FromStructureId("INVALID") == null, "an empty start (INVALID) is no structure");
        foreach (var (table, key) in new[]
        {
            ("minecraft:chests/buried_treasure", "buried_treasure"), ("loot_tables/chests/buriedtreasure.json", "buried_treasure"),
            ("loot_tables/chests/shipwrecksupply.json", "shipwreck"), ("minecraft:archaeology/trail_ruins_rare", "trail_ruins"),
            ("minecraft:chests/village/village_plains_house", "village"), ("minecraft:chests/jungle_temple_dispenser", "jungle_pyramid"),
            ("minecraft:chests/trial_chambers/reward", "trial_chambers"), ("minecraft:chests/nether_bridge", "fortress"),
        })
            Program.Check(StructureKinds.FromLootTable(table) == key, $"loot table {table} → {key}");
        Program.Check(StructureKinds.FromLootTable("minecraft:chests/spawn_bonus_chest") == null, "the bonus chest is no structure");
        Program.Check(StructureKinds.FromSpawner("minecraft:cave_spider") == "mineshaft" && StructureKinds.FromSpawner("minecraft:blaze") == "fortress"
            && StructureKinds.FromSpawner("minecraft:zombie") == "dungeon" && StructureKinds.FromSpawner("minecraft:pig") == null, "spawner mobs → mineshaft, fortress, dungeon; a pig spawner is a player's own");
    }

    static void Samples()
    {
        Console.WriteLine("== The sample worlds as they are");
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (Directory.Exists(java))
        {
            using var w = WorldLoader.Open(java);
            var sites = StructureMap.Find(w, w.Dimensions).Sites;
            int Count(string key, bool saved) => sites.Count(s => s.Key == key && s.Saved == saved);
            Program.Check(Count("village", true) == 2 && Count("mineshaft", true) == 13 && Count("ruined_portal", true) == 2 && Count("ocean_ruin", true) == 1,
                $"Java 1.16: the saved structures (2 villages, 13 mineshafts, 2 ruined portals, an ocean ruin; got {Summary(sites.Where(s => s.Saved))})");
            Program.Check(sites.Where(s => s.Key == "village").All(s => s.Unlooted == 2), "Java 1.16: each village's 2 unopened chests count for it");
            Program.Check(Count("dungeon", false) == 18 && sites.Where(s => s.Key == "dungeon").All(s => s.Notes.Any(n => n.Contains("spawner"))),
                $"Java 1.16: 18 dungeons told by their spawners ({Count("dungeon", false)})");
            Program.Check(sites.Where(s => s.Key == "mineshaft").Count(s => s.Notes.Any(n => n.Contains("cave spider spawner"))) >= 6,
                "Java 1.16: the cave spider spawners count for their mineshafts (no mineshaft of their own)");
        }
        string bedrock = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla");
        if (Directory.Exists(bedrock))
        {
            using var w = WorldLoader.Open(bedrock);
            var sites = StructureMap.Find(w, w.Dimensions).Sites;
            var nether = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:the_nether").Id;
            var fortresses = sites.Where(s => s.Key == "fortress" && s.DimensionId == nether).ToList();
            Program.Check(fortresses.Count == 2 && fortresses.All(s => s.Saved), $"Bedrock: 2 nether fortresses, joined from their spawn-area pieces ({fortresses.Count})");
            var treasure = sites.Where(s => s.Key == "buried_treasure").ToList();
            Program.Check(treasure.Count == 37 && treasure.All(s => s.Unlooted == 1), $"Bedrock: 37 buried treasures, each still buried ({treasure.Count})");
            Program.Check(sites.Any(s => s.Key == "mineshaft") && sites.Any(s => s.Key == "igloo") && sites.Any(s => s.Key == "ocean_ruin"),
                $"Bedrock: mineshafts, an igloo and ocean ruins told by what they left ({Summary(sites)})");
        }
    }

    static string Summary(IEnumerable<StructureSite> sites) =>
        string.Join(", ", sites.GroupBy(s => s.Key).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}"));

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    static NbtIntArray BB(int x0, int y0, int z0, int x1, int y1, int z1) => new([x0, y0, z0, x1, y1, z1]);

    static void Java120(string temp)
    {
        Console.WriteLine("== Java 1.20: a start in structures.starts, its loot, a lone treasure chest");
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) { Console.WriteLine("   (not found: skipped)"); return; }
        string world;
        using (var w = WorldLoader.Open(java))
        {
            var target = TargetCatalog.All.Where(t => t.Kind == TargetKind.Java && t.DataVersion >= 3463 && t.DataVersion <= 3700).OrderByDescending(t => t.DataVersion).First();
            world = WorldConverter.Run(w, target, temp, new ConversionOptions { Dimensions = new HashSet<string> { "minecraft:overworld" } }, null).OpenPath ?? temp;
        }
        ChunkPos a, b;
        using (var w = WorldLoader.Open(world))
        {
            var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var chunks = dim.ChunkPositions.OrderBy(p => p.X).ThenBy(p => p.Z).ToList();
            a = chunks[chunks.Count / 2];
            b = chunks.First(p => Math.Abs(p.X - a.X) > 20 || Math.Abs(p.Z - a.Z) > 20);
            int ax = a.X * 16, az = a.Z * 16;
            var nbt = dim.LoadChunkNbt(a)!;
            var starts = new NbtCompound
            {
                ["minecraft:trial_chambers"] = new NbtCompound
                {
                    ["id"] = new NbtString("minecraft:trial_chambers"), ["ChunkX"] = new NbtInt(a.X), ["ChunkZ"] = new NbtInt(a.Z), ["references"] = new NbtInt(0),
                    ["Children"] = new NbtList(NbtType.Compound)
                    {
                        new NbtCompound { ["id"] = new NbtString("minecraft:jigsaw"), ["BB"] = BB(ax, -20, az, ax + 30, 0, az + 24) },
                        new NbtCompound { ["id"] = new NbtString("minecraft:jigsaw"), ["BB"] = BB(ax + 31, -20, az - 10, ax + 60, -5, az + 10) },
                    },
                },
                ["minecraft:village_plains"] = new NbtCompound { ["id"] = new NbtString("INVALID") },
            };
            nbt["structures"] = new NbtCompound { ["starts"] = starts, ["References"] = new NbtCompound() };
            var bes = nbt.GetList("block_entities") ?? new NbtList(NbtType.Compound);
            bes.Add(new NbtCompound { ["id"] = new NbtString("minecraft:vault"), ["x"] = new NbtInt(ax + 5), ["y"] = new NbtInt(-10), ["z"] = new NbtInt(az + 5) });
            bes.Add(new NbtCompound
            {
                ["id"] = new NbtString("minecraft:chest"), ["x"] = new NbtInt(ax + 40), ["y"] = new NbtInt(-8), ["z"] = new NbtInt(az + 2),
                ["LootTable"] = new NbtString("minecraft:chests/trial_chambers/corridor"), ["Items"] = new NbtList(NbtType.Compound),
            });
            nbt["block_entities"] = bes;
            dim.SaveChunkNbt(a, nbt);
            var other = dim.LoadChunkNbt(b)!;
            var obes = other.GetList("block_entities") ?? new NbtList(NbtType.Compound);
            obes.Add(new NbtCompound
            {
                ["id"] = new NbtString("minecraft:chest"), ["x"] = new NbtInt(b.X * 16 + 9), ["y"] = new NbtInt(58), ["z"] = new NbtInt(b.Z * 16 + 9),
                ["LootTable"] = new NbtString("minecraft:chests/buried_treasure"), ["LootTableSeed"] = new NbtLong(123), ["Items"] = new NbtList(NbtType.Compound),
            });
            other["block_entities"] = obes;
            dim.SaveChunkNbt(b, other);
        }
        using (var w = WorldLoader.Open(world))
        {
            var sites = StructureMap.Find(w, w.Dimensions).Sites;
            var chambers = sites.Where(s => s.Key == "trial_chambers").ToList();
            int ax = a.X * 16, az = a.Z * 16;
            Program.Check(chambers.Count == 1 && chambers[0].Saved && chambers[0].Box == (ax, -20, az - 10, ax + 60, 0, az + 24),
                $"the trial chambers start is found, its box from its pieces ({string.Join("; ", chambers.Select(s => s.Box.ToString()))})");
            Program.Check(chambers.Count == 1 && chambers[0].Unlooted == 1 && chambers[0].Notes.Contains("a vault"),
                $"its vault and unopened chest count for it, not as structures of their own ({string.Join("; ", chambers.SelectMany(s => s.Notes))}, {chambers.FirstOrDefault()?.Unlooted} unlooted)");
            Program.Check(!sites.Any(s => s.Key == "village" && s.Box.MinX >= ax && s.Box.MaxX <= ax + 15), "an empty start (INVALID) shows nothing");
            var treasure = sites.Where(s => s.Key == "buried_treasure" && s.X == b.X * 16 + 9 && s.Z == b.Z * 16 + 9).ToList();
            Program.Check(treasure.Count == 1 && !treasure[0].Saved && treasure[0].Unlooted == 1, "a lone unopened treasure chest is buried treasure, still to be dug up");
        }
    }

    static void OldJava(string temp)
    {
        Console.WriteLine("== Java before 1.13: data/Village.dat and Temple.dat");
        string source = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2");
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        CopyDir(source, temp);
        Directory.CreateDirectory(Path.Combine(temp, "data"));
        NbtCompound Feature(string id, int cx, int cz, NbtIntArray bb, string? firstPiece = null)
        {
            var children = new NbtList(NbtType.Compound);
            if (firstPiece != null) children.Add(new NbtCompound { ["id"] = new NbtString(firstPiece), ["BB"] = bb });
            return new NbtCompound { ["id"] = new NbtString(id), ["ChunkX"] = new NbtInt(cx), ["ChunkZ"] = new NbtInt(cz), ["BB"] = bb, ["Children"] = children };
        }
        void Write(string file, params (string Key, NbtCompound Feature)[] features)
        {
            var all = new NbtCompound();
            foreach (var (key, f) in features) all[key] = f;
            var root = new NbtCompound { ["data"] = new NbtCompound { ["Features"] = all } };
            File.WriteAllBytes(Path.Combine(temp, "data", file), MinecraftStudio.Core.IO.Compression.Gzip(NbtBinary.Serialize(root, NbtFormat.Java)));
        }
        Write("Village.dat", ("[3,4]", Feature("Village", 3, 4, BB(40, 60, 56, 110, 80, 120))));
        // The game itself writes every temple as a "Temple", the kind in its first piece.
        Write("Temple.dat", ("[-7,9]", Feature("TeDP", -7, 9, BB(-112, 63, 144, -92, 78, 164))), ("[20,20]", Feature("Temple", 20, 20, BB(320, 64, 320, 326, 72, 328), "TeSH")));
        using var w = WorldLoader.Open(temp);
        var sites = StructureMap.Find(w, w.Dimensions).Sites;
        Program.Check(sites.Any(s => s.Key == "village" && s.Saved && s.Box == (40, 60, 56, 110, 80, 120)), "the village of Village.dat, with its box");
        Program.Check(sites.Any(s => s.Key == "desert_pyramid" && s.Name == "Desert temple" && s.Box == (-112, 63, 144, -92, 78, 164)), "the desert temple of Temple.dat (TeDP)");
        Program.Check(sites.Any(s => s.Key == "swamp_hut" && s.Box == (320, 64, 320, 326, 72, 328)), "a \"Temple\" whose first piece is TeSH is a witch hut");
    }

    static void BedrockVillage(string temp)
    {
        Console.WriteLine("== Bedrock: a village record");
        string source = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16");
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        CopyDir(source, temp);
        using (var w = (BedrockWorld)WorldLoader.Open(temp))
        {
            var info = new NbtCompound { ["X0"] = new NbtInt(-50), ["Y0"] = new NbtInt(60), ["Z0"] = new NbtInt(20), ["X1"] = new NbtInt(30), ["Y1"] = new NbtInt(90), ["Z1"] = new NbtInt(110), ["Tick"] = new NbtLong(5) };
            w.Db.Put(System.Text.Encoding.ASCII.GetBytes("VILLAGE_Overworld_0b6c1c7e-1f58-4a55-9d2e-6f0b2d3a9c11_INFO"), NbtBinary.SerializeAll([new NamedTag("", info)], NbtFormat.Bedrock));
            w.Save(MinecraftStudio.Core.LevelDb.CommitMode.AppendLog);
        }
        using (var w = WorldLoader.Open(temp))
        {
            var sites = StructureMap.Find(w, w.Dimensions).Sites;
            Program.Check(sites.Any(s => s.Key == "village" && s.Saved && s.Box == (-50, 60, 20, 30, 90, 110)), $"the village the game keeps track of, with its box ({Summary(sites.Where(s => s.Saved))})");
        }
    }
}
