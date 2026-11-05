// MinecraftStudio/Studio/SkinDocument.cs: Skin Studio: paint a skin on the 3D model or on the flat texture,
// switch arm width and HD size, then save it, export it for any edition and version, or install it into the
// games on this PC. Painting tools, pictures, selections and colours come from PaintToolbars (shared with the other
// studios).
// Owns: SkinDocument.
using System.Diagnostics;
using MinecraftStudio.Core.Imaging;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// Skin Studio: paint a skin on the 3D model or on the flat texture, switch arm width and HD size, then save it, export
/// it for any edition and version, or install it into the games on this PC.
/// </summary>
sealed partial class SkinDocument : Document
{
    readonly SkinEditor _editor;
    string? _path;
    /// <summary>The file on disk is an old 64×32 skin (saving over it changes its layout; the player is asked).</summary>
    bool _fileIsLegacy;
    readonly SkinModelView _view3d;
    readonly SkinTextureView _view2d;
    readonly ColorPanel _colors;
    readonly Label _status, _hint, _checks;
    readonly DarkTextField _name;
    readonly ToolStripButton _undo, _redo;
    readonly PaintToolbars _paint;
    readonly DarkComboBox _arms, _size;
    readonly DarkRadioButton _paintBase, _paintOverlay;
    readonly DarkCheckBox _showOverlay, _grid;
    readonly System.Windows.Forms.Timer _checkTimer;
    bool _updating;

    public static string SkinsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Skins");

    public SkinDocument(Skin skin, string? path)
    {
        _editor = new SkinEditor(skin);
        _path = path;
        _fileIsLegacy = path != null && skin.LoadNotes.Any(n => n.Contains("64 × 32"));

        // ---- toolbar
        var bar = Theme.ToolStrip();
        bar.Dock = DockStyle.Top;
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        var newMenu = DropDown("New", Glyphs.Add, "Start a new skin");
        newMenu.DropDownItems.Add(MenuItem("Blank skin, classic arms (4 pixels)", (_, _) => OpenNew(Blank(SkinModel.Classic))));
        newMenu.DropDownItems.Add(MenuItem("Blank skin, slim arms (3 pixels)", (_, _) => OpenNew(Blank(SkinModel.Slim))));
        newMenu.DropDownItems.Add(new ToolStripSeparator());
        newMenu.DropDownItems.Add(MenuItem("From the game's default skins…", (_, _) => NewFromGame(Main)));
        var export = DropDown("Export", Glyphs.Export, "Save the skin for an edition or version");
        export.DropDownItems.Add(MenuItem("Java Edition 1.8 and newer (64 × 64 PNG)…", (_, _) => ExportFile("Java Edition skin", SkinExport.JavaPng(_editor.Skin), "PNG image|*.png")));
        export.DropDownItems.Add(MenuItem("Java 1.7.10 and older, old Pocket and console versions (64 × 32 PNG)…", (_, _) => ExportFile("Old 64 × 32 skin", SkinExport.LegacyPng(_editor.Skin), "PNG image|*.png")));
        export.DropDownItems.Add(MenuItem("Bedrock Edition (PNG for the Dressing Room)…", (_, _) => ExportFile("Bedrock skin", SkinExport.BedrockPng(_editor.Skin), "PNG image|*.png")));
        export.DropDownItems.Add(new ToolStripSeparator());
        export.DropDownItems.Add(MenuItem("Bedrock skin pack (.mcpack)…", (_, _) => BedrockPack(install: false)));
        export.DropDownItems.Add(MenuItem("Java pack that replaces the default skins (.zip)…", (_, _) => JavaPack(install: false)));
        var install = DropDown("Install", Glyphs.Game, "Put the skin into the games on this PC");
        install.DropDownItems.Add(MenuItem("Into Minecraft Bedrock (skin pack)…", (_, _) => BedrockPack(install: true)));
        install.DropDownItems.Add(MenuItem("Into Minecraft Java (default-skin pack, works offline)…", (_, _) => JavaPack(install: true)));
        install.DropDownItems.Add(MenuItem("As my Java account skin (Minecraft Launcher or minecraft.net)…", (_, _) => UseAsAccountSkin()));
        _undo = Button("Undo", Glyphs.Undo, "Undo (Ctrl+Z)", (_, _) => _editor.Undo());
        _redo = Button("Redo", Glyphs.Redo, "Redo (Ctrl+Y)", (_, _) => _editor.Redo());
        _arms = new DarkComboBox { Width = this.Px(150), AccessibleName = "Arm width" };
        _arms.Items.AddRange([new ComboItem("Classic arms", "4 px"), new ComboItem("Slim arms", "3 px")]);
        _size = new DarkComboBox { Width = this.Px(190), AccessibleName = "Size" };
        _size.Items.AddRange([new ComboItem("64 × 64"), new ComboItem("128 × 128 HD", "Bedrock")]);
        bar.Items.AddRange(
        [
            newMenu,
            Button("Open", Glyphs.Open, "Open a skin (PNG) or a skin from a Bedrock skin pack", (_, _) => OpenDialog(Main)),
            Button("Save", Glyphs.Save, "Save the skin as a PNG (Ctrl+S)", async (_, _) => await SaveAsync()),
            Button("Save As", Glyphs.SaveAs, "Save the skin as a new PNG (Ctrl+Shift+S)", (_, _) => SaveAs()),
            new ToolStripSeparator(), _undo, _redo, new ToolStripSeparator(), export, install, new ToolStripSeparator(),
            new ToolStripLabel("Arms") { ForeColor = C.FG_DIM }, new ToolStripHost(_arms, this.Px(150), this.Px(24)),
            new ToolStripLabel("Size") { ForeColor = C.FG_DIM }, new ToolStripHost(_size, this.Px(190), this.Px(24)),
        ]);
        _arms.SelectedIndexChanged += (_, _) => { if (!_updating) _editor.SetModel(_arms.SelectedIndex == 1 ? SkinModel.Slim : SkinModel.Classic); };
        _size.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            if (_size.SelectedIndex == 0 && _editor.Skin.Image.Width > 64 &&
                Dialogs.Ask(FindForm()!, "Scale the HD skin down to 64 × 64? Fine details are averaged (Undo brings them back).") != DialogResult.Yes)
            {
                UpdateState();
                return;
            }
            _editor.SetSize(_size.SelectedIndex == 1 ? 128 : 64);
        };

