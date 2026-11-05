// MinecraftStudio/UI/ChoiceTile.cs: A selectable card with a large icon, a title and a short caption.
// Owns: ChoiceTile.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>A selectable card with a large icon, a title and a short caption.</summary>
sealed class ChoiceTile : Control
{
    bool _selected, _hover;
    Font? _titleFont, _captionFont;

    public ChoiceTile()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        ForeColor = C.FG;
    }

    public Image? Image { get; set; }
    public string Caption { get; set; } = "";

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float s = DeviceDpi / 96f;
        _titleFont ??= new Font(Font.FontFamily, Font.Size, FontStyle.Bold);
        _captionFont ??= new Font(Font.FontFamily, Font.Size * 0.88f);
        var r = ClientRectangle;
        // Switched off (a tool this world's edition doesn't have): dim, and the caption says why.
        bool off = !Enabled;
        Color back = off ? C.BG2 : _selected ? Color.FromArgb(36, 54, 38) : _hover ? C.BG4 : C.BG3;
        using (var b = new SolidBrush(back)) g.FillRectangle(b, r);
        if (_selected)
        {
            using var p = new Pen(C.ACCENT2, Math.Max(2, (int)(2 * s))) { Alignment = PenAlignment.Inset };
            g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
        else
        {
            using var p = new Pen(Focused ? C.FG_DIM : C.BORDER);
            g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
        int icon = (int)Math.Round(30 * s), top = (int)Math.Round(11 * s);
        if (Image != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var place = new Rectangle((r.Width - icon) / 2, top, icon, icon);
            if (!off) g.DrawImage(Image, place);
            else
            {
                using var faint = new System.Drawing.Imaging.ImageAttributes();
                faint.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });
                g.DrawImage(Image, place, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, faint);
            }
        }
        int y = top + icon + (int)Math.Round(5 * s);
        int titleH = TextRenderer.MeasureText(g, "Ag", _titleFont).Height;
        const TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Text, _titleFont, new Rectangle(4, y, r.Width - 8, titleH), off ? C.FG_FAINT : C.FG, center | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        y += titleH + (int)Math.Round(2 * s);
        TextRenderer.DrawText(g, Caption, _captionFont, new Rectangle((int)(8 * s), y, r.Width - (int)(16 * s), r.Height - y - (int)(4 * s)), off ? C.FG_FAINT : _selected ? C.FG : C.FG_DIM, center | TextFormatFlags.WordBreak);
        if (_selected)
        {
            // Tick badge in the corner.
            float d = 16 * s, x0 = r.Width - d - 7 * s, y0 = 7 * s;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(C.ACCENT2)) g.FillEllipse(b, x0, y0, d, d);
            using var p = new Pen(Color.White, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(p, [new PointF(x0 + 4.4f * s, y0 + 8.2f * s), new PointF(x0 + 7f * s, y0 + 10.8f * s), new PointF(x0 + 11.8f * s, y0 + 5.4f * s)]);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { PerformClick(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    public void PerformClick() => OnClick(EventArgs.Empty);

    protected override AccessibleObject CreateAccessibilityInstance() => new TileAccessibleObject(this);

    /// <summary>Screen readers and UI Automation see the tile as a radio button that can be selected.</summary>
    sealed class TileAccessibleObject(ChoiceTile tile) : ControlAccessibleObject(tile)
    {
        public override AccessibleRole Role => AccessibleRole.RadioButton;
        public override string? Name => tile.Text;
        public override string? Description => tile.Caption;
        public override AccessibleStates State => base.State | (tile.Selected ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => "Select";
        public override void DoDefaultAction() => tile.PerformClick();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose(); _titleFont = null;
        _captionFont?.Dispose(); _captionFont = null;
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _titleFont?.Dispose(); _captionFont?.Dispose(); }
        base.Dispose(disposing);
    }
}
