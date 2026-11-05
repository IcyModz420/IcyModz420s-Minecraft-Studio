// MinecraftStudio/Studio/PictureDialog.cs: "Insert a picture" / "Replace with a picture" — any picture made ready for a
// skin, texture, tile or mob texture, or for the selected part of one.
// Owns: PictureDialog — the picture (a file, the clipboard, one dropped or pasted, or a saved tile), its size (fit, fill
// and crop, stretch, as it is, or a size typed in) within the selection or the whole picture, crisp or smooth scaling,
// a plain background made see-through, fewer colours (its best few, or the colours already there) with optional
// dithering, and a live preview. Inserted, the result floats to be dragged into place; replacing puts it down at once.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.UI;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;

namespace MinecraftStudio.Studio;

sealed class PictureDialog : DarkDialog
{
    readonly IPixelEditor _editor;
    readonly RgbaImage _area;
    /// <summary>Where the picture goes: the selection, or the whole area.</summary>
    readonly PixelRect _target;
    readonly bool _replace;
    RgbaImage? _source, _work;
    string _sourceName = "";
    readonly PreviewBox _preview;
    readonly Label _what, _result;
    readonly DarkRadioButton _fit, _fill, _stretch, _original, _custom;
    readonly NumberField _w, _h;
    readonly DarkComboBox _scaling, _colors;
    readonly DarkCheckBox _background, _dither;
    readonly NumberField _tolerance;
    readonly Button _ok;
    bool _loading;

    /// <summary>The pixels to paste, and where (relative to the area).</summary>
    public RgbaImage? Result { get; private set; }
    public (int X, int Y) Placement { get; private set; }
    /// <summary>The part of the area the picture goes into (replacing clears all of it first).</summary>
    public PixelRect Target => _target;

    static readonly (string Text, int Colors)[] ColorChoices =
    [
        ("Keep every colour", 0), ("32 colours", 32), ("16 colours", 16), ("8 colours", 8), ("4 colours", 4), ("Only the colours already here", -1),
    ];

