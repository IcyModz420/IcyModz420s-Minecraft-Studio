// MinecraftStudio.Core/Skins/SkinConvert.cs: Skin layout conversions: old 64×32 ↔ 64×64, classic ↔ slim arms.
// Owns: SkinConvert.
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Skins;

/// <summary>Skin layout conversions: old 64×32 ↔ 64×64, classic ↔ slim arms.</summary>
public static class SkinConvert
{
    /// <summary>
    /// Java Edition's own upgrade of an old 64×32 skin (it does this every time it loads one): the right arm and leg are
    /// copied, mirrored, to the left ones, the base layer is made opaque and a hat layer without any see-through pixel
    /// is treated as empty. <paramref name="hatCleared"/> tells whether that last rule applied.
    /// </summary>
    public static RgbaImage LegacyToModern(RgbaImage legacy, out bool hatCleared)
    {
        int s = legacy.Width / 64;
        var img = new RgbaImage(64 * s, 64 * s);
        img.CopyFrom(legacy, 0, 0, 64 * s, 32 * s, 0, 0);
        void Copy(int sx, int sy, int dx, int dy, int w, int h) => img.CopyFrom(img, sx * s, sy * s, w * s, h * s, (sx + dx) * s, (sy + dy) * s, flipX: true);
        // Leg: top, bottom, then the four sides (the right and left sides swap places).
        Copy(4, 16, 16, 32, 4, 4); Copy(8, 16, 16, 32, 4, 4);
        Copy(0, 20, 24, 32, 4, 12); Copy(4, 20, 16, 32, 4, 12); Copy(8, 20, 8, 32, 4, 12); Copy(12, 20, 16, 32, 4, 12);
        // Arm.
        Copy(44, 16, -8, 32, 4, 4); Copy(48, 16, -8, 32, 4, 4);
        Copy(40, 20, 0, 32, 4, 12); Copy(44, 20, -8, 32, 4, 12); Copy(48, 20, -16, 32, 4, 12); Copy(52, 20, -8, 32, 4, 12);
        Opaque(img, 0, 0, 32, 16, s);
        hatCleared = NotchHack(img, 32, 0, 64, 32, s);
        Opaque(img, 0, 16, 64, 32, s);
        Opaque(img, 16, 48, 48, 64, s);
        return img;
    }

    public static RgbaImage LegacyToModern(RgbaImage legacy) => LegacyToModern(legacy, out _);

    /// <summary>The base layer of a modern skin as the game shows it: every base pixel opaque.</summary>
    public static void MakeBaseOpaque(RgbaImage img)
    {
        int s = img.Width / 64;
        Opaque(img, 0, 0, 32, 16, s);
        Opaque(img, 0, 16, 64, 32, s);
        Opaque(img, 16, 48, 48, 64, s);
    }

    static void Opaque(RgbaImage img, int x0, int y0, int x1, int y1, int s)
    {
        for (int y = y0 * s; y < y1 * s; y++)
            for (int x = x0 * s; x < x1 * s; x++)
                img[x, y] |= 0xFF000000u;
    }

    /// <summary>
    /// Skins drawn before the hat layer existed often filled it with a solid colour. If no pixel in the upper right
    /// quarter is even half see-through, the game clears the alpha there (the colours stay, so the base parts in that
    /// area come back when they are made opaque again).
    /// </summary>
    static bool NotchHack(RgbaImage img, int x0, int y0, int x1, int y1, int s)
    {
        for (int y = y0 * s; y < y1 * s; y++)
            for (int x = x0 * s; x < x1 * s; x++)
                if (RgbaImage.A(img[x, y]) < 128) return false;
        for (int y = y0 * s; y < y1 * s; y++)
            for (int x = x0 * s; x < x1 * s; x++)
                img[x, y] &= 0x00FFFFFFu;
        return true;
    }

