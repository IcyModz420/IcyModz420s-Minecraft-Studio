// MinecraftStudio.Core/Skins/SkinLayout.cs: Arm width of a player model: Classic (Steve, 4 pixels) or Slim
// (Alex, 3 pixels).
// Owns: SkinModel, FaceSide, SkinFace, SkinBox, SkinLayout.
namespace MinecraftStudio.Core.Skins;

/// <summary>Arm width of a player model: Classic (Steve, 4 pixels) or Slim (Alex, 3 pixels).</summary>
public enum SkinModel { Classic, Slim }

public enum FaceSide { Top, Bottom, Right, Front, Left, Back }

/// <summary>One face of a body part: its rectangle in the skin (at 64×64 scale) and the side of the box it covers.</summary>
public sealed record SkinFace(SkinBox Box, FaceSide Side, int U, int V, int W, int H);

/// <summary>
/// A body part of the player model: where its texture starts (U, V) at 64×64 scale, its size in pixels and where the
/// box sits in model space (pixels; origin between the feet, y up, the player facing +z). The overlay ("second
/// layer": hat, jacket, sleeves, trousers) is the same box drawn slightly larger.
/// </summary>
public sealed record SkinBox(string Part, string Name, bool Overlay, int U, int V, int W, int H, int D, float X, float Y, float Z, float Inflate)
{
    public IEnumerable<SkinFace> Faces()
    {
        yield return new SkinFace(this, FaceSide.Top, U + D, V, W, D);
        yield return new SkinFace(this, FaceSide.Bottom, U + D + W, V, W, D);
        yield return new SkinFace(this, FaceSide.Right, U, V + D, D, H);
        yield return new SkinFace(this, FaceSide.Front, U + D, V + D, W, H);
        yield return new SkinFace(this, FaceSide.Left, U + D + W, V + D, D, H);
        yield return new SkinFace(this, FaceSide.Back, U + D + W + D, V + D, W, H);
    }

    /// <summary>The texture rectangle the whole box uses.</summary>
    public (int U, int V, int W, int H) Bounds => (U, V, 2 * (D + W), D + H);
}

/// <summary>
/// The player model of the 64×64 skin layout (Java 1.8+, Bedrock, every console from TU31): six parts, each with a base
/// box and an overlay box. Texture mapping matches the game's model boxes, so painting on the 3D view lands on the
/// right pixel of the skin.
/// </summary>
public static class SkinLayout
{
    public static readonly string[] Parts = ["Head", "Body", "Right arm", "Left arm", "Right leg", "Left leg"];

    public static IReadOnlyList<SkinBox> Boxes(SkinModel model)
    {
        int arm = model == SkinModel.Slim ? 3 : 4;
        // Slim arms hang half a pixel lower than classic ones in the game's model.
        float armY = model == SkinModel.Slim ? 11.5f : 12;
        return
        [
            new("Head", "Head", false, 0, 0, 8, 8, 8, -4, 24, -4, 0),
            new("Head", "Hat", true, 32, 0, 8, 8, 8, -4, 24, -4, 0.5f),
            new("Body", "Body", false, 16, 16, 8, 12, 4, -4, 12, -2, 0),
            new("Body", "Jacket", true, 16, 32, 8, 12, 4, -4, 12, -2, 0.25f),
            new("Right arm", "Right arm", false, 40, 16, arm, 12, 4, -4 - arm, armY, -2, 0),
            new("Right arm", "Right sleeve", true, 40, 32, arm, 12, 4, -4 - arm, armY, -2, 0.25f),
            new("Left arm", "Left arm", false, 32, 48, arm, 12, 4, 4, armY, -2, 0),
            new("Left arm", "Left sleeve", true, 48, 48, arm, 12, 4, 4, armY, -2, 0.25f),
            new("Right leg", "Right leg", false, 0, 16, 4, 12, 4, -4, 0, -2, 0),
            new("Right leg", "Right trouser leg", true, 0, 32, 4, 12, 4, -4, 0, -2, 0.25f),
            new("Left leg", "Left leg", false, 16, 48, 4, 12, 4, 0, 0, -2, 0),
            new("Left leg", "Left trouser leg", true, 0, 48, 4, 12, 4, 0, 0, -2, 0.25f),
        ];
    }

    /// <summary>The face (and texel within it) a skin pixel belongs to, or null for unused pixels.</summary>
    public static (SkinFace Face, int I, int J)? FaceAt(SkinModel model, int x, int y)
    {
        foreach (var box in Boxes(model))
            foreach (var f in box.Faces())
                if (x >= f.U && x < f.U + f.W && y >= f.V && y < f.V + f.H) return (f, x - f.U, y - f.V);
        return null;
    }

    /// <summary>
    /// The 3D corners of texel (i, j) of a face, in model space: column i and row j of the face's rectangle. Overlay
    /// boxes are inflated on every side, so their texels are slightly larger.
    /// </summary>
    public static (float X, float Y, float Z)[] TexelQuad(SkinFace f, int i, int j)
    {
        var b = f.Box;
        float g = b.Inflate;
        float x0 = b.X - g, x1 = b.X + b.W + g, y0 = b.Y - g, y1 = b.Y + b.H + g, z0 = b.Z - g, z1 = b.Z + b.D + g;
        float sx = (x1 - x0) / b.W, sy = (y1 - y0) / b.H, sz = (z1 - z0) / b.D;
        // Column and row ranges along the face's own axes.
        (float A, float B) Cols(float lo, float step, bool reverse, int count) =>
            reverse ? (lo + (count - i - 1) * step, lo + (count - i) * step) : (lo + i * step, lo + (i + 1) * step);
        float yt = y1 - j * sy, yb = y1 - (j + 1) * sy;
        switch (f.Side)
        {
            case FaceSide.Front:
                {
                    var (a, c) = Cols(x0, sx, false, b.W);
                    return [(a, yt, z1), (c, yt, z1), (c, yb, z1), (a, yb, z1)];
                }
            case FaceSide.Back:
                {
                    var (a, c) = Cols(x0, sx, true, b.W);
                    return [(c, yt, z0), (a, yt, z0), (a, yb, z0), (c, yb, z0)];
                }
            case FaceSide.Right:
                {
                    var (a, c) = Cols(z0, sz, false, b.D);
                    return [(x0, yt, a), (x0, yt, c), (x0, yb, c), (x0, yb, a)];
                }
            case FaceSide.Left:
                {
                    var (a, c) = Cols(z0, sz, true, b.D);
                    return [(x1, yt, c), (x1, yt, a), (x1, yb, a), (x1, yb, c)];
                }
            case FaceSide.Top:
                {
                    var (a, c) = Cols(x0, sx, false, b.W);
                    float za = z0 + j * sz, zb = z0 + (j + 1) * sz;
                    return [(a, y1, za), (c, y1, za), (c, y1, zb), (a, y1, zb)];
                }
            default:
                {
                    var (a, c) = Cols(x0, sx, false, b.W);
                    float za = z0 + j * sz, zb = z0 + (j + 1) * sz;
                    return [(a, y0, za), (c, y0, za), (c, y0, zb), (a, y0, zb)];
                }
        }
    }

    /// <summary>The outward normal of a face side (for shading and back-face culling).</summary>
    public static (float X, float Y, float Z) Normal(FaceSide side) => side switch
    {
        FaceSide.Top => (0, 1, 0),
        FaceSide.Bottom => (0, -1, 0),
        FaceSide.Right => (-1, 0, 0),
        FaceSide.Left => (1, 0, 0),
        FaceSide.Front => (0, 0, 1),
        _ => (0, 0, -1),
    };
}
