// MinecraftStudio.Core/World/WorldCheck.cs: "Check for problems" — what would stop a world opening in its game, crash the
// game or kick its players, lose their things, or leave a player stuck; looked for in any edition, each problem with the
// fix this program has for it.
// Owns: WorldCheck.Run, CheckReport, CheckProblem, CheckSeverity, CheckFix. Only reads. One pass over every chunk finds the
// chunks that can't be read and the lag machines (book and chunk bans among them, LagMachines.Find) and notes blocks from
// mods; then a Bedrock world's chunks are held against how the game stores them (BedrockChunkShape), a console save's
// archive warnings are read, a Java world's mods are matched to this PC's (WorldMods, only when it has blocks from mods),
// the packs it uses are looked for (missing ones; Java data packs with mistakes, PackCheck), a Java world is held against
// the newest game on this PC, and every player: dead in Hardcore, head inside a solid block, in lava or fire, below the
// world, or with no position the game can use.
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

/// <summary>How bad a problem is, the worst first.</summary>
public enum CheckSeverity { WontOpen, Crashes, LosesThings, Stuck, Warning }

/// <summary>What fixes a problem: the window (or step) this program has for it.</summary>
public enum CheckFix { None, Repair, Convert, CleanUp, MoveToSafety, RestoreHardcore, Packs, Mods, CloseGame }

/// <summary>One problem: how bad, what (a line), the details and how to fix it in words, and where (chunks, or a player).</summary>
public sealed record CheckProblem(CheckSeverity Severity, string Title, string Detail, CheckFix Fix,
    string? DimensionId = null, IReadOnlyList<ChunkPos>? Chunks = null, string? Player = null);

public sealed class CheckReport
{
    public string World { get; set; } = "";
    /// <summary>The worst first.</summary>
    public List<CheckProblem> Problems { get; } = [];
    /// <summary>What was looked at, in words, so "nothing found" says where nothing was found.</summary>
    public List<string> Checked { get; } = [];
    public int ChunksRead { get; set; }
    public TimeSpan Elapsed { get; set; }

    /// <summary>A problem worse than a warning: the world won't open, crashes, loses things or a player is stuck.</summary>
    public bool Serious => Problems.Any(p => p.Severity != CheckSeverity.Warning);

    public static string SeverityText(CheckSeverity s) => s switch
    {
        CheckSeverity.WontOpen => "Won't open",
        CheckSeverity.Crashes => "Crashes or kicks",
        CheckSeverity.LosesThings => "Things get lost",
        CheckSeverity.Stuck => "Player stuck",
        _ => "Worth a look",
    };

    public static string FixText(CheckFix f) => f switch
    {
        CheckFix.Repair => "Rescue: save a repaired copy (the world itself is never changed).",
        CheckFix.Convert => "Convert: write it again as a new world (to the same edition and version, or another).",
        CheckFix.CleanUp => "Lag machines: Clean up, into a new copy.",
        CheckFix.MoveToSafety => "Move to safety: the player is put on safe ground (a backup is made first).",
        CheckFix.RestoreHardcore => "Cheats: Restore Hardcore world from death.",
        CheckFix.Packs => "Packs: turn the pack off, or install it.",
        CheckFix.Mods => "Mods and add-ons: which mod it is, and where its things are.",
        CheckFix.CloseGame => "Close the world in the game (or quit the game), then check again.",
        _ => "Nothing here can fix it.",
    };

    /// <summary>The report as text (the window's "Copy the report" and mcstudio check).</summary>
    public string Text()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Check for problems: ").Append(World).Append("\r\n");
        sb.Append(Problems.Count == 0 ? "Nothing found that stops the world opening, crashes it, loses things or leaves a player stuck." : $"{Problems.Count} problem(s) found.").Append("\r\n");
        foreach (var p in Problems)
        {
            sb.Append("\r\n[").Append(SeverityText(p.Severity)).Append("] ").Append(p.Title).Append("\r\n");
            sb.Append("  ").Append(p.Detail).Append("\r\n");
            sb.Append("  Fix: ").Append(FixText(p.Fix)).Append("\r\n");
        }
        sb.Append("\r\nLooked at:\r\n");
        foreach (var c in Checked) sb.Append("  • ").Append(c).Append("\r\n");
        return sb.ToString();
    }
}

