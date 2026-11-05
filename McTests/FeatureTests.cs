// McTests/FeatureTests.cs: "features [write]" — the table of what works where says what the code does, and FEATURES.md is
// that table.
// Owns: FeatureTests — every feature has a cell for every platform and a unique id; a sample world of each edition (Java,
// Bedrock, an Xbox 360 save, the 3DS, Pocket 0.x, a Classic and an Indev level) is opened and the table is held against
// the code's own answers: one-file sharing, Hardcore, saving players, slime chunks, map art, repair, chunk NBT, level.dat,
// the database, and the copy edits and placement that refuse the 3DS and fixed-size levels before writing anything.
// FEATURES.md must equal PlatformSupport.Markdown(); "features write" writes it.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class FeatureTests
{
    static readonly (string Label, string Path)[] Samples =
    [
        ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
        ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
        ("3DS 1.9", Path.Combine(TestPaths.Data, "3ds", "1.9")),
        ("Pocket 0.x", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")),
        ("Classic", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "Dojo_64_64_128.dat")),
        ("Indev", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "hell.mclevel")),
    ];

    public static void Run(string[] args)
    {
        string doc = Path.Combine(TestPaths.Repo, "FEATURES.md");
        if (args.FirstOrDefault() == "write")
        {
            File.WriteAllText(doc, PlatformSupport.Markdown());
            Program.Info($"wrote {doc}");
            // The README's compatibility grid, between its markers.
            string readme = Path.Combine(TestPaths.Repo, "README.md");
            if (File.Exists(readme))
            {
                string text = File.ReadAllText(readme);
                const string Start = "<!-- compatibility:start -->", End = "<!-- compatibility:end -->";
                int a = text.IndexOf(Start, StringComparison.Ordinal), b = text.IndexOf(End, StringComparison.Ordinal);
                if (a >= 0 && b > a)
                {
                    File.WriteAllText(readme, text[..(a + Start.Length)] + PlatformSupport.MarkdownGrid().Replace("\n", "\r\n") + "\r\n" + text[b..]);
                    Program.Info($"wrote the grid into {readme}");
                }
            }
        }
        var all = PlatformSupport.All;
        Program.Check(all.Select(f => f.Id).Distinct().Count() == all.Count, $"every feature has its own id ({all.Count} features)");
        var missing = all.SelectMany(f => Enum.GetValues<Platform>().Where(p => !f.Cells.ContainsKey(p)).Select(p => $"{f.Id}/{p}")).ToList();
        Program.Check(missing.Count == 0, $"every feature says how it works on every platform{(missing.Count > 0 ? ": missing " + string.Join(", ", missing.Take(8)) : "")}");

        foreach (var (label, path) in Samples)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            using var w = WorldLoader.Open(path);
            var p = PlatformSupport.Of(w);
            if (p is not Platform platform) { Program.Check(false, $"{label}: has a platform"); continue; }
            bool On(string id) => PlatformSupport.Get(id)[platform].Support != Support.No;
            var wrong = new List<string>();
            void Same(string id, bool code) { if (On(id) != code) wrong.Add($"{id} (the table says {(On(id) ? "yes" : "no")}, the code {(code ? "yes" : "no")})"); }
            bool indev = w is FiniteWorld { Indev: not null };
            Same("share", WorldShare.CanPack(w));
            Same("hardcore", HardcoreRestore.HasHardcore(w));
            Same("slime", SlimeChunks.For(w) != null);
            Same("mapart", MapArt.CannotAdd(w) == null);
            Same("database", w is BedrockWorld);
            Same("chunknbt", w.Dimensions.Any(d => d.CanSaveChunkNbt) || w.Dimensions.Any(d => d.ChunkPositions.FirstOrDefault() is var c && d.LoadChunkNbt(c) != null));
            // A fixed-size level is Classic or Indev in one column: Indev has the players and level.dat Classic lacks.
            // Players are shown where they are read (the 3DS's only to look at) and saved where they can be.
            if (platform != Platform.Classic) { Same("players", PlayerStore.CanSave(w) || w.Players.Count > 0); Same("leveldat", w.LevelData != null || w is PocketLegacyWorld); }
            else if (PlayerStore.CanSave(w) != indev || (w.LevelData != null) != indev) wrong.Add("players/level.dat: only Indev levels have them");
            if (PlatformSupport.Get("rescue")[platform].Support == Support.Yes != WorldRepair.CanRepair(w)) wrong.Add("rescue (a repaired copy)");
            // The copy edits and placement refuse before writing anything where the table says no, each by its own way in.
            string never = Path.Combine(TestPaths.Output, "features-never");
            var ow = w.Dimensions.FirstOrDefault(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld") ?? w.Dimensions[0];
            var machine = new LagMachine(LagKind.DroppedItems, ow.Id, "", 8, 64, 8, (0, 64, 0, 15, 64, 15), 0, []);
            var ways = new (string Id, Action Run)[]
            {
                ("shape", () => LandShape.ShapeIntoCopy(w, "minecraft:overworld", 0, 0, 15, 15, new LandShapeOptions(LandShapeKind.Raise, 1), never)),
                ("biome", () => BiomePaint.PaintIntoCopy(w, "minecraft:overworld", 0, 0, 0, 15, 15, 15, "minecraft:plains", never)),
                ("lagclean", () => LagMachines.CleanIntoCopy(w, [machine], never)),
                ("clearitems", () => LagMachines.CleanIntoCopy(w, [machine], never)),
                ("petshome", () => PetsHome.BringIntoCopy(w, ow, [new PetMove("wolf", null, "", Guid.NewGuid(), new ChunkPos(0, 0), 8, 64, 8)], never)),
            };
            foreach (var (id, run) in ways)
                if (!On(id) && !Refused(run)) wrong.Add(id + " (not refused)");
            if (!On("place") && !Refused(() => StructurePlacement.PlaceIntoCopy(w, new Structure(1, 1, 1), "minecraft:overworld", 0, 0, 0, false, Path.Combine(TestPaths.Output, "features-never"))))
                wrong.Add("place (not refused)");
            Program.Check(wrong.Count == 0, $"{label} ({PlatformSupport.ShortName(platform)}): the table says what the code does{(wrong.Count > 0 ? ": " + string.Join("; ", wrong) : "")}");
        }
        Program.Check(!Directory.Exists(Path.Combine(TestPaths.Output, "features-never")), "nothing was written by the refusals");

        // One sentence per feature, every platform named once.
        var sentence = PlatformSupport.WorksOn(PlatformSupport.Get("place"));
        Program.Check(sentence.StartsWith("Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (", StringComparison.Ordinal), $"\"works on\" in words: {sentence}");
        string want = PlatformSupport.Markdown();
        bool same = File.Exists(doc) && File.ReadAllText(doc) == want;
        Program.Check(same, same ? "FEATURES.md is the table" : "FEATURES.md is not the table: run McTests features write");
    }

    static bool Refused(Action a)
    {
        try { a(); return false; }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or IOException) { return true; }
    }
}
