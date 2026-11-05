// MinecraftStudio/UI/DarkRadioButton.cs: Radio button painted in the dark palette (ring with an accent dot).
// Owns: DarkRadioButton.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>Radio button painted in the dark palette (ring with an accent dot).</summary>
sealed class DarkRadioButton : RadioButton
{
    bool _hover;

    public DarkRadioButton(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        AutoSize = true;
        ForeColor = C.FG;
        UseMnemonic = false;
        Cursor = Cursors.Hand;
    }

    int Box => (int)Math.Round(15 * DeviceDpi / 96.0);
    int Gap => (int)Math.Round(7 * DeviceDpi / 96.0);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return new Size(Box + Gap + t.Width + 2, Math.Max(Box, t.Height) + (int)Math.Round(6 * DeviceDpi / 96.0));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
        float s = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, (Height - Box) / 2f + 0.5f, Box - 1, Box - 1);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(C.BG)) g.FillEllipse(b, r);
        Color ring = !Enabled ? C.BORDER : Checked ? C.ACCENT2 : Focused || _hover ? C.FG_DIM : C.FG_FAINT;
        using (var p = new Pen(ring, Math.Max(1f, 1.2f * s))) g.DrawEllipse(p, r);
        if (Checked)
        {
            var dot = RectangleF.Inflate(r, -4f * s, -4f * s);
            using var b = new SolidBrush(Enabled ? C.ACCENT2 : C.FG_FAINT);
            g.FillEllipse(b, dot);
        }
        g.SmoothingMode = SmoothingMode.None;
        var text = new Rectangle(Box + Gap, 0, Math.Max(0, Width - Box - Gap), Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? ForeColor : C.FG_FAINT, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}
