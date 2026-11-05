// McTests/TerrainTests.cs: "terrain" — a world's heights out as a picture, and land made from a picture.
// Owns: TerrainTests — the ground rules (a flower, a tree, a snow layer, a lake and frozen water each give the ground under
// them), a sample world's heights saved as a 16-bit PNG and read back to the exact block (see-through where there is no
// ground, the corner block in the notes), and land from a heightmap written by the converter as Java (newest and 1.12),
// Bedrock, an Xbox 360 save, Pocket 0.x and Classic: the converter's own checks pass, every column of the written world
// stands at the height the picture asked for with water to its level, the new world's heights export gives the picture
// back, and a target too low for the picture says what it raised or cut.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class TerrainTests
{
    public static void Run(string[] args)
    {
        string dir = Path.Combine(TestPaths.Output, "terrain", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(dir);
        GroundRules();
        Export(dir);
        Land(dir);
        ShapeColumns();
        Shape(dir);
        ShapeLevels(Path.Combine(dir, "levels"));
    }

    /// <summary>
    /// Classic and Indev levels: a box flattened in a copy (Classic Dojo; Indev hell.mclevel, a box clear of its chests and
    /// player), every column at its height (the shaper's own check), the original untouched; an edit that would change an
    /// Indev chest's block, or put a block where the level's player stands, refused with nothing left behind.
    /// </summary>
    static void ShapeLevels(string dir)
    {
        Console.WriteLine("== Shaping Classic and Indev levels");
        string folder = Path.Combine(TestPaths.Data, "pymclevel", "testfiles");
        string hell = Path.Combine(folder, "hell.mclevel");
        foreach (var name in new[] { "Dojo_64_64_128.dat", "hell.mclevel" })
        {
            string source = Path.Combine(folder, name);
            if (!File.Exists(source)) { Program.Info($"(skipped {name}: not in TestData)"); continue; }
            byte[] before = File.ReadAllBytes(source);
            using var w = WorldLoader.Open(source);
            var level = (MinecraftStudio.Core.Legacy.FiniteWorld)w;
            // Keep clear of the level's chests and its player (both are refused, checked below).
            var keepOff = (level.Indev?.Root.GetList("TileEntities")?.OfType<MinecraftStudio.Core.Nbt.NbtCompound>() ?? []).Select(t => (t.GetInt("Pos") & 1023, (t.GetInt("Pos") >> 20) & 1023)).ToList();
            if (level.Players.FirstOrDefault()?.Data.GetList("Pos") is { Count: 3 } pp) keepOff.Add(((int)pp[0].AsDouble, (int)pp[2].AsDouble));
            int width = level.Classic?.Width ?? level.Indev!.Width, length = level.Classic?.Length ?? level.Indev!.Length;
            (int, int, int, int)? box = null;
            List<int> ground = [];
            for (int z0 = 8; z0 + 32 < length && box == null; z0 += 24)
                for (int x0 = 8; x0 + 32 < width && box == null; x0 += 24)
                {
                    if (keepOff.Any(k => k.Item1 >= x0 - 8 && k.Item1 <= x0 + 40 && k.Item2 >= z0 - 8 && k.Item2 <= z0 + 40)) continue;
                    var plan = LandShape.Plan(w, "minecraft:overworld", x0, z0, x0 + 32, z0 + 32, new LandShapeOptions(LandShapeKind.Smooth, 4));
                    ground = plan.Now.Where(h => h != MapRenderer.NoHeight).OrderBy(h => h).ToList();
                    if (ground.Count > 100 && ground[ground.Count / 2] > plan.Bottom + 2) box = (x0, z0, x0 + 32, z0 + 32);
                }
            if (box is not var (bx0, bz0, bx1, bz1)) { Program.Check(false, $"{name}: a box with ground, clear of chests and the player"); continue; }
            // Flat ground (hell.mclevel's is) is raised instead: something must change.
            var shape = ground[0] == ground[^1] ? new LandShapeOptions(LandShapeKind.Raise, 2) : new LandShapeOptions(LandShapeKind.Flatten, ground[ground.Count / 2]);
            try
            {
                var r = LandShape.ShapeIntoCopy(w, "minecraft:overworld", bx0, bz0, bx1, bz1, shape, Path.Combine(dir, Path.GetFileNameWithoutExtension(name)));
                Program.Check(r.Verified && r.BlocksPlaced > 0, $"{name}: {bx0}, {bz0} to {bx1}, {bz1} {(shape.Kind == LandShapeKind.Raise ? "raised by 2" : $"flattened to y {shape.Amount}")} in a copy, every column at its height, {r.BlocksPlaced:N0} blocks changed ({string.Join("; ", r.Problems.Concat(r.Notes).Take(3))})");
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or NotSupportedException or ArgumentException) { Program.Check(false, $"{name}: {e.Message}"); }
            Program.Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(before), $"{name}: the level itself is not changed");
        }
        if (!File.Exists(hell)) return;
        using var hw = WorldLoader.Open(hell);
        var hl = (MinecraftStudio.Core.Legacy.FiniteWorld)hw;
        int packed = hl.Indev!.Root.GetList("TileEntities")!.OfType<MinecraftStudio.Core.Nbt.NbtCompound>().First().GetInt("Pos");
        var chest = (X: packed & 1023, Y: (packed >> 10) & 1023, Z: (packed >> 20) & 1023);
        var feet = hl.Players[0].Data.GetList("Pos")!;
        var player = (X: (int)Math.Floor(feet[0].AsDouble), Y: (int)Math.Floor(feet[1].AsDouble), Z: (int)Math.Floor(feet[2].AsDouble));
        foreach (var (what, at) in new[] { ("chest", chest), ("player", player) })
        {
            string output = Path.Combine(dir, "hell-" + what);
            bool refused = false;
            try
            {
                StructurePlacement.EditIntoCopy(hw, "minecraft:overworld", [new ChunkPos(at.X >> 4, at.Z >> 4)],
                    c => c.SetBlock(at.X & 15, at.Y, at.Z & 15, BlockState.Get("minecraft:stone")), output);
            }
            catch (NotSupportedException e) { refused = e.Message.Contains(what); }
            Program.Check(refused && !Directory.Exists(output), $"hell.mclevel: stone put on the {what}'s block ({at.X}, {at.Y}, {at.Z}) is refused, nothing left behind");
        }
    }

    static void GroundRules()
    {
        var col = new ChunkColumn(0, 0, 0, 15);
        var cells = new Dictionary<int, (BlockState[] Palette, ushort[] Indices)>();
        void Set(int x, int y, int z, string name)
        {
            var b = BlockState.Parse(name);
            if (!col.Sections.TryGetValue(y >> 4, out var s))
                col.Sections[y >> 4] = s = new BlockSection(y >> 4, [BlockState.Air], new ushort[4096]);
            int i = Array.IndexOf(s.Palette, b);
            if (i < 0) { s.Palette = [.. s.Palette, b]; i = s.Palette.Length - 1; }
            s.Indices![BlockSection.Index(x, y & 15, z)] = (ushort)i;
        }
        for (int x = 0; x < 6; x++)
            for (int y = 0; y <= 50; y++) Set(x, y, 0, "minecraft:stone");
        for (int y = 51; y <= 64; y++) Set(0, y, 0, "minecraft:dirt");
        Set(0, 65, 0, "minecraft:poppy");                                   // a flower: ground at 64
        for (int y = 51; y <= 64; y++) Set(1, y, 0, "minecraft:grass_block[snowy=false]");
        for (int y = 65; y <= 69; y++) Set(1, y, 0, "minecraft:oak_log[axis=y]");
        for (int y = 70; y <= 72; y++) Set(1, y, 0, "minecraft:oak_leaves[distance=1,persistent=false,waterlogged=false]");   // a tree: 64
        for (int y = 51; y <= 62; y++) Set(2, y, 0, "minecraft:water[level=0]");                                     // a lake: 50
        for (int y = 51; y <= 64; y++) Set(3, y, 0, "minecraft:dirt");
        Set(3, 65, 0, "minecraft:snow[layers=1]");                                                                  // snow layer: 64
        for (int y = 51; y <= 61; y++) Set(4, y, 0, "minecraft:water[level=0]");
        Set(4, 62, 0, "minecraft:ice");                                                                              // frozen: 50
        var h = new int[256];
        MapRenderer.GroundHeights(col, h, bedrockNames: false);
        Program.Check(h[0] == 64 && h[1] == 64 && h[2] == 50 && h[3] == 64 && h[4] == 50 && h[5] == 50 && h[6] == MapRenderer.NoHeight,
            $"ground: under a flower, a tree, a lake, a snow layer and ice it is the land itself ({string.Join(", ", h.Take(7).Select(v => v == MapRenderer.NoHeight ? "none" : v.ToString()))})");
    }

    static void Export(string dir)
    {
        string source = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(source)) { Program.Info("(skipped the heights export: no sample world)"); return; }
        using var w = WorldLoader.Open(source);
        var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
        var r = DimensionRenderer.Render(dim, new MapRenderOptions(), ground: true);
        var g = r.Ground!;
        var have = g.Where(v => v != MapRenderer.NoHeight).ToList();
        int low = have.Min(), high = have.Max();
        byte[] png = Heightmap.Encode(g, r.Width, r.Height, low, high, r.MinChunkX * 16, r.MinChunkZ * 16);
        File.WriteAllBytes(Path.Combine(dir, "sample heights.png"), png);
        Program.Check(png[24] == 16 && png[25] == 4, $"heights: a 16-bit grey PNG with alpha ({png.Length / 1024.0:0} KB, {r.Width} × {r.Height}, y {low} to {high})");
        var pic = Heightmap.Read(png);
        Program.Check(pic.HasRange && pic.LowY == low && pic.HighY == high && pic.WestX == r.MinChunkX * 16 && pic.NorthZ == r.MinChunkZ * 16,
            $"heights: the file says its range and corner (y {pic.LowY} to {pic.HighY}, x {pic.WestX}, z {pic.NorthZ})");
        int wrong = 0, holes = 0;
        for (int i = 0; i < g.Length; i++)
        {
            float level = pic.Levels[i];
            if (g[i] == MapRenderer.NoHeight) { if (!float.IsNaN(level)) wrong++; else holes++; continue; }
            if (float.IsNaN(level) || low + (int)Math.Round(level * (high - low)) != g[i]) wrong++;
        }
        Program.Check(wrong == 0, $"heights: every column reads back to its block ({g.Length - holes:N0} columns, {holes:N0} without ground see-through, {wrong} wrong)");
        // Trees are left out: a forest's ground is lower than the map's top blocks.
        var tops = new int[256];
        int lower = 0;
        foreach (var pos in dim.ChunkPositions.Take(400))
        {
            var col = dim.LoadChunk(pos);
            if (col == null) continue;
            MapRenderer.GroundHeights(col, tops, false);
            for (int i = 0; i < 256; i++)
                if (col.Top(i & 15, i >> 4) is var (y, _) && tops[i] != MapRenderer.NoHeight && tops[i] < y) lower++;
        }
        Program.Check(lower > 0, $"heights: water, plants and trees left out ({lower:N0} columns lower than their top block)");
    }

    // A hill with a snowy top, a sea, a cliff and a see-through corner.
    static HeightPicture Picture(int w, int h)
    {
        var levels = new float[w * h];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                double dx = (x - w * 0.62) / (w * 0.2), dz = (z - h * 0.4) / (h * 0.2);
                double hill = Math.Exp(-(dx * dx + dz * dz));
                double sea = x < w * 0.3 ? 0.0 : Math.Min(1, (x - w * 0.3) / (w * 0.15));
                double cliff = z > h * 0.75 && x > w * 0.5 ? 0.18 : 0;
                levels[z * w + x] = x < 32 && z < 32 ? float.NaN : (float)Math.Clamp(0.12 + 0.18 * sea + 0.65 * hill + cliff, 0, 1);
            }
        return new HeightPicture(levels, w, h);
    }

    static void Land(string dir)
    {
        var picture = Picture(176, 144);
        var land = new HeightmapLand { Picture = picture, LowY = 40, HighY = 150, WaterY = 62, SnowY = 125, Name = "Heightmap test", WestX = -96, NorthZ = -64 };
        var targets = new[]
        {
            TargetCatalog.All.First(t => t.Kind == TargetKind.Java),
            TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.VersionName.StartsWith("1.12")),
            TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock),
            TargetCatalog.All.First(t => t.Kind == TargetKind.LegacyConsole),
            TargetCatalog.All.First(t => t.Kind == TargetKind.PocketLegacy),
            TargetCatalog.All.First(t => t.Kind == TargetKind.Classic),
        };
        string? newestJava = null;
        foreach (var target in targets)
        {
            var world = new HeightmapWorld(land, Path.Combine(dir, "picture.png"));
            string output = Path.Combine(dir, string.Concat(target.Name.Where(char.IsLetterOrDigit)).ToLowerInvariant());
            ConversionReport report;
            try { report = WorldConverter.Run(world, target, output, new ConversionOptions()); }
            catch (Exception ex) { Program.Check(false, $"land as {target.Name}: {ex.GetType().Name}: {ex.Message}"); continue; }
            var failed = report.Checks.Where(c => !c.Passed).ToList();
            Program.Check(report.Checks.Count > 0 && failed.Count == 0,
                $"land as {target.Name}: the converter's checks pass ({report.Checks.Count}, {report.Chunks:N0} chunks)" + string.Concat(failed.Select(f => $"\n    {f.Name}: {f.Detail}")));
            foreach (var n in report.Notes.Where(n => n.Contains("keeps y"))) Program.Info($"    note: {n}");
            var lost = report.Losses.Where(l => l.Lost).ToList();
            if (target.Kind is TargetKind.Java or TargetKind.Bedrock)
                Program.Check(!report.AnythingLost, $"land as {target.Name}: nothing lost" + string.Concat(lost.Take(5).Select(l => $"\n    {l}")));
            else foreach (var l in lost.Take(3)) Program.Info($"    lost: {l}");
            // Every column at the height the picture asked for, water to its level.
            var (bottom, top) = HeightmapWorld.HeightsFor(target);
            int water = Math.Clamp(62, bottom + 1, top);
            // A Classic level counts its own heights from the bottom of the window it keeps.
            int readY = target.Kind == TargetKind.Classic ? bottom : 0;
            using var written = WorldLoader.Open(report.OpenPath ?? output);
            var dim = written.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var reader = new CanonicalReader(written, dim);
            long columns = 0, wrong = 0, wet = 0;
            string? first = null;
            var (shiftX, shiftZ) = Shift(target, written, world);
            foreach (var pos in dim.ChunkPositions)
            {
                var c = reader.Get(pos);
                if (c == null) continue;
                var heights = c.Heights();
                for (int i = 0; i < 256; i++)
                {
                    int x = pos.X * 16 + (i & 15), z = pos.Z * 16 + (i >> 4);
                    // A finite target is moved around its spawn: compare with the source block it came from.
                    int sx = x + shiftX, sz = z + shiftZ;
                    if (!InLand(world, sx, sz) || world.GroundAt(sx, sz) is not int g) continue;
                    columns++;
                    int expect = Math.Max(g, water) - readY;
                    if (g < water) wet++;
                    if (heights[i] != expect) { wrong++; first ??= $"x {x} z {z}: {heights[i]} for {expect}"; }
                }
            }
            Program.Check(columns > 0 && wrong == 0, $"land as {target.Name}: every column at its height ({columns:N0} columns, {wet:N0} under water, {wrong} wrong{(first != null ? ", first " + first : "")})");
            if (target == targets[0]) newestJava = output;
        }
        if (newestJava != null) HeightsBack(newestJava, land);
        // A target lower than the picture says what had to give.
        var deep = new HeightmapWorld(land with { LowY = -30, HighY = 300 }, "deep.png");
        string? note = deep.FitTo(targets[1]);
        Program.Check(note != null && note.Contains("raised") && note.Contains("cut") && deep.GroundAt(0, 0) is int y0 && y0 >= 1 && y0 <= 255,
            $"land: a target too low says so ({note})");
        var big = new HeightmapWorld(land with { BlocksPerPixel = 2, WestX = null, NorthZ = null }, "big.png");
        Program.Check(big.Width == 352 && big.Length == 288 && big.WestX == -176 && big.NorthZ == -144, $"land: two blocks per pixel, centred ({big.Width} × {big.Length} from x {big.WestX}, z {big.NorthZ})");
        Program.Check(Refused(() => new HeightmapWorld(land with { BlocksPerPixel = 200 }, "x.png")) && Refused(() => new HeightmapWorld(land with { LowY = 90, HighY = 90 }, "x.png")),
            "land: settings that can't be met are refused");
    }

    // Finite targets are written around the spawn: console chunks less the spawn's chunk, Pocket's 16 × 16 chunks and a
    // Classic level from their north-west corner (the writers' own SourceArea rules).
    static (int X, int Z) Shift(ConversionTarget target, SaveWorld written, HeightmapWorld world)
    {
        var (sx, _, sz) = world.Spawn();
        int cx = sx >> 4, cz = sz >> 4;
        return target.Kind switch
        {
            TargetKind.LegacyConsole => (cx * 16, cz * 16),
            TargetKind.PocketLegacy => ((cx - 8) * 16, (cz - 8) * 16),
            TargetKind.Classic when written.Dimensions[0] is MinecraftStudio.Core.Legacy.FiniteDimension f => ((cx - f.Width / 32) * 16, (cz - f.Length / 32) * 16),
            _ => (0, 0),
        };
    }

    static bool InLand(HeightmapWorld w, int x, int z) => w.Dimensions[0].ChunkPositions.Contains(new ChunkPos(x >> 4, z >> 4));

    // The new world's heights export gives the picture back, block for block.
    static void HeightsBack(string folder, HeightmapLand land)
    {
        using var w = WorldLoader.Open(folder);
        var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
        var r = DimensionRenderer.Render(dim, new MapRenderOptions(), ground: true);
        var source = new HeightmapWorld(land, "again.png");
        source.FitTo(TargetCatalog.All.First(t => t.Kind == TargetKind.Java));
        long same = 0, wrong = 0;
        for (int z = 0; z < r.Height; z++)
            for (int x = 0; x < r.Width; x++)
            {
                int bx = r.MinChunkX * 16 + x, bz = r.MinChunkZ * 16 + z;
                if (!InLand(source, bx, bz) || source.GroundAt(bx, bz) is not int g) continue;
                if (r.Ground![z * r.Width + x] == g) same++; else wrong++;
            }
        Program.Check(same > 0 && wrong == 0, $"land: the new world's heights give the picture back ({same:N0} columns, {wrong} wrong)");
    }

    // The column moves on a built chunk: raised land keeps what stands on it, a lowered sea floor keeps the water's level,
    // a raised one comes out of the water.
    static void ShapeColumns()
    {
        var c = new CanonicalChunk(0, 0);
        static BlockState B(string n) => BlockState.Parse(n);
        for (int x = 0; x < 3; x++)
        {
            c.SetBlock(x, 0, 0, B("minecraft:bedrock"));
            for (int y = 1; y < 40; y++) c.SetBlock(x, y, 0, B("minecraft:stone"));
        }
        for (int y = 40; y < 64; y++) c.SetBlock(0, y, 0, B("minecraft:dirt"));
        c.SetBlock(0, 64, 0, B("minecraft:grass_block[snowy=false]"));
        c.SetBlock(0, 65, 0, B("minecraft:chest[facing=north,type=single,waterlogged=false]"));
        c.BlockEntities.Add(new NbtCompound { { "id", new NbtString("minecraft:chest") }, { "x", new NbtInt(0) }, { "y", new NbtInt(65) }, { "z", new NbtInt(0) } });
        c.Entities.Add(new NbtCompound { { "id", new NbtString("minecraft:pig") }, { "Pos", new NbtList(NbtType.Double) { new NbtDouble(0.5), new NbtDouble(65), new NbtDouble(0.5) } } });
        for (int x = 1; x < 3; x++)
        {
            for (int y = 40; y <= 50; y++) c.SetBlock(x, y, 0, B("minecraft:sand"));
            for (int y = 51; y <= 62; y++) c.SetBlock(x, y, 0, B("minecraft:water[level=0]"));
        }
        // Column 0 up 5, column 1 down 5, column 2 up 20 (out of the sea).
        var plan = new LandPlan(0, 0, 2, 0, [64, 50, 50], [69, 45, 70], -64, 319);
        LandShape.Shaper(plan)(c);
        string Name(int x, int y) => c.GetBlock(x, y, 0).BaseName;
        var heights = new int[256];
        MapRenderer.GroundHeights(c.Sections.Values, heights, false);
        bool raised = Name(0, 69) == "grass_block" && Name(0, 70) == "chest" && Name(0, 65) == "dirt" && Name(0, 71) == "air"
            && c.BlockEntities[0].GetInt("y") == 70 && Entities.Position(c.Entities[0]) is var (_, py, _) && py == 70;
        Program.Check(raised, $"shape: a raised column keeps its grass, the chest (block entity at y {c.BlockEntities[0].GetInt("y")}) and the pig on top");
        bool lowered = heights[1] == 45 && Name(1, 45) == "sand" && Enumerable.Range(46, 17).All(y => Name(1, y) == "water") && Name(1, 63) == "air";
        Program.Check(lowered, $"shape: a lowered sea floor (ground {heights[1]}) keeps the water up to y 62");
        bool island = heights[2] == 70 && Name(2, 70) == "sand" && Enumerable.Range(71, 30).All(y => Name(2, y) == "air") && Name(2, 0) == "bedrock";
        Program.Check(island, $"shape: a sea floor raised to y {heights[2]} comes out of the water");
    }

    // Flatten, raise with soft edges and smooth, each in a copy of a sample world, read back column by column.
    static void Shape(string dir)
    {
        foreach (var (label, source) in new[]
        {
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        })
        {
            if (!Directory.Exists(source)) { Program.Info($"(skipped shaping {label}: not in TestData)"); continue; }
            using var w = WorldLoader.Open(source);
            var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            // A box of land around the middle of what is there.
            var mid = dim.ChunkPositions.OrderBy(p => p.X).ThenBy(p => p.Z).ElementAt(dim.ChunkPositions.Count / 2);
            int minX = mid.X * 16 - 16 + 3, minZ = mid.Z * 16 - 16 + 5, maxX = minX + 40, maxZ = minZ + 36;
            var before = LandShape.Plan(w, "minecraft:overworld", minX, minZ, maxX, maxZ, new LandShapeOptions(LandShapeKind.Raise, 1));
            var have = before.Now.Where(h => h != MapRenderer.NoHeight).OrderBy(h => h).ToList();
            if (have.Count < 100) { Program.Info($"(skipped shaping {label}: no land in the box)"); continue; }
            int level = have[have.Count / 2] + 2;
            foreach (var (what, o) in new (string, LandShapeOptions)[]
            {
                ($"flattened to y {level}", new LandShapeOptions(LandShapeKind.Flatten, level, 0)),
                ("raised 6 with soft edges", new LandShapeOptions(LandShapeKind.Raise, 6, 3)),
                ("smoothed over 4 blocks", new LandShapeOptions(LandShapeKind.Smooth, 4, 2)),
            })
            {
                string output = Path.Combine(dir, "shape", string.Concat((label + what).Where(char.IsLetterOrDigit)));
                var plan = LandShape.Plan(w, "minecraft:overworld", minX, minZ, maxX, maxZ, o);
                PlacementResult r;
                try { r = LandShape.ShapeIntoCopy(w, "minecraft:overworld", minX, minZ, maxX, maxZ, o, output); }
                catch (Exception ex) { Program.Check(false, $"shape {label}, {what}: {ex.GetType().Name}: {ex.Message}"); continue; }
                Program.Check(r.Verified, $"shape {label}, {what}: the copy checks out ({r.ChunksChanged} chunks; {string.Join(" ", r.Notes.Take(1))})" + string.Concat(r.Problems.Select(p => "\n    " + p)));
                if (o.Kind == LandShapeKind.Flatten)
                    Program.Check(plan.Then.All(t => t == MapRenderer.NoHeight || t == level), $"shape {label}, {what}: every column at y {level}");
                if (o.Kind == LandShapeKind.Raise)
                {
                    int i = 18 * plan.Width + 20, e = 20;
                    Program.Check(plan.Then[i] - plan.Now[i] == 6 && plan.Then[e] - plan.Now[e] == 2, $"shape {label}, {what}: the middle up {plan.Then[i] - plan.Now[i]}, the edge up {plan.Then[e] - plan.Now[e]}");
                }
                if (o.Kind == LandShapeKind.Smooth)
                    Program.Check(Rough(plan.Then, plan.Width) < Rough(plan.Now, plan.Width), $"shape {label}, {what}: less rough (steps {Rough(plan.Now, plan.Width):N0} → {Rough(plan.Then, plan.Width):N0})");
            }
        }
    }

    static long Rough(int[] h, int w)
    {
        long sum = 0;
        for (int i = 0; i < h.Length; i++)
        {
            if (h[i] == MapRenderer.NoHeight) continue;
            if ((i + 1) % w != 0 && h[i + 1] != MapRenderer.NoHeight) sum += Math.Abs(h[i] - h[i + 1]);
            if (i + w < h.Length && h[i + w] != MapRenderer.NoHeight) sum += Math.Abs(h[i] - h[i + w]);
        }
        return sum;
    }

    static bool Refused(Action a)
    {
        try { a(); return false; }
        catch (ArgumentException) { return true; }
    }
}