    /// <param name="replace">Replace the whole picture (or the selection) at once, instead of pasting the picture to place it.</param>
    public PictureDialog(IPixelEditor editor, RgbaImage area, RgbaImage? picture, string? name, bool replace = false) : base(replace ? "Replace with a picture" : "Insert a picture", 860)
    {
        _editor = editor;
        _area = area;
        _replace = replace;
        var whole = new PixelRect(0, 0, area.Width, area.Height);
        _target = editor.Session.Selection is { } sel && sel.Intersect(whole) is { IsEmpty: false } inside ? inside : whole;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, BackColor = C.BG2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.Px(400)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _preview = new PreviewBox { Size = new Size(this.Px(384), this.Px(384)), Margin = new Padding(0, 0, this.Px(16), 0) };
        layout.Controls.Add(_preview, 0, 0);

        var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, BackColor = C.BG2 };
        Label Note(string text, Color? color = null) => new() { Text = text, AutoSize = true, MaximumSize = new Size(this.Px(420), 0), ForeColor = color ?? C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, this.Px(8), 0, this.Px(2)) };
        string into = _target.Width == area.Width && _target.Height == area.Height ? $"{editor.AreaName} ({area.Width} × {area.Height})" : $"the selected part of {editor.AreaName} ({_target.Width} × {_target.Height})";
        side.Controls.Add(Note(replace
            ? $"Replaces all of {into} with the picture, as one step (Ctrl+Z takes it back)."
            : $"Into {into}. The picture floats afterwards: drag it into place and press Enter.", C.FG));
        var sourceRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = C.BG2, Margin = new Padding(0, this.Px(6), 0, 0) };
        var browse = Theme.Button("Choose a picture file…", primary: picture == null);
        var clip = Theme.Button("From the clipboard");
        browse.Click += (_, _) => Browse();
        clip.Click += (_, _) => FromClipboard();
        sourceRow.Controls.AddRange([browse, clip]);
        side.Controls.Add(sourceRow);
        _what = Note("No picture yet.");
        side.Controls.Add(_what);

        side.Controls.Add(Note("SIZE", C.FG_FAINT));
        _fit = new DarkRadioButton("Fit inside (keeps its shape)") { Checked = true };
        _fill = new DarkRadioButton("Fill (keeps its shape, cuts off what sticks out)");
        _stretch = new DarkRadioButton("Stretch to the whole picture");
        _original = new DarkRadioButton("As it is (not scaled)");
        _custom = new DarkRadioButton("This size:");
        _w = new NumberField { Minimum = 1, Maximum = 4096, Value = _target.Width, Width = this.Px(70), Height = this.Px(28), AccessibleName = "Width" };
        _h = new NumberField { Minimum = 1, Maximum = 4096, Value = _target.Height, Width = this.Px(70), Height = this.Px(28), AccessibleName = "Height" };
        // Replacing a whole tile or skin wants exactly its size.
        if (replace) _stretch.Checked = true;
        // The size boxes sit on their own row so all five choices share one parent (and stay one choice).
        var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = C.BG2, Margin = new Padding(this.Px(20), 0, 0, 0) };
        sizeRow.Controls.AddRange([_w, new Label { Text = "×", AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(this.Px(4), this.Px(7), this.Px(4), 0) }, _h]);
        side.Controls.AddRange([_fit, _fill, _stretch, _original, _custom, sizeRow]);

        side.Controls.Add(Note("LOOK", C.FG_FAINT));
        _scaling = new DarkComboBox { Width = this.Px(300), AccessibleName = "Scaling" };
        _scaling.Items.AddRange([new ComboItem("Smooth (photos and drawings)"), new ComboItem("Crisp (pixel art)")]);
        _scaling.SelectedIndex = 0;
        side.Controls.Add(_scaling);
        var bgRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = C.BG2, Margin = new Padding(0, this.Px(4), 0, 0) };
        _background = new DarkCheckBox("Make a plain background see-through", false);
        _tolerance = new NumberField { Minimum = 0, Maximum = 100, Value = 12, Width = this.Px(60), Height = this.Px(28), AccessibleName = "How close to the background colour (percent)" };
        bgRow.Controls.AddRange([_background, _tolerance, new Label { Text = "%", AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(this.Px(2), this.Px(7), 0, 0) }]);
        side.Controls.Add(bgRow);
        _colors = new DarkComboBox { Width = this.Px(300), AccessibleName = "Colours" };
        foreach (var (text, _) in ColorChoices) _colors.Items.Add(new ComboItem(text));
        _colors.SelectedIndex = 0;
        side.Controls.Add(_colors);
        _dither = new DarkCheckBox("Dither (mixes the colours for smoother shading)", false);
        side.Controls.Add(_dither);
        _result = Note("");
        side.Controls.Add(_result);
        layout.Controls.Add(side, 1, 0);

        var buttons = AddButtons(("Cancel", DialogResult.Cancel, false), (replace ? "Replace" : "Insert", DialogResult.OK, true));
        _ok = (Button)buttons.Controls.Cast<Control>().First(c => c is Button b && b.DialogResult == DialogResult.OK);
        Controls.Add(layout);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        foreach (var r in new[] { _fit, _fill, _stretch, _original, _custom }) r.CheckedChanged += (_, _) => { if (((RadioButton)r).Checked) Redo(); };
        _w.ValueChanged += (_, _) => { if (!_loading) { _custom.Checked = true; Redo(); } };
        _h.ValueChanged += (_, _) => { if (!_loading) { _custom.Checked = true; Redo(); } };
        _scaling.SelectedIndexChanged += (_, _) => Redo();
        _colors.SelectedIndexChanged += (_, _) => Redo();
        _background.CheckedChanged += (_, _) => Redo();
        _tolerance.ValueChanged += (_, _) => { if (_background.Checked) Redo(); };
        _dither.CheckedChanged += (_, _) => Redo();
        if (Palette.Colors(area, 1).Count == 0) _colors.Items.RemoveAt(_colors.Items.Count - 1);

        if (picture != null) SetSource(picture, name ?? "the picture");
        else Shown += (_, _) => { if (_source == null) Browse(); };
        Redo();
    }

    void Browse()
    {
        using var dlg = new OpenFileDialog { Filter = PixelClipboard.OpenFilter, Title = "Choose a picture" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { SetSource(PixelClipboard.Load(dlg.FileName), Path.GetFileName(dlg.FileName)); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { Dialogs.Error(this, $"Could not use {Path.GetFileName(dlg.FileName)}:\n\n{e.Message}"); }
    }

    void FromClipboard()
    {
        try
        {
            if (PixelClipboard.FromClipboard() is { } c) SetSource(c.Image, c.Name ?? "the picture on the clipboard");
            else Dialogs.Info(this, "There is no picture on the clipboard. Copy one in any program (or a browser) first.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException) { Dialogs.Error(this, "The picture on the clipboard could not be read:\n\n" + e.Message); }
    }

    void SetSource(RgbaImage picture, string name)
    {
        _source = picture;
        _sourceName = name;
        // Fit, fill and stretch never need more than a few times the area's size: shrinking a big photo once keeps it quick.
        int limit = Math.Max(1024, 4 * Math.Max(_target.Width, _target.Height));
        _work = picture;
        while (_work.Width > limit * 2 || _work.Height > limit * 2) _work = _work.Halved();
        // A picture made at this size already (pixel art) starts crisp and as it is.
        bool pixelArt = picture.Width <= _target.Width * 2 && picture.Height <= _target.Height * 2;
        _loading = true;
        _scaling.SelectedIndex = pixelArt ? 1 : 0;
        if (picture.Width == _target.Width && picture.Height == _target.Height) _original.Checked = true;
        _w.Value = PictureFit.Size(picture.Width, picture.Height, _target.Width, _target.Height, PictureFitMode.Fit).Width;
        _h.Value = PictureFit.Size(picture.Width, picture.Height, _target.Width, _target.Height, PictureFitMode.Fit).Height;
        _loading = false;
        _what.Text = $"{name}: {picture.Width} × {picture.Height}.";
        Redo();
    }

    void Redo()
    {
        if (_loading) return;
        _tolerance.Enabled = _background.Checked;
        _dither.Enabled = _colors.SelectedIndex > 0;
        if (_source == null || _work == null) { Result = null; _ok.Enabled = false; _preview.Display(_area, null, (0, 0), null); return; }
        int choice = ColorChoices[Math.Clamp(_colors.SelectedIndex, 0, ColorChoices.Length - 1)].Colors;
        var o = new PictureOptions
        {
            Fit = _fill.Checked ? PictureFitMode.Fill : _stretch.Checked || _custom.Checked ? PictureFitMode.Stretch : _original.Checked ? PictureFitMode.Original : PictureFitMode.Fit,
            Smooth = _scaling.SelectedIndex == 0,
            RemoveBackground = _background.Checked,
            BackgroundTolerance = (int)_tolerance.Value,
            Colors = Math.Max(0, choice),
            UsePalette = choice < 0,
            Dither = _dither.Checked && choice != 0,
        };
        // As it is and a typed size work on the picture as it came; the others on the quick copy.
        var from = _original.Checked || _custom.Checked ? _source : _work;
        int tw = _custom.Checked ? (int)_w.Value : _target.Width, th = _custom.Checked ? (int)_h.Value : _target.Height;
        var img = PictureFit.Prepare(from, tw, th, o, choice < 0 ? Palette.Colors(_area) : null);
        Result = img;
        // In the middle of where it goes (its top-left corner when it is larger).
        Placement = (_target.X + Math.Max(0, (_target.Width - img.Width) / 2), _target.Y + Math.Max(0, (_target.Height - img.Height) / 2));
        _ok.Enabled = true;
        int colours = Palette.Colors(img, 100000).Count;
        _result.Text = $"Result: {img.Width} × {img.Height}, {colours} colour{(colours == 1 ? "" : "s")}." +
                       (img.Width > _target.Width || img.Height > _target.Height ? (_replace ? "  Larger than where it goes: its top-left part is used." : "  Larger than where it goes: drag to choose the part that shows.") : "");
        _preview.Display(_area, img, Placement, _replace ? _target : null);
    }

    /// <summary>The result over the picture it goes into, as large as fits.</summary>
    sealed class PreviewBox : Control
    {
        Bitmap? _bitmap;

        public PreviewBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = C.BG;
        }

        /// <param name="cleared">The part replaced (cleared before the picture goes on), or null when inserting.</param>
        public void Display(RgbaImage area, RgbaImage? result, (int X, int Y) at, PixelRect? cleared)
        {
            var img = area.Clone();
            if (result != null && cleared is { } c) img.Fill(c.X, c.Y, c.Width, c.Height, 0);
            if (result != null)
                for (int y = 0; y < result.Height; y++)
                    for (int x = 0; x < result.Width; x++)
                        if (img.Contains(at.X + x, at.Y + y) && (cleared == null || cleared.Value.Contains(at.X + x, at.Y + y)))
                            img[at.X + x, at.Y + y] = PaintOps.Over(result[x, y], img[at.X + x, at.Y + y]);
            _bitmap?.Dispose();
            _bitmap = PixelClipboard.ToBitmap(img);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_bitmap == null) return;
            float z = Math.Min((float)Width / _bitmap.Width, (float)Height / _bitmap.Height);
            if (z >= 1) z = MathF.Floor(z);
            var r = new RectangleF((Width - _bitmap.Width * z) / 2, (Height - _bitmap.Height * z) / 2, _bitmap.Width * z, _bitmap.Height * z);
            ColorPanel.Checker(g, Rectangle.Round(r), Math.Max(4, (int)(z / 2)));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_bitmap, r);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bitmap?.Dispose();
            base.Dispose(disposing);
        }
    }
}
