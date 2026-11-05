// McTests/SchematicTests.cs: "schematics" — every structure file format, for more than one game version.
// Owns: SchematicTests — Litematica's packed bits (values across two longs); one structure (stairs, a fence, a chest
// with diamonds and a shulker box holding an elytra, a sign with words, a bed, an armour stand with a name) saved as a
// Java structure .nbt (newest and 1.16), WorldEdit .schem (written as version 2; also read back as version 3), Litematica
// (newest and 1.20.4) and MCEdit .schematic (1.12), each read back and compared; the MCEdit schematics in TestData read;
// and one schematic placed into a copy of a world and found there with Find. Output goes to the test folder only.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class SchematicTests
{
    public static void Run(string[] args)
    {
        string dir = Path.Combine(TestPaths.Output, "schematics", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(dir);
        Console.WriteLine("== Litematica's packed bits");
        Bits();
        Console.WriteLine("== One structure through every format");
        var s = Sample();
        foreach (var (file, dv, label) in new[]
        {
            ("newest.nbt", 0, "Java structure .nbt (newest)"),
            ("for-1.16.nbt", 2586, "Java structure .nbt for 1.16.5"),
            ("newest.schem", 0, "WorldEdit .schem (newest)"),
            ("for-1.13.schem", 1631, "WorldEdit .schem for 1.13.2"),
            ("newest.litematic", 0, "Litematica (newest)"),
            ("for-1.20.4.litematic", 3700, "Litematica for 1.20.4"),
            ("old.schematic", 0, "MCEdit .schematic (1.12)"),
        })
            RoundTrip(s, Path.Combine(dir, file), dv, label);
        SpongeVersion3(s, dir);
        Console.WriteLine("== MCEdit schematics from TestData");
        foreach (var f in Directory.Exists(Path.Combine(TestPaths.Data, "pymclevel", "testfiles"))
            ? Directory.EnumerateFiles(Path.Combine(TestPaths.Data, "pymclevel", "testfiles"), "*.schematic") : [])
            Old(f);
        Console.WriteLine("== A schematic placed into a copy of a world, then found there");
        Placed(s, dir);
    }

    static void Bits()
    {
        var rnd = new Random(5);
        bool ok = true;
        foreach (int bits in new[] { 2, 3, 4, 5, 7, 9, 12, 13, 17 })
        {
            int n = 1000;
            var values = Enumerable.Range(0, n).Select(_ => rnd.Next(1 << bits)).ToArray();
            var data = new long[(n * (long)bits + 63) / 64];
            for (int i = 0; i < n; i++) LitematicSchematic.Set(data, i, bits, values[i]);
            for (int i = 0; i < n; i++) ok &= LitematicSchematic.Get(data, i, bits) == values[i];
            // Writing one value again leaves its neighbours as they were.
            LitematicSchematic.Set(data, 37, bits, 0);
            ok &= LitematicSchematic.Get(data, 36, bits) == values[36] && LitematicSchematic.Get(data, 38, bits) == values[38] && LitematicSchematic.Get(data, 37, bits) == 0;
        }
        Program.Check(ok, "values of 2 to 17 bits read back, also those running across two longs");
        Program.Check(LitematicSchematic.Bits(1) == 2 && LitematicSchematic.Bits(4) == 2 && LitematicSchematic.Bits(5) == 3 && LitematicSchematic.Bits(17) == 5, "bits per block: at least 2, enough for the palette");
    }

    static BlockState B(string name, string props = "") => BlockState.Get(name, props);

    static Structure Sample()
    {
        var s = new Structure(5, 3, 4) { Author = "tests" };
        for (int z = 0; z < 4; z++) for (int x = 0; x < 5; x++) s.Set(x, 0, z, B("minecraft:stone"));
        s.Set(0, 1, 0, B("minecraft:oak_stairs", "facing=east,half=bottom,shape=straight,waterlogged=false"));
        s.Set(4, 1, 3, B("minecraft:oak_fence", "east=false,north=false,south=false,waterlogged=false,west=false"));
        s.Set(2, 2, 2, B("minecraft:glass"));
        s.Set(1, 1, 1, B("minecraft:chest", "facing=north,type=single,waterlogged=false"));
        var shulker = new NbtCompound
        {
            ["minecraft:container"] = new NbtList(NbtType.Compound)
            {
                new NbtCompound { ["slot"] = new NbtInt(0), ["item"] = new NbtCompound { ["id"] = new NbtString("minecraft:elytra"), ["count"] = new NbtInt(1) } },
            },
        };
        s.BlockEntities[s.Index(1, 1, 1)] = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:chest"),
            ["Items"] = new NbtList(NbtType.Compound)
            {
                new NbtCompound
                {
                    ["Slot"] = new NbtByte(0), ["id"] = new NbtString("minecraft:diamond"), ["count"] = new NbtInt(5),
                    // Lore mixing a plain line and a formatted one: from 1.21.5 such a list needs its strings wrapped.
                    ["components"] = new NbtCompound
                    {
                        ["minecraft:lore"] = new NbtList(NbtType.String) { new NbtString("\"Plain line\""), new NbtString("{\"text\":\"Red line\",\"color\":\"red\"}") },
                    },
                },
                new NbtCompound { ["Slot"] = new NbtByte(1), ["id"] = new NbtString("minecraft:shulker_box"), ["count"] = new NbtInt(1), ["components"] = shulker },
            },
        };
        s.Set(3, 1, 1, B("minecraft:oak_sign", "rotation=0,waterlogged=false"));
        NbtList Lines(string first) => new(NbtType.String) { new NbtString("{\"text\":\"" + first + "\"}"), new NbtString("\"\""), new NbtString("\"\""), new NbtString("\"\"") };
        s.BlockEntities[s.Index(3, 1, 1)] = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:sign"),
            ["front_text"] = new NbtCompound { ["messages"] = Lines("Find me 42"), ["color"] = new NbtString("black"), ["has_glowing_text"] = new NbtByte(0) },
            ["back_text"] = new NbtCompound { ["messages"] = Lines(""), ["color"] = new NbtString("black"), ["has_glowing_text"] = new NbtByte(0) },
            ["is_waxed"] = new NbtByte(0),
        };
        s.Set(1, 1, 3, B("minecraft:blue_bed", "facing=east,occupied=false,part=foot"));
        s.Set(2, 1, 3, B("minecraft:blue_bed", "facing=east,occupied=false,part=head"));
        s.BlockEntities[s.Index(1, 1, 3)] = new NbtCompound { ["id"] = new NbtString("minecraft:bed") };
        s.BlockEntities[s.Index(2, 1, 3)] = new NbtCompound { ["id"] = new NbtString("minecraft:bed") };
        s.Entities.Add(new StructureEntity(4.5, 1, 0.5, new NbtCompound
        {
            ["id"] = new NbtString("minecraft:armor_stand"),
            ["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(4.5), new NbtDouble(1), new NbtDouble(0.5) },
            ["CustomName"] = new NbtString("{\"text\":\"Smoke stand\"}"),
        }));
        return s;
    }

    static bool HasItem(NbtCompound? be, string id, int count) =>
        be?.GetList("Items")?.OfType<NbtCompound>().Any(i => i.GetString("id") == id && Items.Count(i) == count) == true;

    static bool HasNested(NbtCompound? be, string id) =>
        be?.GetList("Items")?.OfType<NbtCompound>().Any(i => Items.Components(i)?.GetList("minecraft:container")?.OfType<NbtCompound>()
            .Any(e => e.GetCompound("item")?.GetString("id") == id) == true) == true;

    static string SignText(NbtCompound? be) => be?.GetCompound("front_text")?.GetList("messages") is { } m ? string.Join("|", m.Select(t => TextComponents.ToPlain(t is NbtString str ? str.Value : t.ToString()))) : "";

    static void RoundTrip(Structure s, string path, int dv, string label)
    {
        Structure back;
        try
        {
            StructureFile.Save(s, path, dv);
            back = StructureFile.Load(path);
        }
        catch (Exception e) { Program.Check(false, $"{label}: saving and loading threw {e.GetType().Name}: {e.Message}"); return; }
        bool numbered = StructureFile.IsNumbered(StructureFile.FormatOf(path));
        Program.Check(back.SizeX == s.SizeX && back.SizeY == s.SizeY && back.SizeZ == s.SizeZ, $"{label}: the size is kept ({back.SizeX} × {back.SizeY} × {back.SizeZ})");
        int same = 0, total = 0;
        var wrong = new List<string>();
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    total++;
                    var a = s.Get(x, y, z);
                    var b = back.Get(x, y, z);
                    // Numbered blocks keep the block and its facing; links (fences, stair corners) are worked out again.
                    bool eq = numbered ? a.BaseName == b.BaseName && a.GetProperty("facing") == b.GetProperty("facing") && a.GetProperty("part") == b.GetProperty("part") : a == b;
                    if (eq) same++;
                    else if (wrong.Count < 4) wrong.Add($"{a} → {b}");
                }
        Program.Check(same == total, $"{label}: every block is the same ({same} of {total}{(wrong.Count > 0 ? "; " + string.Join("; ", wrong) : "")})");
        var chest = back.BlockEntities.GetValueOrDefault(back.Index(1, 1, 1));
        Program.Check(HasItem(chest, "minecraft:diamond", 5), $"{label}: the chest still has its 5 diamonds");
        if (!numbered)
        {
            var lore = chest?.GetList("Items")?.OfType<NbtCompound>().FirstOrDefault(i => i.GetString("id") == "minecraft:diamond") is { } d && Items.Components(d)?.GetList("minecraft:lore") is { } l
                ? l.Select(t => TextComponents.ToPlain(t is NbtString str ? str.Value : t.ToString())).ToList() : [];
            Program.Check(lore.Count == 2 && lore[0] == "Plain line" && lore[1] == "Red line", $"{label}: the diamonds' lore lines are kept ({string.Join(" | ", lore)})");
        }
        Program.Check(HasNested(chest, "minecraft:elytra"), $"{label}: the shulker box in the chest still holds the elytra");
        var sign = back.BlockEntities.GetValueOrDefault(back.Index(3, 1, 1));
        Program.Check(SignText(sign).Contains("Find me 42"), $"{label}: the sign's words are kept ({SignText(sign)})");
        Program.Check(back.Entities.Count == 1 && back.Entities[0].Nbt.GetString("id") == "minecraft:armor_stand" && Math.Abs(back.Entities[0].X - 4.5) < 1e-6
            && TextComponents.ToPlain(back.Entities[0].Nbt.GetString("CustomName") ?? "").Contains("Smoke stand"),
            $"{label}: the named armour stand is kept where it stood ({string.Join(", ", back.Entities.Select(e => $"{e.Nbt.GetString("id")} at {e.X}, {e.Y}, {e.Z}"))})");
        if (dv > 0 && !numbered)
            Program.Check(FileVersion(path) == dv, $"{label}: the file says it is for data version {dv} ({FileVersion(path)})");
    }

    static int FileVersion(string path)
    {
        var root = StructureFile.ReadRoot(path);
        return root.GetInt("DataVersion", root.GetInt("MinecraftDataVersion"));
    }

    // Version 3 (WorldEdit 7.3 writes it): the same contents, arranged the newer way; it must read the same.
    static void SpongeVersion3(Structure s, string dir)
    {
        var v2 = SpongeSchematic.ToNbt(s);
        var blocks = new NbtCompound
        {
            ["Palette"] = v2["Palette"]!.Clone(),
            ["Data"] = v2["BlockData"]!.Clone(),
            ["BlockEntities"] = Wrap(v2.GetList("BlockEntities")!, "Pos", "Id"),
        };
        var inner = new NbtCompound
        {
            ["Version"] = new NbtInt(3), ["DataVersion"] = v2["DataVersion"]!.Clone(), ["Width"] = v2["Width"]!.Clone(), ["Height"] = v2["Height"]!.Clone(),
            ["Length"] = v2["Length"]!.Clone(), ["Blocks"] = blocks, ["Entities"] = Wrap(v2.GetList("Entities")!, "Pos", "Id"),
        };
        string path = Path.Combine(dir, "version3.schem");
        new NbtFile(new NbtCompound { ["Schematic"] = inner }, NbtFormat.Java, MinecraftStudio.Core.IO.CompressionKind.Gzip).Save(path);
        var back = StructureFile.Load(path);
        int same = 0;
        for (int y = 0; y < s.SizeY; y++) for (int z = 0; z < s.SizeZ; z++) for (int x = 0; x < s.SizeX; x++) if (s.Get(x, y, z) == back.Get(x, y, z)) same++;
        Program.Check(same == s.Volume && HasItem(back.BlockEntities.GetValueOrDefault(back.Index(1, 1, 1)), "minecraft:diamond", 5) && back.Entities.Count == 1,
            $"WorldEdit .schem version 3: blocks ({same} of {s.Volume}), the chest and the armour stand read");
    }

    static NbtList Wrap(NbtList list, params string[] outside)
    {
        var result = new NbtList(NbtType.Compound);
        foreach (var c in list.OfType<NbtCompound>())
        {
            var data = (NbtCompound)c.Clone();
            var w = new NbtCompound();
            foreach (var k in outside) { if (data[k] is NbtTag t) w[k] = t.Clone(); data.Remove(k); }
            w["Data"] = data;
            result.Add(w);
        }
        return result;
    }

    static void Old(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            var root = StructureFile.ReadRoot(path);
            if (!McEditSchematic.Looks(root)) { Program.Info($"{name}: not a schematic inside (skipped)"); return; }
            var s = StructureFile.Load(path);
            long solid = 0, unknown = 0;
            var kinds = new Dictionary<string, long>();
            for (int y = 0; y < s.SizeY; y++)
                for (int z = 0; z < s.SizeZ; z++)
                    for (int x = 0; x < s.SizeX; x++)
                    {
                        var b = s.Get(x, y, z);
                        if (b.IsAir) continue;
                        solid++;
                        if (b.BaseName.StartsWith("legacy_block_")) unknown++;
                        kinds[b.BaseName] = kinds.GetValueOrDefault(b.BaseName) + 1;
                    }
            bool sized = s.SizeX == (root.GetInt("Width") & 0xFFFF) && s.SizeY == (root.GetInt("Height") & 0xFFFF) && s.SizeZ == (root.GetInt("Length") & 0xFFFF);
            Program.Check(sized && solid > 0 && unknown == 0, $"{name}: {s.SizeX} × {s.SizeY} × {s.SizeZ}, {solid:N0} blocks of {kinds.Count} kinds, {unknown} unknown, {s.BlockEntities.Count} block entities, {s.Entities.Count} entities");
            Program.Info($"{name}: most: {string.Join(", ", kinds.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value:N0}"))}");
            // The Place dialog's pictures of it, written to look at.
            string pictures = Path.Combine(TestPaths.Output, "schematics", "previews");
            Directory.CreateDirectory(pictures);
            var top = StructurePreview.Top(s);
            var front = StructurePreview.Front(s);
            File.WriteAllBytes(Path.Combine(pictures, Path.GetFileNameWithoutExtension(name) + "-top.png"), MinecraftStudio.Core.Imaging.Png.Encode(top));
            File.WriteAllBytes(Path.Combine(pictures, Path.GetFileNameWithoutExtension(name) + "-front.png"), MinecraftStudio.Core.Imaging.Png.Encode(front));
            Program.Check(top.Width == s.SizeX && top.Height == s.SizeZ && front.Height == s.SizeY && top.Pixels.Any(p => p >> 24 != 0), $"{name}: previews drawn ({top.Width} × {top.Height} from above, {front.Width} × {front.Height} from the south)");
            // Saved again for 1.12, it reads back the same.
            string again = Path.Combine(TestPaths.Output, "schematics", "again-" + name);
            McEditSchematic.Save(s, again);
            var back = StructureFile.Load(again);
            long same = 0;
            for (int y = 0; y < s.SizeY; y++) for (int z = 0; z < s.SizeZ; z++) for (int x = 0; x < s.SizeX; x++) if (s.Get(x, y, z).BaseName == back.Get(x, y, z).BaseName) same++;
            Program.Check(same == s.Volume, $"{name}: saved again as a .schematic and read back: {same:N0} of {s.Volume:N0} blocks the same");
        }
        catch (Exception e) { Program.Check(false, $"{name}: threw {e.GetType().Name}: {e.Message}"); }
    }

    static void Placed(Structure s, string dir)
    {
        string world = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_13");
        if (!Directory.Exists(world)) { Program.Info("(skipped: no Java 1.13 world in TestData)"); return; }
        string schem = Path.Combine(dir, "to-place.schem");
        SpongeSchematic.Save(s, schem);
        var loaded = StructureFile.Load(schem);
        string outDir = Path.Combine(dir, "placed");
        int ox, oy, oz;
        PlacementResult r;
        using (var w = WorldLoader.Open(world))
        {
            var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var info = WorldInfoReader.Read(w);
            var stored = dim.ChunkPositions.ToHashSet();
            var spawn = new ChunkPos(info.SpawnX >> 4, info.SpawnZ >> 4);
            var chunk = stored.Contains(spawn) ? spawn : stored.OrderBy(p => Math.Abs((long)p.X - spawn.X) + Math.Abs((long)p.Z - spawn.Z)).First();
            (ox, oy, oz) = (chunk.X * 16 + 2, Math.Clamp(info.SpawnY is > 0 and < 250 ? info.SpawnY : 64, 1, 250), chunk.Z * 16 + 2);
            r = StructurePlacement.PlaceIntoCopy(w, loaded, "minecraft:overworld", ox, oy, oz, pasteAir: true, outDir);
        }
        Program.Check(r.Verified, $"placed into a copy of Java 1.13 and checked ({string.Join("; ", r.Problems)})");
        using var copy = WorldLoader.Open(r.OpenPath);
        var over = copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
        var found = WorldSearch.Run(copy, over, new SearchOptions { Query = "diamond", Chunks = [new ChunkPos(ox >> 4, oz >> 4), new ChunkPos((ox + 4) >> 4, (oz + 3) >> 4)] });
        Program.Check(found.Hits.Any(h => h.Kind == SearchKind.Item && h.What == "diamond" && h.Count == 5 && (h.X, h.Y, h.Z) == (ox + 1, oy + 1, oz + 1)),
            $"Find shows the 5 diamonds in the placed chest at {ox + 1}, {oy + 1}, {oz + 1} ({string.Join("; ", found.Hits.Where(h => h.Kind == SearchKind.Item).Select(h => $"{h.Count} {h.What} at {h.X}, {h.Y}, {h.Z} in {h.Where}"))})");
        var words = WorldSearch.Run(copy, over, new SearchOptions { Query = "Find me 42", Blocks = false, Items = false, Entities = false, Players = false, Chunks = [new ChunkPos((ox + 3) >> 4, (oz + 1) >> 4)] });
        Program.Check(words.Hits.Any(h => (h.X, h.Y, h.Z) == (ox + 3, oy + 1, oz + 1)), $"the sign's words are in the world at {ox + 3}, {oy + 1}, {oz + 1}");
    }
}
