// MinecraftStudio/Studio/TextureDocument.cs: Texture Studio: change block and item textures (starting from the
// game's own), animate them, and save the pack for any Java version or for Bedrock, or install it into the
// games on this PC. Painting tools, pictures, selections and colours come from PaintToolbars (shared with the other
// studios).
// Owns: TextureDocument.
using System.Diagnostics;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;

namespace MinecraftStudio.Studio;

/// <summary>
/// Texture Studio: change block and item textures (starting from the game's own), animate them, and save the pack for
/// any Java version or for Bedrock, or install it into the games on this PC.
/// </summary>
sealed partial class TextureDocument : Document
{
    readonly TextureEditor _editor;
    readonly TextureNames _names = TextureNames.Default;
    string? _path;
    readonly TextureCanvas _canvas;
    readonly ColorPanel _colors;
    readonly Label _status, _info;
    readonly DarkTextField _name, _description;
    readonly ToolStripButton _undo, _redo;
    readonly PaintToolbars _paint;
    readonly Label _frameLabel;
    readonly NumberField _frameTime;
    readonly DarkCheckBox _smooth, _grid;
    readonly Button _prevFrame, _nextFrame, _addFrame, _removeFrame;
    bool _updating;

    public static string PacksFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Texture Packs");

    /// <summary>The textures the installed Bedrock game animates (read once: it means opening every vanilla archive).</summary>
    static readonly Lazy<HashSet<string>> BedrockAnimated = new(() => TexturePackReader.BedrockAnimated());

