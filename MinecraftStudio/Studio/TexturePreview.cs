// MinecraftStudio/Studio/TexturePreview.cs: The selected texture repeated three by three, the way blocks sit
// side by side, so seams show; animated textures play at the game's speed (20 ticks a second).
// Owns: TexturePreview.
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// The selected texture repeated three by three, the way blocks sit side by side, so seams show; animated textures
/// play at the game's speed (20 ticks a second).
/// </summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed class TexturePreview : Control
{
    readonly TextureEditor _editor;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    int _tick;
    Bitmap? _frame;
    (string? Key, int Frame) _shown = (null, -1);

    public TexturePreview(TextureEditor editor)
    {
        _editor = editor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = C.BG2;
        editor.ImageChanged += Changed;
        editor.SelectionChanged += Changed;
        _timer.Tick += (_, _) => { _tick++; if (Playing) Invalidate(); };
        _timer.Start();
        AccessibleName = "Preview";
    }

    bool Playing => _editor.Current?.Animation != null;

    void Changed() { _shown = (null, -1); Invalidate(); }

    /// <summary>The frame to show at a tick: the animation's own order and times, or every frame in turn.</summary>
    static int FrameAt(PackTexture t, int tick)
    {
        var a = t.Animation!;
        int count = TextureEditor.FrameCount(t);
        var order = a.Frames is { Count: > 0 } f ? f.Select(x => (Index: Math.Clamp(x.Index, 0, count - 1), Time: Math.Max(1, x.Time ?? a.FrameTime))).ToList()
                                              : Enumerable.Range(0, count).Select(i => (Index: i, Time: Math.Max(1, a.FrameTime))).ToList();
        int total = order.Sum(o => o.Time);
        int at = tick % Math.Max(1, total);
        foreach (var (index, time) in order)
        {
            if (at < time) return index;
            at -= time;
        }
        return order[^1].Index;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_editor.Current is not { } t) return;
        int frame = Playing ? FrameAt(t, _tick) : _editor.Frame;
        if (_shown != (_editor.Selected, frame) || _frame == null)
        {
            _frame?.Dispose();
            var r = TextureEditor.FrameRect(t, frame);
            _frame = SkinPickDialog.BitmapOf(t.Image.Crop(r.X, r.Y, r.Width, r.Height));
            _shown = (_editor.Selected, frame);
        }
        int size = Math.Min(Width, Height) - this.Px(8);
        float cell = size / 3f;
        var area = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
        ColorPanel.Checker(g, Rectangle.Round(area), 6);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                g.DrawImage(_frame, area.Left + x * cell, area.Top + y * cell, cell, cell);
        if (Playing)
        {
            using var f = Theme.Ui(7.5f);
            TextRenderer.DrawText(g, $"frame {frame + 1} of {TextureEditor.FrameCount(t)}", f, new Point((int)area.Left + 4, (int)area.Bottom - this.Px(16)), Color.White);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.ImageChanged -= Changed;
            _editor.SelectionChanged -= Changed;
            _timer.Dispose();
            _frame?.Dispose();
        }
        base.Dispose(disposing);
    }
}
