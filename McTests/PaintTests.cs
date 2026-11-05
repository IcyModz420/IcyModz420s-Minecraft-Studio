// McTests/PaintTests.cs: Part of "skins": the painting tools and the skin's pixel map (faces and mirror images).
// Owns: PaintTests.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;

/// <summary>Part of "skins": the painting tools and the skin's pixel map (faces and mirror images).</summary>
static class PaintTests
{
    public static void Run()
    {
        Console.WriteLine("== Painting tools and the mirror map");
        foreach (var model in new[] { SkinModel.Classic, SkinModel.Slim })
            foreach (var size in new[] { 64, 128 })
            {
                var px = new SkinPixels(model, size);
                int faces = 0, missing = 0, notInvolution = 0, layerSwap = 0;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        if (px.At(x, y) is not { } p) continue;
                        faces++;
                        if (px.Mirror(x, y) is not { } m) { missing++; continue; }
                        if (px.Mirror(m.X, m.Y) != (x, y)) notInvolution++;
                        if (px.At(m.X, m.Y)!.Value.Overlay != p.Overlay) layerSwap++;
                    }
                int expected = 3264 * (size / 64) * (size / 64) - (model == SkinModel.Slim ? 4 * (2 * 12 + 2 * 4) * (size / 64) * (size / 64) : 0);
                Program.Check(faces == expected && missing == 0 && notInvolution == 0 && layerSwap == 0,
                    $"{model} {size}: {faces} face pixels (expected {expected}), {missing} without a mirror, {notInvolution} not mirrored back, {layerSwap} on the other layer");
            }
        // Concrete mirror pairs: the face's columns swap; the right arm's outer side is the left arm's outer side.
        var p64 = new SkinPixels(SkinModel.Classic, 64);
        Program.Check(p64.Mirror(8, 8) == (15, 8) && p64.Mirror(9, 10) == (14, 10), "head front: column 0 ↔ column 7");
        Program.Check(p64.Mirror(40, 20) == (43, 52) || p64.Mirror(40, 20) == (40 + 3, 52), $"right arm outer side ↔ left arm outer side ({p64.Mirror(40, 20)})");
        Program.Check(p64.Mirror(44, 20) == (39, 52), $"right arm front column 0 (outer edge) ↔ left arm front column 3 ({p64.Mirror(44, 20)})");
        var slim = new SkinPixels(SkinModel.Slim, 64);
        Program.Check(slim.Mirror(44, 20) == (38, 52), $"slim: right arm front column 0 ↔ left arm front column 2 ({slim.Mirror(44, 20)})");

        // Fill stays on its face and takes only the touching pixels of the same colour.
        var img = new RgbaImage(64, 64);
        img.Fill(0, 0, 64, 64, 0xFF336699);
        img[10, 10] = 0xFFFF0000;
        var changed = Paint.FloodFill(img, 9, 9, 0xFF00FF00, (x, y) => p64.SameFace(9, 9, x, y));
        Program.Check(changed.Count == 63 && img[10, 10] == 0xFFFF0000 && img[7, 9] == 0xFF336699 && img[16, 9] == 0xFF336699, $"fill stays on the head's front face ({changed.Count} pixels)");
        Program.Check(Paint.FloodFill(img, 9, 9, 0xFF00FF00).Count == 0, "filling with the same colour changes nothing");

        // Colour helpers.
        Program.Check(Paint.Shade(0xFF808080, -0.5) == 0xFF404040 && Paint.Shade(0x80000000, 0.5) == 0x80808080, "shade darkens and lightens, alpha kept");
        bool roundTrip = true;
        var rng = new Random(5);
        for (int i = 0; i < 2000; i++)
        {
            uint c = (uint)rng.Next() | 0xFF000000;
            var (h, s, v) = Paint.ToHsv(c);
            if (Paint.FromHsv(h, s, v) != c) roundTrip = false;
        }
        Program.Check(roundTrip, "RGB → HSV → RGB is exact");
        Program.Check(Paint.TryParseHex("#3a7", out var c3) && c3 == 0xFF33AA77 && Paint.TryParseHex("80112233", out var c8) && c8 == 0x80112233 && !Paint.TryParseHex("#12345", out _),
            "hex colours: #RGB, #AARRGGBB, bad lengths refused");
        Program.Check(Paint.Line(0, 0, 5, 2).Count() == 6 && Paint.Line(3, 3, 3, 3).Single() == (3, 3), "lines");
    }
}
