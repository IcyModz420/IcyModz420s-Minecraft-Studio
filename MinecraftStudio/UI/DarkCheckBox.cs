// MinecraftStudio/UI/DarkCheckBox.cs: Check box painted in the dark palette (accent-filled box with a white
// tick).
// Owns: DarkCheckBox.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>Check box painted in the dark palette (accent-filled box with a white tick).</summary>
sealed class DarkCheckBox : CheckBox
{
    bool _hover;

    public DarkCheckBox(string text, bool isChecked = true)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Checked = isChecked;
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
        var r = new Rectangle(0, (Height - Box) / 2, Box, Box);
        Color fill = Checked ? (Enabled ? C.ACCENT : C.BG4) : C.BG;
        Color border = Checked ? (Enabled ? C.ACCENT2 : C.BORDER) : Focused || _hover && Enabled ? C.FG_DIM : C.FG_FAINT;
        using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
        using (var p = new Pen(border)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        if (Checked)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var p = new Pen(Enabled ? Color.White : C.FG_DIM, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(p, [new PointF(r.Left + 3.5f * s, r.Top + 7.5f * s), new PointF(r.Left + 6.3f * s, r.Top + 10.3f * s), new PointF(r.Left + 11.5f * s, r.Top + 4.6f * s)]);
            g.SmoothingMode = SmoothingMode.None;
        }
        var text = new Rectangle(Box + Gap, 0, Math.Max(0, Width - Box - Gap), Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? ForeColor : C.FG_FAINT, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}
