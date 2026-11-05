// McTests/StructureTests.cs: structure: the building mover. Round-trips structures through .nbt and .mcstructure,
// crops one out of every kind of world, and places structures into copies of every kind of world through the real
// placement path — checking the blocks landed, the copy is the same kind of world, and the original is untouched.
// Owns: StructureTests.
using System.IO.Compression;
using System.Security.Cryptography;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

/// <summary>
/// structure: the crop-a-building / move-a-building feature, on every platform. Placement copies the world and rewrites
/// only the chunks the structure touches; each case reopens the copy and also hashes the original to prove it is
/// byte-for-byte unchanged.
/// </summary>
static class StructureTests
{
    static readonly (string Label, string Path)[] Worlds =
    [
        ("Java 1.13", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_13")),
        ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
        ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
        ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
        ("PE 0.x", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")),
        ("Indev", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "hell.mclevel")),
        ("Classic", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "Dojo_64_64_128.dat")),
        ("3DS 1.9", Path.Combine(TestPaths.Data, "3ds", "1.9")),
    ];

    public static void Run(string[] args)
    {
        var only = args.Length > 0 ? Worlds.Where(w => args.Any(a => w.Label.Contains(a, StringComparison.OrdinalIgnoreCase))).ToArray() : Worlds;
        if (args.Length == 0) { Synthetic(); McStructureRoundTrip(); Hostile(); Ground(); Checker(); }
        Console.WriteLine("== Crop a structure out of every kind of world");
        foreach (var (label, path) in only) CropFrom(label, path);
        Console.WriteLine("== Place a structure into a copy of every kind of world");
        foreach (var (label, path) in only) PlaceInto(label, path, Cube());
        if (args.Length == 0)
        {
            Console.WriteLine("== A chest keeps its items");
            ChestInto("Java 1.12.2", Worlds[1].Path);
            ChestInto("Bedrock 1.18", Worlds[2].Path);
        }
    }

    static BlockState Stone => BlockState.Get("minecraft:stone");
    static BlockState Glass => BlockState.Get("minecraft:glass");

