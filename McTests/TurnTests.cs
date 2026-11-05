// McTests/TurnTests.cs: "turn" — structures turned and mirrored.
// Owns: TurnTests — block states with known answers (stairs, rails, signs, fences, doors, chests, logs, jigsaws, redstone,
// bookshelves); all 32 thousand Java block states turned and mirrored every way: each result a real state, four quarter
// turns and two mirrors give it back, a north–south mirror and half a turn make the east–west mirror; a real building with
// 112 paintings (Twilight Forest) turned all eleven ways: every painting on the turned wall exactly, and back again to the
// same; a turned building placed into copies of a Java 1.12 and a Bedrock world and its paintings read there; the material
// list (doors and beds once, double slabs twice, wall torches as torches, pots and their plants, buckets, seeds, nothing
// for fire or portals) and a turned file saved in every format reads back the same.
using System.IO.Compression;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class TurnTests
{
    static readonly StructureMirror[] Mirrors = [StructureMirror.None, StructureMirror.NorthSouth, StructureMirror.EastWest];

    public static void Run(string[] args)
    {
        KnownAnswers();
        AllStates();
        Materials();
        string twilight = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_12_2_twilight_forest");
        if (!Directory.Exists(twilight)) { Program.Info("(skipped the building tests: Twilight Forest world not in TestData)"); return; }
        var building = Gallery(twilight);
        Program.Info($"gallery: {building.Describe()}");
        Building(building);
        SaveTurned(building);
        Place(building, "Java 1.12.2", twilight);
        string bedrock = Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16");
        if (Directory.Exists(bedrock)) Place(building, "Bedrock 1.16", bedrock);
    }

    static void Materials()
    {
        var s = new Structure(8, 3, 2);
        void Put(int x, int y, int z, string state) => s.Set(x, y, z, BlockState.Parse(state));
        Put(0, 0, 0, "minecraft:oak_door[facing=north,half=lower,hinge=left,open=false,powered=false]");
        Put(0, 1, 0, "minecraft:oak_door[facing=north,half=upper,hinge=left,open=false,powered=false]");
        Put(1, 0, 0, "minecraft:red_bed[facing=north,occupied=false,part=foot]");
        Put(1, 0, 1, "minecraft:red_bed[facing=north,occupied=false,part=head]");
        Put(2, 0, 0, "minecraft:stone_slab[type=double,waterlogged=false]");
        Put(2, 1, 0, "minecraft:stone_slab[type=bottom,waterlogged=false]");
        Put(3, 0, 0, "minecraft:wall_torch[facing=north]");
        Put(3, 1, 0, "minecraft:torch");
        Put(4, 0, 0, "minecraft:potted_fern");
        Put(5, 0, 0, "minecraft:white_candle[candles=3,lit=false,waterlogged=false]");
        Put(6, 0, 0, "minecraft:water[level=0]");
        Put(6, 1, 0, "minecraft:water[level=3]");
        Put(7, 0, 0, "minecraft:wheat[age=7]");
        Put(7, 1, 0, "minecraft:fire[age=0,east=false,north=false,south=false,up=false,west=false]");
        Put(4, 1, 0, "minecraft:snow[layers=5]");
        Put(5, 1, 0, "minecraft:oak_wall_sign[facing=north,waterlogged=false]");
        Put(4, 2, 0, "minecraft:nether_portal[axis=x]");
        Put(5, 2, 0, "minecraft:sunflower[half=lower]");
        Put(5, 2, 1, "minecraft:sunflower[half=upper]");
        var got = StructureMaterials.Count(s).ToDictionary(l => l.Item.Replace("minecraft:", ""), l => l.Count);
        var expected = new Dictionary<string, long>
        {
            ["oak_door"] = 1, ["red_bed"] = 1, ["stone_slab"] = 3, ["torch"] = 2, ["flower_pot"] = 1, ["fern"] = 1, ["white_candle"] = 3,
            ["water_bucket"] = 1, ["wheat_seeds"] = 1, ["snow"] = 5, ["oak_sign"] = 1, ["sunflower"] = 1,
        };
        bool same = got.Count == expected.Count && expected.All(kv => got.GetValueOrDefault(kv.Key) == kv.Value);
        Program.Check(same, $"material list: doors and beds once, a double slab twice, wall torches and signs as torches and signs, a pot and its fern, three candles, one bucket for still water, seeds, five snow, nothing for fire, flowing water or portals ({string.Join(", ", got.Select(kv => $"{kv.Value} {kv.Key}"))})");
        var big = new MaterialLine("minecraft:stone", 200);
        Program.Check(big.Stacks == "3 stacks + 8" && new MaterialLine("minecraft:oak_sign", 20).Stacks == "1 stack + 4 (of 16)" && new MaterialLine("minecraft:red_bed", 3).Stacks == "3",
            $"stacks: 200 stone is \"{big.Stacks}\", 20 signs \"{new MaterialLine("minecraft:oak_sign", 20).Stacks}\", beds one a slot");
        string csv = StructureMaterials.Csv(StructureMaterials.Count(s));
        Program.Check(csv.StartsWith("Item,Name,Count,Stacks") && csv.Contains("minecraft:stone_slab,stone slab,3,"), "material list as CSV: id, name, count, stacks");
    }

    /// <summary>The gallery turned and mirrored, saved in every format and read back: the same turned blocks.</summary>
    static void SaveTurned(Structure gallery)
    {
        var turned = StructureTransform.Apply(gallery, 3, StructureMirror.NorthSouth);
        string dir = Path.Combine(TestPaths.Output, "turn", "files");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var ext in new[] { ".nbt", ".schem", ".litematic", ".mcstructure" })
            {
                string path = Path.Combine(dir, "gallery" + ext);
                var standIns = StructureFile.SaveChecked(turned, path, 0);
                // Only blocks no file can name (a mod's blocks read from a 1.12 world without its mod) become stand-ins.
                Program.Check(standIns.All(c => c.From.StartsWith("minecraft:legacy_block_", StringComparison.Ordinal)),
                    $"{ext}: the save check lists what became a stand-in, and only blocks no file can name did ({string.Join(", ", standIns.Select(c => $"{c.From} → {c.To} ×{c.Count}"))})");
                var back = StructureFile.Load(path);
                int same = 0;
                var differ = new List<string>();
                for (int i = 0; i < turned.AllBlocks.Count; i++)
                    if (back.AllBlocks[i].BaseName == turned.AllBlocks[i].BaseName || turned.AllBlocks[i].BaseName.StartsWith("legacy_block_", StringComparison.Ordinal)) same++;
                    else if (differ.Count < 3) differ.Add($"{turned.AllBlocks[i]} came back as {back.AllBlocks[i]}");
                foreach (var d in differ) Program.Info($"   {ext}: {d}");
                Program.Check(back.SizeX == turned.SizeX && back.SizeZ == turned.SizeZ && same == turned.AllBlocks.Count && StructureFile.IsStructure(path),
                    $"the turned gallery saved as {ext} reads back as the same turned building ({same:N0} of {turned.AllBlocks.Count:N0} blocks)");
            }
            Program.Check(!StructureFile.IsStructure(Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2", "level.dat")), "a level.dat is not taken for a structure");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    static string T(string state, int turns, StructureMirror m = StructureMirror.None) => StructureTransform.Block(BlockState.Parse(state), turns, m).ToString();

    static void KnownAnswers()
    {
        var ns = StructureMirror.NorthSouth;
        var ew = StructureMirror.EastWest;
        void Is(string state, int turns, StructureMirror m, string expected) =>
            Program.Check(T(state, turns, m) == BlockState.Parse(expected).ToString(), $"{state} turned {turns}, mirror {m} → {expected} (got {T(state, turns, m)})");
        Is("minecraft:oak_stairs[facing=north,half=bottom,shape=inner_left,waterlogged=false]", 1, StructureMirror.None, "minecraft:oak_stairs[facing=east,half=bottom,shape=inner_left,waterlogged=false]");
        Is("minecraft:oak_stairs[facing=north,half=bottom,shape=inner_left,waterlogged=false]", 0, ns, "minecraft:oak_stairs[facing=south,half=bottom,shape=inner_right,waterlogged=false]");
        Is("minecraft:oak_stairs[facing=north,half=top,shape=outer_right,waterlogged=false]", 0, ew, "minecraft:oak_stairs[facing=north,half=top,shape=outer_left,waterlogged=false]");
        Is("minecraft:rail[shape=north_east,waterlogged=false]", 1, StructureMirror.None, "minecraft:rail[shape=south_east,waterlogged=false]");
        Is("minecraft:rail[shape=north_east,waterlogged=false]", 0, ew, "minecraft:rail[shape=north_west,waterlogged=false]");
        Is("minecraft:rail[shape=north_south,waterlogged=false]", 3, StructureMirror.None, "minecraft:rail[shape=east_west,waterlogged=false]");
        Is("minecraft:powered_rail[powered=false,shape=ascending_north,waterlogged=false]", 1, StructureMirror.None, "minecraft:powered_rail[powered=false,shape=ascending_east,waterlogged=false]");
        Is("minecraft:oak_sign[rotation=0,waterlogged=false]", 1, StructureMirror.None, "minecraft:oak_sign[rotation=4,waterlogged=false]");
        Is("minecraft:oak_sign[rotation=2,waterlogged=false]", 0, ns, "minecraft:oak_sign[rotation=6,waterlogged=false]");
        Is("minecraft:oak_sign[rotation=2,waterlogged=false]", 0, ew, "minecraft:oak_sign[rotation=14,waterlogged=false]");
        Is("minecraft:oak_fence[east=false,north=true,south=false,waterlogged=false,west=true]", 1, StructureMirror.None, "minecraft:oak_fence[east=true,north=true,south=false,waterlogged=false,west=false]");
        Is("minecraft:oak_door[facing=east,half=lower,hinge=left,open=false,powered=false]", 0, ns, "minecraft:oak_door[facing=east,half=lower,hinge=right,open=false,powered=false]");
        Is("minecraft:oak_door[facing=east,half=lower,hinge=left,open=false,powered=false]", 0, ew, "minecraft:oak_door[facing=west,half=lower,hinge=right,open=false,powered=false]");
        Is("minecraft:chest[facing=north,type=left,waterlogged=false]", 2, StructureMirror.None, "minecraft:chest[facing=south,type=left,waterlogged=false]");
        Is("minecraft:chest[facing=north,type=left,waterlogged=false]", 0, ew, "minecraft:chest[facing=north,type=right,waterlogged=false]");
        Is("minecraft:oak_log[axis=x]", 1, StructureMirror.None, "minecraft:oak_log[axis=z]");
        Is("minecraft:oak_log[axis=y]", 1, ns, "minecraft:oak_log[axis=y]");
        Is("minecraft:jigsaw[orientation=north_up]", 1, StructureMirror.None, "minecraft:jigsaw[orientation=east_up]");
        Is("minecraft:jigsaw[orientation=up_north]", 3, StructureMirror.None, "minecraft:jigsaw[orientation=up_west]");
        Is("minecraft:redstone_wire[east=side,north=up,power=0,south=none,west=none]", 1, StructureMirror.None, "minecraft:redstone_wire[east=up,north=none,power=0,south=side,west=none]");
        Is("minecraft:piston[extended=false,facing=up]", 1, ns, "minecraft:piston[extended=false,facing=up]");
        Is("minecraft:chiseled_bookshelf[facing=north,slot_0_occupied=true,slot_1_occupied=false,slot_2_occupied=false,slot_3_occupied=false,slot_4_occupied=false,slot_5_occupied=true]", 0, ew,
            "minecraft:chiseled_bookshelf[facing=north,slot_0_occupied=false,slot_1_occupied=false,slot_2_occupied=true,slot_3_occupied=true,slot_4_occupied=false,slot_5_occupied=false]");
        Is("minecraft:nether_portal[axis=x]", 1, StructureMirror.None, "minecraft:nether_portal[axis=z]");
        Is("minecraft:oak_slab[type=top,waterlogged=false]", 0, ns, "minecraft:oak_slab[type=top,waterlogged=false]");
        Program.Check(StructureTransform.Yaw(0, 1, StructureMirror.None) == 90 && StructureTransform.Yaw(90, 0, StructureMirror.NorthSouth) == 90
            && StructureTransform.Yaw(0, 0, StructureMirror.NorthSouth) == 180 && StructureTransform.Yaw(90, 0, StructureMirror.EastWest) == 270,
            "yaw: a quarter turn adds 90°, the north–south mirror swaps south and north, the east–west mirror west and east");
    }

    /// <summary>Every Java block state the program knows (the Java → Bedrock table), turned and mirrored every way.</summary>
    static void AllStates()
    {
        var states = new List<BlockState>();
        using (var s = typeof(JavaBlockData).Assembly.GetManifestResourceStream("MinecraftStudio.Data.mappings.j2b.tsv.gz"))
        using (var gz = new GZipStream(s!, CompressionMode.Decompress))
        using (var r = new StreamReader(gz))
            for (string? line; (line = r.ReadLine()) != null;)
            {
                int tab = line.IndexOf('\t');
                if (tab > 0) states.Add(BlockState.Parse(line[..tab].Replace("[]", "")));
            }
        var known = states.Select(b => b.ToString()).ToHashSet(StringComparer.Ordinal);
        int notReal = 0, notBack = 0, notSame = 0, changed = 0;
        var examples = new List<string>();
        foreach (var b in states)
        {
            foreach (var m in Mirrors)
                for (int turns = 0; turns < 4; turns++)
                {
                    if (turns == 0 && m == StructureMirror.None) continue;
                    var t = StructureTransform.Block(b, turns, m);
                    if (t != b) changed++;
                    if (!known.Contains(t.ToString())) { notReal++; if (examples.Count < 6) examples.Add($"{b} → {t}"); }
                    // Back again: a turn by the rest of the way round; a mirror (with any turn) is its own undoing.
                    var back = m == StructureMirror.None ? StructureTransform.Block(t, 4 - turns, m) : StructureTransform.Block(t, turns, m);
                    if (back != b) { notBack++; if (examples.Count < 6) examples.Add($"{b} → {t} → {back}"); }
                }
            if (StructureTransform.Block(b, 2, StructureMirror.NorthSouth) != StructureTransform.Block(b, 0, StructureMirror.EastWest)) notSame++;
        }
        foreach (var e in examples) Program.Info("  " + e);
        Program.Check(notReal == 0, $"all {states.Count:N0} Java block states turned and mirrored 11 ways are real block states ({changed:N0} changed, {notReal} not real)");
        Program.Check(notBack == 0, $"every turned state turns back, every mirrored one mirrors back ({notBack} did not)");
        Program.Check(notSame == 0, $"for every state, the north–south mirror and half a turn equal the east–west mirror ({notSame} did not)");
    }

    /// <summary>The Twilight Forest's paintings (over a hundred on its walls), cropped in one box with what is around them.</summary>
    static Structure Gallery(string path)
    {
        using var w = WorldLoader.Open(path);
        // The dimension with the most paintings, and the box around all of them.
        var best = w.Dimensions.Select(d => (Dim: d, Spots: Paintings(d))).OrderByDescending(d => d.Spots.Count).First();
        var spots = best.Spots;
        int minX = spots.Min(p => p.X) - 4, maxX = spots.Max(p => p.X) + 4, minY = spots.Min(p => p.Y) - 4, maxY = spots.Max(p => p.Y) + 4;
        int minZ = spots.Min(p => p.Z) - 4, maxZ = spots.Max(p => p.Z) + 4;
        Program.Info($"paintings: {spots.Count} in {best.Dim.DisplayName}, box {minX}..{maxX}, {minY}..{maxY}, {minZ}..{maxZ}");
        return StructureClip.Extract(w, best.Dim, minX, minY, minZ, maxX, maxY, maxZ);
    }

    static List<(int X, int Y, int Z)> Paintings(WorldDimension dim)
    {
        var list = new List<(int, int, int)>();
        foreach (var pos in dim.ChunkPositions)
        {
            NbtCompound? root = null;
            try { root = dim.LoadChunkNbt(pos); } catch { }
            var level = root?.GetCompound("Level") ?? root;
            foreach (var e in level?.GetList("Entities")?.OfType<NbtCompound>() ?? [])
                if ((e.GetString("id") ?? "").EndsWith("ainting", StringComparison.Ordinal)) list.Add((e.GetInt("TileX"), e.GetInt("TileY"), e.GetInt("TileZ")));
        }
        return list;
    }

    static void Building(Structure s)
    {
        var paintings = s.Entities.Where(e => e.Nbt.GetString("id") == "minecraft:painting").ToList();
        Program.Check(paintings.Count > 0, $"the gallery holds paintings ({paintings.Count})");
        foreach (var m in Mirrors)
            for (int turns = 0; turns < 4; turns++)
            {
                if (turns == 0 && m == StructureMirror.None) continue;
                string how = $"turn {turns * 90}°{(m == StructureMirror.None ? "" : $", mirror {m}")}";
                var t = StructureTransform.Apply(s, turns, m);
                bool size = (turns & 1) == 1 ? t.SizeX == s.SizeZ && t.SizeZ == s.SizeX : t.SizeX == s.SizeX && t.SizeZ == s.SizeZ;
                int onWall = 0, sameCells = 0;
                foreach (var (before, after) in s.Entities.Zip(t.Entities))
                {
                    if (before.Nbt.GetString("id") != "minecraft:painting") continue;
                    var off = Hanging.OffsetOf(before.Nbt);
                    var (cx, cy, cz) = Hanging.CentreFromBlock(after.Nbt.GetInt("TileX"), after.Nbt.GetInt("TileY"), after.Nbt.GetInt("TileZ"), Hanging.Facing(after.Nbt), off);
                    var pos = after.Nbt.GetList("Pos")!;
                    if (Math.Abs(cx - pos[0].AsDouble) < 1e-6 && Math.Abs(cy - pos[1].AsDouble) < 1e-6 && Math.Abs(cz - pos[2].AsDouble) < 1e-6) onWall++;
                    // The blocks the painting covers are the turned blocks it covered.
                    var was = Covered(before.Nbt, s);
                    var now = Covered(after.Nbt, t);
                    var expected = was.Select(c => { var (nx, nz) = StructureTransform.Cell(c.X, c.Z, s.SizeX, s.SizeZ, turns, m); return (nx, c.Y, nz); }).ToHashSet();
                    if (now.SetEquals(expected)) sameCells++;
                }
                Program.Check(size && onWall == paintings.Count && sameCells == paintings.Count,
                    $"{how}: every painting hangs on its turned wall ({onWall} of {paintings.Count} at the game's own centre, {sameCells} over the same turned blocks)");
                var back = m == StructureMirror.None ? StructureTransform.Apply(t, 4 - turns, m) : StructureTransform.Apply(t, turns, m);
                Program.Check(Same(s, back, out string why), $"{how}, and back: the same building, block entities and entities ({why})");
            }
    }

    /// <summary>The blocks (counted from the structure's corner) a painting covers, from its block, facing and size.</summary>
    static HashSet<(int X, int Y, int Z)> Covered(NbtCompound painting, Structure s)
    {
        var art = Hanging.Art(painting.GetString("variant"))!;
        int six = Hanging.Facing(painting);
        var (dx, _, dz) = Hanging.Vector(six);
        var off = Hanging.OffsetOf(painting);
        var (px, py, pz) = Hanging.CentreFromBlock(painting.GetInt("TileX"), painting.GetInt("TileY"), painting.GetInt("TileZ"), six, off);
        // The painting's middle in the plane of block centres, then whole blocks to each side and up.
        double qx = px + 0.46875 * dx, qy = py, qz = pz + 0.46875 * dz;
        var pos = Entities.Position(painting)!.Value;
        var e = s.Entities.First(x => ReferenceEquals(x.Nbt, painting));
        double cornerX = pos.X - e.X, cornerY = pos.Y - e.Y, cornerZ = pos.Z - e.Z;
        var cells = new HashSet<(int, int, int)>();
        for (int i = 0; i < art.Width; i++)
            for (int j = 0; j < art.Height; j++)
            {
                double a = i - (art.Width - 1) / 2.0, u = j - (art.Height - 1) / 2.0;
                double x = qx + a * dz, y = qy + u, z = qz - a * dx;
                cells.Add(((int)Math.Floor(x - cornerX), (int)Math.Floor(y - cornerY), (int)Math.Floor(z - cornerZ)));
            }
        return cells;
    }

    static bool Same(Structure a, Structure b, out string why)
    {
        why = "same";
        if (a.SizeX != b.SizeX || a.SizeY != b.SizeY || a.SizeZ != b.SizeZ) { why = "size differs"; return false; }
        for (int i = 0; i < a.AllBlocks.Count; i++) if (a.AllBlocks[i] != b.AllBlocks[i]) { why = $"block {i}: {a.AllBlocks[i]} vs {b.AllBlocks[i]}"; return false; }
        if (a.BlockEntities.Count != b.BlockEntities.Count) { why = "block entity count"; return false; }
        foreach (var (k, be) in a.BlockEntities)
            if (!b.BlockEntities.TryGetValue(k, out var other) || !be.ContentEquals(other)) { why = $"block entity at {k}"; return false; }
        if (a.Entities.Count != b.Entities.Count) { why = "entity count"; return false; }
        for (int i = 0; i < a.Entities.Count; i++)
        {
            var (x, y) = (a.Entities[i], b.Entities[i]);
            if (Math.Abs(x.X - y.X) > 1e-9 || Math.Abs(x.Y - y.Y) > 1e-9 || Math.Abs(x.Z - y.Z) > 1e-9) { why = $"entity {i} position"; return false; }
            var xn = (NbtCompound)x.Nbt.Clone(); var yn = (NbtCompound)y.Nbt.Clone();
            // Positions and motion come back to within rounding; yaw the same angle (written 0–360).
            foreach (var key in new[] { "Pos", "Motion" })
            {
                var (p, q) = (xn.GetList(key), yn.GetList(key));
                if (p?.Count != q?.Count || p != null && q != null && Enumerable.Range(0, p.Count).Any(k => Math.Abs(p[k].AsDouble - q[k].AsDouble) > 1e-9)) { why = $"entity {i} {key}"; return false; }
                xn.Remove(key); yn.Remove(key);
            }
            // Yaw comes back the same angle to within float rounding (a turn adds 90° to a float), pitch exactly.
            if (xn.GetList("Rotation") is { Count: 2 } ra && yn.GetList("Rotation") is { Count: 2 } rb)
            {
                double d = Math.Abs(((ra[0].AsDouble - rb[0].AsDouble) % 360 + 540) % 360 - 180);
                if (d > 1e-3 || !ra[1].ContentEquals(rb[1])) { why = $"entity {i} rotation {ra[0].AsDouble} vs {rb[0].AsDouble}"; return false; }
                xn.Remove("Rotation"); yn.Remove("Rotation");
            }
            if (!xn.ContentEquals(yn)) { why = $"entity {i}: {Snbt.Write(xn, SnbtStyle.Compact)} vs {Snbt.Write(yn, SnbtStyle.Compact)}"; return false; }
        }
        return true;
    }

    /// <summary>The building turned a quarter and mirrored, placed into a copy of a world; its paintings read in the copy.</summary>
    static void Place(Structure gallery, string label, string path)
    {
        var turned = StructureTransform.Apply(gallery, 1, StructureMirror.EastWest);
        string outDir = Path.Combine(TestPaths.Output, "turn", "place-" + string.Concat(label.Where(char.IsLetterOrDigit)));
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        try
        {
            PlacementResult r;
            int ox, oy, oz;
            using (var world = WorldLoader.Open(path))
            {
                var dim = world.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                var c = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).First();
                (ox, oy, oz) = (c.X * 16, 70, c.Z * 16);
                r = StructurePlacement.PlaceIntoCopy(world, turned, "minecraft:overworld", ox, oy, oz, pasteAir: true, outDir);
            }
            Program.Check(r.Verified, $"{label}: the copy with the turned gallery passes its checks{(r.Verified ? "" : " — " + string.Join("; ", r.Problems))}");
            foreach (var n in r.Notes) Program.Info($"   {label}: {n}");
            using var copy = WorldLoader.Open(r.OpenPath);
            var dimCopy = copy.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var reader = new CanonicalReader(copy, dimCopy);
            var expected = turned.Entities.Where(e => e.Nbt.GetString("id") == "minecraft:painting")
                .Select(e => (Variant: e.Nbt.GetString("variant"), Facing: e.Nbt.GetInt("facing"), X: e.Nbt.GetInt("TileX") - (int)Math.Round(Entities.Position(e.Nbt)!.Value.X - e.X) + ox,
                    Y: e.Nbt.GetInt("TileY") - (int)Math.Round(Entities.Position(e.Nbt)!.Value.Y - e.Y) + oy, Z: e.Nbt.GetInt("TileZ") - (int)Math.Round(Entities.Position(e.Nbt)!.Value.Z - e.Z) + oz))
                .ToHashSet();
            var found = new HashSet<(string?, int, int, int, int)>();
            foreach (var cp in expected.Select(e => new ChunkPos(e.X >> 4, e.Z >> 4)).Distinct())
                foreach (var e in reader.Get(cp)?.Entities ?? [])
                    if (e.GetString("id") == "minecraft:painting")
                    {
                        var (x, y, z) = Hanging.BlockOf(e);
                        found.Add((e.GetString("variant"), e.GetInt("facing"), x, y, z));
                    }
            int hit = expected.Count(found.Contains);
            Program.Check(hit == expected.Count, $"{label}: every placed painting is in the copy with its art, facing and block ({hit} of {expected.Count})");
        }
        catch (Exception ex) { Program.Check(false, $"{label}: placing the turned gallery threw {ex.GetType().Name}: {ex.Message}"); }
        finally { try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch (IOException) { } }
    }
}
