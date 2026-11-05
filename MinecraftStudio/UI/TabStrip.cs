// MinecraftStudio/UI/TabStrip.cs: Flat dark tab strip (the WinForms TabControl cannot be fully themed).
// Owns: TabStrip.
using System.Drawing.Drawing2D;

namespace MinecraftStudio.UI;

/// <summary>Flat dark tab strip (the WinForms TabControl cannot be fully themed).</summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed class TabStrip : Control
{
    public sealed class Tab
    {
        public required string Text { get; set; }
        public string? ToolTip { get; set; }
        public object? Tag { get; init; }
        public bool Dirty { get; set; }
    }

    readonly List<Tab> _tabs = new();
    readonly List<(Tab Tab, Rectangle Bounds, Rectangle Close)> _layout = new();
    readonly ToolTip _tip = new();
    Tab? _selected, _hover;
    bool _hoverClose;
    Rectangle _overflow;
    string? _lastTip;

    public bool Closable { get; set; } = true;
    public IReadOnlyList<Tab> Tabs => _tabs;
    public event EventHandler? SelectedChanged;
    public event EventHandler<Tab>? CloseRequested;

    public TabStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = C.BG3;
        Font = Theme.Ui(9f);
        Height = 30;
    }

    public Tab? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Add(Tab tab, bool select = true)
    {
        _tabs.Add(tab);
        if (select) Selected = tab;
        else Invalidate();
    }

    public void Remove(Tab tab)
    {
        int i = _tabs.IndexOf(tab);
        if (i < 0) return;
        _tabs.RemoveAt(i);
        if (_hover == tab) _hover = null;
        if (_selected == tab) Selected = _tabs.Count == 0 ? null : _tabs[Math.Min(i, _tabs.Count - 1)];
        else Invalidate();
    }

    public void SelectNext(bool backward)
    {
        if (_tabs.Count < 2 || _selected == null) return;
        int i = _tabs.IndexOf(_selected);
        Selected = _tabs[(i + (backward ? -1 : 1) + _tabs.Count) % _tabs.Count];
    }

    static string Caption(Tab t) => t.Dirty ? t.Text + "  ●" : t.Text;

    void DoLayout(Graphics g)
    {
        _layout.Clear();
        int h = Height, pad = this.Px(12), closeSize = Closable ? this.Px(18) : 0;
        int min = this.Px(80), max = this.Px(280), overflowWidth = this.Px(26);
        var widths = _tabs.Select(t => Math.Clamp(TextRenderer.MeasureText(g, Caption(t), Font).Width + pad * 2 + (Closable ? closeSize + this.Px(4) : 0), min, max)).ToList();
        int available = Width - overflowWidth;
        int total = widths.Sum();
        if (total > available && total > 0)
        {
            double f = available / (double)total;
            for (int i = 0; i < widths.Count; i++) widths[i] = Math.Max(min, (int)(widths[i] * f));
        }
        int x = 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var r = new Rectangle(x, 0, widths[i], h);
            var close = Closable ? new Rectangle(r.Right - closeSize - this.Px(6), (h - closeSize) / 2, closeSize, closeSize) : Rectangle.Empty;
            _layout.Add((_tabs[i], r, close));
            x += widths[i];
        }
        _overflow = x > Width ? new Rectangle(Width - overflowWidth, 0, overflowWidth, h) : Rectangle.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(C.BG3);
        DoLayout(g);
        int pad = this.Px(12);
        foreach (var (tab, r, close) in _layout)
        {
            if (!_overflow.IsEmpty && r.Left >= _overflow.Left) break;
            bool selected = tab == _selected, hover = tab == _hover;
            using (var back = new SolidBrush(selected ? C.BG : hover ? C.BG4 : C.BG3))
                g.FillRectangle(back, r);
            if (selected)
                using (var accent = new SolidBrush(C.ACCENT2))
                    g.FillRectangle(accent, r.Left, 0, r.Width, this.Px(2));
            int textRight = Closable ? close.Left - this.Px(2) : r.Right - pad;
            var textRect = Rectangle.FromLTRB(r.Left + pad, 0, textRight, r.Height);
            TextRenderer.DrawText(g, Caption(tab), Font, textRect, selected ? C.FG : C.FG_DIM,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (Closable && (selected || hover))
            {
                bool hot = hover && _hoverClose;
                if (hot)
                    using (var hb = new SolidBrush(C.BG4))
                        g.FillRectangle(hb, close);
                using var pen = new Pen(hot ? C.FG : C.FG_DIM, Math.Max(1f, this.Px(1)));
                int m = this.Px(5);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(pen, close.Left + m, close.Top + m, close.Right - m, close.Bottom - m);
                g.DrawLine(pen, close.Right - m, close.Top + m, close.Left + m, close.Bottom - m);
                g.SmoothingMode = SmoothingMode.None;
            }
            using var sep = new Pen(C.BG2);
            g.DrawLine(sep, r.Right - 1, this.Px(6), r.Right - 1, r.Height - this.Px(6));
        }
        if (!_overflow.IsEmpty)
        {
            using var back = new SolidBrush(C.BG3);
            using var arrowFont = Theme.Ui(11f);
            g.FillRectangle(back, _overflow);
            TextRenderer.DrawText(g, "»", arrowFont, _overflow, C.FG, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    (Tab? Tab, bool Close) HitTest(Point p)
    {
        foreach (var (tab, r, close) in _layout)
        {
            if (!_overflow.IsEmpty && r.Left >= _overflow.Left) break;
            if (r.Contains(p)) return (tab, Closable && close.Contains(p));
        }
        return (null, false);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (tab, close) = HitTest(e.Location);
        if (tab != _hover || close != _hoverClose)
        {
            _hover = tab;
            _hoverClose = close;
            Invalidate();
        }
        string? tip = close ? "Close (Ctrl+W)" : tab?.ToolTip;
        if (tip != _lastTip)
        {
            _lastTip = tip;
            _tip.SetToolTip(this, tip);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        _hoverClose = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_overflow.IsEmpty && _overflow.Contains(e.Location))
        {
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
            foreach (var t in _tabs)
            {
                var tab = t;
                menu.Items.Add(new ToolStripMenuItem(Caption(tab), null, (_, _) => Selected = tab) { Checked = tab == _selected, ForeColor = C.FG });
            }
            menu.Show(this, new Point(_overflow.Left, _overflow.Bottom));
            return;
        }
        var (hit, close) = HitTest(e.Location);
        if (hit == null) return;
        if (e.Button == MouseButtons.Middle && Closable) CloseRequested?.Invoke(this, hit);
        else if (e.Button == MouseButtons.Left)
        {
            if (close) CloseRequested?.Invoke(this, hit);
            else Selected = hit;
        }
    }
}
