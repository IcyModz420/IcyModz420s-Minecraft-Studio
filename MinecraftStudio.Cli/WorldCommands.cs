// MinecraftStudio.Cli/WorldCommands.cs: the commands that read a world — info, targets, convert, find, replace, crop,
// biome, maps, share, move.
// Owns: WorldCommands. Each opens the world read-only; convert, replace, biome, move and share write NEW folders or files
// (checked by the same code the app uses); maps writes PNGs into a folder (never over a file that is there).
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Cli;

static class WorldCommands
{
    public static int Info(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        foreach (var (k, v) in w.Summary()) Console.WriteLine($"{k}: {v}");
        foreach (var d in w.Dimensions) Console.WriteLine($"Dimension {CanonicalReader.DimensionId(d)} ({d.DisplayName}): {d.ChunkPositions.Count:N0} chunks");
        foreach (var p in w.Players) Console.WriteLine($"Player: {p.Name}");
        return 0;
    }

    public static int Targets(Args a)
    {
        foreach (var t in TargetCatalog.All) Console.WriteLine($"{t.Kind,-14} {t.Name}");
        return 0;
    }

    /// <summary>A target by its name, the end of it ("26.1", "1.12 – 1.12.2", "Bedrock 1.21.120 – 26.x"), or a version inside its range.</summary>
    internal static ConversionTarget Target(string name)
    {
        var all = TargetCatalog.All;
        var exact = all.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(t => t.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(t => t.Name.Replace("–", "-").EndsWith(name.Replace("–", "-"), StringComparison.OrdinalIgnoreCase));
        return exact ?? throw new UsageException($"No target called \"{name}\". Run mcstudio targets to see them.");
    }

    public static int Convert(Args a)
    {
        string source = a.Require(0, "the world");
        var target = Target(a.Option("to") ?? throw new UsageException("Say what to convert to: --to <target>."));
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the new world goes: --out <new folder>."));
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException($"{output} is not a new, empty folder.");
        var options = new ConversionOptions();
        if (a.Option("dims") is string dims) options.Dimensions = [.. dims.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        if (a.Option("area") is string area)
        {
            var n = Numbers(area, 4, "--area x1,z1,x2,z2 (chunk coordinates)");
            options.Area = (Math.Min(n[0], n[2]), Math.Min(n[1], n[3]), Math.Max(n[0], n[2]), Math.Max(n[1], n[3]));
        }
        // --blocks "thermal:ore=copper_ore;*_log=oak_log": what blocks become as it converts (the Replace window's rules).
        if (a.Option("blocks") is string blocks)
        {
            options.BlockRules = blocks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => r.Split('=', 2) is [var from, var to] ? new ReplaceRule(from.Trim(), to.Trim()) : throw new UsageException($"A block rule is pattern=block, not \"{r}\"."))
                .ToList();
            BlockReplace.Compile(options.BlockRules);
        }
        using var w = WorldLoader.Open(source);
        Console.WriteLine($"{w.Name}: {w.FormatDescription} → {target.Name}");
        string last = "";
        var report = WorldConverter.Run(w, target, output, options, new Progress<ConversionProgress>(p =>
        {
            if (p.Stage != last) { last = p.Stage; Console.WriteLine("  " + p.Stage); }
        }));
        return PrintReport(report, output);
    }

    /// <summary>What a conversion wrote, its notes, losses and checks; 0 when every check passed and nothing was lost, else 1.</summary>
    internal static int PrintReport(ConversionReport report, string output)
    {
        Console.WriteLine($"Wrote {report.Chunks:N0} chunks in {report.Elapsed.TotalSeconds:0.0} s to {report.OpenPath ?? output}");
        foreach (var n in report.Notes) Console.WriteLine("  note: " + n);
        foreach (var l in report.Losses) Console.WriteLine("  " + (l.Lost ? "lost: " : "changed: ") + l);
        foreach (var c in report.Checks) Console.WriteLine($"  {(c.Passed ? "✓" : "✗")} {c.Name}: {c.Detail}");
        foreach (var i in report.Issues) Console.WriteLine("  issue: " + i);
        bool ok = report.FailedChunks == 0 && (report.Checks.Count == 0 || report.Verified);
        return ok && !report.AnythingLost ? 0 : 1;
    }

    public static int Find(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string query = a.Require(1, "what to find");
        var dims = Dimensions(a, w);
        long found = 0;
        foreach (var dim in dims)
        {
            var r = WorldSearch.Run(w, dim, new SearchOptions { Query = query, WholeName = a.Has("whole"), Players = dim == dims[0] });
            Console.WriteLine($"== {CanonicalReader.DimensionId(dim)}: {r.ChunksRead:N0} chunks read");
            foreach (var (name, count) in r.BlockCounts.Concat(r.ItemCounts).Concat(r.EntityCounts).OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {count,10:N0}  {name}");
            if (r.TextCount > 0) Console.WriteLine($"  {r.TextCount,10:N0}  texts");
            foreach (var h in r.Hits.Take(200)) Console.WriteLine($"  {h.X}, {h.Y}, {h.Z}  {h.What}  ({h.Where})");
            if (r.Hits.Count > 200 || r.Cut) Console.WriteLine("  … (the first 200 places are listed; the counts above are complete)");
            found += r.Hits.Count;
        }
        return 0;
    }

    /// <summary>The dimension asked for (--dim), or every one (a world with none has nothing to look through).</summary>
    static List<WorldDimension> Dimensions(Args a, SaveWorld w)
    {
        if (a.Option("dim") is not string d) return w.Dimensions.ToList();
        var dims = w.Dimensions.Where(x => CanonicalReader.DimensionId(x) == d || x.Id == d).ToList();
        if (dims.Count == 0) throw new UsageException($"The world has no dimension {d}. Its dimensions: {(w.Dimensions.Count == 0 ? "none" : string.Join(", ", w.Dimensions.Select(CanonicalReader.DimensionId)))}.");
        return dims;
    }

    public static int Structures(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        var dims = Dimensions(a, w);
        var scan = StructureMap.Find(w, dims);
        string? kind = a.Option("kind")?.ToLowerInvariant();
        var sites = scan.Sites.Where(s => kind == null || s.Key == kind || s.Key.EndsWith(":" + kind, StringComparison.Ordinal)).ToList();
        foreach (var dim in dims)
        {
            var here = sites.Where(s => s.DimensionId == dim.Id).ToList();
            if (here.Count == 0) continue;
            Console.WriteLine($"== {dim.DisplayName}: {here.Count:N0} structures");
            foreach (var s in here)
                Console.WriteLine($"  {s.Name,-28} {s.X,7}, {s.Y,4}, {s.Z,7}" + (s.Unlooted > 0 ? $"   {s.Unlooted} not looted yet" : "") + $"   ({string.Join("; ", s.Notes)})");
        }
        foreach (var warning in scan.Warnings) Console.WriteLine("  note: " + warning);
        Console.WriteLine($"{sites.Count:N0} structures in {scan.ChunksRead:N0} chunks"
            + string.Concat(sites.GroupBy(s => StructureKinds.Name(s.Key)).OrderByDescending(g => g.Count()).Take(8).Select((g, i) => (i == 0 ? ": " : ", ") + $"{g.Count()} × {g.Key}")) + ".");
        return 0;
    }

    public static int Lag(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        var dims = Dimensions(a, w);
        var scan = LagMachines.Find(w, dims);
        foreach (var m in scan.Machines)
        {
            Console.WriteLine($"{m.Title} — {dims.First(x => x.Id == m.DimensionId).DisplayName} {m.X}, {m.Y}, {m.Z}");
            Console.WriteLine($"  {m.What}");
            foreach (var e in m.Evidence) Console.WriteLine($"  · {e}");
        }
        foreach (var warning in scan.Warnings) Console.WriteLine("  note: " + warning);
        Console.WriteLine($"{(scan.Machines.Count == 0 ? "No" : scan.Machines.Count.ToString("N0"))} lag machine{(scan.Machines.Count == 1 ? "" : "s")} in {scan.ChunksRead:N0} chunks.");
        if (!a.Has("clean") || scan.Machines.Count == 0) return scan.Machines.Count == 0 ? 0 : 1;

        // --clean: every machine found taken out of a new copy (one dimension at a time).
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the cleaned copy goes: --out <new folder>."));
        var inDims = scan.Machines.Select(m => m.DimensionId).Distinct().ToList();
        if (inDims.Count > 1) throw new UsageException($"Lag machines were found in {inDims.Count} dimensions: clean up one at a time with --dim ({string.Join(", ", inDims.Select(id => CanonicalReader.DimensionId(w.Dimensions.First(d => d.Id == id))))}).");
        var r = LagMachines.CleanIntoCopy(w, scan.Machines, output);
        foreach (var n in r.Notes) Console.WriteLine("  " + n);
        foreach (var p in r.Problems) Console.WriteLine("  ✗ " + p);
        Console.WriteLine($"Cleaned up {scan.Machines.Count:N0} lag machine{(scan.Machines.Count == 1 ? "" : "s")} in {r.OpenPath} ({r.ChunksChanged:N0} chunks rewritten){(r.Verified ? ", checked: none is found there again" : "")}.");
        return r.Verified ? 0 : 1;
    }

    /// <summary>mods: the mods, add-ons and data packs a world holds things of; exit 1 when one of them is on neither this PC nor in the world.</summary>
    public static int Mods(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        var installed = w is Core.Java.JavaWorld ? Core.Packs.InstalledMods.Find(WorldMods.NearbyGameFolders(w)) : [];
        var report = WorldMods.Read(w, installed);
        Console.Write(report.Text(a.Has("all")));
        var missing = report.Found.Where(m => m.Namespace.Length > 0 && m.OnThisPc.Count == 0 && m.Pack == null).ToList();
        if (!report.Bedrock && missing.Count > 0)
            Console.WriteLine($"\r\nNot found on this PC: {string.Join(", ", missing.Select(m => m.Namespace))}.");
        return !report.Bedrock && missing.Count > 0 ? 1 : 0;
    }

    public static int Check(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        var report = WorldCheck.Run(w);
        Console.Write(report.Text());
        return report.Serious ? 1 : 0;
    }

    public static int Restore(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        using var backup = WorldLoader.Open(a.Require(1, "the backup to bring chunks back from"));
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the copy goes: --out <new folder>."));
        string dim = a.Option("dim") ?? "minecraft:overworld";
        var here = w.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dim) ?? throw new UsageException($"The world has no dimension {dim}.");
        var there = StructurePlacement.MatchingDimension(backup, dim) ?? throw new UsageException($"The backup has no dimension {dim}.");
        List<ChunkPos> chunks;
        if (a.Option("area") is string area)
        {
            var n = Numbers(area, 4, "--area x1,z1,x2,z2");
            chunks = [];
            for (int cz = Math.Min(n[1], n[3]) >> 4; cz <= Math.Max(n[1], n[3]) >> 4; cz++)
                for (int cx = Math.Min(n[0], n[2]) >> 4; cx <= Math.Max(n[0], n[2]) >> 4; cx++) chunks.Add(new ChunkPos(cx, cz));
        }
        else
        {
            // Every chunk that differs from the backup (and that the backup has).
            chunks = WorldCompare.Run(w, here, backup, there).Chunks.Where(c => c.Presence != ChunkPresence.OnlyFirst).Select(c => c.Pos).ToList();
            if (chunks.Count == 0) { Console.WriteLine("No chunk differs from the backup: there is nothing to bring back."); return 0; }
        }
        var r = StructurePlacement.RestoreIntoCopy(w, backup, dim, chunks, output, a.Has("keep-new-mobs"));
        foreach (var n in r.Notes) Console.WriteLine("  note: " + n);
        foreach (var p in r.Problems) Console.WriteLine("  ✗ " + p);
        Console.WriteLine($"Brought {r.ChunksChanged:N0} chunk{(r.ChunksChanged == 1 ? "" : "s")} back from {backup.Name} into {r.OpenPath}{(r.Verified ? ", checked" : "")}.");
        return r.Verified ? 0 : 1;
    }

    public static int Replace(Args a)
    {
        string source = a.Require(0, "the world");
        var rules = new List<ReplaceRule> { new(a.Require(1, "the blocks to replace"), a.Require(2, "the block to put there")) };
        var box = Numbers(a.Option("box") ?? throw new UsageException("Say where: --box x1,y1,z1,x2,y2,z2."), 6, "--box x1,y1,z1,x2,y2,z2");
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the copy goes: --out <new folder>."));
        using var w = WorldLoader.Open(source);
        string dim = a.Option("dim") ?? "minecraft:overworld";
        var (result, summary) = BlockReplace.ReplaceIntoCopy(w, dim, Math.Min(box[0], box[3]), Math.Min(box[1], box[4]), Math.Min(box[2], box[5]),
            Math.Max(box[0], box[3]), Math.Max(box[1], box[4]), Math.Max(box[2], box[5]), rules, output);
        foreach (var c in summary.Changes) Console.WriteLine($"  {c.Count,10:N0}  {c.From} → {c.To}");
        foreach (var n in result.Notes) Console.WriteLine("  note: " + n);
        foreach (var p in result.Problems) Console.WriteLine("  ✗ " + p);
        Console.WriteLine($"Replaced {summary.Total:N0} blocks in {result.OpenPath} ({result.ChunksChanged:N0} chunks rewritten){(result.Verified ? ", checked" : "")}.");
        return result.Verified ? 0 : 1;
    }

    public static int Move(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string who = a.Require(1, "the player (a name, a UUID, or host)");
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the copy goes: --out <new folder>."));
        var found = FindPlayers(w, who);
        if (found.Count == 0)
            throw new UsageException($"The world has no player \"{who}\". Its players: {(w.Players.Count == 0 ? "none" : string.Join(", ", w.Players.Select(p => p.Name)))}.");
        Func<SaveWorld, PlayerRecord, HardcoreRestore.Spot?> pick;
        if (a.Option("near") is string near)
        {
            var xz = Numbers(near, 2, "--near x,z");
            pick = (copy, p) => HardcoreRestore.SafeSpotNear(copy, xz[0], xz[1], p);
        }
        else if (a.Has("spawn")) pick = (copy, p) => HardcoreRestore.SafeSpot(copy, p, besideBed: false);
        else pick = (copy, p) => HardcoreRestore.SafeSpot(copy, p);
        var r = PlayerMoveCopy.Run(w, found, pick, output);
        foreach (var p in r.Problems) Console.WriteLine("  ✗ " + p);
        Console.WriteLine($"{string.Join(" and ", found.Select(p => p.Name))}: {string.Join(", ", r.Done)}, in the overworld, in {r.OpenPath}{(r.Verified ? ", checked" : "")}.");
        return r.Verified ? 0 : 1;
    }

    // A player by "host" (the one single-player opens with), the name the world shows, or a UUID or a server's
    // usercache.json name — every record with that UUID: a single-player Java world keeps the host in level.dat and in
    // playerdata, and which one the game reads depends on how it is played.
    static List<PlayerRecord> FindPlayers(SaveWorld w, string who)
    {
        if (who.Equals("host", StringComparison.OrdinalIgnoreCase))
        {
            var host = (w is Core.Java.JavaWorld java ? ServerPlayers.Host(java) : w.Players.FirstOrDefault(p => p.Source == "~local_player")) ?? w.Players.FirstOrDefault();
            return host == null ? [] : [host];
        }
        Guid? uuid = Guid.TryParse(who, out var g) ? g : null;
        if (uuid == null && w.Players.FirstOrDefault(p => p.Name.Equals(who, StringComparison.OrdinalIgnoreCase)) is PlayerRecord named) return [named];
        if (uuid == null && w is Core.Java.JavaWorld j)
            foreach (var (id, name) in ServerPlayers.KnownNames(j))
                if (name.Equals(who, StringComparison.OrdinalIgnoreCase)) { uuid = id; break; }
        return uuid is Guid u ? w.Players.Where(p => ServerPlayers.UuidOf(p) == u).ToList() : [];
    }

    public static int Crop(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string file = Path.GetFullPath(a.Require(1, "the new file"));
        if (File.Exists(file) && !a.Has("replace")) throw new IOException($"{file} is there already (add --replace to write over it).");
        var box = Numbers(a.Option("box") ?? throw new UsageException("Say where: --box x1,y1,z1,x2,y2,z2."), 6, "--box x1,y1,z1,x2,y2,z2");
        string dimId = a.Option("dim") ?? "minecraft:overworld";
        var dim = w.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimId || d.Id == dimId) ?? throw new UsageException($"The world has no dimension {dimId}.");
        var s = StructureClip.Extract(w, dim, Math.Min(box[0], box[3]), Math.Min(box[1], box[4]), Math.Min(box[2], box[5]), Math.Max(box[0], box[3]), Math.Max(box[1], box[4]), Math.Max(box[2], box[5]));
        int dv = a.Option("version") is string v ? FileCommands.JavaVersion(v) : 0;
        var standIns = StructureFile.SaveChecked(s, file, dv);
        Console.WriteLine($"Saved {s.Describe()} to {file} ({StructureFile.FormatOf(file)}).");
        foreach (var c in standIns) Console.WriteLine($"  stand-in: {c.Count:N0} × {c.From} → {c.To}");
        return standIns.Count == 0 ? 0 : 1;
    }

