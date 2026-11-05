// MinecraftStudio/Studio/PixelCanvas.cs: the flat picture of every pixel editor (a texture's frame, a skin, a mob's
// texture), large and zoomable, with the selection and anything floating shown over it.
// Owns: PixelCanvas — zoom, pan and fit, the cached bitmap, checkerboard, pixel grid, hover and mirror marks, the
// selection's marching ants, the floating pixels' outline and hint, and drops of pictures. What the mouse does is in
// PixelCanvas.Mouse.cs; each editor's view adds its own drawing (face outlines, unused pixels, the mirror axis).
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// The flat picture of a pixel editor. Left button paints with the first colour, right button with the second; the wheel
/// zooms at the mouse, middle-drag (or Ctrl+Shift+drag) moves the picture, double-click beside it fits it.
/// </summary>
[System.ComponentModel.DesignerCategory("Code")]
abstract partial class PixelCanvas : Control
{
    protected readonly IPixelEditor Editor;
    protected float Zoom = 16;
    protected PointF Offset;
    bool _fit = true;
    Bitmap? _bitmap;
    bool _stale = true;
    readonly System.Windows.Forms.Timer _ants;
    int _antPhase;

    protected PixelCanvas(IPixelEditor editor)
    {
        Editor = editor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = C.BG;
        AllowDrop = true;
        editor.ImageChanged += MarkStale;
        editor.HoverChanged += Invalidate;
        editor.StateChanged += Invalidate;
        _ants = new System.Windows.Forms.Timer { Interval = 150 };
        _ants.Tick += (_, _) =>
        {
            if (Editor.Session.Selection == null) return;
            _antPhase = (_antPhase + 1) % 8;
            Invalidate();
        };
        _ants.Start();
    }

    /// <summary>The picture and the part of it shown (a texture's frame, a whole skin), or null when nothing is open.</summary>
    protected abstract (RgbaImage Image, PixelRect Area)? View { get; }
    protected virtual string EmptyText => "Nothing to show yet.";
    protected virtual float MaxZoom => 128f;
    /// <summary>Space between screen grid lines (0: no grid at this zoom).</summary>
    protected virtual float GridStep => Zoom >= 6 ? Zoom : 0;
    /// <summary>Drawing over the picture: face outlines, unused pixels, a mirror axis …</summary>
    protected virtual void DrawOverlay(Graphics g, RectangleF rect) { }
    /// <summary>The pixels that mirror one (marked faintly while mirroring).</summary>
    protected virtual IEnumerable<(int X, int Y)> MirrorMarks((int X, int Y) p) => [];
    /// <summary>The face a pixel belongs to (double-click with the Select tool selects it); null: the whole picture.</summary>
    protected virtual PixelRect? FaceAt(int x, int y) => null;

    /// <summary>Something was dragged onto the view and let go (picture files, or a picture from another program).</summary>
    public event Action<IDataObject>? Dropped;

    public void Fit() { _fit = true; Invalidate(); }

    protected void MarkStale() { _stale = true; Invalidate(); }

    protected override void OnResize(EventArgs e) { base.OnResize(e); _fit = true; }

    void FitNow(PixelRect a)
    {
        float pad = this.Px(20);
        float z = Math.Min((ClientSize.Width - pad * 2) / a.Width, (ClientSize.Height - pad * 2) / a.Height);
        Zoom = Math.Clamp(MathF.Floor(z), 1, MaxZoom);
        Offset = new PointF((ClientSize.Width - a.Width * Zoom) / 2f, (ClientSize.Height - a.Height * Zoom) / 2f);
        _fit = false;
    }

