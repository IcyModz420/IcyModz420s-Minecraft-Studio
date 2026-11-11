// McTests/CheckTests.cs: "check" — Check for problems finds what was put into copies on purpose, nothing serious in the
// clean samples, and the real converted world that didn't open is fixed by the repaired copy.
// Owns: CheckTests — the Bedrock world that didn't open (TestData/regressions: its 3,307 blank chunks found by the check and
// by the rescue; the repaired copy leaves just those out, keeps every other chunk, and passes); a Java copy with the host
// inside stone, a damaged chunk and a data pack whose load tag names a missing function; a Bedrock copy with the player
// below the world; and every sample edition checked without an error. Copies need some room: run with TEMP on D:.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class CheckTests
{
    public static void Run()
    {
        CrashWorld();
        Java();
        BedrockBelow();
        Samples();
    }

    static void Print(CheckReport r)
    {
        foreach (var p in r.Problems) Program.Info($"    [{CheckReport.SeverityText(p.Severity)}] {p.Title} → {p.Fix}");
    }

    static void CrashWorld()
    {
        Console.WriteLine("== The converted Bedrock world that didn't open");
        string bad = Path.Combine(TestPaths.Data, "regressions", "bedrock-blank-old-chunks");
        if (!Directory.Exists(bad)) { Program.Info("(skipped: not in TestData)"); return; }
        string repaired = Path.Combine(TestPaths.Output, "check-repaired");
        if (Directory.Exists(repaired)) Directory.Delete(repaired, true);
        try
        {
            int before;
            using (var w = WorldLoader.Open(bad))
            {
                before = w.Dimensions.Sum(d => d.ChunkPositions.Count);
                var r = WorldCheck.Run(w);
                Print(r);
                var p = r.Problems.FirstOrDefault(x => x.Severity == CheckSeverity.WontOpen);
                Program.Check(p != null && p.Title.StartsWith("3,307 chunks are stored", StringComparison.Ordinal) && p.Fix == CheckFix.Repair && p.Chunks?.Count == 3307,
                    $"the check finds its 3,307 blank chunks, fixed by a repaired copy ({p?.Title} → {p?.Fix})");
                Program.Check(r.Problems.Count(x => x.Severity == CheckSeverity.WontOpen) == 1, "nothing else stops it opening");
            }
            var findings = new List<string>();
            using (var rescued = WorldRescue.Open(bad, findings))
            {
                Program.Check(findings.Any(f => f.StartsWith("3,307 chunks are blank", StringComparison.Ordinal)), "the rescue names them too");
                var report = WorldRepair.Write(rescued, repaired);
                Program.Check(report.Verified, "the repaired copy passes every check: " + string.Join("; ", report.Checks.Select(c => $"{c.Name} {(c.Passed ? "ok" : "FAILED: " + c.Detail)}")));
                Program.Check(report.Notes.Any(n => n.StartsWith("3,307 blank chunks", StringComparison.Ordinal)), "it says it left them out");
            }
            using (var copy = WorldLoader.Open(repaired))
            {
                Program.Check(BedrockChunkShape.Check((BedrockWorld)copy).Count == 0, "every chunk of the copy is stored the way the game stores it");
                int after = copy.Dimensions.Sum(d => d.ChunkPositions.Count);
                Program.Check(after == before - 3307, $"every other chunk is kept ({after:N0} of {before:N0})");
                var r = WorldCheck.Run(copy);
                Print(r);
                Program.Check(r.Problems.All(x => x.Severity != CheckSeverity.WontOpen), "the check finds nothing in the copy that stops it opening");
            }
        }
        catch (Exception ex) { Program.Check(false, $"threw {ex.GetType().Name}: {ex.Message}"); }
        finally { try { if (Directory.Exists(repaired)) Directory.Delete(repaired, true); } catch (IOException) { } }
    }

    static void Java()
    {
        Console.WriteLine("== A Java copy with a player inside stone, a damaged chunk and a broken data pack");
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) { Program.Info("(skipped: the 1.16.5 sample is not in TestData)"); return; }
        using (var clean = WorldLoader.Open(java))
        {
            var r = WorldCheck.Run(clean);
            Print(r);
            Program.Check(!r.Serious, $"the clean sample: nothing serious ({r.Problems.Count} found, {r.ChunksRead:N0} chunks read)");
        }
        string src = Path.Combine(TestPaths.Output, "check-java");
        if (Directory.Exists(src)) Directory.Delete(src, true);
        try
        {
            CopyDir(java, src);
            string host, region;
            ChunkPos damaged;
            using (var w = WorldLoader.Open(src))
            {
                var player = w.Players.First();
                host = player.Name;
                var ow = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                var reader = new CanonicalReader(w, ow);
                // A column with two whole stone-like blocks one over the other, in a chunk the world has.
                (int X, int Y, int Z)? inside = null;
                foreach (var c in ow.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)))
                {
                    int x = c.X * 16 + 8, z = c.Z * 16 + 8;
                    for (int y = 60; y > 5 && inside == null; y--)
                        if (reader.BlockAt(x, y, z) is { } a && reader.BlockAt(x, y + 1, z) is { } b && Cube(a) && Cube(b)) inside = (x, y, z);
                    if (inside != null) break;
                }
                Program.Check(inside != null, $"a place inside stone to put {host} ({inside})");
                if (inside is not { } spot) return;
                var (ix, iy, iz) = spot;
                HardcoreRestore.MoveAndSave(w, player, new HardcoreRestore.Spot(ix, iy, iz, "inside stone (test)"));
                damaged = ow.ChunkPositions.First(p => p != new ChunkPos(ix >> 4, iz >> 4));
                region = Path.Combine(((JavaRegionDimension)ow).RegionFolder, $"r.{damaged.X >> 5}.{damaged.Z >> 5}.mca");
            }
            // Damaged once the world is closed (it may hold its region files open).
            Damage(region, damaged);
            // A data pack whose load tag names a function of its own namespace that isn't there, turned on in level.dat.
            string pack = Path.Combine(src, "datapacks", "broken");
            Directory.CreateDirectory(Path.Combine(pack, "data", "broken", "functions"));
            Directory.CreateDirectory(Path.Combine(pack, "data", "minecraft", "tags", "functions"));
            File.WriteAllText(Path.Combine(pack, "pack.mcmeta"), "{\"pack\":{\"pack_format\":6,\"description\":\"broken\"}}");
            File.WriteAllText(Path.Combine(pack, "data", "broken", "functions", "other.mcfunction"), "say hello\n");
            File.WriteAllText(Path.Combine(pack, "data", "minecraft", "tags", "functions", "load.json"), "{\"values\":[\"broken:start\"]}");
            var level = NbtFile.Load(Path.Combine(src, "level.dat"));
            var data = level.RootCompound.GetCompound("Data")!;
            if (data.GetCompound("DataPacks") is not { } packs) data["DataPacks"] = packs = new NbtCompound();
            if (packs.GetList("Enabled") is not { } enabled) packs["Enabled"] = enabled = new NbtList(NbtType.String);
            enabled.Add(new NbtString("file/broken"));
            // A mod's item and nothing else of it (a world converted from Bedrock can hold an add-on's items this way):
            // the game drops it when the mod isn't there, so the check has to find it with no block of the mod in sight.
            if (data.GetCompound("Player")?.GetList("Inventory") is { } inventory)
                inventory.Add(new NbtCompound { ["id"] = new NbtString("testmod:gem"), ["Count"] = new NbtByte(1), ["Slot"] = new NbtByte(20) });
            level.Save(Path.Combine(src, "level.dat"));

            using var copy = WorldLoader.Open(src);
            var report = WorldCheck.Run(copy);
            Print(report);
            var stuck = report.Problems.FirstOrDefault(p => p.Severity == CheckSeverity.Stuck && p.Player == host);
            Program.Check(stuck != null && stuck.Title == $"{host} is inside a block" && stuck.Fix == CheckFix.MoveToSafety, $"the player inside stone: {stuck?.Title} → {stuck?.Fix}");
            var lost = report.Problems.FirstOrDefault(p => p.Severity == CheckSeverity.LosesThings && p.Title.Contains("can't be read", StringComparison.Ordinal));
            Program.Check(lost != null && lost.Chunks?.Contains(damaged) == true && lost.Fix == CheckFix.Repair, $"the damaged chunk {damaged}: {lost?.Title} → {lost?.Fix}");
            var broken = report.Problems.FirstOrDefault(p => p.Title.StartsWith("The data pack broken has", StringComparison.Ordinal));
            Program.Check(broken != null && broken.Fix == CheckFix.Packs && broken.Detail.Contains("broken:start", StringComparison.Ordinal), $"the broken data pack: {broken?.Title} — {broken?.Detail}");
            var mod = report.Problems.FirstOrDefault(p => p.Title == "Needs the mod testmod, which is not on this PC");
            Program.Check(mod != null && mod.Severity == CheckSeverity.LosesThings && mod.Detail.StartsWith("1 item comes from it.", StringComparison.Ordinal) && mod.Fix == CheckFix.Mods,
                $"a mod's lone item in the player's inventory: {mod?.Title} — {mod?.Detail[..Math.Min(40, mod.Detail.Length)]}");
        }
        catch (Exception ex) { Program.Check(false, $"threw {ex.GetType().Name}: {ex.Message}"); }
        finally { try { if (Directory.Exists(src)) Directory.Delete(src, true); } catch (IOException) { } }
    }

    static void BedrockBelow()
    {
        Console.WriteLine("== A Bedrock copy with the player below the world");
        string bedrock = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16");
        if (!Directory.Exists(bedrock)) { Program.Info("(skipped: the Bedrock sample is not in TestData)"); return; }
        string src = Path.Combine(TestPaths.Output, "check-bedrock");
        if (Directory.Exists(src)) Directory.Delete(src, true);
        try
        {
            CopyDir(bedrock, src);
            string name;
            using (var w = WorldLoader.Open(src))
            {
                var player = w.Players.First();
                name = player.Name;
                HardcoreRestore.MoveAndSave(w, player, new HardcoreRestore.Spot(0, -100, 0, "below the world (test)"));
            }
            using var copy = WorldLoader.Open(src);
            var report = WorldCheck.Run(copy);
            Print(report);
            var below = report.Problems.FirstOrDefault(p => p.Player == name && p.Title == $"{name} is below the world");
            Program.Check(below != null && below.Fix == CheckFix.MoveToSafety, $"{name} below the world: {below?.Title} → {below?.Fix}");
        }
        catch (Exception ex) { Program.Check(false, $"threw {ex.GetType().Name}: {ex.Message}"); }
        finally { try { if (Directory.Exists(src)) Directory.Delete(src, true); } catch (IOException) { } }
    }

    static void Samples()
    {
        Console.WriteLine("== Every edition's sample");
        foreach (var (label, path) in new[]
        {
            ("Java 1.12.2 Forge", Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_12_2_twilight_forest")),
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
            ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
            ("3DS 1.9", Path.Combine(TestPaths.Data, "3ds", "1.9")),
            ("Pocket 0.x", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")),
            ("Classic", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "Dojo_64_64_128.dat")),
            ("Indev", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "hell.mclevel")),
        })
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            try
            {
                using var w = WorldLoader.Open(path);
                var r = WorldCheck.Run(w);
                Print(r);
                Program.Check(r.ChunksRead > 0 && r.Checked.Count > 0, $"{label}: checked ({r.ChunksRead:N0} chunks, {r.Problems.Count} found: {string.Join(" | ", r.Problems.Select(p => p.Title).Take(4))})");
            }
            catch (Exception ex) { Program.Check(false, $"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    // The checker's own rule for a block a head can't be in.
    static bool Cube(BlockState b) => !b.IsAir && JavaBlockData.For(StructureJava.Newest).Blocks.TryGetValue(b.Name, out var d) && d.IsPlainCube;

    /// <summary>Overwrites a chunk's compressed data in its region file with bytes that don't decompress.</summary>
    static void Damage(string region, ChunkPos c)
    {
        using var f = new FileStream(region, FileMode.Open, FileAccess.ReadWrite);
        var header = new byte[4];
        f.Position = 4 * ((c.X & 31) + (c.Z & 31) * 32);
        f.ReadExactly(header);
        long offset = ((header[0] << 16) | (header[1] << 8) | header[2]) * 4096L;
        f.Position = offset + 5;
        f.Write(Enumerable.Repeat((byte)0xA5, 256).ToArray());
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
