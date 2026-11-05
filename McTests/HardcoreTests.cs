// McTests/HardcoreTests.cs: "hardcore" — Restore Hardcore world from death, on copies of sample worlds (and, given a
// folder, on a copy of a real world).
// Owns: HardcoreTests — a Java and a Bedrock world made hardcore with a dead player (Spectator; Java also no health),
// found as dead, a safe spot found on the surface near the spawn (ground solid, two blocks of air above), the player
// restored and saved, and everything checked after reopening from disk: Survival, full health, standing there, still
// hardcore, and achievements on once the marks are cleared. The worlds are copies; nothing outside the test folder changes.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class HardcoreTests
{
    public static void Run(string[] args)
    {
        string temp = Path.Combine(TestPaths.Output, "hardcore", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(temp);
        Console.WriteLine("== Java Edition");
        One(Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension"), Path.Combine(temp, "java"), makeDead: true);
        Console.WriteLine("== Bedrock Edition");
        One(Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16"), Path.Combine(temp, "bedrock"), makeDead: true);
        if (args.Length > 0 && Directory.Exists(args[0]))
        {
            Console.WriteLine("== A copy of " + Path.GetFileName(args[0]) + " (a real world that died)");
            One(args[0], Path.Combine(temp, "real"), makeDead: false);
        }
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    static void One(string source, string copy, bool makeDead)
    {
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        CopyDir(source, copy);
        string label = Path.GetFileName(source);
        // Make it a hardcore world whose player died (as the game leaves it: Spectator; Java also with no health).
        if (makeDead)
            using (var w = WorldLoader.Open(copy))
            {
                switch (w)
                {
                    case JavaWorld j: (j.Data ?? throw new InvalidDataException("The test world has no level.dat Data.")).SetByte("hardcore", 1); j.SaveLevelDat(); break;
                    case BedrockWorld b: b.LevelRoot.SetByte("IsHardcore", 1); b.SaveLevelDat(); break;
                }
                foreach (var p in w.Players.ToList())
                {
                    if (w is JavaWorld) { p.Data.SetInt("playerGameType", 3); p.Data["Health"] = new NbtFloat(0); }
                    else p.Data.SetInt("PlayerGameMode", 6);
                    PlayerStore.Save(w, p);
                }
            }
        var spots = new Dictionary<string, HardcoreRestore.Spot>();
        using (var w = WorldLoader.Open(copy))
        {
            Program.Check(HardcoreRestore.HasHardcore(w) && HardcoreRestore.IsHardcore(w), $"{label}: a hardcore world");
            var died = HardcoreRestore.Dead(w);
            Program.Check(died.Count > 0 && died.Count == w.Players.Count(p => died.Contains(p)), $"{label}: the dead player(s) are found ({died.Count} of {w.Players.Count})");
            var dim = w.Dimensions.FirstOrDefault(d => d.Id is "minecraft:overworld" or "overworld" or "0") ?? w.Dimensions[0];
            var records = w.Players.ToList();
            foreach (var p in died)
            {
                if (HardcoreRestore.SafeSpot(w, p) is not { } spot) { Program.Check(false, $"{label}: a safe spot for {p.Name}"); continue; }
                // Solid ground under the feet; the body's two blocks are air or plants walked through (in Java's names: a
                // Bedrock world's "snow" is the full snow block, so the feet are never in one).
                var col = dim.LoadChunk(new ChunkPos(spot.X >> 4, spot.Z >> 4))!;
                var under = col.GetBlock(spot.X & 15, spot.Y - 1, spot.Z & 15);
                var feet = col.GetBlock(spot.X & 15, spot.Y, spot.Z & 15);
                var head = col.GetBlock(spot.X & 15, spot.Y + 1, spot.Z & 15);
                string u = MoveTests.JavaName(w, under), f = MoveTests.JavaName(w, feet);
                Program.Check(!under.IsAir && !under.IsWaterLike && !u.Contains("lava") && !u.Contains("leaf_litter") && !u.EndsWith("grass") && !MoveTests.Walkable(u)
                    && !feet.IsWaterLike && (feet.IsAir || MoveTests.Walkable(f)) && head.IsAir,
                    $"{label}: the spot {spot} stands on {u} (feet in {f}, head in {MoveTests.JavaName(w, head)})");
                Program.Info($"{label}: {p.Name} will stand at {spot} on {u}");
                var changed = HardcoreRestore.Restore(w, p, spot);
                Program.Check(changed.Any(c => c.Contains("Survival")), $"{label}: {p.Name} restored ({string.Join(", ", changed)})");
                Program.Info($"{p.Name}: {string.Join(", ", changed)}");
                PlayerStore.Save(w, p);
                spots[p.Source] = spot;
            }
            // A save keeps the records the pages hold (Bedrock re-reads its database after a save).
            Program.Check(w.Players.SequenceEqual(records), $"{label}: the same player records after saving");
            // Achievements back on: the marks cleared.
            var flags = WorldFlags.Read(w);
            flags.ClearMarks();
            flags.Save();
        }
        using (var w = WorldLoader.Open(copy))
        {
            Program.Check(HardcoreRestore.IsHardcore(w) && HardcoreRestore.Dead(w).Count == 0, $"{label}: reopened, still hardcore and nobody dead");
            foreach (var p in w.Players)
            {
                var d = p.Data;
                if (!spots.TryGetValue(p.Source, out var spot)) continue;
                if (w is JavaWorld)
                {
                    var pos = d.GetList("Pos")!;
                    Program.Check(d.GetLongOrNull("playerGameType") == 0 && d["Health"]!.AsDouble == 20 && Math.Abs(pos[0].AsDouble - (spot.X + 0.5)) < 1e-6 && Math.Abs(pos[1].AsDouble - spot.Y) < 1e-6,
                        $"{label}: {p.Name} in Survival, full health, at the spot");
                }
                else
                {
                    var pos = d.GetList("Pos")!;
                    float health = d.GetList("Attributes")?.OfType<NbtCompound>().FirstOrDefault(a => a.GetString("Name") == "minecraft:health") is { } h ? (float)h["Current"]!.AsDouble : -1;
                    Program.Check(d.GetLongOrNull("PlayerGameMode") == 0 && Math.Abs(pos[1].AsDouble - (spot.Y + 1.62)) < 0.01 && (health < 0 || health >= 20),
                        $"{label}: {p.Name} in Survival at the spot (eyes 1.62 up), health {health}");
                }
            }
            var flags = WorldFlags.Read(w);
            Program.Check(!flags.LocksAchievements || flags.AchievementBlockers().Count == 0 || flags.AddOns || flags.ExperimentsOn.Count > 0,
                $"{label}: achievements on ({string.Join("; ", flags.AchievementBlockers())})");
            if (makeDead) RespawnPoints(w, label);
        }
    }

    // A player's own bed or respawn point in the overworld comes first; one in the Nether (a respawn anchor) is not used.
    // Only in memory: nothing here is saved.
    static void RespawnPoints(SaveWorld w, string label)
    {
        if (w.Players.Count == 0 || HardcoreRestore.SafeSpot(w) is not { } near) return;
        var p = w.Players[0];
        var d = p.Data;
        void Expect(string what, bool home)
        {
            var s = HardcoreRestore.SafeSpot(w, p);
            Program.Check(s is { } got && got.Where.Contains(home ? "bed" : "world spawn") && (!home || (got.X, got.Y, got.Z) == (near.X, near.Y, near.Z)),
                $"{label}: {what} → {s?.ToString() ?? "none"}");
        }
        Expect("no respawn point of their own", home: false);
        if (w is JavaWorld)
        {
            d.SetInt("SpawnX", near.X); d.SetInt("SpawnY", near.Y); d.SetInt("SpawnZ", near.Z); d.SetString("SpawnDimension", "minecraft:overworld");
            Expect("a bed in the overworld (SpawnX/Y/Z)", home: true);
            d.SetString("SpawnDimension", "minecraft:the_nether");
            Expect("a respawn anchor in the Nether", home: false);
            foreach (var k in (string[])["SpawnX", "SpawnY", "SpawnZ", "SpawnDimension"]) d.Remove(k);
            d["respawn"] = new NbtCompound { ["pos"] = new NbtIntArray([near.X, near.Y, near.Z]), ["dimension"] = new NbtString("minecraft:overworld") };
            Expect("a bed in the overworld (1.21.5's respawn)", home: true);
            d.Remove("respawn");
        }
        else
        {
            d.SetInt("SpawnX", near.X); d.SetInt("SpawnY", near.Y); d.SetInt("SpawnZ", near.Z); d.SetInt("SpawnDimension", 0);
            foreach (var k in (string[])["SpawnBlockPositionX", "SpawnBlockPositionY", "SpawnBlockPositionZ"]) d.SetInt(k, int.MinValue);
            Expect("a bed in the overworld (SpawnX/Y/Z)", home: true);
            d.SetInt("SpawnDimension", 1);
            Expect("a respawn anchor in the Nether", home: false);
            d.SetInt("SpawnDimension", 0);
            foreach (var k in (string[])["SpawnX", "SpawnY", "SpawnZ"]) d.SetInt(k, int.MinValue);
            Expect("no respawn point set (int.MinValue)", home: false);
        }
    }
}
