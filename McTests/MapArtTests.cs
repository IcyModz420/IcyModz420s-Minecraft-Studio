// McTests/MapArtTests.cs: "mapart" — the maps of a world as pictures, and pictures made into map art.
// Owns: MapArtTests — every map colour as a picture and back is the same colour; a picture cut into a wall of locked maps
// left to right, then down; an old version's palette never used past its last colour; dithering keeps the picture's
// average colour; on copies of the Java 1.16.5 and Bedrock 1.16 sample worlds: new maps added after the highest id
// (idcounts.dat raised in its own layout), read back with exactly the colours made, given to the player as filled maps
// with their ids; consoles refused; the originals byte for byte unchanged.
using System.Security.Cryptography;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class MapArtTests
{
    public static void Run(string[] args)
    {
        Colours();
        Buildable();
        foreach (var (label, source) in new[]
        {
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        })
        {
            if (!Directory.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            var before = Hashes(source);
            string copy = Path.Combine(TestPaths.Output, "mapart", string.Concat(label.Where(char.IsLetterOrDigit)));
            if (Directory.Exists(copy)) Directory.Delete(copy, true);
            CopyDir(source, copy);
            try { Into(label, copy); }
            catch (Exception ex) { Program.Check(false, $"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
            finally { try { Directory.Delete(copy, true); } catch (IOException) { } }
            Program.Check(before.SequenceEqual(Hashes(source)), $"{label}: the original world is byte for byte unchanged");
        }
        string console = Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");
        if (File.Exists(console))
            using (var w = WorldLoader.Open(console))
                Program.Check(MapArt.CannotAdd(w) != null, $"a console world is refused for adding map art ({MapArt.CannotAdd(w)})");
    }

    /// <summary>
    /// Map art to build in survival: every block table colour is a real map colour; the staircase palette is the flat one
    /// in three shades, never the darkest or water; the structure, read the way the game draws a map (a block higher than
    /// the one north of it is light, level normal, lower dark), gives back exactly the colours planned; saved as a 1.12.2
    /// schematic and as the newest, no block becomes a stand-in; the material list counts every block placed.
    /// </summary>
    static void Buildable()
    {
        Console.WriteLine("== Map art to build in survival");
        int newest = 9999;
        var flat = MapArt.BuildableColors(newest, staircase: false);
        var stairs = MapArt.BuildableColors(newest, staircase: true);
        Program.Check(flat.Count >= 55 && stairs.Count == flat.Count * 3 && stairs.All(c => (c.Color & 3) != 3 && c.Color >> 2 != 12) && flat.All(c => (c.Color & 3) == 1),
            $"the buildable colours: {flat.Count} flat, {stairs.Count} with the steps' shades (no darkest shade, no water)");
        Program.Check(MapArt.BuildableColors(1343, true).All(c => c.Color >> 2 <= 51) && MapArt.BuildableColors(1343, true).Count < stairs.Count, "Java 1.12.2 builds only the colours it has");

        // A picture of stripes and a gradient, dithered: many shades, so the steps go up and down.
        var pic = new RgbaImage(256, 128);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 256; x++)
                pic.Pixels[y * 256 + x] = RgbaImage.Argb(255, x % 256, y * 2, (x + y) % 200 + 40);
        var maps = MapArt.MakeBuildable(pic, 2, 1, new PictureOptions { Fit = PictureFitMode.Stretch }, newest, staircase: true, dither: true);
        bool drawsBack = true;
        long placed = 0, counted = 0;
        foreach (var m in maps)
        {
            var s = MapArt.Building(m, newest);
            int Top(int x, int z) { for (int y = s.SizeY - 1; y >= 0; y--) if (!s.Get(x, y, z).IsAir) return y; return -1; }
            for (int x = 0; x < 128; x++)
                for (int z = 0; z < 128; z++)
                {
                    int here = Top(x, z + 1), north = Top(x, z);
                    int shade = here > north ? 2 : here < north ? 0 : 1;
                    var block = s.Get(x, here, z + 1);
                    if (MapArt.BuildBlock(m.Colors[z * 128 + x] >> 2, newest) != block || shade != (m.Colors[z * 128 + x] & 3)) drawsBack = false;
                }
            for (int y = 0; y < s.SizeY; y++) for (int z = 0; z < s.SizeZ; z++) for (int x = 0; x < s.SizeX; x++) if (!s.Get(x, y, z).IsAir) placed++;
            counted += MinecraftStudio.Core.Structures.StructureMaterials.Count(s).Sum(l => l.Count);
            if (m == maps[0])
            {
                string dir = Path.Combine(TestPaths.Output, "mapart-build");
                Directory.CreateDirectory(dir);
                var oldStandIns = MinecraftStudio.Core.Structures.StructureFile.SaveChecked(MapArt.Building(MapArt.MakeBuildable(pic, 2, 1, new PictureOptions { Fit = PictureFitMode.Stretch }, 1343, true, true)[0], 1343), Path.Combine(dir, "old.schematic"), 1343);
                var newStandIns = MinecraftStudio.Core.Structures.StructureFile.SaveChecked(s, Path.Combine(dir, "new.litematic"), 3953);
                Program.Check(oldStandIns.Count == 0 && newStandIns.Count == 0, $"saved for Java 1.12.2 (.schematic) and the newest (.litematic): no block became a stand-in ({string.Join(", ", oldStandIns.Concat(newStandIns).Take(3).Select(t => t.From))})");
                Program.Info($"one map to build: {s.SizeX} × {s.SizeY} × {s.SizeZ} blocks (the reference row on the north side)");
            }
        }
        Program.Check(drawsBack, "the structures, read the way the game draws a map, give back exactly the colours planned (every block, every shade)");
        Program.Check(placed == counted, $"the material list counts every block placed ({counted:N0} of {placed:N0})");
    }

    static void Colours()
    {
        var palette = MapArt.Colors(61);
        var m = new CanonicalMap();
        for (int i = 0; i < m.Colors.Length; i++) m.Colors[i] = palette[i % palette.Count].Color;
        var picture = MapArt.Picture(m);
        var back = MapArt.Quantize(picture, palette, dither: false);
        int same = 0;
        for (int i = 0; i < back.Length; i++) if (Maps.Rgb(back[i]) == Maps.Rgb(m.Colors[i])) same++;
        Program.Check(same == back.Length, $"every map colour ({palette.Count} of them, four shades each) as a picture and back is the same colour ({same:N0} of {back.Length:N0})");
        Program.Check(MapArt.Picture(new CanonicalMap()).Pixels.All(p => p == 0), "an empty map is see-through");

        // A wall: left half red, right half blue → two maps, left red, right blue, both locked.
        var pic = new RgbaImage(256, 128);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 256; x++) pic[x, y] = x < 128 ? 0xFFFF0000u : 0xFF0000FFu;
        var wall = MapArt.Make(pic, 2, 1, new PictureOptions { Fit = PictureFitMode.Stretch, Smooth = false }, 61, dither: false);
        bool leftRed = wall[0].Colors.All(c => (Maps.Rgb(c) >> 16 & 255) > 150 && (Maps.Rgb(c) & 255) < 100);
        bool rightBlue = wall[1].Colors.All(c => (Maps.Rgb(c) & 255) > 150 && (Maps.Rgb(c) >> 16 & 255) < 100);
        Program.Check(wall.Count == 2 && wall.All(x => x.Locked) && leftRed && rightBlue, "a picture cut into a wall: left to right, each map locked");
        var tall = MapArt.Make(pic, 1, 2, new PictureOptions { Fit = PictureFitMode.Stretch, Smooth = false }, 61, false);
        Program.Check(tall.Count == 2, "1 across × 2 down makes two maps");

        // An old version's palette (Java before 1.7: 27 base colours) is never used past its last colour.
        var photo = new RgbaImage(128, 128);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) photo[x, y] = RgbaImage.Argb(255, x * 2, y * 2, 255 - x);
        var old = MapArt.Make(photo, 1, 1, new PictureOptions { Fit = PictureFitMode.Stretch }, 27, dither: true);
        Program.Check(old[0].Colors.All(c => c >> 2 <= 27), "an old version's maps use only the colours it has");

        // Dithering keeps the average colour of a gradient close; plain nearest colour bands it.
        var grad = new RgbaImage(128, 128);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { int v = x * 2; grad[x, y] = RgbaImage.Argb(255, v, v, v); }
        var dithered = MapArt.Make(grad, 1, 1, new PictureOptions { Fit = PictureFitMode.Stretch, Smooth = false }, 61, dither: true)[0];
        double source = grad.Pixels.Average(p => RgbaImage.R(p));
        double result = dithered.Colors.Average(c => (Maps.Rgb(c) >> 16) & 255);
        Program.Check(Math.Abs(source - result) < 4 && dithered.Colors.Distinct().Count() > 4, $"dithering keeps the average brightness ({source:0.0} → {result:0.0}) with many colours");
    }

    static void Into(string label, string folder)
    {
        List<int> ids;
        List<CanonicalMap> made;
        int existingHighest;
        using (var w = WorldLoader.Open(folder))
        {
            Program.Check(MapArt.CannotAdd(w) == null, $"{label}: maps can be added");
            var existing = Maps.Read(w);
            existingHighest = existing.Count == 0 ? -1 : existing.Max(m => m.Id);
            var pic = new RgbaImage(256, 256);
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) pic[x, y] = RgbaImage.Argb(255, x, y, (x + y) / 2);
            made = MapArt.Make(pic, 2, 2, new PictureOptions { Fit = PictureFitMode.Fill }, MapArt.MaxBase(w), dither: true);
            ids = MapArt.Add(w, made);
            Program.Check(ids.Count == 4 && ids[0] > existingHighest && ids.Distinct().Count() == 4, $"{label}: four maps added after the world's highest id ({string.Join(", ", ids)}; highest was {existingHighest})");
            var host = w.Players.FirstOrDefault();
            if (host != null)
            {
                int given = MapArt.Give(w, host, ids);
                Program.Check(given == 4, $"{label}: the four maps are given to {host.Name} ({given})");
            }
            if (w is JavaWorld j)
            {
                var counts = NbtFile.Load(Path.Combine(j.Folder, "data", "idcounts.dat")).RootCompound;
                long counter = (counts.GetCompound("data") ?? counts).GetLong("map", -1);
                Program.Check(counter == ids.Max(), $"{label}: idcounts.dat is raised to the last new map ({counter})");
            }
        }
        using (var again = WorldLoader.Open(folder))
        {
            var read = Maps.Read(again).ToDictionary(m => m.Id);
            int same = 0, cells = 0;
            foreach (var m in made)
                if (read.TryGetValue(m.Id, out var r))
                    for (int i = 0; i < m.Colors.Length; i++) { cells++; if (Maps.Rgb(r.Colors[i]) == Maps.Rgb(m.Colors[i])) same++; }
            Program.Check(cells == 4 * 128 * 128 && same == cells && made.All(m => read.TryGetValue(m.Id, out var r) && r.Locked), $"{label}: reopened, the new maps read back with exactly the colours made, locked ({same:N0} of {cells:N0})");
            var host = again.Players.FirstOrDefault();
            if (host != null)
            {
                var inv = new PlayerInventory(again, host);
                var mapIds = new List<long>();
                foreach (var part in new[] { InventoryPart.Hotbar, InventoryPart.Storage })
                    for (int i = 0; i < PlayerInventory.Size(part); i++)
                        if (inv.Get(part, i) is { IsEmpty: false, Raw: NbtCompound raw } it && it.Id.Contains("filled_map"))
                            mapIds.Add(raw.GetCompound("components")?.GetLong("minecraft:map_id", -1) is long a and >= 0 ? a
                                : raw.GetCompound("tag")?.GetLongOrNull("map") ?? raw.GetCompound("tag")?.GetLongOrNull("map_uuid") ?? raw.GetLong("Damage"));
                Program.Check(ids.All(id => mapIds.Contains(id)), $"{label}: reopened, the player holds a filled map for each new map ({string.Join(", ", mapIds)})");
            }
        }
    }

    static List<string> Hashes(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => Path.GetRelativePath(folder, f) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToList();

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
