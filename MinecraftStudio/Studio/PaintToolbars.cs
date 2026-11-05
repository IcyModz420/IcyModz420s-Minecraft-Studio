// MinecraftStudio/Studio/PaintToolbars.cs: the tool strip and the tool-options bar that every pixel editor shows.
// Owns: PaintToolbars — the tools with their keys, mirror and colour swap (left strip), and the options bar: the tool's
// own settings (brush size, how far a fill reaches, filled shapes), what to do with a selection, and colours; and the
// Picture menu each studio puts on its main tool bar (insert or replace with a picture, blank white or see-through, My
// tiles, edit in another program). The actions and keys are in PaintToolbars.Actions.cs.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>The tool strip and the tool-options bar of a pixel editor (skin, texture or mob).</summary>
sealed partial class PaintToolbars
{
    readonly IPixelEditor _editor;
    readonly Control _host;
    readonly Func<bool> _mirrorOn;
    readonly Action _toggleMirror, _swapColours;
    readonly Dictionary<PaintTool, ToolStripButton> _toolButtons = [];
    readonly ToolStripButton _mirror, _filled, _fillAll;
    readonly ToolStripItem[] _brushItems, _fillItems, _shapeItems;
    readonly DarkComboBox _brush, _reach;
    readonly ToolStripLabel _hint;
    readonly ToolStripMenuItem _resize, _putDown, _takeBack;
    bool _updating;

    public ToolStrip Tools { get; }
    public ToolStrip Options { get; }
    /// <summary>Pictures in and out — the studio puts it on its main tool bar.</summary>
    public ToolStripDropDownButton PictureMenu { get; }

    static readonly (PaintTool Tool, string Icon, string Key, string Tip)[] ToolList =
    [
        (PaintTool.Pencil, "pencil", "B", "Pencil (B): left button first colour, right button second colour"),
        (PaintTool.Eraser, "eraser", "E", "Eraser (E): makes pixels see-through"),
        (PaintTool.Fill, "fill", "G", "Fill (G): fills touching pixels of the same colour"),
        (PaintTool.Picker, "picker", "I", "Pick colour (I): or hold Alt with any tool"),
        (PaintTool.Shade, "shade", "S", "Shade (S): first button darker, second button lighter"),
        (PaintTool.Noise, "noise", "N", "Noise (N): varies the brightness of painted pixels a little"),
        (PaintTool.Dither, "dither", "D", "Dither (D): paints every other pixel, for smooth-looking blends"),
        (PaintTool.Line, "line", "L", "Line (L): drag from end to end; Shift keeps it straight"),
        (PaintTool.Rectangle, "rectangle", "R", "Rectangle (R): drag corner to corner; Shift keeps it square"),
        (PaintTool.Ellipse, "ellipse", "C", "Circle and ellipse (C): drag corner to corner; Shift keeps it round"),
        (PaintTool.Select, "select", "V", "Select (V): drag to select, drag inside to move, Ctrl+drag to move a copy"),
    ];

