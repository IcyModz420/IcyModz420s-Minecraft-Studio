// MinecraftStudio/Studio/SkinTextureView.cs: The skin as a flat picture: every face outlined in its body part's
// colour (dashed for the second layer), pixels the game never uses dimmed, a pixel grid when zoomed in.
// Owns: SkinTextureView — what PixelCanvas shows and does (painting, selections, floating pastes), for the whole skin.
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// The skin as a flat picture: every face outlined in its body part's colour (dashed for the second layer), pixels the
/// game never uses dimmed, a pixel grid when zoomed in. Left button paints with the first colour, right button with the
/// second; the wheel zooms at the mouse, middle-drag moves, double-click fits.
/// </summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed class SkinTextureView : PixelCanvas
{
    readonly SkinEditor _editor;
    Bitmap? _unused;

    static readonly Dictionary<string, Color> PartColors = new()
    {
        ["Head"] = Color.FromArgb(240, 200, 80),
        ["Body"] = Color.FromArgb(110, 200, 120),
        ["Right arm"] = Color.FromArgb(110, 170, 250),
        ["Left arm"] = Color.FromArgb(80, 210, 220),
        ["Right leg"] = Color.FromArgb(240, 150, 80),
        ["Left leg"] = Color.FromArgb(200, 140, 230),
    };

    public static Color PartColor(string part) => PartColors.TryGetValue(part, out var c) ? c : C.FG_DIM;

    public SkinTextureView(SkinEditor editor) : base(editor)
    {
        _editor = editor;
        Zoom = 8;
        editor.LayoutChanged += OnLayoutChanged;
        AccessibleName = "Skin texture";
        AccessibleDescription = "The skin as a flat picture. Left button paints with the first colour, right button with the second.";
    }

    void OnLayoutChanged()
    {
        _unused?.Dispose();
        _unused = null;
        Fit();
        MarkStale();
    }

    int Size_ => _editor.Skin.Image.Width;

    protected override (RgbaImage Image, PixelRect Area)? View => (_editor.Skin.Image, new PixelRect(0, 0, Size_, Size_));
    protected override float MaxZoom => 96f;
    // Every pixel when zoomed in enough, otherwise every skin texel of an HD skin.
    protected override float GridStep => Zoom >= 8 ? Zoom : Zoom * _editor.Skin.Scale >= 8 ? Zoom * _editor.Skin.Scale : 0;

    Bitmap UnusedMask()
    {
        int size = Size_;
        var mask = new RgbaImage(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (_editor.Pixels.At(x, y) == null) mask[x, y] = 0xB0141414u;
        return PixelClipboard.ToBitmap(mask);
    }

    protected override void DrawOverlay(Graphics g, RectangleF area)
    {
        _unused ??= UnusedMask();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_unused, area);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        // Face outlines in their part's colour; the second layer dashed.
        int s = _editor.Skin.Scale;
        foreach (var box in SkinLayout.Boxes(_editor.Skin.Model))
        {
            var color = PartColor(box.Part);
            bool hidden = _editor.HiddenParts.Contains(box.Part);
            using var pen = new Pen(Color.FromArgb(hidden ? 50 : 150, color), 1) { DashStyle = box.Overlay ? DashStyle.Dash : DashStyle.Solid };
            foreach (var f in box.Faces())
                g.DrawRectangle(pen, area.Left + f.U * s * Zoom, area.Top + f.V * s * Zoom, f.W * s * Zoom - 1, f.H * s * Zoom - 1);
        }
    }

    protected override PixelRect? FaceAt(int x, int y)
    {
        if (_editor.Pixels.At(x, y) is not { } p) return null;
        int s = _editor.Skin.Scale;
        return new PixelRect(p.Face.U * s, p.Face.V * s, p.Face.W * s, p.Face.H * s);
    }

    protected override IEnumerable<(int X, int Y)> MirrorMarks((int X, int Y) p)
    {
        if (_editor.Mirror && _editor.Pixels.Mirror(p.X, p.Y) is { } m && m != p) yield return m;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.LayoutChanged -= OnLayoutChanged;
            _unused?.Dispose();
        }
        base.Dispose(disposing);
    }
}
