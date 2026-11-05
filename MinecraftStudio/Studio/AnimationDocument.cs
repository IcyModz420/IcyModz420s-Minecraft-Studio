// MinecraftStudio/Studio/AnimationDocument.cs: Animation Studio: see every mob of Bedrock Edition (Windows PC) and Java
// Edition (PC) move, and design them — their animations (keyframes, formulas, timing) and their models (bones, boxes).
// Owns: AnimationDocument — the window: the mob list, the moving 3D mob with play controls and the timeline, and the
// panels (the mob's animations, the chosen bone's keys, the model, the preview's speed). One project per mob; the pack
// saved holds every mob changed. Keys and model editing are in AnimationDocument.Keys.cs; files in .Files.cs.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

sealed partial class AnimationDocument : Document
{
    /// <summary>A mob in the list: its entity, name, the texture and model it is shown with, and its other textures for that model.</summary>
    sealed record MobEntry(string EntityId, string Name, string Texture, string ModelId, List<string> Textures);

    readonly MobCatalog _catalog;
    readonly List<MobEntry> _mobs;
    readonly Dictionary<string, AnimationProject> _projects = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Animations and models a pack that was opened changed (they stay in the pack when it is saved again).</summary>
    readonly HashSet<string> _fromPack = new(StringComparer.OrdinalIgnoreCase);
    MobEntry? _mob;
    string? _texture;
    AnimationProject? _project;
    string? _animationId;
    string? _bone;
    double _time;
    bool _playing;
    DateTime _lastTick;
    ModelMesh? _mesh;
    string? _path;
    string _packName;
    bool _updating;

    readonly DarkTextField _find;
    readonly ListBox _mobList, _animList;
    readonly AnimatedModelView _view;
    readonly AnimationTimeline _timeline;
    readonly Button _play;
    readonly Label _timeLabel, _status, _animInfo;
    readonly DarkCheckBox _withSetup, _loopPreview;
    readonly DarkSlider _moveSpeed, _rate;
    readonly DarkComboBox _loopMode, _variant;
    readonly DarkTextField _length;
    readonly ToolStripButton _undo, _redo;
    readonly System.Windows.Forms.Timer _timer;

