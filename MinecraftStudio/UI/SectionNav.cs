// MinecraftStudio/UI/SectionNav.cs: A list of sections down the side of a page (a glyph and a name each), one
// of them selected, like the sidebar of a settings page.
// Owns: SectionNav.
namespace MinecraftStudio.UI;

/// <summary>
/// A list of sections down the side of a page (a glyph and a name each), one of them selected, like the sidebar of a
/// settings page. The entries are real buttons, so they work with the keyboard and screen readers.
/// </summary>
sealed class SectionNav : Panel
{
    readonly FlowLayoutPanel _list;
    readonly List<NavButton> _buttons = [];
    int _selected = -1;

    public event EventHandler? SelectedChanged;

    public SectionNav()
    {
        BackColor = C.BG2;
        Padding = new Padding(0, this.Px(8), 0, this.Px(8));
        _list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = C.BG2 };
        // Entries and headings fill the width exactly, so the list never scrolls sideways.
        _list.Resize += (_, _) => { foreach (Control c in _list.Controls) c.Width = _list.ClientSize.Width; };
        Controls.Add(_list);
    }

    public void Add(string text, string glyph, object tag, string? tip = null)
    {
        var b = new NavButton(text, Theme.Glyph(glyph, C.FG_DIM, this.Px(18)), Theme.Glyph(glyph, C.FG, this.Px(18))) { Tag = tag, Width = _list.ClientSize.Width, Height = this.Px(40) };
        if (tip != null) b.AccessibleDescription = tip;
        int index = _buttons.Count;
        b.Click += (_, _) => SelectedIndex = index;
        _buttons.Add(b);
        _list.Controls.Add(b);
    }

    /// <summary>A small heading between groups of sections.</summary>
    public void AddHeading(string text) =>
        _list.Controls.Add(new Label
        {
            Text = text.ToUpperInvariant(), AutoSize = false, Height = this.Px(28), Width = _list.ClientSize.Width, ForeColor = C.FG_FAINT, Font = Theme.Ui(8f, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(this.Px(14), 0, 0, this.Px(2)), Margin = new Padding(0, this.Px(6), 0, 0), UseMnemonic = false,
        });

    public object? SelectedTag => _selected >= 0 ? _buttons[_selected].Tag : null;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _buttons.Count) return;
            _selected = value;
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].Selected = i == value;
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Select(object tag)
    {
        int i = _buttons.FindIndex(b => Equals(b.Tag, tag));
        if (i >= 0) SelectedIndex = i;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ContainsFocus && keyData is Keys.Down or Keys.Up && _buttons.Count > 0)
        {
            int next = Math.Clamp(_selected + (keyData == Keys.Down ? 1 : -1), 0, _buttons.Count - 1);
            SelectedIndex = next;
            _buttons[next].Focus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    sealed class NavButton : Button
    {
        readonly Image? _dim, _bright;
        bool _selected;

        public NavButton(string text, Image? dim, Image? bright)
        {
            _dim = dim;
            _bright = bright;
            Text = text;
            Image = dim;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = C.BG3;
            BackColor = C.BG2;
            ForeColor = C.FG_DIM;
            TextAlign = ContentAlignment.MiddleLeft;
            ImageAlign = ContentAlignment.MiddleLeft;
            TextImageRelation = TextImageRelation.ImageBeforeText;
            Padding = new Padding(this.Px(14), 0, 0, 0);
            Margin = Padding.Empty;
            Font = Theme.Ui(10f);
            UseMnemonic = false;
            AccessibleName = text;
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                BackColor = value ? C.BG4 : C.BG2;
                ForeColor = value ? C.FG : C.FG_DIM;
                Image = value ? _bright : _dim;
                FlatAppearance.MouseOverBackColor = value ? C.BG4 : C.BG3;
                AccessibleName = Text + (value ? " (selected)" : "");
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_selected) return;
            using var accent = new SolidBrush(C.ACCENT2);
            e.Graphics.FillRectangle(accent, 0, this.Px(8), this.Px(3), Height - this.Px(16));
        }
    }
}