    /// <summary>The picture's place on screen.</summary>
    protected RectangleF Screen(PixelRect a) => new(Offset.X, Offset.Y, a.Width * Zoom, a.Height * Zoom);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (View is not { } v)
        {
            using var f = Theme.Ui(10f);
            TextRenderer.DrawText(g, EmptyText, f, ClientRectangle, C.FG_DIM, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var a = v.Area;
        if (_fit) FitNow(a);
        if (_stale || _bitmap == null || _bitmap.Width != a.Width || _bitmap.Height != a.Height)
        {
            _bitmap = ToBitmap(v.Image, a, _bitmap);
            _stale = false;
        }
        var rect = Screen(a);
        ColorPanel.Checker(g, Rectangle.Round(rect), Math.Max(4, (int)(Zoom / 2)));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_bitmap, rect);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        float step = GridStep;
        if (Editor.ShowGrid && step > 0)
        {
            using var p = new Pen(Color.FromArgb(30, 255, 255, 255));
            for (float x = rect.Left; x <= rect.Right + 0.5f; x += step) g.DrawLine(p, x, rect.Top, x, rect.Bottom);
            for (float y = rect.Top; y <= rect.Bottom + 0.5f; y += step) g.DrawLine(p, rect.Left, y, rect.Right, y);
        }
        DrawOverlay(g, rect);
        using (var frame = new Pen(C.BORDER)) g.DrawRectangle(frame, rect.X - 1, rect.Y - 1, rect.Width + 1, rect.Height + 1);
        DrawSelection(g, rect);
        if (Editor.Hover is { } h && !Editor.Session.IsLive)
        {
            Mark(g, rect, h, Color.White, Editor.Tool is PaintTool.Fill or PaintTool.Picker or PaintTool.Select ? 1 : Editor.Settings.BrushSize);
            foreach (var m in MirrorMarks(h)) Mark(g, rect, m, Color.FromArgb(160, 255, 255, 255), 1);
        }
    }

    void DrawSelection(Graphics g, RectangleF rect)
    {
        if (Editor.Session.Selection is not { } s) return;
        var r = new RectangleF(rect.Left + s.X * Zoom, rect.Top + s.Y * Zoom, s.Width * Zoom, s.Height * Zoom);
        bool floating = Editor.Session.Floating != null;
        using var dark = new Pen(Color.FromArgb(220, 0, 0, 0), 1);
        using var light = new Pen(floating ? C.ACCENT2 : Color.White, 1) { DashStyle = DashStyle.Dash, DashOffset = _antPhase };
        g.DrawRectangle(dark, r.X, r.Y, r.Width, r.Height);
        g.DrawRectangle(light, r.X, r.Y, r.Width, r.Height);
        if (!floating) return;
        using var f = Theme.Ui(8.5f);
        string hint = "Drag to move  ·  Enter puts it down  ·  Esc takes it back";
        var size = TextRenderer.MeasureText(hint, f);
        var at = new Rectangle(this.Px(8), ClientSize.Height - size.Height - this.Px(10), size.Width + this.Px(12), size.Height + this.Px(4));
        using (var bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20))) g.FillRectangle(bg, at);
        TextRenderer.DrawText(g, hint, f, at, C.FG, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Outlines a pixel (or a brush of that size around it).</summary>
    protected void Mark(Graphics g, RectangleF rect, (int X, int Y) p, Color color, int brush)
    {
        int lo = -(brush - 1) / 2;
        var r = new RectangleF(rect.Left + (p.X + lo) * Zoom, rect.Top + (p.Y + lo) * Zoom, Zoom * brush, Zoom * brush);
        using var black = new Pen(Color.FromArgb(200, 0, 0, 0), 3);
        using var white = new Pen(color, 1);
        g.DrawRectangle(black, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
        g.DrawRectangle(white, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
    }

    static Bitmap ToBitmap(RgbaImage img, PixelRect a, Bitmap? reuse)
    {
        var b = reuse != null && reuse.Width == a.Width && reuse.Height == a.Height ? reuse : new Bitmap(a.Width, a.Height, PixelFormat.Format32bppArgb);
        if (b != reuse) reuse?.Dispose();
        var data = b.LockBits(new Rectangle(0, 0, a.Width, a.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[a.Width];
        for (int y = 0; y < a.Height; y++)
        {
            Buffer.BlockCopy(img.Pixels, ((a.Y + y) * img.Width + a.X) * 4, row, 0, a.Width * 4);
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, a.Width);
        }
        b.UnlockBits(data);
        return b;
    }

    // ------------------------------------------------------------------ drops

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data != null && PixelClipboard.HasPicture(e.Data)) e.Effect = DragDropEffects.Copy;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data != null) Dropped?.Invoke(e.Data);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ants.Dispose();
            Editor.ImageChanged -= MarkStale;
            Editor.HoverChanged -= Invalidate;
            Editor.StateChanged -= Invalidate;
            _bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
