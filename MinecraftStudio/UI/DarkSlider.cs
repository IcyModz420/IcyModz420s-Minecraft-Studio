// MinecraftStudio/UI/DarkSlider.cs: a slider in the dark theme, with its name and value written on it.
// Owns: DarkSlider — drag or click to set, the wheel and arrow keys step, Home resets to the middle value, double-click
// resets; ValueChanged fires while dragging (for live previews).
namespace MinecraftStudio.UI;

/// <summary>A slider in the dark theme with its label and value drawn on it.</summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed class DarkSlider : Control
{
    int _value;
    bool _dragging;

    public DarkSlider(string label, int min, int max, int value, string unit = "")
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Label = label;
        Minimum = min;
        Maximum = max;
        Default = value;
        _value = value;
        Unit = unit;
        BackColor = C.BG2;
        Height = this.Px(38);
        AccessibleName = label;
        AccessibleRole = AccessibleRole.Slider;
    }

    public string Label { get; }
    public string Unit { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    /// <summary>The value double-click and Home go back to.</summary>
    public int Default { get; }
    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            AccessibleDescription = v.ToString();
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    Rectangle Track => new(this.Px(6), Height - this.Px(14), Width - this.Px(12), this.Px(4));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var f = Theme.Ui(9f);
        TextRenderer.DrawText(g, Label, f, new Point(this.Px(4), this.Px(2)), Enabled ? C.FG : C.FG_FAINT);
        string text = (_value > 0 && Minimum < 0 ? "+" : "") + _value + Unit;
        var size = TextRenderer.MeasureText(text, f);
        TextRenderer.DrawText(g, text, f, new Point(Width - size.Width - this.Px(4), this.Px(2)), _value == Default ? C.FG_DIM : C.ACCENT2);
        var t = Track;
        using (var bg = new SolidBrush(C.BG4)) g.FillRectangle(bg, t);
        float k = (float)(_value - Minimum) / Math.Max(1, Maximum - Minimum);
        float zero = Minimum < 0 && Maximum > 0 ? (float)-Minimum / (Maximum - Minimum) : 0;
        float a = t.Left + t.Width * Math.Min(k, zero), b = t.Left + t.Width * Math.Max(k, zero);
        using (var fill = new SolidBrush(Enabled ? C.ACCENT : C.BG4)) g.FillRectangle(fill, a, t.Top, b - a, t.Height);
        float x = t.Left + t.Width * k;
        int r = this.Px(7);
        using (var knob = new SolidBrush(Enabled ? (Focused ? Color.White : C.FG) : C.FG_FAINT)) g.FillEllipse(knob, x - r, t.Top + t.Height / 2f - r, 2 * r, 2 * r);
    }

    void SetFrom(int mouseX)
    {
        var t = Track;
        float k = Math.Clamp((mouseX - t.Left) / (float)Math.Max(1, t.Width), 0, 1);
        Value = (int)Math.Round(Minimum + k * (Maximum - Minimum));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        SetFrom(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFrom(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        Value = Default;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta) * Step;
    }

    int Step => Math.Max(1, (Maximum - Minimum) / 100);

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int big = Math.Max(Step, (Maximum - Minimum) / 10);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= e.Shift ? big : Step; e.Handled = true; break;
            case Keys.Right or Keys.Up: Value += e.Shift ? big : Step; e.Handled = true; break;
            case Keys.Home: Value = Default; e.Handled = true; break;
            case Keys.PageDown: Value -= big; e.Handled = true; break;
            case Keys.PageUp: Value += big; e.Handled = true; break;
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
