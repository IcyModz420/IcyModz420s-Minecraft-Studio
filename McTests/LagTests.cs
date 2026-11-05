// McTests/LagTests.cs: "lag" — the lag machine finder (LagMachines) on worlds with lag machines built in beside the ordinary
// redstone, farms and storage that must not count.
// Owns: LagTests — the sample worlds as they are hold none; then into copies of a Java 1.16 world, the same world converted
// to Java 1.20 (block_ticks, entities/ folder) and a Bedrock world, each thing in a generated chunk of its own (three chunks
// apart, written by the world's own writer through StructurePlacement.EditIntoCopy; waiting updates put into the chunks' own
// NBT): solid cubes of pistons (with a two-observer clock and waiting updates), observers, hoppers and dispensers, a dust
// carpet, piles of minecarts, chickens, armour stands, boats and falling sand in one block, floods of dropped items and lit
// TNT, a summoning command block, a book ban, stacked item frames, a running sparse observer field — and a piston door, a
// hopper sorter, a hopper floor, an observer cane farm, a map-art wall, chickens in a pen, minecarts on a track, a storage
// room, a night-vision command block, decorative dust, a lamp wall, a piston/slime block, a running clock, a book chest,
// flowing water, a farm's spilled items and a sand cave-in. Each machine must be found at its own chunk as its kind, and
// nothing anywhere else. The worlds are copies; nothing outside the test folder changes.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class LagTests
{
    const int Y0 = 100;

    /// <summary>One thing to build in a chunk of its own: what is expected to be found there (null: nothing).</summary>
    sealed record Build(string Name, LagKind? Expect, Action<CanonicalChunk, Random> Make, bool JavaOnly = false,
        int RedstoneTicks = 0, int FluidTicks = 0, string? ExpectInEvidence = null);

    public static void Run(string[] args)
    {
        string temp = Path.Combine(TestPaths.Output, "lag", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(temp);
        Console.WriteLine("== The sample worlds as they are: no lag machines");
        foreach (var rel in new[]
        {
            Path.Combine("amulet", "java", "vanilla", "1_12_2"), Path.Combine("amulet", "java", "vanilla", "1_13"),
            Path.Combine("amulet", "java", "vanilla", "1_16_5_dimension"), Path.Combine("amulet", "bedrock", "vanilla", "1_16"),
            Path.Combine("amulet", "bedrock", "vanilla", "1_18", "vanilla"), Path.Combine("lce", "mcsaves_x360_fgh_tu70", "savegame.dat"),
            Path.Combine("pymclevel", "testfiles", "PocketWorld"), Path.Combine("3ds", "1.9"),
        })
        {
            string path = Path.Combine(TestPaths.Data, rel);
            if (!File.Exists(path) && !Directory.Exists(path)) { Console.WriteLine($"   ({rel} not found: skipped)"); continue; }
            using var w = WorldLoader.Open(path);
            var scan = LagMachines.Find(w, w.Dimensions);
            Program.Check(scan.Machines.Count == 0, $"{rel}: no lag machines in {scan.ChunksRead:N0} chunks"
                + string.Concat(scan.Machines.Take(3).Select(m => $" [{m.Title}: {m.What} at {m.X}, {m.Y}, {m.Z}]")));
        }

        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        string bedrock = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla");
        if (Directory.Exists(java))
        {
            Console.WriteLine("== Java 1.16: lag machines beside ordinary builds");
            One("Java 1.16", java, Path.Combine(temp, "java16"));
            Console.WriteLine("== Java 1.20 (block_ticks, entities in their own files)");
            string converted = Path.Combine(temp, "java120-source");
            using (var w = WorldLoader.Open(java))
            {
                var target = TargetCatalog.All.Where(t => t.Kind == TargetKind.Java && t.DataVersion >= 3463 && t.DataVersion <= 3700).OrderByDescending(t => t.DataVersion).First();
                var report = WorldConverter.Run(w, target, converted, new ConversionOptions { Dimensions = new HashSet<string> { "minecraft:overworld" } }, null);
                converted = report.OpenPath ?? converted;
            }
            One("Java 1.20", converted, Path.Combine(temp, "java120"));
        }
        if (Directory.Exists(bedrock))
        {
            Console.WriteLine("== Bedrock 1.18");
            One("Bedrock", bedrock, Path.Combine(temp, "bedrock"));
        }
    }

    static void One(string label, string source, string output)
    {
        bool isBedrock;
        List<ChunkPos> slots;
        var builds = Builds();
        using (var w = WorldLoader.Open(source))
        {
            isBedrock = w is BedrockWorld;
            builds = builds.Where(b => !(isBedrock && b.JavaOnly)).ToList();
            var dim = Overworld(w);
            slots = Slots(dim, builds.Count);
            if (slots.Count < builds.Count) { Program.Check(false, $"{label}: room for {builds.Count} builds (found {slots.Count} free chunks)"); return; }
            var plan = builds.Zip(slots).ToDictionary(p => p.Second, p => p.First);
            var rnd = new Random(42);
            var result = StructurePlacement.EditIntoCopy(w, "minecraft:overworld", slots, c => plan[new ChunkPos(c.X, c.Z)].Make(c, rnd), output);
            Program.Check(result.Verified, $"{label}: {builds.Count} builds written into a copy ({string.Join("; ", result.Problems.Take(3))})");
            output = result.OpenPath ?? output;
        }
        // Waiting updates go into the chunks' own NBT.
        using (var w = WorldLoader.Open(output))
        {
            var dim = Overworld(w);
            foreach (var (b, pos) in builds.Zip(slots))
            {
                if (b.RedstoneTicks > 0) AddTicks(dim, pos, b.RedstoneTicks, "observer", fluid: false);
                if (b.FluidTicks > 0) AddTicks(dim, pos, b.FluidTicks, "water", fluid: true);
            }
        }
        using (var w = WorldLoader.Open(output))
        {
            var scan = LagMachines.Find(w, w.Dimensions);
            foreach (var m in scan.Machines) Program.Info($"{label}: {m.Title} at {m.X}, {m.Y}, {m.Z}: {m.What}" + (m.Evidence.Count > 0 ? " · " + string.Join(" · ", m.Evidence) : ""));
            var claimed = new HashSet<LagMachine>();
            foreach (var (b, pos) in builds.Zip(slots))
            {
                var here = scan.Machines.Where(m => Inside(m, pos)).ToList();
                claimed.UnionWith(here);
                if (b.Expect is LagKind kind)
                {
                    bool found = here.Count == 1 && here[0].Kind == kind && (b.ExpectInEvidence == null || here[0].Evidence.Any(e => e.Contains(b.ExpectInEvidence, StringComparison.Ordinal)));
                    Program.Check(found, $"{label}: {b.Name} found as \"{LagMachines.Title(kind)}\"" + (b.ExpectInEvidence != null ? $", with \"{b.ExpectInEvidence}\" among the evidence" : "")
                        + $" (found: {(here.Count == 0 ? "nothing" : string.Join("; ", here.Select(m => $"{m.Title}: {m.What} | {string.Join(" | ", m.Evidence)}")))})");
                }
                else Program.Check(here.Count == 0, $"{label}: {b.Name}: not a lag machine (found: {(here.Count == 0 ? "nothing" : string.Join("; ", here.Select(m => $"{m.Title}: {m.What}")))})");
            }
            var stray = scan.Machines.Where(m => !claimed.Contains(m)).ToList();
            Program.Check(stray.Count == 0, $"{label}: nothing found anywhere else ({string.Join("; ", stray.Take(3).Select(m => $"{m.Title} at {m.X}, {m.Y}, {m.Z}: {m.What}"))})");
        }

        // Cleaned up in a copy: none of the machines is found again, the ordinary builds stay as they were, and the
        // chicken pile keeps the 24 the game allows in one block (the named one among them).
        string cleaned = output + "-clean";
        using (var w = WorldLoader.Open(output))
        {
            var machines = LagMachines.Find(w, [Overworld(w)]).Machines;
            var clean = LagMachines.CleanIntoCopy(w, machines, cleaned);
            foreach (var n in clean.Notes) Program.Info($"{label} clean-up: {n}");
            Program.Check(clean.Verified && machines.Count > 0, $"{label}: the {machines.Count} lag machines cleaned up in a copy, none found there again{(clean.Verified ? "" : ": " + string.Join("; ", clean.Problems.Take(4)))}");
            cleaned = clean.OpenPath;
        }
        using (var a = WorldLoader.Open(output))
        using (var b = WorldLoader.Open(cleaned))
        {
            var after = LagMachines.Find(b, b.Dimensions);
            Program.Check(after.Machines.Count == 0, $"{label}: the cleaned copy holds no lag machine at all ({string.Join("; ", after.Machines.Take(3).Select(m => $"{m.Title} at {m.X}, {m.Y}, {m.Z}: {m.What}"))})");
            var ra = new CanonicalReader(a, Overworld(a));
            var rb = new CanonicalReader(b, Overworld(b));
            var changed = new List<string>();
            foreach (var (build, pos) in builds.Zip(slots).Where(p => p.First.Expect == null))
            {
                var ca = ra.Get(pos);
                var cb = rb.Get(pos);
                if (ca == null || cb == null || ca.Entities.Count != cb.Entities.Count || ca.BlockEntities.Count != cb.BlockEntities.Count || !SameBlocks(ca, cb)) changed.Add(build.Name);
            }
            Program.Check(changed.Count == 0, $"{label}: every ordinary build is untouched by the clean-up{(changed.Count == 0 ? "" : " (changed: " + string.Join(", ", changed) + ")")}");
            var pile = slots[builds.FindIndex(x => x.Expect == LagKind.MobPile)];
            var chickens = rb.Get(pile)!.Entities.Where(e => e.GetString("id") == "minecraft:chicken").ToList();
            Program.Check(chickens.Count == 24 && chickens.Any(e => e["CustomName"] != null), $"{label}: the chicken pile keeps the 24 the game allows in one block, the named one among them ({chickens.Count} left)");
        }
    }

    static bool SameBlocks(CanonicalChunk a, CanonicalChunk b)
    {
        foreach (var sy in a.Sections.Keys.Union(b.Sections.Keys))
            for (int y = sy * 16; y < sy * 16 + 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        if (a.GetBlock(x, y, z) != b.GetBlock(x, y, z)) return false;
        return true;
    }

    static WorldDimension Overworld(SaveWorld w) => w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");

    // The machine lies in the chunk (or reaches into it from right beside it).
    static bool Inside(LagMachine m, ChunkPos pos) =>
        m.Box.MaxX >= pos.X * 16 - 4 && m.Box.MinX <= pos.X * 16 + 19 && m.Box.MaxZ >= pos.Z * 16 - 4 && m.Box.MinZ <= pos.Z * 16 + 19;

    /// <summary>Chunks the world has, with all eight around them too, at least three chunks from each other.</summary>
    static List<ChunkPos> Slots(WorldDimension dim, int count)
    {
        var have = dim.ChunkPositions.ToHashSet();
        var slots = new List<ChunkPos>();
        foreach (var p in have.OrderBy(p => p.X).ThenBy(p => p.Z))
        {
            bool around = true;
            for (int dz = -1; dz <= 1 && around; dz++)
                for (int dx = -1; dx <= 1 && around; dx++)
                    around = have.Contains(new ChunkPos(p.X + dx, p.Z + dz));
            if (!around || slots.Any(s => Math.Max(Math.Abs(s.X - p.X), Math.Abs(s.Z - p.Z)) < 3)) continue;
            slots.Add(p);
            if (slots.Count == count) break;
        }
        return slots;
    }

    /// <summary>Waiting updates of a block put into the chunk's own NBT, on blocks of the chunk at Y0 and above.</summary>
    static void AddTicks(WorldDimension dim, ChunkPos pos, int n, string block, bool fluid)
    {
        var nbt = dim.LoadChunkNbt(pos) ?? throw new InvalidDataException($"chunk {pos} has no NBT");
        var at = Enumerable.Range(0, n).Select(i => (X: pos.X * 16 + i % 16, Y: Y0 + i / 256, Z: pos.Z * 16 + i / 16 % 16));
        if (dim is BedrockDimension)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var (x, y, z) in at)
                list.Add(new NbtCompound
                {
                    ["blockState"] = new NbtCompound { ["name"] = new NbtString("minecraft:" + block), ["states"] = new NbtCompound(), ["version"] = new NbtInt(18100737) },
                    ["time"] = new NbtLong(1), ["x"] = new NbtInt(x), ["y"] = new NbtInt(y), ["z"] = new NbtInt(z),
                });
            nbt["PendingTicks"] = new NbtList(NbtType.Compound) { new NbtCompound { ["currentTick"] = new NbtInt(0), ["tickList"] = list } };
        }
        else
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var (x, y, z) in at)
                list.Add(new NbtCompound { ["i"] = new NbtString("minecraft:" + block), ["x"] = new NbtInt(x), ["y"] = new NbtInt(y), ["z"] = new NbtInt(z), ["t"] = new NbtInt(1), ["p"] = new NbtInt(0) });
            if (nbt["Level"] is NbtCompound level) level[fluid ? "LiquidTicks" : "TileTicks"] = list;
            else nbt[fluid ? "fluid_ticks" : "block_ticks"] = list;
        }
        dim.SaveChunkNbt(pos, nbt);
    }

    // ------------------------------------------------------------------ what is built

    static BlockState B(string name, string properties = "") => BlockState.Get("minecraft:" + name, properties);

    static NbtList Doubles(params double[] v) { var l = new NbtList(NbtType.Double); foreach (var d in v) l.Add(new NbtDouble(d)); return l; }

    static NbtCompound Entity(string id, double x, double y, double z, Random rnd, NbtCompound? more = null)
    {
        var e = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:" + id), ["Pos"] = Doubles(x, y, z), ["Motion"] = Doubles(0, 0, 0),
            ["Rotation"] = new NbtList(NbtType.Float) { new NbtFloat(0), new NbtFloat(0) },
            ["UUID"] = new NbtIntArray([rnd.Next(), rnd.Next(), rnd.Next(), rnd.Next()]),
        };
        foreach (var (k, v) in more ?? []) e[k] = v;
        return e;
    }

    static NbtCompound Item(string id, int count = 1) => new() { ["id"] = new NbtString("minecraft:" + id), ["count"] = new NbtInt(count) };

    static NbtCompound BlockEntity(string id, CanonicalChunk c, int lx, int y, int lz, NbtCompound? more = null)
    {
        var be = new NbtCompound { ["id"] = new NbtString("minecraft:" + id), ["x"] = new NbtInt(c.X * 16 + lx), ["y"] = new NbtInt(y), ["z"] = new NbtInt(c.Z * 16 + lz) };
        foreach (var (k, v) in more ?? []) be[k] = v;
        return be;
    }

    static void Cube(CanonicalChunk c, int side, BlockState state, string? blockEntity = null)
    {
        for (int y = 0; y < side; y++)
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    c.SetBlock(4 + x, Y0 + y, 4 + z, state);
                    if (blockEntity != null) c.BlockEntities.Add(BlockEntity(blockEntity, c, 4 + x, Y0 + y, 4 + z, new NbtCompound { ["Items"] = new NbtList(NbtType.Compound) }));
                }
    }

    static NbtCompound Book(int pages, int chars, int seed)
    {
        var list = new NbtList(NbtType.Compound);
        string text = new string((char)('a' + seed % 26), chars);
        for (int i = 0; i < pages; i++) list.Add(new NbtCompound { ["raw"] = new NbtString(text) });
        var book = Item("written_book");
        book["components"] = new NbtCompound
        {
            ["minecraft:written_book_content"] = new NbtCompound { ["title"] = new NbtCompound { ["raw"] = new NbtString("Book " + seed) }, ["author"] = new NbtString("Test"), ["pages"] = list },
        };
        return book;
    }

    static NbtCompound Chest(CanonicalChunk c, int lx, int y, int lz, IEnumerable<NbtCompound> items)
    {
        c.SetBlock(lx, y, lz, B("chest", "facing=north,type=single,waterlogged=false"));
        var list = new NbtList(NbtType.Compound);
        int slot = 0;
        foreach (var it in items) { it["Slot"] = new NbtByte((sbyte)slot++); list.Add(it); }
        return BlockEntity("chest", c, lx, y, lz, new NbtCompound { ["Items"] = list });
    }

    static List<Build> Builds() =>
    [
        // ---- lag machines
        new("a 7×7×7 cube of sticky pistons with a two-observer clock and waiting updates", LagKind.PistonMass, (c, _) =>
        {
            Cube(c, 7, B("sticky_piston", "extended=false,facing=up"));
            c.SetBlock(12, Y0, 12, B("observer", "facing=east,powered=false"));
            c.SetBlock(13, Y0, 12, B("observer", "facing=west,powered=false"));
        }, RedstoneTicks: 1500, ExpectInEvidence: "observers watching each other"),
        new("a 6×6×6 cube of observers", LagKind.ObserverMass, (c, _) => Cube(c, 6, B("observer", "facing=north,powered=false"))),
        new("a 6×6×6 cube of hoppers", LagKind.HopperMass, (c, _) => Cube(c, 6, B("hopper", "enabled=true,facing=down"), "hopper")),
        new("a 5×5×5 cube of dispensers", LagKind.DispenserMass, (c, _) => Cube(c, 5, B("dispenser", "facing=up,triggered=false"), "dispenser")),
        new("a 16×16 carpet of redstone dust", LagKind.RedstoneCarpet, (c, _) =>
        {
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    c.SetBlock(x, Y0, z, B("stone"));
                    c.SetBlock(x, Y0 + 1, z, B("redstone_wire", "east=side,north=side,power=0,south=side,west=side"));
                }
        }),
        new("120 minecarts in one block", LagKind.MinecartStack, (c, rnd) =>
        {
            for (int i = 0; i < 120; i++) c.Entities.Add(Entity("minecart", c.X * 16 + 8.5, Y0, c.Z * 16 + 8.5, rnd));
        }),
        new("150 chickens in one block", LagKind.MobPile, (c, rnd) =>
        {
            for (int i = 0; i < 150; i++)
            {
                var more = new NbtCompound { ["Health"] = new NbtFloat(4) };
                // One of them has a name: the clean-up keeps it among the 24 that stay.
                if (i == 149) more["CustomName"] = new NbtString("{\"text\":\"Nugget\"}");
                c.Entities.Add(Entity("chicken", c.X * 16 + 8.5, Y0, c.Z * 16 + 8.5, rnd, more));
            }
        }),
        new("50 armour stands in one block", LagKind.ArmourStandStack, (c, rnd) =>
        {
            for (int i = 0; i < 50; i++) c.Entities.Add(Entity("armor_stand", c.X * 16 + 8.5, Y0, c.Z * 16 + 8.5, rnd));
        }),
        new("50 boats in one block", LagKind.BoatStack, (c, rnd) =>
        {
            for (int i = 0; i < 50; i++) c.Entities.Add(Entity("oak_boat", c.X * 16 + 8.5, Y0, c.Z * 16 + 8.5, rnd));
        }),
        new("60 falling sand in one block", LagKind.FallingBlocks, (c, rnd) =>
        {
            for (int i = 0; i < 60; i++)
                c.Entities.Add(Entity("falling_block", c.X * 16 + 8.5, Y0 + 0.3, c.Z * 16 + 8.5, rnd, new NbtCompound { ["BlockState"] = new NbtCompound { ["Name"] = new NbtString("minecraft:sand") }, ["Time"] = new NbtInt(5) }));
        }),
        new("2,500 dropped items", LagKind.DroppedItems, (c, rnd) =>
        {
            for (int i = 0; i < 2500; i++)
            {
                var item = Item(i % 2 == 0 ? "cobblestone" : "dirt");
                c.Entities.Add(Entity("item", c.X * 16 + rnd.NextDouble() * 16, Y0, c.Z * 16 + rnd.NextDouble() * 16, rnd, new NbtCompound { ["Item"] = item, ["Age"] = new NbtShort(10) }));
            }
        }),
        new("80 lit TNT", LagKind.LitTnt, (c, rnd) =>
        {
            for (int i = 0; i < 80; i++) c.Entities.Add(Entity("tnt", c.X * 16 + 2 + i % 12, Y0, c.Z * 16 + 2 + i / 12, rnd, new NbtCompound { ["fuse"] = new NbtShort(60) }));
        }),
        new("a repeating command block summoning every tick", LagKind.SummoningCommandBlock, (c, _) =>
        {
            c.SetBlock(8, Y0, 8, B("repeating_command_block", "conditional=false,facing=up"));
            c.BlockEntities.Add(BlockEntity("command_block", c, 8, Y0, 8, new NbtCompound
            {
                ["Command"] = new NbtString("summon minecraft:armor_stand ~ ~1 ~"), ["auto"] = new NbtByte(1), ["powered"] = new NbtByte(0),
                ["conditionMet"] = new NbtByte(1), ["TrackOutput"] = new NbtByte(1), ["SuccessCount"] = new NbtInt(0),
            }));
        }),
        new("a chest of 27 full books (a book ban)", LagKind.BookBan, (c, _) =>
            c.BlockEntities.Add(Chest(c, 8, Y0, 8, Enumerable.Range(0, 27).Select(i => Book(100, 450, i))))),
        new("12 item frames on one face", LagKind.StackedFrames, (c, rnd) =>
        {
            c.SetBlock(8, Y0, 8, B("stone"));
            for (int i = 0; i < 12; i++)
                c.Entities.Add(Entity("item_frame", c.X * 16 + 8.5, Y0 + 0.5, c.Z * 16 + 9.03125, rnd, new NbtCompound
                {
                    ["TileX"] = new NbtInt(c.X * 16 + 8), ["TileY"] = new NbtInt(Y0), ["TileZ"] = new NbtInt(c.Z * 16 + 9), ["Facing"] = new NbtByte(3),
                }));
        }, JavaOnly: true),
        new("a running field of observers (half of a floor, 1,200 updates waiting)", LagKind.WaitingUpdates, (c, _) =>
        {
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                    if ((x + z) % 2 == 0) c.SetBlock(x, Y0, z, B("observer", "facing=up,powered=false"));
        }, RedstoneTicks: 1200),

        // ---- ordinary builds
        new("a 2×2 piston door", null, (c, _) =>
        {
            for (int y = 0; y < 2; y++)
            {
                c.SetBlock(5, Y0 + y, 8, B("sticky_piston", "extended=false,facing=east"));
                c.SetBlock(10, Y0 + y, 8, B("sticky_piston", "extended=false,facing=west"));
                c.SetBlock(6, Y0 + y, 8, B("stone"));
                c.SetBlock(9, Y0 + y, 8, B("stone"));
            }
            for (int x = 3; x < 13; x++) c.SetBlock(x, Y0 - 1, 9, B("redstone_wire", "east=side,north=none,power=0,south=none,west=side"));
            c.SetBlock(3, Y0 - 1, 10, B("repeater", "delay=1,facing=north,locked=false,powered=false"));
        }, RedstoneTicks: 6),
        new("a 16-slice hopper sorter", null, (c, _) =>
        {
            for (int x = 0; x < 16; x++)
            {
                c.SetBlock(x, Y0 + 3, 8, B("hopper", "enabled=true,facing=west"));
                c.SetBlock(x, Y0 + 2, 8, B("hopper", "enabled=true,facing=down"));
                c.SetBlock(x, Y0 + 1, 8, B("hopper", "enabled=true,facing=down"));
                c.SetBlock(x, Y0, 8, B("chest", "facing=north,type=single,waterlogged=false"));
                c.SetBlock(x, Y0 + 2, 9, B("comparator", "facing=north,mode=compare,powered=false"));
                c.SetBlock(x, Y0 + 2, 10, B("redstone_wire", "east=none,north=side,power=0,south=none,west=none"));
                c.SetBlock(x, Y0 + 1, 9, B("redstone_torch", "lit=true"));
                foreach (int y in new[] { 1, 2, 3 }) c.BlockEntities.Add(BlockEntity("hopper", c, x, Y0 + y, 8, new NbtCompound { ["Items"] = new NbtList(NbtType.Compound) }));
                c.BlockEntities.Add(Chest(c, x, Y0, 8, []));
            }
        }),
        new("a 16×16 hopper floor under a farm", null, (c, _) =>
        {
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    c.SetBlock(x, Y0, z, B("hopper", "enabled=true,facing=down"));
                    c.BlockEntities.Add(BlockEntity("hopper", c, x, Y0, z, new NbtCompound { ["Items"] = new NbtList(NbtType.Compound) }));
                }
        }),
        new("an observer sugar cane farm", null, (c, _) =>
        {
            for (int x = 0; x < 16; x++)
            {
                c.SetBlock(x, Y0, 8, B("grass_block", "snowy=false"));
                c.SetBlock(x, Y0 + 1, 8, B("sugar_cane", "age=0"));
                c.SetBlock(x, Y0 + 2, 9, B("sticky_piston", "extended=false,facing=north"));
                c.SetBlock(x, Y0 + 3, 8, B("observer", "facing=down,powered=false"));
            }
        }, RedstoneTicks: 12),
        new("a 10×10 map-art wall of item frames", null, (c, rnd) =>
        {
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                {
                    c.SetBlock(3 + x, Y0 + y, 8, B("stone"));
                    c.Entities.Add(Entity("item_frame", c.X * 16 + 3.5 + x, Y0 + y + 0.5, c.Z * 16 + 9.03125, rnd, new NbtCompound
                    {
                        ["TileX"] = new NbtInt(c.X * 16 + 3 + x), ["TileY"] = new NbtInt(Y0 + y), ["TileZ"] = new NbtInt(c.Z * 16 + 9), ["Facing"] = new NbtByte(3),
                    }));
                }
        }, JavaOnly: true),
        new("a chicken farm (20 in one block, 60 in a pen)", null, (c, rnd) =>
        {
            for (int i = 0; i < 20; i++) c.Entities.Add(Entity("chicken", c.X * 16 + 2.5, Y0, c.Z * 16 + 2.5, rnd, new NbtCompound { ["Health"] = new NbtFloat(4) }));
            for (int i = 0; i < 60; i++) c.Entities.Add(Entity("chicken", c.X * 16 + 4.5 + i % 10, Y0, c.Z * 16 + 4.5 + i / 10, rnd, new NbtCompound { ["Health"] = new NbtFloat(4) }));
        }),
        new("minecarts on a track", null, (c, rnd) =>
        {
            for (int x = 0; x < 10; x++)
            {
                c.SetBlock(3 + x, Y0, 8, B("rail", "shape=east_west,waterlogged=false"));
                c.Entities.Add(Entity("minecart", c.X * 16 + 3.5 + x, Y0, c.Z * 16 + 8.5, rnd));
            }
        }),
        new("a storage room of 40 chests", null, (c, _) =>
        {
            for (int i = 0; i < 40; i++)
                c.BlockEntities.Add(Chest(c, i % 16, Y0 + i / 16, 8, Enumerable.Range(0, 27).Select(_ => Item("cobblestone", 64))));
        }),
        new("a night-vision command block", null, (c, _) =>
        {
            c.SetBlock(8, Y0, 8, B("repeating_command_block", "conditional=false,facing=up"));
            c.BlockEntities.Add(BlockEntity("command_block", c, 8, Y0, 8, new NbtCompound
            {
                ["Command"] = new NbtString("effect give @a minecraft:night_vision 20 0 true"), ["auto"] = new NbtByte(1), ["powered"] = new NbtByte(0),
                ["conditionMet"] = new NbtByte(1), ["TrackOutput"] = new NbtByte(1), ["SuccessCount"] = new NbtInt(0),
            }));
        }),
        new("decorative dust and a long redstone line", null, (c, _) =>
        {
            for (int z = 0; z < 6; z++)
                for (int x = 0; x < 6; x++) { c.SetBlock(x, Y0, z, B("stone")); c.SetBlock(x, Y0 + 1, z, B("redstone_wire", "east=side,north=side,power=0,south=side,west=side")); }
            for (int x = 0; x < 16; x++) { c.SetBlock(x, Y0, 12, B("stone")); c.SetBlock(x, Y0 + 1, 12, x % 8 == 7 ? B("repeater", "delay=1,facing=west,locked=false,powered=false") : B("redstone_wire", "east=side,north=none,power=0,south=none,west=side")); }
        }),
        new("a 16×12 wall of redstone lamps", null, (c, _) =>
        {
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 16; x++) c.SetBlock(x, Y0 + y, 8, B("redstone_lamp", "lit=false"));
        }),
        new("a 4×4×4 block of pistons and slime", null, (c, _) =>
        {
            for (int y = 0; y < 4; y++)
                for (int z = 0; z < 4; z++)
                    for (int x = 0; x < 4; x++) c.SetBlock(6 + x, Y0 + y, 6 + z, (x + y + z) % 2 == 0 ? B("piston", "extended=false,facing=up") : B("slime_block"));
        }),
        new("a running repeater clock", null, (c, _) =>
        {
            for (int x = 6; x < 10; x++) { c.SetBlock(x, Y0, 8, B("stone")); c.SetBlock(x, Y0 + 1, 8, B("repeater", "delay=4,facing=east,locked=false,powered=false")); }
        }, RedstoneTicks: 20),
        new("a chest of ten short books", null, (c, _) => c.BlockEntities.Add(Chest(c, 8, Y0, 8, Enumerable.Range(0, 10).Select(i => Book(5, 200, i))))),
        new("water flowing (300 updates waiting)", null, (c, _) => c.SetBlock(8, Y0, 8, B("water", "level=0")), FluidTicks: 300),
        new("a farm's spilled items (150)", null, (c, rnd) =>
        {
            for (int i = 0; i < 150; i++) c.Entities.Add(Entity("item", c.X * 16 + 6 + rnd.NextDouble() * 4, Y0, c.Z * 16 + 6 + rnd.NextDouble() * 4, rnd, new NbtCompound { ["Item"] = Item("bamboo", 64), ["Age"] = new NbtShort(10) }));
        }),
        new("sand caving in (40 falling, one per column)", null, (c, rnd) =>
        {
            for (int i = 0; i < 40; i++)
                c.Entities.Add(Entity("falling_block", c.X * 16 + 0.5 + i % 16, Y0 + 0.3, c.Z * 16 + 0.5 + i / 16, rnd, new NbtCompound { ["BlockState"] = new NbtCompound { ["Name"] = new NbtString("minecraft:sand") }, ["Time"] = new NbtInt(5) }));
        }),
    ];
}
