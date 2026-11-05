// MinecraftStudio/Documents/LagMachinesDialog.cs: "Lag machines" — the devices built to make a world lag or crash.
// Owns: LagMachinesDialog. A window beside the world's tab (not modal, like Find) that looks through every chunk of one
// dimension or all of them (LagMachines.Find, on a world of its own read off disk) and lists each machine found: what it
// is, where, how much, and what else found there points to it. Ordinary redstone, farms and storage are left alone.
// "Show on the map" marks every machine of the chosen one's dimension and centres it; the list can be copied. "Clean up"
// takes the chosen machines (or every one of a dimension) out of a new copy of the world (LagMachines.CleanIntoCopy) and
// offers to open it; the world itself is only read.
using System.Text;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class LagMachinesDialog : DarkDialog
{
    readonly WorldDocument _doc;
    readonly DarkComboBox _dimension;
    readonly Button _look, _stop, _show, _copy, _clean;
    readonly ListView _list;
    readonly Label _summary, _details, _status;
    readonly DarkComboBox _which;
    readonly CopyFolderRow _copyTo;
    LagScan? _result;
    CancellationTokenSource? _cts;

    public LagMachinesDialog(WorldDocument doc) : base("Lag machines in " + doc.World.Name, 900)
    {
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(this.Px(900), this.Px(700));
        MinimumSize = new Size(this.Px(640), this.Px(380));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Looks for the devices built to make a world or a server lag or crash: piles of minecarts, mobs or armour stands in one block, "
                + "floods of dropped items, falling sand or lit TNT, solid blocks of pistons, observers or hoppers, machines left running when the "
                + "world was saved, command blocks summoning every tick, and book or chunk bans. Ordinary redstone, farms and storage are not counted. "
                + "The world is only read.",
            AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _dimension = new DarkComboBox { Width = this.Px(260), AccessibleName = "Dimension", Margin = new Padding(0, 0, this.Px(8), 0) };
        _dimension.Items.Add(new ComboItem("All dimensions", $"{doc.World.Dimensions.Sum(d => d.ChunkPositions.Count):N0} chunks", null));
        foreach (var d in doc.World.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, $"{d.ChunkPositions.Count:N0} chunks", d.Id));
        _dimension.SelectedIndex = 0;
        _look = Theme.Button("Look", true);
        _look.MinimumSize = new Size(this.Px(90), this.Px(30));
        _look.Click += async (_, _) => await LookAsync();
        _stop = Theme.Button("Stop");
        _stop.MinimumSize = new Size(this.Px(80), this.Px(30));
        _stop.Click += (_, _) => _cts?.Cancel();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_dimension, _look, _stop]);
        top.Controls.Add(row);

        _summary = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, this.Px(6)) };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Lag machines",
        };
        foreach (var (text, width) in new[] { ("What", 190), ("Dimension", 100), ("Where (x, y, z)", 130), ("Found", 330), ("Also there", 300) })
            _list.Columns.Add(text, this.Px(width));
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.DoubleClick += (_, _) => ShowOnMap();
        _list.SelectedIndexChanged += (_, _) => { FillWhich(); ShowDetails(); };
        // The chosen machine in full (the columns cut long sentences short).
        _details = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _copy = Theme.Button("Copy the list");
        _copy.Click += (_, _) => CopyList();
        _show = Theme.Button("Show on the map");
        _show.Click += (_, _) => ShowOnMap();
        foreach (var b in new[] { _copy, _show }) { b.MinimumSize = new Size(this.Px(130), this.Px(30)); buttons.Controls.Add(b); }

        // Cleaning up: into a new copy, like every other change.
        var clean = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, this.Px(10), 0, 0) };
        clean.Controls.Add(new SectionHeader("CLEAN UP") { Size = new Size(this.Px(860), this.Px(18)), Margin = new Padding(0, 0, 0, this.Px(6)) });
        _which = new DarkComboBox { Width = this.Px(400), AccessibleName = "Machines to clean up" };
        var whichRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        whichRow.Controls.Add(new Label { Text = "Clean up", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(90), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        whichRow.Controls.Add(_which);
        clean.Controls.Add(whichRow);
        _clean = Theme.Button("Clean up into a copy", true);
        _clean.MinimumSize = new Size(this.Px(160), this.Px(30));
        _clean.Click += async (_, _) => await CleanAsync();
        _copyTo = new CopyFolderRow(this, doc.World.Name, doc.World.Location, "Cleaned", "cleaned", 90, _clean);
        clean.Controls.Add(_copyTo);
        clean.Controls.Add(new Label
        {
            Text = "Piles are thinned to what fits in one block (24 mobs, named and tamed ones first; one minecart, boat or frame), floods are removed, "
                + "solid masses and carpets are taken away, running machines stop, summoning command blocks are emptied (their commands are listed) "
                + "and book bans lose their biggest items. Only those chunks change, in a new copy you can open; the world itself is only read.",
            AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(0, this.Px(4), 0, 0),
        });
        // Docked last first: the buttons and status at the bottom, the controls on top, the list filling the rest.
        Controls.Add(_list);
        Controls.Add(_details);
        Controls.Add(clean);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);
        FillWhich();
    }

    void UpdateButtons()
    {
        bool idle = _cts == null;
        _look.Enabled = idle;
        _stop.Enabled = !idle;
        _show.Enabled = idle && _result is { Machines.Count: > 0 };
        _copy.Enabled = idle && _result is { Machines.Count: > 0 };
        _which.Enabled = idle;
        _copyTo.Editable = idle;
        _clean.Enabled = idle && _which.Items.Count > 0;
    }

    /// <summary>What can be cleaned up in one go (one dimension at a time): the machines picked in the list, or every one of a dimension.</summary>
    void FillWhich()
    {
        string? chosen = (_which.SelectedItem as ComboItem)?.Text;
        _which.BeginUpdate();
        _which.Items.Clear();
        if (_result is { Machines.Count: > 0 } r)
        {
            var picked = _list.SelectedItems.Cast<ListViewItem>().Select(i => (LagMachine)i.Tag!).ToList();
            if (picked.Count > 0 && picked.Select(m => m.DimensionId).Distinct().Count() == 1)
                _which.Items.Add(new ComboItem(picked.Count == 1 ? "The machine picked in the list" : "The machines picked in the list", $"{picked.Count:N0}", picked));
            foreach (var g in r.Machines.GroupBy(m => m.DimensionId))
                _which.Items.Add(new ComboItem($"Every lag machine in the {DimensionName(g.Key)}", $"{g.Count():N0}", g.ToList()));
        }
        if (_which.Items.Count > 0)
            _which.SelectedIndex = Math.Max(0, Enumerable.Range(0, _which.Items.Count).FirstOrDefault(i => ((ComboItem)_which.Items[i]!).Text == chosen));
        _which.EndUpdate();
        UpdateButtons();
    }

    /// <summary>The chosen machines out of a new copy of the world, checked; offered to open afterwards.</summary>
    async Task CleanAsync()
    {
        if (_which.SelectedItem is not ComboItem { Tag: List<LagMachine> machines } || machines.Count == 0) return;
        if (_doc.World.OpenedForRescue) { Warn("This world was opened for rescue. Rescue it into a new world first, then clean up that."); return; }
        if (_copyTo.Check(out string output) is string why) { Warn(why); return; }
        string location = _doc.World.Location;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Copying the world…";
        var progress = new Progress<ConversionProgress>(p => _status.Text = p.Total > 1 && p.Done < p.Total ? $"{p.Stage} · {p.Done:N0} of {p.Total:N0}" : p.Stage);
        try
        {
            var result = await Task.Run(() =>
            {
                using var world = WorldLoader.Open(location);
                return LagMachines.CleanIntoCopy(world, machines, output, progress, token);
            }, token);
            string notes = result.Notes.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", result.Notes.Select(n => "• " + n)) : "";
            if (!result.Verified)
            {
                _status.ForeColor = C.RED;
                _status.Text = "Cleaned up, but the check of the copy found a problem. Read the report before using it.";
                Dialogs.ShowText(this, "The copy did not pass its check", string.Join("\r\n", result.Problems.Select(p => "✗ " + p)) + notes);
            }
            else
            {
                _status.ForeColor = C.GREEN;
                _status.Text = $"✓  Cleaned up {machines.Count:N0} lag machine{(machines.Count == 1 ? "" : "s")} in a copy ({result.ChunksChanged:N0} chunks rewritten, {ConvertDialog.Duration(result.Elapsed)}); none is found there again.";
                if (Dialogs.Ask(this, $"Saved the cleaned copy to:\n{result.OpenPath}{notes}\n\nOpen it now?") == DialogResult.Yes && _doc.Main is { } main)
                    await main.OpenPath(result.OpenPath);
            }
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped. The unfinished copy was removed."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Warn("The lag machines could not be cleaned up: " + ex.Message);
        }
        finally { _cts.Dispose(); _cts = null; UpdateButtons(); }
    }

    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }

    /// <summary>Looks through the dimension chosen (also run by the hidden-window checks).</summary>
    internal async Task LookAsync()
    {
        string? dimId = _dimension.SelectedItem is ComboItem { Tag: string id } ? id : null;
        string location = _doc.World.Location;
        bool rescue = _doc.World.OpenedForRescue;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            var result = await Task.Run(() =>
            {
                // A world of its own off disk: the map tab may be reading the same files (a rescued world as it is).
                using var own = rescue ? null : WorldLoader.Open(location);
                var w = own ?? _doc.World;
                var dims = dimId == null ? w.Dimensions.ToList() : w.Dimensions.Where(d => d.Id == dimId).ToList();
                return LagMachines.Find(w, dims, progress, token, shared: rescue);
            }, token);
            _result = result;
            Fill();
            FillWhich();
            _status.ForeColor = C.FG_DIM;
            _status.Text = $"Looked at {result.ChunksRead:N0} chunks." + (result.Warnings.Count > 0 ? $" {result.Warnings.Count} damaged part(s) were skipped." : "")
                + (result.Machines.Count > 0 ? " Double-click one to see it on the map." : "");
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "The world could not be looked through: " + ex.Message;
        }
        finally { _cts.Dispose(); _cts = null; UpdateButtons(); }
    }

    void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not LagMachine m) { _details.Text = ""; return; }
        _details.Text = $"{m.Title} at {m.X}, {m.Y}, {m.Z} ({DimensionName(m.DimensionId)}): {m.What}." + string.Concat(m.Evidence.Select(e => $"\n  · {e}"));
    }

    string DimensionName(string id) => _doc.World.Dimensions.FirstOrDefault(d => d.Id == id)?.DisplayName ?? id;

    void Fill()
    {
        if (_result == null) return;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var m in _result.Machines)
            _list.Items.Add(new ListViewItem([m.Title, DimensionName(m.DimensionId), $"{m.X}, {m.Y}, {m.Z}", m.What, string.Join(" · ", m.Evidence)]) { Tag = m });
        _list.EndUpdate();
        int n = _result.Machines.Count;
        _summary.ForeColor = n == 0 ? C.GREEN : C.ORANGE;
        _summary.Text = n == 0
            ? $"No lag machines in {_result.ChunksRead:N0} chunks."
            : $"{n:N0} lag machine{(n == 1 ? "" : "s")} found: " + string.Join(", ", _result.Machines.GroupBy(m => m.Title).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}"));
        if (n > 0) _list.Items[0].Selected = true;
    }

    /// <summary>Every machine in the chosen one's dimension marked on the map, the chosen one circled in the middle.</summary>
    internal void ShowOnMap()
    {
        if (_result is not { Machines.Count: > 0 } r) return;
        var chosen = _list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is LagMachine m ? m : r.Machines[0];
        var hits = r.Machines.Where(x => x.DimensionId == chosen.DimensionId)
            .Select(x => (Machine: x, Hit: new SearchHit(SearchKind.Block, x.Title, x.X, x.Y, x.Z, x.What, x.Count))).ToList();
        _doc.ShowFound(chosen.DimensionId, hits.Select(h => h.Hit).ToList(), hits.First(h => h.Machine == chosen).Hit);
    }

    void CopyList()
    {
        if (_result == null) return;
        var sb = new StringBuilder($"Lag machines in {_doc.World.Name}\r\n\r\nwhat\tdimension\tx, y, z\tfound\talso there\r\n");
        foreach (var m in _result.Machines)
            sb.Append($"{m.Title}\t{DimensionName(m.DimensionId)}\t{m.X}, {m.Y}, {m.Z}\t{m.What}\t{string.Join(" · ", m.Evidence)}\r\n");
        try { Clipboard.SetText(sb.ToString()); _status.Text = $"Copied {_result.Machines.Count:N0} lag machines."; }
        catch (System.Runtime.InteropServices.ExternalException) { _status.Text = "The clipboard is busy; try again."; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
