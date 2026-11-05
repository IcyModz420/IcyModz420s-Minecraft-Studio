// MinecraftStudio/UI/SlimProgress.cs: A thin flat progress bar; shows a sliding block when the amount of work
// is unknown.
// Owns: SlimProgress.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>A thin flat progress bar; <see cref="Marquee"/> shows a sliding block when the amount of work is unknown.</summary>
sealed class SlimProgress : Control
{
    double _value;
    int _phase;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };

    public SlimProgress()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = C.BG4;
        _timer.Tick += (_, _) => { _phase = (_phase + Math.Max(2, Width / 90)) % Math.Max(1, Width + Width / 3); Invalidate(); };
    }

    public Color BarColor { get; set; } = C.ACCENT2;
    /// <summary>When set, only a centred band this many pixels high is drawn (the rest shows the parent's colour).</summary>
    public int Thickness { get; set; }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    public bool Marquee
    {
        get => _timer.Enabled;
        set { _timer.Enabled = value; _phase = 0; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var band = ClientRectangle;
        if (Thickness > 0 && Thickness < Height)
        {
            using (var outside = new SolidBrush(Parent?.BackColor ?? BackColor)) e.Graphics.FillRectangle(outside, ClientRectangle);
            band = new Rectangle(0, (Height - Thickness) / 2, Width, Thickness);
        }
        using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, band);
        using var bar = new SolidBrush(BarColor);
        if (Marquee)
        {
            int block = Math.Max(8, Width / 3);
            e.Graphics.FillRectangle(bar, Rectangle.Intersect(band, new Rectangle(_phase - block, band.Top, block, band.Height)));
            return;
        }
        int w = (int)Math.Round(Width * _value);
        if (w > 0) e.Graphics.FillRectangle(bar, band.Left, band.Top, w, band.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
