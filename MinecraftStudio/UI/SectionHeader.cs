// MinecraftStudio/UI/SectionHeader.cs: Small bold section title followed by a thin rule.
// Owns: SectionHeader.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>Small bold section title followed by a thin rule.</summary>
sealed class SectionHeader : Control
{
    public SectionHeader(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Font = Theme.Ui(8.5f, FontStyle.Bold);
        ForeColor = C.FG_DIM;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var bg = new SolidBrush(BackColor)) e.Graphics.FillRectangle(bg, ClientRectangle);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var size = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, flags);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Point(0, (Height - size.Height) / 2), ForeColor, flags);
        int x = size.Width + (int)Math.Round(10 * DeviceDpi / 96.0), mid = Height / 2;
        using var p = new Pen(C.BORDER);
        if (x < Width) e.Graphics.DrawLine(p, x, mid, Width, mid);
    }
}
