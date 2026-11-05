// MinecraftStudio/Documents/ProgressDialog.cs: a Java player's statistics and advancements (the Players page's button).
// Owns: ProgressDialog — "Show": the advancements (titles from the player's own installed Minecraft) or one category of
// statistics; a filter; advancements marked done or not done (one, or every one), statistics set to a value. Saved
// through PlayerProgress after a backup, refused while the game has the world open.
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class ProgressDialog : DarkDialog
{
    readonly JavaWorld _world;
    readonly PlayerProgress _progress;
    readonly DarkComboBox _show;
    readonly DarkTextField _filter;
    readonly ListView _list;
    readonly NumberField _value;
    readonly Button _done, _notDone, _allDone, _set, _save;
    readonly FlowLayoutPanel _advancementTools, _statTools;
    readonly Label _status;

    static readonly (string Id, string Name)[] Categories =
    [
        ("minecraft:custom", "General"), ("minecraft:mined", "Mined"), ("minecraft:crafted", "Crafted"), ("minecraft:used", "Used"),
        ("minecraft:broken", "Broken"), ("minecraft:picked_up", "Picked up"), ("minecraft:dropped", "Dropped"), ("minecraft:killed", "Killed"),
        ("minecraft:killed_by", "Killed by"), ("", "Statistics"),
    ];

    public ProgressDialog(JavaWorld world, PlayerRecord player, PlayerProgress progress, AdvancementCatalog? catalog) : base($"{player.Name}: statistics and advancements", 820)
    {
        _world = world;
        _progress = progress;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ClientSize = new Size(this.Px(820), this.Px(600));
        MinimumSize = new Size(this.Px(600), this.Px(420));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = catalog is { Version.Length: > 0 }
                ? $"Advancement titles and what each takes come from your installed Minecraft {catalog.Version} (and the world's data packs). Changes are saved into the world's own files after a backup."
                : "No Java Minecraft is installed on this PC, so the game's own advancements show by name and only those the player has started can be marked done (the world's data packs' advancements are all known). Changes are saved after a backup.",
            AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _show = new DarkComboBox { Width = this.Px(260), AccessibleName = "Show", Margin = new Padding(0, 0, this.Px(10), 0) };
        _show.Items.Add(new ComboItem("Advancements", $"{progress.Advancements().Count(a => a.Done)} done", "advancements"));
        var stats = progress.Stats();
        foreach (var (id, name) in Categories)
            if (stats.Any(s => s.Category == id)) _show.Items.Add(new ComboItem(name, $"{stats.Count(s => s.Category == id)}", id));
        _show.SelectedIndex = 0;
        _filter = new DarkTextField { Width = this.Px(260), Height = this.Px(28), AccessibleName = "Filter" };
        _filter.Box.PlaceholderText = "Filter by name…";
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_show, _filter]);
        top.Controls.Add(row);

        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Statistics and advancements",
        };
        Theme.DarkList(_list);

        _done = Theme.Button("Mark done");
        _done.Click += (_, _) => Each(a => _progress.Grant(a.Id));
        _notDone = Theme.Button("Mark not done");
        _notDone.Click += (_, _) => Each(a => _progress.Revoke(a.Id));
        _allDone = Theme.Button("Mark every advancement done");
        _allDone.Click += (_, _) => { foreach (var a in _progress.Advancements().Where(a => !a.Done)) Try(() => _progress.Grant(a.Id)); Fill(); };
        _advancementTools = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        _advancementTools.Controls.AddRange([_done, _notDone, _allDone]);
        _value = new NumberField { Width = this.Px(140), Height = this.Px(28), Minimum = 0, Maximum = int.MaxValue, AccessibleName = "Value", Margin = new Padding(0, 0, this.Px(6), 0) };
        _set = Theme.Button("Set the value");
        _set.Click += (_, _) =>
        {
            foreach (ListViewItem item in _list.SelectedItems) if (item.Tag is StatLine s) _progress.SetStat(s.Category, s.Name, (long)_value.Value);
            Fill();
        };
        _statTools = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        _statTools.Controls.AddRange([_value, _set]);

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _save = Theme.Button("Save", true);
        _save.Click += (_, _) => Save();
        buttons.Controls.Add(_save);
        Controls.Add(_list);
        Controls.Add(_advancementTools);
        Controls.Add(_statTools);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);

        _show.SelectedIndexChanged += (_, _) => Fill();
        _filter.Box.TextChanged += (_, _) => Fill();
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is StatLine s) _value.Value = Math.Min(s.Value, int.MaxValue);
            UpdateButtons();
        };
        Fill();
    }

    bool Advancements => _show.SelectedItem is ComboItem { Tag: "advancements" };

    void Fill()
    {
        string filter = _filter.Text.Trim();
        bool Match(string s) => filter.Length == 0 || s.Contains(filter, StringComparison.OrdinalIgnoreCase);
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Columns.Clear();
        if (Advancements)
        {
            foreach (var (text, width) in new[] { ("Advancement", 230), ("Done", 170), ("Criteria", 90), ("What it takes", 300) }) _list.Columns.Add(text, this.Px(width));
            foreach (var a in _progress.Advancements().Where(a => Match(a.Title) || Match(a.Id)))
                _list.Items.Add(new ListViewItem([a.Title, a.Done ? "✓ " + (a.When ?? "") : "", $"{a.CriteriaDone.Count} of {Math.Max(a.Criteria.Count, a.CriteriaDone.Count)}", a.Description ?? a.Id]) { Tag = a, ForeColor = a.Done ? C.GREEN : C.FG });
        }
        else if (_show.SelectedItem is ComboItem { Tag: string category })
        {
            foreach (var (text, width) in new[] { ("Statistic", 330), ("Value", 160), ("Name in the file", 280) }) _list.Columns.Add(text, this.Px(width));
            foreach (var s in _progress.Stats().Where(s => s.Category == category && Match(s.Name)))
                _list.Items.Add(new ListViewItem([WorldSearch.Nice(s.Name.Replace("stat.", "")), s.Pretty, s.Name]) { Tag = s });
        }
        Theme.FillLastColumn(_list);
        _list.EndUpdate();
        _advancementTools.Visible = Advancements;
        _statTools.Visible = !Advancements;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        _done.Enabled = _notDone.Enabled = Advancements && _list.SelectedItems.Count > 0;
        _set.Enabled = !Advancements && _list.SelectedItems.Count > 0;
        _save.Enabled = _progress.Changed;
    }

    void Each(Action<AdvancementLine> act)
    {
        foreach (ListViewItem item in _list.SelectedItems) if (item.Tag is AdvancementLine a) Try(() => act(a));
        Fill();
    }

    void Try(Action act)
    {
        try { act(); }
        catch (ArgumentException ex) { _status.ForeColor = C.ORANGE; _status.Text = ex.Message; }
    }

    /// <summary>Whether anything was saved (the Players page then reads the players again).</summary>
    public bool Saved { get; private set; }

    void Save()
    {
        if (WorldUse.InUse(_world) is string busy) { _status.ForeColor = C.ORANGE; _status.Text = busy + " Close it first."; return; }
        try
        {
            Backup.WorldBeforeSave(_world, null);
            _progress.Save(_world.DataVersion ?? 0);
            Saved = true;
            _status.ForeColor = C.GREEN;
            _status.Text = "✓  Saved into the world (a backup was made first).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "Could not save: " + ex.Message;
        }
        UpdateButtons();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_progress.Changed && DialogResult != DialogResult.OK && Dialogs.Ask(this, "Close without saving the changes?") != DialogResult.Yes) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }
}