    public static int Biome(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string biome = a.Require(1, "the biome");
        var box = Numbers(a.Option("box") ?? throw new UsageException("Say where: --box x1,y1,z1,x2,y2,z2."), 6, "--box x1,y1,z1,x2,y2,z2");
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the copy goes: --out <new folder>."));
        var r = BiomePaint.PaintIntoCopy(w, a.Option("dim") ?? "minecraft:overworld", Math.Min(box[0], box[3]), Math.Min(box[1], box[4]), Math.Min(box[2], box[5]),
            Math.Max(box[0], box[3]), Math.Max(box[1], box[4]), Math.Max(box[2], box[5]), biome, output);
        foreach (var n in r.Notes) Console.WriteLine("  note: " + n);
        foreach (var p in r.Problems) Console.WriteLine("  ✗ " + p);
        Console.WriteLine($"The box is {BiomePaint.Canonical(biome)} in {r.OpenPath} ({r.ChunksChanged:N0} chunks rewritten){(r.Verified ? ", checked" : "")}.");
        return r.Verified ? 0 : 1;
    }

    public static int Maps(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string folder = Path.GetFullPath(a.Require(1, "the folder for the pictures"));
        Directory.CreateDirectory(folder);
        int saved = 0, skipped = 0;
        foreach (var m in Core.Conversion.Maps.Read(w).OrderBy(m => m.Id))
        {
            string path = Path.Combine(folder, $"map_{m.Id}.png");
            if (File.Exists(path)) { skipped++; continue; }
            SafeFile.WriteAllBytes(path, Png.Encode(Resample.Scale(MapArt.Picture(m), 512, 512, smooth: false)));
            saved++;
        }
        Console.WriteLine($"Saved {saved:N0} map pictures to {folder}{(skipped > 0 ? $" ({skipped} were there already and were left)" : "")}.");
        return 0;
    }

    public static int Share(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string file = Path.GetFullPath(a.Require(1, "the file to write"));
        if (File.Exists(file)) throw new IOException($"{file} is there already; choose a new name.");
        if (!WorldShare.CanPack(w)) throw new NotSupportedException("Only Java and Bedrock world folders can be packed into one file.");
        long size = WorldShare.Pack(w, file);
        Console.WriteLine($"Packed {w.Name} into {file} ({size / 1024.0 / 1024.0:0.0} MB).");
        return 0;
    }

    internal static int[] Numbers(string text, int count, string usage)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != count || parts.Any(p => !int.TryParse(p, out _))) throw new UsageException($"Write {usage}.");
        return parts.Select(int.Parse).ToArray();
    }
}
