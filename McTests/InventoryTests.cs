// McTests/InventoryTests.cs: inventory: the inventory editor and the cheat flags on copies of real worlds of
// every kind in TestData.
// Owns: InventoryTests.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

/// <summary>
/// inventory: the inventory editor and the cheat flags on copies of real worlds of every kind in TestData. Every
/// item written back untouched must be identical; edits must read back; new items must get the version's own field
/// types; saved players and flags must survive reopening the world.
/// </summary>
static class InventoryTests
{
    public static void Run(string[] args)
    {
        string temp = Path.Combine(TestPaths.Output, "inventory");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        var worlds = new List<string>();
        foreach (var level in Directory.EnumerateFiles(Path.Combine(TestPaths.Data, "amulet"), "level.dat", SearchOption.AllDirectories)) worlds.Add(Path.GetDirectoryName(level)!);
        string console = Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");
        if (File.Exists(console)) worlds.Add(console);
        string threeDs = Path.Combine(TestPaths.Data, "3ds");
        if (Directory.Exists(threeDs)) worlds.AddRange(Directory.GetDirectories(threeDs).Where(d => File.Exists(Path.Combine(d, "level.dat"))));
        // An Indev level keeps its player as the LocalPlayer entity inside the level file.
        string indev = Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "hell.mclevel");
        if (File.Exists(indev)) worlds.Add(indev);
        Components();
        IndevExtras(indev, temp);
        // Formats the sample worlds do not have, made by the converter from a world whose player has items: item
        // components with and without the levels wrapper and the equipment compound (1.21.4, 26.1), numeric ids (1.7),
        // Pocket Edition 0.9 (numeric Bedrock) and 0.2–0.8 (level.dat player).
        string forge = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_16_5_twilight_forest");
        if (Directory.Exists(forge))
        {
            using var source = WorldLoader.Open(forge);
            foreach (var targetName in new[] { "Java 26.1", "Java 1.21.4", "Java 1.7.2 – 1.7.10", "Pocket Edition 0.9 – 0.16", "Pocket Edition 0.2 – 0.8 · Pi Edition" })
            {
                var target = MinecraftStudio.Core.Conversion.TargetCatalog.All.FirstOrDefault(t => t.Name == targetName);
                if (target == null) { Program.Check(false, "no target " + targetName); continue; }
                string outDir = Path.Combine(temp, "converted-" + worlds.Count);
                var report = MinecraftStudio.Core.Conversion.WorldConverter.Run(source, target, outDir, new MinecraftStudio.Core.Conversion.ConversionOptions { Verify = false });
                worlds.Add(report.OpenPath ?? outDir);
                Program.Info($"converted the forge world to {targetName} for the test");
            }
        }
        int n = 0;
        foreach (var src in worlds)
        {
            string name = Path.GetRelativePath(TestPaths.Data, src);
            string copy = Path.Combine(temp, $"w{n++}");
            if (File.Exists(src)) { Directory.CreateDirectory(copy); copy = Path.Combine(copy, Path.GetFileName(src)); File.Copy(src, copy); }
            else CopyDir(src, copy);
            Console.WriteLine("== " + name);
            try { World(copy, name); }
            catch (Exception e) { Program.Check(false, $"{name}: {e.GetType().Name} {e.Message}"); }
        }
        Transfer(temp, worlds.Where(w => w.Contains("converted-", StringComparison.Ordinal)).ToList());
    }

    /// <summary>
    /// Things moved between editions (InventoryTransfer): a Java 1.16.5 (Forge) player's, an Xbox 360 save's and a Bedrock
    /// player's into copies of a Java 1.16.5, a Java 26.1 (the equipment layout), a Bedrock world and two Xbox 360 saves (TU70
    /// with named ids, TU0 - 4 with numbers and Java 1.0's items); reopened from disk, every slot holds the same item and
    /// count as the converter reads them, or paper named after what the version lacks; a part the source or target lacks is
    /// left as it was; Pocket 0.x is refused as a target before anything changes.
    /// </summary>
    static void Transfer(string temp, List<string> converted)
    {
        string dir = Path.Combine(temp, "transfer");
        var sources = new[]
        {
            ("Java 1.16.5 Forge", Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_16_5_twilight_forest")),
            ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        };
        var targets = new List<(string Label, string Path)>
        {
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
            ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
            ("Xbox 360 TU0 - 4", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_0033_upgraded", "savegame_upgraded.dat")),
        };
        // The first world the converter made above is Java 26.1; its player is made a 26.x one (armour and off-hand in
        // "equipment") in the copy, as a world played in 26.x has it.
        if (converted.Count > 0) targets.Add(("Java 26.x player (equipment)", converted[0]));
        // The converter's Pocket 0.2 - 0.8 world (its player in level.dat, the inventory behind the hotbar's links).
        if (MadePocket(converted) is string pocket) targets.Add(("Pocket 0.2 - 0.8", pocket));
        int n = 0;
        foreach (var (sourceLabel, sourcePath) in sources)
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)) { Program.Info($"(skipped {sourceLabel}: not in TestData)"); continue; }
            using var source = WorldLoader.Open(sourcePath);
            var from = source.Players.FirstOrDefault(p => new PlayerInventory(source, p) is var inv && inv.Parts.Any(part => Enumerable.Range(0, PlayerInventory.Size(part)).Any(i => inv.Get(part, i) is { IsEmpty: false })));
            if (from == null) { Program.Info($"(skipped {sourceLabel}: no player with items)"); continue; }
            var srcInv = new PlayerInventory(source, from);
            foreach (var (targetLabel, targetPath) in targets)
            {
                if (!File.Exists(targetPath) && !Directory.Exists(targetPath)) continue;
                if (targetPath == sourcePath) continue;
                string copy = Path.Combine(dir, $"t{n++}");
                if (File.Exists(targetPath)) { Directory.CreateDirectory(copy); copy = Path.Combine(copy, Path.GetFileName(targetPath)); File.Copy(targetPath, copy); }
                else CopyDir(targetPath, copy);
                TransferResult result;
                PlayerRecord to;
                bool offhandBefore;
                InventoryItem? keptOffhand;
                using (var target = WorldLoader.Open(copy))
                {
                    to = target.Players.OrderBy(p => p.Source.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First();
                    if (targetLabel.Contains("equipment")) { to.Data.SetInt("DataVersion", 4786); to.Data["equipment"] = new NbtCompound(); }
                    var before = new PlayerInventory(target, to);
                    offhandBefore = before.HasOffhand;
                    keptOffhand = before.Get(InventoryPart.Offhand, 0);
                    result = InventoryTransfer.Copy(source, from, target, to);
                    PlayerStore.Save(target, to);
                }
                using var reopened = WorldLoader.Open(copy);
                var again = reopened.Players.First(p => p.Source == to.Source);
                var dstInv = new PlayerInventory(reopened, again);
                if (targetLabel.Contains("equipment"))
                    Program.Check(again.Data.GetCompound("equipment") is { Count: > 0 } || !Enumerable.Range(0, 4).Any(i => srcInv.Get(InventoryPart.Armor, i) is { IsEmpty: false }),
                        $"{sourceLabel} → {targetLabel}: armour went into \"equipment\", as 26.x keeps it");
                int same = 0, wrong = 0, placeholders = 0;
                string? first = null;
                foreach (var part in Enum.GetValues<InventoryPart>())
                {
                    bool both = (part != InventoryPart.Offhand || srcInv.HasOffhand && dstInv.HasOffhand) && (part != InventoryPart.EnderChest || srcInv.HasEnderChest && dstInv.HasEnderChest);
                    if (!both) continue;
                    for (int i = 0; i < PlayerInventory.Size(part); i++)
                    {
                        var a = srcInv.Get(part, i);
                        var b = dstInv.Get(part, i);
                        var ca = a == null || a.IsEmpty ? null : InventoryTransfer.Canonical(source, a.Raw ?? srcInv.WriteItem(a), srcInv.DataVersion);
                        var cb = b == null || b.IsEmpty ? null : InventoryTransfer.Canonical(reopened, b.Raw!, dstInv.DataVersion);
                        string Key(NbtCompound? c) => c == null ? "empty" : $"{c.GetString("id")}×{c.GetInt("count", c.GetInt("Count", 1))}";
                        string written = b?.Raw == null ? "" : MinecraftStudio.Core.Nbt.Snbt.Write(b.Raw, MinecraftStudio.Core.Nbt.SnbtStyle.Compact);
                        if (Key(ca) == Key(cb)) same++;
                        // What the target can't hold (a mod's item in Bedrock) is paper named after it, and said in the notes.
                        // Pocket 0.x items keep no name: there the paper is plain, and the notes say what it was.
                        else if (ca?.GetString("id") is string lost && cb?.GetString("id") == "minecraft:paper" && (written.Contains("Was " + lost) || targetLabel.StartsWith("Pocket", StringComparison.Ordinal)) && result.Notes.Any(nt => nt.Contains(lost))) placeholders++;
                        else { wrong++; first ??= $"{part} {i}: {Key(ca)} became {Key(cb)} (written {written})"; }
                    }
                }
                // A source without an off-hand (a console save) leaves the target's off-hand alone.
                bool offhandKept = srcInv.HasOffhand || !offhandBefore || Same(keptOffhand, dstInv.Get(InventoryPart.Offhand, 0));
                Program.Info($"{sourceLabel} → {targetLabel}: {result.Items} items, {same} slots the same, {placeholders} as named placeholders");
                Program.Check(wrong == 0 && offhandKept && result.Items > 0,
                    $"{sourceLabel} → {targetLabel}: {result.Items} items moved, every slot the same item and count after reopening ({same} slots{(wrong > 0 ? $", {wrong} wrong: {first}" : "")}){(result.Notes.Count > 0 ? "; notes: " + string.Join(" | ", result.Notes.Take(3)) : "")}");
            }
        }
        PocketSlots(converted);
        ThreeDs();
        // Not into a Pocket 0.1 world (it keeps no player) or a 3DS world (the game deletes a changed one): refused first.
        foreach (var (label, path) in new[] { ("Pocket 0.1", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")), ("3DS", Path.Combine(TestPaths.Data, "3ds", "1.9")) })
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            using var w = WorldLoader.Open(path);
            Program.Check(InventoryTransfer.WhyNotInto(w) != null, $"items are not put into {label} saves ({InventoryTransfer.WhyNotInto(w)})");
        }
    }

    static bool Same(InventoryItem? a, InventoryItem? b) => a == null ? b == null : b != null && a.Id == b.Id && a.Count == b.Count;

    // The converter's Pocket 0.2 - 0.8 world among those made for the test.
    static string? MadePocket(List<string> converted)
    {
        foreach (var c in converted)
        {
            using var w = WorldLoader.Open(c);
            if (w is PocketLegacyWorld { Level: not null }) return c;
        }
        return null;
    }

    /// <summary>
    /// Pocket 0.2 - 0.8 numbers the inventory behind the hotbar's links (slots 0 - 8 are links, the 36 slots are 9 - 44), as
    /// the 3DS's own save shows: the converter's Pocket world, read back by the converter's reader and by the inventory
    /// editor, has every item of the source in the slot it had; the editor writes it back exactly so.
    /// </summary>
    static void PocketSlots(List<string> converted)
    {
        string forge = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_16_5_twilight_forest");
        string? pocket = MadePocket(converted);
        if (pocket == null || !Directory.Exists(forge)) { Program.Info("(skipped the Pocket slots: no converted Pocket world)"); return; }
        using var source = WorldLoader.Open(forge);
        var from = MinecraftStudio.Core.Conversion.WorldInfoReader.Read(source).Players.First(p => p.Data.GetList("Inventory") is { Count: > 0 });
        using var w = WorldLoader.Open(pocket);
        var back = MinecraftStudio.Core.Conversion.WorldInfoReader.Read(w).Players.FirstOrDefault();
        var raw = w.Players.FirstOrDefault();
        if (back == null || raw == null) { Program.Check(false, "Pocket 0.2 - 0.8: the made world has its player"); return; }
        var stored = raw.Data.GetList("Inventory")!.OfType<NbtCompound>().Select(e => e.GetInt("Slot")).ToList();
        Program.Check(stored.All(s => s is >= 9 and <= 44), $"Pocket 0.2 - 0.8: the writer puts the inventory at slots 9 - 44 ({string.Join(" ", stored.Take(8))} …)");
        // Every source slot below 36 the Pocket world could hold comes back in the same slot (items 0.8 lacks become others).
        var want = from.Data.GetList("Inventory")!.OfType<NbtCompound>().Where(e => e.GetInt("Slot") is >= 0 and < 36).Select(e => e.GetInt("Slot")).ToHashSet();
        var got = back.Data.GetList("Inventory")!.OfType<NbtCompound>().Select(e => e.GetInt("Slot")).ToHashSet();
        Program.Check(want.SetEquals(got), $"Pocket 0.2 - 0.8: read back by the converter, every item in its own slot ({string.Join(" ", want.Order())} → {string.Join(" ", got.Order())})");
        var inv = new PlayerInventory(w, raw);
        int filled = Enumerable.Range(0, 9).Count(i => inv.Get(InventoryPart.Hotbar, i) != null) + Enumerable.Range(0, 27).Count(i => inv.Get(InventoryPart.Storage, i) != null);
        Program.Check(filled == want.Count && want.All(s => (s < 9 ? inv.Get(InventoryPart.Hotbar, s) : inv.Get(InventoryPart.Storage, s - 9)) != null),
            $"Pocket 0.2 - 0.8: the inventory editor shows them in the same slots ({filled} of {want.Count})");
        var before = (NbtCompound)raw.Data.Clone();
        inv.WriteBack();
        // An NBT compound's keys have no order: each entry is compared key by key.
        static string Keyed(NbtCompound e) => string.Join(",", e.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + (kv.Value is NbtCompound inner ? "{" + Keyed(inner) + "}" : Snbt.Write(kv.Value, SnbtStyle.Compact))));
        static Dictionary<int, string> BySlot(NbtList l) => l.OfType<NbtCompound>().ToDictionary(e => e.GetInt("Slot"), Keyed);
        var a = BySlot(before.GetList("Inventory")!);
        var c = BySlot(raw.Data.GetList("Inventory")!);
        bool armourSame = raw.Data.GetList("Armor")!.OfType<NbtCompound>().Select(Keyed).SequenceEqual(before.GetList("Armor")!.OfType<NbtCompound>().Select(Keyed));
        Program.Check(a.Count == c.Count && a.All(kv => c.GetValueOrDefault(kv.Key) == kv.Value) && armourSame,
            $"Pocket 0.2 - 0.8: written back unchanged, every slot exactly as it was ({a.Count} slots){(a.Count == c.Count ? "" : $"; now {c.Count}")}"
            + string.Concat(a.Where(kv => c.GetValueOrDefault(kv.Key) != kv.Value).Take(2).Select(kv => $"; slot {kv.Key}: {kv.Value} became {c.GetValueOrDefault(kv.Key)}"))
            + (armourSame ? "" : $"; armour {Snbt.Write(before.GetList("Armor")!, SnbtStyle.Compact)} became {Snbt.Write(raw.Data.GetList("Armor")!, SnbtStyle.Compact)}"));
    }

    /// <summary>The 3DS's player, read from its value store (db/vdb): its inventory behind the hotbar's links, only to look at.</summary>
    static void ThreeDs()
    {
        string path = Path.Combine(TestPaths.Data, "3ds", "1.9");
        if (!Directory.Exists(path)) { Program.Info("(skipped the 3DS player: not in TestData)"); return; }
        using var w = WorldLoader.Open(path);
        Program.Check(w.Players.Count == 1 && !PlayerStore.CanSave(w), $"3DS: its player read from the value store, not to be saved ({w.Players.Count})");
        if (w.Players.Count == 0) return;
        var inv = new PlayerInventory(w, w.Players[0]);
        var links = w.Players[0].Data.GetList("Inventory")!.OfType<NbtCompound>().Where(e => e.GetInt("Slot") < 9).ToList();
        Program.Check(links.Count == 9 && links.All(l => l.GetInt("id") == 255) && inv.HasOffhand && inv.HasEnderChest, "3DS: slots 0 - 8 are the hotbar's links (id 255), off-hand and ender chest there too");
        var info = MinecraftStudio.Core.Conversion.WorldInfoReader.Read(w);
        var pos = info.Players.FirstOrDefault()?.Data.GetList("Pos");
        Program.Check(info.Players.Count == 1 && pos is { Count: 3 } && Math.Abs(pos[1].AsDouble - 70) < 0.01, $"3DS: the converter carries the player, standing at y 70 (its eyes at {(pos is { Count: 3 } ? pos[1].AsDouble + 1.62 : 0):0.00})");
    }

    /// <summary>Indev: saving the player leaves the level's blocks as they were, and a conversion carries the inventory.</summary>
    static void IndevExtras(string src, string temp)
    {
        if (!File.Exists(src)) return;
        Console.WriteLine("== Indev level player");
        string dir = Path.Combine(temp, "indev-extras");
        Directory.CreateDirectory(dir);
        string copy = Path.Combine(dir, Path.GetFileName(src));
        File.Copy(src, copy, true);
        byte[] before;
        int items;
        using (var w = WorldLoader.Open(copy))
        {
            var level = (MinecraftStudio.Core.Legacy.FiniteWorld)w;
            before = (byte[])level.Indev!.Blocks.Clone();
            Program.Check(w.Players.Count == 1, $"the Indev level shows its player ({w.Players.Count})");
            if (w.Players.Count == 0) return;
            var player = w.Players[0];
            var inv = new PlayerInventory(w, player);
            items = inv.Parts.Sum(part => Enumerable.Range(0, PlayerInventory.Size(part)).Count(i => inv.Get(part, i) != null));
            Program.Info($"Indev player: {inv.Format}, {items} item(s)");
            inv.WriteBack();
            PlayerStore.Save(w, player);
        }
        using (var w = WorldLoader.Open(copy))
            Program.Check(((MinecraftStudio.Core.Legacy.FiniteWorld)w).Indev!.Blocks.AsSpan().SequenceEqual(before), "saving the player leaves every block of the level as it was");

        // Converting the level carries the player's inventory (it used to be left behind).
        var target = MinecraftStudio.Core.Conversion.TargetCatalog.All.First(t => t.Name.StartsWith("Java 1.12"));
        string outDir = Path.Combine(dir, "converted");
        using (var w = WorldLoader.Open(copy))
            MinecraftStudio.Core.Conversion.WorldConverter.Run(w, target, outDir, new MinecraftStudio.Core.Conversion.ConversionOptions { Verify = false });
        using (var converted = WorldLoader.Open(outDir))
        {
            var p = converted.Players.FirstOrDefault();
            var after = p == null ? null : new PlayerInventory(converted, p);
            int carried = after == null ? -1 : after.Parts.Sum(part => Enumerable.Range(0, PlayerInventory.Size(part)).Count(i => after.Get(part, i) != null));
            Program.Check(p != null && carried == items, $"converting the Indev level to {target.Name} keeps the player's {items} item(s) ({carried} arrived)");
        }
    }

    static void CopyDir(string src, string dest)
    {
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dest, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
    }

    static void World(string path, string name)
    {
        // Flags: read, clear, save, reopen.
        using (var w = WorldLoader.Open(path))
        {
            var flags = WorldFlags.Read(w);
            Program.Info($"flags: cheats {flags.Cheats}, creative {flags.PlayedInCreative}, experiments {flags.ExperimentsUsed}, mode {flags.GameMode}, blockers [{string.Join("; ", flags.AchievementBlockers())}]");
            if (w is BedrockWorld or JavaWorld or ConsoleWorld or ThreeDsWorld or PocketLegacyWorld { Level: not null })
            {
                var root = w switch
                {
                    JavaWorld j => j.Data!, BedrockWorld b => b.LevelRoot, ConsoleWorld c => c.Level!.RootCompound.GetCompound("Data")!,
                    ThreeDsWorld t => t.Level.RootCompound, PocketLegacyWorld pl => pl.Level!.RootCompound, _ => null!,
                };
                var types = root.ToDictionary(kv => kv.Key, kv => kv.Value.Type);
                flags.ClearMarks();
                flags.Save();
                Program.Check(root.All(kv => types.TryGetValue(kv.Key, out var t) && t == kv.Value.Type), $"{name}: level.dat keeps its keys and tag types");
            }
        }
        if (Directory.Exists(path) || File.Exists(path))
            using (var again = WorldLoader.Open(path))
            {
                var flags = WorldFlags.Read(again);
                Program.Check(flags.Cheats != true && flags.PlayedInCreative != true && flags.ExperimentsUsed != true && flags.GameMode != WorldFlags.Creative,
                    $"{name}: cleared marks survive reopening (cheats {flags.Cheats}, creative {flags.PlayedInCreative}, mode {flags.GameMode})");
            }

        // Inventories: untouched items are written back identically; an edit and a new item read back.
        using var world = WorldLoader.Open(path);
        foreach (var player in world.Players.ToList())
        {
            var inv = new PlayerInventory(world, player);
            int items = 0, same = 0;
            foreach (var part in inv.Parts)
                for (int i = 0; i < PlayerInventory.Size(part); i++)
                    if (inv.Get(part, i) is InventoryItem item)
                    {
                        items++;
                        var raw = (NbtCompound)item.Raw!.Clone();
                        raw.Remove("Slot");
                        if (inv.WriteItem(item).ContentEquals(raw)) same++;
                        else Program.Info($"differs: {Snbt.Write(raw, SnbtStyle.Compact)} → {Snbt.Write(inv.WriteItem(item), SnbtStyle.Compact)}");
                    }
            Program.Check(same == items, $"{name} / {player.Name} ({inv.Format}): {same} of {items} untouched items written back identically");
            Program.Info($"{player.Name}: {inv.Format}, {items} items, {same} identical when written back");
            if (!PlayerStore.CanSave(world)) continue;

            // Edit the first item (or make one), add a new one in an empty slot, save the player, reopen.
            var first = inv.Parts.SelectMany(p => Enumerable.Range(0, PlayerInventory.Size(p)).Select(i => (p, i))).FirstOrDefault(s => inv.Get(s.p, s.i) != null);
            InventoryItem edited;
            if (first.p == default && inv.Get(first.p, first.i) == null)
            {
                edited = NewItem(inv, "sword");
                first = (InventoryPart.Hotbar, 0);
            }
            else edited = inv.Get(first.p, first.i)!.Clone();
            edited.Count = 1;
            edited.CustomName = "Tested blade";
            edited.Enchantments.Clear();
            edited.Enchantments.Add(new("unbreaking", 3));
            inv.Set(first.p, first.i, edited);
            var empty = Enumerable.Range(0, 27).FirstOrDefault(i => inv.Get(InventoryPart.Storage, i) == null);
            var added = NewItem(inv, "apple");
            added.Count = 5;
            inv.Set(InventoryPart.Storage, empty, added);
            string playerName = player.Name;
            inv.WriteBack();
            PlayerStore.Save(world, player);

            using var reopened = WorldLoader.Open(path);
            var p2 = reopened.Players.First(p => p.Name == playerName);
            var inv2 = new PlayerInventory(reopened, p2);
            var e2 = inv2.Get(first.p, first.i);
            var a2 = inv2.Get(InventoryPart.Storage, empty);
            Program.Check(e2 != null && e2.CustomName == "Tested blade" && e2.Enchantments.SequenceEqual([new Enchantment("unbreaking", 3)]) && e2.Count == 1,
                $"{name} / {playerName}: the edited item reads back ({e2?.Id}, name \"{e2?.CustomName}\", {string.Join(",", e2?.Enchantments ?? [])})");
            Program.Check(a2 != null && a2.Id == added.Id && a2.Count == 5, $"{name} / {playerName}: the new item is in its slot ({a2?.Id} ×{a2?.Count})");
            if (a2?.Raw is NbtCompound r)
                Program.Check(inv2.Format switch
                {
                    ItemFormat.Components => r["count"] is NbtInt && r["id"] is NbtString,
                    ItemFormat.Bedrock => r["Count"] is NbtByte && r["Name"] is NbtString && r["Damage"] is NbtShort,
                    ItemFormat.Numeric => r["Count"] is NbtByte && r["id"] is NbtShort && r["Damage"] is NbtShort,
                    _ => r["Count"] is NbtByte && r["id"] is NbtString,
                }, $"{name} / {playerName}: the new item has {inv2.Format}'s field types: {Snbt.Write(r, SnbtStyle.Compact)}");
        }
    }

    /// <summary>
    /// Item components (1.20.5+), which converted worlds do not have on disk (their level.dat is written in 1.20.4's
    /// layout for the game to upgrade): players built from the converter's own items for 1.21.4 (levels wrapper,
    /// armour in slots 100–103) and 26.1 (no wrapper, armour in equipment).
    /// </summary>
    static void Components()
    {
        Console.WriteLine("== item components (1.21.4 and 26.1 players)");
        string forge = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_16_5_twilight_forest");
        if (!Directory.Exists(forge)) return;
        using var world = WorldLoader.Open(forge);
        foreach (int dv in new[] { 4189, 4786 })
        {
            var canonical = new NbtCompound
            {
                { "id", new NbtString("minecraft:diamond_sword") }, { "count", new NbtInt(1) },
                { "components", new NbtCompound
                    {
                        { "minecraft:damage", new NbtInt(12) },
                        { "minecraft:enchantments", new NbtCompound { { "minecraft:sharpness", new NbtInt(5) } } },
                        { "minecraft:custom_name", new NbtString("{\"text\":\"Old\",\"color\":\"gold\"}") },
                        { "minecraft:repair_cost", new NbtInt(3) },
                    } },
            };
            var sword = MinecraftStudio.Core.Conversion.Items.ToJava(canonical, dv);
            sword.SetByte("Slot", 0);
            var helmet = MinecraftStudio.Core.Conversion.Items.ToJava(new NbtCompound { { "id", new NbtString("minecraft:iron_helmet") }, { "count", new NbtInt(1) } }, dv);
            var player = new NbtCompound { { "DataVersion", new NbtInt(dv) }, { "Inventory", new NbtList(NbtType.Compound) { sword } } };
            if (dv >= 4325) player["equipment"] = new NbtCompound { { "head", helmet } };
            else { helmet.SetByte("Slot", 103); ((NbtList)player["Inventory"]!).Add(helmet); }
            var record = new PlayerRecord("test", "test", player);
            var inv = new PlayerInventory(world, record);
            var s = inv.Get(InventoryPart.Hotbar, 0);
            var h = inv.Get(InventoryPart.Armor, 0);
            Program.Check(inv.Format == ItemFormat.Components && s?.Damage == 12 && s.CustomName == "Old" && s.Enchantments.SequenceEqual([new Enchantment("sharpness", 5)]) && h?.Id == "minecraft:iron_helmet",
                $"data version {dv}: components read ({inv.Format}; damage {s?.Damage}, name {s?.CustomName}, {string.Join(",", s?.Enchantments ?? [])}, helmet {h?.Id})");
            var raw = (NbtCompound)s!.Raw!.Clone();
            raw.Remove("Slot");
            Program.Check(inv.WriteItem(s).ContentEquals(raw), $"data version {dv}: the untouched sword is written back identically (its gold name kept)");
            var edited = s.Clone();
            edited.Enchantments.Add(new("unbreaking", 3));
            edited.Count = 1;
            inv.Set(InventoryPart.Hotbar, 0, edited);
            inv.Set(InventoryPart.Armor, 1, new InventoryItem { Id = "minecraft:iron_chestplate" });
            inv.WriteBack();
            var again = new PlayerInventory(world, record);
            var s2 = again.Get(InventoryPart.Hotbar, 0)!;
            var en = s2.Raw!.GetCompound("components")!.GetCompound("minecraft:enchantments")!;
            Program.Check(s2.Enchantments.Count == 2 && (dv >= 4325 ? !en.ContainsKey("levels") : en.ContainsKey("levels")) && s2.Raw.GetCompound("components")!.GetInt("minecraft:repair_cost") == 3,
                $"data version {dv}: enchantments written {(dv >= 4325 ? "without" : "inside")} the levels wrapper, other components kept");
            Program.Check(again.Get(InventoryPart.Armor, 1)?.Id == "minecraft:iron_chestplate" && (dv >= 4325 ? player.GetCompound("equipment")?.ContainsKey("chest") == true : ((NbtList)player["Inventory"]!).OfType<NbtCompound>().Any(i => i.GetInt("Slot") == 102)),
                $"data version {dv}: armour goes {(dv >= 4325 ? "into equipment" : "into slot 102")}");
        }
    }

    static InventoryItem NewItem(PlayerInventory inv, string what) => inv.Format switch
    {
        ItemFormat.Numeric => new InventoryItem { Id = what == "sword" ? "276" : "260" },
        _ => new InventoryItem { Id = what == "sword" ? "minecraft:diamond_sword" : "minecraft:apple" },
    };
}
