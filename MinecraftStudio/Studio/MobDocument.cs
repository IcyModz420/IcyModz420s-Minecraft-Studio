// MinecraftStudio/Studio/MobDocument.cs: Mob Studio: paint the textures of every mob — animals, monsters, villagers,
// bosses — and the other entities (chests, beds, signs, boats …) of Java Edition (PC) and Bedrock Edition (Windows PC),
// on their 3D models or flat, then save the pack for any Java version or Bedrock, install it, or add it to a world.
// Owns: MobDocument — the window: the mob list, the 3D model beside the flat texture, colours, which model a texture is
// drawn on and which of its parts show. The painting tools come from PaintToolbars (shared with the other studios);
// new, open, save, export, install and "add to a world" are in MobDocument.Files.cs.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;

namespace MinecraftStudio.Studio;

sealed partial class MobDocument : Document
{
    const string NoModel = "";
    readonly TextureEditor _editor;
    readonly MobCatalog _catalog;
    readonly TextureNames _names = TextureNames.Default;
    string? _path;
    readonly MobModelView _view3d;
    readonly MobTextureView _view2d;
    readonly ColorPanel _colors;
    readonly PaintToolbars _paint;
    readonly TextureList _list;
    readonly Label _status, _info;
    readonly DarkTextField _name, _description;
    readonly DarkComboBox _model;
    readonly FlowLayoutPanel _parts;
    readonly ToolStripButton _undo, _redo;
    /// <summary>A model chosen by hand for a texture (<see cref="NoModel"/>: flat only), by texture.</summary>
    readonly Dictionary<string, string> _chosen = new(StringComparer.Ordinal);
    readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    ModelMesh? _mesh;
    string? _meshFor;
    bool _updating;

