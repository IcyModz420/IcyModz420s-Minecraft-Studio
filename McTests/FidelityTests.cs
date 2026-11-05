// McTests/FidelityTests.cs: fidelity <source> <target name part> [radiusChunks]: converts, reopens the output
// and compares the host player (inventory, ender chest, XP, health, position, spawn, game mode) and every
// entity (by type and position: name, owner/tamed, villager data and trades, equipment, armour stand pose,
// minecart and frame contents, painting).
// Owns: FidelityTests.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

/// <summary>
/// fidelity &lt;source&gt; &lt;target name part&gt; [radiusChunks]: converts, reopens the output and compares the host player
/// (inventory, ender chest, XP, health, position, spawn, game mode) and every entity (by type and position: name,
/// owner/tamed, villager data and trades, equipment, armour stand pose, minecart and frame contents, painting).
/// </summary>
static class FidelityTests
{
    static readonly string[] EntityKeys =
    [
        "CustomName", "Owner", "Tame", "Sitting", "VillagerData", "Offers", "equipment", "ArmorItems", "HandItems", "Items", "Item",
        "Pose", "ShowArms", "Small", "NoBasePlate", "variant", "Motive", "Facing", "SaddleItem", "Saddle", "Age", "CollarColor", "Variant",
        "Color", "Sheared", "ChestedHorse", "DecorItem", "Strength", "BodyArmorItem",
    ];

