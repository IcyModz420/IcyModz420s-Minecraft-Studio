// McTests/RidingTests.cs: "riding" — riders and mounts across versions and editions.
// Owns: RidingTests — the old stack (a rider holding what it rides, "Riding": Java before 1.9 and consoles) read as
// canonical (the bottom one holding its riders, "Passengers") and written back for old versions (one rider each, the
// others set down beside it); riders of what a version lacks set down; a player's mount (RootVehicle, "Riding" before 1.9)
// read and written in each version's layout; every sample world's entities read without an old stack left in them; and
// real conversions of a copy of a world whose player rides a named, saddled horse — still riding it in Java 1.20 and 1.12,
// the horse standing where they were in Bedrock and Java 1.8 (said in the report), nothing reported lost.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class RidingTests
{
    public static void Run(string[] args)
    {
        Console.WriteLine("== Old stacks and canonical riders");
        Stacks();
        Console.WriteLine("== Bedrock riders (actors of their own, linked from what they ride)");
        BedrockLinks();
        Console.WriteLine("== A player's mount in each version's layout");
        PlayerMounts();
        Console.WriteLine("== Sample worlds: no old stack left after reading");
        Samples();
        Console.WriteLine("== Conversions of a player riding a horse");
        Conversions();
    }

    static NbtList Doubles(params double[] v) { var l = new NbtList(NbtType.Double); foreach (var d in v) l.Add(new NbtDouble(d)); return l; }

    static NbtCompound Old(string id, long most, double y, NbtCompound? riding = null)
    {
        var e = new NbtCompound
        {
            ["id"] = new NbtString(id), ["Pos"] = Doubles(10.5, y, 20.5), ["Motion"] = Doubles(0, 0, 0), ["UUIDMost"] = new NbtLong(most), ["UUIDLeast"] = new NbtLong(-most),
            ["Health"] = new NbtFloat(10), ["Fire"] = new NbtShort(-1),
        };
        if (riding != null) e["Riding"] = riding;
        return e;
    }

    static bool HasOldStack(NbtCompound e) => e.ContainsKey("Riding") || (e.GetList("Passengers")?.OfType<NbtCompound>().Any(HasOldStack) ?? false);

    static List<NbtCompound> Riders(NbtCompound e) => e.GetList("Passengers")?.OfType<NbtCompound>().ToList() ?? [];

    static void Stacks()
    {
        // 1.8: a zombie riding a chicken riding a minecart, stored as the zombie holding the rest.
        var stack = Old("Zombie", 3, 66.2, Old("Chicken", 2, 65.7, Old("MinecartRideable", 1, 65)));
        var canon = Entities.FromJava(stack, 0);
        var chicken = Riders(canon).FirstOrDefault();
        var zombie = chicken == null ? null : Riders(chicken).FirstOrDefault();
        Program.Check(canon.GetString("id") == "minecraft:minecart" && chicken?.GetString("id") == "minecraft:chicken" && zombie?.GetString("id") == "minecraft:zombie" && !HasOldStack(canon),
            $"1.8 stack → canonical: the minecart at the bottom, the chicken in it, the zombie on the chicken ({canon.GetString("id")} / {chicken?.GetString("id")} / {zombie?.GetString("id")})");
        Program.Check(canon["UUID"] is NbtIntArray { Value.Length: 4 } && zombie?["UUID"] is NbtIntArray { Value.Length: 4 } && zombie.GetList("Pos")![1].AsDouble == 66.2,
            "each keeps its own UUID and place");
        Program.Check(stack["Riding"] is NbtCompound, "the source compound is left as it was");
        // Back to 1.8: the same stack, the top rider outermost.
        var setDown = new List<NbtCompound>();
        var back = Entities.ToJava(canon, 0, setDown);
        var backChicken = back?.GetCompound("Riding");
        var backCart = backChicken?.GetCompound("Riding");
        Program.Check(back?.GetString("id") == "Zombie" && backChicken?.GetString("id") == "Chicken" && backCart?.GetString("id") == "MinecartRideable" && backCart.GetCompound("Riding") == null
            && !back.ContainsKey("Passengers") && !backChicken.ContainsKey("Passengers") && !backCart.ContainsKey("Passengers") && setDown.Count == 0,
            $"canonical → 1.8: the zombie holding the chicken holding the minecart ({back?.GetString("id")} / {backChicken?.GetString("id")} / {backCart?.GetString("id")})");
        Program.Check(back?.GetLongOrNull("UUIDMost") == 3 && backCart?.GetLongOrNull("UUIDMost") == 1, "UUIDs written the old way, the same ones");
        // 1.12 keeps riders the new way.
        var j12 = Entities.ToJava(canon, 1139, setDown);
        Program.Check(j12?.GetString("id") == "minecraft:minecart" && Riders(j12).FirstOrDefault()?.GetString("id") == "minecraft:chicken" && !HasOldStack(j12),
            "canonical → 1.12: riders kept the new way (Passengers)");

        // Two in a boat: before 1.9 only one is seated; the other gets off and stands beside it.
        var boat = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:oak_boat"), ["Pos"] = Doubles(0.5, 63, 0.5), ["UUID"] = new NbtIntArray([1, 1, 1, 1]),
            ["Passengers"] = new NbtList(NbtType.Compound)
            {
                new NbtCompound { ["id"] = new NbtString("minecraft:villager"), ["Pos"] = Doubles(0.5, 63.2, 0.5), ["UUID"] = new NbtIntArray([2, 2, 2, 2]) },
                new NbtCompound { ["id"] = new NbtString("minecraft:pig"), ["Pos"] = Doubles(0.5, 63.2, 0.5), ["UUID"] = new NbtIntArray([3, 3, 3, 3]) },
            },
        };
        setDown.Clear();
        var seated = Entities.ToJava(boat, 0, setDown);
        Program.Check(seated?.GetString("id") == "Villager" && seated.GetCompound("Riding")?.GetString("id") == "Boat" && setDown.Count == 1 && setDown[0].GetString("id") == "Pig" && !setDown[0].ContainsKey("Riding"),
            $"two in a boat → 1.8: the villager seated, the pig standing beside it ({seated?.GetString("id")} in {seated?.GetCompound("Riding")?.GetString("id")}; set down {string.Join(", ", setDown.Select(s => s.GetString("id")))})");
        var both = Entities.ToJava(boat, 3698, setDown = []);
        Program.Check(Riders(both!).Count == 2 && setDown.Count == 0, "two in a boat → 1.20: both seated");

        // A camel (1.20) with a villager on it, written for 1.12: no camel there, so the villager gets off where it was.
        var camel = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:camel"), ["Pos"] = Doubles(5.5, 70, 5.5), ["UUID"] = new NbtIntArray([4, 4, 4, 4]),
            ["Passengers"] = new NbtList(NbtType.Compound) { new NbtCompound { ["id"] = new NbtString("minecraft:villager"), ["Pos"] = Doubles(5.5, 72, 5.5), ["UUID"] = new NbtIntArray([5, 5, 5, 5]) } },
        };
        setDown = [];
        var noCamel = Entities.ToJava(camel, 1139, setDown);
        Program.Check(noCamel == null && setDown.Count == 1 && setDown[0].GetString("id") == "minecraft:villager" && setDown[0].GetList("Pos")![1].AsDouble == 72,
            $"a camel's rider → 1.12 (no camels): the villager stands where it was ({setDown.Count} set down)");
    }

    static void BedrockLinks()
    {
        NbtCompound Actor(string identifier, long unique, params long[] riders)
        {
            var a = new NbtCompound
            {
                ["identifier"] = new NbtString(identifier), ["UniqueID"] = new NbtLong(unique),
                ["Pos"] = new NbtList(NbtType.Float) { new NbtFloat(4.5f), new NbtFloat(64), new NbtFloat(4.5f) },
            };
            if (riders.Length > 0)
            {
                var links = new NbtList(NbtType.Compound);
                for (int i = riders.Length - 1; i >= 0; i--) links.Add(new NbtCompound { ["entityID"] = new NbtLong(riders[i]), ["LinkID"] = new NbtInt(i) });
                a["LinksTag"] = links;
            }
            return a;
        }
        List<NbtCompound> Seat(params NbtCompound[] stored) =>
            Entities.SeatRiders(stored.Select(s => (s, Entities.FromBedrock(s)!)).ToList());
        // A boat with a villager and a cat (links listed out of order: seats go by LinkID), a pig on its own, a link to an
        // actor in another chunk.
        var standing = Seat(Actor("minecraft:boat", 1, 2, 3, 77), Actor("minecraft:villager_v2", 2), Actor("minecraft:cat", 3), Actor("minecraft:pig", 4));
        var boat = standing.FirstOrDefault(e => e.GetString("id")?.EndsWith("boat") == true);
        Program.Check(standing.Count == 2 && boat != null && string.Join(",", Riders(boat).Select(r => r.GetString("id"))) == "minecraft:villager,minecraft:cat"
            && standing.Any(e => e.GetString("id") == "minecraft:pig"),
            $"the villager and the cat seated in the boat in seat order, the pig on its own ({string.Join("; ", standing.Select(e => e.GetString("id") + "[" + string.Join(",", Riders(e).Select(r => r.GetString("id"))) + "]"))})");
        // A damaged save: two actors each listing the other — one ends up riding, never both (no endless stack).
        var loop = Seat(Actor("minecraft:horse", 10, 11), Actor("minecraft:zombie", 11, 10));
        Program.Check(loop.Count == 1 && Riders(loop[0]).Count == 1 && Riders(Riders(loop[0])[0]).Count == 0, "two actors linking each other: one rides the other, no loop");
        var self = Seat(Actor("minecraft:horse", 20, 20));
        Program.Check(self.Count == 1 && Riders(self[0]).Count == 0, "an actor linking itself rides nothing");
    }

    static void PlayerMounts()
    {
        var horse = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:horse"), ["Pos"] = Doubles(1.5, 64, 1.5), ["UUID"] = new NbtIntArray([7, 8, 9, 10]), ["Tame"] = new NbtByte(1),
            ["SaddleItem"] = new NbtCompound { ["id"] = new NbtString("minecraft:saddle"), ["Count"] = new NbtByte(1) },
        };
        var player = new NbtCompound
        {
            ["Pos"] = Doubles(1.5, 65.2, 1.5), ["Dimension"] = new NbtString("minecraft:overworld"),
            ["RootVehicle"] = new NbtCompound { ["Attach"] = new NbtIntArray([7, 8, 9, 10]), ["Entity"] = horse },
        };
        var canon = Players.FromJava("Player", player, 2586);
        var rv = canon.Data.GetCompound("RootVehicle");
        Program.Check(rv?["Attach"] is NbtIntArray { Value: [7, 8, 9, 10] } && rv.GetCompound("Entity")?.GetString("id") == "minecraft:horse", "1.16: RootVehicle read (seat and horse)");
        var t20 = TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.Name.EndsWith("1.20.3 – 1.20.4", StringComparison.Ordinal));
        var t12 = TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.Name.EndsWith("1.12 – 1.12.2", StringComparison.Ordinal));
        var j20 = Players.ToJava(canon, 3698, t20).GetCompound("RootVehicle");
        Program.Check(j20?["Attach"] is NbtIntArray { Value: [7, 8, 9, 10] } && j20.GetCompound("Entity")?["UUID"] is NbtIntArray { Value: [7, 8, 9, 10] },
            "→ 1.20: Attach and the horse's UUID as int[4], the same");
        var j12 = Players.ToJava(canon, 1139, t12).GetCompound("RootVehicle");
        long most = ((long)7 << 32) | 8u, least = ((long)9 << 32) | 10u;
        Program.Check(j12?.GetLongOrNull("AttachMost") == most && j12.GetLongOrNull("AttachLeast") == least && j12.GetCompound("Entity")?.GetLongOrNull("UUIDMost") == most && j12.GetCompound("Entity")?.GetLongOrNull("UUIDLeast") == least,
            "→ 1.12: AttachMost/Least and the horse's UUIDMost/Least, the same");
        var t8 = TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.Name.EndsWith("1.8 – 1.8.9", StringComparison.Ordinal));
        Program.Check(!Players.ToJava(canon, 0, t8).ContainsKey("RootVehicle"), "→ 1.8: no RootVehicle (the conversion stands the horse in the world instead)");
        // 1.8: what the player sits on, inside them.
        var old = new NbtCompound
        {
            ["Pos"] = Doubles(1.5, 65.2, 1.5), ["Dimension"] = new NbtInt(0),
            ["Riding"] = new NbtCompound { ["id"] = new NbtString("EntityHorse"), ["Type"] = new NbtInt(1), ["Pos"] = Doubles(1.5, 64, 1.5), ["UUIDMost"] = new NbtLong(most), ["UUIDLeast"] = new NbtLong(least) },
        };
        var fromOld = Players.FromJava("Player", old, 0).Data.GetCompound("RootVehicle");
        Program.Check(fromOld?.GetCompound("Entity")?.GetString("id") == "minecraft:donkey" && fromOld["Attach"] is NbtIntArray { Value: [7, 8, 9, 10] } && fromOld.GetCompound("Entity")?["UUID"] is NbtIntArray { Value: [7, 8, 9, 10] },
            $"1.8 player riding a donkey (EntityHorse Type 1) → canonical mount, seated on it ({fromOld?.GetCompound("Entity")?.GetString("id")})");
    }

    static void Samples()
    {
        var roots = new[] { Path.Combine(TestPaths.Data, "amulet"), Path.Combine(TestPaths.Data, "pymclevel"), Path.Combine(TestPaths.Data, "lce") };
        var candidates = roots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "level.dat", SearchOption.AllDirectories).Select(Path.GetDirectoryName).Concat(Directory.EnumerateFiles(r, "savegame.dat", SearchOption.AllDirectories)))
            .OfType<string>().Distinct().ToList();
        int worlds = 0, entities = 0, stacked = 0, old = 0;
        foreach (var path in candidates)
        {
            SaveWorld w;
            try { w = WorldLoader.Open(path); }
            catch (Exception) { continue; }
            using (w)
            {
                if (w is not (JavaWorld or MinecraftStudio.Core.LegacyConsole.ConsoleWorld)) continue;
                worlds++;
                foreach (var dim in w.Dimensions)
                {
                    var reader = new CanonicalReader(w, dim);
                    foreach (var pos in dim.ChunkPositions)
                    {
                        CanonicalChunk? c;
                        try { c = reader.Get(pos); } catch (Exception) { continue; }
                        foreach (var e in c?.Entities ?? [])
                        {
                            entities++;
                            if (Riders(e).Count > 0) stacked++;
                            if (HasOldStack(e)) old++;
                        }
                    }
                }
            }
        }
        Program.Check(worlds > 0 && old == 0, $"{worlds} Java and console sample worlds, {entities:N0} entities read, {stacked} carrying riders, {old} with an old stack left");
        Program.Info($"{worlds} Java and console sample worlds, {entities:N0} entities, {stacked} carrying riders");
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    static void Conversions()
    {
        string source = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        string temp = Path.Combine(TestPaths.Output, "riding", DateTime.Now.ToString("HHmmss"));
        string copy = Path.Combine(temp, "source");
        CopyDir(source, copy);
        HardcoreRestore.Spot spot;
        int[] horseId = [70, 71, 72, 73];
        using (var w = WorldLoader.Open(copy))
        {
            var p = w.Players.First(r => Path.GetFileName(r.Source).Equals("level.dat", StringComparison.OrdinalIgnoreCase));
            spot = HardcoreRestore.SafeSpot(w, null, besideBed: false) ?? throw new InvalidDataException("no spot");
            HardcoreRestore.MoveTo(w, p, spot);
            var horse = new NbtCompound
            {
                ["id"] = new NbtString("minecraft:horse"), ["Pos"] = Doubles(spot.X + 0.5, spot.Y, spot.Z + 0.5), ["Motion"] = Doubles(0, 0, 0),
                ["Rotation"] = new NbtList(NbtType.Float) { new NbtFloat(45), new NbtFloat(0) }, ["UUID"] = new NbtIntArray(horseId), ["Health"] = new NbtFloat(25),
                ["Tame"] = new NbtByte(1), ["Variant"] = new NbtInt(3), ["CustomName"] = new NbtString("{\"text\":\"Sugar\"}"), ["Fire"] = new NbtShort(-1),
                ["SaddleItem"] = new NbtCompound { ["id"] = new NbtString("minecraft:saddle"), ["Count"] = new NbtByte(1) },
                ["ArmorItem"] = new NbtCompound { ["id"] = new NbtString("minecraft:iron_horse_armor"), ["Count"] = new NbtByte(1) },
            };
            p.Data["RootVehicle"] = new NbtCompound { ["Attach"] = new NbtIntArray(horseId), ["Entity"] = horse };
            // A parrot on the left shoulder, and a thrown ender pearl (1.21.2+ saves it with the player) over the same chunk.
            p.Data["ShoulderEntityLeft"] = new NbtCompound
            {
                ["id"] = new NbtString("minecraft:parrot"), ["Variant"] = new NbtInt(2), ["UUID"] = new NbtIntArray([120, 121, 122, 123]), ["Health"] = new NbtFloat(6),
                ["CustomName"] = new NbtString("{\"text\":\"Polly\"}"), ["Pos"] = Doubles(spot.X + 0.5, spot.Y + 1.4, spot.Z + 0.5),
            };
            p.Data["ender_pearls"] = new NbtList(NbtType.Compound)
            {
                new NbtCompound
                {
                    ["id"] = new NbtString("minecraft:ender_pearl"), ["Pos"] = Doubles((spot.X >> 4) * 16 + 12.5, spot.Y + 3, (spot.Z >> 4) * 16 + 8.5),
                    ["Motion"] = Doubles(0, 0, 0), ["UUID"] = new NbtIntArray([130, 131, 132, 133]), ["ender_pearl_dimension"] = new NbtString("minecraft:overworld"),
                },
            };
            PlayerStore.Save(w, p);
            // In the chunk: a villager in a minecart, and a boat with a villager and a pig in it.
            var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var at = new ChunkPos(spot.X >> 4, spot.Z >> 4);
            var nbt = dim.LoadChunkNbt(at) ?? throw new InvalidDataException("the spot's chunk isn't stored");
            var level = nbt.GetCompound("Level") ?? nbt;
            if (level.GetList("Entities") is not NbtList stored) level["Entities"] = stored = new NbtList(NbtType.Compound);
            double cx = at.X * 16 + 8.5, cz = at.Z * 16 + 8.5;
            NbtCompound Mob(string id, int n, double x, double y, string? name = null)
            {
                var e = new NbtCompound { ["id"] = new NbtString(id), ["Pos"] = Doubles(x, y, cz), ["Motion"] = Doubles(0, 0, 0), ["UUID"] = new NbtIntArray([n, n, n, n]), ["Health"] = new NbtFloat(10) };
                if (name != null) e["CustomName"] = new NbtString($"{{\"text\":\"{name}\"}}");
                return e;
            }
            var cart = Mob("minecraft:minecart", 80, cx, spot.Y);
            cart.Remove("Health");
            cart["Passengers"] = new NbtList(NbtType.Compound) { Mob("minecraft:villager", 81, cx, spot.Y + 0.3, "Carter") };
            var boat = Mob("minecraft:boat", 90, cx + 2, spot.Y);
            boat.Remove("Health");
            boat.SetString("Type", "oak");
            boat["Passengers"] = new NbtList(NbtType.Compound) { Mob("minecraft:villager", 91, cx + 2, spot.Y + 0.2, "Rower"), Mob("minecraft:pig", 92, cx + 2, spot.Y + 0.2, "Hamlet") };
            // A camel (Java 1.20+, Bedrock) carrying a villager, and a giant (Java only) carrying a zombie.
            var camel = Mob("minecraft:camel", 100, cx - 2, spot.Y);
            camel["Passengers"] = new NbtList(NbtType.Compound) { Mob("minecraft:villager", 101, cx - 2, spot.Y + 1.9, "Hump") };
            var giant = Mob("minecraft:giant", 110, cx - 4, spot.Y);
            giant["Passengers"] = new NbtList(NbtType.Compound) { Mob("minecraft:zombie", 111, cx - 4, spot.Y + 10, "Shorty") };
            stored.Add(cart);
            stored.Add(boat);
            stored.Add(camel);
            stored.Add(giant);
            dim.SaveChunkNbt(at, nbt);
        }
        // Who sits in what, read back canonical from a world: "vehicle[riders]" for each entity carrying riders, named ones alone.
        static string Seats(SaveWorld o, ChunkPos at)
        {
            static string Name(NbtCompound e) => e.GetString("CustomName") is string n ? TextComponents.ToPlain(n)
                : (e.GetString("id") ?? "?").Replace("minecraft:", "") is var id && id.Contains("boat") ? "boat" : id;
            var dim = o.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var c = new CanonicalReader(o, dim).Get(at);
            var parts = new List<string>();
            foreach (var e in c?.Entities ?? [])
            {
                var riders = Riders(e);
                if (riders.Count > 0) parts.Add($"{Name(e)}[{string.Join(",", riders.Select(Name))}]");
                else if (e.ContainsKey("CustomName")) parts.Add(Name(e));
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join(" ", parts);
        }
        foreach (var (name, kind, keeps) in new[] { ("1.20.3 – 1.20.4", TargetKind.Java, true), ("1.12 – 1.12.2", TargetKind.Java, true), ("1.8 – 1.8.9", TargetKind.Java, false), ("1.21.120 – 26.x", TargetKind.Bedrock, false) })
        {
            var target = TargetCatalog.All.First(t => t.Kind == kind && t.Name.EndsWith(name, StringComparison.Ordinal));
            string label = target.Name;
            string outDir = Path.Combine(temp, string.Concat(label.Where(char.IsLetterOrDigit)));
            try
            {
                ConversionReport report;
                using (var w = WorldLoader.Open(copy))
                    report = WorldConverter.Run(w, target, outDir, new ConversionOptions(), new Progress<ConversionProgress>(_ => { }));
                var horseLosses = report.Losses.Where(l => l.Lost && (l.What.Contains("horse", StringComparison.OrdinalIgnoreCase) || l.What.Contains("rider", StringComparison.OrdinalIgnoreCase))).ToList();
                Program.Check(horseLosses.Count == 0, $"{label}: nothing about the horse reported lost{(horseLosses.Count > 0 ? ": " + string.Join("; ", horseLosses.Select(l => l.What + " " + l.Outcome)) : "")}");
                bool said = report.Losses.Any(l => !l.Lost && l.Outcome.Contains("was riding it", StringComparison.Ordinal));
                foreach (var l in report.Losses.Where(l => l.Outcome.Contains("riding", StringComparison.Ordinal) || l.What.Contains("rider", StringComparison.Ordinal)))
                    Program.Info($"{label}: {l.What}: {l.Outcome}{(l.Lost ? " [lost]" : "")}");
                Program.Check(said == !keeps, $"{label}: the report {(keeps ? "says nothing about it (still riding)" : "says the horse stands where they were riding it")}");
                using var o = WorldLoader.Open(report.OpenPath ?? outDir);
                var host = o.Players.FirstOrDefault();
                var at = new ChunkPos(spot.X >> 4, spot.Z >> 4);
                // Riders in the world: one seat each before 1.9 (the pig got off), two in a boat since; on what a version
                // lacks (a camel before 1.20, a giant on Bedrock) they got off and stand there; Sugar stands where the
                // player was wherever she can't be ridden. This 1.16.5 world's old terrain is written for 1.20 as 1.17.1
                // chunks and entities on purpose (the game upgrades them), so there is no camel there either.
                string seats = Seats(o, at), expected = name switch
                {
                    "1.20.3 – 1.20.4" => "Hump boat[Rower,Hamlet] giant[Shorty] minecart[Carter]",
                    "1.12 – 1.12.2" => "Hump boat[Rower,Hamlet] giant[Shorty] minecart[Carter]",
                    "1.8 – 1.8.9" => "Hamlet Hump Sugar boat[Rower] giant[Shorty] minecart[Carter]",
                    _ => "Polly Shorty Sugar boat[Rower,Hamlet] camel[Hump] minecart[Carter]",
                };
                Program.Check(seats == expected, $"{label}: riders in the world read back as \"{expected}\" (got \"{seats}\")");
                // Polly stays on the shoulder where the version has parrots on players (Java 1.12+); Bedrock has her beside
                // the player (above); 1.8 has no parrots at all, which the report says.
                var shoulder = host?.Data.GetCompound("ShoulderEntityLeft");
                string? polly = shoulder?["CustomName"] is NbtTag pn ? TextComponents.ToPlain(TextComponents.FromJavaTag(pn)) : null;
                if (kind == TargetKind.Java && target.DataVersion >= Players.ShouldersKept)
                    Program.Check(shoulder?.GetString("id") == "minecraft:parrot" && polly == "Polly", $"{label}: Polly is still on the left shoulder ({shoulder?.GetString("id")}, {polly})");
                else Program.Check(shoulder == null || shoulder.Count == 0, $"{label}: no parrot left on the shoulder");
                if (name == "1.8 – 1.8.9")
                    Program.Check(report.Losses.Any(l => l.Lost && l.What.Contains("Polly", StringComparison.Ordinal)), $"{label}: the report says Polly could not come (no parrots before 1.12)");
                // The thrown pearl is in the world where it was, wherever the player is written in a layout without it.
                var pearls = new CanonicalReader(o, o.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld")).Get(at)?.Entities
                    .Count(e => e.GetString("id") == "minecraft:ender_pearl") ?? 0;
                Program.Check(pearls == 1 && host?.Data.ContainsKey("ender_pearls") != true, $"{label}: the thrown pearl is in the world where it was ({pearls} there)");
                if (target.Kind == TargetKind.Java && target.DataVersion < Players.MountsKept)
                {
                    // Written the old way: the rider holding what it rides.
                    var raw = o.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld").LoadChunkNbt(at);
                    var list = (raw?.GetCompound("Level") ?? raw)?.GetList("Entities")?.OfType<NbtCompound>().ToList() ?? [];
                    bool InOld(string name, string vehicle) => list.Any(e => e.GetString("CustomName") == name && e.GetCompound("Riding")?.GetString("id") == vehicle && !e.ContainsKey("Passengers"));
                    Program.Check(InOld("Carter", "MinecartRideable") && InOld("Rower", "Boat") && InOld("Shorty", "Giant") && list.Any(e => e.GetString("CustomName") == "Hamlet" && !e.ContainsKey("Riding"))
                        && list.All(e => !e.ContainsKey("Passengers")),
                        $"{label}: stored the old way — Carter holding the minecart, Rower the boat, Hamlet standing ({string.Join(", ", list.Select(e => e.GetString("id") + (e.GetCompound("Riding") is NbtCompound r ? "→" + r.GetString("id") : "")))})");
                }
                if (keeps)
                {
                    var rv = host?.Data.GetCompound("RootVehicle");
                    var mount = rv?.GetCompound("Entity");
                    bool seat = target.DataVersion >= 2515
                        ? rv?["Attach"] is NbtIntArray { Value: [70, 71, 72, 73] } && mount?["UUID"] is NbtIntArray { Value: [70, 71, 72, 73] }
                        : rv?.GetLongOrNull("AttachMost") is long am && mount?.GetLongOrNull("UUIDMost") == am && rv.GetLongOrNull("AttachLeast") == mount.GetLongOrNull("UUIDLeast");
                    string? named = mount?["CustomName"] is NbtTag cn ? TextComponents.ToPlain(TextComponents.FromJavaTag(cn)) : null;
                    Program.Check(mount != null && (mount.GetString("id") is "minecraft:horse" or "EntityHorse") && seat && named == "Sugar",
                        $"{label}: still riding Sugar (id {mount?.GetString("id")}, seated {seat}, name {named})");
                }
                else
                {
                    var dim = o.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                    var chunk = new CanonicalReader(o, dim).Get(new ChunkPos(spot.X >> 4, spot.Z >> 4));
                    var found = chunk?.Entities.FirstOrDefault(e => e.GetString("id") == "minecraft:horse" && e.GetString("CustomName") is string n && TextComponents.ToPlain(n) == "Sugar");
                    bool saddled = found != null && Entities.CarriedItems(found).Any(i => i.GetString("id") == "minecraft:saddle");
                    var pos = found?.GetList("Pos");
                    bool there = pos != null && Math.Abs(pos[0].AsDouble - (spot.X + 0.5)) < 0.01 && Math.Abs(pos[2].AsDouble - (spot.Z + 0.5)) < 0.01;
                    Program.Check(found != null && saddled && there, $"{label}: Sugar stands in the world where the player was riding (found {found != null}, saddled {saddled}, there {there})");
                    Program.Check(host?.Data.ContainsKey("RootVehicle") != true && host?.Data.ContainsKey("Riding") != true, $"{label}: the player isn't holding a mount the game can't seat");
                }
            }
            catch (Exception ex) { Program.Check(false, $"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
        }
        try { Directory.Delete(temp, true); } catch (IOException) { }
    }
}
