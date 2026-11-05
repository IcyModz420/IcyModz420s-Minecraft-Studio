// MinecraftStudio.Core/Imaging/PaintStroke.cs: one stroke of a paint tool, shared by every pixel editor.
// Owns: PaintSettings (brush size, fill tolerance and reach, filled shapes) and PaintStroke — what each tool does to the
// pixels of an area (a skin, one frame of a texture, a mob's texture), mirroring, staying inside the selection, and
// lines, rectangles and ellipses drawn live from where the stroke started (redrawn from the area as it was on each move).
using MinecraftStudio.Core.Skins;

namespace MinecraftStudio.Core.Imaging;

/// <summary>How the tools paint: brush width, how far a fill reaches, and whether shapes are filled.</summary>
public sealed class PaintSettings
{
    int _brush = 1, _tolerance;

    /// <summary>Brush width in pixels (1–16) for the pencil, eraser, shading, noise, dither and shape outlines.</summary>
    public int BrushSize { get => _brush; set => _brush = Math.Clamp(value, 1, 16); }
    /// <summary>How different a colour may be and still be filled: 0 only exactly the same colour, 100 any colour.</summary>
    public int FillTolerance { get => _tolerance; set => _tolerance = Math.Clamp(value, 0, 100); }
    /// <summary>Fill every pixel of that colour in the picture, not only the ones touching each other.</summary>
    public bool FillAll { get; set; }
    /// <summary>Rectangles and ellipses are filled with the colour, not only outlined.</summary>
    public bool FilledShapes { get; set; }
}

/// <summary>
/// One stroke of a paint tool on an area of a picture, from the button going down to it coming up. Coordinates are
/// relative to the area; pixels outside the area (or outside the selection, when there is one) are never changed.
/// </summary>
public sealed class PaintStroke
{
    const double ShadeStep = 0.12, NoiseAmount = 0.12;
    readonly RgbaImage _image;
    readonly PixelRect _area;
    readonly RgbaImage _before;
    readonly PaintTool _tool;
    readonly uint _color;
    readonly bool _secondary;
    readonly PaintSettings _settings;
    readonly Func<int, int, IEnumerable<(int X, int Y)>>? _mirror;
    readonly Func<int, int, Func<int, int, bool>?>? _fillRegion;
    readonly PixelRect? _limit;
    readonly Random _rng;
    readonly bool[] _visited;
    readonly (int X, int Y) _start;
    (int X, int Y)? _last;
    bool _filled;

    /// <param name="image">The picture painted on.</param>
    /// <param name="area">The part of it that is painted (one frame of a texture, or the whole picture).</param>
    /// <param name="x">Where the stroke starts (relative to the area).</param>
    /// <param name="mirror">The pixels mirroring a pixel (each painted pixel is painted there too), or null.</param>
    /// <param name="fillRegion">For a fill starting at a pixel: the pixels it may spread to (one face of a model), or null.</param>
    /// <param name="limit">The selection (relative to the area): nothing outside it changes; null for the whole area.</param>
    public PaintStroke(RgbaImage image, PixelRect area, PaintTool tool, uint color, bool secondary, PaintSettings settings, int x, int y,
        Func<int, int, IEnumerable<(int X, int Y)>>? mirror = null, Func<int, int, Func<int, int, bool>?>? fillRegion = null,
        PixelRect? limit = null, Random? rng = null)
    {
        _image = image;
        _area = area;
        _before = image.Crop(area.X, area.Y, area.Width, area.Height);
        _tool = tool;
        _color = tool == PaintTool.Eraser ? 0u : color;
        _secondary = secondary;
        _settings = settings;
        _mirror = mirror;
        _fillRegion = fillRegion;
        _limit = limit is { IsEmpty: false } l ? l : null;
        _rng = rng ?? new Random();
        _visited = new bool[area.Width * area.Height];
        _start = (x, y);
    }

    public PaintTool Tool => _tool;
    public bool IsShape => _tool is PaintTool.Line or PaintTool.Rectangle or PaintTool.Ellipse;
    /// <summary>Shapes: lines keep to steps of 45°, rectangles and ellipses stay square (Shift held).</summary>
    public bool Constrain { get; set; }
    /// <summary>Where the stroke started and where it is now (relative to the area).</summary>
    public (int X, int Y) Start => _start;
    public (int X, int Y) Current => _last ?? _start;

    /// <summary>
    /// Continues the stroke to a pixel; connect draws the line from the last pixel (a mouse drag on a flat view), otherwise
    /// only the pixel is painted (the 3D view, where neighbouring points can be on different faces). Shapes are redrawn
    /// from where the stroke started. Returns whether the picture changed.
    /// </summary>
    public bool To(int x, int y, bool connect)
    {
        if (_tool == PaintTool.Fill)
        {
            if (_filled) return false;
            _filled = true;
            _last = (x, y);
            bool any = FillFrom(x, y);
            foreach (var m in Mirrors(x, y)) any |= FillFrom(m.X, m.Y);
            return any;
        }
        if (IsShape)
        {
            if (_last == (x, y) && _last != null) return false;
            _last = (x, y);
            _image.CopyFrom(_before, 0, 0, _area.Width, _area.Height, _area.X, _area.Y);
            foreach (var (px, py) in ShapePixels(_start, Constrained(x, y))) Plot(px, py);
            return true;
        }
        var points = connect && _last is { } l ? Paint.Line(l.X, l.Y, x, y) : [(x, y)];
        bool changed = false;
        foreach (var (px, py) in points) changed |= Plot(px, py);
        _last = (x, y);
        return changed;
    }