public static class WorldCheck
{
    /// <summary>
    /// Looks for everything that stops the world opening, crashes it, loses things or leaves a player stuck. Only reads.
    /// <paramref name="installed"/>: the mods on this PC (found here when not given). <paramref name="shared"/>: the world is
    /// read by a map too, so chunks are read under their dimension's lock.
    /// </summary>
    public static CheckReport Run(SaveWorld world, IReadOnlyList<InstalledMod>? installed = null, IProgress<string>? progress = null,
        CancellationToken cancel = default, bool shared = false)
    {
        var clock = Stopwatch.StartNew();
        var report = new CheckReport { World = world.Name };
        var problems = report.Problems;
        if (WorldUse.InUse(world) is string open) problems.Add(new(CheckSeverity.Warning, "The game has this world open", open, CheckFix.CloseGame));

        // Every chunk once: the ones that can't be read, the lag machines, and anything from a mod — a block, a block entity,
        // a mob, or an item in a chest or on a mob (a Java world converted from Bedrock can hold an add-on's items).
        bool modThings = false;
        var scan = LagMachines.Find(world, world.Dimensions, progress, cancel, shared, alsoEach: (c, _) =>
        {
            if (modThings) return;
            foreach (var s in c.Sections.Values)
                foreach (var b in s.Palette)
                    if (FromMod(b.Name)) { modThings = true; return; }
            modThings = c.BlockEntities.Any(HasModId) || c.Entities.Any(HasModId);
        });
        report.ChunksRead = scan.ChunksRead;
        report.Checked.Add($"Every chunk ({scan.ChunksRead:N0}): whether it can be read, and lag machines, book bans and chunk bans.");
        bool repairable = WorldRepair.CanRepair(world);
        foreach (var (dimId, chunks) in scan.Damaged)
        {
            var dim = world.Dimensions.First(d => d.Id == dimId);
            problems.Add(new(CheckSeverity.LosesThings, $"{WorldRescue.Count(chunks.Count, "chunk", "chunks")} in {dim.DisplayName} can't be read",
                $"Damaged (the first at chunk {chunks[0].X}, {chunks[0].Z}). The game makes a damaged chunk again from the seed, and what was built there is lost — or it won't open the world. "
                + (repairable ? "A repaired copy keeps every chunk that can still be read, and older copies of damaged ones the files still hold, byte for byte."
                              : "Converting it saves everything that can still be read as a new world; the damaged chunks are left out and the game makes them again."),
                repairable ? CheckFix.Repair : CheckFix.Convert, dimId, chunks));
        }
        foreach (var group in scan.Machines.GroupBy(m => (m.Kind, m.DimensionId)))
        {
            var first = group.First();
            var dim = world.Dimensions.FirstOrDefault(d => d.Id == first.DimensionId);
            int n = group.Count();
            string what = first.Kind switch
            {
                LagKind.ChunkBan => "A chunk holds so much that the game can't send it: a player who comes near is thrown out of the world or server.",
                LagKind.BookBan => "A container holds so much item data that a player who opens it — or carries it — is thrown out of the world or server.",
                _ => "Built to make the game lag or crash: " + first.What + ".",
            };
            problems.Add(new(CheckSeverity.Crashes, (n == 1 ? first.Title : $"{n} × {first.Title}") + $" in {dim?.DisplayName ?? first.DimensionId}",
                $"{what} The first at {first.X}, {first.Y}, {first.Z}. Clean up thins or removes it in a new copy, by the same rules that found it, and checks it is gone.",
                CheckFix.CleanUp, first.DimensionId, group.Select(m => new ChunkPos(m.X >> 4, m.Z >> 4)).Distinct().ToList()));
        }

        cancel.ThrowIfCancellationRequested();
        if (world is BedrockWorld bedrock)
        {
            progress?.Report("Looking at how the chunks are stored");
            foreach (var p in BedrockChunkShape.Check(bedrock))
            {
                var byDim = p.Chunks.GroupBy(c => c.Dimension).OrderByDescending(g => g.Count()).First();
                var dim = bedrock.Dimensions.OfType<BedrockDimension>().FirstOrDefault(d => d.Dimension == byDim.Key);
                var chunks = byDim.Select(c => new ChunkPos(c.X, c.Z)).ToList();
                if (p.LostVersion)
                    // The game doesn't see a chunk without its Version: it makes it again over what was built there.
                    problems.Add(new(CheckSeverity.LosesThings, $"{WorldRescue.Count(p.Count, "chunk has", "chunks have")} lost its Version record",
                        $"Their blocks are there, but the record that says what kind of chunk it is was damaged (the first: {p.First}): the game doesn't see "
                        + "these chunks, and makes them again over what was built there. A repaired copy writes it again, as the world's other chunks of their kind have it, so the blocks load "
                        + "(one whose heights were damaged too shows no kind: it is left for the game to make again, and listed as lost).",
                        CheckFix.Repair, dim?.Id, chunks));
                else if (p.Problem == BedrockChunkShape.HeightsOnlyProblem)
                    problems.Add(new(CheckSeverity.Warning, $"{WorldRescue.Count(p.Count, "chunk is", "chunks are")} left with only heights",
                        $"No blocks and no Version record, what damage leaves (the first: {p.First}). Nothing of yours is in them; a repaired copy leaves them out and the game makes them again.",
                        CheckFix.Repair, dim?.Id, chunks));
                else
                    problems.Add(new(CheckSeverity.WontOpen, $"{WorldRescue.Count(p.Count, "chunk is", "chunks are")} stored in a way the game can't open",
                        $"What is wrong: {p.Problem}. The first: {p.First}. "
                        + (p.Blank ? "They hold no blocks: a repaired copy leaves them out, and the game makes them again."
                                   : "Converting it to the same Bedrock version writes every chunk again the way the game stores them."),
                        p.Blank ? CheckFix.Repair : CheckFix.Convert, dim?.Id, chunks));
            }
            report.Checked.Add("How every chunk is stored: the records the game writes for each kind of chunk.");
        }
        if (world is ConsoleWorld console && console.Warnings.Count > 0)
            problems.Add(new(CheckSeverity.LosesThings, "Parts of the save can't be read",
                string.Join(" ", console.Warnings.Take(3)) + " Converting it saves everything that can still be read as a new world (for a console, or any edition).",
                CheckFix.Convert));
        Storage(world, problems, report.Checked, progress, cancel);

        cancel.ThrowIfCancellationRequested();
        Mods(world, modThings, installed, problems, report.Checked, progress, cancel);
        Packs(world, problems, report.Checked, progress);
        GameVersion(world, problems, report.Checked);
        Players(world, problems, report.Checked, progress, shared);

        problems.Sort((a, b) => a.Severity.CompareTo(b.Severity));
        report.Elapsed = clock.Elapsed;
        return report;
    }

