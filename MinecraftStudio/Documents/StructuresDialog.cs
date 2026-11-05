// MinecraftStudio/Documents/StructuresDialog.cs: "Structures" — every structure of a world in a list.
// Owns: StructuresDialog. A window beside the world's tab (not modal, like Find): the structures of one dimension or all of
// them (WorldDocument.StructuresOf: read once in the background and kept), by kind, with how many of their loot containers
// were never opened ("only with loot not taken yet" for treasure hunting). "Show on the map" turns the map's structure
// markers on and circles the chosen one; the list can be copied. Only reads.
using System.Text;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class StructuresDialog : DarkDialog
{
    readonly WorldDocument _doc;
    readonly DarkComboBox _dimension, _kind;
    readonly DarkCheckBox _lootOnly;
    readonly ListView _list;
    readonly Label _summary, _details, _status;
    readonly Button _show, _copy;
    List<StructureSite> _all = [];
    int _loading;

    public StructuresDialog(WorldDocument doc) : base("Structures in " + doc.World.Name, 900)
    {
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(this.Px(900), this.Px(560));
        MinimumSize = new Size(this.Px(640), this.Px(380));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Every structure the world has saved — villages, outposts, shipwrecks, buried treasure, temples, monuments, strongholds, mansions, "
                + "mineshafts, fortresses, bastions, end cities, ancient cities, trail ruins, trial chambers, ruined portals — and those told by what they "
                + "left: unopened chests, spawners, vaults (Bedrock saves only a few structures, so most show this way). Only land that has been generated "
                + "holds them. The world is only read.",
            AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _dimension = new DarkComboBox { Width = this.Px(240), AccessibleName = "Dimension", Margin = new Padding(0, 0, this.Px(8), 0) };
        _dimension.Items.Add(new ComboItem("All dimensions", null, null));
        foreach (var d in doc.World.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, $"{d.ChunkPositions.Count:N0} chunks", d.Id));
        _dimension.SelectedIndex = 0;
        _kind = new DarkComboBox { Width = this.Px(240), AccessibleName = "Kind of structure", Margin = new Padding(0, 0, this.Px(8), 0) };
        _kind.Items.Add(new ComboItem("Every kind", null, null));
        _kind.SelectedIndex = 0;
        _lootOnly = new DarkCheckBox("Only with loot not taken yet", isChecked: false) { AutoSize = true, Margin = new Padding(0, this.Px(6), 0, 0) };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_dimension, _kind, _lootOnly]);
        top.Controls.Add(row);

        _summary = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, this.Px(6)) };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Structures",
        };
        foreach (var (text, width, right) in new[] { ("Structure", 210, false), ("Dimension", 100, false), ("Where (x, y, z)", 140, false), ("Not looted", 90, true), ("Found by", 330, false) })
            _list.Columns.Add(text, this.Px(width), right ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.DoubleClick += (_, _) => ShowOnMap();
        _list.SelectedIndexChanged += (_, _) => { UpdateButtons(); ShowDetails(); };
        _details = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _copy = Theme.Button("Copy the list");
        _copy.Click += (_, _) => CopyList();
        _show = Theme.Button("Show on the map");
        _show.Click += (_, _) => ShowOnMap();
        foreach (var b in new[] { _copy, _show }) { b.MinimumSize = new Size(this.Px(130), this.Px(30)); buttons.Controls.Add(b); }
        // Docked last first: the buttons and status at the bottom, the controls on top, the list filling the rest.
        Controls.Add(_list);
        Controls.Add(_details);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);

        _dimension.SelectedIndexChanged += async (_, _) => await LoadAsync();
        _kind.SelectedIndexChanged += (_, _) => Fill();
        _lootOnly.CheckedChanged += (_, _) => Fill();
        UpdateButtons();
        FirstLoad = LoadAsync();
    }

    /// <summary>The first reading of the structures (the hidden-window checks wait for it).</summary>
    internal Task FirstLoad { get; }

    void UpdateButtons()
    {
        _show.Enabled = _list.SelectedItems.Count > 0;
        _copy.Enabled = _list.Items.Count > 0;
    }

    /// <summary>The structures of the dimension chosen (or of all), read through the world tab (once, then kept).</summary>
    async Task LoadAsync()
    {
        int ticket = ++_loading;
        string? dimId = _dimension.SelectedItem is ComboItem { Tag: string id } ? id : null;
        var dims = _doc.World.Dimensions.Where(d => dimId == null || d.Id == dimId).ToList();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Reading the structures…";
        var progress = new Progress<string>(s => { if (ticket == _loading) _status.Text = s; });
        var all = new List<StructureSite>();
        try
        {
            foreach (var d in dims) all.AddRange(await _doc.StructuresOf(d.Id, progress));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Core.IO.SaveFormatException or InvalidOperationException)
        {
            if (ticket == _loading) { _status.ForeColor = C.ORANGE; _status.Text = "The structures could not be read: " + ex.Message; }
            return;
        }
        if (IsDisposed || ticket != _loading) return;
        _all = all;
        // The kinds there are, most first; the one chosen stays chosen.
        string? chosen = _kind.SelectedItem is ComboItem { Tag: string k } ? k : null;
        _kind.BeginUpdate();
        _kind.Items.Clear();
        _kind.Items.Add(new ComboItem("Every kind", $"{all.Count:N0}", null));
        foreach (var g in all.GroupBy(s => s.Key).OrderByDescending(g => g.Count()).ThenBy(g => StructureKinds.Name(g.Key), StringComparer.Ordinal))
            _kind.Items.Add(new ComboItem(StructureKinds.Name(g.Key), $"{g.Count():N0}", g.Key));
        _kind.SelectedIndex = Math.Max(0, Enumerable.Range(0, _kind.Items.Count).FirstOrDefault(i => _kind.Items[i] is ComboItem { Tag: string t } && t == chosen));
        _kind.EndUpdate();
        Fill();
        _status.Text = "Double-click one to see it on the map. Only generated land holds structures.";
    }

    string DimensionName(string id) => _doc.World.Dimensions.FirstOrDefault(d => d.Id == id)?.DisplayName ?? id;

    void Fill()
    {
        string? kind = _kind.SelectedItem is ComboItem { Tag: string k } ? k : null;
        var shown = _all.Where(s => (kind == null || s.Key == kind) && (!_lootOnly.Checked || s.Unlooted > 0)).ToList();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var s in shown)
            _list.Items.Add(new ListViewItem([s.Name, DimensionName(s.DimensionId), $"{s.X}, {s.Y}, {s.Z}", s.Unlooted > 0 ? s.Unlooted.ToString("N0") : "", string.Join("; ", s.Notes)]) { Tag = s });
        _list.EndUpdate();
        int loot = shown.Count(s => s.Unlooted > 0);
        _summary.Text = shown.Count == 0 ? "No structures to show." :
            $"{shown.Count:N0} structure{(shown.Count == 1 ? "" : "s")}" + (loot > 0 ? $", {loot:N0} with loot not taken yet" : "") + ": "
            + string.Join(", ", shown.GroupBy(s => StructureKinds.Name(s.Key)).OrderByDescending(g => g.Count()).Take(10).Select(g => $"{g.Count():N0} × {g.Key}"));
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        UpdateButtons();
        ShowDetails();
    }

    void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not StructureSite s) { _details.Text = ""; return; }
        var b = s.Box;
        _details.Text = $"{s.Name} at {s.X}, {s.Y}, {s.Z} ({DimensionName(s.DimensionId)}), from {b.MinX}, {b.MinY}, {b.MinZ} to {b.MaxX}, {b.MaxY}, {b.MaxZ}"
            + (s.Unlooted > 0 ? $": {s.Unlooted:N0} loot container{(s.Unlooted == 1 ? "" : "s")} never opened" : "") + $". {Capital(string.Join("; ", s.Notes))}.";
    }

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>The map's structure markers on, the chosen one circled in the middle.</summary>
    internal void ShowOnMap()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not StructureSite s) return;
        _doc.StructuresShown = true;
        var hit = new SearchHit(SearchKind.Block, s.Name, s.X, s.Y, s.Z, string.Join("; ", s.Notes), Math.Max(1, s.Unlooted));
        _doc.ShowFound(s.DimensionId, [hit], hit);
    }

    void CopyList()
    {
        var sb = new StringBuilder($"Structures in {_doc.World.Name}\r\n\r\nstructure\tdimension\tx, y, z\tnot looted\tfound by\r\n");
        foreach (ListViewItem item in _list.Items)
            if (item.Tag is StructureSite s) sb.Append($"{s.Name}\t{DimensionName(s.DimensionId)}\t{s.X}, {s.Y}, {s.Z}\t{s.Unlooted}\t{string.Join("; ", s.Notes)}\r\n");
        try { Clipboard.SetText(sb.ToString()); _status.Text = $"Copied {_list.Items.Count:N0} structures."; }
        catch (System.Runtime.InteropServices.ExternalException) { _status.Text = "The clipboard is busy; try again."; }
    }
}
