// McTests/ShareTests.cs: "share" — a world packed into one file (.mcworld for Bedrock, .zip for Java) unpacks to the very
// same files, and opens as the same world.
// Owns: ShareTests — copies of a Bedrock and a Java sample world packed with WorldShare, the file unpacked in the test
// folder: every file byte for byte the same (level.dat at the top of a .mcworld; the world's folder at the top of a .zip),
// and opened: same name, chunks and players. A file asked for inside the world's own folder is refused.
using System.IO.Compression;
using System.Security.Cryptography;
using MinecraftStudio.Core.World;

static class ShareTests
{
    public static void Run(string[] args)
    {
        string dir = Path.Combine(TestPaths.Output, "share", DateTime.Now.ToString("HHmmss"));
        foreach (var (label, source) in new[]
        {
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
        })
        {
            if (!Directory.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            string copy = Path.Combine(dir, string.Concat(label.Where(char.IsLetterOrDigit)), "My World");
            CopyDir(source, copy);
            string file, unpacked = Path.Combine(dir, string.Concat(label.Where(char.IsLetterOrDigit)) + "-unpacked");
            string name;
            int chunks, players;
            using (var w = WorldLoader.Open(copy))
            {
                name = w.Name;
                chunks = w.Dimensions.Sum(d => d.ChunkPositions.Count);
                players = w.Players.Count;
                file = Path.Combine(dir, "shared" + WorldShare.Extension(w));
                long size = WorldShare.Pack(w, file);
                Program.Check(size > 0 && File.Exists(file) && !File.Exists(file + ".partial"), $"{label}: packed into {Path.GetFileName(file)} ({size / 1024.0:0} KB)");
                Program.Check(Refused(() => WorldShare.Pack(w, Path.Combine(copy, "inside" + WorldShare.Extension(w)))), $"{label}: a file inside the world's own folder is refused");
            }
            ZipFile.ExtractToDirectory(file, unpacked);
            // A .mcworld holds the world's files; a .zip holds its folder.
            string root = file.EndsWith(".zip") ? Path.Combine(unpacked, "My World") : unpacked;
            Program.Check(File.Exists(Path.Combine(root, "level.dat")), $"{label}: level.dat is where the game looks ({Path.GetRelativePath(unpacked, Path.Combine(root, "level.dat"))})");
            var a = Hashes(copy);
            var b = Hashes(root);
            Program.Check(a.SequenceEqual(b), $"{label}: every file unpacks byte for byte the same ({b.Count} of {a.Count})");
            using (var w = WorldLoader.Open(root))
                Program.Check(w.Name == name && w.Dimensions.Sum(d => d.ChunkPositions.Count) == chunks && w.Players.Count == players,
                    $"{label}: opens as the same world ({w.Name}, {chunks:N0} chunks, {players} players)");
        }
        WebMaps(dir);
    }

    /// <summary>
    /// A dimension as a web map (WebMap): a tile per region whose pixels are the whole-dimension picture's (the shading runs on
    /// across tiles), zoomed-out levels down to one tile, map.js listing exactly the tiles written and the markers, the
    /// viewer's script valid JavaScript (when Node.js is installed), and a folder with other files in it refused.
    /// </summary>
    static void WebMaps(string dir)
    {
        string source = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(source)) return;
        using var w = WorldLoader.Open(source);
        var dim = w.Dimensions.First(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld");
        string folder = Path.Combine(dir, "webmap");
        var markers = new List<MinecraftStudio.Core.Rendering.WebMapMarker> { new("Spawn", 0, 0, "Spawn", 0xFFD54F), new("Village", 100, -40, "Structures", 0x9C27B0, "3 loot containers never opened") };
        var r = MinecraftStudio.Core.Rendering.WebMap.Export(dim, folder, "Test world", markers, new MinecraftStudio.Core.Rendering.MapRenderOptions());
        // The whole dimension's picture: every region with something to see has its tile, and the tiles' pixels are its pixels.
        var whole = MinecraftStudio.Core.Rendering.DimensionRenderer.Render(dim, new MinecraftStudio.Core.Rendering.MapRenderOptions());
        var visible = new HashSet<(int, int)>();
        for (int z = 0; z < whole.Height; z++)
            for (int x = 0; x < whole.Width; x++)
                if (whole.Pixels[z * whole.Width + x] >> 24 != 0) visible.Add(((int)Math.Floor((whole.MinChunkX * 16 + x) / 512.0), (int)Math.Floor((whole.MinChunkZ * 16 + z) / 512.0)));
        var level0 = Directory.GetFiles(Path.Combine(folder, "tiles", "0"), "*.png");
        int top = r.Levels - 1, topTiles = Directory.GetFiles(Path.Combine(folder, "tiles", top.ToString()), "*.png").Length;
        Program.Check(File.Exists(r.IndexPath) && level0.Length == visible.Count && topTiles is >= 1 and <= 4 && r.Levels < 12,
            $"web map: a tile for each of the {visible.Count} regions with something to see ({level0.Length}), {r.Levels} zoom levels down to {topTiles} tile(s)");
        int differ = 0, compared = 0;
        foreach (var file in level0)
        {
            var parts = Path.GetFileNameWithoutExtension(file).Split('_');
            int rx = int.Parse(parts[0]), rz = int.Parse(parts[1]);
            var tile = MinecraftStudio.Core.Imaging.Png.Decode(File.ReadAllBytes(file));
            Program.Check(tile.Width == 512 && tile.Height == 512, $"web map: tile {rx}_{rz} is 512 × 512");
            for (int z = 0; z < 512; z += 4)
                for (int x = 0; x < 512; x += 4)
                {
                    int wx = rx * 512 + x - whole.MinChunkX * 16, wz = rz * 512 + z - whole.MinChunkZ * 16;
                    if (wx < 0 || wz < 0 || wx >= whole.Width || wz >= whole.Height) continue;
                    uint a = whole.Pixels[wz * whole.Width + wx], b = tile.Pixels[z * 512 + x];
                    if (a >> 24 == 0 && b >> 24 == 0) continue;
                    compared++;
                    if (a != b) differ++;
                }
        }
        Program.Check(compared > 10_000 && differ == 0, $"web map: the tiles' pixels are the whole dimension's picture ({compared:N0} compared, {differ} differ)");
        string js = File.ReadAllText(Path.Combine(folder, "map.js"));
        using var data = System.Text.Json.JsonDocument.Parse(js["window.MAP = ".Length..js.LastIndexOf(';')]);
        var listed = data.RootElement.GetProperty("levels").EnumerateArray().Select(l => l.EnumerateArray().Select(t => t.GetString()!).ToHashSet()).ToList();
        bool matches = listed.Count == r.Levels && Enumerable.Range(0, r.Levels).All(k =>
            listed[k].SetEquals(Directory.GetFiles(Path.Combine(folder, "tiles", k.ToString()), "*.png").Select(Path.GetFileNameWithoutExtension)!));
        Program.Check(matches && data.RootElement.GetProperty("markers").GetArrayLength() == 2, "web map: map.js lists exactly the tiles written, and the markers");
        string html = File.ReadAllText(r.IndexPath);
        int s0 = html.LastIndexOf("<script>", StringComparison.Ordinal) + "<script>".Length, s1 = html.LastIndexOf("</script>", StringComparison.Ordinal);
        string script = Path.Combine(dir, "viewer.js");
        File.WriteAllText(script, html[s0..s1]);
        try
        {
            using var node = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", ["--check", script]) { RedirectStandardError = true, UseShellExecute = false });
            string err = node!.StandardError.ReadToEnd();
            node.WaitForExit();
            Program.Check(node.ExitCode == 0, "web map: the viewer's script is valid JavaScript" + (node.ExitCode == 0 ? "" : ": " + err));
        }
        catch (System.ComponentModel.Win32Exception) { Program.Info("(Node.js is not installed: the viewer's script is not checked)"); }
        Directory.CreateDirectory(Path.Combine(dir, "webmap-other"));
        File.WriteAllText(Path.Combine(dir, "webmap-other", "keep.txt"), "mine");
        Program.Check(Refused(() => MinecraftStudio.Core.Rendering.WebMap.Export(dim, Path.Combine(dir, "webmap-other"), "x", [], new MinecraftStudio.Core.Rendering.MapRenderOptions())), "web map: a folder with other files in it is refused");
    }

    static bool Refused(Action a)
    {
        try { a(); return false; }
        catch (IOException) { return true; }
    }

    static List<string> Hashes(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .OrderBy(f => Path.GetRelativePath(folder, f), StringComparer.OrdinalIgnoreCase)
        .Select(f => Path.GetRelativePath(folder, f).ToLowerInvariant() + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToList();

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
