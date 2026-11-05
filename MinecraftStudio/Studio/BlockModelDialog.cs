// MinecraftStudio/Studio/BlockModelDialog.cs: the Texture Studio's model window — a Java block or item model in 3D with
// the pack's textures, its boxes, faces and texture names changed, and saved into the pack.
// Owns: BlockModelDialog — the model (typed or picked; those that use the selected texture listed first), the 3D view
// (redrawn as textures are painted in the studio), the boxes (add, copy, remove; from/to in sixteenths, a turn about one
// axis with the game's angles, rescale), each side's face (on or off, the texture variable, the part of the texture,
// the turn) and the texture variables ("all = block/stone", one a line). A model that takes its boxes from its parent
// gets its own copy when one is changed (said). "Save into the pack" checks every box the way the game does, then puts
// the model in the pack (assets/…/models/…json, kept when the pack is saved for Java); "Use the game's model" takes the
// pack's out again. Not modal: paint in the studio and watch the block.
using System.Globalization;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

sealed class BlockModelDialog : DarkDialog
{
    readonly TextureDocument _doc;
    readonly TextureEditor _editor;
    readonly JavaModelLibrary _lib;
    readonly DarkComboBox _modelBox, _axis, _angle, _side, _turn;
    readonly ListBox _uses, _elements;
    readonly DarkTextField _fx, _fy, _fz, _tx, _ty, _tz, _ox, _oy, _oz, _faceTexture, _u1, _v1, _u2, _v2;
    readonly DarkCheckBox _rescale, _hasFace;
    readonly TextBox _textures;
    readonly AnimatedModelView _view;
    readonly Label _status, _from;
    readonly Button _save, _revert, _add, _copy, _remove;
    JavaBlockModel? _model;
    ModelMesh? _mesh;
    RgbaImage? _atlas;
    bool _filling, _changed;

    static readonly string[] Angles = ["-45", "-22.5", "0", "22.5", "45"];