    /// <param name="host">The editor's document (dialogs open over it).</param>
    /// <param name="mirrorTip">What mirroring does in this editor.</param>
    public PaintToolbars(IPixelEditor editor, Control host, string mirrorTip, Func<bool> mirrorOn, Action toggleMirror, Action swapColours)
    {
        _editor = editor;
        _host = host;
        _mirrorOn = mirrorOn;
        _toggleMirror = toggleMirror;
        _swapColours = swapColours;

        // ---- tools (left strip)
        Tools = Theme.ToolStrip();
        Tools.Dock = DockStyle.Left;
        Tools.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
        Tools.ImageScalingSize = new Size(host.Px(20), host.Px(20));
        Tools.Padding = new Padding(host.Px(3), host.Px(6), host.Px(3), host.Px(6));
        foreach (var (tool, icon, _, tip) in ToolList)
        {
            if (tool == PaintTool.Line || tool == PaintTool.Select) Tools.Items.Add(new ToolStripSeparator());
            var b = new ToolStripButton(StudioIcons.Draw(icon, host.Px(20), C.FG)) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Image, AccessibleName = tip.Split(':')[0] };
            var t = tool;
            b.Click += (_, _) => SetTool(t);
            _toolButtons[tool] = b;
            Tools.Items.Add(b);
        }
        Tools.Items.Add(new ToolStripSeparator());
        _mirror = new ToolStripButton(StudioIcons.Draw("mirror", host.Px(20), C.FG)) { ToolTipText = mirrorTip, DisplayStyle = ToolStripItemDisplayStyle.Image, AccessibleName = "Mirror" };
        _mirror.Click += (_, _) => { _toggleMirror(); Update(); };
        Tools.Items.Add(_mirror);
        Tools.Items.Add(new ToolStripButton(null, StudioIcons.Draw("swap", host.Px(20), C.FG), (_, _) => _swapColours()) { ToolTipText = "Swap the two colours (X)", DisplayStyle = ToolStripItemDisplayStyle.Image, AccessibleName = "Swap colours" });

        // ---- options bar (top)
        Options = Theme.ToolStrip();
        Options.Dock = DockStyle.Top;
        Options.ImageScalingSize = new Size(host.Px(16), host.Px(16));
        Options.Padding = new Padding(host.Px(6), host.Px(2), host.Px(6), host.Px(2));
        _brush = new DarkComboBox { AccessibleName = "Brush size" };
        foreach (int n in BrushSizes) _brush.Items.Add(new ComboItem($"{n} px"));
        _brush.SelectedIndexChanged += (_, _) => { if (!_updating && _brush.SelectedIndex >= 0) { _editor.Settings.BrushSize = BrushSizes[_brush.SelectedIndex]; _editor.NotifyState(); } };
        _brushItems = [new ToolStripLabel("Brush") { ForeColor = C.FG_DIM }, new ToolStripHost(_brush, host.Px(80), host.Px(24))];
        _reach = new DarkComboBox { AccessibleName = "Which colours a fill takes" };
        foreach (var (text, _) in Reaches) _reach.Items.Add(new ComboItem(text));
        _reach.SelectedIndexChanged += (_, _) => { if (!_updating && _reach.SelectedIndex >= 0) { _editor.Settings.FillTolerance = Reaches[_reach.SelectedIndex].Tolerance; _editor.NotifyState(); } };
        _fillAll = new ToolStripButton("Whole picture") { CheckOnClick = true, ForeColor = C.FG, ToolTipText = "Fill every pixel of that colour in the picture, not only the touching ones" };
        _fillAll.CheckedChanged += (_, _) => { if (!_updating) { _editor.Settings.FillAll = _fillAll.Checked; _editor.NotifyState(); } };
        _fillItems = [new ToolStripLabel("Fill takes") { ForeColor = C.FG_DIM }, new ToolStripHost(_reach, host.Px(170), host.Px(24)), _fillAll];
        _filled = new ToolStripButton("Filled") { CheckOnClick = true, ForeColor = C.FG, ToolTipText = "Rectangles and ellipses are filled, not only outlined" };
        _filled.CheckedChanged += (_, _) => { if (!_updating) { _editor.Settings.FilledShapes = _filled.Checked; _editor.NotifyState(); } };
        _shapeItems = [_filled];

