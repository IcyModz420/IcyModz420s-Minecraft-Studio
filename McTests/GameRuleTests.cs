// McTests/GameRuleTests.cs: "gamerules" — a world's game rules read, changed and saved, on copies.
// Owns: GameRuleTests — copies of Java 1.16, Java 1.12, Bedrock 1.16, Bedrock 1.18 and Xbox 360 worlds: the rules are read
// with plain names (keep inventory found), keep inventory switched and random tick speed set, saved the way the Cheats
// page saves them, reopened: those two changed, every other rule and every other level.dat value exactly as before
// (same type too). Classic and Indev levels: the spawn point set and saved into the level file where the level keeps one
// (Indev, Classic from 0.0.14a), every block as before; refused, the file untouched, where it doesn't. The originals are never touched.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class GameRuleTests
{
    static readonly (string Label, string Path)[] Worlds =
    [
        ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
        ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
        ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
        ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
    ];

    public static void Run(string[] args)
    {
        string dir = Path.Combine(TestPaths.Output, "gamerules", DateTime.Now.ToString("HHmmss"));
        foreach (var (label, source) in Worlds)
        {
            if (!File.Exists(source) && !Directory.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            string copy = Path.Combine(dir, string.Concat(label.Where(char.IsLetterOrDigit)));
            if (File.Exists(source)) { Directory.CreateDirectory(copy); copy = Path.Combine(copy, Path.GetFileName(source)); File.Copy(source, copy); }
            else CopyDir(source, copy);
            One(label, copy);
            Settings(label, copy);
        }
        LevelSpawns(Path.Combine(dir, "levels"));
    }

    // Every Classic and Indev sample: a spawn set and read back after reopening, the blocks as they were; or refused, untouched.
    static void LevelSpawns(string dir)
    {
        string folder = Path.Combine(TestPaths.Data, "pymclevel", "testfiles");
        if (!Directory.Exists(folder)) return;
        int set = 0, refused = 0;
        foreach (var source in Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".mclevel", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mine", StringComparison.OrdinalIgnoreCase)))
        {
            MinecraftStudio.Core.Legacy.FiniteWorld? level;
            try { level = WorldLoader.Open(source) as MinecraftStudio.Core.Legacy.FiniteWorld; } catch (Exception) { continue; }
            if (level == null) continue;
            level.Dispose();
            string name = Path.GetFileName(source);
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, name);
            File.Copy(source, copy, true);
            byte[] before = File.ReadAllBytes(copy);
            byte[] blocks;
            (int X, int Y, int Z) want;
            using (var w = (MinecraftStudio.Core.Legacy.FiniteWorld)WorldLoader.Open(copy))
            {
                blocks = (byte[])(w.Classic?.Blocks ?? w.Indev!.Blocks).Clone();
                int width = w.Classic?.Width ?? w.Indev!.Width, length = w.Classic?.Length ?? w.Indev!.Length;
                want = (width / 3, 20, length / 4);
                if (!w.HasSpawn)
                {
                    try { w.SetSpawn(want.X, want.Y, want.Z); Program.Check(false, $"{name}: a level without a spawn point is refused"); }
                    catch (InvalidOperationException) { Program.Check(File.ReadAllBytes(copy).AsSpan().SequenceEqual(before), $"{name}: keeps no spawn point ({w.Classic?.Format}): refused, the file untouched"); refused++; }
                    continue;
                }
                w.SetSpawn(want.X, want.Y, want.Z);
            }
            using (var w = (MinecraftStudio.Core.Legacy.FiniteWorld)WorldLoader.Open(copy))
            {
                var spawn = w.Classic is { } c ? (c.SpawnX, c.SpawnY, c.SpawnZ) : w.Indev!.Spawn;
                var now = w.Classic?.Blocks ?? w.Indev!.Blocks;
                Program.Check(spawn == want && now.AsSpan().SequenceEqual(blocks), $"{name}: the spawn set to {want} and read back ({spawn}), every block as it was");
                set++;
            }
        }
        Program.Check(set >= 2, $"Classic and Indev: {set} levels' spawn set, {refused} without one refused");
    }

    // Spawn moved, noon, a thunderstorm and (Java) a 1000-block border: saved, reopened, the rest of level.dat as before.
    static void Settings(string label, string path)
    {
        NbtCompound rootBefore;
        int x0;
        long day0, time0;
        using (var w = WorldLoader.Open(path))
        {
            var s = WorldSettings.Read(w);
            if (s.SpawnX is not int sx || s.TimeOfDay == null) { Program.Info($"{label}: keeps no spawn or time"); return; }
            rootBefore = (NbtCompound)Root(w).Clone();
            var data = rootBefore.GetCompound("Data") ?? rootBefore;
            time0 = data.GetLong(s.IsBedrock ? "Time" : "DayTime");
            day0 = (long)Math.Floor(time0 / (double)WorldSettings.Day);
            x0 = sx;
            s.SpawnX = sx + 5;
            s.TimeOfDay = 6000;
            if (s.Weather != null) s.Weather = Weather.Thunder;
            if (s.BorderSize != null) s.BorderSize = 1000;
            // A console save's list shows the save's own title: no name is offered there.
            if (w is MinecraftStudio.Core.LegacyConsole.ConsoleWorld) Program.Check(s.Name == null, $"{label}: no world name offered (the console shows the save's title)");
            else if (s.Name != null) s.Name = "Renamed by the test";
            var done = s.Apply();
            Program.Check(done.Count >= 2, $"{label}: settings changed ({string.Join("; ", done)})");
            WorldFlags.Read(w).Save();
        }
        using (var w = WorldLoader.Open(path))
        {
            var s = WorldSettings.Read(w);
            var data = Root(w).GetCompound("Data") ?? Root(w);
            long time = data.GetLong(s.IsBedrock ? "Time" : "DayTime");
            Program.Check(s.SpawnX == x0 + 5 && s.TimeOfDay == 6000 && (long)Math.Floor(time / (double)WorldSettings.Day) == day0,
                $"{label}: reopened, spawn x {s.SpawnX} (was {x0}), noon ({s.TimeOfDay}), still day {Math.Floor(time / (double)WorldSettings.Day)} ({time0} → {time})");
            if (s.Weather != null) Program.Check(s.Weather == Weather.Thunder, $"{label}: a thunderstorm ({s.Weather})");
            if (s.Name != null)
            {
                string? file = w is MinecraftStudio.Core.Bedrock.BedrockWorld bw && File.Exists(Path.Combine(bw.Folder, "levelname.txt")) ? File.ReadAllText(Path.Combine(bw.Folder, "levelname.txt")) : null;
                Program.Check(s.Name == "Renamed by the test" && w.Name == "Renamed by the test" && (file == null || file == s.Name), $"{label}: renamed (\"{w.Name}\"{(file != null ? $", levelname.txt \"{file}\"" : "")})");
            }
            if (s.BorderSize != null) Program.Check(s.BorderSize == 1000, $"{label}: the border is 1000 wide ({s.BorderSize})");
            var diffs = new List<string>();
            var changed = new HashSet<string> { "LevelName", "SpawnX", "DayTime", "Time", "raining", "thundering", "rainTime", "thunderTime", "clearWeatherTime", "rainLevel", "lightningLevel", "lightningTime", "BorderSize", "BorderSizeLerpTarget", "BorderSizeLerpTime" };
            CompareExcept(rootBefore, Root(w), "", diffs, changed);
            Program.Check(diffs.Count == 0, $"{label}: nothing else in level.dat changed{(diffs.Count > 0 ? ": " + string.Join(", ", diffs.Take(8)) : "")}");
        }
    }

    static void CompareExcept(NbtCompound a, NbtCompound b, string path, List<string> diffs, HashSet<string> skip)
    {
        foreach (var (key, va) in a)
        {
            string p = path.Length == 0 ? key : path + "." + key;
            if (skip.Contains(key) || key is "LastPlayed" or "SizeOnDisk") continue;
            if (b[key] is not NbtTag vb) { diffs.Add(p + " missing"); continue; }
            if (va.Type != vb.Type) { diffs.Add($"{p} {va.Type}→{vb.Type}"); continue; }
            if (va is NbtCompound ca && vb is NbtCompound cb) CompareExcept(ca, cb, p, diffs, skip);
            else if (va.ToString() != vb.ToString()) diffs.Add($"{p} {va}→{vb}");
        }
    }

    static NbtCompound Root(SaveWorld w) => w switch
    {
        JavaWorld j => j.Data ?? throw new InvalidDataException("The test world has no level.dat Data."),
        BedrockWorld b => b.LevelRoot,
        ConsoleWorld c => c.Level!.RootCompound,
        _ => new NbtCompound(),
    };

    static void One(string label, string path)
    {
        List<GameRule> before;
        NbtCompound rootBefore;
        string keep, tick;
        using (var w = WorldLoader.Open(path))
        {
            before = GameRules.Read(w);
            rootBefore = (NbtCompound)Root(w).Clone();
            if (before.Count == 0)
            {
                var data = rootBefore.GetCompound("Data") ?? rootBefore;
                Program.Info($"{label}: keeps no game rules; level.dat has {string.Join(", ", data.Select(kv => $"{kv.Key}:{kv.Value.Type}"))}");
                return;
            }
            var keepRule = before.FirstOrDefault(r => r.Label == "Keep inventory after death");
            var tickRule = before.FirstOrDefault(r => r.Label.StartsWith("Random tick speed"));
            Program.Check(keepRule != null && keepRule.IsBool && tickRule != null && !tickRule.IsBool,
                $"{label}: {before.Count} rules read, keep inventory and random tick speed among them ({string.Join(", ", before.Take(6).Select(r => $"{r.Key}={r.Value}"))})");
            if (keepRule == null || tickRule == null) return;
            keep = keepRule.Key; tick = tickRule.Key;
            keepRule.Value = keepRule.On ? "false" : "true";
            tickRule.Value = "7";
            var done = GameRules.Apply(w, before);
            Program.Check(done.Count == 2, $"{label}: two rules changed ({string.Join("; ", done)})");
            WorldFlags.Read(w).Save();
        }
        using (var w = WorldLoader.Open(path))
        {
            var after = GameRules.Read(w);
            var k = after.First(r => r.Key == keep);
            var t = after.First(r => r.Key == tick);
            var originalKeep = before.First(r => r.Key == keep).Original;
            Program.Check(k.Value != originalKeep && t.Value == "7", $"{label}: reopened, keep inventory is {k.Value} (was {originalKeep}) and random tick speed 7 ({t.Value})");
            var others = before.Where(r => r.Key != keep && r.Key != tick).ToList();
            Program.Check(others.All(o => after.Any(a => a.Key == o.Key && a.Value == o.Original)), $"{label}: the other {others.Count} rules are as they were");
            // Every other value in level.dat: the same, with the same type.
            var rootAfter = Root(w);
            var changedKeys = new List<string>();
            Compare(rootBefore, rootAfter, "", changedKeys, keep, tick);
            Program.Check(changedKeys.Count == 0, $"{label}: nothing else in level.dat changed{(changedKeys.Count > 0 ? ": " + string.Join(", ", changedKeys.Take(8)) : "")}");
        }
    }

    // Differences between two level.dat compounds, besides the two rules changed and what the game itself updates on
    // every save (when it was last played, and the size of the file).
    static void Compare(NbtCompound a, NbtCompound b, string path, List<string> diffs, string keep, string tick)
    {
        foreach (var (key, va) in a)
        {
            string p = path.Length == 0 ? key : path + "." + key;
            if (key == keep || key == tick || key is "LastPlayed" or "SizeOnDisk") continue;
            if (b[key] is not NbtTag vb) { diffs.Add(p + " missing"); continue; }
            if (va.Type != vb.Type) { diffs.Add($"{p} {va.Type}→{vb.Type}"); continue; }
            if (va is NbtCompound ca && vb is NbtCompound cb) Compare(ca, cb, p, diffs, keep, tick);
            else if (va.ToString() != vb.ToString()) diffs.Add($"{p} {va}→{vb}");
        }
        foreach (var (key, _) in b) if (a[key] == null) diffs.Add((path.Length == 0 ? key : path + "." + key) + " added");
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
