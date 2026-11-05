// MinecraftStudio.Core/Skins/SkinPixels.cs: Where one pixel of a skin image sits on the model.
// Owns: SkinPixel, SkinPixels.
namespace MinecraftStudio.Core.Skins;

/// <summary>Where one pixel of a skin image sits on the model.</summary>
public readonly record struct SkinPixel(SkinFace Face, int I, int J)
{
    public bool Overlay => Face.Box.Overlay;
    public string Part => Face.Box.Part;
}

/// <summary>
/// Pixel-level facts about a skin layout at a given resolution: which face each image pixel belongs to, and which pixel
/// is its mirror image across the body's centre (right arm ↔ left arm and so on), found from the 3D model so it holds
/// for every face, both layers and both arm widths.
/// </summary>
public sealed class SkinPixels
{
    readonly SkinPixel?[] _at;
    readonly int[] _mirror;

    public SkinModel Model { get; }
    public int Size { get; }
    public int Scale => Size / 64;

    public SkinPixels(SkinModel model, int size)
    {
        Model = model;
        Size = size;
        int s = size / 64;
        _at = new SkinPixel?[size * size];
        var faces = SkinLayout.Boxes(model).SelectMany(b => b.Faces()).ToList();
        foreach (var f in faces)
            for (int j = 0; j < f.H * s; j++)
                for (int i = 0; i < f.W * s; i++)
                    _at[(f.V * s + j) * size + f.U * s + i] = new SkinPixel(f, i, j);

        // Mirror: the pixel whose centre is this one's with x negated, on a face that points the mirrored way.
        var byCentre = new Dictionary<(int, int, int, int, bool), int>();
        for (int p = 0; p < _at.Length; p++)
            if (_at[p] is { } px) byCentre[Key(Centre(px, s), SkinLayout.Normal(px.Face.Side), px.Overlay, false)] = p;
        _mirror = new int[_at.Length];
        for (int p = 0; p < _at.Length; p++)
        {
            _mirror[p] = -1;
            if (_at[p] is { } px && byCentre.TryGetValue(Key(Centre(px, s), SkinLayout.Normal(px.Face.Side), px.Overlay, true), out int m)) _mirror[p] = m;
        }
    }

    static (float X, float Y, float Z) Centre(SkinPixel px, int s)
    {
        // The pixel's centre inside its skin texel, from the texel's corners.
        var q = SkinLayout.TexelQuad(px.Face, px.I / s, px.J / s);
        float fx = (px.I % s + 0.5f) / s, fy = (px.J % s + 0.5f) / s;
        // Bilinear between corners 0 (top-left), 1 (top-right) and 3 (bottom-left).
        return (q[0].X + (q[1].X - q[0].X) * fx + (q[3].X - q[0].X) * fy,
                q[0].Y + (q[1].Y - q[0].Y) * fx + (q[3].Y - q[0].Y) * fy,
                q[0].Z + (q[1].Z - q[0].Z) * fx + (q[3].Z - q[0].Z) * fy);
    }

    static (int, int, int, int, bool) Key((float X, float Y, float Z) c, (float X, float Y, float Z) n, bool overlay, bool mirrored)
    {
        float x = mirrored ? -c.X : c.X;
        int nx = (int)(mirrored ? -n.X : n.X), ny = (int)n.Y, nz = (int)n.Z;
        int dir = nx != 0 ? (nx > 0 ? 1 : 2) : ny != 0 ? (ny > 0 ? 3 : 4) : nz > 0 ? 5 : 6;
        return ((int)Math.Round(x * 64), (int)Math.Round(c.Y * 64), (int)Math.Round(c.Z * 64), dir, overlay);
    }

    /// <summary>The face pixel at image position (x, y), or null for pixels the model does not use.</summary>
    public SkinPixel? At(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size ? _at[y * Size + x] : null;

    /// <summary>The mirror image of image position (x, y), or null.</summary>
    public (int X, int Y)? Mirror(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return null;
        int m = _mirror[y * Size + x];
        return m < 0 ? null : (m % Size, m / Size);
    }

    /// <summary>Whether two image positions are on the same face (fills stay on one face).</summary>
    public bool SameFace(int x0, int y0, int x1, int y1) => At(x0, y0) is { } a && At(x1, y1) is { } b && a.Face == b.Face;
}