    /// <summary>Whether a file is a texture pack (Java or Bedrock resources), not a world or a skin pack.</summary>
    public static bool LooksLikePack(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return File.Exists(Path.Combine(path, "pack.mcmeta")) || File.Exists(Path.Combine(path, "pack.txt")) ||
                       File.Exists(Path.Combine(path, "manifest.json")) && !File.Exists(Path.Combine(path, "skins.json")) && !File.Exists(Path.Combine(path, "level.dat"));
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            bool Has(string file) => names.Any(n => n.Equals(file, StringComparison.OrdinalIgnoreCase) || n.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase) && n.Count(c => c == '/') <= 1);
            if (Has("level.dat") || Has("skins.json")) return false;
            return Has("pack.mcmeta") || Has("pack.txt") || Has("manifest.json");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }

    public TextureDocument(TexturePack pack, TexturePack? basePack, string? path)
    {
        _editor = new TextureEditor(pack, basePack);
        _path = path;

        // ---- toolbar
        var bar = Theme.ToolStrip();
        bar.Dock = DockStyle.Top;
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        var export = DropDown("Export", Glyphs.Export, "Save the pack for a Java version or for Bedrock");
        export.DropDownItems.Add(MenuItem("For a Java Edition version…", (_, _) => ExportJava(install: false)));
        export.DropDownItems.Add(MenuItem("For Bedrock Edition (.mcpack)…", (_, _) => ExportBedrock(install: false)));
        var install = DropDown("Install", Glyphs.Game, "Put the pack into the games on this PC");
        install.DropDownItems.Add(MenuItem("Into Minecraft Java…", (_, _) => ExportJava(install: true)));
        install.DropDownItems.Add(MenuItem("Into Minecraft Bedrock…", (_, _) => ExportBedrock(install: true)));
        var texture = DropDown("Texture", Glyphs.Picture, "Change the selected texture as a whole");
        texture.DropDownItems.Add(MenuItem("Use the game's texture again", (_, _) => _editor.Revert()));
        var resize = new ToolStripMenuItem("Resolution") { ForeColor = C.FG };
        resize.DropDown.BackColor = C.BG2;
        foreach (int size in new[] { 16, 32, 64, 128, 256, 512 })
        {
            int s = size;
            resize.DropDownItems.Add(MenuItem($"{s} × {s}", (_, _) => _editor.Resize(s)));
        }
        texture.DropDownItems.Add(resize);
        texture.DropDownItems.Add(new ToolStripSeparator());
        texture.DropDownItems.Add(MenuItem("Replace with a picture file, as it is (any size, animation strips)…", (_, _) => ImportPicture()));
        texture.DropDownItems.Add(MenuItem("Save as a picture file…", (_, _) => ExportPicture()));
        texture.DropDownItems.Add(MenuItem("Use as the pack's icon", (_, _) => UseAsIcon()));
        _undo = Button("Undo", Glyphs.Undo, "Undo (Ctrl+Z)", (_, _) => _editor.Undo());
        _redo = Button("Redo", Glyphs.Redo, "Redo (Ctrl+Y)", (_, _) => _editor.Redo());
        bar.Items.AddRange(
        [
            Button("New", Glyphs.Add, "Start a new pack from a game's textures", (_, _) => NewPack(Main)),
            Button("Open", Glyphs.Open, "Open a texture pack (Java .zip, Bedrock .mcpack, or a game jar)", (_, _) => OpenDialog(Main)),
            Button("Save", Glyphs.Save, "Save the pack (Ctrl+S)", async (_, _) => await SaveAsync()),
            Button("Save As", Glyphs.SaveAs, "Save the pack under a new name (Ctrl+Shift+S)", (_, _) => SaveAs()),
            new ToolStripSeparator(), _undo, _redo, new ToolStripSeparator(), texture,
            Button("Models", Glyphs.Grid, "The selected texture on its block in 3D, and the shape of any block or item (Java models)", async (_, _) => await ShowModels()),
            export, install,
        ]);

        // ---- tools (left strip) and the tool's options (under the tool bar)
        _paint = new PaintToolbars(_editor, this, "Mirror (M): paint the left and right halves at once", () => _editor.MirrorX,
            () => { _editor.MirrorX = !_editor.MirrorX; _editor.NotifyState(); }, () => _colors!.Swap());
        var tools = _paint.Tools;
        bar.Items.Insert(bar.Items.IndexOf(texture), _paint.PictureMenu);

        // ---- texture list (left)
        var list = new TextureList(_editor) { Dock = DockStyle.Left, Width = this.Px(270) };

        // ---- side panel (right)
        var side = new Panel { Dock = DockStyle.Right, Width = this.Px(244), BackColor = C.BG2, AutoScroll = true };
        Theme.DarkControl(side);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = C.BG2, Padding = new Padding(this.Px(4), this.Px(6), 0, this.Px(10)) };
        SectionHeader Header(string text) => new(text) { Width = this.Px(222), Height = this.Px(24), Margin = new Padding(this.Px(6), this.Px(8), 0, this.Px(2)), BackColor = C.BG2 };
        _name = new DarkTextField(pack.Name) { Width = this.Px(210), Height = this.Px(28), Margin = new Padding(this.Px(6), this.Px(2), 0, 0), AccessibleName = "Pack name" };
        _description = new DarkTextField(pack.Description) { Width = this.Px(210), Height = this.Px(28), Margin = new Padding(this.Px(6), this.Px(4), 0, 0), AccessibleName = "Pack description" };
        _name.Box.TextChanged += (_, _) => { if (!_updating && _name.Text.Trim().Length > 0) { _editor.Pack.Name = _name.Text.Trim(); OnStateChanged(); } };
        _description.Box.TextChanged += (_, _) => { if (!_updating) _editor.Pack.Description = _description.Text.Trim(); };
        _colors = new ColorPanel { Margin = Padding.Empty };
        _colors.Size = _colors.GetPreferredSize(Size.Empty);
        _colors.ColorsChanged += (_, _) => { _editor.Primary = _colors.Primary; _editor.Secondary = _colors.Secondary; };
        _editor.Primary = _colors.Primary;
        _editor.Secondary = _colors.Secondary;
        var preview = new TexturePreview(_editor) { Width = this.Px(210), Height = this.Px(210), Margin = new Padding(this.Px(6), this.Px(2), 0, 0) };
        _grid = new DarkCheckBox("Pixel grid", true) { Margin = new Padding(this.Px(6), this.Px(3), 0, 0) };
        _grid.CheckedChanged += (_, _) => { _editor.ShowGrid = _grid.Checked; _editor.NotifyState(); };
        // Animation: frame in view, add/remove, timing.
        var frames = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(this.Px(2), this.Px(2), 0, 0) };
        _prevFrame = Theme.Button("◀");
        _nextFrame = Theme.Button("▶");
        _frameLabel = new Label { AutoSize = true, ForeColor = C.FG, Margin = new Padding(this.Px(4), this.Px(10), this.Px(4), 0), UseMnemonic = false };
        _prevFrame.Click += (_, _) => _editor.SelectFrame(_editor.Frame - 1);
        _nextFrame.Click += (_, _) => _editor.SelectFrame(_editor.Frame + 1);
        frames.Controls.AddRange([_prevFrame, _frameLabel, _nextFrame]);
        var frameButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(this.Px(2), 0, 0, 0) };
        _addFrame = Theme.Button("Add frame");
        _removeFrame = Theme.Button("Remove");
        _addFrame.Click += (_, _) => _editor.AddFrame();
        _removeFrame.Click += (_, _) => _editor.RemoveFrame();
        frameButtons.Controls.AddRange([_addFrame, _removeFrame]);
        var timing = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(this.Px(6), this.Px(4), 0, 0) };
        _frameTime = new NumberField { Minimum = 1, Maximum = 1000, Value = 1, Width = this.Px(64), Height = this.Px(28), AccessibleName = "Ticks per frame" };
        _smooth = new DarkCheckBox("Smooth", false) { Margin = new Padding(this.Px(10), this.Px(5), 0, 0) };
        _frameTime.ValueChanged += (_, _) => { if (!_updating) _editor.SetTiming((int)_frameTime.Value, _smooth.Checked); };
        _smooth.CheckedChanged += (_, _) => { if (!_updating) _editor.SetTiming((int)_frameTime.Value, _smooth.Checked); };
        timing.Controls.AddRange([_frameTime, new Label { Text = "ticks a frame", AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(this.Px(4), this.Px(7), 0, 0) }, _smooth]);
        _info = new Label { AutoSize = true, MaximumSize = new Size(this.Px(214), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(6), this.Px(3), 0, 0) };
        stack.Controls.Add(Header("PACK"));
        stack.Controls.AddRange([_name, _description]);
        stack.Controls.Add(Header("COLOURS"));
        stack.Controls.Add(_colors);
        stack.Controls.Add(Header("PREVIEW"));
        stack.Controls.AddRange([preview, _grid]);
        stack.Controls.Add(Header("ON A BLOCK (JAVA MODELS)"));
        stack.Controls.AddRange(BlockPreview());
        stack.Controls.Add(Header("ANIMATION"));
        stack.Controls.AddRange([frames, frameButtons, timing]);
        stack.Controls.Add(Header("TEXTURE"));
        stack.Controls.Add(_info);
        side.Controls.Add(stack);

        // ---- centre and status
        _canvas = new TextureCanvas(_editor) { Dock = DockStyle.Fill };
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = this.Px(26), BackColor = C.BG3 };
        _status = new Label { Dock = DockStyle.Fill, ForeColor = C.FG, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(this.Px(8), 0, 0, 0), AutoEllipsis = true };
        statusBar.Controls.Add(_status);
        Controls.Add(_canvas);
        Controls.Add(side);
        Controls.Add(list);
        Controls.Add(tools);
        Controls.Add(statusBar);
        Controls.Add(_paint.Options);
        Controls.Add(bar);
        _canvas.Dropped += _paint.HandleDrop;

        _editor.StateChanged += UpdateState;
        _editor.SelectionChanged += UpdateState;
        _editor.HoverChanged += UpdateStatus;
        _editor.ColorPicked += (c, secondary) => { if (secondary) _colors.Secondary = c; else _colors.Primary = c; _colors.AddRecent(c); };
        _editor.ColorUsed += c => _colors.AddRecent(c);
        _editor.Message += text => Main?.SetStatus(text);
        // Start on the first texture of the pack (or of the game).
        _editor.Select(pack.Textures.Keys.OrderBy(TextureNames.Display).FirstOrDefault() ?? _editor.AllKeys().FirstOrDefault());
        SetTool(PaintTool.Pencil);
        UpdateState();
    }

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