    public static void Run(string[] args)
    {
        string src = args[0];
        var target = TargetCatalog.All.First(t => t.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase));
        int radius = args.Length > 2 ? int.Parse(args[2]) : 8;
        string outDir = Path.Combine(ConvertTests.OutRoot, "fidelity", string.Concat((Path.GetFileName(src.TrimEnd('\\')) + "__" + target.Name).Select(c => char.IsLetterOrDigit(c) ? c : '_')));
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        using var source = WorldLoader.Open(src);
        var info = WorldInfoReader.Read(source);
        var options = new ConversionOptions { VerticalShift = int.TryParse(Environment.GetEnvironmentVariable("MSS_SHIFT"), out int shift) ? shift : 0 };
        if (target.Kind is TargetKind.Java or TargetKind.Bedrock)
            options.Area = ((info.SpawnX >> 4) - radius, (info.SpawnZ >> 4) - radius, (info.SpawnX >> 4) + radius, (info.SpawnZ >> 4) + radius);
        var report = WorldConverter.Run(source, target, outDir, options);
        Console.WriteLine($"== {source.Name} ({source.FormatDescription}) -> {target.Name}: {report.Chunks} chunks");
        foreach (var n in report.Notes) Console.WriteLine("   note: " + n);
        foreach (var c in report.Checks) Console.WriteLine($"   check {(c.Passed ? "OK  " : "FAIL")} {c.Name}: {c.Detail}");
        foreach (var l in report.Losses) Console.WriteLine($"   {(l.Lost ? "LOST" : "kept")} {l.Kind}: {l}");
        foreach (var i in report.Issues) Console.WriteLine("   issue: " + i);
        foreach (var r in report.RescueChests) Console.WriteLine($"   rescue chest {r.Label} at {r.X},{r.Y},{r.Z}");
        using var output = WorldLoader.Open(report.OpenPath ?? outDir);
        var outInfo = WorldInfoReader.Read(output);
        ComparePlayers(info, outInfo, target);
        CompareEntities(source, output, options, target, info);
    }

    static string Item(NbtCompound it) => $"{Items.Id(it)}×{it.GetInt("count", it.GetInt("Count", 1))}";

    static Dictionary<int, string> Slots(NbtList? list) => (list?.OfType<NbtCompound>() ?? []).GroupBy(i => i.GetInt("Slot")).ToDictionary(g => g.Key, g => Item(g.First()));

    static void ComparePlayers(WorldInfo a, WorldInfo b, ConversionTarget target)
    {
        if (a.Players.Count == 0) { Console.WriteLine("   players: the source has none"); return; }
        if (b.Players.Count == 0) { Program.Check(false, "the host player exists in the output"); return; }
        var p = a.Players[0].Data;
        var q = b.Players[0].Data;
        var problems = new List<string>();
        void Same(string what, object? x, object? y) { if (!Equals(x, y)) problems.Add($"{what}: {x} -> {y}"); }
        var inv = Slots(p.GetList("Inventory"));
        var outInv = Slots(q.GetList("Inventory"));
        foreach (var (slot, item) in inv) Same($"inventory slot {slot}", item, outInv.GetValueOrDefault(slot));
        foreach (var (slot, item) in outInv) if (!inv.ContainsKey(slot)) problems.Add($"inventory slot {slot}: (empty) -> {item}");
        var ender = Slots(p.GetList("EnderItems"));
        var outEnder = Slots(q.GetList("EnderItems"));
        foreach (var (slot, item) in ender) Same($"ender chest slot {slot}", item, outEnder.GetValueOrDefault(slot));
        Same("XP level", p.GetInt("XpLevel"), q.GetInt("XpLevel"));
        Same("game mode", p.GetInt("playerGameType"), q.GetInt("playerGameType"));
        Same("health", Math.Round(p.GetDouble("Health", 20)), Math.Round(q.GetDouble("Health", 20)));
        Same("food", p.GetInt("foodLevel", 20), q.GetInt("foodLevel", 20));
        if (p.GetLongOrNull("SpawnX") is long sx) Same("spawn point", $"{sx},{p.GetInt("SpawnY")},{p.GetInt("SpawnZ")}", q.GetLongOrNull("SpawnX") is long qx ? $"{qx},{q.GetInt("SpawnY")},{q.GetInt("SpawnZ")}" : "(none)");
        var pp = p.GetList("Pos"); var qp = q.GetList("Pos");
        if (pp is { Count: 3 } && qp is { Count: 3 } && target.Kind is TargetKind.Java or TargetKind.Bedrock)
            for (int i = 0; i < 3; i++) if (Math.Abs(pp[i].AsDouble - qp[i].AsDouble) > 1.0) problems.Add($"position[{i}]: {pp[i].AsDouble:0.##} -> {qp[i].AsDouble:0.##}");
        Console.WriteLine($"   host player: {inv.Count} inventory stacks, {ender.Count} ender chest stacks, level {p.GetInt("XpLevel")}; {problems.Count} differences");
        foreach (var x in problems.Take(12)) Console.WriteLine("      " + x);
        Program.Check(problems.Count == 0, "the host player's items, XP, health, spawn and position survive");
    }

    static void CompareEntities(SaveWorld source, SaveWorld output, ConversionOptions options, ConversionTarget target, WorldInfo info)
    {
        var perType = new SortedDictionary<string, (int Src, int Out, int Matched, int FieldDiffs)>(StringComparer.Ordinal);
        var diffs = new List<string>();
        foreach (var dim in source.Dimensions)
        {
            string id = CanonicalReader.DimensionId(dim);
            if (!WorldConverter.SupportsDimension(target, id)) continue;
            var outDim = output.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == id);
            var srcReader = new CanonicalReader(source, dim);
            var outReader = outDim == null ? null : new CanonicalReader(output, outDim);
            foreach (var pos in dim.ChunkPositions.Where(p => options.Area is not var (a, b, c, d) || (p.X >= a && p.X <= c && p.Z >= b && p.Z <= d)))
            {
                var s = srcReader.Get(pos);
                if (s == null || s.Entities.Count == 0) continue;
                var o = outReader?.Get(pos);
                var outs = o?.Entities.ToList() ?? [];
                foreach (var e in s.Entities)
                {
                    string type = Entities.CanonicalId(e.GetString("id") ?? "?");
                    var (sx, sy, sz) = Entities.Position(e) ?? (0, 0, 0);
                    var match = outs.FirstOrDefault(x => Entities.CanonicalId(x.GetString("id") ?? "") == type && Entities.Position(x) is var (ox, oy, oz) && Math.Abs(ox - sx) < 0.75 && Math.Abs(oy - sy) < 1.0 && Math.Abs(oz - sz) < 0.75);
                    var cur = perType.GetValueOrDefault(type);
                    cur.Src++;
                    if (match == null && Environment.GetEnvironmentVariable("MSS_UNMATCHED") != null)
                    {
                        var near = outs.Where(x => Entities.CanonicalId(x.GetString("id") ?? "") == type).Select(x => Entities.Position(x)).FirstOrDefault();
                        Console.WriteLine($"      unmatched {type} at {sx:0.##},{sy:0.##},{sz:0.##} in chunk {pos}; same type in output chunk: {near}; output chunk has {outs.Count} entities: {string.Join(",", outs.Select(x => x.GetString("id")).Distinct().Take(5))}");
                    }
                    if (match != null)
                    {
                        outs.Remove(match);
                        cur.Out++; cur.Matched++;
                        foreach (var key in EntityKeys)
                        {
                            var x = e[key]; var y = match[key];
                            if (x == null) continue;
                            string xs = Snbt.Write(x, SnbtStyle.Compact), ys = y == null ? "(missing)" : Snbt.Write(y, SnbtStyle.Compact);
                            if (xs != ys && !Equivalent(key, x, y)) { cur.FieldDiffs++; if (diffs.Count < 25) diffs.Add($"{type} {key}: {Trim(xs)} -> {Trim(ys)}"); }
                        }
                    }
                    perType[type] = cur;
                }
                foreach (var extra in outs)
                {
                    string type = Entities.CanonicalId(extra.GetString("id") ?? "?");
                    var cur = perType.GetValueOrDefault(type);
                    cur.Out++;
                    perType[type] = cur;
                }
            }
        }
        int src = perType.Values.Sum(v => v.Src), matched = perType.Values.Sum(v => v.Matched), fieldDiffs = perType.Values.Sum(v => v.FieldDiffs);
        Console.WriteLine($"   entities: {src} in the source area, {matched} found again ({(src == 0 ? 100 : matched * 100.0 / src):0.#}%), {fieldDiffs} field differences");
        foreach (var (type, v) in perType.Where(kv => kv.Value.Src != kv.Value.Matched || kv.Value.FieldDiffs > 0))
            Console.WriteLine($"      {type}: {v.Src} -> {v.Matched} matched (+{v.Out - v.Matched} other), {v.FieldDiffs} field differences");
        foreach (var d in diffs) Console.WriteLine("      " + d);
        Program.Check(matched == src, "every entity survives");
    }

    /// <summary>Values the game assumes when the tag is absent (0, empty lists of empty slots, empty strings).</summary>
    static bool IsDefault(NbtTag t) => t switch
    {
        { IsNumeric: true } n => n.AsDouble == 0,
        NbtString s => s.Value.Length == 0,
        NbtList l => l.All(e => e is NbtCompound c && c.Count == 0),
        NbtCompound c => c.Count == 0,
        _ => false,
    };

    static string Trim(string s) => s.Length > 140 ? s[..140] + "…" : s;

    /// <summary>Differences that are not losses (numbers stored with another type, rounding).</summary>
    static bool Equivalent(string key, NbtTag x, NbtTag? y)
    {
        if (y == null) return IsDefault(x);
        if (x.IsNumeric && y.IsNumeric) return Math.Abs(x.AsDouble - y.AsDouble) < 0.01;
        return false;
    }
}