        var select = DropDown("Selection", "What to do with the selected pixels");
        select.DropDownItems.Add(Item("Select all", "Ctrl+A", () => _editor.Session.SelectAll()));
        select.DropDownItems.Add(Item("Select nothing", "Ctrl+D", () => _editor.Session.Select(null)));
        select.DropDownItems.Add(new ToolStripSeparator());
        select.DropDownItems.Add(Item("Copy", "Ctrl+C", () => Copy(cut: false)));
        select.DropDownItems.Add(Item("Cut", "Ctrl+X", () => Copy(cut: true)));
        select.DropDownItems.Add(Item("Paste", "Ctrl+V", Paste));
        select.DropDownItems.Add(Item("Delete (make see-through)", "Del", () => _editor.Session.Delete()));
        select.DropDownItems.Add(new ToolStripSeparator());
        select.DropDownItems.Add(Item("Flip left to right", "Shift+H", () => Transform(PixelTransform.FlipX)));
        select.DropDownItems.Add(Item("Flip upside down", "Shift+V", () => Transform(PixelTransform.FlipY)));
        select.DropDownItems.Add(Item("Turn right", "Shift+R", () => Transform(PixelTransform.RotateRight)));
        select.DropDownItems.Add(Item("Turn left", "Shift+L", () => Transform(PixelTransform.RotateLeft)));
        _resize = Item("Resize what floats…", "", ResizeFloating);
        select.DropDownItems.Add(_resize);
        select.DropDownItems.Add(new ToolStripSeparator());
        _putDown = Item("Put down", "Enter", () => _editor.Session.Commit());
        _takeBack = Item("Take back", "Esc", () => _editor.Session.Cancel());
        select.DropDownItems.AddRange([_putDown, _takeBack]);

        PictureMenu = new ToolStripDropDownButton("Picture", Theme.Glyph(Glyphs.Picture, C.FG, host.Px(16)))
        {
            ToolTipText = "Put a picture in, start blank, keep your own tiles, or edit in another program", ForeColor = C.FG, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
        };
        var picture = PictureMenu;
        picture.DropDown.BackColor = C.BG2;
        picture.DropDownItems.Add(Item("Insert a picture (place it)…", "Ctrl+I", () => InsertPicture()));
        picture.DropDownItems.Add(Item("Replace with a picture (fitted)…", "Ctrl+Shift+I", () => InsertPicture(replace: true)));
        picture.DropDownItems.Add(Item("Paste a picture", "Ctrl+V", Paste));
        picture.DropDownItems.Add(new ToolStripSeparator());
        picture.DropDownItems.Add(Item("Make it blank: white", "", () => MakeBlank(white: true)));
        picture.DropDownItems.Add(Item("Make it blank: see-through", "", () => MakeBlank(white: false)));
        picture.DropDownItems.Add(Item("Start blank in another program…", "", () => { if (MakeBlank(white: true)) EditElsewhere(); }));
        picture.DropDownItems.Add(new ToolStripSeparator());
        picture.DropDownItems.Add(Item("My tiles…", "", () => MyTiles()));
        picture.DropDownItems.Add(Item("Save this picture to My tiles…", "", () => MyTiles(save: true)));
        picture.DropDownItems.Add(new ToolStripSeparator());
        picture.DropDownItems.Add(Item("Edit in another program…", "Ctrl+E", EditElsewhere));
        picture.DropDownItems.Add(Item("Copy the picture", "", () => { if (_editor.AreaPixels() is { } p) Tell(PixelClipboard.Copy(p) ? "Copied." : "The clipboard is busy; try again."); }));

        var colours = DropDown("Colours", "Change the colours of the selection, or of the whole picture");
        colours.DropDownItems.Add(Item("Adjust colours…", "Ctrl+U", AdjustColours));
        colours.DropDownItems.Add(new ToolStripSeparator());
        colours.DropDownItems.Add(Item("Invert", "", () => QuickColours("Invert", new ColorAdjustment { Invert = true })));
        colours.DropDownItems.Add(Item("Make grey", "", () => QuickColours("Make grey", new ColorAdjustment { Grayscale = true })));
        colours.DropDownItems.Add(Item("A little darker", "", () => QuickColours("Darker", new ColorAdjustment { Brightness = -12 })));
        colours.DropDownItems.Add(Item("A little lighter", "", () => QuickColours("Lighter", new ColorAdjustment { Brightness = 12 })));
        colours.DropDownItems.Add(new ToolStripSeparator());
        colours.DropDownItems.Add(Item("Shift by half, wrapping round (check tiling seams)", "", () => Wrap(0.5)));