    /// <summary>A 3×3×3 glass shell with a stone core: solid and see-through blocks that every edition has.</summary>
    static Structure Cube()
    {
        var cube = new Structure(3, 3, 3);
        for (int y = 0; y < 3; y++) for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) cube.Set(x, y, z, Glass);
        cube.Set(1, 1, 1, Stone);
        return cube;
    }

    static void Synthetic()
    {
        Console.WriteLine("== Structure .nbt round trip");
        var s = new Structure(4, 3, 5) { DataVersion = 3700, Author = "test" };
        var rnd = new Random(7);
        var states = new[] { BlockState.Air, Stone, Glass, BlockState.Get("minecraft:oak_log", "axis=y"), BlockState.Get("minecraft:chest", "facing=north,type=single,waterlogged=false") };
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                    s.Set(x, y, z, states[rnd.Next(states.Length)]);
        s.BlockEntities[s.Index(1, 0, 2)] = Chest(3);
        s.BlockEntities[s.Index(0, 1, 0)] = new NbtCompound { ["id"] = new NbtString("minecraft:sign"), ["Text1"] = new NbtString("hi") };
        s.Entities.Add(new StructureEntity(1.5, 0.0, 2.5, new NbtCompound { ["id"] = new NbtString("minecraft:armor_stand") }));

        var back = StructureNbt.FromNbt(StructureNbt.ToNbt(s));
        Program.Check(SameBlocks(s, back), "blocks survive ToNbt/FromNbt");
        Program.Check(back.BlockEntities.Count == 2 && back.BlockEntities[back.Index(1, 0, 2)].GetList("Items")?.Count == 1, "block entities survive ToNbt/FromNbt");
        Program.Check(back.Entities.Count == 1 && back.Entities[0].Nbt.GetString("id") == "minecraft:armor_stand", "entities survive ToNbt/FromNbt");
        Program.Check(back.DataVersion == 3700, "DataVersion survives");

        string file = Path.Combine(TestPaths.Output, "structure", "synthetic.nbt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        StructureNbt.Save(s, file);
        var loaded = StructureNbt.Load(file);
        Program.Check(SameBlocks(s, loaded) && loaded.BlockEntities.Count == 2 && loaded.Entities.Count == 1, "blocks, block entities and entities survive Save/Load to a .nbt file");
        Program.Check(new Structure(1, 1, 1).SizeX == 1, "a 1×1×1 structure is allowed");
        Program.Check(Throws(() => new Structure(0, 1, 1)), "a zero-size structure is refused");
    }

    // BuildCheck (the place window's "Check the build"): a box cropped out of a world checks as fully built where it came
    // from; set one cell away it does not; against a copy whose ores became stone, exactly those blocks are wrong and the
    // ores are what is still to gather; a stair turned the other way is "set wrong", a lit furnace still right.
    static void Checker()
    {
        Console.WriteLine("== Checking a build against its structure");
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) { Program.Info("(skipped: the Java 1.16.5 sample is not in TestData)"); return; }
        const string Ow = "minecraft:overworld";
        string replaced = Path.Combine(TestPaths.Output, "structure", "checker-replaced");
        if (Directory.Exists(replaced)) Directory.Delete(replaced, true);
        try
        {
            Structure cropped;
            int ox, oz, low;
            using (var w = WorldLoader.Open(java))
            {
                var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == Ow);
                var c = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).First();
                (ox, oz) = (c.X * 16, c.Z * 16);
                (low, _) = StructurePlacement.Height(w, Ow);
                cropped = StructureClip.Extract(w, dim, ox, low, oz, ox + 31, low + 80, oz + 31);
                var same = BuildCheck.Run(w, Ow, cropped, ox, low, oz, airCounts: true);
                Program.Check(same.Checked > 50_000 && same.Right == same.Checked && same.StillNeeded.Count == 0, $"a box checked where it was cropped from: {same.Done:P0} built right ({same.Right:N0} of {same.Checked:N0})");
                var moved = BuildCheck.Run(w, Ow, cropped, ox + 1, low, oz, airCounts: true);
                Program.Check(moved.Wrong > 1000 && moved.Done < 0.99, $"one block to the east it is not ({moved.Wrong:N0} cells wrong)");
                BlockReplace.ReplaceIntoCopy(w, Ow, ox, low, oz, ox + 31, low + 80, oz + 31, [new("*_ore", "stone")], replaced);
            }
            using (var w = WorldLoader.Open(replaced))
            {
                var r = BuildCheck.Run(w, Ow, cropped, ox, low, oz, airCounts: true);
                int ores = 0;
                for (int y = 0; y < cropped.SizeY; y++) for (int z = 0; z < cropped.SizeZ; z++) for (int x = 0; x < cropped.SizeX; x++) if (cropped.Get(x, y, z).BaseName.EndsWith("_ore", StringComparison.Ordinal)) ores++;
                bool exact = r.WrongBlock == ores && r.Missing == 0 && r.Extra == 0 && r.Problems.All(p => p.Want.BaseName.EndsWith("_ore", StringComparison.Ordinal) && p.Have.BaseName == "stone");
                bool gather = r.StillNeeded.Count > 0 && r.StillNeeded.All(l => l.Item.EndsWith("_ore", StringComparison.Ordinal)) && r.StillNeeded.Sum(l => l.Count) == ores;
                Program.Check(ores > 0 && exact && gather, $"against a copy whose ores became stone: the {ores:N0} ores are the wrong blocks, and what is still to gather ({string.Join(", ", r.StillNeeded.Take(4).Select(l => $"{l.Count} {l.Item}"))})");
            }
            // States: a stair turned the other way is set wrong; a lit furnace (it changes by itself) is right.
            var want = new Structure(2, 1, 1);
            want.Set(0, 0, 0, BlockState.Get("minecraft:oak_stairs", "facing=north,half=bottom,shape=straight,waterlogged=false"));
            want.Set(1, 0, 0, BlockState.Get("minecraft:furnace", "facing=north,lit=false"));
            var built = new Structure(2, 1, 1);
            built.Set(0, 0, 0, BlockState.Get("minecraft:oak_stairs", "facing=south,half=bottom,shape=straight,waterlogged=false"));
            built.Set(1, 0, 0, BlockState.Get("minecraft:furnace", "facing=north,lit=true"));
            string states = Path.Combine(TestPaths.Output, "structure", "checker-states");
            if (Directory.Exists(states)) Directory.Delete(states, true);
            using (var w = WorldLoader.Open(java))
            {
                var placed = StructurePlacement.PlaceIntoCopy(w, built, Ow, ox + 4, low + 100, oz + 4, pasteAir: true, states);
                using var copy = WorldLoader.Open(placed.OpenPath);
                var r = BuildCheck.Run(copy, Ow, want, ox + 4, low + 100, oz + 4, airCounts: true);
                Program.Check(r.WrongState == 1 && r.Right == 1 && r.Problems.Single().Want.BaseName == "oak_stairs", $"a stair turned the other way is set wrong, a lit furnace still right ({r.Right} right, {r.WrongState} set wrong)");
            }
            Directory.Delete(states, true);
        }
        catch (Exception ex) { Program.Check(false, $"the build check threw {ex.GetType().Name}: {ex.Message}"); }
        finally { try { if (Directory.Exists(replaced)) Directory.Delete(replaced, true); } catch (IOException) { } }
    }

    // StructureGround: the height a structure is put at to stand on the ground, from the top blocks under it (as a map draws them).
    static void Ground()
    {
        Console.WriteLine("== Standing a structure on the ground (the place window's map)");
        var grass = BlockState.Get("minecraft:grass_block", "snowy=false");
        var leaves = BlockState.Get("minecraft:oak_leaves", "distance=1,persistent=false,waterlogged=false");
        int? On(bool bedrock, params (int Y, BlockState? Top)[] columns) => StructureGround.GroundY(columns, bedrock);
        Program.Check(On(false, (64, grass), (64, grass), (65, grass)) == 64, "plain ground: its middle height");
        Program.Check(On(false, (64, grass), (65, BlockState.Get("minecraft:short_grass")), (66, BlockState.Get("minecraft:tall_grass", "half=upper")),
            (65, BlockState.Get("minecraft:poppy")), (75, leaves), (76, leaves), (80, BlockState.Get("minecraft:oak_log", "axis=y"))) == 64,
            "plants and flowers stand on the ground (a tall plant's top two above it), tree tops are left out");
        Program.Check(On(false, (75, leaves), (77, leaves), (76, leaves)) == 76, "under trees only: the trees' middle height");
        Program.Check(On(false, (64, grass), (64, grass), (64, grass), (30, Stone), (64, grass), (90, Stone), (64, grass)) == 64, "a hole or a tower does not move it (the middle height wins)");
        Program.Check(On(false, (70, BlockState.Get("minecraft:snow", "layers=1"))) == 69, "Java: snow is a layer lying on the ground");
        Program.Check(On(true, (70, BlockState.Get("minecraft:snow"))) == 70, "Bedrock: snow is the full snow block, the ground itself");
        Program.Check(On(true, (70, BlockState.Get("minecraft:snow_layer", "covered_bit=false,height=0"))) == 69, "Bedrock: a snow_layer lies on the ground");
        Program.Check(On(true, (66, BlockState.Get("minecraft:tall_grass", "upper_block_bit=true"))) == 64, "Bedrock: a tall plant's top half is two above the ground");
        Program.Check(On(false, (63, BlockState.Get("minecraft:water", "level=0"))) == 63, "water counts as it is");
        Program.Check(On(false) == null, "no columns drawn: no height");
    }

    static void McStructureRoundTrip()
    {
        Console.WriteLine("== Bedrock .mcstructure round trip");
        var kinds = new[] { BlockState.Air, Stone, Glass, BlockState.Get("minecraft:oak_planks"), BlockState.Get("minecraft:bricks"), BlockState.Get("minecraft:cobblestone") };
        var s = new Structure(4, 3, 5);
        var rnd = new Random(11);
        for (int y = 0; y < s.SizeY; y++) for (int z = 0; z < s.SizeZ; z++) for (int x = 0; x < s.SizeX; x++) s.Set(x, y, z, kinds[rnd.Next(kinds.Length)]);
        string file = Path.Combine(TestPaths.Output, "structure", "roundtrip.mcstructure");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        McStructure.Save(s, file);
        var back = McStructure.Load(file);
        bool size = back.SizeX == s.SizeX && back.SizeY == s.SizeY && back.SizeZ == s.SizeZ;
        Program.Check(size, "mcstructure size round trips");
        int good = 0, total = 0;
        if (size)
            for (int y = 0; y < s.SizeY; y++) for (int z = 0; z < s.SizeZ; z++) for (int x = 0; x < s.SizeX; x++)
            { total++; if (s.Get(x, y, z).BaseName == back.Get(x, y, z).BaseName) good++; }
        Program.Check(good == total, $"mcstructure blocks round trip ({good}/{total})");
        Program.Check(StructureFile.Load(file).SizeX == s.SizeX, "StructureFile.Load reads a .mcstructure");
    }

    /// <summary>Structure files come from anywhere: junk, non-structures, absurd sizes and gzip bombs are refused cleanly.</summary>
    static void Hostile()
    {
        Console.WriteLine("== Hostile structure files are refused");
        string dir = Path.Combine(TestPaths.Output, "structure", "hostile");
        Directory.CreateDirectory(dir);

        string junk = Path.Combine(dir, "junk.nbt");
        var bytes = new byte[5000];
        new Random(3).NextBytes(bytes);
        File.WriteAllBytes(junk, bytes);
        Program.Check(Refused(() => StructureFile.Load(junk)), "random bytes are refused");

        string notStructure = Path.Combine(dir, "notstructure.nbt");
        new NbtFile(new NbtCompound { ["foo"] = new NbtInt(1) }, NbtFormat.Java, CompressionKind.Gzip).Save(notStructure);
        Program.Check(Refused(() => StructureFile.Load(notStructure)), "an NBT file that is not a structure is refused");

        string huge = Path.Combine(dir, "huge.nbt");
        var size = new NbtList(NbtType.Int) { new NbtInt(100000), new NbtInt(100000), new NbtInt(100000) };
        new NbtFile(new NbtCompound { ["size"] = size, ["palette"] = new NbtList(NbtType.Compound), ["blocks"] = new NbtList(NbtType.Compound) }, NbtFormat.Java, CompressionKind.Gzip).Save(huge);
        Program.Check(Refused(() => StructureFile.Load(huge)), "an absurd size (10^15 blocks) is refused before anything is allocated");

        string bomb = Path.Combine(dir, "bomb.nbt");
        using (var fs = File.Create(bomb))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
        {
            var zeros = new byte[1 << 20];
            for (int i = 0; i < 1100; i++) gz.Write(zeros);
        }
        Program.Check(new FileInfo(bomb).Length < (20 << 20), $"the bomb is small on disk ({new FileInfo(bomb).Length / 1024:N0} KB for 1.1 GB)");
        Program.Check(Refused(() => StructureFile.Load(bomb)), "a gzip bomb is stopped at the size cap");
        File.Delete(bomb);
    }

    static bool Refused(Action a)
    {
        try { a(); return false; }
        catch (Exception e) when (e is SaveFormatException or ArgumentException or InvalidDataException or IOException) { return true; }
    }

    static void CropFrom(string label, string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); return; }
        try
        {
            using var world = WorldLoader.Open(path);
            var dim = Overworld(world);
            var (ox, _, oz) = Spot(world, dim, 9, 1, 9);
            // The whole build height of a 9×9 column: wherever the terrain is, the crop holds it.
            var (minY, maxY) = StructurePlacement.Height(world, CanonicalReader.DimensionId(dim));
            var cropped = StructureClip.Extract(world, dim, ox, minY, oz, ox + 8, maxY, oz + 8);
            var back = StructureNbt.FromNbt(StructureNbt.ToNbt(cropped));
            Program.Check(cropped.NonAirCount > 0, $"{label}: crop has blocks ({cropped.NonAirCount:N0} of {cropped.Volume:N0})");
            Program.Check(SameBlocks(cropped, back), $"{label}: the crop survives a .nbt round trip");
        }
        catch (Exception ex) { Program.Check(false, $"{label}: crop threw {ex.GetType().Name}: {ex.Message}"); }
    }

    static void PlaceInto(string label, string path, Structure cube)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); return; }
        var before = Hashes(path);
        string outDir = Path.Combine(TestPaths.Output, "structure", "place-" + string.Concat(label.Where(char.IsLetterOrDigit)));
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        if (label.StartsWith("3DS"))
        {
            // Placing into 3DS is refused before anything is written (changed 3DS chunks make the game delete the world).
            bool refused = false;
            using (var world = WorldLoader.Open(path))
            {
                var (rx, ry, rz) = Spot(world, Overworld(world), 3, 3, 3);
                try { StructurePlacement.PlaceIntoCopy(world, cube, "minecraft:overworld", rx, ry, rz, pasteAir: true, outDir); }
                catch (NotSupportedException) { refused = true; }
            }
            Program.Check(refused, $"{label}: placing into a 3DS world is refused with an explanation");
            Program.Check(!Directory.Exists(outDir), $"{label}: nothing is written when it is refused");
            Program.Check(before.SequenceEqual(Hashes(path)), $"{label}: the original world is byte-for-byte unchanged");
            return;
        }
        try
        {
            PlacementResult r;
            int ox, oy, oz;
            string kind;
            using (var world = WorldLoader.Open(path))
            {
                kind = world.GetType().Name;
                (ox, oy, oz) = Spot(world, Overworld(world), 3, 3, 3);
                r = StructurePlacement.PlaceIntoCopy(world, cube, "minecraft:overworld", ox, oy, oz, pasteAir: true, outDir);
            }
            Program.Check(r.Verified, $"{label}: copy passes its checks{(r.Verified ? "" : " — " + string.Join("; ", r.Problems))}");
            foreach (var n in r.Notes) Program.Info($"   {label}: {n}");
            using var copy = WorldLoader.Open(r.OpenPath);
            Program.Check(copy.GetType().Name == kind, $"{label}: the copy is the same kind of world ({copy.FormatDescription})");
            var reader = new CanonicalReader(copy, Overworld(copy));
            int good = 0;
            for (int y = 0; y < 3; y++)
                for (int z = 0; z < 3; z++)
                    for (int x = 0; x < 3; x++)
                    {
                        string? got = reader.Get(new ChunkPos((ox + x) >> 4, (oz + z) >> 4))?.GetBlock((ox + x) & 15, oy + y, (oz + z) & 15).BaseName;
                        if (got == cube.Get(x, y, z).BaseName) good++;
                        else Program.Info($"   {label}: ({x},{y},{z}) expected {cube.Get(x, y, z).BaseName}, got {got ?? "<none>"}");
                    }
            Program.Check(good == 27, $"{label}: all 27 cube blocks are in the copy ({good} matched, {r.ChunksChanged} chunk(s) changed)");
            // level.dat and everything else outside the changed chunks is the world's own (where level.dat is a file).
            string lvA = Path.Combine(path, "level.dat"), lvB = Path.Combine(r.OpenPath, "level.dat");
            if (File.Exists(lvA)) Program.Check(File.Exists(lvB) && File.ReadAllBytes(lvA).AsSpan().SequenceEqual(File.ReadAllBytes(lvB)), $"{label}: level.dat in the copy is byte-identical");
        }
        catch (Exception ex) { Program.Check(false, $"{label}: placement threw {ex.GetType().Name}: {ex.Message}"); }
        Program.Check(before.SequenceEqual(Hashes(path)), $"{label}: the original world is byte-for-byte unchanged");
    }

    static void ChestInto(string label, string path)
    {
        if (!Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); return; }
        var chest = new Structure(1, 1, 1);
        chest.Set(0, 0, 0, BlockState.Get("minecraft:chest", "facing=north,type=single,waterlogged=false"));
        chest.BlockEntities[0] = Chest(5);
        string outDir = Path.Combine(TestPaths.Output, "structure", "chest-" + string.Concat(label.Where(char.IsLetterOrDigit)));
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        try
        {
            PlacementResult r;
            int ox, oy, oz;
            using (var world = WorldLoader.Open(path))
            {
                (ox, oy, oz) = Spot(world, Overworld(world), 1, 1, 1);
                r = StructurePlacement.PlaceIntoCopy(world, chest, "minecraft:overworld", ox, oy, oz, pasteAir: true, outDir);
            }
            using var copy = WorldLoader.Open(r.OpenPath);
            var c = new CanonicalReader(copy, Overworld(copy)).Get(new ChunkPos(ox >> 4, oz >> 4));
            var be = c?.BlockEntities.FirstOrDefault(b => b.GetInt("x") == ox && b.GetInt("y") == oy && b.GetInt("z") == oz);
            Program.Check(c?.GetBlock(ox & 15, oy, oz & 15).BaseName == "chest", $"{label}: placed chest is a chest in the copy");
            Program.Check(be?.GetList("Items")?.Count == 1, $"{label}: placed chest keeps its items in the copy");
        }
        catch (Exception ex) { Program.Check(false, $"{label}: chest placement threw {ex.GetType().Name}: {ex.Message}"); }
    }

    static WorldDimension Overworld(SaveWorld w) => w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");

    /// <summary>A spot near the spawn, inside a stored chunk and inside the world, where a box of the given size fits.</summary>
    static (int X, int Y, int Z) Spot(SaveWorld world, WorldDimension dim, int sx, int sy, int sz)
    {
        var info = WorldInfoReader.Read(world);
        var stored = dim.ChunkPositions.ToHashSet();
        var spawn = new ChunkPos(info.SpawnX >> 4, info.SpawnZ >> 4);
        var chunk = stored.Contains(spawn) ? spawn : stored.OrderBy(p => Math.Abs((long)p.X - spawn.X) + Math.Abs((long)p.Z - spawn.Z)).First();
        int x = chunk.X * 16 + 2, z = chunk.Z * 16 + 2;
        var (minY, maxY) = StructurePlacement.Height(world, CanonicalReader.DimensionId(dim));
        int y = Math.Clamp(info.SpawnY is > 0 and < 30000 ? info.SpawnY : 64, minY + 1, maxY - sy - 1);
        return (x, y, z);
    }

    /// <summary>A content hash of every file of a world (a folder) or of the file itself.</summary>
    static List<string> Hashes(string path)
    {
        var files = File.Exists(path) ? [path] : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
        return files.Select(f =>
        {
            using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Path.GetRelativePath(File.Exists(path) ? Path.GetDirectoryName(path)! : path, f) + " " + Convert.ToHexString(SHA256.HashData(s));
        }).ToList();
    }

    static NbtCompound Chest(int count) => new()
    {
        ["id"] = new NbtString("minecraft:chest"),
        ["Items"] = new NbtList(NbtType.Compound) { new NbtCompound { ["Slot"] = new NbtByte(0), ["id"] = new NbtString("minecraft:diamond"), ["Count"] = new NbtByte((sbyte)count) } },
    };

    static bool SameBlocks(Structure a, Structure b)
    {
        if (a.SizeX != b.SizeX || a.SizeY != b.SizeY || a.SizeZ != b.SizeZ) return false;
        for (int y = 0; y < a.SizeY; y++)
            for (int z = 0; z < a.SizeZ; z++)
                for (int x = 0; x < a.SizeX; x++)
                    if (a.Get(x, y, z) != b.Get(x, y, z)) return false;
        return true;
    }

    static bool Throws(Action a) { try { a(); return false; } catch { return true; } }
}