        // ---- tools (left) and the tool's options (under the tool bar)
        _paint = new PaintToolbars(_editor, this, "Mirror (M): paint both sides of the body at once", () => _editor.Mirror,
            () => { _editor.Mirror = !_editor.Mirror; _editor.NotifyState(); }, () => _colors!.Swap());
        var tools = _paint.Tools;
        bar.Items.Insert(bar.Items.IndexOf(export), _paint.PictureMenu);

        // ---- side panel (right)
        var side = new Panel { Dock = DockStyle.Right, Width = this.Px(244), BackColor = C.BG2, AutoScroll = true, Padding = new Padding(0, 0, this.Px(4), 0) };
        Theme.DarkControl(side);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = C.BG2, Padding = new Padding(this.Px(4), this.Px(6), 0, this.Px(10)) };
        SectionHeader Header(string text) => new(text) { Width = this.Px(222), Height = this.Px(24), Margin = new Padding(this.Px(6), this.Px(8), 0, this.Px(2)), BackColor = C.BG2 };
        _name = new DarkTextField(skin.Name) { Width = this.Px(210), Height = this.Px(28), Margin = new Padding(this.Px(6), this.Px(2), 0, 0), AccessibleName = "Skin name" };
        _name.Box.TextChanged += (_, _) =>
        {
            string n = _name.Text.Trim();
            if (n.Length > 0 && n != _editor.Skin.Name) { _editor.Skin.Name = n; OnStateChanged(); }
        };
        _colors = new ColorPanel { Margin = Padding.Empty };
        _colors.Size = _colors.GetPreferredSize(Size.Empty);
        _colors.ColorsChanged += (_, _) => { _editor.Primary = _colors.Primary; _editor.Secondary = _colors.Secondary; };
        _editor.Primary = _colors.Primary;
        _editor.Secondary = _colors.Secondary;
        _showOverlay = new DarkCheckBox("Show the second layer", true) { Margin = Margin6() };
        _grid = new DarkCheckBox("Pixel grid", true) { Margin = Margin6() };
        _paintBase = new DarkRadioButton("Base layer") { Margin = Margin6(), Checked = true };
        _paintOverlay = new DarkRadioButton("Second layer (hat, jacket, sleeves…)") { Margin = Margin6() };
        _showOverlay.CheckedChanged += (_, _) =>
        {
            _editor.ShowOverlay = _showOverlay.Checked;
            // The second layer cannot be painted while it is hidden.
            if (!_showOverlay.Checked && _paintOverlay.Checked) _paintBase.Checked = true;
            _editor.NotifyState();
        };
        _grid.CheckedChanged += (_, _) => { _editor.ShowGrid = _grid.Checked; _editor.NotifyState(); };
        _paintOverlay.CheckedChanged += (_, _) =>
        {
            _editor.PaintOverlay = _paintOverlay.Checked;
            if (_paintOverlay.Checked && !_showOverlay.Checked) _showOverlay.Checked = true;
            _editor.NotifyState();
        };
        stack.Controls.Add(Header("NAME"));
        stack.Controls.Add(_name);
        stack.Controls.Add(Header("COLOURS"));
        stack.Controls.Add(_colors);
        stack.Controls.Add(Header("3D VIEW PAINTS"));
        stack.Controls.AddRange([_paintBase, _paintOverlay]);
        stack.Controls.Add(Header("SHOW"));
        stack.Controls.AddRange([_showOverlay, _grid]);
        foreach (var part in SkinLayout.Parts)
        {
            var box = new DarkCheckBox(part, true) { Margin = Margin6(), ForeColor = SkinTextureView.PartColor(part) };
            string p = part;
            box.CheckedChanged += (_, _) =>
            {
                if (box.Checked) _editor.HiddenParts.Remove(p); else _editor.HiddenParts.Add(p);
                _editor.NotifyState();
            };
            stack.Controls.Add(box);
        }
        stack.Controls.Add(Header("CHECKS"));
        _checks = new Label { AutoSize = true, MaximumSize = new Size(this.Px(214), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = Margin6() };
        stack.Controls.Add(_checks);
        side.Controls.Add(stack);

        // ---- views (centre) and status (bottom)
        _view3d = new SkinModelView(_editor) { Dock = DockStyle.Fill };
        _view2d = new SkinTextureView(_editor) { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = C.BG3, SplitterWidth = this.Px(4) };
        split.Panel1.Controls.Add(_view3d);
        split.Panel2.Controls.Add(_view2d);
        // Half and half once the view has a real width (setting it while the window is still tiny throws).
        bool splitPlaced = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitPlaced || split.Width < this.Px(400)) return;
            splitPlaced = true;
            split.SplitterDistance = split.Width / 2;
        };
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = this.Px(26), BackColor = C.BG3 };
        _status = new Label { Dock = DockStyle.Fill, ForeColor = C.FG, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(this.Px(8), 0, 0, 0), AutoEllipsis = true };
        _hint = new Label
        {
            Dock = DockStyle.Right, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, this.Px(5), this.Px(8), 0),
            Text = "3D: left paints, Ctrl+left second colour, right-drag turns, wheel zooms  ·  2D: left/right paint, middle-drag moves  ·  O switches layer",
        };
        statusBar.Controls.Add(_status);
        statusBar.Controls.Add(_hint);

        Controls.Add(split);
        Controls.Add(side);
        Controls.Add(tools);
        Controls.Add(statusBar);
        Controls.Add(_paint.Options);
        Controls.Add(bar);
        _view2d.Dropped += _paint.HandleDrop;
        _view3d.Dropped += _paint.HandleDrop;

        _checkTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _checkTimer.Tick += (_, _) => { _checkTimer.Stop(); UpdateChecks(); };
        _editor.StateChanged += UpdateState;
        _editor.ImageChanged += () => { _checkTimer.Stop(); _checkTimer.Start(); };
        _editor.LayoutChanged += () => { UpdateState(); UpdateChecks(); };
        _editor.HoverChanged += UpdateStatus;
        _editor.ColorPicked += (c, secondary) => { if (secondary) _colors.Secondary = c; else _colors.Primary = c; _colors.AddRecent(c); };
        _editor.ColorUsed += c => _colors.AddRecent(c);
        _editor.Message += text => Main?.SetStatus(text);
        SetTool(PaintTool.Pencil);
        UpdateState();
        UpdateChecks();
        UpdateStatus();
    }

    Padding Margin6() => new(this.Px(6), this.Px(3), 0, this.Px(1));

    ToolStripButton Button(string text, string glyph, string tip, EventHandler click) => new(text, Theme.Glyph(glyph, C.FG, this.Px(16)), click)
    {
        ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ForeColor = C.FG,
    };

    ToolStripDropDownButton DropDown(string text, string glyph, string tip)
    {
        var d = new ToolStripDropDownButton(text, Theme.Glyph(glyph, C.FG, this.Px(16))) { ToolTipText = tip, ForeColor = C.FG, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
        d.DropDown.BackColor = C.BG2;
        return d;
    }

    static ToolStripMenuItem MenuItem(string text, EventHandler click) => new(text, null, click) { ForeColor = C.FG };
}
