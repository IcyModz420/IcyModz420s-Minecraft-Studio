// McTests/HangingTests.cs: "hanging" — paintings, item frames and leash knots between editions and versions.
// Owns: HangingTests — the real paintings and frames in TestData (Java 1.12 Twilight Forest, Java 1.16, Xbox 360) read to
// canonical: art, facing and block right, and the game's centre worked out from the block equals the saved centre; written
// for every Java and Bedrock target and read back the same, in each version's own field names; art a version lacks is
// refused; an unknown console art name survives. `hanging convert`: real worlds with paintings converted to Java (26.1,
// 1.16.5, 1.12.2, 1.8) and Bedrock (newest, 1.16): every painting arrives with its art, facing and place, in the target's
// own field names. `hanging scan` lists the raw hanging entities in every world (read only).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class HangingTests
{
    static readonly string[] Ids = ["painting", "item_frame", "glow_item_frame", "leash_knot", "Painting", "ItemFrame", "LeashKnot"];

    public static void Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "scan") { Scan(); return; }
        if (args.Length > 0 && args[0] == "convert") { Convert(); return; }
        Geometry();
        var samples = new List<(string Label, NbtCompound Raw, int Dv)>();
        foreach (var (label, path) in new[]
        {
            ("Java 1.12.2 Twilight Forest", Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_12_2_twilight_forest")),
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
        })
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            var found = Raw(path);
            Program.Info($"{label}: {found.Count} hanging entities");
            samples.AddRange(found.Select(f => (label, f.Raw, f.Dv)));
        }
        if (samples.Count > 0) RealSaves(samples);
        Versions(samples.Select(s => Entities.FromJava(s.Raw, s.Dv)).ToList());
        Bedrock();
    }

    /// <summary>The block from the centre and back, for every art, facing and some awkward places (negative, chunk edges).</summary>
    static void Geometry()
    {
        int bad = 0, n = 0;
        foreach (var art in Hanging.Arts)
            for (int six = 2; six <= 5; six++)
                foreach (var (x, y, z) in new[] { (0, 64, 0), (-1, -60, -1), (15, 300, 16), (-29_999_985, 70, 29_999_983), (123, 5, -456) })
                {
                    var off = new Hanging.Offset(art.Width % 2 == 0 ? 0.5 : 0, art.Height % 2 == 0 ? 0.5 : 0);
                    var (cx, cy, cz) = Hanging.CentreFromBlock(x, y, z, six, off);
                    n++;
                    if (Hanging.BlockFromCentre(cx, cy, cz, six, off) != (x, y, z)) bad++;
                }
        Program.Check(bad == 0, $"block → centre → block is the same for every painting size, facing and place ({n:N0} cases)");
    }

    /// <summary>What the real saves hold reads to canonical fields that put each one exactly where the game had it.</summary>
    static void RealSaves(List<(string Label, NbtCompound Raw, int Dv)> samples)
    {
        int paintings = 0, frames = 0, artOk = 0, facingOk = 0, blockOk = 0, centreOk = 0, unknown = 0;
        var arts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (label, raw, dv) in samples)
        {
            var c = Entities.FromJava(raw, dv);
            string id = c.GetString("id") ?? "";
            bool painting = id == "minecraft:painting";
            if (painting) paintings++; else if (Hanging.IsFrame(id)) frames++; else continue;
            int rawFour = raw.GetInt("Facing", raw.GetInt("facing"));
            int six = Hanging.Facing(c);
            // Before 1.13 every hanging entity faced four ways; after, frames face six.
            bool fourWays = painting || dv < 1451;
            if (fourWays ? Hanging.FourWay(six) == rawFour : six == rawFour) facingOk++;
            if (Hanging.BlockOf(c) == (raw.GetInt("TileX"), raw.GetInt("TileY"), raw.GetInt("TileZ"))) blockOk++;
            var (cx, cy, cz) = Hanging.CentreFromBlock(raw.GetInt("TileX"), raw.GetInt("TileY"), raw.GetInt("TileZ"), six, Hanging.OffsetOf(c));
            var pos = raw.GetList("Pos")!;
            if (Math.Abs(cx - pos[0].AsDouble) < 1e-6 && Math.Abs(cy - pos[1].AsDouble) < 1e-6 && Math.Abs(cz - pos[2].AsDouble) < 1e-6) centreOk++;
            else Program.Info($"  {label}: {raw.GetString("Motive") ?? id} centre {cx}, {cy}, {cz} but saved at {pos[0].AsDouble}, {pos[1].AsDouble}, {pos[2].AsDouble}");
            if (painting)
            {
                string variant = c.GetString("variant") ?? "";
                arts.Add(variant);
                var art = Hanging.Art(variant);
                if (art == null) unknown++;
                if (art != null ? art.LegacyName == raw.GetString("Motive") : c.GetString("Motive") == raw.GetString("Motive")) artOk++;
            }
            else artOk++;
        }
        int all = paintings + frames;
        Program.Check(all > 0 && artOk == all, $"real saves: every painting's art is read right ({paintings} paintings: {string.Join(", ", arts.Take(12).Select(a => a.Replace("minecraft:", "")))}…; {unknown} with art only consoles have, kept as written)");
        Program.Check(facingOk == all, $"real saves: every painting and frame faces the same way ({facingOk} of {all})");
        Program.Check(blockOk == all, $"real saves: every one keeps the block it hangs in ({blockOk} of {all})");
        Program.Check(centreOk == all, $"real saves: the centre worked out from the block is exactly where the game saved it, for every size ({centreOk} of {all})");
    }

    /// <summary>Canonical → every Java target and back: the version's own field names, and the same art, facing and block.</summary>
    static void Versions(List<NbtCompound> canonicals)
    {
        // A few made-up ones besides the real ones: new art, a floor frame, a leash knot.
        canonicals.Add(Painting("minecraft:orb", 2, 10, 70, -3));
        canonicals.Add(Painting("minecraft:dennis", 1, -5, 64, 9));
        canonicals.Add(Painting("minecraft:earth", 3, 0, 80, 0));
        canonicals.Add(Frame(1, 4, 63, 4));
        canonicals.Add(new NbtCompound { { "id", new NbtString("minecraft:leash_knot") }, { "Pos", Pos(7.5, 64.5, 7.5) }, { "TileX", new NbtInt(7) }, { "TileY", new NbtInt(64) }, { "TileZ", new NbtInt(7) } });
        var targets = TargetCatalog.All.Where(t => t.Kind == TargetKind.Java).ToList();
        int written = 0, same = 0, names = 0, refused = 0, refusedRight = 0;
        var wrong = new List<string>();
        foreach (var t in targets)
            foreach (var c in canonicals)
            {
                var copy = (NbtCompound)c.Clone();
                string? why = Hanging.ToJava(copy, c, t.DataVersion, t.LegacyLevel);
                string id = c.GetString("id")!;
                bool painting = id == "minecraft:painting";
                bool floor = Hanging.IsFrame(id) && Hanging.FourWay(Hanging.Facing(c)) == null;
                bool expectRefused = painting ? Hanging.MissingInJava(c, t.DataVersion, t.LegacyLevel) != null : floor && t.DataVersion < 1451;
                if (why != null)
                {
                    refused++;
                    if (expectRefused) refusedRight++; else wrong.Add($"{t.Name}: {id} refused ({why})");
                    continue;
                }
                if (expectRefused) { wrong.Add($"{t.Name}: {c.GetString("variant")} written but the version lacks it"); continue; }
                written++;
                if (NamesRight(copy, id, t)) names++; else wrong.Add($"{t.Name}: {id} field names {Snbt.Write(copy, SnbtStyle.Compact)}");
                int dv = t.DataVersion == 0 ? 0 : t.DataVersion;
                var back = Entities.FromJava(copy, dv);
                if (Hanging.Facing(back) == Hanging.Facing(c) && Hanging.BlockOf(back) == Hanging.BlockOf(c)
                    && (!painting || back.GetString("variant") == c.GetString("variant")))
                    same++;
                else wrong.Add($"{t.Name}: {id} read back as {Snbt.Write(back, SnbtStyle.Compact)}");
            }
        foreach (var w in wrong.Take(8)) Program.Info("  " + w);
        Program.Check(wrong.Count == 0 && same == written, $"every Java target ({targets.Count}): written and read back with the same art, facing and block ({same} of {written})");
        Program.Check(names == written, $"every Java target: the version's own names (variant/facing 1.19+, Motive/Facing before, CamelCase art and Direction/Dir in the old ones, block_pos 1.21.5+)");
        Program.Check(refused == refusedRight && refused > 0, $"art and floor frames a version does not have are refused, to go to the rescue chests ({refused})");

        // Exact versions where art came in.
        static bool Has(string variant, int dv, double level = LegacyLevel.Release_1_12) => Hanging.MissingInJava(Painting(variant, 0, 0, 0, 0), dv, level) == null;
        Program.Check(!Has("minecraft:orb", 3837) && Has("minecraft:orb", 3953), "1.21's paintings: not in 1.20.5, in 1.21");
        Program.Check(!Has("minecraft:earth", 3000) && Has("minecraft:earth", 3105), "Earth, Wind, Fire and Water: from 1.19");
        Program.Check(!Has("minecraft:dennis", 4435) && Has("minecraft:dennis", 4554), "Dennis: not in 1.21.6, in 1.21.9");
        Program.Check(!Has("minecraft:wither", 0, 1.03) && Has("minecraft:wither", 0, 1.04), "Wither: from 1.4.2");
        Program.Check(!Has("minecraft:burning_skull", 0, LegacyLevel.Beta_1_2) && Has("minecraft:burning_skull", 0, LegacyLevel.Beta_1_3), "Burning Skull: from Beta 1.3 (added in Beta 1.2_01)");
        Program.Check(!Has("minecraft:graham", 0, LegacyLevel.Alpha) && Has("minecraft:graham", 0, LegacyLevel.Alpha_1_2) && Has("minecraft:kebab", 0, LegacyLevel.Alpha), "Graham (Alpha 1.1.1) not in Alpha 1.0; Kebab in every version");

        // An art name only consoles have survives a console → console trip, and reads as itself.
        var odd = new NbtCompound { { "id", new NbtString("Painting") }, { "Motive", new NbtString("1x1E3") }, { "Facing", new NbtByte(2) }, { "TileX", new NbtInt(3) }, { "TileY", new NbtInt(70) }, { "TileZ", new NbtInt(5) }, { "Pos", Pos(3.5, 70.5, 5.96875) } };
        var oddCanonical = Entities.FromJava(odd, 1343);
        var oddBack = (NbtCompound)oddCanonical.Clone();
        Hanging.ToJava(oddBack, oddCanonical, 0, LegacyLevel.Release_1_12);
        var oddModern = (NbtCompound)oddCanonical.Clone();
        Hanging.ToJava(oddModern, oddCanonical, 4786, LegacyLevel.Release_1_12);
        Program.Check(oddBack.GetString("Motive") == "1x1E3" && oddModern.GetString("variant") == "minecraft:1x1e3" && !oddModern.ContainsKey("Motive"),
            $"an art name only consoles have is kept as written for consoles (\"{oddBack.GetString("Motive")}\") and named for Java (\"{oddModern.GetString("variant")}\")");
    }

    /// <summary>The version's own field names were used, and no other version's.</summary>
    static bool NamesRight(NbtCompound j, string id, ConversionTarget t)
    {
        int dv = t.DataVersion;
        bool blockPos = dv >= Hanging.BlockPosArray;
        if (blockPos != j.ContainsKey("block_pos") || blockPos == j.ContainsKey("TileX")) return false;
        if (id == "minecraft:leash_knot") return true;
        bool old = dv == 0 && t.LegacyLevel < LegacyLevel.Release_1_8;
        if (id == "minecraft:painting")
        {
            if (dv >= Hanging.VariantNames) return j.ContainsKey("variant") && j.ContainsKey("facing") && !j.ContainsKey("Motive") && !j.ContainsKey("Facing");
            string motive = j.GetString("Motive") ?? "";
            bool namespaced = motive.StartsWith("minecraft:", StringComparison.Ordinal);
            if (namespaced != (dv >= 1451) || j.ContainsKey("variant") || j.ContainsKey("facing")) return false;
            if (dv < 1451 && Hanging.Art(Hanging.CanonicalVariant(motive)) != null && !char.IsUpper(motive[0])) return false;
            return old ? j.ContainsKey("Direction") && j.ContainsKey("Dir") && !j.ContainsKey("Facing") : j.ContainsKey("Facing") && !j.ContainsKey("Direction");
        }
        if (dv >= 1451) return j.ContainsKey("Facing") && !j.ContainsKey("Direction");
        return old ? j.ContainsKey("Direction") && j.ContainsKey("Dir") : j.ContainsKey("Facing");
    }

    /// <summary>Bedrock: names (old CamelCase, 1.21 lower case), Motive → Motif, versions, and the block worked out from the centre.</summary>
    static void Bedrock()
    {
        var targets = TargetCatalog.All.Where(t => t.Kind == TargetKind.Bedrock).ToList();
        int trips = 0, same = 0, keys = 0;
        var wrong = new List<string>();
        foreach (var t in targets)
            foreach (var art in Hanging.Arts)
                for (int four = 0; four < 4; four++)
                {
                    var c = Painting("minecraft:" + art.Id, four, -17, 70, 33);
                    if (Hanging.MissingInBedrock(c, t.BedrockVersion) != null) continue;
                    var b = new NbtCompound { { "identifier", new NbtString("minecraft:painting") }, { "Pos", c["Pos"]!.Clone() } };
                    Hanging.ToBedrock(b, c, t.BedrockVersion);
                    trips++;
                    bool motif = Hanging.AtLeast(t.BedrockVersion, [1, 20, 0]), motive = !Hanging.AtLeast(t.BedrockVersion, [1, 21, 0]);
                    string name = b.GetString("Motif") ?? b.GetString("Motive") ?? "";
                    bool expectCamel = art.JavaSince <= 3105; // the 26 old ones and the four elements
                    if (b.ContainsKey("Motif") == motif && b.ContainsKey("Motive") == motive && name == art.BedrockName && char.IsUpper(name[0]) == expectCamel) keys++;
                    else wrong.Add($"{t.Name}: {art.Id} → {Snbt.Write(b, SnbtStyle.Compact)}");
                    var back = new NbtCompound { { "id", new NbtString("minecraft:painting") }, { "Pos", c["Pos"]!.Clone() } };
                    Hanging.FromBedrock(back, b);
                    if (back.GetString("variant") == c.GetString("variant") && back.GetInt("facing") == four && Hanging.BlockOf(back) == Hanging.BlockOf(c)) same++;
                    else wrong.Add($"{t.Name}: {art.Id} facing {four} read back as {Snbt.Write(back, SnbtStyle.Compact)}");
                }
        foreach (var w in wrong.Take(8)) Program.Info("  " + w);
        Program.Check(keys == trips, $"Bedrock targets ({targets.Count}): Bedrock's own art names under Motive (before 1.20), both (1.20) or Motif (1.21+) ({keys} of {trips})");
        Program.Check(same == trips, $"Bedrock: every painting reads back with the same art, facing and block — the block worked out from the centre ({same} of {trips})");
        static bool Has(string v, int[] version) => Hanging.MissingInBedrock(Painting(v, 0, 0, 0, 0), version) == null;
        Program.Check(!Has("minecraft:orb", [1, 20, 0]) && Has("minecraft:orb", [1, 21, 0]) && !Has("minecraft:dennis", [1, 21, 40]) && Has("minecraft:dennis", [1, 21, 120])
            && !Has("minecraft:wither", [0, 9, 5]) && Has("minecraft:wither", [1, 1, 0]) && Has("minecraft:earth", [1, 1, 0]) && !Has("mypack:sunrise", [1, 21, 120]),
            "Bedrock art by version: 1.21's from 1.21.0, Dennis from 1.21.93, Wither from Pocket 1.0.7, the four elements always, data-pack art never");
        // A modern Bedrock world names the art "Motif"; an older one "Motive".
        foreach (var (key, label) in new[] { ("Motif", "Bedrock 1.21 (Motif)"), ("Motive", "Bedrock 1.19 (Motive)") })
        {
            var raw = new NbtCompound { { "identifier", new NbtString("minecraft:painting") }, { key, new NbtString("SkullAndRoses") }, { "Direction", new NbtByte(1) }, { "Pos", new NbtList(NbtType.Float) { new NbtFloat(10.96875f), new NbtFloat(65f), new NbtFloat(-3f) } } };
            var c = Entities.FromBedrock(raw)!;
            Program.Check(c.GetString("variant") == "minecraft:skull_and_roses" && c.GetInt("facing") == 1 && Hanging.BlockOf(c) == (10, 64, -4),
                $"{label}: \"SkullAndRoses\" reads as minecraft:skull_and_roses facing west, hanging in 10, 64, -4 ({Hanging.BlockOf(c)})");
        }
    }

    /// <summary>Real worlds with paintings converted to Java and Bedrock targets; the paintings checked in each output.</summary>
    static void Convert()
    {
        string twilight = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_12_2_twilight_forest");
        string console = Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");
        foreach (var (label, source, dims, targets) in new (string, string, string[]?, string[])[]
        {
            ("Twilight Forest 1.12.2", twilight, ["DIM7"], ["26.1", "1.21.5", "1.21.4", "1.19 – 1.19.2", "1.18 – 1.18.2", "1.16.2 – 1.16.5"]),
            ("Xbox 360 TU70", console, null, ["26.1", "1.12 – 1.12.2", "1.8 – 1.8.9", "1.7.2 – 1.7.10", "1.21.120 – 26.x", "1.21.0 – 1.21.30", "1.20.0 – 1.20.8x", "1.16.0 – 1.16.20x"]),
        })
        {
            if (!File.Exists(source) && !Directory.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            foreach (var name in targets)
            {
                var target = TargetCatalog.All.First(t => t.Name.EndsWith(name, StringComparison.Ordinal) && t.Kind is TargetKind.Java or TargetKind.Bedrock);
                string outDir = Path.Combine(TestPaths.Output, "hanging", string.Concat((label + " " + target.Name).Where(char.IsLetterOrDigit)));
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                try
                {
                    List<(string Dim, NbtCompound E)> before;
                    ConversionReport report;
                    using (var w = WorldLoader.Open(source))
                    {
                        before = Canonical(w, dims);
                        var options = new ConversionOptions();
                        if (dims != null) options.Dimensions = [.. dims];
                        report = WorldConverter.Run(w, target, outDir, options, new Progress<ConversionProgress>(_ => { }));
                    }
                    using var o = WorldLoader.Open(report.OpenPath ?? outDir);
                    var after = Canonical(o, dims);
                    Compare($"{label} → {target.Name}", before, after, target);
                    if (target.Kind == TargetKind.Java) RawNames($"{label} → {target.Name}", o, target);
                }
                catch (Exception ex) { Program.Check(false, $"{label} → {target.Name}: threw {ex.GetType().Name}: {ex.Message}"); }
                finally { try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch (IOException) { } }
            }
        }
    }

    /// <summary>The canonical paintings and frames of a world (only the dimensions asked for).</summary>
    static List<(string Dim, NbtCompound E)> Canonical(SaveWorld w, string[]? dims)
    {
        var list = new List<(string, NbtCompound)>();
        foreach (var dim in w.Dimensions)
        {
            string id = CanonicalReader.DimensionId(dim);
            if (dims != null && !dims.Any(d => d == id || JavaWorldWriter.OutputDimensionId(d) == id)) continue;
            var reader = new CanonicalReader(w, dim);
            foreach (var pos in dim.ChunkPositions)
                foreach (var e in reader.Get(pos)?.Entities ?? [])
                    if (Hanging.Is(e.GetString("id") ?? "") && e.GetString("id") != "minecraft:leash_knot") list.Add((id, e));
        }
        return list;
    }

    /// <summary>
    /// Same paintings and frames after the conversion: art and facing, and the same places relative to each other (console
    /// worlds move as a whole when they are converted, so places are compared from the lowest corner of them all); each
    /// output one's block is the block its centre is in.
    /// </summary>
    static void Compare(string label, List<(string Dim, NbtCompound E)> before, List<(string Dim, NbtCompound E)> after, ConversionTarget target)
    {
        bool bedrock = target.Kind == TargetKind.Bedrock;
        // Floor and ceiling frames cannot go before 1.13 (they go to the rescue chests).
        var expected = before.Where(b => !(Hanging.IsFrame(b.E.GetString("id") ?? "") && Hanging.FourWay(Hanging.Facing(b.E)) == null && target.Kind == TargetKind.Java && target.DataVersion < 1451)).ToList();
        string Key((string Dim, NbtCompound E) x, (int X, int Y, int Z) origin)
        {
            var (bx, by, bz) = Hanging.BlockOf(x.E);
            string id = (x.E.GetString("id") ?? "").Replace("glow_item_frame", "item_frame");
            return $"{JavaWorldWriter.OutputDimensionId(x.Dim)} {id} {x.E.GetString("variant")} {Hanging.Facing(x.E)} {bx - origin.X},{by - origin.Y},{bz - origin.Z}";
        }
        (int, int, int) Origin(List<(string Dim, NbtCompound E)> l) => l.Count == 0 ? (0, 0, 0) : (l.Min(x => Hanging.BlockOf(x.E).X), l.Min(x => Hanging.BlockOf(x.E).Y), l.Min(x => Hanging.BlockOf(x.E).Z));
        var a = expected.Select(x => Key(x, Origin(expected))).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var b = after.Select(x => Key(x, Origin(after))).OrderBy(k => k, StringComparer.Ordinal).ToList();
        int consistent = after.Count(x =>
        {
            var (bx, by, bz) = Hanging.BlockOf(x.E);
            var (cx, cy, cz) = Hanging.CentreFromBlock(bx, by, bz, Hanging.Facing(x.E), Hanging.OffsetOf(x.E));
            return Entities.Position(x.E) is var (px, py, pz) && Math.Abs(px - cx) < 0.01 && Math.Abs(py - cy) < 0.01 && Math.Abs(pz - cz) < 0.01;
        });
        foreach (var k in a.Except(b).Take(4)) Program.Info($"   {label}: missing {k}");
        foreach (var k in b.Except(a).Take(4)) Program.Info($"   {label}: extra {k}");
        Program.Check(a.Count > 0 && a.SequenceEqual(b), $"{label}: every painting and frame arrives with its art, facing and place ({b.Count} of {a.Count})");
        Program.Check(consistent == after.Count, $"{label}: each one's block is the block its centre is in ({consistent} of {after.Count}{(bedrock ? ", frames read from Bedrock's frame blocks" : "")})");
    }

    /// <summary>The output's raw paintings and frames use the target version's own field names.</summary>
    static void RawNames(string label, SaveWorld o, ConversionTarget t)
    {
        int seen = 0, right = 0;
        foreach (var dim in o.Dimensions)
            foreach (var pos in dim.ChunkPositions)
            {
                NbtCompound? root = null;
                try { root = dim.LoadChunkNbt(pos); } catch { }
                var level = root?.GetCompound("Level") ?? root;
                var entities = new List<(NbtCompound E, int Dv)>();
                int chunkDv = (int)(root?.GetLongOrNull("DataVersion") ?? t.DataVersion);
                foreach (var e in (level?.GetList("Entities") ?? level?.GetList("entities"))?.OfType<NbtCompound>() ?? []) entities.Add((e, chunkDv));
                if (dim is JavaRegionDimension jr)
                    try
                    {
                        var file = jr.LoadEntitiesNbt(pos);
                        int fileDv = (int)(file?.GetLongOrNull("DataVersion") ?? t.DataVersion);
                        foreach (var e in file?.GetList("Entities")?.OfType<NbtCompound>() ?? []) entities.Add((e, fileDv));
                    }
                    catch { }
                foreach (var (e, dv) in entities)
                {
                    string id = (e.GetString("id") ?? "").Replace("minecraft:", "");
                    if (id is not ("painting" or "Painting" or "item_frame" or "ItemFrame")) continue;
                    seen++;
                    string canonicalId = id is "Painting" or "painting" ? "minecraft:painting" : "minecraft:item_frame";
                    var asWritten = new ConversionTarget { Kind = TargetKind.Java, Name = t.Name, DataVersion = dv, LegacyLevel = t.LegacyLevel };
                    if (NamesRight(e, canonicalId, asWritten)) right++;
                    else if (seen - right <= 3) Program.Info($"   {label}: {Snbt.Write(e, SnbtStyle.Compact)[..Math.Min(300, Snbt.Write(e, SnbtStyle.Compact).Length)]}");
                }
            }
        Program.Check(seen > 0 && right == seen, $"{label}: the saved paintings and frames use the version's own field names ({right} of {seen})");
    }

    static NbtCompound Painting(string variant, int four, int x, int y, int z)
    {
        var c = new NbtCompound { { "id", new NbtString("minecraft:painting") }, { "variant", new NbtString(variant) }, { "facing", new NbtByte((sbyte)four) } };
        var art = Hanging.Art(variant);
        var off = art == null ? Hanging.Offset.None : new Hanging.Offset(art.Width % 2 == 0 ? 0.5 : 0, art.Height % 2 == 0 ? 0.5 : 0);
        var (cx, cy, cz) = Hanging.CentreFromBlock(x, y, z, Hanging.SixWay(four), off);
        c["Pos"] = Pos(cx, cy, cz);
        c.SetInt("TileX", x); c.SetInt("TileY", y); c.SetInt("TileZ", z);
        return c;
    }

    static NbtCompound Frame(int six, int x, int y, int z)
    {
        var (cx, cy, cz) = Hanging.CentreFromBlock(x, y, z, six, Hanging.Offset.None);
        return new NbtCompound
        {
            { "id", new NbtString("minecraft:item_frame") }, { "Facing", new NbtByte((sbyte)six) }, { "Pos", Pos(cx, cy, cz) },
            { "TileX", new NbtInt(x) }, { "TileY", new NbtInt(y) }, { "TileZ", new NbtInt(z) },
        };
    }

    static NbtList Pos(double x, double y, double z) => new(NbtType.Double) { new NbtDouble(x), new NbtDouble(y), new NbtDouble(z) };

    /// <summary>The raw hanging entities in a Java or console world, with the data version their chunk was written at.</summary>
    static List<(NbtCompound Raw, int Dv)> Raw(string path)
    {
        var list = new List<(NbtCompound, int)>();
        using var w = WorldLoader.Open(path);
        bool console = w is MinecraftStudio.Core.LegacyConsole.ConsoleWorld;
        foreach (var dim in w.Dimensions)
            foreach (var pos in dim.ChunkPositions)
            {
                NbtCompound? root = null;
                try { root = dim.LoadChunkNbt(pos); } catch { }
                if (root == null) continue;
                var level = root.GetCompound("Level") ?? root;
                int dv = console ? 1343 : (int)(root.GetLongOrNull("DataVersion") ?? 0);
                var entities = (level.GetList("Entities") ?? level.GetList("entities"))?.OfType<NbtCompound>().ToList() ?? [];
                if (dim is JavaRegionDimension jr)
                    try { entities.AddRange(jr.LoadEntitiesNbt(pos)?.GetList("Entities")?.OfType<NbtCompound>() ?? []); } catch { }
                foreach (var e in entities)
                    if (Ids.Contains((e.GetString("id") ?? "").Replace("minecraft:", ""))) list.Add((e, dv));
            }
        return list;
    }

    /// <summary>Every hanging entity in the sample worlds, raw, with the fields that place and turn it.</summary>
    static void Scan()
    {
        var worlds = Directory.EnumerateFiles(TestPaths.Data, "level.dat", SearchOption.AllDirectories)
            .Where(f => !f.Contains("mapdata")).Select(f => Path.GetDirectoryName(f)!).ToList();
        worlds.AddRange(Directory.EnumerateFiles(Path.Combine(TestPaths.Data, "lce"), "savegame.dat", SearchOption.AllDirectories));
        foreach (var path in worlds)
        {
            int shown = 0, total = 0;
            try
            {
                using var w = WorldLoader.Open(path);
                void Show(NbtCompound e, string where)
                {
                    string id = (e.GetString("id") ?? e.GetString("identifier") ?? "").Replace("minecraft:", "");
                    if (e["id"] is NbtInt numeric) id = numeric.Value switch { 83 => "painting", 82 => "item_frame?", _ => id };
                    if (!Ids.Contains(id)) return;
                    total++;
                    if (shown++ >= 6) return;
                    var keep = new NbtCompound();
                    foreach (var k in new[] { "id", "identifier", "Pos", "Rotation", "TileX", "TileY", "TileZ", "block_pos", "Facing", "facing", "Direction", "Dir", "Motive", "variant", "ItemRotation" })
                        if (e[k] is NbtTag t) keep[k] = t;
                    Console.WriteLine($"   {where}: {Snbt.Write(keep, SnbtStyle.Compact)}");
                }
                Console.WriteLine($"== {Path.GetRelativePath(TestPaths.Data, path)} ({w.FormatDescription})");
                if (w is BedrockWorld bw)
                {
                    foreach (var key in bw.Db.Keys())
                    {
                        bool actor = key.Length > 11 && System.Text.Encoding.ASCII.GetString(key, 0, 11) == "actorprefix";
                        bool chunkList = BedrockKeys.TryParseChunkKey(key, out var ck) && ck.Tag == ChunkTag.Entity;
                        if (!actor && !chunkList) continue;
                        var v = bw.Db.Get(key);
                        if (v == null) continue;
                        foreach (var t in NbtBinary.ReadAll(v, NbtFormat.Bedrock)) if (t.Tag is NbtCompound a) Show(a, actor ? "actor" : "chunk list");
                    }
                }
                else if (w is PocketLegacyWorld pw)
                {
                    foreach (var e in pw.Entities?.Roots.FirstOrDefault()?.Tag is NbtCompound root ? root.GetList("Entities")?.OfType<NbtCompound>() ?? [] : [])
                        Show(e, "entities.dat");
                }
                else
                    foreach (var dim in w.Dimensions)
                        foreach (var pos in dim.ChunkPositions)
                        {
                            NbtCompound? root = null;
                            try { root = dim.LoadChunkNbt(pos); } catch { }
                            var level = root?.GetCompound("Level") ?? root;
                            foreach (var e in (level?.GetList("Entities") ?? level?.GetList("entities"))?.OfType<NbtCompound>() ?? []) Show(e, $"{dim.DisplayName} {pos}");
                            if (dim is JavaRegionDimension jr)
                                try { foreach (var e in jr.LoadEntitiesNbt(pos)?.GetList("Entities")?.OfType<NbtCompound>() ?? []) Show(e, $"{dim.DisplayName} {pos} (entities file)"); } catch { }
                        }
                Console.WriteLine($"   {total} hanging entities");
            }
            catch (Exception ex) { Console.WriteLine($"== {Path.GetRelativePath(TestPaths.Data, path)}: not read ({ex.Message})"); }
        }
    }
}