    public BlockModelDialog(TextureDocument doc, TextureEditor editor, JavaModelLibrary lib, string? start) : base("Block and item models (Java)", 1120)
    {
        _doc = doc;
        _editor = editor;
        _lib = lib;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        ShowInTaskbar = true;
        ClientSize = new Size(this.Px(1120), this.Px(760));
        MinimumSize = new Size(this.Px(860), this.Px(600));
        Padding = new Padding(this.Px(10));

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, Width = this.Px(380), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = C.BG2 };
        SectionHeader Header(string text) => new(text) { Width = this.Px(350), Height = this.Px(22), Margin = new Padding(0, this.Px(8), 0, this.Px(2)), BackColor = C.BG2 };
        left.Controls.Add(Header("MODEL"));
        _modelBox = new DarkComboBox { Width = this.Px(350), DropDownStyle = ComboBoxStyle.DropDown, AccessibleName = "Model" };
        var ids = lib.Ids.ToArray();
        var names = new AutoCompleteStringCollection();
        names.AddRange(ids);
        _modelBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _modelBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _modelBox.AutoCompleteCustomSource = names;
        _modelBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Open(_modelBox.Text.Trim()); } };
        _modelBox.SelectionChangeCommitted += (_, _) => { if (_modelBox.SelectedItem is string s) Open(s); };
        left.Controls.Add(_modelBox);
        _uses = List();
        _uses.Width = this.Px(350);
        _uses.Height = this.Px(92);
        _uses.AccessibleName = "Models that use the selected texture";
        _uses.SelectedIndexChanged += (_, _) => { if (!_filling && _uses.SelectedItem is string s) Open(s); };
        left.Controls.Add(Note("Models that use the texture chosen in the studio:"));
        left.Controls.Add(_uses);
        _from = Note("", 350);
        left.Controls.Add(_from);

        left.Controls.Add(Header("BOXES"));
        _elements = List();
        _elements.Width = this.Px(350);
        _elements.Height = this.Px(96);
        _elements.AccessibleName = "Boxes";
        _elements.SelectedIndexChanged += (_, _) => { if (!_filling) FillElement(); };
        left.Controls.Add(_elements);
        _add = Small("Add", () => Edit(m => { m.Elements!.Add(new JavaModelElement { Faces = { } }); foreach (var s in JavaModelElement.Sides) m.Elements[^1].Faces[s] = new JavaModelFace { Texture = FirstVariable() }; return m.Elements.Count - 1; }));
        _copy = Small("Copy", () => Edit(m => { if (Selected(m) is not { } e) return -1; m.Elements!.Insert(_elements.SelectedIndex + 1, e.Clone()); return _elements.SelectedIndex + 1; }));
        _remove = Small("Remove", () => Edit(m => { if (Selected(m) == null) return -1; m.Elements!.RemoveAt(_elements.SelectedIndex); return Math.Min(_elements.SelectedIndex, m.Elements.Count - 1); }));
        left.Controls.Add(Row(_add, _copy, _remove));
        _fx = Num(); _fy = Num(); _fz = Num(); _tx = Num(); _ty = Num(); _tz = Num();
        left.Controls.Add(Row(Note("From x y z", 90), _fx, _fy, _fz));
        left.Controls.Add(Row(Note("To x y z", 90), _tx, _ty, _tz));
        _axis = Combo(70, "no turn", "x", "y", "z");
        _angle = Combo(70, Angles);
        _ox = Num(); _oy = Num(); _oz = Num();
        left.Controls.Add(Row(Note("Turned", 90), _axis, _angle, Note("°")));
        left.Controls.Add(Row(Note("Around x y z", 90), _ox, _oy, _oz));
        _rescale = new DarkCheckBox("Rescale (stays a block wide)", false) { AutoSize = true, Margin = new Padding(this.Px(90), this.Px(2), 0, 0) };
        left.Controls.Add(_rescale);

        left.Controls.Add(Header("FACE"));
        _side = Combo(110, JavaModelElement.Sides);
        _side.SelectedIndex = 3;
        _hasFace = new DarkCheckBox("This side has a face", true) { AutoSize = true, Margin = new Padding(this.Px(8), this.Px(4), 0, 0) };
        left.Controls.Add(Row(_side, _hasFace));
        _faceTexture = new DarkTextField { Width = this.Px(120), Height = this.Px(28), AccessibleName = "Face texture" };
        _turn = Combo(70, "0", "90", "180", "270");
        left.Controls.Add(Row(Note("Texture", 90), _faceTexture, Note("turned"), _turn));
        _u1 = Num(); _v1 = Num(); _u2 = Num(); _v2 = Num();
        left.Controls.Add(Row(Note("Part u v u v", 90), _u1, _v1, _u2, _v2));
        left.Controls.Add(Note("The part of the texture in sixteenths (0 0 16 16: all of it); empty: the game's own for the box.", 350));

        left.Controls.Add(Header("TEXTURES"));
        _textures = new TextBox { Multiline = true, Width = this.Px(350), Height = this.Px(96), ScrollBars = ScrollBars.Vertical, BackColor = C.BG3, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Texture variables", Font = Theme.Mono(9f) };
        left.Controls.Add(_textures);
        left.Controls.Add(Note("One a line: name = texture (all = block/stone), or = #other. The model's own; its parents add theirs.", 350));

        // The chosen box is tinted when there are others to tell it from (a one-box block keeps its true colours).
        _view = new AnimatedModelView(() => _mesh, () => _atlas, () => _elements.SelectedIndex >= 0 && _elements.Items.Count > 1 ? "element " + (_elements.SelectedIndex + 1) : null, () => _model?.Id ?? "",
            "Choose a model on the left.") { Dock = DockStyle.Fill };
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = this.Px(40), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _save = Theme.Button("Save into the pack", true);
        _save.MinimumSize = new Size(this.Px(160), this.Px(30));
        _save.Click += (_, _) => Save();
        _revert = Theme.Button("Use the game's model");
        _revert.MinimumSize = new Size(this.Px(160), this.Px(30));
        _revert.Click += (_, _) => Revert();
        buttons.Controls.AddRange([_save, _revert]);
        Controls.Add(_view);
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = this.Px(10) });
        Controls.Add(left);
        Controls.Add(_status);
        // Docked last, so it fills what the buttons, the status line and the left side leave.
        _view.BringToFront();

        foreach (var f in new[] { _fx, _fy, _fz, _tx, _ty, _tz, _ox, _oy, _oz }) f.Box.TextChanged += (_, _) => ElementChanged();
        foreach (var f in new[] { _u1, _v1, _u2, _v2 }) f.Box.TextChanged += (_, _) => FaceChanged();
        _faceTexture.Box.TextChanged += (_, _) => FaceChanged();
        _axis.SelectedIndexChanged += (_, _) => ElementChanged();
        _angle.SelectedIndexChanged += (_, _) => ElementChanged();
        _rescale.CheckedChanged += (_, _) => ElementChanged();
        _side.SelectedIndexChanged += (_, _) => { if (!_filling) FillFace(); };
        _hasFace.CheckedChanged += (_, _) => FaceChanged();
        _turn.SelectedIndexChanged += (_, _) => FaceChanged();
        _textures.TextChanged += (_, _) => TexturesChanged();
        _editor.ImageChanged += Repaint;
        _editor.SelectionChanged += FillUses;
        FormClosed += (_, _) => { _editor.ImageChanged -= Repaint; _editor.SelectionChanged -= FillUses; };
        if (_editor.Pack.Edition != PackEdition.Java) _save.Enabled = _revert.Enabled = false;
        FillUses();
        if (start != null) Open(start);
        else Say(lib.GameVersion == null ? "Minecraft Java isn't installed on this PC, so only the pack's own models are here." : $"The models of Java {lib.GameVersion}: type a name (block/stone, item/apple) or pick one that uses the texture.");
    }

    static Label Note(string text, int width = 0)
    {
        var l = new Label { Text = text, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false };
        l.Margin = new Padding(0, l.Px(7), l.Px(6), 0);
        if (width > 0) { l.MinimumSize = new Size(l.Px(width), 0); l.MaximumSize = new Size(l.Px(Math.Max(width, 350)), 0); }
        return l;
    }

    FlowLayoutPanel Row(params Control[] items)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.AddRange(items);
        return row;
    }

    static ListBox List() => new() { BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };

    DarkTextField Num() => new() { Width = this.Px(56), Height = this.Px(28), Margin = new Padding(0, 0, this.Px(4), 0), AccessibleName = "Number" };

    DarkComboBox Combo(int width, params string[] items)
    {
        var c = new DarkComboBox { Width = this.Px(width), Margin = new Padding(0, 0, this.Px(4), 0) };
        c.Items.AddRange(items);
        c.SelectedIndex = 0;
        return c;
    }

    Button Small(string text, Action click)
    {
        var b = Theme.Button(text);
        b.MinimumSize = new Size(this.Px(80), this.Px(28));
        b.Margin = new Padding(0, 0, this.Px(4), 0);
        b.Click += (_, _) => click();
        return b;
    }

    void Say(string text, bool warn = false) { _status.ForeColor = warn ? C.ORANGE : C.FG_DIM; _status.Text = text; }

    // ------------------------------------------------------------------ the model

    void FillUses()
    {
        _filling = true;
        _uses.Items.Clear();
        if (_editor.Selected is string shared && _doc.ModelTextureId(shared, _lib) is string id)
            foreach (var m in _lib.UsingTexture(id)) _uses.Items.Add(m);
        _filling = false;
    }

    /// <summary>Shows a model (the pack's own when it has one); a model not saved yet asks first.</summary>
    internal void Open(string id)
    {
        id = JavaModelLibrary.Normal(id);
        if (_model != null && id == _model.Id) return;
        if (_changed && Dialogs.Ask(this, $"{_model!.Id} has changes that are not in the pack yet. Leave them?") != DialogResult.Yes) return;
        var m = _lib.Get(id);
        if (m == null) { Say($"There is no model called {id}.", warn: true); return; }
        // A working copy: the library's stays as read until it is saved.
        _model = JavaBlockModel.Read(m.Id, m.Write(), m.FromPack);
        _changed = false;
        _filling = true;
        _modelBox.Text = id;
        _textures.Text = string.Join("\r\n", _model.Textures.Select(t => $"{t.Key} = {t.Value}"));
        _filling = false;
        FillElements(0);
        Redraw();
    }

    JavaModelElement? Selected(JavaBlockModel m) => m.Elements is { } list && _elements.SelectedIndex >= 0 && _elements.SelectedIndex < list.Count ? list[_elements.SelectedIndex] : null;

    // The boxes the model draws: its own, else (read only until changed) its parent's.
    List<JavaModelElement> Boxes() => _model?.Elements ?? (_model == null ? [] : _lib.Resolve(_model.Id, _model)?.Elements ?? []);

    void FillElements(int select)
    {
        _filling = true;
        _elements.Items.Clear();
        var boxes = Boxes();
        for (int i = 0; i < boxes.Count; i++)
            _elements.Items.Add($"{i + 1}:  {V(boxes[i].From)} → {V(boxes[i].To)}" + (boxes[i].Axis != null ? $"  turned {boxes[i].Angle.ToString(CultureInfo.InvariantCulture)}° about {boxes[i].Axis}" : ""));
        _elements.SelectedIndex = boxes.Count == 0 ? -1 : Math.Clamp(select, 0, boxes.Count - 1);
        var resolved = _model == null ? null : _lib.Resolve(_model.Id, _model);
        _from.Text = _model == null ? "" : (_model.FromPack ? "The pack's own model. " : $"The game's model (Java {_lib.GameVersion}). ")
            + (_model.Elements == null && resolved?.ElementsFrom is { } owner ? $"Its boxes come from {owner.Id}; changing one gives this model its own." : "")
            + (resolved?.Generated == true ? "A flat item, made from its layer0 picture." : "") + (resolved?.BuiltIn == true ? "Drawn by the game's own code (no boxes)." : "");
        _filling = false;
        foreach (var c in new Control[] { _copy, _remove }) c.Enabled = _elements.SelectedIndex >= 0;
        FillElement();
    }

    static string V(double[] v) => string.Join(" ", v.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)));

    void FillElement()
    {
        var boxes = Boxes();
        var e = _elements.SelectedIndex >= 0 && _elements.SelectedIndex < boxes.Count ? boxes[_elements.SelectedIndex] : null;
        _filling = true;
        foreach (var (f, v) in new[] { (_fx, e?.From[0]), (_fy, e?.From[1]), (_fz, e?.From[2]), (_tx, e?.To[0]), (_ty, e?.To[1]), (_tz, e?.To[2]), (_ox, e?.Origin[0]), (_oy, e?.Origin[1]), (_oz, e?.Origin[2]) })
            f.Text = v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
        _axis.SelectedIndex = e?.Axis switch { "x" => 1, "y" => 2, "z" => 3, _ => 0 };
        _angle.SelectedIndex = Math.Max(0, Array.IndexOf(Angles, (e?.Angle ?? 0).ToString(CultureInfo.InvariantCulture)));
        _rescale.Checked = e?.Rescale ?? false;
        _filling = false;
        FillFace();
        _view.Redraw();
    }

    void FillFace()
    {
        var boxes = Boxes();
        var e = _elements.SelectedIndex >= 0 && _elements.SelectedIndex < boxes.Count ? boxes[_elements.SelectedIndex] : null;
        string side = JavaModelElement.Sides[Math.Max(0, _side.SelectedIndex)];
        var f = e != null && e.Faces.TryGetValue(side, out var face) ? face : null;
        _filling = true;
        _hasFace.Checked = f != null;
        _faceTexture.Text = f?.Texture ?? "";
        _turn.SelectedIndex = (f?.Rotation ?? 0) / 90 & 3;
        foreach (var (box, i) in new[] { (_u1, 0), (_v1, 1), (_u2, 2), (_v2, 3) }) box.Text = f?.Uv?[i].ToString("0.###", CultureInfo.InvariantCulture) ?? "";
        _filling = false;
    }

    // A change to the working copy: a model that took its parent's boxes gets its own first.
    void Edit(Func<JavaBlockModel, int> change)
    {
        if (_model == null) return;
        if (_model.Elements == null) _model.Elements = Boxes().Select(b => b.Clone()).ToList();
        int select = change(_model);
        _changed = true;
        FillElements(select < 0 ? _elements.SelectedIndex : select);
        Redraw();
    }

    string FirstVariable()
    {
        var resolved = _model == null ? null : _lib.Resolve(_model.Id, _model);
        return resolved?.Textures.Keys.FirstOrDefault(k => k != "particle") is string k ? "#" + k : "#all";
    }

    static double? Number(DarkTextField f) => double.TryParse(f.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    void ElementChanged()
    {
        if (_filling || _model == null || _elements.SelectedIndex < 0) return;
        double?[] from = [Number(_fx), Number(_fy), Number(_fz)], to = [Number(_tx), Number(_ty), Number(_tz)], at = [Number(_ox), Number(_oy), Number(_oz)];
        if (from.Concat(to).Concat(at).Any(v => v == null)) { Say("Each corner and the turn's centre need three numbers (sixteenths of a block).", warn: true); return; }
        int index = _elements.SelectedIndex;
        Edit(m =>
        {
            var e = m.Elements![index];
            e.From = from.Select(v => v!.Value).ToArray();
            e.To = to.Select(v => v!.Value).ToArray();
            e.Origin = at.Select(v => v!.Value).ToArray();
            e.Axis = _axis.SelectedIndex switch { 1 => "x", 2 => "y", 3 => "z", _ => null };
            e.Angle = e.Axis == null ? 0 : double.Parse(Angles[Math.Max(0, _angle.SelectedIndex)], CultureInfo.InvariantCulture);
            e.Rescale = _rescale.Checked;
            return index;
        });
        Say(_model.Elements![index].Problem() is string p ? $"Box {index + 1}: {p}." : "");
    }

    void FaceChanged()
    {
        if (_filling || _model == null || _elements.SelectedIndex < 0) return;
        int index = _elements.SelectedIndex;
        string side = JavaModelElement.Sides[Math.Max(0, _side.SelectedIndex)];
        double?[] uv = [Number(_u1), Number(_v1), Number(_u2), Number(_v2)];
        bool noUv = new[] { _u1, _v1, _u2, _v2 }.All(b => b.Text.Trim().Length == 0);
        if (!noUv && uv.Any(v => v == null)) { Say("The part of the texture needs four numbers (or none, for the game's own).", warn: true); return; }
        Edit(m =>
        {
            var e = m.Elements![index];
            if (!_hasFace.Checked) { e.Faces.Remove(side); return index; }
            if (!e.Faces.TryGetValue(side, out var f)) e.Faces[side] = f = new JavaModelFace { Texture = FirstVariable() };
            if (_faceTexture.Text.Trim().Length > 0) f.Texture = _faceTexture.Text.Trim();
            f.Rotation = Math.Max(0, _turn.SelectedIndex) * 90;
            f.Uv = noUv ? null : uv.Select(v => v!.Value).ToArray();
            return index;
        });
        _filling = true;
        if (_faceTexture.Text.Trim().Length == 0 && _model.Elements![index].Faces.TryGetValue(side, out var now)) _faceTexture.Text = now.Texture;
        _filling = false;
    }

    void TexturesChanged()
    {
        if (_filling || _model == null) return;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in _textures.Lines.Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0 || line[(eq + 1)..].Trim().Length == 0) { Say($"\"{line}\" needs a name, = and a texture (all = block/stone).", warn: true); return; }
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        _model.Textures.Clear();
        foreach (var (k, v) in map) _model.Textures[k] = v;
        _changed = true;
        Say("");
        Redraw();
    }

    void Repaint() => Redraw();

    void Redraw()
    {
        var resolved = _model == null ? null : _lib.Resolve(_model.Id, _model);
        if (resolved == null) { _mesh = null; _atlas = null; _view.Redraw(); return; }
        var (mesh, atlas, missing) = JavaModelMesh.Build(resolved, t => _doc.ModelTexture(t, _lib));
        (_mesh, _atlas) = (mesh, atlas);
        if (missing.Count > 0 && _status.ForeColor != C.ORANGE) Say("No picture for " + string.Join(", ", missing.Take(4)) + (missing.Count > 4 ? " …" : "") + " (a name the model's textures don't set, or a texture neither the pack nor the game has).");
        _view.Redraw();
    }

    // ------------------------------------------------------------------ saving

    internal void Save()
    {
        if (_model == null) return;
        if (_model.Elements?.Select((e, i) => e.Problem() is string p ? $"box {i + 1}: {p}" : null).FirstOrDefault(p => p != null) is string problem)
        {
            Say($"Not saved: {problem}.", warn: true);
            return;
        }
        var json = _model.Write();
        string path = JavaModelLibrary.PathOf(_model.Id);
        _editor.Pack.OtherFiles[path] = json;
        _lib.SetPackModel(_model.Id, json);
        _model = JavaBlockModel.Read(_model.Id, json, true);
        _changed = false;
        _editor.OtherFileChanged($"{_model.Id} is in the pack now ({path}); save the pack to keep it.");
        FillElements(_elements.SelectedIndex);
        Say($"Saved into the pack as {path}. Save the pack (Ctrl+S) to keep it; it goes with the pack for Java versions.");
    }

    internal void Revert()
    {
        if (_model == null) return;
        string path = JavaModelLibrary.PathOf(_model.Id);
        bool had = _editor.Pack.OtherFiles.Remove(path);
        _lib.SetPackModel(_model.Id, null);
        string id = _model.Id;
        _model = null;
        _changed = false;
        if (_lib.Get(id) == null) { Say(had ? $"{id} was the pack's own and is gone from it; the game has no model of that name." : "", warn: had); FillElements(0); Redraw(); return; }
        Open(id);
        if (had) _editor.OtherFileChanged($"{id}: the game's model again.");
        Say(had ? $"The pack's {id} is gone: the game's model again." : "The pack had no model of its own here; this is the game's.");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_changed && Dialogs.Ask(this, $"{_model?.Id} has changes that are not in the pack yet. Close without them?") != DialogResult.Yes) e.Cancel = true;
        base.OnFormClosing(e);
    }

    /// <summary>The model being shown (for the checks).</summary>
    internal JavaBlockModel? Model => _model;
    internal ModelMesh? Mesh => _mesh;
}
