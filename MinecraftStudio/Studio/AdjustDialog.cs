// MinecraftStudio/Studio/AdjustDialog.cs: "Adjust colours" for the selection or the whole picture, seen live.
// Owns: AdjustDialog — hue, saturation, brightness and contrast sliders, colourising toward a colour (each pixel keeps
// its own shading), invert, grey and fewer colour levels. Every change is tried on the picture itself (all views show
// it, the 3D model too); Apply makes it one undo step, Cancel puts the picture back.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

sealed class AdjustDialog : DarkDialog
{
    readonly IPixelEditor _editor;
    readonly DarkSlider _hue, _saturation, _brightness, _contrast, _strength;
    readonly DarkCheckBox _tint, _invert, _grey;
    readonly DarkComboBox _levels;
    readonly Button _swatch;
    uint _tintColor;

    static readonly int[] Levels = [0, 2, 3, 4, 6, 8, 16];

    public AdjustDialog(IPixelEditor editor) : base("Adjust colours", 440)
    {
        _editor = editor;
        _tintColor = editor.Primary | 0xFF000000;
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = C.BG2 };
        int w = this.Px(400);
        stack.Controls.Add(new Label
        {
            Text = (editor.Session.Selection != null ? "The selection of " : "All of ") + editor.AreaName + ". The picture shows the change as you make it.",
            AutoSize = true, MaximumSize = new Size(w, 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        DarkSlider Slider(string name, int min, int max, string unit = "")
        {
            var s = new DarkSlider(name, min, max, 0, unit) { Width = w, Margin = new Padding(0, this.Px(2), 0, this.Px(4)) };
            s.ValueChanged += (_, _) => Try();
            stack.Controls.Add(s);
            return s;
        }
        _hue = Slider("Hue", -180, 180, "°");
        _saturation = Slider("Saturation", -100, 100, "%");
        _brightness = Slider("Brightness", -100, 100, "%");
        _contrast = Slider("Contrast", -100, 100, "%");

        var tintRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = C.BG2, Margin = new Padding(0, this.Px(8), 0, 0) };
        _tint = new DarkCheckBox("Colourise toward", false);
        _swatch = Theme.Button("      ");
        _swatch.BackColor = Color.FromArgb(unchecked((int)_tintColor));
        _swatch.Click += (_, _) => PickTint();
        tintRow.Controls.AddRange([_tint, _swatch]);
        stack.Controls.Add(tintRow);
        _strength = new DarkSlider("Colourise strength", 0, 100, 100, "%") { Width = w, Margin = new Padding(0, this.Px(2), 0, this.Px(4)) };
        _strength.ValueChanged += (_, _) => Try();
        stack.Controls.Add(_strength);
        var flags = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = C.BG2, Margin = new Padding(0, this.Px(6), 0, 0) };
        _invert = new DarkCheckBox("Invert", false);
        _grey = new DarkCheckBox("Grey", false);
        flags.Controls.AddRange([_invert, _grey]);
        stack.Controls.Add(flags);
        var levelRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = C.BG2, Margin = new Padding(0, this.Px(6), 0, 0) };
        _levels = new DarkComboBox { Width = this.Px(220), AccessibleName = "Colour levels" };
        foreach (int n in Levels) _levels.Items.Add(new ComboItem(n == 0 ? "Every colour level" : $"{n} levels per colour"));
        _levels.SelectedIndex = 0;
        levelRow.Controls.AddRange([new Label { Text = "Levels", AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(0, this.Px(7), this.Px(8), 0) }, _levels]);
        stack.Controls.Add(levelRow);
        foreach (var c in new[] { _tint, _invert, _grey }) c.CheckedChanged += (_, _) => Try();
        _levels.SelectedIndexChanged += (_, _) => Try();

        var buttons = AddButtons(("Cancel", DialogResult.Cancel, false), ("Apply", DialogResult.OK, true));
        var reset = Theme.Button("Reset");
        reset.Click += (_, _) => Reset();
        buttons.Controls.Add(reset);
        Controls.Add(stack);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        // Apply (or Enter) keeps the change as one undo step; anything else puts the picture back.
        FormClosed += (_, _) => { if (DialogResult == DialogResult.OK) _editor.Session.Commit(); else _editor.Session.Cancel(); };
    }

    ColorAdjustment Current => new()
    {
        Hue = _hue.Value, Saturation = _saturation.Value, Brightness = _brightness.Value, Contrast = _contrast.Value,
        Tint = _tint.Checked ? _tintColor : null, TintStrength = _strength.Value, Invert = _invert.Checked, Grayscale = _grey.Checked,
        Posterize = Levels[Math.Max(0, _levels.SelectedIndex)],
    };

    void Try()
    {
        _strength.Enabled = _tint.Checked;
        var a = Current;
        if (a.IsNone) { if (_editor.Session.IsLive) _editor.Session.Cancel(); return; }
        _editor.Session.Preview("Adjust colours", c => ColorAdjust.Apply(c, a));
    }

    void PickTint()
    {
        using var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = Color.FromArgb(unchecked((int)_tintColor)) };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _tintColor = (uint)dlg.Color.ToArgb() | 0xFF000000;
        _swatch.BackColor = dlg.Color;
        _tint.Checked = true;
        Try();
    }

    void Reset()
    {
        foreach (var s in new[] { _hue, _saturation, _brightness, _contrast }) s.Value = 0;
        _strength.Value = 100;
        _tint.Checked = _invert.Checked = _grey.Checked = false;
        _levels.SelectedIndex = 0;
        Try();
    }
}