    /// <summary>Whether the stroke changed any pixel of the area.</summary>
    public bool Changed()
    {
        for (int y = 0; y < _area.Height; y++)
        {
            int row = (_area.Y + y) * _image.Width + _area.X;
            if (!_image.Pixels.AsSpan(row, _area.Width).SequenceEqual(_before.Pixels.AsSpan(y * _area.Width, _area.Width))) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ pixels

    bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _area.Width && y < _area.Height && (_limit?.Contains(x, y) ?? true);

    IEnumerable<(int X, int Y)> Mirrors(int x, int y) => _mirror == null ? [] : _mirror(x, y).Where(m => m != (x, y));

    /// <summary>The brush at a pixel, and its mirror image.</summary>
    bool Plot(int x, int y)
    {
        int n = _settings.BrushSize, lo = -(n - 1) / 2;
        bool changed = false;
        for (int dy = lo; dy < lo + n; dy++)
            for (int dx = lo; dx < lo + n; dx++)
            {
                changed |= Apply(x + dx, y + dy);
                foreach (var m in Mirrors(x + dx, y + dy)) changed |= Apply(m.X, m.Y);
            }
        return changed;
    }

    bool Apply(int x, int y)
    {
        if (!Inside(x, y)) return false;
        int ix = _area.X + x, iy = _area.Y + y, v = y * _area.Width + x;
        uint old = _image[ix, iy];
        uint next = _tool switch
        {
            PaintTool.Shade => _visited[v] ? old : Paint.Shade(old, _secondary ? ShadeStep : -ShadeStep),
            PaintTool.Noise => _visited[v] || RgbaImage.A(old) == 0 ? old : Paint.Noise(old, _rng, NoiseAmount),
            PaintTool.Dither => ((x + y) & 1) == 0 ? _color : old,
            _ => _color,
        };
        _visited[v] = true;
        if (next == old) return false;
        _image[ix, iy] = next;
        return true;
    }

    // ------------------------------------------------------------------ fill

    bool FillFrom(int x, int y)
    {
        if (!Inside(x, y)) return false;
        var region = _fillRegion?.Invoke(x, y);
        uint target = _before[x, y];
        int tolerance = _settings.FillTolerance * 255 / 100;
        bool Takes(int px, int py) => Inside(px, py) && (region == null || region(px, py)) && Paint.Near(_before[px, py], target, tolerance);
        bool changed = false;
        void Set(int px, int py)
        {
            int ix = _area.X + px, iy = _area.Y + py;
            if (_image[ix, iy] == _color) return;
            _image[ix, iy] = _color;
            changed = true;
        }
        if (_settings.FillAll)
        {
            for (int py = 0; py < _area.Height; py++)
                for (int px = 0; px < _area.Width; px++)
                    if (Takes(px, py)) Set(px, py);
            return changed;
        }
        var seen = new bool[_area.Width * _area.Height];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        seen[y * _area.Width + x] = true;
        while (stack.Count > 0)
        {
            var (px, py) = stack.Pop();
            Set(px, py);
            foreach (var (nx, ny) in new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) })
            {
                if (nx < 0 || ny < 0 || nx >= _area.Width || ny >= _area.Height || seen[ny * _area.Width + nx]) continue;
                seen[ny * _area.Width + nx] = true;
                if (Takes(nx, ny)) stack.Push((nx, ny));
            }
        }
        return changed;
    }

    // ------------------------------------------------------------------ shapes

    (int X, int Y) Constrained(int x, int y)
    {
        if (!Constrain) return (x, y);
        int dx = x - _start.X, dy = y - _start.Y, ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (_tool == PaintTool.Line)
        {
            if (ax > 2 * ay) return (x, _start.Y);
            if (ay > 2 * ax) return (_start.X, y);
        }
        int side = Math.Max(ax, ay);
        return (_start.X + Math.Sign(dx == 0 ? 1 : dx) * side, _start.Y + Math.Sign(dy == 0 ? 1 : dy) * side);
    }

    IEnumerable<(int X, int Y)> ShapePixels((int X, int Y) a, (int X, int Y) b) => _tool switch
    {
        PaintTool.Line => Paint.Line(a.X, a.Y, b.X, b.Y),
        PaintTool.Rectangle => Paint.Rectangle(PixelRect.FromCorners(a.X, a.Y, b.X, b.Y), _settings.FilledShapes),
        _ => Paint.Ellipse(PixelRect.FromCorners(a.X, a.Y, b.X, b.Y), _settings.FilledShapes),
    };
}
