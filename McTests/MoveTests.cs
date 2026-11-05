// McTests/MoveTests.cs: "move" — Move a player to safety (Players page), on copies of sample worlds.
// Owns: MoveTests — a Java and a Bedrock player put somewhere bad (the Nether, under the world, falling, burning), a safe
// spot found near the spawn and near coordinates (solid ground, room for the body), moved and saved, and checked after
// reopening: at the spot (Bedrock's eyes 1.62 up), in the overworld in the file's own types, still, not falling or burning,
// and every other field as before. A Java player riding a boat with a villager in it (RootVehicle): room for the boat, the
// boat and the villager moved with them. A save that fails (level.dat read-only) leaves the player and the file as they
// were. What mounts are called and the room they need. The worlds are copies; nothing outside the test folder changes.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class MoveTests
{
    static readonly string Java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
    static readonly string Bedrock = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16");

    public static void Run(string[] args)
    {
        string temp = Path.Combine(TestPaths.Output, "move", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(temp);
        Console.WriteLine("== Java Edition");
        One(Java, Path.Combine(temp, "java"));
        Console.WriteLine("== Bedrock Edition");
        One(Bedrock, Path.Combine(temp, "bedrock"));
        Console.WriteLine("== Java Edition, riding a boat with a villager in it");
        Riding(Java, Path.Combine(temp, "riding"));
        Console.WriteLine("== A save that fails");
        Failing(Java, Path.Combine(temp, "failing"));
        Console.WriteLine("== What mounts are called and the room they need");
        Mounts();
        Console.WriteLine("== Into a new copy (the command line's move)");
        IntoCopy(Java, Path.Combine(temp, "copy"));
        Console.WriteLine("== Xbox 360 Edition, riding a pig the old way (\"Riding\")");
        Console360(Path.Combine(temp, "x360"));
        Console.WriteLine("== Pocket Edition 0.x");
        Pocket(Path.Combine(temp, "pocket"));
        Console.WriteLine("== Indev");
        Indev(Path.Combine(temp, "indev"));
        Console.WriteLine("== Into a new copy of an Xbox 360 save");
        IntoCopyConsole(Path.Combine(temp, "x360-copy"));
    }

    static readonly string X360 = Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");

    /// <summary>The fields a move may change: the rest of the player must read back as it was.</summary>
    static void SameOthers(string label, NbtCompound before, NbtCompound now, params string[] changed)
    {
        var others = before.Keys.Where(k => !changed.Contains(k)).ToList();
        var differ = others.Where(k => now[k] is not { } v || !v.ContentEquals(before[k]!)).ToList();
        Program.Check(differ.Count == 0 && now.Count == before.Count, $"{label}: the other {others.Count} fields are as they were{(differ.Count > 0 ? ": " + string.Join(", ", differ) : "")}");
    }

    // A console player is a Java 1.x player: the feet's height in doubles, Dimension a number, what they ride in "Riding".
    static void Console360(string folder)
    {
        if (!File.Exists(X360)) { Console.WriteLine("   (not found: skipped)"); return; }
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "savegame.dat");
        File.Copy(X360, file);
        HardcoreRestore.Spot spot;
        NbtCompound before;
        string key;
        using (var w = WorldLoader.Open(file))
        {
            Program.Check(HardcoreRestore.CanMove(w) && w.Players.Count > 0, "Xbox 360: its players can be moved");
            var p = w.Players[0];
            key = p.Source;
            var d = p.Data;
            d["Dimension"] = new NbtInt(-1);
            d["Pos"] = Doubles(12.5, 30, -7.5);
            d["Motion"] = Doubles(0.2, -3.9, 0.1);
            d["FallDistance"] = new NbtFloat(80);
            d["Fire"] = new NbtShort(300);
            d["Riding"] = new NbtCompound
            {
                ["id"] = new NbtString("Pig"), ["Pos"] = Doubles(100.5, 70, 100.5), ["Motion"] = Doubles(0, -1, 0), ["Fire"] = new NbtShort(50),
                ["FallDistance"] = new NbtFloat(10), ["Saddle"] = new NbtByte(1), ["Health"] = new NbtShort(10),
            };
            before = (NbtCompound)d.Clone();
            Program.Check(HardcoreRestore.RoomFor(w, p) == (3, 1) && HardcoreRestore.MountName(w, p) == "pig", $"Xbox 360: a pig ridden the old way needs 3 up and the columns around ({HardcoreRestore.RoomFor(w, p)}, {HardcoreRestore.MountName(w, p)})");
            if (HardcoreRestore.SafeSpot(w, p, besideBed: false) is not { } found) { Program.Check(false, "Xbox 360: a safe spot near the world spawn"); return; }
            spot = found;
            Program.Check(Unsafe(w, spot, 3, 1) is null, $"Xbox 360: near the world spawn, room for the pig: {spot} {Unsafe(w, spot, 3, 1)}");
            var done = HardcoreRestore.MoveAndSave(w, p, spot);
            Program.Check(done.Contains("their pig came with them"), $"Xbox 360: moved ({string.Join(", ", done)})");
        }
        using (var w = WorldLoader.Open(file))
        {
            var p = w.Players.FirstOrDefault(r => r.Source == key);
            if (p == null) { Program.Check(false, "Xbox 360: the player is still in the save after reopening"); return; }
            var d = p.Data;
            var pos = d.GetList("Pos")!;
            Program.Check(pos.ElementType == NbtType.Double && Math.Abs(pos[0].AsDouble - (spot.X + 0.5)) < 1e-9 && Math.Abs(pos[1].AsDouble - spot.Y) < 1e-9 && Math.Abs(pos[2].AsDouble - (spot.Z + 0.5)) < 1e-9,
                $"Xbox 360: standing at the spot, the feet's height ({pos[0].AsDouble}, {pos[1].AsDouble}, {pos[2].AsDouble})");
            Program.Check(d["Dimension"] is NbtInt { Value: 0 } && d["Fire"] is NbtShort { Value: -20 } && d["FallDistance"] is NbtFloat { Value: 0 } && d.GetList("Motion")!.All(t => t.AsDouble == 0),
                $"Xbox 360: in the overworld, still, not falling or burning ({d["Dimension"]}, Fire {d["Fire"]})");
            var pig = d.GetCompound("Riding");
            Program.Check(pig != null && pig.GetList("Pos") is { } pp && Math.Abs(pp[0].AsDouble - (spot.X + 0.5)) < 1e-9 && Math.Abs(pp[1].AsDouble - spot.Y) < 1e-9 && pig["Fire"] is NbtShort { Value: -1 }
                && pig["FallDistance"] is NbtFloat { Value: 0 } && pig["Saddle"] is NbtByte { Value: 1 } && pig["Health"] is NbtShort { Value: 10 },
                "Xbox 360: the pig came along, still and not burning, its saddle and health kept");
            SameOthers("Xbox 360", before, d, "Pos", "Motion", "FallDistance", "Fire", "Dimension", "Riding");
        }
    }

    // Pocket 0.2 – 0.8: the one player in level.dat, at the eyes' height in floats (the sample has none: one is put in its copy).
    static void Pocket(string folder)
    {
        // The only Pocket sample is a 0.1 world (no level.dat, no player): a 0.2 – 0.8 world is made by the converter's own
        // Pocket writer from the Bedrock sample, and its player is then put somewhere bad.
        if (!Directory.Exists(Bedrock)) { Console.WriteLine("   (no Bedrock sample to make a Pocket world from: skipped)"); return; }
        using (var b = WorldLoader.Open(Bedrock))
        {
            var report = MinecraftStudio.Core.Conversion.WorldConverter.Run(b, MinecraftStudio.Core.Conversion.TargetCatalog.All.First(t => t.Kind == MinecraftStudio.Core.Conversion.TargetKind.PocketLegacy), folder, new MinecraftStudio.Core.Conversion.ConversionOptions());
            Program.Check(report.Verified && report.OpenPath != null, $"Pocket 0.x: a 0.2 – 0.8 world made from the Bedrock sample ({report.Checks.Count} checks)");
            if (report.OpenPath == null) return;
            folder = report.OpenPath;
        }
        using (var w = (PocketLegacyWorld)WorldLoader.Open(folder))
        {
            if (w.Level?.RootCompound is not NbtCompound level) { Program.Check(false, "Pocket 0.x: the made world has a level.dat"); return; }
            level["Player"] = new NbtCompound
            {
                ["Pos"] = Floats(12.5f, -100f, -7.5f), ["Motion"] = Floats(0.2f, -3.9f, 0.1f), ["Rotation"] = Floats(90, 10), ["FallDistance"] = new NbtFloat(80),
                ["Fire"] = new NbtShort(300), ["Health"] = new NbtShort(20), ["Air"] = new NbtShort(300), ["OnGround"] = new NbtByte(0), ["Inventory"] = new NbtList(NbtType.Compound),
            };
            w.SaveLevel();
        }
        HardcoreRestore.Spot spot;
        NbtCompound before;
        using (var w = WorldLoader.Open(folder))
        {
            Program.Check(HardcoreRestore.CanMove(w) && w.Players.Count == 1, "Pocket 0.x: its player can be moved");
            if (w.Players.Count == 0) return;
            var p = w.Players[0];
            before = (NbtCompound)p.Data.Clone();
            Program.Check(HardcoreRestore.RoomFor(w, p) == (2, 0), "Pocket 0.x: a player needs 2 blocks of room");
            if (HardcoreRestore.SafeSpot(w, p, besideBed: false) is not { } found) { Program.Check(false, "Pocket 0.x: a safe spot near the world spawn"); return; }
            spot = found;
            Program.Check(Unsafe(w, spot) is null, $"Pocket 0.x: near the world spawn, safe: {spot} {Unsafe(w, spot)}");
            HardcoreRestore.MoveAndSave(w, p, spot);
        }
        using (var w = WorldLoader.Open(folder))
        {
            var d = w.Players.Single().Data;
            var pos = d.GetList("Pos")!;
            Program.Check(pos.ElementType == NbtType.Float && Math.Abs(pos[0].AsDouble - (spot.X + 0.5)) < 1e-4 && Math.Abs(pos[1].AsDouble - (spot.Y + 1.62)) < 1e-4 && Math.Abs(pos[2].AsDouble - (spot.Z + 0.5)) < 1e-4,
                $"Pocket 0.x: standing at the spot, eyes 1.62 up ({pos[0].AsDouble}, {pos[1].AsDouble}, {pos[2].AsDouble})");
            Program.Check(d["Fire"] is NbtShort { Value: 0 } && d["FallDistance"] is NbtFloat { Value: 0 } && d.GetList("Motion")!.All(t => t.AsDouble == 0), "Pocket 0.x: still, not falling or burning");
            SameOthers("Pocket 0.x", before, d, "Pos", "Motion", "FallDistance", "Fire");
        }
    }

    // Indev: the LocalPlayer entity of a one-file level, written at the eyes' height, two more blocks of room taken.
    static void Indev(string folder)
    {
        string source = Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "hell.mclevel");
        if (!File.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "hell.mclevel");
        File.Copy(source, file);
        HardcoreRestore.Spot spot;
        NbtCompound before;
        using (var w = WorldLoader.Open(file))
        {
            Program.Check(HardcoreRestore.CanMove(w) && w.Players.Count == 1, "Indev: its player can be moved");
            if (w.Players.Count == 0) return;
            var p = w.Players[0];
            var d = p.Data;
            d["Pos"] = Floats(5.5f, 300f, 5.5f);
            d["Motion"] = Floats(0, -3.9f, 0);
            d["FallDistance"] = new NbtFloat(80);
            d["Fire"] = new NbtShort(300);
            before = (NbtCompound)d.Clone();
            Program.Check(HardcoreRestore.RoomFor(w, p) == (4, 0), "Indev: 4 blocks of room (2, and 2 more for either reading of the height)");
            if (HardcoreRestore.SafeSpot(w, p, besideBed: false) is not { } found) { Program.Check(false, "Indev: a safe spot near the level's spawn"); return; }
            spot = found;
            Program.Check(Unsafe(w, spot, 4) is null, $"Indev: near the spawn, safe with 4 blocks of room: {spot} {Unsafe(w, spot, 4)}");
            HardcoreRestore.MoveAndSave(w, p, spot);
        }
        using (var w = WorldLoader.Open(file))
        {
            var d = w.Players.Single().Data;
            var pos = d.GetList("Pos")!;
            Program.Check(pos.ElementType == NbtType.Float && Math.Abs(pos[1].AsDouble - (spot.Y + 1.62)) < 1e-4 && Math.Abs(pos[0].AsDouble - (spot.X + 0.5)) < 1e-4,
                $"Indev: standing at the spot, written at the eyes ({pos[0].AsDouble}, {pos[1].AsDouble}, {pos[2].AsDouble})");
            Program.Check(d["Fire"] is NbtShort { Value: -20 } && d["FallDistance"] is NbtFloat { Value: 0 } && d.GetList("Motion")!.All(t => t.AsDouble == 0), $"Indev: still, not falling or burning (Fire {d["Fire"]})");
            SameOthers("Indev", before, d, "Pos", "Motion", "FallDistance", "Fire");
        }
    }

    // The command line's move on a one-file console save: the save only read, the copy's player found by its name inside the save.
    static void IntoCopyConsole(string output)
    {
        if (!File.Exists(X360)) { Console.WriteLine("   (not found: skipped)"); return; }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(X360)));
        using (var w = WorldLoader.Open(X360))
        {
            var r = PlayerMoveCopy.Run(w, [w.Players[0]], (copy, p) => HardcoreRestore.SafeSpot(copy, p, besideBed: false), output);
            Program.Check(r.Verified && r.OpenPath.EndsWith("savegame.dat", StringComparison.OrdinalIgnoreCase), $"Xbox 360 into a copy: moved and read back ({string.Join("; ", r.Problems)})");
        }
        Program.Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(X360))) == hash, "Xbox 360 into a copy: the save itself is not changed");
    }

    static void IntoCopy(string source, string output)
    {
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        byte[] before = File.ReadAllBytes(Path.Combine(source, "level.dat"));
        using var w = WorldLoader.Open(source);
        var host = w.Players.First(r => Path.GetFileName(r.Source).Equals("level.dat", StringComparison.OrdinalIgnoreCase));
        var same = w.Players.Where(r => MinecraftStudio.Core.World.ServerPlayers.UuidOf(r) is Guid g && g == MinecraftStudio.Core.World.ServerPlayers.UuidOf(host)).ToList();
        var r = PlayerMoveCopy.Run(w, same, (copy, p) => HardcoreRestore.SafeSpot(copy, p, besideBed: false), output);
        Program.Check(r.Verified && r.Done.Any(d => d.StartsWith("standing at", StringComparison.Ordinal)), $"into a copy: {same.Count} record(s) of the player moved and read back ({string.Join("; ", r.Problems)})");
        Program.Check(File.ReadAllBytes(Path.Combine(source, "level.dat")).AsSpan().SequenceEqual(before), "into a copy: the world itself is not changed");
        try { PlayerMoveCopy.Run(w, [host], (copy, p) => HardcoreRestore.SafeSpot(copy, p), output); Program.Check(false, "into a copy: a folder that is not empty is refused"); }
        catch (IOException) { Program.Check(true, "into a copy: a folder that is not empty is refused"); }
        string far = output + "-far";
        try { PlayerMoveCopy.Run(w, [host], (copy, p) => HardcoreRestore.SafeSpotNear(copy, 20_000_000, 0, p), far); Program.Check(false, "into a copy: no safe ground is refused"); }
        catch (IOException) { Program.Check(!Directory.Exists(far), "into a copy: no safe ground is refused, and no copy is left behind"); }
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    static NbtList Doubles(params double[] v) { var l = new NbtList(NbtType.Double); foreach (var d in v) l.Add(new NbtDouble(d)); return l; }
    static NbtList Floats(params float[] v) { var l = new NbtList(NbtType.Float); foreach (var f in v) l.Add(new NbtFloat(f)); return l; }

    static WorldDimension Overworld(SaveWorld w) => w.Dimensions.FirstOrDefault(d => d.Id is "minecraft:overworld" or "overworld" or "0") ?? w.Dimensions[0];

    // Solid ground under the feet and room above, read back from the world: reach columns out to each side, height blocks up.
    static string? Unsafe(SaveWorld w, HardcoreRestore.Spot s, int height = 2, int reach = 0)
    {
        var dim = Overworld(w);
        var under = dim.LoadChunk(new ChunkPos(s.X >> 4, s.Z >> 4))!.GetBlock(s.X & 15, s.Y - 1, s.Z & 15);
        string u = JavaName(w, under);
        if (under.IsAir || under.IsWaterLike || u.Contains("lava") || u.Contains("leaves") || u.Contains("fire") || Walkable(u))
            return $"stands on {u}";
        for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                var col = dim.LoadChunk(new ChunkPos((s.X + dx) >> 4, (s.Z + dz) >> 4));
                if (col == null) return $"column {s.X + dx}, {s.Z + dz} isn't in the world";
                for (int h = 0; h < height; h++)
                {
                    var b = col.GetBlock((s.X + dx) & 15, s.Y + h, (s.Z + dz) & 15);
                    if (!b.IsAir && !Walkable(JavaName(w, b))) return $"{JavaName(w, b)} at {s.X + dx}, {s.Y + h}, {s.Z + dz}";
                }
            }
        return null;
    }

    /// <summary>A block's name in Java's words: a Bedrock world's "snow" is the full snow block, its "snow_layer" the layer.</summary>
    internal static string JavaName(SaveWorld w, BlockState b) => w is BedrockWorld ? BlockNames.JavaBaseName(b) : b.BaseName;

    /// <summary>A plant or thin cover a player stands in (Java names: "grass" is the short grass of 1.13 to 1.20.2).</summary>
    internal static bool Walkable(string javaName) => WalkableNames.Contains(javaName) || javaName.EndsWith("_tulip", StringComparison.Ordinal) || javaName.EndsWith("_sapling", StringComparison.Ordinal);

    static readonly HashSet<string> WalkableNames = ["grass", "short_grass", "tall_grass", "fern", "large_fern", "dandelion", "poppy", "blue_orchid", "allium", "azure_bluet",
        "oxeye_daisy", "cornflower", "lily_of_the_valley", "sunflower", "lilac", "rose_bush", "peony", "snow", "dead_bush", "torch"];

    static void One(string source, string copy)
    {
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        CopyDir(source, copy);
        string label = Path.GetFileName(source);
        HardcoreRestore.Spot spot;
        NbtCompound before;
        string key;
        using (var w = WorldLoader.Open(copy))
        {
            if (w.Players.Count == 0) { Program.Check(false, $"{label}: has a player"); return; }
            var p = w.Players[0];
            key = p.Source;
            var d = p.Data;
            // Somewhere bad: the Nether (or under the world), falling fast, burning.
            if (w is JavaWorld)
            {
                d["Dimension"] = d["Dimension"] is NbtString ? new NbtString("minecraft:the_nether") : new NbtInt(-1);
                d["Pos"] = Doubles(12.5, 30, -7.5);
                d["Motion"] = Doubles(0.2, -3.9, 0.1);
                d["FallDistance"] = new NbtFloat(80);
                d["Fire"] = new NbtShort(300);
            }
            else
            {
                d.SetInt("DimensionId", 1);
                d["Pos"] = Floats(12.5f, -100f, -7.5f);
                d["Motion"] = Floats(0.2f, -3.9f, 0.1f);
                d["FallDistance"] = new NbtFloat(80);
                d["Fire"] = new NbtShort(300);
            }
            before = (NbtCompound)d.Clone();
            Program.Check(HardcoreRestore.RoomFor(w, p) == (2, 0) && HardcoreRestore.MountName(w, p) == null, $"{label}: a player on foot needs 2 blocks of room");
            if (HardcoreRestore.SafeSpot(w, p, besideBed: false) is not { } found) { Program.Check(false, $"{label}: a safe spot near the world spawn"); return; }
            spot = found;
            Program.Check(spot.Where.Contains("world spawn") && Unsafe(w, spot) is null, $"{label}: near the world spawn, safe: {spot} {Unsafe(w, spot)}");
            var info = MinecraftStudio.Core.Conversion.WorldInfoReader.Read(w);
            var near = HardcoreRestore.SafeSpotNear(w, info.SpawnX, info.SpawnZ, p);
            Program.Check(near is { } n && (n.X, n.Y, n.Z) == (spot.X, spot.Y, spot.Z) && n.Where == $"near {info.SpawnX}, {info.SpawnZ}",
                $"{label}: near the spawn's own coordinates finds the same place ({near})");
            var far = System.Diagnostics.Stopwatch.StartNew();
            Program.Check(HardcoreRestore.SafeSpotNear(w, 20_000_000, 20_000_000, p) == null && far.Elapsed.TotalSeconds < 10,
                $"{label}: none where the world isn't generated ({far.Elapsed.TotalSeconds:0.0} s)");
            var records = w.Players.ToList();
            var done = HardcoreRestore.MoveAndSave(w, p, spot);
            Program.Check(done.Count == 1 && done[0].StartsWith("standing at", StringComparison.Ordinal), $"{label}: moved ({string.Join(", ", done)})");
            Program.Check(w.Players.SequenceEqual(records), $"{label}: the same player records after saving");
        }
        using (var w = WorldLoader.Open(copy))
        {
            var p = w.Players.FirstOrDefault(r => r.Source == key);
            if (p == null) { Program.Check(false, $"{label}: the player is still there after reopening"); return; }
            var d = p.Data;
            var pos = d.GetList("Pos")!;
            string[] changed;
            if (w is JavaWorld)
            {
                changed = ["Pos", "Motion", "FallDistance", "fall_distance", "Fire", "Dimension"];
                Program.Check(pos.ElementType == NbtType.Double && Math.Abs(pos[0].AsDouble - (spot.X + 0.5)) < 1e-9 && Math.Abs(pos[1].AsDouble - spot.Y) < 1e-9 && Math.Abs(pos[2].AsDouble - (spot.Z + 0.5)) < 1e-9,
                    $"{label}: standing at the spot ({pos[0].AsDouble}, {pos[1].AsDouble}, {pos[2].AsDouble})");
                Program.Check(d["Dimension"] is NbtString { Value: "minecraft:overworld" } || d["Dimension"] is NbtInt { Value: 0 }, $"{label}: in the overworld, written as before ({d["Dimension"]})");
                Program.Check(d["Fire"] is NbtShort { Value: -20 } && d["FallDistance"] is NbtFloat { Value: 0 }, $"{label}: not burning, not falling (Fire {d["Fire"]}, FallDistance {d["FallDistance"]})");
            }
            else
            {
                changed = ["Pos", "Motion", "FallDistance", "Fire", "DimensionId"];
                Program.Check(pos.ElementType == NbtType.Float && Math.Abs(pos[0].AsDouble - (spot.X + 0.5)) < 1e-4 && Math.Abs(pos[1].AsDouble - (spot.Y + 1.62)) < 1e-4 && Math.Abs(pos[2].AsDouble - (spot.Z + 0.5)) < 1e-4,
                    $"{label}: standing at the spot, eyes 1.62 up ({pos[0].AsDouble}, {pos[1].AsDouble}, {pos[2].AsDouble})");
                Program.Check(d["DimensionId"] is NbtInt { Value: 0 }, $"{label}: in the overworld ({d["DimensionId"]})");
                Program.Check(d["Fire"] is NbtShort { Value: 0 } && d["FallDistance"] is NbtFloat { Value: 0 }, $"{label}: not burning, not falling");
            }
            Program.Check(d.GetList("Motion") is { Count: 3 } m && m.All(t => t.AsDouble == 0), $"{label}: standing still");
            var others = before.Keys.Where(k => !changed.Contains(k)).ToList();
            var differ = others.Where(k => d[k] is not { } now || !now.ContentEquals(before[k]!)).ToList();
            Program.Check(differ.Count == 0 && d.Count == before.Count, $"{label}: the other {others.Count} fields are as they were{(differ.Count > 0 ? ": " + string.Join(", ", differ) : "")}");
            Program.Info($"{label}: moved to {spot}");
        }
    }

    static void Riding(string source, string copy)
    {
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        CopyDir(source, copy);
        HardcoreRestore.Spot spot;
        NbtCompound boatBefore;
        string key;
        using (var w = WorldLoader.Open(copy))
        {
            var p = w.Players[0];
            key = p.Source;
            int[] boatId = [11, 22, 33, 44], villagerId = [55, 66, 77, 88];
            var villager = new NbtCompound
            {
                ["id"] = new NbtString("minecraft:villager"), ["Pos"] = Doubles(1000.5, 40.6, 1000.5), ["Motion"] = Doubles(0, -1, 0),
                ["FallDistance"] = new NbtFloat(12), ["Fire"] = new NbtShort(-1), ["UUID"] = new NbtIntArray(villagerId), ["Health"] = new NbtFloat(20),
            };
            var boat = new NbtCompound
            {
                ["id"] = new NbtString("minecraft:boat"), ["Type"] = new NbtString("oak"), ["Pos"] = Doubles(1000.5, 40, 1000.5), ["Motion"] = Doubles(0.3, -1, 0),
                ["Rotation"] = Floats(90, 0), ["FallDistance"] = new NbtFloat(30), ["Fire"] = new NbtShort(50), ["UUID"] = new NbtIntArray(boatId),
                ["Passengers"] = new NbtList(NbtType.Compound) { villager },
            };
            p.Data["RootVehicle"] = new NbtCompound { ["Attach"] = new NbtIntArray(boatId), ["Entity"] = boat };
            boatBefore = (NbtCompound)boat.Clone();
            Program.Check(HardcoreRestore.RoomFor(w, p) == (3, 1) && HardcoreRestore.MountName(w, p) == "boat", $"riding: a boat needs 3 up and the columns around ({HardcoreRestore.RoomFor(w, p)}, {HardcoreRestore.MountName(w, p)})");
            if (HardcoreRestore.SafeSpot(w, p, besideBed: false) is not { } found) { Program.Check(false, "riding: a safe spot with room for the boat"); return; }
            spot = found;
            Program.Check(Unsafe(w, spot, height: 3, reach: 1) is null, $"riding: room for the boat around {spot} {Unsafe(w, spot, 3, 1)}");
            var done = HardcoreRestore.MoveAndSave(w, p, spot);
            Program.Check(done.Contains("their boat came with them"), $"riding: moved ({string.Join(", ", done)})");
        }
        using (var w = WorldLoader.Open(copy))
        {
            var p = w.Players.First(r => r.Source == key);
            var boat = HardcoreRestore.Mount(p.Data);
            if (boat == null) { Program.Check(false, "riding: still riding the boat after reopening"); return; }
            bool At(NbtCompound e) => e.GetList("Pos") is { Count: 3 } q && q[0].AsDouble == spot.X + 0.5 && q[1].AsDouble == spot.Y && q[2].AsDouble == spot.Z + 0.5;
            Program.Check(At(boat) && boat.GetList("Motion")!.All(t => t.AsDouble == 0) && boat["Fire"] is NbtShort { Value: -1 } && boat["FallDistance"] is NbtFloat { Value: 0 },
                "riding: the boat is at the spot, still, not burning, not falling");
            var villager = boat.GetList("Passengers")?.OfType<NbtCompound>().FirstOrDefault();
            Program.Check(villager != null && At(villager) && villager["Fire"] is NbtShort { Value: -1 } && villager["FallDistance"] is NbtFloat { Value: 0 } && villager["Health"] is NbtFloat { Value: 20 },
                "riding: the villager in it came too");
            Program.Check(p.Data.GetCompound("RootVehicle")?["Attach"] is NbtIntArray { Value: [11, 22, 33, 44] } && boat["UUID"]!.ContentEquals(boatBefore["UUID"]!) && boat["Type"]!.ContentEquals(boatBefore["Type"]!) && boat["Rotation"]!.ContentEquals(boatBefore["Rotation"]!),
                "riding: still seated in the same boat (its id, kind and facing kept)");
            Program.Check(At(p.Data), "riding: the player is there too");
        }
    }

    static void Failing(string source, string copy)
    {
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        CopyDir(source, copy);
        string levelDat = Path.Combine(copy, "level.dat");
        byte[] onDisk = File.ReadAllBytes(levelDat);
        using var w = WorldLoader.Open(copy);
        var p = w.Players.FirstOrDefault(r => Path.GetFileName(r.Source).Equals("level.dat", StringComparison.OrdinalIgnoreCase));
        if (p == null) { Console.WriteLine("   (no player in level.dat: skipped)"); return; }
        var before = (NbtCompound)p.Data.Clone();
        var spot = HardcoreRestore.SafeSpot(w, p, besideBed: false);
        if (spot == null) { Program.Check(false, "failing: a spot"); return; }
        File.SetAttributes(levelDat, FileAttributes.ReadOnly);
        Exception? caught = null;
        try { HardcoreRestore.MoveAndSave(w, p, spot.Value); }
        catch (Exception e) { caught = e; }
        finally { File.SetAttributes(levelDat, FileAttributes.Normal); }
        Program.Check(caught is UnauthorizedAccessException or IOException, $"failing: the save is refused ({caught?.GetType().Name}: {caught?.Message})");
        Program.Check(p.Data.ContentEquals(before), "failing: the player in memory is as before (no half move left to save later)");
        Program.Check(File.ReadAllBytes(levelDat).AsSpan().SequenceEqual(onDisk), "failing: level.dat on disk is as before");
    }

    static void Mounts()
    {
        if (!Directory.Exists(Java) || !Directory.Exists(Bedrock)) { Console.WriteLine("   (sample worlds not found: skipped)"); return; }
        using var java = WorldLoader.Open(Java);
        using var bedrock = WorldLoader.Open(Bedrock);
        PlayerRecord Riding(string? id, int? type = null)
        {
            var data = new NbtCompound();
            if (id != null)
            {
                var e = new NbtCompound { ["id"] = new NbtString(id) };
                if (type is int t) e.SetInt("Type", t);
                data["RootVehicle"] = new NbtCompound { ["Entity"] = e };
            }
            return new PlayerRecord("test", "test.dat", data);
        }
        foreach (var (id, type, name, room) in new (string?, int?, string?, (int, int))[]
        {
            (null, null, null, (2, 0)), ("minecraft:oak_boat", null, "oak boat", (3, 1)), ("Boat", null, "boat", (3, 1)), ("MinecartRideable", null, "minecart", (3, 1)),
            ("Pig", null, "pig", (3, 1)), ("minecraft:horse", null, "horse", (4, 1)), ("EntityHorse", 1, "donkey", (4, 1)), ("EntityHorse", 4, "skeleton horse", (4, 1)),
            ("minecraft:strider", null, "strider", (4, 1)), ("minecraft:camel", null, "camel", (5, 1)), ("minecraft:happy_ghast", null, "happy ghast", (7, 2)),
            ("", null, "mount", (5, 1)),
        })
        {
            var p = Riding(id);
            if (type != null) p = Riding(id, type);
            var gotRoom = HardcoreRestore.RoomFor(java, p);
            var gotName = HardcoreRestore.MountName(java, p);
            Program.Check(gotRoom == room && gotName == name, $"{id ?? "on foot"}{(type != null ? $" Type {type}" : "")}: {gotName ?? "rides nothing"}, room {gotRoom}");
        }
        var b = Riding("minecraft:horse");
        Program.Check(HardcoreRestore.RoomFor(bedrock, b) == (2, 0) && HardcoreRestore.MountName(bedrock, b) == null, "Bedrock keeps what a player rides in the world, not in the player: on foot");
    }
}