    static string? Namespace(string name) => name.IndexOf(':') is int i and > 0 ? name[..i] : null;

    /// <summary>
    /// Below the chunks: a Java world's region tables (a chunk pointing outside its file, two chunks in one place …) and a
    /// Bedrock world's database (tables that don't read, damaged log pieces). What "Check for damage" looked at.
    /// </summary>
    static void Storage(SaveWorld world, List<CheckProblem> problems, List<string> checkedText, IProgress<string>? progress, CancellationToken cancel)
    {
        bool repairable = WorldRepair.CanRepair(world);
        if (world is JavaWorld)
        {
            int files = 0;
            foreach (var dim in world.Dimensions.OfType<JavaRegionDimension>())
            {
                var fatal = new List<string>();
                var minor = new List<string>();
                foreach (var file in dim.RegionFiles)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (files++ % 16 == 0) progress?.Report($"Reading the region tables of {dim.DisplayName}");
                    List<RegionIssue> issues;
                    try { issues = RegionFile.Open(file).Validate(false); }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { fatal.Add($"{Path.GetFileName(file)}: {e.Message}"); continue; }
                    foreach (var issue in issues) (issue.Fatal ? fatal : minor).Add($"{Path.GetFileName(file)} slot {issue.Index}: {issue.Message}");
                }
                if (fatal.Count > 0)
                    problems.Add(new(CheckSeverity.LosesThings, $"{WorldRescue.Count(fatal.Count, "damaged place", "damaged places")} in the region files of {dim.DisplayName}",
                        string.Join("; ", fatal.Take(3)) + (fatal.Count > 3 ? $"; and {fatal.Count - 3} more" : "") + ". The game loses those chunks (it makes them again from the seed). "
                        + "A repaired copy rebuilds every region file from what can still be read, older copies of chunks the files hold included.",
                        CheckFix.Repair, dim.Id));
                if (minor.Count > 0)
                    problems.Add(new(CheckSeverity.Warning, $"Small faults in the region files of {dim.DisplayName}",
                        string.Join("; ", minor.Take(3)) + (minor.Count > 3 ? $"; and {minor.Count - 3} more" : "") + ". The game reads past these; a repaired copy writes the files cleanly.",
                        CheckFix.Repair, dim.Id));
            }
            checkedText.Add($"Region files ({files:N0}): every chunk's place in its file.");
        }
        if (world is BedrockWorld b)
        {
            var warnings = b.Db.Warnings.ToList();
            if (warnings.Count > 0 || b.Db.CorruptLogFragments > 0)
                problems.Add(new(CheckSeverity.LosesThings, "The world's database is damaged",
                    (warnings.Count > 0 ? string.Join("; ", warnings.Take(3)) + (warnings.Count > 3 ? $"; and {warnings.Count - 3} more" : "") + ". " : "")
                    + (b.Db.CorruptLogFragments > 0 ? $"{b.Db.CorruptLogFragments} damaged piece(s) of its log (the last changes before the game stopped). " : "")
                    + "Bedrock may not open it, or loses what it can't read. A repaired copy rebuilds the database from every record that can still be read.",
                    repairable ? CheckFix.Repair : CheckFix.Convert));
            checkedText.Add($"The database: {b.Db.Count:N0} records in {b.Db.Manifest.Files.Count} tables.");
        }
    }

    // ------------------------------------------------------------------ mods and packs

    /// <summary>"41 items", "3 blocks and 2 mobs": what of a mod the world holds, only what there is.</summary>
    static string Things(ModUse m)
    {
        var parts = new List<string>();
        if (m.BlockCount > 0) parts.Add(ModUse.Count(m.BlockCount, "blocks"));
        if (m.BlockEntities.Values.Sum() is long be and > 0 && m.BlockCount == 0) parts.Add(ModUse.Count(be, "block entities"));
        if (m.Entities.Values.Sum() is long mobs and > 0) parts.Add(ModUse.Count(mobs, "mobs"));
        if (m.Items.Values.Sum() is long items and > 0) parts.Add(ModUse.Count(items, "items"));
        if (parts.Count == 0) return m.Biomes.Count > 0 ? ModUse.Count(m.Biomes.Count, "biomes") : "Its dimension";
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + " and " + parts[^1];
    }

    /// <summary>A name from a mod or an add-on (not the game's own, nor the console reader's stand-in "lce").</summary>
    static bool FromMod(string name) => Namespace(name) is string ns && ns is not ("minecraft" or "lce");

    /// <summary>Any "id" in it, at any depth, that comes from a mod: the thing itself, what it carries or holds.</summary>
    static bool HasModId(NbtTag tag)
    {
        switch (tag)
        {
            case NbtCompound c:
                if (c["id"] is NbtString id && FromMod(id.Value)) return true;
                foreach (var (_, v) in c) if (v is NbtCompound or NbtList && HasModId(v)) return true;
                return false;
            case NbtList l:
                foreach (var v in l) if (v is NbtCompound or NbtList && HasModId(v)) return true;
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// A Java world's mods not on this PC: opened without them, the game turns their blocks into air and drops their mobs and
    /// items. Looked for when anything in a chunk, or in a player's inventory, comes from a mod.
    /// </summary>
    static void Mods(SaveWorld world, bool modThings, IReadOnlyList<InstalledMod>? installed, List<CheckProblem> problems, List<string> checkedText,
        IProgress<string>? progress, CancellationToken cancel)
    {
        if (world is not JavaWorld) return;
        modThings = modThings || world.Players.Any(p => HasModId(p.Data));
        if (!modThings) { checkedText.Add("Mods: no block, mob or item in it comes from a mod."); return; }
        progress?.Report("Matching its mods with the mods on this PC");
        var mods = WorldMods.Read(world, installed ?? InstalledMods.Find(WorldMods.NearbyGameFolders(world), cancel), progress, cancel);
        var missing = mods.Found.Where(m => m.Namespace.Length > 0 && m.OnThisPc.Count == 0 && m.Pack == null).ToList();
        foreach (var m in missing)
        {
            // Where it has the most, by the world's own dimension id (the mods report keys chunks by the canonical one).
            var most = m.Chunks.Where(kv => kv.Value.Count > 0).OrderByDescending(kv => kv.Value.Count).FirstOrDefault();
            string? raw = most.Key == null ? null : world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == most.Key)?.Id;
            problems.Add(new(CheckSeverity.LosesThings, $"Needs the mod {m.Title}, which is not on this PC",
                $"{Things(m)} {(Things(m).StartsWith("1 ", StringComparison.Ordinal) && !Things(m).Contains(" and ", StringComparison.Ordinal) ? "comes" : "come")} from it. {mods.Without}",
                CheckFix.Mods, raw, raw != null ? most.Value.Take(4000).ToList() : null));
        }
        checkedText.Add($"Mods: {mods.Counted()} found, matched with the mods on this PC{(missing.Count > 0 ? $" — {missing.Count} not there" : "")}.");
    }

    /// <summary>The packs the world uses: ones not on this PC, and Java data packs with mistakes.</summary>
    static void Packs(SaveWorld world, List<CheckProblem> problems, List<string> checkedText, IProgress<string>? progress)
    {
        if (world is not (JavaWorld or BedrockWorld)) return;
        List<WorldPack> packs;
        try { packs = WorldPacks.Read(world); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            problems.Add(new(CheckSeverity.Warning, "The list of packs the world uses can't be read", e.Message, CheckFix.Packs));
            return;
        }
        var on = packs.Where(p => p.Active && !p.BuiltIn).ToList();
        foreach (var p in on)
        {
            string name = p.Name.Length > 0 ? p.Name : p.Id;
            if (p.Location == null)
            {
                problems.Add(world is BedrockWorld
                    ? new(CheckSeverity.LosesThings, $"Uses the pack {name}, which is not on this PC",
                        "Without it Bedrock can't show the add-on's blocks and mobs (and what the pack changes is missing). Install the pack, or put the world on a device that has it.", CheckFix.Packs)
                    : new(CheckSeverity.Warning, $"The data pack {name} is turned on, but its file is gone", "The game leaves it out when the world opens.", CheckFix.Packs));
                continue;
            }
            if (p.Kind != WorldPackKind.DataPack) continue;
            progress?.Report("Checking the data pack " + name);
            PackReport check;
            try { check = PackCheck.Run(p.Location); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
            var mistakes = check.Findings.Where(f => f.Severity == PackSeverity.Problem).ToList();
            if (mistakes.Count == 0) continue;
            problems.Add(new(CheckSeverity.Warning, $"The data pack {name} has {WorldRescue.Count(mistakes.Count, "mistake", "mistakes")}",
                $"{mistakes[0].Message}{(mistakes[0].File != null ? $" ({mistakes[0].File})" : "")}{(mistakes.Count > 1 ? $" — and {mistakes.Count - 1} more (Check a pack lists them all)" : "")}. A data pack with mistakes can stop the world loading (the game then offers Safe Mode, without data packs) or leave parts of it not working.",
                CheckFix.Packs));
        }
        checkedText.Add(on.Count == 0 ? "Packs: the world uses none of its own." : $"Packs: the {on.Count} it uses are on this PC{(world is JavaWorld ? ", and its data packs checked for mistakes" : "")}, or listed above.");
    }

    /// <summary>A Java world saved by a newer game than any on this PC: an older game can lose what it doesn't know.</summary>
    static void GameVersion(SaveWorld world, List<CheckProblem> problems, List<string> checkedText)
    {
        if (world is not JavaWorld java || java.DataVersion is not int dv) return;
        var jars = GameFolders.JavaJars().Select(j => (j.Version, Data: JarDataVersion(j.Jar))).Where(j => j.Data > 0).ToList();
        if (jars.Count == 0) return;
        var newest = jars.MaxBy(j => j.Data);
        string saved = java.Data?.GetCompound("Version")?.GetString("Name") ?? $"data version {dv}";
        if (dv > newest.Data)
            problems.Add(new(CheckSeverity.LosesThings, $"Saved by a newer game ({saved}) than any on this PC",
                $"The newest Java Edition on this PC is {newest.Version}. Play it with {saved} or newer: an older game can't read what is newer, and loses it when it saves. Or convert it to {newest.Version}.",
                CheckFix.Convert));
        checkedText.Add($"Game version: saved by {saved}; the newest on this PC is {newest.Version}.");
    }

    /// <summary>The data version a game jar makes worlds with (version.json's world_version), or 0.</summary>
    static int JarDataVersion(string jar)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jar);
            if (zip.GetEntry("version.json") is not { Length: > 0 and < 1 << 20 } entry) return 0;
            using var s = entry.Open();
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.TryGetProperty("world_version", out var v) && v.TryGetInt32(out int n) ? n : 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return 0; }
    }

    // ------------------------------------------------------------------ players

    /// <summary>
    /// Every player record the world keeps (as the Players page names them): dead in Hardcore, head inside a solid block,
    /// in lava or fire, below the world, or with no usable position. Each is looked at in its own dimension (a mod's too);
    /// one whose dimension the world doesn't have is left alone.
    /// </summary>
    static void Players(SaveWorld world, List<CheckProblem> problems, List<string> checkedText, IProgress<string>? progress, bool shared)
    {
        progress?.Report("Looking at the players");
        bool canMove = HardcoreRestore.CanMove(world);
        var move = canMove ? CheckFix.MoveToSafety : CheckFix.None;
        string moveText = canMove ? "" : " (This edition's players can't be moved here.)";
        List<PlayerRecord> players;
        try { players = world.Players.ToList(); }
        catch (Exception e) when (e is IOException or InvalidDataException or IO.SaveFormatException)
        {
            problems.Add(new(CheckSeverity.Warning, "The players can't be read", e.Message, CheckFix.None));
            return;
        }
        var dead = HardcoreRestore.Dead(world);
        foreach (var p in dead)
            problems.Add(new(CheckSeverity.Stuck, $"{p.Name} died in this Hardcore world",
                "They can only watch. Restore Hardcore world from death brings them back in Survival, with full health, on safe ground beside their bed or the world spawn.",
                CheckFix.RestoreHardcore, Player: p.Name));
        // Where Pos is: the feet (Java, consoles) or the eyes (Bedrock, Pocket, Indev — and the 3DS, whose player is Bedrock's
        // form; HardcoreRestore leaves it out only because a 3DS player is never moved).
        double eyeHeight = world is ThreeDsWorld ? 1.62 : HardcoreRestore.EyeHeight(world);
        var readers = new Dictionary<string, CanonicalReader>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // A single-player Java world keeps the host in level.dat and in playerdata: level.dat's first, the copy left out.
        foreach (var p in players.OrderBy(p => p.Source.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (dead.Contains(p) || WorldFlags.PlayerGameMode(world, p.Data) == WorldFlags.Spectator) continue;   // spectators go through blocks
            if (p.Data.GetList("Pos") is not { Count: >= 3 } pos) continue;
            double x = pos[0].AsDouble, feet = pos[1].AsDouble - eyeHeight, z = pos[2].AsDouble;
            if (!double.IsFinite(x) || !double.IsFinite(feet) || !double.IsFinite(z))
            {
                problems.Add(new(CheckSeverity.Stuck, $"{p.Name} has no position the game can use", "Their position is not a number: the game can't put them anywhere." + moveText, move, Player: p.Name));
                continue;
            }
            if (DimensionOf(world, p.Data) is not { } dim) continue;
            int bx = (int)Math.Floor(x), by = (int)Math.Floor(feet), bz = (int)Math.Floor(z);
            if (!seen.Add($"{Uuid(p.Data)}|{dim.Id}|{bx}|{by}|{bz}")) continue;
            var (min, _) = StructurePlacement.Height(world, CanonicalReader.DimensionId(dim));
            string at = $"{bx}, {by}, {bz} in {dim.DisplayName}";
            var chunk = new ChunkPos(bx >> 4, bz >> 4);
            if (feet < min)
            {
                problems.Add(new(CheckSeverity.Stuck, $"{p.Name} is below the world",
                    $"At {at}, under the bottom of the world (y {min}): they fall into the void as soon as it opens." + moveText, move, dim.Id, [chunk], p.Name));
                continue;
            }
            if (!readers.TryGetValue(dim.Id, out var reader)) readers[dim.Id] = reader = new CanonicalReader(world, dim);
            BlockState? eye, standing;
            if (shared) Monitor.Enter(dim);
            try
            {
                eye = reader.BlockAt(bx, (int)Math.Floor(feet + 1.62), bz);
                standing = reader.BlockAt(bx, (int)Math.Floor(feet + 0.05), bz);
            }
            finally { if (shared) Monitor.Exit(dim); }
            if (eye is { } e && SolidCube(e))
                problems.Add(new(CheckSeverity.Stuck, $"{p.Name} is inside a block",
                    $"Their head is in {Nice(e)} at {at}: they suffocate as soon as the world opens." + moveText, move, dim.Id, [chunk], p.Name));
            else if ((Burning(standing) ?? Burning(eye)) is string burning)
                problems.Add(new(CheckSeverity.Stuck, $"{p.Name} stands in {burning}",
                    $"At {at}: they burn as soon as the world opens." + moveText, move, dim.Id, [chunk], p.Name));
        }
        checkedText.Add(players.Count == 0 ? "Players: the world keeps none." : $"Players ({players.Count}): where each stands, in their own dimension, and whether a Hardcore player has died.");
    }

    /// <summary>
    /// The dimension a player record is in, by the edition's own field: Java and the consoles' Dimension (0, -1, 1, a
    /// Forge number → DIM7, or a namespaced id), Bedrock's, the 3DS's and Pocket's DimensionId (0, 1, 2); a world with one
    /// dimension (Indev) has it there. Null when the world doesn't have that dimension.
    /// </summary>
    static WorldDimension? DimensionOf(SaveWorld world, NbtCompound p)
    {
        if (world.Dimensions.Count == 1) return world.Dimensions[0];
        string? id = world is BedrockWorld or ThreeDsWorld or PocketLegacyWorld
            ? (p.GetLongOrNull("DimensionId") ?? 0) switch { 0 => "minecraft:overworld", 1 => "minecraft:the_nether", 2 => "minecraft:the_end", _ => null }
            : p["Dimension"] switch
            {
                NbtString s => s.Value.Contains(':') ? s.Value : "minecraft:" + s.Value,
                { IsNumeric: true } n => n.AsLong switch { 0 => "minecraft:overworld", -1 => "minecraft:the_nether", 1 => "minecraft:the_end", var k => "DIM" + k },
                _ => "minecraft:overworld",
            };
        return id == null ? null : world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == id || string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The player's UUID in whatever form the record keeps it (to tell a host's two copies from two players).</summary>
    static string Uuid(NbtCompound p) =>
        p["UUID"] is { } u ? u.ToString() ?? "" : p.GetLongOrNull("UUIDMost") is long most ? $"{most}:{p.GetLongOrNull("UUIDLeast")}" : "";

    /// <summary>
    /// A whole solid opaque block with nothing to it (stone, dirt, ores, planks, sand …): a head inside one suffocates.
    /// Blocks with room in them or other shapes (slabs, glass, leaves, cauldrons) don't count, so a player standing
    /// where the game lets them stand is never called stuck.
    /// </summary>
    static bool SolidCube(BlockState b) => !b.IsAir && JavaBlockData.For(StructureJava.Newest).Blocks.TryGetValue(b.Name, out var def) && def.IsPlainCube;

    static string? Burning(BlockState? b) => b?.BaseName switch { "lava" => "lava", "fire" or "soul_fire" => "fire", _ => null };

    static string Nice(BlockState b) => b.BaseName.Replace('_', ' ');
}