    public MobDocument(TexturePack pack, MobCatalog catalog, string? path)
    {
        _catalog = catalog;
        _editor = new TextureEditor(pack, catalog.Textures);
        _path = path;

        // ---- tool bar
        var bar = Theme.ToolStrip();
        bar.Dock = DockStyle.Top;
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        var newMenu = DropDown("New", Glyphs.Add, "Start a new mob pack from a game's mobs");
        newMenu.DropDownOpening += (_, _) => FillNewMenu(newMenu);
        newMenu.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        var texture = DropDown("Texture", Glyphs.Picture, "Change the selected texture as a whole");
        texture.DropDownItems.Add(MenuItem("Use the game's texture again", (_, _) => _editor.Revert()));
        texture.DropDownItems.Add(new ToolStripSeparator());
        texture.DropDownItems.Add(MenuItem("Replace with a picture file (as it is)…", (_, _) => ReplacePicture()));
        texture.DropDownItems.Add(MenuItem("Save as a picture file…", (_, _) => ExportPicture()));
        texture.DropDownItems.Add(MenuItem("Use as the pack's icon", (_, _) => UseAsIcon()));
        var export = DropDown("Export", Glyphs.Export, "Save the pack for a Java version or for Bedrock");
        export.DropDownItems.Add(MenuItem("For a Java Edition version…", (_, _) => ExportJava(install: false)));
        export.DropDownItems.Add(MenuItem("For Bedrock Edition (.mcpack)…", (_, _) => ExportBedrock(install: false)));
        var install = DropDown("Install", Glyphs.Game, "Put the pack into the games on this PC, or into a world");
        install.DropDownItems.Add(MenuItem("Into Minecraft Java…", (_, _) => ExportJava(install: true)));
        install.DropDownItems.Add(MenuItem("Into Minecraft Bedrock…", (_, _) => ExportBedrock(install: true)));
        install.DropDownItems.Add(new ToolStripSeparator());
        install.DropDownItems.Add(MenuItem("Into a world on this PC…", async (_, _) => await AddToWorld()));
        _undo = Button("Undo", Glyphs.Undo, "Undo (Ctrl+Z)", (_, _) => _editor.Undo());
        _redo = Button("Redo", Glyphs.Redo, "Redo (Ctrl+Y)", (_, _) => _editor.Redo());
        bar.Items.AddRange(
        [
            newMenu,
            Button("Open", Glyphs.Open, "Open a pack (Java .zip, Bedrock .mcpack or a folder) to change its mobs", (_, _) => OpenDialog(Main)),
            Button("Save", Glyphs.Save, "Save the pack (Ctrl+S)", async (_, _) => await SaveAsync()),
            Button("Save As", Glyphs.SaveAs, "Save the pack under a new name (Ctrl+Shift+S)", (_, _) => SaveAs()),
            new ToolStripSeparator(), _undo, _redo, new ToolStripSeparator(), texture, export, install,
        ]);

        // ---- tools and their options (shared with the other studios)
        _paint = new PaintToolbars(_editor, this, "Mirror (M): paint the left and right halves of the texture at once", () => _editor.MirrorX,
            () => { _editor.MirrorX = !_editor.MirrorX; _editor.NotifyState(); }, () => _colors!.Swap());
        bar.Items.Insert(bar.Items.IndexOf(texture), _paint.PictureMenu);

        // ---- mob list (left)
        _list = new TextureList(_editor, MobCatalog.IsMobKey, Label, "Find a mob…") { Dock = DockStyle.Left, Width = this.Px(280) };

        // ---- side panel (right)
        var side = new Panel { Dock = DockStyle.Right, Width = this.Px(250), BackColor = C.BG2, AutoScroll = true };
        Theme.DarkControl(side);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = C.BG2, Padding = new Padding(this.Px(4), this.Px(6), 0, this.Px(10)) };
        SectionHeader Header(string text) => new(text) { Width = this.Px(226), Height = this.Px(24), Margin = new Padding(this.Px(6), this.Px(8), 0, this.Px(2)), BackColor = C.BG2 };
        _name = new DarkTextField(pack.Name) { Width = this.Px(214), Height = this.Px(28), Margin = new Padding(this.Px(6), this.Px(2), 0, 0), AccessibleName = "Pack name" };
        _description = new DarkTextField(pack.Description) { Width = this.Px(214), Height = this.Px(28), Margin = new Padding(this.Px(6), this.Px(4), 0, 0), AccessibleName = "Pack description" };
        _name.Box.TextChanged += (_, _) => { if (!_updating && _name.Text.Trim().Length > 0) { _editor.Pack.Name = _name.Text.Trim(); OnStateChanged(); } };
        _description.Box.TextChanged += (_, _) => { if (!_updating) _editor.Pack.Description = _description.Text.Trim(); };
        _colors = new ColorPanel { Margin = Padding.Empty };
        _colors.Size = _colors.GetPreferredSize(Size.Empty);
        _colors.ColorsChanged += (_, _) => { _editor.Primary = _colors.Primary; _editor.Secondary = _colors.Secondary; };
        _editor.Primary = _colors.Primary;
        _editor.Secondary = _colors.Secondary;
        _model = new DarkComboBox { Width = this.Px(214), Margin = new Padding(this.Px(6), this.Px(2), 0, 0), AccessibleName = "Model the texture is drawn on" };
        _model.SelectedIndexChanged += (_, _) => ChooseModel();
        var grid = new DarkCheckBox("Pixel grid", true) { Margin = new Padding(this.Px(6), this.Px(4), 0, 0) };
        grid.CheckedChanged += (_, _) => { _editor.ShowGrid = grid.Checked; _editor.NotifyState(); };
        _parts = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = C.BG2, Margin = new Padding(this.Px(2), 0, 0, 0) };
        _info = new Label { AutoSize = true, MaximumSize = new Size(this.Px(220), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(6), this.Px(3), 0, 0) };
        stack.Controls.Add(Header("PACK"));
        stack.Controls.AddRange([_name, _description]);
        stack.Controls.Add(Header("COLOURS"));
        stack.Controls.Add(_colors);
        stack.Controls.Add(Header("MODEL"));
        stack.Controls.AddRange([_model, grid]);
        stack.Controls.Add(Header("PARTS SHOWN"));
        stack.Controls.Add(_parts);
        stack.Controls.Add(Header("TEXTURE"));
        stack.Controls.Add(_info);
        side.Controls.Add(stack);

        // ---- the model and the flat texture (centre), status (bottom)
        _view3d = new MobModelView(_editor, () => Mesh, () => ModelCaption()) { Dock = DockStyle.Fill };
        _view2d = new MobTextureView(_editor, () => Mesh) { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = C.BG3, SplitterWidth = this.Px(4) };
        split.Panel1.Controls.Add(_view3d);
        split.Panel2.Controls.Add(_view2d);
        bool splitPlaced = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitPlaced || split.Width < this.Px(400)) return;
            splitPlaced = true;
            split.SplitterDistance = split.Width / 2;
        };
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = this.Px(26), BackColor = C.BG3 };
        _status = new Label { Dock = DockStyle.Fill, ForeColor = C.FG, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(this.Px(8), 0, 0, 0), AutoEllipsis = true };
        var hint = new Label
        {
            Dock = DockStyle.Right, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(5), this.Px(8), 0),
            Text = "3D: left paints, Ctrl+left second colour, right-drag turns, wheel zooms  ·  flat: left/right paint, middle-drag moves",
        };
        statusBar.Controls.Add(_status);
        statusBar.Controls.Add(hint);

        Controls.Add(split);
        Controls.Add(side);
        Controls.Add(_list);
        Controls.Add(_paint.Tools);
        Controls.Add(statusBar);
        Controls.Add(_paint.Options);
        Controls.Add(bar);
        _view2d.Dropped += _paint.HandleDrop;
        _view3d.Dropped += _paint.HandleDrop;

        _editor.StateChanged += UpdateState;
        _editor.SelectionChanged += OnSelected;
        _editor.HoverChanged += UpdateStatus;
        _editor.ColorPicked += (c, secondary) => { if (secondary) _colors.Secondary = c; else _colors.Primary = c; _colors.AddRecent(c); };
        _editor.ColorUsed += c => _colors.AddRecent(c);
        _editor.Message += text => Main?.SetStatus(text);
        // Start on the pack's first mob, or a cow.
        var first = pack.Textures.Keys.Where(MobCatalog.IsMobKey).OrderBy(Label, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            ?? new[] { "entity/cow/cow_v2", "entity/cow/cow", "entity/cow/cow_temperate" }.FirstOrDefault(k => _editor.Texture(k) != null)
            ?? _editor.AllKeys().FirstOrDefault(MobCatalog.IsMobKey);
        _editor.Select(first);
        _paint.SetTool(PaintTool.Pencil);
        OnSelected();
    }

    /// <summary>What the list calls a texture: the mob and its variant, and when it has no model to show.</summary>
    string Label(string key)
    {
        var m = _catalog.Describe(key);
        return m.Display + (m.GeometryId == null ? "  (flat)" : "");
    }

    // ------------------------------------------------------------------ the model

    string? Chosen(string? key) => key != null && _chosen.TryGetValue(key, out var c) ? c : null;

    /// <summary>The mesh of the selected texture's model (with the parts shown), or null when it has none.</summary>
    ModelMesh? Mesh
    {
        get
        {
            string? key = _editor.Selected;
            string sig = $"{key}|{Chosen(key)}|{string.Join(",", _hidden.Order())}";
            if (sig == _meshFor) return _mesh;
            _meshFor = sig;
            _mesh = null;
            if (key == null) return null;
            var chosen = Chosen(key);
            var model = chosen == NoModel ? null : _catalog.ModelFor(key, chosen);
            if (model != null) _mesh = ModelMesh.Build(model, _hidden, chosen == null ? _catalog.PoseFor(key) : null);
            return _mesh;
        }
    }

    string ModelCaption()
    {
        var key = _editor.Selected;
        var model = key == null ? null : Chosen(key) is { } c && c != NoModel ? _catalog.Models.GetValueOrDefault(c) : _catalog.ModelFor(key);
        return model == null ? "" : $"{model.Id}  ·  {model.CubeCount} boxes";
    }

    void OnSelected()
    {
        _hidden.Clear();
        _meshFor = null;
        FillModels();
        FillParts();
        _view3d.ModelChanged();
        UpdateState();
    }

    void FillModels()
    {
        _updating = true;
        _model.Items.Clear();
        var key = _editor.Selected;
        string auto = key != null && _catalog.Describe(key).GeometryId is { } g ? g : "none";
        _model.Items.Add(new ComboItem($"Automatic ({auto})", null, null));
        _model.Items.Add(new ComboItem("None: paint flat only", null, NoModel));
        foreach (var id in _catalog.Models.Keys.Where(k => !k.Contains('@')).Order(StringComparer.OrdinalIgnoreCase)) _model.Items.Add(new ComboItem(id, null, id));
        var chosen = Chosen(key);
        int index = chosen == null ? 0 : chosen == NoModel ? 1 : Math.Max(0, _model.Items.Cast<ComboItem>().ToList().FindIndex(i => i.Tag as string == chosen));
        _model.SelectedIndex = index;
        _model.Enabled = key != null;
        _updating = false;
    }

    void ChooseModel()
    {
        if (_updating || _editor.Selected is not { } key || _model.SelectedItem is not ComboItem item) return;
        if (item.Tag is string id) _chosen[key] = id; else _chosen.Remove(key);
        _hidden.Clear();
        _meshFor = null;
        FillParts();
        _view3d.ModelChanged();
        _view2d.Invalidate();
    }

    void FillParts()
    {
        _parts.SuspendLayout();
        foreach (Control c in _parts.Controls) c.Dispose();
        _parts.Controls.Clear();
        var key = _editor.Selected;
        var model = key == null ? null : Chosen(key) == NoModel ? null : _catalog.ModelFor(key, Chosen(key));
        if (model != null)
            foreach (var bone in model.Bones.Where(b => b.Cubes.Count > 0 && !b.NeverRender))
            {
                var box = new DarkCheckBox(bone.Name, !_hidden.Contains(bone.Name)) { Margin = new Padding(this.Px(6), this.Px(1), 0, 0) };
                string name = bone.Name;
                box.CheckedChanged += (_, _) =>
                {
                    if (box.Checked) _hidden.Remove(name); else _hidden.Add(name);
                    _meshFor = null;
                    _view3d.Redraw();
                    _view2d.Invalidate();
                };
                _parts.Controls.Add(box);
            }
        else _parts.Controls.Add(new Label { Text = "No model.", AutoSize = true, ForeColor = C.FG_FAINT, Margin = new Padding(this.Px(6), this.Px(2), 0, 0) });
        _parts.ResumeLayout();
    }

    // ------------------------------------------------------------------ state

    void UpdateState()
    {
        _updating = true;
        _undo.Enabled = _editor.CanUndo || _editor.Session.IsLive;
        _redo.Enabled = _editor.CanRedo;
        _undo.ToolTipText = _editor.UndoName is { } u ? $"Undo {u} (Ctrl+Z)" : "Nothing to undo";
        _redo.ToolTipText = _editor.RedoName is { } r ? $"Redo {r} (Ctrl+Y)" : "Nothing to redo";
        _info.Text = Describe();
        _updating = false;
        UpdateStatus();
        OnStateChanged();
    }

    string Describe()
    {
        if (_editor.Selected is not { } key || _editor.Current is not { } t) return "";
        var m = _catalog.Describe(key);
        var lines = new List<string>
        {
            $"{m.Display}: {t.Image.Width} × {t.Image.Height}",
            _editor.InPack(key) ? "In this pack." : $"The game's own texture ({_catalog.Source}): painting on it adds it to the pack.",
            $"Java: assets/minecraft/textures/{key}.png",
            $"Bedrock: textures/{key}.png",
        };
        if (m.EntityId != null) lines.Add("Entity: " + m.EntityId);
        if (_catalog.OtherEdition is { } other && !other.Contains(key)) lines.Add($"The other edition has no texture of this name: it only shows in {(_catalog.Edition == PackEdition.Java ? "Java" : "Bedrock")}.");
        return string.Join("\n", lines);
    }

    void UpdateStatus()
    {
        if (_editor.Current is not { } t || _editor.Hover is not { } h)
        {
            _status.Text = $"{_editor.Pack.Textures.Count(k => MobCatalog.IsMobKey(k.Key))} mob texture(s) in the pack  ·  started from {_catalog.Source}";
            return;
        }
        var r = TextureEditor.FrameRect(t, _editor.Frame);
        uint c = t.Image[r.X + h.X, r.Y + h.Y];
        string where = "";
        if (Mesh is { } mesh)
        {
            float sx = t.Image.Width / (float)mesh.Model.TextureWidth, sy = t.Image.Height / (float)mesh.Model.TextureHeight;
            var face = mesh.Quads.FirstOrDefault(q =>
            {
                var (x, y, w, hh) = q.TexRect;
                return h.X + 0.5f >= x * sx && h.X + 0.5f < (x + w) * sx && h.Y + 0.5f >= y * sy && h.Y + 0.5f < (y + hh) * sy;
            });
            where = face != null ? $"  ·  {face.Bone}, {face.Face} face" : "  ·  not used by the model";
        }
        _status.Text = $"{h.X}, {h.Y}{where}  ·  {PaintOps.ToHex(c)}{(RgbaImage.A(c) == 0 ? " (see-through)" : "")}";
    }

    // ------------------------------------------------------------------ helpers

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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Keys a focused list, box or slider uses stay with it (StudioKeys); the rest are the studio's shortcuts.
        if (!StudioKeys.UsesKey(StudioKeys.Focused(this), keyData))
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: _editor.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: _editor.Redo(); return true;
            }
            if (_paint.HandleKey(keyData)) return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveAs(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public override void OnActivated() => _view3d.Focus();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _paint.Dispose();
        base.Dispose(disposing);
    }
}
