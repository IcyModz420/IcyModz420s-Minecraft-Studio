// MinecraftStudio/Studio/AnimationDocument.Keys.cs: editing in the Animation Studio — the chosen bone's keys, the
// animations themselves, and the model.
// Part of AnimationDocument (main part in AnimationDocument.cs).
// Owns: the bone's rotation, position and scale at the current time (a key there, values worked out between keys, or a
// formula for the whole animation; numbers or Molang), keying them (K), a formula for the whole animation, deleting,
// moving and smoothing keys; new / copied / renamed / removed animations, loop and length; and the model: a bone's pivot
// and turn, its boxes (corner, size, texture place, inflate), adding and removing boxes and bones. Every change is one
// undo step of the mob's project.
using System.Globalization;
using System.Numerics;
using MinecraftStudio.Core.Models;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class AnimationDocument
{
    static readonly string[] Channels = ["rotation", "position", "scale"];
    string _keyChannel = "rotation";
    (string Bone, string Channel, double Time)? _key;
    DarkComboBox _boneBox = null!;
    readonly DarkTextField[,] _values = new DarkTextField[3, 3];
    Label _keyNote = null!;
    DarkCheckBox _smooth = null!;
    DarkTextField[] _pivot = [], _boneTurn = [], _cubeOrigin = [], _cubeSize = [], _cubeUv = [];
    DarkTextField _cubeInflate = null!;
    ListBox _cubes = null!;
    Label _modelNote = null!;

    // ------------------------------------------------------------------ panels

    void BuildKeyPanel(FlowLayoutPanel stack)
    {
        stack.Controls.Add(Header("BONE AND ITS KEYS"));
        _boneBox = new DarkComboBox { Width = _sideWidth, Margin = Indent(), AccessibleName = "Bone" };
        _boneBox.SelectedIndexChanged += (_, _) => { if (!_updating && _boneBox.SelectedItem is ComboItem { Tag: string b }) ChooseBone(b); };
        stack.Controls.Add(_boneBox);
        for (int c = 0; c < 3; c++)
        {
            int channel = c;
            var label = new Label
            {
                Text = Channels[c] switch { "rotation" => "Turn", "position" => "Move", _ => "Scale" }, AutoSize = false, Width = this.Px(44), Height = this.Px(26),
                ForeColor = Channels[c] switch { "rotation" => AnimationTimeline.RotationColor, "position" => AnimationTimeline.PositionColor, _ => AnimationTimeline.ScaleColor },
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, this.Px(2), 0, 0),
            };
            var fields = new List<Control> { label };
            for (int a = 0; a < 3; a++)
            {
                var f = Field($"{Channels[c]} {"xyz"[a]}", this.Px(62));
                f.Box.Enter += (_, _) => _keyChannel = Channels[channel];
                f.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { KeyIt(Channels[channel]); e.SuppressKeyPress = true; } };
                f.Box.TextChanged += (_, _) => Validate(f);
                _values[c, a] = f;
                fields.Add(f);
            }
            var key = Theme.Button("Key");
            key.Margin = new Padding(this.Px(2), 0, 0, 0);
            key.Click += (_, _) => KeyIt(Channels[channel]);
            new ToolTip().SetToolTip(key, $"Put a {Channels[c]} key here, at the current time (K, or Enter in a box)");
            fields.Add(key);
            stack.Controls.Add(Row([.. fields]));
        }
        _smooth = new DarkCheckBox("Smooth from this key", false) { Margin = new Padding(0, this.Px(4), this.Px(6), 0) };
        _smooth.CheckedChanged += (_, _) => { if (!_updating && _bone != null) SetLerp(_bone, _keyChannel, _time, _smooth.Checked ? KeyLerp.Smooth : KeyLerp.Linear); };
        stack.Controls.Add(Row(SmallButton("Whole animation", WholeAnimation), SmallButton("Delete key", () => { if (_bone != null) DeleteKey(_bone, _keyChannel, _time); })));
        _smooth.Margin = Indent();
        stack.Controls.Add(_smooth);
        _keyNote = Note("Choose a bone: click a part of the mob, a row of the timeline, or pick it above.");
        stack.Controls.Add(_keyNote);
    }

    void BuildModelPanel(FlowLayoutPanel stack)
    {
        stack.Controls.Add(Header("MODEL"));
        DarkTextField[] Three(string name) => [Field(name + " x", this.Px(62)), Field(name + " y", this.Px(62)), Field(name + " z", this.Px(62))];
        _pivot = Three("Pivot");
        _boneTurn = Three("Bone turn");
        stack.Controls.Add(Row([Caption("Pivot"), .. _pivot]));
        stack.Controls.Add(Row([Caption("Turn"), .. _boneTurn]));
        stack.Controls.Add(Row(SmallButton("Apply to the bone", ApplyBone)));
        stack.Controls.Add(Row(SmallButton("Add a bone", AddBone), SmallButton("Remove the bone", RemoveBone)));
        _cubes = new ListBox { Width = _sideWidth, Height = this.Px(80), BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, Margin = Indent(), AccessibleName = "The bone's boxes" };
        Theme.DarkControl(_cubes);
        _cubes.SelectedIndexChanged += (_, _) => FillCubeFields();
        stack.Controls.Add(_cubes);
        _cubeOrigin = Three("Box corner");
        _cubeSize = Three("Box size");
        _cubeUv = [Field("Texture u", this.Px(62)), Field("Texture v", this.Px(62))];
        _cubeInflate = Field("Inflate", this.Px(62));
        stack.Controls.Add(Row([Caption("Corner"), .. _cubeOrigin]));
        stack.Controls.Add(Row([Caption("Size"), .. _cubeSize]));
        stack.Controls.Add(Row([Caption("Texture"), .. _cubeUv, Caption("inflate"), _cubeInflate]));
        stack.Controls.Add(Row(SmallButton("Apply to the box", ApplyCube)));
        stack.Controls.Add(Row(SmallButton("Add a box", AddCube), SmallButton("Remove the box", RemoveCube)));
        _modelNote = Note("Sizes and places are in pixels (16 to a block); y is up, the mob faces north.");
        stack.Controls.Add(_modelNote);
    }

    // ------------------------------------------------------------------ the bone and its keys

    void ChooseBone(string? bone)
    {
        _bone = bone;
        _updating = true;
        if (_boneBox.Items.Cast<ComboItem>().ToList().FindIndex(x => (x.Tag as string)?.Equals(bone, StringComparison.OrdinalIgnoreCase) == true) is int at and >= 0) _boneBox.SelectedIndex = at;
        _updating = false;
        _timeline.SelectedBone = bone;
        _timeline.Invalidate();
        FillKeyFields();
        FillModelPanel(fillBones: false);
        _view.Redraw();
    }

    static string F(float v) => EntityAnimations.Format(v);

    /// <summary>x, y or z by number.</summary>
    static float At(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    /// <summary>Shows the bone's channels at the current time: a key's own values (formulas as written), otherwise the worked-out values.</summary>
    void FillKeyFields()
    {
        if (_values[0, 0] == null) return;
        _updating = true;
        var a = Current;
        var ba = _bone != null ? a?.Bone(_bone) : null;
        var model = _project?.Model;
        var boneModel = model?.Bones.FirstOrDefault(b => b.Name.Equals(_bone, StringComparison.OrdinalIgnoreCase));
        string note = _bone == null ? "Choose a bone: click a part of the mob, a row of the timeline, or pick it above." : "";
        bool keyHere = false;
        for (int c = 0; c < 3; c++)
        {
            var ch = ba == null ? null : c == 0 ? ba.Rotation : c == 1 ? ba.Position : ba.Scale;
            var key = ch?.Keys.FirstOrDefault(k => Math.Abs(k.Time - _time) < 0.0005);
            string[] shown;
            Color color;
            if (ch == null) { shown = c == 2 ? ["1", "1", "1"] : ["0", "0", "0"]; color = C.FG_FAINT; }
            else if (!ch.IsKeyed) { shown = ch.Constant ?? ["0", "0", "0"]; color = C.YELLOW; }
            else if (key != null) { shown = key.Post; color = C.FG; if (Channels[c] == _keyChannel) keyHere = true; }
            else
            {
                var v = AnimationPlayer.Value(ch, _time, PreviewContext(), c == 0 ? boneModel?.Rotation ?? Vector3.Zero : c == 2 ? Vector3.One : Vector3.Zero, c == 2 ? 1 : 0);
                shown = [F(v.X), F(v.Y), F(v.Z)];
                color = C.FG_DIM;
            }
            for (int x = 0; x < 3; x++)
            {
                _values[c, x].Text = shown[x];
                _values[c, x].ForeColor = color;
                _values[c, x].Enabled = _bone != null && a != null;
            }
        }
        if (_bone != null && a != null)
            note = "White: a key at this time  ·  grey: worked out between keys  ·  yellow: one formula for the whole animation  ·  faint: not animated. Type numbers or Molang, then Key.";
        else if (a == null) note = "Choose or make an animation first.";
        var selCh = ba == null ? null : _keyChannel == "rotation" ? ba.Rotation : _keyChannel == "position" ? ba.Position : ba.Scale;
        _smooth.Enabled = keyHere;
        _smooth.Checked = keyHere && selCh?.Keys.FirstOrDefault(k => Math.Abs(k.Time - _time) < 0.0005)?.Lerp == KeyLerp.Smooth;
        _keyNote.Text = note;
        _updating = false;
    }

    void Validate(DarkTextField f)
    {
        if (_updating) return;
        string t = f.Text.Trim();
        f.ForeColor = t.Length == 0 || Molang.IsValid(t) ? C.FG : C.RED;
    }

    /// <summary>The three values typed for a channel, or null (with a message) when one cannot be read.</summary>
    string[]? Typed(string channel)
    {
        int c = Array.IndexOf(Channels, channel);
        var v = new string[3];
        for (int a = 0; a < 3; a++)
        {
            string t = _values[c, a].Text.Trim();
            if (t.Length == 0) t = channel == "scale" ? "1" : "0";
            if (!Molang.IsValid(t)) { Main?.SetStatus($"The {channel} {"xyz"[a]} value \"{t}\" is not a number or a formula the game can read."); return null; }
            // Plain numbers are written tidily (12.50 → 12.5).
            v[a] = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? EntityAnimations.Format(d) : t;
        }
        return v;
    }

    static AnimChannel? Get(BoneAnimation ba, string channel) => channel switch { "rotation" => ba.Rotation, "position" => ba.Position, _ => ba.Scale };

    static void Set(BoneAnimation ba, string channel, AnimChannel? ch)
    {
        switch (channel)
        {
            case "rotation": ba.Rotation = ch; break;
            case "position": ba.Position = ch; break;
            default: ba.Scale = ch; break;
        }
    }

    BoneAnimation Bone(EntityAnimation a, string bone)
    {
        if (a.Bone(bone) is { } ba) return ba;
        ba = new BoneAnimation { Bone = bone };
        a.Bones.Add(ba);
        return ba;
    }

    /// <summary>Puts a key for a channel of the chosen bone at the current time (or changes the key there).</summary>
    void KeyIt(string channel)
    {
        if (_project == null || Current is not { } a || _bone is not { } bone || Typed(channel) is not { } values) return;
        double time = Math.Round(_time, 4);
        _keyChannel = channel;
        _project.Change($"{channel} key of {bone} at {time:0.##}s", () =>
        {
            var ba = Bone(a, bone);
            var ch = Get(ba, channel) ?? new AnimChannel();
            if (!ch.IsKeyed && ch.Constant != null)
            {
                // A formula for the whole animation becomes the first key, so the animation still starts the same.
                if (time > 0.0005) ch.Keys.Add(new AnimKey { Time = 0, Post = ch.Constant });
                ch.Constant = null;
            }
            var existing = ch.Keys.FirstOrDefault(k => Math.Abs(k.Time - time) < 0.0005);
            if (existing != null) { existing.Post = values; existing.Pre = null; }
            else ch.Keys.Add(new AnimKey { Time = time, Post = values });
            ch.Sort();
            Set(ba, channel, ch);
        }, animations: a.Id);
        _key = (bone, channel, time);
        _timeline.SelectedKey = _key;
    }

    /// <summary>The typed values become one value (or formula) for the whole animation: its keys for that channel go.</summary>
    void WholeAnimation()
    {
        if (_project == null || Current is not { } a || _bone is not { } bone || Typed(_keyChannel) is not { } values) return;
        string channel = _keyChannel;
        int keys = a.Bone(bone) is { } had && Get(had, channel) is { IsKeyed: true } kc ? kc.Keys.Count : 0;
        if (keys > 1 && Dialogs.Ask(FindForm()!, $"Replace the {keys} {channel} keys of {bone} with one value for the whole animation?") != DialogResult.Yes) return;
        _project.Change($"{channel} of {bone} for the whole animation", () => Set(Bone(a, bone), channel, new AnimChannel { Constant = values }), animations: a.Id);
    }

    void DeleteKey(string bone, string channel, double time)
    {
        if (_project == null || Current is not { } a || a.Bone(bone) is not { } ba || Get(ba, channel) is not { } ch) return;
        var key = ch.Keys.FirstOrDefault(k => Math.Abs(k.Time - time) < 0.0005);
        if (key == null && ch.IsKeyed) { Main?.SetStatus($"There is no {channel} key of {bone} at {time:0.##}s."); return; }
        _project.Change(key != null ? $"Delete {channel} key of {bone}" : $"Delete {channel} of {bone}", () =>
        {
            if (key != null) ch.Keys.Remove(key);
            if (key == null || ch.Keys.Count == 0 && ch.Constant == null) Set(ba, channel, null);
            if (!ba.Channels().Any() && ba.Extra.Count == 0) a.Bones.Remove(ba);
        }, animations: a.Id);
        _key = null;
    }

    void MoveKey(string bone, string channel, double from, double to)
    {
        if (_project == null || Current is not { } a || a.Bone(bone) is not { } ba || Get(ba, channel) is not { } ch) return;
        var key = ch.Keys.FirstOrDefault(k => Math.Abs(k.Time - from) < 0.0005);
        if (key == null) return;
        to = Math.Round(Math.Max(0, to), 4);
        _project.Change($"Move {channel} key of {bone}", () =>
        {
            // A key already at the new time is replaced by the one moved there.
            ch.Keys.RemoveAll(k => k != key && Math.Abs(k.Time - to) < 0.0005);
            key.Time = to;
            ch.Sort();
            if (a.Length is double len && to > len) a.Length = to;
        }, animations: a.Id);
        _key = (bone, channel, to);
        SetTime(to);
    }

    void SetLerp(string bone, string channel, double time, KeyLerp lerp)
    {
        if (_project == null || Current is not { } a || a.Bone(bone) is not { } ba || Get(ba, channel) is not { } ch) return;
        var key = ch.Keys.FirstOrDefault(k => Math.Abs(k.Time - time) < 0.0005);
        if (key == null || key.Lerp == lerp) return;
        _project.Change(lerp == KeyLerp.Smooth ? "Smooth key" : "Straight key", () => key.Lerp = lerp, animations: a.Id);
    }

    // ------------------------------------------------------------------ the animations

    void NewAnimation()
    {
        if (_project == null) return;
        string? name = Dialogs.Input(FindForm()!, "New animation", "Name of the new animation (for example: dance, wave, roar):", "dance");
        if (string.IsNullOrWhiteSpace(name)) return;
        ChooseAnimation(_project.NewAnimation(name));
    }

    void CopyAnimation()
    {
        if (_project == null || Current is not { } a) return;
        string? name = Dialogs.Input(FindForm()!, "Copy the animation", "Name of the copy:", ShortName(a.Id) + " copy");
        if (string.IsNullOrWhiteSpace(name)) return;
        ChooseAnimation(_project.NewAnimation(name, a));
    }

    void RenameAnimation()
    {
        if (_project == null || Current is not { } a) return;
        if (!_project.NewIds.Contains(a.Id)) { Dialogs.Info(FindForm()!, "This is one of the game's own animations: it keeps its name, so your changes replace the game's. (Copy it to make one under a new name.)"); return; }
        string? name = Dialogs.Input(FindForm()!, "Rename the animation", "New name:", ShortName(a.Id));
        if (string.IsNullOrWhiteSpace(name)) return;
        string clean = new string(name.Trim().Replace(' ', '_').Where(c => char.IsLetterOrDigit(c) || c is '_' or '.').ToArray()).ToLowerInvariant();
        if (clean.Length == 0 || _project.Names.Any(n => n.Name == clean)) { Main?.SetStatus("That name is taken (or has no letters)."); return; }
        _project.Change("Rename to " + clean, () =>
        {
            int i = _project.Names.FindIndex(n => n.Id == a.Id);
            if (i >= 0) _project.Names[i] = (clean, a.Id);
        }, animations: a.Id);
    }

    void RemoveAnimation()
    {
        if (_project == null || Current is not { } a) return;
        if (_project.NewIds.Contains(a.Id))
        {
            if (Dialogs.Ask(FindForm()!, $"Remove the animation {ShortName(a.Id)}? (Undo brings it back.)") != DialogResult.Yes) return;
            _project.Change("Remove " + ShortName(a.Id), () => { _project.Animations.Remove(a.Id); _project.Names.RemoveAll(n => n.Id == a.Id); });
            ChooseAnimation(_project.Names.FirstOrDefault().Id);
            return;
        }
        if (!_catalog.Animations.TryGetValue(a.Id, out var game)) return;
        if (Dialogs.Ask(FindForm()!, $"{ShortName(a.Id)} is one of the game's own animations, so it cannot be removed. Put it back the way the game has it (your changes to it go; Undo brings them back)?") != DialogResult.Yes) return;
        _project.Change("Back to the game's " + ShortName(a.Id), () => _project.Animations[a.Id] = game.Clone(), animations: a.Id);
    }

    void SetLoop()
    {
        if (_updating || _project == null || Current is not { } a) return;
        string loop = _loopMode.SelectedIndex switch { 0 => "true", 2 => "hold_on_last_frame", _ => "false" };
        if (loop == a.Loop) return;
        _project.Change("How it plays", () => a.Loop = loop, animations: a.Id);
    }

    void SetLength()
    {
        if (_updating || _project == null || Current is not { } a) return;
        string t = _length.Text.Trim();
        double? len = t.Length == 0 ? null : double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 && d <= 3600 ? Math.Round(d, 4) : -1;
        if (len == -1) { Main?.SetStatus("The length is in seconds, for example 1.5 (leave it empty to end at the last key)."); FillAnimationFields(); return; }
        if (len == a.Length) return;
        _project.Change("Length", () => a.Length = len, animations: a.Id);
    }

    // ------------------------------------------------------------------ the model

    ModelBone? ModelBone => _project?.Model?.Bones.FirstOrDefault(b => b.Name.Equals(_bone, StringComparison.OrdinalIgnoreCase));

    void FillModelPanel(bool fillBones = true)
    {
        if (_pivot.Length == 0) return;
        _updating = true;
        if (fillBones)
        {
            _boneBox.Items.Clear();
            foreach (var b in _project?.Model?.Bones ?? []) _boneBox.Items.Add(new ComboItem(b.Name + (b.Parent != null ? $"  (on {b.Parent})" : ""), null, b.Name));
            if (_bone != null && _boneBox.Items.Cast<ComboItem>().ToList().FindIndex(i => string.Equals(i.Tag as string, _bone, StringComparison.OrdinalIgnoreCase)) is int bi and >= 0) _boneBox.SelectedIndex = bi;
        }
        var bone = ModelBone;
        for (int i = 0; i < 3; i++)
        {
            _pivot[i].Text = bone == null ? "" : F(At(bone.Pivot, i));
            _boneTurn[i].Text = bone == null ? "" : F(At(bone.Rotation, i));
            _pivot[i].Enabled = _boneTurn[i].Enabled = bone != null;
        }
        _cubes.Items.Clear();
        if (bone != null)
            for (int i = 0; i < bone.Cubes.Count; i++)
            {
                var c = bone.Cubes[i];
                _cubes.Items.Add($"Box {i + 1}: {F(c.Size.X)} × {F(c.Size.Y)} × {F(c.Size.Z)} at {F(c.Origin.X)}, {F(c.Origin.Y)}, {F(c.Origin.Z)}");
            }
        if (_cubes.Items.Count > 0) _cubes.SelectedIndex = 0;
        _modelNote.Text = bone == null ? "Choose a bone to change its pivot, turn and boxes." : bone.BindPoseInherited ? "This bone's resting turn is the game's own (it adds it itself)." : "Sizes and places are in pixels (16 to a block); y is up, the mob faces north.";
        _updating = false;
        FillCubeFields();
    }

    ModelCube? SelectedCube => ModelBone is { } b && _cubes.SelectedIndex >= 0 && _cubes.SelectedIndex < b.Cubes.Count ? b.Cubes[_cubes.SelectedIndex] : null;

    void FillCubeFields()
    {
        var c = SelectedCube;
        for (int i = 0; i < 3; i++)
        {
            _cubeOrigin[i].Text = c == null ? "" : F(At(c.Origin, i));
            _cubeSize[i].Text = c == null ? "" : F(At(c.Size, i));
            _cubeOrigin[i].Enabled = _cubeSize[i].Enabled = c != null;
        }
        var uv = c?.BoxUv ?? Vector2.Zero;
        _cubeUv[0].Text = c == null ? "" : F(uv.X);
        _cubeUv[1].Text = c == null ? "" : F(uv.Y);
        _cubeInflate.Text = c == null ? "" : F(c.Inflate);
        _cubeUv[0].Enabled = _cubeUv[1].Enabled = c is { BoxUv: not null };
        _cubeInflate.Enabled = c != null;
    }

    static bool Parse(DarkTextField[] f, out Vector3 v)
    {
        v = Vector3.Zero;
        var n = new float[3];
        for (int i = 0; i < 3; i++) if (!float.TryParse(f[i].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || !float.IsFinite(n[i])) return false;
        v = new Vector3(n[0], n[1], n[2]);
        return true;
    }

    void ApplyBone()
    {
        if (_project?.Model == null || ModelBone is not { } bone) return;
        if (!Parse(_pivot, out var pivot) || !Parse(_boneTurn, out var turn)) { Main?.SetStatus("Pivot and turn are numbers (for example 0, 12.5, -4)."); return; }
        if (pivot == bone.Pivot && turn == bone.Rotation) return;
        _project.Change($"Bone {bone.Name}", () => { bone.Pivot = pivot; bone.Rotation = turn; }, model: true);
    }

    void ApplyCube()
    {
        if (_project?.Model == null || SelectedCube is not { } cube) return;
        if (!Parse(_cubeOrigin, out var origin) || !Parse(_cubeSize, out var size) || size.X < 0 || size.Y < 0 || size.Z < 0) { Main?.SetStatus("A box's corner and size are numbers; sizes are not below 0."); return; }
        if (!float.TryParse(_cubeInflate.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float inflate)) inflate = cube.Inflate;
        Vector2? uv = cube.BoxUv;
        if (cube.BoxUv != null && float.TryParse(_cubeUv[0].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float u) && float.TryParse(_cubeUv[1].Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float vv)) uv = new Vector2(u, vv);
        int index = _cubes.SelectedIndex;
        _project.Change("Box of " + _bone, () => { cube.Origin = origin; cube.Size = size; cube.Inflate = inflate; cube.BoxUv = uv; }, model: true);
        if (index < _cubes.Items.Count) _cubes.SelectedIndex = index;
    }

    void AddCube()
    {
        if (_project?.Model == null || ModelBone is not { } bone) { Main?.SetStatus("Choose the bone the box goes on first."); return; }
        _project.Change("Add a box to " + bone.Name, () => bone.Cubes.Add(new ModelCube { Origin = bone.Pivot - new Vector3(2, 2, 2), Size = new Vector3(4, 4, 4), BoxUv = Vector2.Zero, Inflate = bone.Inflate }), model: true);
        _cubes.SelectedIndex = _cubes.Items.Count - 1;
    }

    void RemoveCube()
    {
        if (_project?.Model == null || ModelBone is not { } bone || SelectedCube is not { } cube) return;
        _project.Change("Remove a box of " + bone.Name, () => bone.Cubes.Remove(cube), model: true);
    }

    void AddBone()
    {
        if (_project?.Model is not { } model) return;
        string? name = Dialogs.Input(FindForm()!, "Add a bone", "Name of the new bone:", "new_bone");
        if (string.IsNullOrWhiteSpace(name)) return;
        string clean = new string(name.Trim().Replace(' ', '_').Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (clean.Length == 0 || model.Bones.Any(b => b.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))) { Main?.SetStatus("That bone name is taken (or has no letters)."); return; }
        var parent = ModelBone;
        _project.Change("Add bone " + clean, () => model.Bones.Add(new ModelBone { Name = clean, Parent = parent?.Name, Pivot = parent?.Pivot ?? Vector3.Zero }), model: true);
        ChooseBone(clean);
    }

    void RemoveBone()
    {
        if (_project?.Model is not { } model || ModelBone is not { } bone) return;
        int children = model.Bones.Count(b => string.Equals(b.Parent, bone.Name, StringComparison.OrdinalIgnoreCase));
        if (Dialogs.Ask(FindForm()!, $"Remove the bone {bone.Name} and its {bone.Cubes.Count} box(es)?" + (children > 0 ? $" Its {children} bone(s) hang from {bone.Parent ?? "the mob"} instead." : "") + " (Undo brings it back.)") != DialogResult.Yes) return;
        _project.Change("Remove bone " + bone.Name, () =>
        {
            foreach (var child in model.Bones.Where(b => string.Equals(b.Parent, bone.Name, StringComparison.OrdinalIgnoreCase))) child.Parent = bone.Parent;
            model.Bones.Remove(bone);
        }, model: true);
        ChooseBone(null);
    }
}
