// MinecraftStudio.Core/Imaging/FloatingPixels.cs: pixels above a picture, not yet put down — a paste, an inserted
// picture, or a selection being moved.
// Owns: PixelTransform and FloatingPixels — where they are, flipping, turning and resizing. Resizing always starts from
// the pixels as they came (then flips and turns them again), so making them smaller and back loses nothing.
namespace MinecraftStudio.Core.Imaging;

public enum PixelTransform { FlipX, FlipY, RotateRight, RotateLeft }

/// <summary>Pixels above a picture that are not part of it yet: they can be moved, flipped, turned and resized.</summary>
public sealed class FloatingPixels
{
    readonly RgbaImage _source;
    bool _mirror;
    int _turns, _width, _height;
    bool _smooth;

    /// <param name="pixels">The pixels as they came.</param>
    /// <param name="x">Where their top-left pixel is, relative to the area (may be outside it).</param>
    /// <param name="liftedFrom">Where they were cut from (that place shows see-through while they float), or null for a paste.</param>
    public FloatingPixels(RgbaImage pixels, int x, int y, PixelRect? liftedFrom = null)
    {
        _source = pixels.Clone();
        _width = pixels.Width;
        _height = pixels.Height;
        Pixels = pixels.Clone();
        X = x;
        Y = y;
        LiftedFrom = liftedFrom;
    }

    public RgbaImage Pixels { get; private set; }
    /// <summary>The pixels as they came (before any resizing, flipping or turning).</summary>
    public RgbaImage Source => _source;
    public int X { get; set; }
    public int Y { get; set; }
    public PixelRect? LiftedFrom { get; }
    public PixelRect Bounds => new(X, Y, Pixels.Width, Pixels.Height);
    /// <summary>Whether the size is not the one they came with.</summary>
    public bool Resized => _width != _source.Width || _height != _source.Height;

    /// <summary>Flips or turns the pixels in place, around their middle.</summary>
    public void Transform(PixelTransform t)
    {
        // The orientation is kept as "mirror (optionally), then turn right n times"; each step folds into that.
        (_mirror, _turns) = t switch
        {
            PixelTransform.RotateRight => (_mirror, (_turns + 1) % 4),
            PixelTransform.RotateLeft => (_mirror, (_turns + 3) % 4),
            PixelTransform.FlipX => (!_mirror, (4 - _turns) % 4),
            _ => (!_mirror, (6 - _turns) % 4),
        };
        var old = Bounds;
        Rebuild();
        // Turning keeps the middle where it was.
        X = old.X + (old.Width - Pixels.Width) / 2;
        Y = old.Y + (old.Height - Pixels.Height) / 2;
    }

    /// <summary>Gives the pixels a new size (as shown, after turning), scaled from how they came.</summary>
    public void Resize(int width, int height, bool smooth)
    {
        width = Math.Clamp(width, 1, 4096);
        height = Math.Clamp(height, 1, 4096);
        (_width, _height) = _turns % 2 == 1 ? (height, width) : (width, height);
        _smooth = smooth;
        Rebuild();
    }

    void Rebuild()
    {
        var img = _width == _source.Width && _height == _source.Height ? _source.Clone() : Resample.Scale(_source, _width, _height, _smooth);
        if (_mirror) img = Resample.Flip(img, horizontal: true);
        for (int i = 0; i < _turns; i++) img = Resample.RotateRight(img);
        Pixels = img;
    }
}
