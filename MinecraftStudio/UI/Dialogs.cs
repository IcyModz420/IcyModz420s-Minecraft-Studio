// MinecraftStudio/UI/Dialogs.cs: Dark modal dialog base. Like the rest of the app, sizes are scaled explicitly
// with Px(); nothing is scaled automatically.
// Owns: DarkDialog, Dialogs, FormBuilder.
namespace MinecraftStudio.UI;

/// <summary>Dark modal dialog base. Like the rest of the app, sizes are scaled explicitly with Px(); nothing is scaled automatically.</summary>
class DarkDialog : Form
{
    public DarkDialog(string title, int width)
    {
        Text = title;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = C.BG2;
        ForeColor = C.FG;
        Font = Theme.Ui(9.5f);
        ClientSize = new Size(this.Px(width), this.Px(100));
        Padding = new Padding(this.Px(16));
        // Auto-sized dialogs measure docked content poorly; never let them shrink below the intended width.
        MinimumSize = new Size(Width, 0);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    bool _fitScreen;

    /// <summary>
    /// Makes the dialog as tall as its content: call at the end of the constructor, once the controls are in. On a screen
    /// too small for that it opens as tall as the screen allows, the content above the buttons scrolling (<see cref="FitHeight"/>).
    /// </summary>
    protected void SizeToContent()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _fitScreen = true;
    }

    /// <summary>
    /// For a dialog that sets its ClientSize (content laid out by hand in a filling panel): on a screen too short for it,
    /// it opens as tall as the screen allows and the panel scrolls (<see cref="FitHeight"/>).
    /// </summary>
    protected void FitScreen() => _fitScreen = true;

    protected override void OnLoad(EventArgs e)
    {
        // Before the base, which centres the dialog on its parent with the size it has then.
        if (_fitScreen) FitHeight(Screen.FromControl(ActiveForm ?? this).WorkingArea.Height);
        base.OnLoad(e);
    }

    /// <summary>Keeps the dialog at most <paramref name="max"/> pixels tall: the content docked above the buttons then scrolls.</summary>
    public void FitHeight(int max)
    {
        PerformLayout();
        if (Height <= max) return;
        var content = Controls.Cast<Control>().Where(c => c.Dock == DockStyle.Top).ToList();
        if (content.Count == 0)
        {
            // Content laid out by hand in a filling panel (a dialog that sets its ClientSize): that panel scrolls.
            if (Controls.Cast<Control>().FirstOrDefault(c => c.Dock == DockStyle.Fill) is not Panel fill) return;
            SuspendLayout();
            AutoSize = false;
            fill.AutoScroll = true;
            MinimumSize = Size.Empty;
            Size = new Size(Width + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi), max);
            ResumeLayout(true);
            return;
        }
        SuspendLayout();
        AutoSize = false;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BackColor, ForeColor = ForeColor };
        Theme.DarkControl(scroll);
        // Top to bottom as they were docked (the last in the collection docks first, at the top), no longer docked: the
        // panel scrolls them at their own size.
        int y = 0;
        foreach (var c in Enumerable.Reverse(content))
        {
            int width = c.Width;
            Controls.Remove(c);
            c.Dock = DockStyle.None;
            // No wider than it was docked: text inside wraps as before.
            c.MaximumSize = new Size(width, 0);
            c.Location = new Point(0, y);
            scroll.Controls.Add(c);
            y += c.Height;
        }
        Controls.Add(scroll);
        scroll.BringToFront(); // a filling control docks after the edges
        // Wider by the scroll bar, so the content keeps its width (no sideways scrolling).
        MinimumSize = Size.Empty;
        Size = new Size(Width + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi), max);
        ResumeLayout(true);
    }

    /// <summary>
    /// Keeps the dialog as wide as <paramref name="content"/> needs (a row of fields with a map beside them): an auto-sized
    /// dialog measures what is docked across it no wider than it was made. Call before <see cref="SizeToContent"/>.
    /// </summary>
    protected void WideEnoughFor(Control content)
    {
        int needed = content.GetPreferredSize(Size.Empty).Width + content.Margin.Horizontal + Padding.Horizontal + (Width - ClientSize.Width);
        if (needed > MinimumSize.Width) MinimumSize = new Size(needed, 0);
    }

    /// <summary>
    /// On a screen narrower than the dialog, <paramref name="giver"/> (a map beside the fields) gives up width, down to
    /// <paramref name="least"/>, so the dialog fits. Call in OnLoad before the base.
    /// </summary>
    protected void FitWidth(Control giver, int least)
    {
        PerformLayout();
        int over = Width - Screen.FromControl(ActiveForm ?? this).WorkingArea.Width;
        if (over <= 0) return;
        int before = giver.Width;
        giver.Width = Math.Max(least, giver.Width - over);
        MinimumSize = new Size(Math.Max(0, MinimumSize.Width - (before - giver.Width)), 0);
        PerformLayout();
    }

    public FlowLayoutPanel AddButtons(params (string Text, DialogResult Result, bool Primary)[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = new Padding(0, this.Px(14), 0, 0) };
        foreach (var (text, result, primary) in buttons.Reverse())
        {
            var b = Theme.Button(text, primary);
            b.DialogResult = result;
            b.MinimumSize = new Size(this.Px(90), this.Px(30));
            panel.Controls.Add(b);
            if (result == DialogResult.OK && primary) AcceptButton = b;
            if (result == DialogResult.Cancel) CancelButton = b;
        }
        Controls.Add(panel);
        return panel;
    }
}

