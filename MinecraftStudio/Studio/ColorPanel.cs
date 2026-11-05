// MinecraftStudio/Studio/ColorPanel.cs: Colour choice for the pixel editors: a primary colour (left mouse
// button) and a secondary one (right button), a saturation/brightness square with hue and opacity bars, a hex
// field, recently used colours and a fixed palette.
// Owns: ColorPanel.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MinecraftStudio.Core.Imaging;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// Colour choice for the pixel editors: a primary colour (left mouse button) and a secondary one (right button), a
/// saturation/brightness square with hue and opacity bars, a hex field, recently used colours and a fixed palette.
/// </summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed class ColorPanel : Control
{
    uint _primary = 0xFF2E7DD1, _secondary = 0xFFFFFFFF;
    bool _editSecondary;
    double _h, _s, _v;
    int _a = 255;
    Bitmap? _square;
    double _squareHue = -1;
    readonly List<uint> _recent = [];
    readonly DarkTextField _hex;
    bool _updatingHex;
    string? _drag;

    static readonly uint[] Palette =
    [
        0xFF000000, 0xFF3F3F3F, 0xFF7F7F7F, 0xFFBFBFBF, 0xFFFFFFFF, 0xFF6B4226, 0xFF96643C, 0xFFC8955C,
        0xFFF2C79B, 0xFFE0A47A, 0xFFB57A52, 0xFF7A4B2E, 0xFF4A2A18, 0xFFFFE0BD, 0xFFFFCD94, 0xFFEAC086,
        0xFFB80000, 0xFFE53935, 0xFFFF7043, 0xFFFFA000, 0xFFFFEB3B, 0xFFC0CA33, 0xFF43A047, 0xFF1B5E20,
        0xFF00897B, 0xFF00ACC1, 0xFF1E88E5, 0xFF283593, 0xFF5E35B1, 0xFF8E24AA, 0xFFD81B60, 0xFF6D4C41,
    ];

    public ColorPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = C.BG2;
        ForeColor = C.FG;
        Font = Theme.Ui(8.75f);
        _hex = new DarkTextField(PaintOps.ToHex(_primary)) { AccessibleName = "Colour as hex" };
        _hex.Box.Font = Theme.Mono(9.5f);
        _hex.Box.TextChanged += (_, _) =>
        {
            if (_updatingHex || !PaintOps.TryParseHex(_hex.Text, out var c)) return;
            SetActive(c, fromHex: true);
        };
        Controls.Add(_hex);
        Load(_primary);
        AccessibleName = "Colours";
        // A fixed width; the height follows from it (the square is as wide as the panel allows).
        Size = new Size(this.Px(222), 0);
        Height = PaletteArea.Bottom + Pad;
    }

    public event EventHandler? ColorsChanged;

    public uint Primary { get => _primary; set { _primary = value; if (!_editSecondary) Load(value); Invalidate(); ColorsChanged?.Invoke(this, EventArgs.Empty); } }
    public uint Secondary { get => _secondary; set { _secondary = value; if (_editSecondary) Load(value); Invalidate(); ColorsChanged?.Invoke(this, EventArgs.Empty); } }

    public void Swap()
    {
        (_primary, _secondary) = (_secondary, _primary);
        Load(_editSecondary ? _secondary : _primary);
        Invalidate();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remembers a colour that was just used (newest first, no repeats).</summary>
    public void AddRecent(uint c)
    {
        _recent.Remove(c);
        _recent.Insert(0, c);
        if (_recent.Count > 16) _recent.RemoveAt(16);
        Invalidate();
    }

    void Load(uint c)
    {
        var (h, s, v) = PaintOps.ToHsv(c);
        // Keep the hue while the colour is grey, so the square does not jump to red.
        if (s > 0 && v > 0) _h = h;
        _s = s;
        _v = v;
        _a = RgbaImage.A(c);
        _updatingHex = true;
        _hex.Text = PaintOps.ToHex(c);
        _updatingHex = false;
    }

    /// <summary>
    /// Sets the colour being edited. Dragging in the square keeps the exact hue, saturation and value (so the marker
    /// does not drift through rounding); typing in the hex field leaves the field alone.
    /// </summary>
    void SetActive(uint c, bool fromHex = false, bool keepHsv = false)
    {
        if (_editSecondary) _secondary = c; else _primary = c;
        if (!keepHsv)
        {
            var (h, s, v) = PaintOps.ToHsv(c);
            if (s > 0 && v > 0) _h = h;
            _s = s;
            _v = v;
            _a = RgbaImage.A(c);
        }
        if (!fromHex)
        {
            _updatingHex = true;
            _hex.Text = PaintOps.ToHex(c);
            _updatingHex = false;
        }
        Invalidate();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    void FromHsv() => SetActive(PaintOps.FromHsv(_h, _s, _v, _a), keepHsv: true);

    // ------------------------------------------------------------------ layout

    int Pad => this.Px(10);
    // Two labelled swatches side by side (left and right mouse button) with the swap button between them.
    Rectangle LeftSwatch => new(Pad, Pad, this.Px(34), this.Px(30));
    Rectangle SwapBox => new(LeftSwatch.Right + this.Px(4), Pad + this.Px(6), this.Px(20), this.Px(18));
    Rectangle RightSwatch => new(SwapBox.Right + this.Px(4), Pad, this.Px(34), this.Px(30));
    Rectangle Swatches => Rectangle.FromLTRB(Pad, Pad, Width - Pad, LeftSwatch.Bottom + this.Px(18));
    Rectangle Square => new(Pad, Swatches.Bottom + this.Px(10), Math.Max(this.Px(80), Width - Pad * 3 - this.Px(16)), Math.Max(this.Px(80), Width - Pad * 3 - this.Px(16)));
    Rectangle HueBar => new(Square.Right + Pad, Square.Top, this.Px(16), Square.Height);
    Rectangle AlphaBar => new(Pad, Square.Bottom + this.Px(8), HueBar.Right - Pad, this.Px(14));
    int HexTop => AlphaBar.Bottom + this.Px(8);
    int Cell => Math.Max(this.Px(14), (HueBar.Right - Pad - this.Px(7) * 3) / 8);
    Rectangle RecentArea => new(Pad, HexTop + this.Px(26) + this.Px(26), HueBar.Right - Pad, Cell * 2 + this.Px(4));
    Rectangle PaletteArea => new(Pad, RecentArea.Bottom + this.Px(22), HueBar.Right - Pad, Cell * 4 + this.Px(6));

    public override Size GetPreferredSize(Size proposedSize) => new(Width, PaletteArea.Bottom + Pad);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // Layout already runs while the constructor sets the font, before the hex field exists.
        _hex?.SetBounds(Pad, HexTop, HueBar.Right - Pad, this.Px(26));
    }

    Rectangle CellRect(Rectangle area, int index) =>
        new(area.Left + index % 8 * (Cell + this.Px(3)), area.Top + (index / 8) * (Cell + this.Px(3)), Cell, Cell);

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        // The colour the picker below changes has a bright frame and a bold label.
        DrawSwatch(g, LeftSwatch, _primary, !_editSecondary);
        DrawSwatch(g, RightSwatch, _secondary, _editSecondary);
        using (var f = Theme.Ui(10f))
            TextRenderer.DrawText(g, "↔", f, SwapBox, C.FG_DIM, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using (var small = Theme.Ui(7.5f))
        using (var bold = Theme.Ui(7.5f, FontStyle.Bold))
        {
            int y = LeftSwatch.Bottom + this.Px(2);
            TextRenderer.DrawText(g, "Left", _editSecondary ? small : bold, new Rectangle(LeftSwatch.Left - this.Px(4), y, LeftSwatch.Width + this.Px(8), this.Px(14)), _editSecondary ? C.FG_DIM : C.FG, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "Right", _editSecondary ? bold : small, new Rectangle(RightSwatch.Left - this.Px(4), y, RightSwatch.Width + this.Px(8), this.Px(14)), _editSecondary ? C.FG : C.FG_DIM, TextFormatFlags.HorizontalCenter);
            var info = new Rectangle(RightSwatch.Right + this.Px(10), Pad, Width - RightSwatch.Right - this.Px(10) - Pad, this.Px(46));
            TextRenderer.DrawText(g, $"The picker below sets the {(_editSecondary ? "Right" : "Left")} colour.", small, info, C.FG_FAINT, TextFormatFlags.WordBreak);
        }

        var sq = Square;
        if (_square == null || _square.Width != sq.Width || _squareHue != _h) BuildSquare(sq.Size);
        g.DrawImageUnscaled(_square!, sq.Location);
        int mx = sq.Left + (int)Math.Round(_s * (sq.Width - 1)), my = sq.Top + (int)Math.Round((1 - _v) * (sq.Height - 1));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = new Pen(_v > 0.6 && _s < 0.5 ? Color.Black : Color.White, this.Px(2))) g.DrawEllipse(p, mx - this.Px(5), my - this.Px(5), this.Px(10), this.Px(10));
        g.SmoothingMode = SmoothingMode.None;

        var hb = HueBar;
        for (int y = 0; y < hb.Height; y++)
        {
            uint c = PaintOps.FromHsv(360.0 * y / hb.Height, 1, 1);
            using var p = new Pen(Color.FromArgb((int)c));
            g.DrawLine(p, hb.Left, hb.Top + y, hb.Right - 1, hb.Top + y);
        }
        int hy = hb.Top + (int)(_h / 360 * hb.Height);
        using (var p = new Pen(Color.White, this.Px(2))) g.DrawRectangle(p, hb.Left - 1, hy - this.Px(2), hb.Width + 1, this.Px(4));

        var ab = AlphaBar;
        Checker(g, ab);
        uint solid = PaintOps.FromHsv(_h, _s, _v);
        using (var lg = new LinearGradientBrush(ab, Color.FromArgb(0, Color.FromArgb((int)solid)), Color.FromArgb(255, Color.FromArgb((int)solid)), LinearGradientMode.Horizontal))
            g.FillRectangle(lg, ab);
        int ax = ab.Left + (int)(_a / 255.0 * (ab.Width - 1));
        using (var p = new Pen(Color.White, this.Px(2))) g.DrawRectangle(p, ax - this.Px(2), ab.Top - 1, this.Px(4), ab.Height + 1);

        using (var cap = Theme.Ui(8f, FontStyle.Bold))
        {
            TextRenderer.DrawText(g, "RECENT", cap, new Point(Pad, RecentArea.Top - this.Px(16)), C.FG_DIM);
            TextRenderer.DrawText(g, "PALETTE", cap, new Point(Pad, PaletteArea.Top - this.Px(16)), C.FG_DIM);
        }
        for (int i = 0; i < 16; i++)
        {
            var r = CellRect(RecentArea, i);
            if (i < _recent.Count) DrawSwatch(g, r, _recent[i], false);
            else using (var p = new Pen(C.BORDER)) g.DrawRectangle(p, r.Left, r.Top, r.Width - 1, r.Height - 1);
        }
        for (int i = 0; i < Palette.Length; i++) DrawSwatch(g, CellRect(PaletteArea, i), Palette[i], false);
    }

    void DrawSwatch(Graphics g, Rectangle r, uint c, bool active)
    {
        Checker(g, r);
        using (var b = new SolidBrush(Color.FromArgb((int)c))) g.FillRectangle(b, r);
        using var p = new Pen(active ? C.FG : C.BORDER, active ? this.Px(2) : 1);
        g.DrawRectangle(p, r.Left, r.Top, r.Width - 1, r.Height - 1);
    }

    /// <summary>The grey checkerboard that shows through see-through colours.</summary>
    public static void Checker(Graphics g, Rectangle r, int cell = 6)
    {
        using var light = new SolidBrush(Color.FromArgb(78, 78, 82));
        using var dark = new SolidBrush(Color.FromArgb(54, 54, 58));
        g.FillRectangle(dark, r);
        var clip = g.Clip;
        g.SetClip(r);
        for (int y = r.Top; y < r.Bottom; y += cell)
            for (int x = r.Left + ((y - r.Top) / cell % 2) * cell; x < r.Right; x += cell * 2)
                g.FillRectangle(light, x, y, cell, cell);
        g.Clip = clip;
    }

    void BuildSquare(Size size)
    {
        _square?.Dispose();
        _square = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        var data = _square.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[size.Width];
        for (int y = 0; y < size.Height; y++)
        {
            double v = 1 - (double)y / (size.Height - 1);
            for (int x = 0; x < size.Width; x++) row[x] = (int)PaintOps.FromHsv(_h, (double)x / (size.Width - 1), v);
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, size.Width);
        }
        _square.UnlockBits(data);
        _squareHue = _h;
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (SwapBox.Contains(e.Location)) { Swap(); return; }
        if (LeftSwatch.Contains(e.Location) || RightSwatch.Contains(e.Location))
        {
            _editSecondary = RightSwatch.Contains(e.Location);
            Load(_editSecondary ? _secondary : _primary);
            Invalidate();
            return;
        }
        if (Square.Contains(e.Location)) { _drag = "square"; DragTo(e.Location); return; }
        if (HueBar.Contains(e.Location)) { _drag = "hue"; DragTo(e.Location); return; }
        if (Rectangle.Inflate(AlphaBar, 0, this.Px(3)).Contains(e.Location)) { _drag = "alpha"; DragTo(e.Location); return; }
        foreach (var (area, colors) in new[] { (RecentArea, _recent.ToArray()), (PaletteArea, Palette) })
            for (int i = 0; i < colors.Length; i++)
                if (CellRect(area, i).Contains(e.Location))
                {
                    // Left click sets the primary colour, right click the secondary one.
                    if (e.Button == MouseButtons.Right) Secondary = colors[i]; else Primary = colors[i];
                    return;
                }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag != null && e.Button != MouseButtons.None) DragTo(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = null;
    }

    void DragTo(Point p)
    {
        double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        switch (_drag)
        {
            case "square":
                _s = Clamp01((p.X - Square.Left) / (double)(Square.Width - 1));
                _v = 1 - Clamp01((p.Y - Square.Top) / (double)(Square.Height - 1));
                break;
            case "hue":
                _h = Clamp01((p.Y - HueBar.Top) / (double)HueBar.Height) * 359.999;
                break;
            case "alpha":
                _a = (int)Math.Round(Clamp01((p.X - AlphaBar.Left) / (double)(AlphaBar.Width - 1)) * 255);
                break;
            default: return;
        }
        FromHsv();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _square?.Dispose();
        base.Dispose(disposing);
    }
}
