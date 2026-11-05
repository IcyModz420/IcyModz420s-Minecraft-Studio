// MinecraftStudio/UI/WorldCard.cs: A clickable card for a world: edition icon, name, version and location.
// Owns: WorldCard.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>A clickable card for a world: edition icon, name, version and location.</summary>
sealed class WorldCard : Control
{
    bool _hover;
    Font? _nameFont, _smallFont;

    public WorldCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public Image? Image { get; set; }
    public string Detail { get; set; } = "";
    public string PathText { get; set; } = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float s = DeviceDpi / 96f;
        _nameFont ??= new Font(Font.FontFamily, Font.Size * 1.08f, FontStyle.Bold);
        _smallFont ??= new Font(Font.FontFamily, Font.Size * 0.9f);
        var r = ClientRectangle;
        using (var b = new SolidBrush(_hover || Focused ? C.BG4 : C.BG2)) g.FillRectangle(b, r);
        using (var p = new Pen(_hover ? C.FG_FAINT : C.BORDER)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        int icon = (int)Math.Round(32 * s), left = (int)Math.Round(12 * s);
        if (Image != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Image, new Rectangle(left, (r.Height - icon) / 2, icon, icon));
        }
        int x = left + icon + (int)Math.Round(12 * s), w = r.Width - x - (int)Math.Round(8 * s);
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        int nameH = TextRenderer.MeasureText(g, "Ag", _nameFont, Size.Empty, flags).Height;
        int smallH = TextRenderer.MeasureText(g, "Ag", _smallFont, Size.Empty, flags).Height;
        int y = (r.Height - nameH - smallH * 2 - (int)(4 * s)) / 2;
        TextRenderer.DrawText(g, Text, _nameFont, new Rectangle(x, y, w, nameH), C.FG, flags);
        y += nameH + (int)(2 * s);
        TextRenderer.DrawText(g, Detail, _smallFont, new Rectangle(x, y, w, smallH), C.FG_DIM, flags);
        y += smallH + (int)(2 * s);
        TextRenderer.DrawText(g, PathText, _smallFont, new Rectangle(x, y, w, smallH), C.FG_FAINT, flags | TextFormatFlags.PathEllipsis);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _nameFont?.Dispose(); _smallFont?.Dispose(); }
        base.Dispose(disposing);
    }
}
