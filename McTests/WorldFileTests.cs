// McTests/WorldFileTests.cs: "worldfiles" — what a Java world keeps beside its chunks, carried over in conversions.
// Owns: WorldFileTests — the 1.16.5 sample (advancements, statistics, a data pack, scoreboard and raids, its icon)
// converted to Java 1.20.4 (everything byte for byte, the data pack order and world border in level.dat), 1.12.2 (only
// what 1.12 reads: advancements, the border; the rest said in the report), 1.8 (the border) and Bedrock (said in the
// report); the 1.12.2 sample upgraded to 1.16.5 (advancements and statistics). Output in the test folder.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class WorldFileTests
{
    public static void Run(string[] args)
    {
        string modern = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        string old = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2");
        string temp = Path.Combine(TestPaths.Output, "worldfiles", DateTime.Now.ToString("HHmmss"));
        if (Directory.Exists(modern) && Directory.Exists(old)) Progress(modern, old, Path.Combine(temp, "progress"));
        foreach (var (source, name, kind, expect) in new (string, string, TargetKind, string)[]
        {
            (modern, "1.20.3 – 1.20.4", TargetKind.Java, "icon advancements stats data datapacks order border"),
            (modern, "1.12 – 1.12.2", TargetKind.Java, "icon advancements border"),
            (modern, "1.8 – 1.8.9", TargetKind.Java, "icon border"),
            (modern, "1.21.120 – 26.x", TargetKind.Bedrock, ""),
            (old, "1.16.2 – 1.16.5", TargetKind.Java, "icon advancements stats border"),
        })
        {
            if (!Directory.Exists(source)) { Console.WriteLine($"   ({source}: not found, skipped)"); continue; }
            var target = TargetCatalog.All.First(t => t.Kind == kind && t.Name.EndsWith(name, StringComparison.Ordinal));
            string label = $"{Path.GetFileName(source)} → {target.Name}";
            string outDir = Path.Combine(temp, string.Concat(label.Where(char.IsLetterOrDigit)));
            try
            {
                ConversionReport report;
                using (var w = WorldLoader.Open(source))
                    report = WorldConverter.Run(w, target, outDir, new ConversionOptions { Dimensions = ["minecraft:overworld"], Area = (-4, -4, 4, 4) }, new Progress<ConversionProgress>(_ => { }));
                string got = kind == TargetKind.Java ? What(source, outDir) : "";
                Program.Check(got == expect, $"{label}: carried over \"{expect}\" (got \"{got}\")");
                // Whatever stayed behind is in the report.
                var behind = report.Losses.Where(l => l.Lost && (l.What is "Advancements" or "Statistics" or "World data" or "Data packs" or "Java world data")).Select(l => l.What).ToList();
                Program.Info($"{label}: left behind (reported): {(behind.Count == 0 ? "nothing" : string.Join(", ", behind))}");
                bool reported = name switch
                {
                    "1.12 – 1.12.2" => behind.Contains("Statistics") && behind.Contains("World data") && behind.Contains("Data packs"),
                    "1.8 – 1.8.9" => behind.Contains("Advancements") && behind.Contains("Statistics") && behind.Contains("Data packs"),
                    "1.21.120 – 26.x" => behind.Contains("Java world data"),
                    _ => behind.Count == 0,
                };
                Program.Check(reported, $"{label}: what stayed behind is in the report");
            }
            catch (Exception ex) { Program.Check(false, $"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
        }
        // Bedrock to Bedrock: the world's picture comes along.
        string bedrock = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16");
        if (Directory.Exists(bedrock))
        {
            var target = TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock && t.Name.EndsWith("1.21.120 – 26.x", StringComparison.Ordinal));
            string outDir = Path.Combine(temp, "bedrock");
            try
            {
                ConversionReport report;
                using (var w = WorldLoader.Open(bedrock))
                    report = WorldConverter.Run(w, target, outDir, new ConversionOptions { Dimensions = ["minecraft:overworld"], Area = (-4, -4, 4, 4) }, new Progress<ConversionProgress>(_ => { }));
                Program.Check(Same(Path.Combine(bedrock, "world_icon.jpeg"), Path.Combine(report.OpenPath ?? outDir, "world_icon.jpeg")), "Bedrock 1.16 → Bedrock 1.21: the world's icon came along");
            }
            catch (Exception ex) { Program.Check(false, $"Bedrock → Bedrock: threw {ex.GetType().Name}: {ex.Message}"); }
        }
        try { Directory.Delete(temp, true); } catch (IOException) { }
    }

    static bool Same(string a, string b) => File.Exists(a) && File.Exists(b) && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    // What came along, byte for byte (and the level.dat fields as they were).
    static string What(string source, string output)
    {
        var parts = new List<string>();
        if (Same(Path.Combine(source, "icon.png"), Path.Combine(output, "icon.png"))) parts.Add("icon");
        bool All(string folder) => Directory.Exists(Path.Combine(source, folder)) && Directory.EnumerateFiles(Path.Combine(source, folder), "*.json")
            .All(f => Same(f, Path.Combine(output, folder, Path.GetFileName(f))));
        if (All("advancements")) parts.Add("advancements");
        if (All("stats")) parts.Add("stats");
        var data = new[] { "scoreboard.dat", "raids.dat" }.Where(f => File.Exists(Path.Combine(source, "data", f))).ToList();
        if (data.Count > 0 && data.All(f => Same(Path.Combine(source, "data", f), Path.Combine(output, "data", f)))) parts.Add("data");
        if (Directory.Exists(Path.Combine(source, "datapacks")) && Directory.EnumerateFiles(Path.Combine(source, "datapacks"))
            .All(f => Same(f, Path.Combine(output, "datapacks", Path.GetFileName(f)))) && Directory.EnumerateFiles(Path.Combine(source, "datapacks")).Any())
            parts.Add("datapacks");
        var src = (NbtFile.Load(Path.Combine(source, "level.dat")).Root as NbtCompound)?.GetCompound("Data");
        var dst = (NbtFile.Load(Path.Combine(output, "level.dat")).Root as NbtCompound)?.GetCompound("Data");
        if (src?.GetCompound("DataPacks") is NbtCompound order && dst?.GetCompound("DataPacks")?.ContentEquals(order) == true && order.GetList("Enabled")?.Count > 1) parts.Add("order");
        if (src != null && dst != null && new[] { "BorderSize", "BorderCenterX", "BorderCenterZ", "BorderDamagePerBlock" }.All(k => src[k] is NbtTag t && dst[k]?.ContentEquals(t) == true)) parts.Add("border");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// A player's statistics and advancements (PlayerProgress) in copies of the 1.16.5 and 1.12.2 samples: read (1.13+ by
    /// category, 1.12's flat names), a statistic changed and an advancement marked not done and done again, saved and read
    /// back — everything else in the files as it was, DataVersion kept; with an installed Minecraft, titles from its jar.
    /// </summary>
    static void Progress(string modern, string old, string temp)
    {
        foreach (var (label, source) in new[] { ("1.16.5", modern), ("1.12.2", old) })
        {
            string copy = Path.Combine(temp, label);
            foreach (var d in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(source, d)));
            foreach (var f in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, Path.GetRelativePath(source, f)))!); File.Copy(f, Path.Combine(copy, Path.GetRelativePath(source, f))); }
            using var w = (MinecraftStudio.Core.Java.JavaWorld)WorldLoader.Open(copy);
            var player = w.Players.First(p => ServerPlayers.UuidOf(p) is Guid g && File.Exists(PlayerProgress.Files(w, g).Stats));
            // The world's own data packs are the catalog here (independent of what this PC has installed).
            var packs = new AdvancementCatalog().AddDataPacks(w.Folder);
            var p0 = PlayerProgress.Read(w, player, packs)!;
            var stats = p0.Stats();
            var advancements = p0.Advancements();
            bool modernStats = label == "1.16.5";
            Program.Check(stats.Count >= (modernStats ? 6 : 3) && (modernStats ? stats.All(s => s.Category.StartsWith("minecraft:", StringComparison.Ordinal)) : stats.All(s => s.Category == "")) && advancements.Count > 0,
                $"{label}: {stats.Count} statistics ({(modernStats ? "by category" : "flat names")}) and {advancements.Count} advancements read");
            var stat = stats.First();
            // One done (1.16.5) or started (the 1.12.2 sample has one advancement, under way).
            var done = advancements.FirstOrDefault(a => a.Done) ?? advancements.First(a => a.CriteriaDone.Count > 0);
            string statsBefore = File.ReadAllText(PlayerProgress.Files(w, ServerPlayers.UuidOf(player)!.Value).Stats);
            p0.SetStat(stat.Category, stat.Name, stat.Value + 1234);
            p0.Revoke(done.Id);
            p0.Save(w.DataVersion ?? 0);
            var p1 = PlayerProgress.Read(w, player, packs)!;
            bool statChanged = p1.Stats().Single(s => s.Category == stat.Category && s.Name == stat.Name).Value == stat.Value + 1234;
            bool othersSame = p1.Stats().Where(s => s.Name != stat.Name || s.Category != stat.Category).Count() == stats.Count - 1;
            bool revoked = !p1.Advancements().Any(a => a.Id == done.Id && a.Done);
            // Marked done again in the same window (it remembers what the advancement takes from when it was read).
            p0.Grant(done.Id);
            p0.Save(w.DataVersion ?? 0);
            var p2 = PlayerProgress.Read(w, player, packs)!;
            var again = p2.Advancements().Single(a => a.Id == done.Id);
            bool keptVersion = !statsBefore.Contains("DataVersion") || File.ReadAllText(PlayerProgress.Files(w, ServerPlayers.UuidOf(player)!.Value).Stats).Contains("DataVersion");
            Program.Check(statChanged && othersSame && revoked && again.Done && again.CriteriaDone.Count == done.CriteriaDone.Count && keptVersion,
                $"{label}: \"{stat.Name}\" set to {stat.Value + 1234:N0}, \"{done.Title}\" marked not done then done again ({again.CriteriaDone.Count} criteria, {again.When}), the rest as it was");
        }
        // Titles and criteria from an installed Minecraft (this PC's own; skipped where none is installed).
        var jars = MinecraftStudio.Core.Packs.GameFolders.JavaJars();
        if (jars.Count == 0) { Program.Info("(no Java Minecraft installed: the advancement titles are not checked)"); return; }
        var catalog = AdvancementCatalog.FromJar(jars[0].Jar, jars[0].Version);
        var root = catalog.Entries.GetValueOrDefault("minecraft:story/root");
        var stone = catalog.Entries.GetValueOrDefault("minecraft:story/mine_stone");
        Program.Check(catalog.Entries.Count > 100 && root?.Title == "Minecraft" && stone?.Title == "Stone Age" && stone.Criteria.Length > 0,
            $"the advancements of Minecraft {catalog.Version}, from its jar: {catalog.Entries.Count} (\"{root?.Title}\", \"{stone?.Title}\")");
    }
}
