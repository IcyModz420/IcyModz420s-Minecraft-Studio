// McTests/SkinTests.cs: skins: the PNG reader against Windows' own encoder (every colour type, bit depth and
// interlacing, written by tools/png-oracle/make-pngs.ps1), hand-made PNGs for the cases that encoder cannot
// write, PNG round trips and TGA reading of the installed games' textures, the 3D skin model's geometry, and
// the skin layout conversions against the rules of the game.
// Owns: SkinTests.
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;

/// <summary>
/// skins: the PNG reader against Windows' own encoder (every colour type, bit depth and interlacing, written by
/// tools/png-oracle/make-pngs.ps1), hand-made PNGs for the cases that encoder cannot write, PNG round trips and TGA
/// reading of the installed games' textures, the 3D skin model's geometry, and the skin layout conversions against the
/// rules of the game.
/// </summary>
static class SkinTests
{
    public static void Run(string[] args)
    {
        Console.WriteLine("== PNG reader vs Windows' encoder");
        Oracle();
        Console.WriteLine("== Hand-made PNGs (grey + alpha, colour keys, damaged files)");
        Handmade();
        Console.WriteLine("== Game textures: PNG round trips, Bedrock archives and TGA");
        GameTextures();
        Console.WriteLine("== Skin model geometry");
        Geometry();
        Console.WriteLine("== Old 64x32 skins");
        Legacy();
        Console.WriteLine("== Arm width");
        Arms();
        Console.WriteLine("== Default skins of the installed games");
        Defaults();
        PaintTests.Run();
        SkinEditorTests.Run();
        SkinExportTests.Run();
    }

    // ------------------------------------------------------------------ PNG