static class Dialogs
{
    public const string AppName = Core.AppInfo.Name;

    /// <summary>Single-line text prompt. Returns null when cancelled.</summary>
    public static string? Input(IWin32Window owner, string title, string prompt, string initial, bool multiline = false)
    {
        using var f = new DarkDialog(title, multiline ? 640 : 520);
        var label = new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(f.Px(multiline ? 608 : 488), 0), Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, f.Px(8)), UseMnemonic = false };
        var box = new TextBox
        {
            Text = initial, Dock = multiline ? DockStyle.Fill : DockStyle.Top, BackColor = C.BG, ForeColor = C.FG,
            BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(10.5f), Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None, WordWrap = false, AcceptsReturn = multiline, AcceptsTab = multiline,
        };
        if (multiline) { box.Height = f.Px(300); f.ClientSize = new Size(f.Px(640), f.Px(420)); }
        f.AddButtons(("OK", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        f.Controls.Add(box);
        f.Controls.Add(label);
        if (!multiline)
        {
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
        else f.AcceptButton = null;
        f.Shown += (_, _) => { box.Focus(); if (!multiline) box.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK ? (multiline ? box.Text : box.Text.Trim()) : null;
    }

    /// <summary>Choice between several labelled options. Returns the index or -1.</summary>
    public static int Choose(IWin32Window owner, string title, string message, params string[] options)
    {
        using var f = new DarkDialog(title, 520);
        var label = new Label { Text = message, AutoSize = true, MaximumSize = new Size(f.Px(488), 0), Dock = DockStyle.Top, UseMnemonic = false, Padding = new Padding(0, 0, 0, f.Px(10)) };
        var list = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        int chosen = -1;
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var b = Theme.Button(options[i], i == 0);
            b.MinimumSize = new Size(f.Px(480), f.Px(34));
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Click += (_, _) => { chosen = index; f.DialogResult = DialogResult.OK; };
            list.Controls.Add(b);
        }
        f.AddButtons(("Cancel", DialogResult.Cancel, false));
        f.Controls.Add(list);
        f.Controls.Add(label);
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        return f.ShowDialog(owner) == DialogResult.OK ? chosen : -1;
    }

    public static DialogResult Ask(IWin32Window owner, string text, string caption = AppName, MessageBoxButtons buttons = MessageBoxButtons.YesNo, MessageBoxIcon icon = MessageBoxIcon.Question) =>
        MessageBox.Show(owner, text, caption, buttons, icon);

    public static void Error(IWin32Window owner, string text, string caption = AppName) =>
        MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window owner, string text, string caption = AppName) =>
        MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>Read-only text (reports, logs) in a resizable dark window.</summary>
    public static void ShowText(IWin32Window owner, string title, string text)
    {
        using var f = new DarkDialog(title, 760) { FormBorderStyle = FormBorderStyle.Sizable, MaximizeBox = true };
        f.MinimumSize = new Size(f.Px(500), f.Px(300));
        f.ClientSize = new Size(f.Px(760), f.Px(520));
        var box = new TextBox
        {
            Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(9.5f),
        };
        f.AddButtons(("Close", DialogResult.Cancel, true));
        f.Controls.Add(box);
        f.Shown += (_, _) => box.Select(0, 0);
        f.ShowDialog(owner);
    }
}

/// <summary>Simple two-column property form built in code (label + editor rows).</summary>
sealed class FormBuilder
{
    readonly TableLayoutPanel _table;
    public FormBuilder(Control host)
    {
        _table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        _table.Padding = new Padding(0, _table.Px(4), 0, _table.Px(4));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.Controls.Add(_table);
    }

    public TableLayoutPanel Table => _table;

    public T Row<T>(string label, T control) where T : Control
    {
        int row = _table.RowCount++;
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var l = new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, Anchor = AnchorStyles.Left, Margin = new Padding(0, _table.Px(6), _table.Px(12), _table.Px(6)), UseMnemonic = false };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, _table.Px(3), 0, _table.Px(3));
        _table.Controls.Add(l, 0, row);
        _table.Controls.Add(control, 1, row);
        return control;
    }

    public DarkTextField Text(string label, string value) => Row(label, new DarkTextField(value) { Height = _table.Px(28), AccessibleName = label });

    public NumberField Number(string label, decimal value, decimal min, decimal max) =>
        Row(label, new NumberField { Minimum = min, Maximum = max, Value = value, Height = _table.Px(28), AccessibleName = label });

    public DarkComboBox Combo(string label, IEnumerable<string> items, int selected)
    {
        var c = new DarkComboBox { AccessibleName = label };
        c.Items.AddRange(items.Cast<object>().ToArray());
        if (selected >= 0 && selected < c.Items.Count) c.SelectedIndex = selected;
        return Row(label, c);
    }

    public DarkCheckBox Check(string label, bool value) => Row(label, new DarkCheckBox("", value) { AccessibleName = label });
}
