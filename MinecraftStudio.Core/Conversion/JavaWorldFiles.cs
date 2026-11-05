// MinecraftStudio.Core/Conversion/JavaWorldFiles.cs: what a Java world keeps beside its chunks, players and level.dat,
// carried over when it is converted to Java.
// Owns: JavaWorldFiles.Copy — the world's icon; each player's advancements (1.12+) and statistics (the same format, or
// upgrading across 1.13); the scoreboard, command storage, raids and random sequences (to the same or a newer version);
// the data packs with their order (1.13+); and level.dat's world border, locked difficulty, dragon fight, boss bars and
// wandering-trader timers (where the target has them). Files go byte for byte (each has its own DataVersion; the game
// upgrades it); what stays behind is said in the report.
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static class JavaWorldFiles
{
    // level.dat fields carried as they are, with the first data version having them (0: every Anvil version from 1.8).
    static readonly (string Key, int Since)[] LevelFields =
    [
        ("BorderCenterX", 0), ("BorderCenterZ", 0), ("BorderSize", 0), ("BorderSafeZone", 0), ("BorderWarningBlocks", 0), ("BorderWarningTime", 0),
        ("BorderSizeLerpTarget", 0), ("BorderSizeLerpTime", 0), ("BorderDamagePerBlock", 0), ("DifficultyLocked", 0), ("clearWeatherTime", 0),
        ("WanderingTraderSpawnDelay", 1952), ("WanderingTraderSpawnChance", 1952),
    ];

    // level.dat fields whose layout changed over the versions: only to the same or a newer version.
    static readonly (string Key, int Since)[] NewerOnly = [("DragonFight", 2566), ("CustomBossEvents", 1451)];

    static readonly string[] DataFiles = ["scoreboard.dat", "raids.dat", "random_sequences.dat"];

    public static void Copy(JavaWorld source, string output, ConversionTarget target, ConversionOptions options, ConversionLog log)
    {
        string from = source.Folder;
        if (target.Kind != TargetKind.Java)
        {
            // The other editions keep none of these.
            var behind = new List<string>();
            if (PlayerFiles(from, "advancements").Count > 0) behind.Add("advancements");
            if (PlayerFiles(from, "stats").Count > 0) behind.Add("statistics");
            if (Directory.Exists(Path.Combine(from, "datapacks")) && Directory.EnumerateFileSystemEntries(Path.Combine(from, "datapacks")).Any()) behind.Add("data packs");
            if (File.Exists(Path.Combine(from, "data", "scoreboard.dat"))) behind.Add("the scoreboard");
            if (behind.Count > 0)
                log.Add(LossKind.World, "Java world data", $"not converted ({string.Join(", ", behind)}): {target.Name} keeps no such thing", lost: true, where: "");
            return;
        }
        if (!Directory.Exists(output)) return;
        int sourceDv = source.Data?.GetInt("DataVersion") ?? 0, targetDv = target.DataVersion;
        bool sameOrNewer = targetDv >= sourceDv;
        log.Where = "world files";
        // The world's picture in the worlds list.
        if (File.Exists(Path.Combine(from, "icon.png"))) CopyFile(Path.Combine(from, "icon.png"), Path.Combine(output, "icon.png"));

        // Each player's advancements and statistics, by UUID (they go where the players go).
        foreach (var (folder, what, can, why) in new (string, string, bool, string)[]
        {
            ("advancements", "Advancements", targetDv >= 1139, "advancements came in Java 1.12"),
            ("stats", "Statistics", sourceDv < 1451 || targetDv >= 1451, "statistics are kept differently before Java 1.13"),
        })
        {
            var files = PlayerFiles(from, folder);
            if (files.Count == 0) continue;
            if (!options.ConvertPlayers) { log.Add(LossKind.Player, what, $"of {files.Count} player(s) not converted: players are turned off in the options", files.Count, lost: true, where: ""); continue; }
            if (!can) { log.Add(LossKind.Player, what, $"of {files.Count} player(s) left behind: {why}", files.Count, lost: true, where: ""); continue; }
            Directory.CreateDirectory(Path.Combine(output, folder));
            foreach (var f in files) CopyFile(f, Path.Combine(output, folder, Path.GetFileName(f)));
            log.Add(LossKind.Player, what, $"of {files.Count} player(s) carried over as they were", files.Count, where: "");
        }

        // The scoreboard, command storage, raids and random sequences.
        string data = Path.Combine(from, "data");
        var kept = Directory.Exists(data)
            ? Directory.EnumerateFiles(data, "*.dat").Where(f => DataFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                || Path.GetFileName(f).StartsWith("command_storage_", StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        if (kept.Count > 0)
        {
            string names = string.Join(", ", kept.Select(Path.GetFileName));
            if (sameOrNewer)
            {
                Directory.CreateDirectory(Path.Combine(output, "data"));
                foreach (var f in kept) CopyFile(f, Path.Combine(output, "data", Path.GetFileName(f)));
                log.Add(LossKind.World, "World data", $"carried over as it was ({names})", kept.Count, where: "");
            }
            else log.Add(LossKind.World, "World data", $"left behind ({names}): it is from a newer version than {target.Name}", kept.Count, lost: true, where: "");
        }

        // Data packs, in their order.
        string packs = Path.Combine(from, "datapacks");
        var packEntries = Directory.Exists(packs) ? Directory.EnumerateFileSystemEntries(packs).ToList() : [];
        if (packEntries.Count > 0)
        {
            if (targetDv >= 1451)
            {
                CopyTree(packs, Path.Combine(output, "datapacks"));
                log.Add(LossKind.World, "Data packs", $"{packEntries.Count} carried over as they were (one made for another version shows as such in the game)", packEntries.Count, where: "");
            }
            else log.Add(LossKind.World, "Data packs", $"{packEntries.Count} left behind: data packs came in Java 1.13", packEntries.Count, lost: true, where: "");
        }

        // level.dat: the world border and the rest the canonical model doesn't keep.
        if (source.Data is NbtCompound src && (targetDv >= 100 || target.LegacyLevel >= LegacyLevel.Release_1_8) && File.Exists(Path.Combine(output, "level.dat")))
        {
            var file = NbtFile.Load(Path.Combine(output, "level.dat"));
            if (file.Root is NbtCompound root && root.GetCompound("Data") is NbtCompound d)
            {
                bool changed = false;
                foreach (var (key, since) in LevelFields)
                    if (targetDv >= since && src[key] is NbtTag t) { d[key] = t.Clone(); changed = true; }
                foreach (var (key, since) in NewerOnly)
                    if (targetDv >= since && sameOrNewer && src[key] is NbtTag t) { d[key] = t.Clone(); changed = true; }
                // Which data packs are on, and in what order (only the ones that came along, and vanilla).
                if (targetDv >= 1451 && src.GetCompound("DataPacks") is NbtCompound order)
                {
                    d["DataPacks"] = order.Clone();
                    changed = true;
                }
                if (changed) file.Save(Path.Combine(output, "level.dat"));
            }
        }
    }

    /// <summary>A world's per-player JSON files in a folder (named by UUID), in the classic place or under players/ (26.1).</summary>
    static List<string> PlayerFiles(string world, string folder)
    {
        foreach (var dir in new[] { Path.Combine(world, folder), Path.Combine(world, "players", folder) })
            if (Directory.Exists(dir))
                return Directory.EnumerateFiles(dir, "*.json").Where(f => Guid.TryParse(Path.GetFileNameWithoutExtension(f), out _)).ToList();
        return [];
    }

    static void CopyFile(string from, string to)
    {
        if (!File.Exists(to)) File.Copy(from, to);
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) CopyFile(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