    static void Oracle()
    {
        string dir = Path.Combine(TestPaths.Output, "png-oracle");
        if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*.png").Length < 26)
        {
            string script = Path.Combine(TestPaths.Data, "..", "tools", "png-oracle", "make-pngs.ps1");
            var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.GetFullPath(script)}\" -Out \"{dir}\"") { UseShellExecute = false, RedirectStandardOutput = true });
            p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
        }
        var files = Directory.GetFiles(dir, "*.png").OrderBy(f => f).ToList();
        Program.Check(files.Count >= 26, $"the oracle wrote {files.Count} PNGs (26 expected)");
        foreach (var f in files)
        {
            var expected = File.ReadAllBytes(Path.ChangeExtension(f, ".argb"));
            RgbaImage img;
            try { img = Png.Decode(File.ReadAllBytes(f)); }
            catch (Exception e) { Program.Check(false, $"{Path.GetFileName(f)}: {e.Message}"); continue; }
            var want = new uint[expected.Length / 4];
            Buffer.BlockCopy(expected, 0, want, 0, expected.Length);
            int wrong = 0;
            for (int i = 0; i < want.Length && i < img.Pixels.Length; i++) if (img.Pixels[i] != want[i]) wrong++;
            Program.Check(img.Width == 37 && img.Height == 23 && wrong == 0, $"{Path.GetFileName(f)}: {wrong} of {want.Length} pixels differ");
            // Our own encoder must give the same pixels back.
            Program.Check(Png.Decode(Png.Encode(img)).SamePixels(img), $"{Path.GetFileName(f)}: re-encoded copy differs");
        }
        Program.Info($"{files.Count} oracle files checked");
    }

    static void Handmade()
    {
        var rng = new Random(7);
        // Grey + alpha, 8 and 16 bit.
        {
            int[][] s = Samples(19, 7, () => [rng.Next(256), rng.Next(256)]);
            var img = Png.Decode(MakePng(19, 7, 8, 4, s));
            Program.Check(Same(img, 19, (x, y) => { var v = s[y * 19 + x]; return RgbaImage.Argb(v[1], v[0], v[0], v[0]); }), "grey + alpha, 8 bit");
            int[][] s16 = Samples(19, 7, () => [rng.Next(65536), rng.Next(65536)]);
            img = Png.Decode(MakePng(19, 7, 16, 4, s16));
            Program.Check(Same(img, 19, (x, y) => { var v = s16[y * 19 + x]; return RgbaImage.Argb(v[1] >> 8, v[0] >> 8, v[0] >> 8, v[0] >> 8); }), "grey + alpha, 16 bit");
        }
        // Grey colour keys: 8 bit, 16 bit (the full 16-bit value counts) and 2 bit.
        {
            int[][] s = Samples(13, 5, () => [rng.Next(3) == 0 ? 77 : rng.Next(256)]);
            var img = Png.Decode(MakePng(13, 5, 8, 0, s, trns: [0, 77]));
            Program.Check(Same(img, 13, (x, y) => { int v = s[y * 13 + x][0]; return RgbaImage.Argb(v == 77 ? 0 : 255, v, v, v); }), "grey 8-bit colour key");
            int[][] s16 = Samples(13, 5, () => [rng.Next(3) switch { 0 => 0x1234, 1 => 0x1235, _ => rng.Next(65536) }]);
            img = Png.Decode(MakePng(13, 5, 16, 0, s16, trns: [0x12, 0x34]));
            Program.Check(Same(img, 13, (x, y) => { int v = s16[y * 13 + x][0]; return RgbaImage.Argb(v == 0x1234 ? 0 : 255, v >> 8, v >> 8, v >> 8); }), "grey 16-bit colour key compares all 16 bits");
            int[][] s2 = Samples(13, 5, () => [rng.Next(4)]);
            img = Png.Decode(MakePng(13, 5, 2, 0, s2, trns: [0, 2]));
            Program.Check(Same(img, 13, (x, y) => { int v = s2[y * 13 + x][0]; return RgbaImage.Argb(v == 2 ? 0 : 255, v * 85, v * 85, v * 85); }), "grey 2-bit colour key");
        }
        // RGB colour keys, 8 and 16 bit.
        {
            int[][] s = Samples(11, 6, () => rng.Next(3) == 0 ? [10, 20, 30] : [rng.Next(256), rng.Next(256), rng.Next(256)]);
            var img = Png.Decode(MakePng(11, 6, 8, 2, s, trns: [0, 10, 0, 20, 0, 30]));
            Program.Check(Same(img, 11, (x, y) => { var v = s[y * 11 + x]; bool key = v[0] == 10 && v[1] == 20 && v[2] == 30; return RgbaImage.Argb(key ? 0 : 255, v[0], v[1], v[2]); }), "RGB 8-bit colour key");
            int[][] s16 = Samples(11, 6, () => rng.Next(3) switch { 0 => [0x0102, 0x0304, 0x0506], 1 => [0x0102, 0x0304, 0x0507], _ => [rng.Next(65536), rng.Next(65536), rng.Next(65536)] });
            img = Png.Decode(MakePng(11, 6, 16, 2, s16, trns: [1, 2, 3, 4, 5, 6]));
            Program.Check(Same(img, 11, (x, y) => { var v = s16[y * 11 + x]; bool key = v[0] == 0x0102 && v[1] == 0x0304 && v[2] == 0x0506; return RgbaImage.Argb(key ? 0 : 255, v[0] >> 8, v[1] >> 8, v[2] >> 8); }), "RGB 16-bit colour key compares all 16 bits");
        }
        // A palette with fewer alpha values than colours (the rest are opaque) and an index past the palette.
        {
            byte[] plte = [255, 0, 0, 0, 255, 0, 0, 0, 255, 9, 9, 9];
            int[][] s = Samples(9, 4, () => [rng.Next(5)]);
            var img = Png.Decode(MakePng(9, 4, 8, 3, s, trns: [0, 128], plte: plte));
            Program.Check(Same(img, 9, (x, y) => s[y * 9 + x][0] switch
            {
                0 => RgbaImage.Argb(0, 255, 0, 0),
                1 => RgbaImage.Argb(128, 0, 255, 0),
                2 => RgbaImage.Argb(255, 0, 0, 255),
                3 => RgbaImage.Argb(255, 9, 9, 9),
                _ => 0u,
            }), "short tRNS palette, index past the palette");
        }
        // Image data split over several IDAT chunks, unknown chunks, and no IEND.
        {
            int[][] s = Samples(16, 16, () => [rng.Next(256), rng.Next(256), rng.Next(256), rng.Next(256)]);
            var png = MakePng(16, 16, 8, 6, s, splitIdat: 3, extraChunk: "tEXt", omitIend: true);
            Program.Check(Same(Png.Decode(png), 16, (x, y) => { var v = s[y * 16 + x]; return RgbaImage.Argb(v[3], v[0], v[1], v[2]); }), "split IDAT, unknown chunk, no IEND");
            // Cut inside the image data: an error, never a half picture.
            Program.Check(Throws(() => Png.Decode(png.AsSpan(0, png.Length / 2).ToArray())), "a cut-off file is refused");
            Program.Check(Throws(() => Png.Decode(MakePng(4, 4, 4, 2, Samples(4, 4, () => [1, 2, 3])))), "colour type 2 with 4-bit samples is refused");
            Program.Check(Throws(() => Png.Decode([1, 2, 3])), "not a PNG");
        }
    }

    static int[][] Samples(int w, int h, Func<int[]> make) => Enumerable.Range(0, w * h).Select(_ => make()).ToArray();

    static bool Same(RgbaImage img, int w, Func<int, int, uint> expected)
    {
        if (img.Width != w) return false;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y] != expected(x, y)) return false;
        return true;
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (InvalidDataException) { return true; }
    }

    /// <summary>A PNG written sample by sample (filter 0 rows), independent of the encoder under test.</summary>
    static byte[] MakePng(int w, int h, int depth, int colorType, int[][] samples, byte[]? trns = null, byte[]? plte = null,
        int splitIdat = 1, string? extraChunk = null, bool omitIend = false)
    {
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int rowBytes = (w * channels * depth + 7) / 8;
        var raw = new byte[h * (rowBytes + 1)];
        for (int y = 0; y < h; y++)
        {
            int o = y * (rowBytes + 1) + 1;
            for (int x = 0; x < w; x++)
                for (int c = 0; c < channels; c++)
                {
                    int v = samples[y * w + x][c], bit = (x * channels + c) * depth;
                    if (depth == 16) { raw[o + bit / 8] = (byte)(v >> 8); raw[o + bit / 8 + 1] = (byte)v; }
                    else if (depth == 8) raw[o + bit / 8] = (byte)v;
                    else raw[o + bit / 8] |= (byte)(v << (8 - depth - bit % 8));
                }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw);
        var idat = z.ToArray();
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = (byte)depth; ihdr[9] = (byte)colorType;
        Chunk(ms, "IHDR", ihdr);
        if (extraChunk != null) Chunk(ms, extraChunk, "Comment\0made by McTests"u8.ToArray());
        if (plte != null) Chunk(ms, "PLTE", plte);
        if (trns != null) Chunk(ms, "tRNS", trns);
        int part = (idat.Length + splitIdat - 1) / splitIdat;
        for (int i = 0; i < idat.Length; i += part) Chunk(ms, "IDAT", idat.AsSpan(i, Math.Min(part, idat.Length - i)).ToArray());
        if (!omitIend) Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] body)
    {
        var head = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head, body.Length);
        System.Text.Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
        s.Write(head);
        s.Write(body);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in head.AsSpan(4).ToArray().Concat(body))
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        var tail = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(tail, ~crc);
        s.Write(tail);
    }

    // ------------------------------------------------------------------ game textures

    public static string? Jar(string version)
    {
        string j = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "versions", version, version + ".jar");
        return File.Exists(j) ? j : null;
    }

    public static string? NewestJar() => new[] { "26.1.2", "1.21.11", "1.21.1", "1.20.1" }.Select(Jar).FirstOrDefault(j => j != null);

    public static string BedrockData => @"C:\XboxGames\Minecraft for Windows\Content\data";

    static void GameTextures()
    {
        foreach (var jar in new[] { Jar("1.8.9"), NewestJar() }.Where(j => j != null).Distinct())
        {
            using var zip = ZipFile.OpenRead(jar!);
            int n = 0, bad = 0;
            foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                using var ms = new MemoryStream();
                using (var s = e.Open()) s.CopyTo(ms);
                try
                {
                    var img = Png.Decode(ms.ToArray());
                    if (!Png.Decode(Png.Encode(img)).SamePixels(img)) { bad++; Program.Info($"round trip differs: {e.FullName}"); }
                }
                catch (Exception ex) { bad++; Program.Info($"{e.FullName}: {ex.Message}"); }
                n++;
            }
            Program.Check(bad == 0 && n > 500, $"{Path.GetFileName(jar)}: {bad} of {n} PNGs failed");
            Program.Info($"{Path.GetFileName(jar)}: {n} PNGs read and written back identically");
        }

        string vanilla = Path.Combine(BedrockData, "resource_packs", "vanilla");
        if (!Directory.Exists(vanilla)) { Program.Info("Bedrock is not installed: archive and TGA checks skipped"); return; }
        var problems = new List<string>();
        var files = BrArchive.PackFiles(vanilla, problems);
        Program.Check(problems.Count == 0, "Bedrock archives read: " + string.Join("; ", problems));
        int png = 0, tga = 0, failed = 0;
        var tgaImages = new List<(string Name, RgbaImage Img, bool TopDown)>();
        foreach (var (path, read) in files.Where(f => f.Key.StartsWith("textures/blocks/") || f.Key.StartsWith("textures/items/") || f.Key.StartsWith("textures/entity/")))
        {
            try
            {
                if (path.EndsWith(".png")) { Png.Decode(read()); png++; }
                else if (path.EndsWith(".tga")) { var b = read(); tgaImages.Add((path, Tga.Decode(b), (b[17] & 0x20) != 0)); tga++; }
            }
            catch (Exception ex) { failed++; Program.Info($"{path}: {ex.Message}"); }
        }
        Program.Check(failed == 0 && png > 500 && tga > 10, $"Bedrock textures: {png} PNG, {tga} TGA, {failed} failed");
        Program.Info($"Bedrock vanilla pack: {files.Count} files, {png} PNG and {tga} TGA textures read");

        // TGA rows run bottom-up unless the header says otherwise. Bedrock and Java share most block art, so TGA
        // textures that match Java textures prove the orientation: compared by pixels, and by the shape of their
        // see-through parts (which survives Bedrock's different colouring of leaves and the like). Flipped copies must
        // not match more often.
        var newest = NewestJar();
        if (newest == null) return;
        var java = new HashSet<string>();
        var javaMasks = new HashSet<string>();
        using (var zip = ZipFile.OpenRead(newest))
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("assets/minecraft/textures/") && e.FullName.EndsWith(".png")))
            {
                using var ms = new MemoryStream();
                using (var s = e.Open()) s.CopyTo(ms);
                try
                {
                    var img = Png.Decode(ms.ToArray());
                    java.Add(img.Hash());
                    if (Mask(img) is { } m) javaMasks.Add(m);
                }
                catch (InvalidDataException) { }
            }
        static RgbaImage Flip(RgbaImage i) { var f = new RgbaImage(i.Width, i.Height); f.CopyFrom(i, 0, 0, i.Width, i.Height, 0, 0, flipY: true); return f; }
        int upright = 0, flipped = 0, maskUp = 0, maskFlip = 0;
        foreach (var t in tgaImages)
        {
            var f = Flip(t.Img);
            if (java.Contains(t.Img.Hash())) upright++;
            else if (!f.SamePixels(t.Img) && java.Contains(f.Hash())) flipped++;
            if (Mask(t.Img) is { } m && Mask(f) is { } mf && m != mf)
            {
                if (javaMasks.Contains(m)) maskUp++;
                else if (javaMasks.Contains(mf)) maskFlip++;
            }
        }
        int topDown = tgaImages.Count(t => t.TopDown);
        Program.Check(upright + maskUp > 5 && flipped + maskFlip == 0, $"TGA orientation: {upright} pixel and {maskUp} shape matches as read, {flipped} and {maskFlip} only when flipped");
        Program.Info($"TGA orientation: {upright} of {tgaImages.Count} TGA textures are pixel-identical to Java ones and {maskUp} more have the same see-through shape; flipped: {flipped} and {maskFlip} ({topDown} stored top-down, {tgaImages.Count - topDown} bottom-up)");
    }

    /// <summary>The shape of an image's see-through parts, or null when it has none (or nothing else).</summary>
    static string? Mask(RgbaImage img)
    {
        var bits = new System.Text.StringBuilder(img.Width * img.Height + 12).Append(img.Width).Append('x').Append(img.Height).Append(':');
        int clear = 0;
        foreach (var p in img.Pixels) { bool c = RgbaImage.A(p) < 128; if (c) clear++; bits.Append(c ? '0' : '1'); }
        return clear == 0 || clear == img.Pixels.Length ? null : bits.ToString();
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>
    /// The model's texels must wrap each box seamlessly: the strip right side → front → left side → back runs around
    /// the box, the top's last row meets the front's top row, the bottom's last row meets the front's bottom row, and so
    /// on. This checks every edge of every box of both models, plus that each texel lies on its face.
    /// </summary>
    static void Geometry()
    {
        int boxes = 0, seams = 0, bad = 0;
        foreach (var model in new[] { SkinModel.Classic, SkinModel.Slim })
            foreach (var box in SkinLayout.Boxes(model))
            {
                boxes++;
                var f = box.Faces().ToDictionary(x => x.Side);
                (float X, float Y, float Z)[] Q(FaceSide side, int i, int j) => SkinLayout.TexelQuad(f[side], i, j);
                bool Edge((float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c, (float X, float Y, float Z) d)
                {
                    seams++;
                    bool Near((float X, float Y, float Z) p, (float X, float Y, float Z) q) => Math.Abs(p.X - q.X) < 1e-4 && Math.Abs(p.Y - q.Y) < 1e-4 && Math.Abs(p.Z - q.Z) < 1e-4;
                    bool ok = Near(a, c) && Near(b, d) || Near(a, d) && Near(b, c);
                    if (!ok) bad++;
                    return ok;
                }
                int W = box.W, H = box.H, D = box.D;
                for (int j = 0; j < H; j++)
                {
                    // Corners: 0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left of the texel in the texture.
                    var r = Q(FaceSide.Right, D - 1, j); var fr = Q(FaceSide.Front, 0, j);
                    Edge(r[1], r[2], fr[0], fr[3]);
                    var fl = Q(FaceSide.Front, W - 1, j); var l = Q(FaceSide.Left, 0, j);
                    Edge(fl[1], fl[2], l[0], l[3]);
                    var ll = Q(FaceSide.Left, D - 1, j); var b = Q(FaceSide.Back, 0, j);
                    Edge(ll[1], ll[2], b[0], b[3]);
                    var bl = Q(FaceSide.Back, W - 1, j); var r0 = Q(FaceSide.Right, 0, j);
                    Edge(bl[1], bl[2], r0[0], r0[3]);
                }
                for (int i = 0; i < W; i++)
                {
                    var t = Q(FaceSide.Top, i, D - 1); var fr = Q(FaceSide.Front, i, 0);
                    Edge(t[3], t[2], fr[0], fr[1]);
                    var t0 = Q(FaceSide.Top, i, 0); var bk = Q(FaceSide.Back, W - 1 - i, 0);
                    Edge(t0[0], t0[1], bk[0], bk[1]);
                    var bo = Q(FaceSide.Bottom, i, D - 1); var fb = Q(FaceSide.Front, i, H - 1);
                    Edge(bo[3], bo[2], fb[3], fb[2]);
                    var b0 = Q(FaceSide.Bottom, i, 0); var bb = Q(FaceSide.Back, W - 1 - i, H - 1);
                    Edge(b0[0], b0[1], bb[3], bb[2]);
                }
                for (int j = 0; j < D; j++)
                {
                    var t = Q(FaceSide.Top, 0, j); var r = Q(FaceSide.Right, j, 0);
                    Edge(t[0], t[3], r[0], r[1]);
                    var tr = Q(FaceSide.Top, W - 1, j); var l = Q(FaceSide.Left, D - 1 - j, 0);
                    Edge(tr[1], tr[2], l[0], l[1]);
                    var bo = Q(FaceSide.Bottom, 0, j); var rb = Q(FaceSide.Right, j, H - 1);
                    Edge(bo[0], bo[3], rb[3], rb[2]);
                    var br = Q(FaceSide.Bottom, W - 1, j); var lb = Q(FaceSide.Left, D - 1 - j, H - 1);
                    Edge(br[1], br[2], lb[3], lb[2]);
                }
                // Every texel lies on its face's outer plane.
                foreach (var face in box.Faces())
                {
                    var n = SkinLayout.Normal(face.Side);
                    float g = box.Inflate;
                    float plane = n.X != 0 ? (n.X > 0 ? box.X + W + g : box.X - g) : n.Y != 0 ? (n.Y > 0 ? box.Y + H + g : box.Y - g) : (n.Z > 0 ? box.Z + D + g : box.Z - g);
                    for (int j = 0; j < face.H; j++)
                        for (int i = 0; i < face.W; i++)
                            foreach (var p in SkinLayout.TexelQuad(face, i, j))
                            {
                                float v = n.X != 0 ? p.X : n.Y != 0 ? p.Y : p.Z;
                                if (Math.Abs(v - plane) > 1e-4) { bad++; break; }
                            }
                }
            }
        Program.Check(bad == 0, $"skin model: {bad} broken seams or misplaced texels");
        Program.Info($"{boxes} boxes, {seams} texel edges where faces meet: all continuous");
        // Every pixel of a part's texture area belongs to exactly one face, and unused corners to none.
        int owned = 0;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                if (SkinLayout.FaceAt(SkinModel.Classic, x, y) != null) owned++;
        // The 12 boxes cover 3840 pixels; the two corners beside each top and bottom face (576 pixels) are unused.
        Program.Check(owned == 3264, $"classic layout: {owned} pixels belong to a face (3264 expected)");
    }

    // ------------------------------------------------------------------ legacy skins

    static RgbaImage Unique(int w, int h)
    {
        var img = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = RgbaImage.Argb(255, x * 3 & 255, y * 3 & 255, 77);
        return img;
    }

    static void Legacy()
    {
        // A legacy skin with a see-through pixel in the hat (so the hat rule does not apply).
        var legacy = Unique(64, 32);
        legacy[40, 2] = 0;
        var up = SkinConvert.LegacyToModern(legacy, out bool cleared);
        Program.Check(!cleared, "a hat with a see-through pixel is kept");
        bool top = true;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 64; x++)
                if (up[x, y] != legacy[x, y]) top = false;
        Program.Check(top, "the upper half is copied unchanged");
        // The left limbs are the right ones mirrored, side faces swapped: the game draws old skins that way.
        var boxes = SkinLayout.Boxes(SkinModel.Classic);
        int wrong = 0;
        foreach (var (left, right) in new[] { ("Left leg", "Right leg"), ("Left arm", "Right arm") })
        {
            var lb = boxes.First(b => b.Name == left);
            var rb = boxes.First(b => b.Name == right);
            foreach (var lf in lb.Faces())
            {
                var side = lf.Side switch { FaceSide.Left => FaceSide.Right, FaceSide.Right => FaceSide.Left, var s => s };
                var rf = rb.Faces().First(f => f.Side == side);
                for (int j = 0; j < lf.H; j++)
                    for (int i = 0; i < lf.W; i++)
                        if (up[lf.U + i, lf.V + j] != legacy[rf.U + rf.W - 1 - i, rf.V + j]) wrong++;
            }
        }
        Program.Check(wrong == 0, $"left limbs mirror the right ones ({wrong} texels wrong)");
        int stray = 0;
        for (int y = 32; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                bool limb = x >= 16 && x < 48 && y >= 48;
                bool face = SkinLayout.FaceAt(SkinModel.Classic, x, y) is { } h && !h.Face.Box.Overlay;
                uint want = limb ? (face ? up[x, y] : 0xFF000000u) : 0u;
                if (up[x, y] != want) stray++;
            }
        Program.Check(stray == 0, $"the new lower half holds only the mirrored limbs ({stray} stray pixels)");

        // An old skin without any see-through pixel: the hat was meant to be empty. Alpha goes, colour stays.
        var solid = Unique(64, 32);
        var s2 = SkinConvert.LegacyToModern(solid, out cleared);
        bool hatClear = true, armsKept = true;
        for (int y = 0; y < 32; y++)
            for (int x = 32; x < 64; x++)
            {
                if (y < 16 && s2[x, y] != (solid[x, y] & 0x00FFFFFFu)) hatClear = false;
                if (y >= 16 && s2[x, y] != solid[x, y]) armsKept = false;
            }
        Program.Check(cleared && hatClear && armsKept, "fully opaque old skin: hat alpha cleared, colours and arms kept");
        // Half see-through counts as see-through (below 128); 200 does not.
        var half = Unique(64, 32); half[50, 5] = RgbaImage.Argb(127, 1, 2, 3);
        SkinConvert.LegacyToModern(half, out cleared);
        Program.Check(!cleared, "alpha 127 in the hat area keeps the hat");
        var most = Unique(64, 32); most[50, 5] = RgbaImage.Argb(200, 1, 2, 3);
        SkinConvert.LegacyToModern(most, out cleared);
        Program.Check(cleared, "alpha 200 in the hat area still counts as opaque");

        // Loading: a 64x32 PNG becomes a 64x64 skin with a note.
        var loaded = Skin.Load(legacy.ToPng());
        Program.Check(loaded.Image.Width == 64 && loaded.Image.Height == 64 && loaded.Image.SamePixels(up) && loaded.LoadNotes.Any(n => n.Contains("64 × 32")), "a 64x32 file loads as the upgraded skin");
        Program.Check(Throws(() => Skin.Load(new RgbaImage(65, 65).ToPng())), "a 65x65 image is refused");

        // Back to 64x32: an upgraded skin goes back exactly, with nothing to report.
        var back = SkinConvert.ModernToLegacy(up, SkinModel.Classic, out var notes);
        bool same = true;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 64; x++)
                if (back[x, y] != up[x, y]) same = false;
        Program.Check(same && notes.Count == 0, $"64x64 → 64x32 → 64x64 is exact (notes: {string.Join(" | ", notes)})");
        // A jacket pixel is painted onto the body; a changed left arm is reported.
        var jacket = up.Clone();
        jacket[20, 36] = RgbaImage.Argb(255, 1, 2, 3); // the jacket's front face starts at (20, 36)
        jacket[36, 52] = RgbaImage.Argb(255, 4, 5, 6); // the left arm's front face
        back = SkinConvert.ModernToLegacy(jacket, SkinModel.Classic, out notes);
        Program.Check(back[20, 20] == RgbaImage.Argb(255, 1, 2, 3), "the jacket is painted onto the body's front");
        Program.Check(notes.Any(n => n.Contains("second layer")) && notes.Any(n => n.Contains("left arm")), "second layer and left limbs are reported");
        var slim = SkinConvert.ModernToLegacy(SkinConvert.ClassicToSlim(up), SkinModel.Slim, out notes);
        Program.Check(slim.Width == 64 && slim.Height == 32 && notes.Any(n => n.Contains("slim")), "slim skins become classic in 64x32");
    }

    // ------------------------------------------------------------------ arms

    static void Arms()
    {
        // A slim skin with every arm texel unique: slim → classic → slim gives it back exactly.
        var slim = new RgbaImage(64, 64);
        var rng = new Random(3);
        foreach (var box in SkinLayout.Boxes(SkinModel.Slim))
            foreach (var f in box.Faces())
                for (int j = 0; j < f.H; j++)
                    for (int i = 0; i < f.W; i++)
                        slim[f.U + i, f.V + j] = RgbaImage.Argb(box.Overlay ? rng.Next(256) : 255, rng.Next(256), rng.Next(256), rng.Next(256));
        Program.Check(Skin.DetectModel(slim) == SkinModel.Slim, "a slim layout is detected as slim");
        var classic = SkinConvert.SlimToClassic(slim);
        Program.Check(Skin.DetectModel(classic) == SkinModel.Classic, "widened arms are detected as classic");
        Program.Check(SkinConvert.ClassicToSlim(classic).SamePixels(slim), "slim → classic → slim is exact");
        // Widening doubles the middle column on every face that runs across the arm, the same column everywhere.
        var front = SkinLayout.Boxes(SkinModel.Classic).First(b => b.Name == "Right arm").Faces().First(f => f.Side == FaceSide.Front);
        var sFront = SkinLayout.Boxes(SkinModel.Slim).First(b => b.Name == "Right arm").Faces().First(f => f.Side == FaceSide.Front);
        bool widened = true;
        for (int j = 0; j < 12; j++)
        {
            int[] from = [0, 1, 1, 2];
            for (int i = 0; i < 4; i++) if (classic[front.U + i, front.V + j] != slim[sFront.U + from[i], sFront.V + j]) widened = false;
        }
        Program.Check(widened, "widening repeats the middle column");
        // Nothing outside the arms changes.
        var classicBoxes = SkinLayout.Boxes(SkinModel.Classic).Where(b => b.Part.EndsWith("arm")).Select(b => b.Bounds).ToList();
        bool rest = true;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                if (!classicBoxes.Any(b => x >= b.U && x < b.U + b.W && y >= b.V && y < b.V + b.H) && classic[x, y] != slim[x, y]) rest = false;
        Program.Check(rest, "only the arm areas change");
        // HD: the same at 128x128.
        var hd = slim.Scaled(128, 128);
        Program.Check(SkinConvert.ClassicToSlim(SkinConvert.SlimToClassic(hd)).SamePixels(hd), "128x128: slim → classic → slim is exact");
        Program.Check(SkinConvert.SlimToClassic(hd).SamePixels(classic.Scaled(128, 128)), "128x128 widening matches the 64x64 one scaled up");
    }

    // ------------------------------------------------------------------ default skins

    static void Defaults()
    {
        int checkedSkins = 0;
        foreach (var version in new[] { "1.8.9", "1.12.2", "1.16.4", "1.19.2", "1.19.3", "1.21.11", "26.1.2" })
        {
            var jar = Jar(version);
            if (jar == null) continue;
            using var zip = ZipFile.OpenRead(jar);
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("assets/minecraft/textures/entity/") && e.FullName.EndsWith(".png") &&
                     (e.FullName.Contains("/player/wide/") || e.FullName.Contains("/player/slim/") || e.FullName.EndsWith("/steve.png") || e.FullName.EndsWith("/alex.png"))))
            {
                using var ms = new MemoryStream();
                using (var s = e.Open()) s.CopyTo(ms);
                var skin = Skin.Load(ms.ToArray());
                bool slim = e.FullName.Contains("/slim/") || !e.FullName.Contains("/player/") && e.FullName.EndsWith("alex.png");
                Program.Check(skin.Model == (slim ? SkinModel.Slim : SkinModel.Classic), $"{version} {e.FullName}: detected {skin.Model}");
                Program.Check(skin.SeeThroughBasePixels() == 0, $"{version} {e.FullName}: {skin.SeeThroughBasePixels()} see-through base pixels");
                checkedSkins++;
            }
        }
        Program.Info($"{checkedSkins} Java default skins: arm model detected correctly, no holes");

        string pack = Path.Combine(BedrockData, "skin_packs", "vanilla");
        if (!File.Exists(Path.Combine(pack, "skins.json"))) { Program.Info("Bedrock is not installed: its default skins were skipped"); return; }
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack, "skins.json")));
        int n = 0;
        foreach (var s in doc.RootElement.GetProperty("skins").EnumerateArray())
        {
            string texture = s.GetProperty("texture").GetString()!, geometry = s.GetProperty("geometry").GetString()!;
            if (s.GetProperty("type").GetString() != "free") continue;
            var skin = Skin.Load(File.ReadAllBytes(Path.Combine(pack, texture)));
            var want = geometry.EndsWith("Slim", StringComparison.OrdinalIgnoreCase) ? SkinModel.Slim : SkinModel.Classic;
            Program.Check(skin.Model == want, $"Bedrock {texture}: {geometry} but detected {skin.Model}");
            n++;
        }
        Program.Info($"{n} Bedrock default skins: arm model matches skins.json");
    }
}
