// MinecraftStudio.Core/Imaging/PixelRect.cs: a rectangle of pixels (a selection, a frame of a texture, a pasted picture).
// Owns: PixelRect — made from two corners in any order, clipped to an area, tested for containing a pixel.
namespace MinecraftStudio.Core.Imaging;

/// <summary>A rectangle of pixels: the top-left pixel and the size (an empty one has no width or height).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    /// <summary>The rectangle from one pixel to another, both included, whichever way round they are.</summary>
    public static PixelRect FromCorners(int x0, int y0, int x1, int y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);

    /// <summary>The part inside the other rectangle (empty when they do not meet).</summary>
    public PixelRect Intersect(PixelRect other)
    {
        int x0 = Math.Max(X, other.X), y0 = Math.Max(Y, other.Y), x1 = Math.Min(Right, other.Right), y1 = Math.Min(Bottom, other.Bottom);
        return x1 <= x0 || y1 <= y0 ? new PixelRect(x0, y0, 0, 0) : new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public override string ToString() => $"{Width} × {Height} at {X}, {Y}";
}