    /// <summary>
    /// A 64×32 skin for old versions and consoles: slim arms become classic ones, and the second layer of the body,
    /// arms and legs is painted onto the base so the look is kept as far as possible. The left limbs cannot differ from
    /// the right ones in that layout; <paramref name="notes"/> says what could not be kept.
    /// </summary>
    public static RgbaImage ModernToLegacy(RgbaImage modern, SkinModel model, out List<string> notes)
    {
        notes = [];
        int s = modern.Width / 64;
        var flat = model == SkinModel.Slim ? SlimToClassic(modern) : modern.Clone();
        var boxes = SkinLayout.Boxes(SkinModel.Classic);
        bool secondLayer = false;
        foreach (var under in boxes.Where(b => !b.Overlay && b.Part != "Head"))
        {
            var over = boxes.First(b => b.Overlay && b.Part == under.Part);
            secondLayer |= Composite(flat, over, under, s);
        }
        var legacy = flat.Crop(0, 0, 64 * s, 32 * s);
        var upgraded = LegacyToModern(legacy);
        bool sameLimbs = boxes.Where(b => !b.Overlay && b.Part.StartsWith("Left")).All(b => SameTexels(upgraded, flat, b, s));
        if (!sameLimbs)
            notes.Add("The left arm and leg differ from the right ones. A 64 × 32 skin only has the right ones, and the game mirrors them onto the left.");
        if (secondLayer)
            notes.Add("The second layer of the body, arms and legs was painted onto the base layer. A 64 × 32 skin only keeps the hat layer.");
        if (model == SkinModel.Slim)
            notes.Add("The slim arms were widened to classic ones. Skins in the 64 × 32 layout only have classic arms.");
        return legacy;
    }

    /// <summary>Draws an overlay box's texels over the matching base texels; true if the overlay had anything on it.</summary>
    static bool Composite(RgbaImage img, SkinBox over, SkinBox under, int s)
    {
        var uf = under.Faces().ToList();
        bool any = false;
        int k = 0;
        foreach (var of in over.Faces())
        {
            var bf = uf[k++];
            for (int j = 0; j < of.H * s; j++)
                for (int i = 0; i < of.W * s; i++)
                {
                    uint top = img[of.U * s + i, of.V * s + j];
                    if (RgbaImage.A(top) == 0) continue;
                    any = true;
                    img[bf.U * s + i, bf.V * s + j] = Blend(img[bf.U * s + i, bf.V * s + j], top);
                }
        }
        return any;
    }

    /// <summary>Alpha compositing of one colour over another.</summary>
    public static uint Blend(uint under, uint over)
    {
        int a = RgbaImage.A(over);
        if (a == 255) return over;
        if (a == 0) return under;
        int ua = RgbaImage.A(under);
        int oa = a + ua * (255 - a) / 255;
        if (oa == 0) return 0;
        int Mix(int o, int u) => (o * a + u * ua * (255 - a) / 255) / oa;
        return RgbaImage.Argb(oa, Mix(RgbaImage.R(over), RgbaImage.R(under)), Mix(RgbaImage.G(over), RgbaImage.G(under)), Mix(RgbaImage.B(over), RgbaImage.B(under)));
    }

    /// <summary>Whether a box's face texels look the same in both images (the base layer is drawn opaque, so alpha is ignored).</summary>
    static bool SameTexels(RgbaImage a, RgbaImage b, SkinBox box, int s)
    {
        foreach (var f in box.Faces())
            for (int y = f.V * s; y < (f.V + f.H) * s; y++)
                for (int x = f.U * s; x < (f.U + f.W) * s; x++)
                    if ((a[x, y] | 0xFF000000u) != (b[x, y] | 0xFF000000u)) return false;
        return true;
    }

    // ------------------------------------------------------------------ arm width

    public static RgbaImage ClassicToSlim(RgbaImage img) => ChangeArms(img, SkinModel.Classic, SkinModel.Slim);
    public static RgbaImage SlimToClassic(RgbaImage img) => ChangeArms(img, SkinModel.Slim, SkinModel.Classic);