    public AnimationDocument(MobCatalog catalog, string packName, string? path = null, IEnumerable<string>? fromPack = null)
    {
        _catalog = catalog;
        _packName = packName;
        _path = path;
        if (fromPack != null) _fromPack.UnionWith(fromPack);
        _mobs = Mobs(catalog);

        // ---- tool bar
        var bar = Theme.ToolStrip();
        bar.Dock = DockStyle.Top;
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        var newMenu = DropDown("New", Glyphs.Add, "Design mobs starting from a game installed on this PC");
        newMenu.DropDownOpening += (_, _) => FillNewMenu(newMenu);
        newMenu.DropDownItems.Add(new ToolStripMenuItem("…") { Enabled = false });
        var export = DropDown("Export", Glyphs.Export, "Save the pack for Bedrock");
        export.DropDownItems.Add(MenuItem("Bedrock resource pack (.mcpack)…", (_, _) => Export(install: false)));
        var install = DropDown("Install", Glyphs.Game, "Put the pack into Minecraft Bedrock, or into a world");
        install.DropDownItems.Add(MenuItem("Into Minecraft Bedrock…", (_, _) => Export(install: true)));
        install.DropDownItems.Add(MenuItem("Into a world on this PC…", async (_, _) => await IntoWorld()));
        _undo = Button("Undo", Glyphs.Undo, "Undo (Ctrl+Z)", (_, _) => { _project?.Undo(); });
        _redo = Button("Redo", Glyphs.Redo, "Redo (Ctrl+Y)", (_, _) => { _project?.Redo(); });
        bar.Items.AddRange(
        [
            newMenu,
            Button("Open", Glyphs.Open, "Open a Bedrock pack with animations or models (its changes are shown, and saved with it again)", (_, _) => OpenDialog(Main)),
            Button("Save", Glyphs.Save, "Save the pack (Ctrl+S)", async (_, _) => await SaveAsync()),
            Button("Save As", Glyphs.SaveAs, "Save the pack under a new name", (_, _) => SaveAs()),
            new ToolStripSeparator(), _undo, _redo, new ToolStripSeparator(), export, install,
        ]);

        // ---- mob list (left)
        var left = new Panel { Dock = DockStyle.Left, Width = this.Px(250), BackColor = C.BG2 };
        _find = new DarkTextField { Dock = DockStyle.Top, Height = this.Px(30), Padding = new Padding(this.Px(8), this.Px(6), this.Px(6), 0) };
        _find.Box.PlaceholderText = "Find a mob…";
        _find.Box.AccessibleName = "Find a mob";
        _mobList = new ListBox { Dock = DockStyle.Fill, BackColor = C.BG2, ForeColor = C.FG, BorderStyle = BorderStyle.None, IntegralHeight = false, Font = Theme.Ui(9.5f), AccessibleName = "Mobs" };
        Theme.DarkControl(_mobList);
        _find.Box.TextChanged += (_, _) => FillMobs();
        _mobList.SelectedIndexChanged += (_, _) => { if (!_updating && _mobList.SelectedItem is MobItem m) ChooseMob(m.Entry); };
        left.Controls.Add(_mobList);
        left.Controls.Add(_find);

        // ---- side panel (right)
        var side = new Panel { Dock = DockStyle.Right, Width = this.Px(300), BackColor = C.BG2, AutoScroll = true };
        Theme.DarkControl(side);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = C.BG2, Padding = new Padding(this.Px(4), this.Px(6), 0, this.Px(10)) };
        _sideWidth = this.Px(276);
        stack.Controls.Add(Header("ANIMATIONS"));
        _animList = new ListBox { Width = _sideWidth, Height = this.Px(150), BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, Margin = Indent(), AccessibleName = "The mob's animations" };
        Theme.DarkControl(_animList);
        _animList.SelectedIndexChanged += (_, _) => { if (!_updating && _animList.SelectedItem is AnimItem a) ChooseAnimation(a.Id); };
        stack.Controls.Add(_animList);
        stack.Controls.Add(Row(SmallButton("New", NewAnimation), SmallButton("Copy", CopyAnimation), SmallButton("Rename", RenameAnimation), SmallButton("Remove", RemoveAnimation)));
        _loopMode = new DarkComboBox { Width = this.Px(150), AccessibleName = "How the animation plays" };
        _loopMode.Items.AddRange([new ComboItem("Loops"), new ComboItem("Plays once"), new ComboItem("Holds the last frame")]);
        _loopMode.SelectedIndexChanged += (_, _) => SetLoop();
        _length = Field("Length in seconds", this.Px(70));
        _length.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { SetLength(); e.SuppressKeyPress = true; } };
        _length.Box.Leave += (_, _) => SetLength();
        stack.Controls.Add(Row(_loopMode, Caption("length"), _length, Caption("s")));
        _animInfo = Note("");
        stack.Controls.Add(_animInfo);
        BuildKeyPanel(stack);
        BuildModelPanel(stack);
        stack.Controls.Add(Header("PREVIEW"));
        _variant = new DarkComboBox { Width = _sideWidth, Margin = Indent(), AccessibleName = "Texture shown" };
        _variant.SelectedIndexChanged += (_, _) => { if (!_updating && _variant.SelectedItem is ComboItem { Tag: string key }) { _texture = key; _view?.Redraw(); } };
        stack.Controls.Add(_variant);
        _moveSpeed = new DarkSlider("Walking speed (query.modified_move_speed)", 0, 100, 100, "%") { Width = _sideWidth, Margin = Indent() };
        _moveSpeed.ValueChanged += (_, _) => UpdateView();
        _rate = new DarkSlider("Playback speed", 10, 300, 100, "%") { Width = _sideWidth, Margin = Indent() };
        _withSetup = new DarkCheckBox("Play the mob's setup pose under it", true) { Margin = Indent() };
        _withSetup.CheckedChanged += (_, _) => UpdateView();
        _loopPreview = new DarkCheckBox("Keep playing (loop the preview)", true) { Margin = Indent() };
        stack.Controls.AddRange([_moveSpeed, _rate, _withSetup, _loopPreview]);
        if (catalog.Edition == PackEdition.Java)
            stack.Controls.Add(Note("Java Edition draws and moves its mobs in the game's own code, so a pack cannot change them there. Here Java's textures are shown on Bedrock's models and animations, and what you make is saved for Bedrock.", C.ORANGE));
        side.Controls.Add(stack);

        // ---- centre: the mob, the play bar, the timeline
        _view = new AnimatedModelView(() => _mesh, Texture, () => _bone, Caption) { Dock = DockStyle.Fill };
        _view.BonePicked += bone => ChooseBone(bone);
        var playBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = C.BG3, Padding = new Padding(this.Px(6), this.Px(3), 0, this.Px(3)) };
        _play = Theme.Button("▶ Play", primary: true);
        _play.Click += (_, _) => TogglePlay();
        var start = Theme.Button("⏮");
        start.Click += (_, _) => SetTime(0);
        var stepBack = Theme.Button("◀ 1 tick");
        stepBack.Click += (_, _) => SetTime(_time - 0.05);
        var stepOn = Theme.Button("1 tick ▶");
        stepOn.Click += (_, _) => SetTime(_time + 0.05);
        _timeLabel = new Label { AutoSize = true, ForeColor = C.FG, UseMnemonic = false, Margin = new Padding(this.Px(10), this.Px(8), 0, 0), Font = Theme.Mono(9.5f) };
        playBar.Controls.AddRange([start, stepBack, _play, stepOn, _timeLabel]);
        _timeline = new AnimationTimeline { Dock = DockStyle.Bottom, Height = this.Px(190) };
        _timeline.TimeChanged += t => SetTime(t);
        _timeline.BoneClicked += b => ChooseBone(b);
        _timeline.KeyClicked += (b, ch, t) => { ChooseBone(b); SetTime(t); _keyChannel = ch; FillKeyFields(); };
        _timeline.KeyMoved += MoveKey;
        _timeline.KeyDeleted += (b, ch, t) => DeleteKey(b, ch, t);
        _timeline.KeyLerpChanged += SetLerp;
        var centre = new Panel { Dock = DockStyle.Fill, BackColor = C.BG };
        centre.Controls.Add(_view);
        centre.Controls.Add(playBar);
        centre.Controls.Add(_timeline);

        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = this.Px(26), BackColor = C.BG3 };
        _status = new Label { Dock = DockStyle.Fill, ForeColor = C.FG, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(this.Px(8), 0, 0, 0), AutoEllipsis = true };
        statusBar.Controls.Add(_status);
        Controls.Add(centre);
        Controls.Add(side);
        Controls.Add(left);
        Controls.Add(statusBar);
        Controls.Add(bar);

        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) => Tick();
        FillMobs();
        var first = _mobs.FirstOrDefault(m => m.EntityId.EndsWith(":cow", StringComparison.OrdinalIgnoreCase)) ?? _mobs.FirstOrDefault();
        if (first != null) ChooseMob(first);
        UpdateState();
    }

    // ------------------------------------------------------------------ the mobs

    sealed record MobItem(MobEntry Entry, string Text) { public override string ToString() => Text; }
    sealed record AnimItem(string Name, string Id, bool Changed) { public override string ToString() => (Changed ? "● " : "") + Name; }

    /// <summary>Every entity with a model and animations: one entry each, shown with its default texture.</summary>
    static List<MobEntry> Mobs(MobCatalog catalog)
    {
        var list = new List<MobEntry>();
        foreach (var group in catalog.Info.Values.Where(i => i.EntityId != null && i.GeometryId != null && catalog.ModelFor(i.Key) != null).GroupBy(i => i.EntityId!, StringComparer.OrdinalIgnoreCase))
        {
            var main = group.OrderBy(i => i.Variant.Length == 0 ? 0 : 1).ThenBy(i => i.Key.Contains("baby") ? 1 : 0).ThenBy(i => i.Key.Length).First();
            var textures = group.Where(i => i.GeometryId == main.GeometryId).Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            list.Add(new MobEntry(group.Key, main.Mob, main.Key, main.GeometryId!, textures));
        }
        return list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.EntityId, StringComparer.Ordinal).ToList();
    }

    void FillMobs()
    {
        string f = _find.Text.Trim();
        _updating = true;
        _mobList.BeginUpdate();
        _mobList.Items.Clear();
        foreach (var m in _mobs.Where(m => f.Length == 0 || m.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || m.EntityId.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            bool changed = _projects.TryGetValue(m.EntityId, out var p) && (p.ChangedAnimations.Count > 0 || p.ModelChanged);
            var item = new MobItem(m, (changed ? "● " : "") + m.Name);
            _mobList.Items.Add(item);
            if (m == _mob) _mobList.SelectedItem = item;
        }
        _mobList.EndUpdate();
        _updating = false;
    }

    void ChooseMob(MobEntry m)
    {
        if (_playing) TogglePlay();
        _mob = m;
        _texture = m.Texture;
        if (!_projects.TryGetValue(m.EntityId, out var project))
        {
            string shortName = m.EntityId[(m.EntityId.IndexOf(':') + 1)..];
            project = new AnimationProject(shortName, _catalog.Models.GetValueOrDefault(m.ModelId), _catalog.Animations, _catalog.AnimationsFor(m.Texture));
            project.MarkFromPack(project.Animations.Keys.Where(_fromPack.Contains), _fromPack.Contains(m.ModelId));
            project.Changed += OnProjectChanged;
            _projects[m.EntityId] = project;
        }
        _project = project;
        _bone = null;
        _keyChannel = "rotation";
        _updating = true;
        _variant.Items.Clear();
        foreach (var t in m.Textures) _variant.Items.Add(new ComboItem(_catalog.Describe(t).Display, null, t));
        _variant.SelectedIndex = Math.Max(0, m.Textures.IndexOf(m.Texture));
        _updating = false;
        FillAnimations();
        string? walk = project.Names.FirstOrDefault(n => n.Name == "walk").Id ?? project.Names.FirstOrDefault(n => n.Name != "setup").Id ?? project.Names.FirstOrDefault().Id;
        ChooseAnimation(walk);
        _view.ResetView();
        FillModelPanel();
    }

    RgbaImage? Texture() => _texture != null && _catalog.Textures.Textures.TryGetValue(_texture, out var t) ? t.Image : null;

    string Caption() => _mob == null ? "" : $"{_mob.Name}  ·  {_project?.Model?.Id}  ·  {(_animationId != null ? ShortName(_animationId) : "no animation")}";

    string ShortName(string id) => _project?.Names.FirstOrDefault(n => n.Id == id).Name ?? id;

    // ------------------------------------------------------------------ the animations

    void FillAnimations()
    {
        _updating = true;
        _animList.BeginUpdate();
        _animList.Items.Clear();
        if (_project != null)
            foreach (var (name, id) in _project.Names)
            {
                var item = new AnimItem(name, id, _project.ChangedAnimations.Contains(id));
                _animList.Items.Add(item);
                if (id == _animationId) _animList.SelectedItem = item;
            }
        _animList.EndUpdate();
        _updating = false;
    }

    EntityAnimation? Current => _animationId != null && _project != null && _project.Animations.TryGetValue(_animationId, out var a) ? a : null;
    string? SetupId => _project?.Names.FirstOrDefault(n => n.Name == "setup").Id;

    void ChooseAnimation(string? id)
    {
        _animationId = id;
        _key = null;
        SetTime(0);
        FillAnimations();
        FillAnimationFields();
        UpdateTimeline();
    }

    void FillAnimationFields()
    {
        _updating = true;
        var a = Current;
        _loopMode.Enabled = _length.Enabled = a != null;
        _loopMode.SelectedIndex = a == null ? -1 : a.Loops ? 0 : a.Holds ? 2 : 1;
        _length.Text = a?.Length is double l ? EntityAnimations.Format(l) : "";
        _length.Box.PlaceholderText = a == null ? "" : EntityAnimations.Format(a.PlayLength);
        _animInfo.Text = a == null ? "This mob has no animations to show." :
            $"{a.Id}{(a.AnimTimeUpdate != null ? "\nIts time runs on: " + a.AnimTimeUpdate : "")}{(_project!.NewIds.Contains(a.Id) ? "\nNew: play it with /playanimation once the pack is on." : "")}";
        _updating = false;
    }

    void UpdateTimeline()
    {
        _timeline.ShowAnimation(Current, _project?.Model?.Bones.Select(b => b.Name).ToList() ?? []);
        _timeline.SelectedBone = _bone;
        _timeline.SelectedKey = _key;
        _timeline.Time = _time;
        _timeline.Invalidate();
    }

    void OnProjectChanged()
    {
        // An undo can take the animation or bone away.
        if (_animationId != null && _project != null && !_project.Animations.ContainsKey(_animationId)) _animationId = _project.Names.FirstOrDefault().Id;
        FillAnimations();
        FillAnimationFields();
        FillModelPanel();
        UpdateTimeline();
        UpdateView();
        FillMobs();
        UpdateState();
    }

    // ------------------------------------------------------------------ time and playback

    MolangContext PreviewContext()
    {
        var ctx = new MolangContext();
        double speed = _moveSpeed.Value / 100.0;
        ctx["query.modified_move_speed"] = speed;
        ctx["query.ground_speed"] = speed * 4;
        ctx["query.modified_distance_moved"] = _time * speed * 8;
        ctx["query.life_time"] = _time;
        ctx["query.is_on_ground"] = 1;
        ctx["query.is_alive"] = 1;
        ctx["query.delta_time"] = 0.05;
        return ctx;
    }

    void SetTime(double t)
    {
        double len = Current?.PlayLength ?? 1;
        _time = Math.Clamp(Math.Round(t, 4), 0, len);
        UpdateView();
        FillKeyFields();
    }

    void TogglePlay()
    {
        _playing = !_playing;
        _play.Text = _playing ? "⏸ Pause" : "▶ Play";
        _lastTick = DateTime.UtcNow;
        if (_playing) _timer.Start(); else _timer.Stop();
        if (!_playing) FillKeyFields();
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastTick).TotalSeconds * _rate.Value / 100.0;
        _lastTick = now;
        if (Current is not { } a) return;
        double t = _time + dt, len = a.PlayLength;
        if (t > len)
        {
            if (a.Loops || _loopPreview.Checked) t %= len;
            else { t = len; TogglePlay(); }
        }
        _time = t;
        UpdateView();
    }

    /// <summary>The mob in the pose of the current time.</summary>
    void UpdateView()
    {
        if (_project?.Model is not { } model) { _mesh = null; _view.Redraw(); return; }
        var ctx = PreviewContext();
        var poses = new List<Dictionary<string, BonePose>>();
        if (_withSetup.Checked && SetupId is { } setup && setup != _animationId && _project.Animations.TryGetValue(setup, out var sa)) poses.Add(AnimationPlayer.Pose(sa, 0, ctx, model));
        if (Current is { } a) poses.Add(AnimationPlayer.Pose(a, _time, ctx, model));
        _mesh = ModelMesh.Build(model, null, null, AnimationPlayer.Combine(poses));
        _timeline.Time = _time;
        _timeline.Invalidate();
        _timeLabel.Text = $"{_time:0.00}s / {(Current?.PlayLength ?? 0):0.00}s  ·  tick {(int)Math.Round(_time * 20)}";
        _view.Redraw();
    }

    void UpdateState()
    {
        _undo.Enabled = _project?.CanUndo == true;
        _redo.Enabled = _project?.CanRedo == true;
        _undo.ToolTipText = _project?.UndoName is { } u ? $"Undo {u} (Ctrl+Z)" : "Nothing to undo";
        _redo.ToolTipText = _project?.RedoName is { } r ? $"Redo {r} (Ctrl+Y)" : "Nothing to redo";
        int mobs = _projects.Values.Count(p => p.ChangedAnimations.Count > 0 || p.ModelChanged);
        _status.Text = mobs == 0 ? $"{_mobs.Count} mobs of {_catalog.Source}: choose one, pick an animation and play it." : $"{mobs} mob(s) changed  ·  {_packName}";
        OnStateChanged();
    }

    // ------------------------------------------------------------------ small helpers

    int _sideWidth;
    Padding Indent() => new(this.Px(6), this.Px(3), 0, this.Px(2));
    SectionHeader Header(string text) => new(text) { Width = _sideWidth, Height = this.Px(24), Margin = new Padding(this.Px(6), this.Px(10), 0, this.Px(2)), BackColor = C.BG2 };
    Label Note(string text, Color? color = null) => new() { Text = text, AutoSize = true, MaximumSize = new Size(_sideWidth, 0), ForeColor = color ?? C.FG_DIM, UseMnemonic = false, Margin = Indent() };
    Label Caption(string text) => new() { Text = text, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(4), this.Px(7), this.Px(2), 0) };
    DarkTextField Field(string name, int width) => new() { Width = width, Height = this.Px(28), Margin = new Padding(0, this.Px(1), this.Px(2), 0), AccessibleName = name };

    FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = C.BG2, Margin = Indent() };
        row.Controls.AddRange(controls);
        return row;
    }

    Button SmallButton(string text, Action click)
    {
        var b = Theme.Button(text);
        b.Margin = new Padding(0, 0, this.Px(6), 0);
        b.Click += (_, _) => click();
        return b;
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var focused = ActiveControl;
        while (focused is ContainerControl cc && cc.ActiveControl != null) focused = cc.ActiveControl;
        if (focused is not TextBoxBase)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: _project?.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: _project?.Redo(); return true;
                case Keys.Space: TogglePlay(); return true;
                case Keys.Left: SetTime(_time - 0.05); return true;
                case Keys.Right: SetTime(_time + 0.05); return true;
                case Keys.Home: SetTime(0); return true;
                case Keys.K: KeyIt(_keyChannel); return true;
                case Keys.Delete: if (_bone != null) DeleteKey(_bone, _keyChannel, _time); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public override void OnActivated() => _view.Focus();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            foreach (var p in _projects.Values) p.Changed -= OnProjectChanged;
        }
        base.Dispose(disposing);
    }
}