        _hint = new ToolStripLabel { ForeColor = C.FG_FAINT, Alignment = ToolStripItemAlignment.Right };
        Options.Items.AddRange(_brushItems);
        Options.Items.AddRange(_fillItems);
        Options.Items.AddRange(_shapeItems);
        Options.Items.Add(new ToolStripSeparator());
        Options.Items.AddRange([select, colours]);
        Options.Items.Add(_hint);

        editor.StateChanged += Update;
        Update();
    }

    static readonly int[] BrushSizes = [1, 2, 3, 4, 5, 8, 12, 16];
    static readonly (string Text, int Tolerance)[] Reaches =
    [
        ("Exactly that colour", 0), ("Close colours (8%)", 8), ("Similar colours (20%)", 20), ("Loosely similar (40%)", 40), ("Any colour (100%)", 100),
    ];

    ToolStripDropDownButton DropDown(string text, string tip)
    {
        var d = new ToolStripDropDownButton(text) { ToolTipText = tip, ForeColor = C.FG, DisplayStyle = ToolStripItemDisplayStyle.Text };
        d.DropDown.BackColor = C.BG2;
        return d;
    }

    static ToolStripMenuItem Item(string text, string keys, Action click) =>
        new(text, null, (_, _) => click()) { ForeColor = C.FG, ShortcutKeyDisplayString = keys.Length > 0 ? keys : null };

    public void SetTool(PaintTool tool)
    {
        if (tool != PaintTool.Select && _editor.Session.Floating != null) _editor.Session.Commit();
        _editor.Tool = tool;
        _editor.NotifyState();
    }

    /// <summary>Shows the editor's state: the tool in use, its options, what can be done now.</summary>
    public void Update()
    {
        _updating = true;
        var tool = _editor.Tool;
        foreach (var (t, b) in _toolButtons) b.Checked = t == tool;
        _mirror.Checked = _mirrorOn();
        var s = _editor.Settings;
        _brush.SelectedIndex = Math.Max(0, Array.IndexOf(BrushSizes, s.BrushSize));
        int reach = Array.FindIndex(Reaches, r => r.Tolerance == s.FillTolerance);
        _reach.SelectedIndex = reach >= 0 ? reach : 0;
        _fillAll.Checked = s.FillAll;
        _filled.Checked = s.FilledShapes;
        bool brush = tool is PaintTool.Pencil or PaintTool.Eraser or PaintTool.Shade or PaintTool.Noise or PaintTool.Dither or PaintTool.Line or PaintTool.Rectangle or PaintTool.Ellipse;
        foreach (var i in _brushItems) i.Visible = brush;
        foreach (var i in _fillItems) i.Visible = tool == PaintTool.Fill;
        foreach (var i in _shapeItems) i.Visible = tool is PaintTool.Rectangle or PaintTool.Ellipse;
        var session = _editor.Session;
        _resize.Enabled = _putDown.Enabled = session.Floating != null;
        _takeBack.Enabled = session.IsLive;
        _hint.Text = session.Floating != null ? "Drag to move · arrow keys nudge · Enter puts it down · Esc takes it back"
            : session.Selection != null ? "Painting stays inside the selection · Ctrl+D selects nothing"
            : tool switch
            {
                PaintTool.Select => "Drag to select · drag inside to move · Ctrl+drag moves a copy",
                PaintTool.Line or PaintTool.Rectangle or PaintTool.Ellipse => "Drag to draw · Shift keeps it straight / square",
                PaintTool.Fill => "Right button fills with the second colour",
                PaintTool.Picker => "Left button: first colour · right button: second colour",
                _ => "[ and ] change the brush size",
            };
        _updating = false;
    }
}
