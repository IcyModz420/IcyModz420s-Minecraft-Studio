// MinecraftStudio/Documents/BusyChunksDialog.cs: "Busiest chunks" — where a world's lag comes from.
// Owns: BusyChunksDialog. A window beside the world's tab (not modal, like Find) that counts every chunk of a dimension
// (ChunkCensus, on a world of its own read off disk) and lists the busiest — mobs and other entities, dropped items and
// experience orbs, hoppers, chests and other block entities, and what is most common in each — with the totals by kind.
// "Show on the map" marks them all and centres the chosen one; the list can be copied. "Clear" takes the dropped items and
// experience orbs out of the chosen chunks in a new copy of the world (LagMachines.CleanIntoCopy, the lag machines' own
// clean-up); the world itself is only read.
using System.Text;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class BusyChunksDialog : DarkDialog
{
    readonly WorldDocument _doc;
    readonly DarkComboBox _dimension;
    readonly Button _count, _stop, _show, _copy, _clear;
    readonly ListView _list;
    readonly Label _summary, _status;
    readonly DarkComboBox _which;
    readonly CopyFolderRow _copyTo;
    CensusResult? _result;
    string? _resultDimension;
    CancellationTokenSource? _cts;

    public BusyChunksDialog(WorldDocument doc) : base("Busiest chunks in " + doc.World.Name, 820)
    {
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(this.Px(820), this.Px(660));
        MinimumSize = new Size(this.Px(600), this.Px(380));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Every chunk is counted: mobs and other entities, dropped items and experience orbs, and hoppers, chests, furnaces and the other block entities. The busiest chunks are the first places to look when a world or a server runs slowly. The world is only read.",
            AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _dimension = new DarkComboBox { Width = this.Px(260), AccessibleName = "Dimension", Margin = new Padding(0, 0, this.Px(8), 0) };
        foreach (var d in doc.World.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, $"{d.ChunkPositions.Count:N0} chunks", d.Id));
        int current = doc.World.Dimensions.ToList().FindIndex(d => d.Id == doc.MapDimensionId);
        if (_dimension.Items.Count > 0) _dimension.SelectedIndex = Math.Max(0, current);
        _count = Theme.Button("Count", true);
        _count.MinimumSize = new Size(this.Px(90), this.Px(30));
        _count.Click += async (_, _) => await CountAsync();
        _stop = Theme.Button("Stop");
        _stop.MinimumSize = new Size(this.Px(80), this.Px(30));
        _stop.Enabled = false;
        _stop.Click += (_, _) => _cts?.Cancel();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_dimension, _count, _stop]);
        top.Controls.Add(row);

        _summary = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, this.Px(6)) };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Busiest chunks",
        };
        foreach (var (text, width, right) in new[] { ("Chunk (x, z)", 120, false), ("Blocks (x, z)", 130, false), ("Entities", 80, true), ("Dropped", 80, true), ("Block entities", 100, true), ("Most common", 280, false) })
            _list.Columns.Add(text, this.Px(width), right ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.DoubleClick += (_, _) => ShowOnMap();
        _list.SelectedIndexChanged += (_, _) => FillWhich();

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(780), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _copy = Theme.Button("Copy the list");
        _copy.Click += (_, _) => CopyList();
        _show = Theme.Button("Show on the map");
        _show.Click += (_, _) => ShowOnMap();
        foreach (var b in new[] { _copy, _show }) { b.MinimumSize = new Size(this.Px(130), this.Px(30)); buttons.Controls.Add(b); }

        // Clearing dropped items and orbs: into a new copy, like every other change.
        var clear = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, this.Px(10), 0, 0) };
        clear.Controls.Add(new SectionHeader("CLEAR DROPPED ITEMS AND EXPERIENCE ORBS") { Size = new Size(this.Px(780), this.Px(18)), Margin = new Padding(0, 0, 0, this.Px(6)) });
        _which = new DarkComboBox { Width = this.Px(360), AccessibleName = "Chunks to clear" };
        var whichRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        whichRow.Controls.Add(new Label { Text = "Clear", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(90), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        whichRow.Controls.Add(_which);
        clear.Controls.Add(whichRow);
        _clear = Theme.Button("Clear into a copy", true);
        _clear.MinimumSize = new Size(this.Px(150), this.Px(30));
        _clear.Click += async (_, _) => await ClearAsync();
        _copyTo = new CopyFolderRow(this, doc.World.Name, doc.World.Location, "Cleaned", "cleaned", 90, _clear);
        clear.Controls.Add(_copyTo);
        // Docked last first: the buttons and status at the bottom, the controls on top, the list filling the rest.
        Controls.Add(_list);
        Controls.Add(clear);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);
        FillWhich();
    }

    void UpdateButtons()
    {
        bool idle = _cts == null;
        _count.Enabled = idle;
        _stop.Enabled = !idle;
        _show.Enabled = idle && _result is { Chunks.Count: > 0 };
        _copy.Enabled = idle && _result is { Chunks.Count: > 0 };
        _which.Enabled = idle;
        _copyTo.Editable = idle;
        _clear.Enabled = idle && _which.Items.Count > 0;
    }

    /// <summary>The chunks whose dropped items and orbs can be cleared: those picked in the list, or every listed one that has any.</summary>
    void FillWhich()
    {
        bool every = (_which.SelectedItem as ComboItem)?.Text.StartsWith("Every", StringComparison.Ordinal) == true;
        _which.BeginUpdate();
        _which.Items.Clear();
        if (_result != null)
        {
            var picked = _list.SelectedItems.Cast<ListViewItem>().Select(i => (BusyChunk)i.Tag!).Where(b => b.Dropped > 0).Select(b => b.Pos).ToList();
            var all = _result.Chunks.Where(b => b.Dropped > 0).Select(b => b.Pos).ToList();
            if (picked.Count > 0) _which.Items.Add(new ComboItem(picked.Count == 1 ? "The chunk picked in the list" : "The chunks picked in the list", $"{picked.Count:N0}", picked));
            if (all.Count > 0) _which.Items.Add(new ComboItem("Every listed chunk that has some", $"{all.Count:N0}", all));
        }
        if (_which.Items.Count > 0) _which.SelectedIndex = every ? _which.Items.Count - 1 : 0;
        _which.EndUpdate();
        UpdateButtons();
    }

    /// <summary>The chosen chunks' dropped items and experience orbs out of a new copy of the world, checked; offered to open afterwards.</summary>
    async Task ClearAsync()
    {
        if (_which.SelectedItem is not ComboItem { Tag: List<ChunkPos> chunks } || chunks.Count == 0 || _resultDimension == null) return;
        if (_doc.World.OpenedForRescue) { Warn("This world was opened for rescue. Rescue it into a new world first, then clear that."); return; }
        if (_copyTo.Check(out string output) is string why) { Warn(why); return; }
        string location = _doc.World.Location, dimId = _resultDimension;
        // The lag machines' own clean-up, told to clear these chunks' items and orbs.
        var machines = chunks.SelectMany(p => new[] { LagKind.DroppedItems, LagKind.ExperienceOrbs }.Select(k =>
            new LagMachine(k, dimId, "", p.X * 16 + 8, 64, p.Z * 16 + 8, (p.X * 16, 64, p.Z * 16, p.X * 16 + 15, 64, p.Z * 16 + 15), 0, []))).ToList();
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
                _status.Text = "Cleared, but the check of the copy found a problem. Read the report before using it.";
                Dialogs.ShowText(this, "The copy did not pass its check", string.Join("\r\n", result.Problems.Select(p => "✗ " + p)) + notes);
            }
            else
            {
                _status.ForeColor = C.GREEN;
                _status.Text = $"✓  Cleared {chunks.Count:N0} chunk{(chunks.Count == 1 ? "" : "s")} in a copy ({ConvertDialog.Duration(result.Elapsed)}); everything else is unchanged.";
                if (Dialogs.Ask(this, $"Saved the cleared copy to:\n{result.OpenPath}{notes}\n\nOpen it now?") == DialogResult.Yes && _doc.Main is { } main)
                    await main.OpenPath(result.OpenPath);
            }
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped. The unfinished copy was removed."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Warn("The chunks could not be cleared: " + ex.Message);
        }
        finally { _cts.Dispose(); _cts = null; UpdateButtons(); }
    }

    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }

    async Task CountAsync()
    {
        if (_dimension.SelectedItem is not ComboItem { Tag: string dimId }) return;
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
                var dim = w.Dimensions.First(d => d.Id == dimId);
                return ChunkCensus.Run(w, dim, 300, progress, token);
            }, token);
            _result = result;
            _resultDimension = dimId;
            Fill();
            FillWhich();
            _status.ForeColor = C.FG_DIM;
            _status.Text = $"Counted {result.ChunksRead:N0} chunks." + (result.Warnings.Count > 0 ? $" {result.Warnings.Count} damaged part(s) were skipped." : "") + " Double-click a chunk to see it on the map.";
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "The chunks could not be counted: " + ex.Message;
        }
        finally { _cts.Dispose(); _cts = null; UpdateButtons(); }
    }

    void Fill()
    {
        if (_result == null) return;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var b in _result.Chunks)
            _list.Items.Add(new ListViewItem([$"{b.Pos.X}, {b.Pos.Z}", $"{b.Pos.X * 16}, {b.Pos.Z * 16}", b.Entities.ToString("N0"), b.Dropped.ToString("N0"), b.BlockEntities.ToString("N0"),
                string.Join(", ", b.Most.Select(m => $"{m.Count:N0} {WorldSearch.Nice(m.Id)}"))]) { Tag = b });
        _list.EndUpdate();
        long entities = _result.EntityTotals.Values.Sum(), tiles = _result.BlockEntityTotals.Values.Sum();
        string Top(Dictionary<string, long> d) => string.Join(", ", d.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Value:N0} {WorldSearch.Nice(kv.Key)}"));
        _summary.Text = $"{entities:N0} entities ({Top(_result.EntityTotals)})  ·  {tiles:N0} block entities ({Top(_result.BlockEntityTotals)})";
    }

    /// <summary>Every listed chunk marked on the map, the chosen one (or the busiest) circled in the middle.</summary>
    void ShowOnMap()
    {
        if (_result is not { Chunks.Count: > 0 } r || _resultDimension == null) return;
        var hits = r.Chunks.Select(b => new SearchHit(SearchKind.Entity, $"{b.Load:N0} things", b.Pos.X * 16 + 8, 64, b.Pos.Z * 16 + 8, $"chunk {b.Pos.X}, {b.Pos.Z}", b.Load)).ToList();
        int chosen = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : 0;
        _doc.ShowFound(_resultDimension, hits, hits[chosen]);
    }

    void CopyList()
    {
        if (_result == null) return;
        var sb = new StringBuilder($"Busiest chunks of {_doc.World.Name} ({_resultDimension})\r\n\r\nchunk x, z\tentities\tdropped\tblock entities\tmost common\r\n");
        foreach (var b in _result.Chunks)
            sb.Append($"{b.Pos.X}, {b.Pos.Z}\t{b.Entities}\t{b.Dropped}\t{b.BlockEntities}\t{string.Join(", ", b.Most.Select(m => $"{m.Count} {WorldSearch.Nice(m.Id)}"))}\r\n");
        try { Clipboard.SetText(sb.ToString()); _status.Text = $"Copied {_result.Chunks.Count:N0} chunks."; }
        catch (System.Runtime.InteropServices.ExternalException) { _status.Text = "The clipboard is busy; try again."; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