    /// <summary>
    /// Re-lays the four arm boxes (both arms, both layers) for the other arm width. Going slim, each arm loses one of
    /// its two middle columns (the one that changes the look least) on every face that runs across the arm, so the
    /// edges where faces meet stay intact; going classic, the middle column is doubled. The side faces keep their size.
    /// Slim → classic → slim gives back the original pixels.
    /// </summary>
    static RgbaImage ChangeArms(RgbaImage img, SkinModel from, SkinModel to)
    {
        int s = img.Width / 64;
        var result = img.Clone();
        var fromBoxes = SkinLayout.Boxes(from).Where(b => b.Part.EndsWith("arm")).ToList();
        var toBoxes = SkinLayout.Boxes(to).Where(b => b.Part.EndsWith("arm")).ToList();
        for (int n = 0; n < fromBoxes.Count; n++)
        {
            var fb = fromBoxes[n];
            var tb = toBoxes[n];
            var ff = fb.Faces().ToList();
            var tf = tb.Faces().ToList();
            var faces = ff.Select(f => img.Crop(f.U * s, f.V * s, f.W * s, f.H * s)).ToList();
            // The column across the arm (counted from the arm's right-hand edge, as on the front face) to drop.
            int drop = fb.W > tb.W ? ColumnToDrop(ff, faces, s) : 0;
            var (bu, bv, bw, bh) = fb.Bounds;
            result.Fill(bu * s, bv * s, bw * s, bh * s, 0);
            for (int k = 0; k < 6; k++)
            {
                var face = faces[k];
                if (ff[k].W != tf[k].W)
                {
                    // The back face runs the other way across the arm.
                    bool reversed = ff[k].Side == FaceSide.Back;
                    face = fb.W > tb.W
                        ? WithoutColumn(face, reversed ? fb.W - 1 - drop : drop, s)
                        : WithDoubledColumn(face, reversed ? fb.W - 1 - fb.W / 2 : fb.W / 2, s);
                }
                result.CopyFrom(face, 0, 0, face.Width, face.Height, tf[k].U * s, tf[k].V * s);
            }
        }
        return result;
    }

    /// <summary>Column 1 or 2 of a 4-wide arm: the one most like its neighbours across the top, bottom, front and back.</summary>
    static int ColumnToDrop(List<SkinFace> faces, List<RgbaImage> pixels, int s)
    {
        long Cost(int c)
        {
            long d = 0;
            for (int k = 0; k < faces.Count; k++)
            {
                if (faces[k].Side is FaceSide.Left or FaceSide.Right) continue;
                var p = pixels[k];
                int col = faces[k].Side == FaceSide.Back ? faces[k].W - 1 - c : c;
                for (int y = 0; y < p.Height; y++)
                    for (int x = 0; x < s; x++)
                    {
                        uint v = p[col * s + x, y];
                        d += Diff(v, p[(col - 1) * s + x, y]) + Diff(v, p[(col + 1) * s + x, y]);
                    }
            }
            return d;
        }
        return Cost(2) < Cost(1) ? 2 : 1;
    }

    static RgbaImage WithoutColumn(RgbaImage face, int column, int s)
    {
        var r = new RgbaImage(face.Width - s, face.Height);
        r.CopyFrom(face, 0, 0, column * s, face.Height, 0, 0);
        r.CopyFrom(face, (column + 1) * s, 0, face.Width - (column + 1) * s, face.Height, column * s, 0);
        return r;
    }

    static RgbaImage WithDoubledColumn(RgbaImage face, int column, int s)
    {
        var r = new RgbaImage(face.Width + s, face.Height);
        r.CopyFrom(face, 0, 0, (column + 1) * s, face.Height, 0, 0);
        r.CopyFrom(face, column * s, 0, face.Width - column * s, face.Height, (column + 1) * s, 0);
        return r;
    }

    static int Diff(uint a, uint b) =>
        Math.Abs(RgbaImage.A(a) - RgbaImage.A(b)) + Math.Abs(RgbaImage.R(a) - RgbaImage.R(b)) + Math.Abs(RgbaImage.G(a) - RgbaImage.G(b)) + Math.Abs(RgbaImage.B(a) - RgbaImage.B(b));
}
