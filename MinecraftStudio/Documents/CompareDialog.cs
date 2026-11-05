// MinecraftStudio/Documents/CompareDialog.cs: "Compare with another world" — what changed since a backup, or what a
// conversion changed (another edition can be compared: blocks are compared by their canonical names) — and bringing
// chunks back from it.
// Owns: CompareDialog. A window beside the world's tab (not modal, like Find): the other world (a folder or a save file;
// a backup's .zip unpacked by the game-world loader), a dimension, WorldCompare on worlds of their own read off disk;
// the changed chunks listed most changed first with their commonest changes, marked on the map. "Bring chunks back"
// restores the changed chunks, those picked in the list or those selected on the map from the other world into a new
// copy (StructurePlacement.RestoreIntoCopy). Both worlds are only read.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class CompareDialog : DarkDialog
{
    readonly WorldDocument _doc;
    readonly DarkTextField _other;
    readonly DarkComboBox _dimension;
    readonly Button _browseFolder, _browseFile, _compare, _stop, _show, _restore;
    readonly ListView _list;
    readonly Label _summary, _status;
    readonly DarkComboBox _which;
    readonly DarkCheckBox _keepMobs;
    readonly CopyFolderRow _copyTo;
    CompareResult? _result;
    string? _resultDimension, _resultDimensionId, _resultOther;
    CancellationTokenSource? _cts;

    public CompareDialog(WorldDocument doc) : base("Compare " + doc.World.Name + " with another world", 860)
    {
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(this.Px(860), this.Px(680));
        MinimumSize = new Size(this.Px(640), this.Px(400));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Choose another world to compare with: a backup (what changed since?), a copy, or a conversion of this world to another edition or version (what did the conversion change?). Every chunk is compared block by block; chests and other block entities by their contents; mobs by how many of each kind. Both worlds are only read.",
            AutoSize = true, MaximumSize = new Size(this.Px(820), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _other = new DarkTextField { Width = this.Px(500), Height = this.Px(28), AccessibleName = "The world to compare with", Margin = new Padding(0, 0, this.Px(6), 0) };
        _other.Box.PlaceholderText = "A world folder or save file";
        _browseFolder = Theme.Button("Folder…");
        _browseFolder.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "The world to compare with", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) _other.Text = dlg.SelectedPath;
        };
        _browseFile = Theme.Button("File…");
        _browseFile.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "The world to compare with", Filter = "Worlds and saves|*.dat;*.mcworld;*.zip;*.bin;*.mclevel;GAMEDATA*;savegame*|All files|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _other.Text = dlg.FileName;
        };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_other, _browseFolder, _browseFile]);
        top.Controls.Add(row);
        _dimension = new DarkComboBox { Width = this.Px(260), AccessibleName = "Dimension", Margin = new Padding(0, 0, this.Px(8), 0) };
        foreach (var d in doc.World.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, $"{d.ChunkPositions.Count:N0} chunks", d));
        int current = doc.World.Dimensions.ToList().FindIndex(d => d.Id == doc.MapDimensionId);
        if (_dimension.Items.Count > 0) _dimension.SelectedIndex = Math.Max(0, current);
        _compare = Theme.Button("Compare", true);
        _compare.MinimumSize = new Size(this.Px(100), this.Px(30));
        _compare.Click += async (_, _) => await CompareAsync();
        _stop = Theme.Button("Stop");
        _stop.MinimumSize = new Size(this.Px(80), this.Px(30));
        _stop.Click += (_, _) => _cts?.Cancel();
        var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(4), 0, 0) };
        row2.Controls.AddRange([_dimension, _compare, _stop]);
        top.Controls.Add(row2);

        _summary = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(820), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, this.Px(6)) };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Chunks that differ",
        };
        foreach (var (text, width, right) in new[] { ("Chunk (x, z)", 110, false), ("In", 110, false), ("Blocks", 80, true), ("Block entities", 100, true), ("Mobs", 60, true), ("Most changed", 320, false) })
            _list.Columns.Add(text, this.Px(width), right ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.DoubleClick += (_, _) => ShowOnMap();
        _list.SelectedIndexChanged += (_, _) => FillWhich();

        // Bringing chunks back from the other world (a backup): into a new copy, like every other change.
        var restore = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, this.Px(10), 0, 0) };
        restore.Controls.Add(new SectionHeader("BRING CHUNKS BACK FROM THE OTHER WORLD") { Size = new Size(this.Px(820), this.Px(18)), Margin = new Padding(0, 0, 0, this.Px(6)) });
        _which = new DarkComboBox { Width = this.Px(330), AccessibleName = "Chunks to bring back", Margin = new Padding(0, 0, this.Px(10), 0) };
        _keepMobs = new DarkCheckBox("Keep the mobs that are there now too", isChecked: false) { AutoSize = true, Margin = new Padding(0, this.Px(6), 0, 0) };
        restore.Controls.Add(Row("Bring back", _which, _keepMobs));
        _restore = Theme.Button("Restore into a copy", true);
        _restore.MinimumSize = new Size(this.Px(150), this.Px(30));
        _restore.Click += async (_, _) => await RestoreAsync();
        _copyTo = new CopyFolderRow(this, doc.World.Name, doc.World.Location, "Restored", "restored", 90, _restore);
        restore.Controls.Add(_copyTo);
        restore.Controls.Add(new Label
        {
            Text = "Blocks, chests, item frames and mobs come back as the other world has them; nothing else changes, and both worlds are only read. A mob from the backup that is still alive somewhere else is not brought back twice.",
            AutoSize = true, MaximumSize = new Size(this.Px(820), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(0, this.Px(4), 0, 0),
        });

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(820), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _show = Theme.Button("Show on the map");
        _show.MinimumSize = new Size(this.Px(130), this.Px(30));
        _show.Click += (_, _) => ShowOnMap();
        buttons.Controls.Add(_show);
        // Docked last first: the buttons and status at the bottom, the controls on top, the list filling the rest.
        Controls.Add(_list);
        Controls.Add(restore);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);
        FillWhich();
        UpdateButtons();
    }

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(90), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    void UpdateButtons()
    {
        bool idle = _cts == null;
        foreach (var c in new Control[] { _compare, _browseFolder, _browseFile, _other, _dimension, _which, _keepMobs }) c.Enabled = idle;
        _copyTo.Editable = idle;
        _stop.Enabled = !idle;
        _show.Enabled = idle && _result is { Chunks.Count: > 0 };
        _restore.Enabled = idle && _result != null && _which.Items.Count > 0;
    }

    /// <summary>The chunk sets that can be brought back: the changed ones the other world has, those picked in the list, those selected on the map.</summary>
    void FillWhich()
    {
        int chosen = _which.SelectedItem is ComboItem { Tag: int t } ? t : 0;
        _which.BeginUpdate();
        _which.Items.Clear();
        if (_result != null)
        {
            int changed = Restorable(_result.Chunks).Count, picked = Restorable(_list.SelectedItems.Cast<ListViewItem>().Select(i => (ChunkDiff)i.Tag!)).Count;
            int onMap = _resultDimension == null ? 0 : _doc.SelectedChunks(_resultDimension).Count;
            if (changed > 0) _which.Items.Add(new ComboItem($"The changed chunks", $"{changed:N0}", 0));
            if (picked > 0) _which.Items.Add(new ComboItem("The chunks picked in the list", $"{picked:N0}", 1));
            if (onMap > 0) _which.Items.Add(new ComboItem("The chunks selected on the map", $"{onMap:N0}", 2));
        }
        if (_which.Items.Count > 0)
            _which.SelectedIndex = Math.Max(0, Enumerable.Range(0, _which.Items.Count).FirstOrDefault(i => _which.Items[i] is ComboItem { Tag: int t } && t == chosen));
        _which.EndUpdate();
        if (_restore != null) UpdateButtons();
    }

    /// <summary>The chunks the other world has (a chunk only this world has cannot come back from it).</summary>
    static List<ChunkPos> Restorable(IEnumerable<ChunkDiff> chunks) => chunks.Where(c => c.Presence != ChunkPresence.OnlyFirst).Select(c => c.Pos).ToList();

    List<ChunkPos> ChosenChunks() => (_which.SelectedItem as ComboItem)?.Tag switch
    {
        0 => Restorable(_result!.Chunks),
        1 => Restorable(_list.SelectedItems.Cast<ListViewItem>().Select(i => (ChunkDiff)i.Tag!)),
        2 => _doc.SelectedChunks(_resultDimension!).ToList(),
        _ => [],
    };

    /// <summary>The chosen chunks from the other world into a new copy of this one, checked; offered to open afterwards.</summary>
    async Task RestoreAsync()
    {
        if (_result == null || _resultOther == null || _resultDimensionId == null) return;
        FillWhich();
        var chunks = ChosenChunks();
        if (chunks.Count == 0) { Warn("Choose the chunks to bring back: all the changed ones, some picked in the list, or some selected on the map."); return; }
        if (_doc.World.OpenedForRescue) { Warn("This world was opened for rescue. Rescue it into a new world first, then bring chunks back into that."); return; }
        if (_copyTo.Check(out string output) is string why) { Warn(why); return; }
        string location = _doc.World.Location, other = _resultOther, dimId = _resultDimensionId;
        bool keep = _keepMobs.Checked;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Opening the worlds…";
        var progress = new Progress<ConversionProgress>(p => _status.Text = p.Total > 1 && p.Done < p.Total ? $"{p.Stage} · {p.Done:N0} of {p.Total:N0}" : p.Stage);
        try
        {
            var result = await Task.Run(() =>
            {
                using var world = WorldLoader.Open(location);
                using var backup = WorldLoader.Open(other);
                return StructurePlacement.RestoreIntoCopy(world, backup, dimId, chunks, output, keep, progress, token);
            }, token);
            string notes = result.Notes.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", result.Notes.Select(n => "• " + n)) : "";
            if (!result.Verified)
            {
                _status.ForeColor = C.RED;
                _status.Text = "Restored, but the check of the copy found a problem. Read the report before using it.";
                Dialogs.ShowText(this, "The copy did not pass its check", string.Join("\r\n", result.Problems.Select(p => "✗ " + p)) + notes);
            }
            else
            {
                _status.ForeColor = C.GREEN;
                _status.Text = $"✓  Brought {result.ChunksChanged:N0} chunk{(result.ChunksChanged == 1 ? "" : "s")} back into a copy ({ConvertDialog.Duration(result.Elapsed)}); everything else is unchanged.";
                if (Dialogs.Ask(this, $"Saved the copy with the chunks brought back to:\n{result.OpenPath}{notes}\n\nOpen it now?") == DialogResult.Yes && _doc.Main is { } main)
                    await main.OpenPath(result.OpenPath);
            }
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped. The unfinished copy was removed."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SaveFormatException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Warn("The chunks could not be brought back: " + ex.Message);
        }
        finally { _cts.Dispose(); _cts = null; UpdateButtons(); }
    }

    /// <summary>Compares with a world or backup given (the Backups window's "Compare with this backup").</summary>
    internal void CompareWith(string path)
    {
        if (_cts != null) return;
        _other.Text = path;
        _ = CompareAsync();
    }

    internal async Task CompareAsync()
    {
        string other = _other.Text.Trim();
        if (other.Length == 0 || !File.Exists(other) && !Directory.Exists(other)) { Warn("Choose the world to compare with."); return; }
        if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension dim }) return;
        string dimId = CanonicalReader.DimensionId(dim), location = _doc.World.Location;
        bool rescue = _doc.World.OpenedForRescue;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Opening the worlds…";
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            var result = await Task.Run(() =>
            {
                // Worlds of their own off disk (the map tab may be reading this one's files).
                using var own = rescue ? null : WorldLoader.Open(location);
                var a = own ?? _doc.World;
                using var b = WorldLoader.Open(other);
                var da = a.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimId);
                var db = b.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimId || CanonicalReader.DimensionId(d) == JavaWorldWriter.OutputDimensionId(dimId))
                    ?? throw new InvalidOperationException($"The other world has no {dim.DisplayName}.");
                return WorldCompare.Run(a, da, b, db, progress: progress, cancel: token);
            }, token);
            _result = result;
            _resultDimension = dim.Id;
            _resultDimensionId = dimId;
            _resultOther = other;
            Fill();
            FillWhich();
            _status.ForeColor = C.FG_DIM;
            _status.Text = $"Compared {result.Compared:N0} chunks in both worlds." + (result.Warnings.Count > 0 ? $" {result.Warnings.Count} damaged part(s) were skipped." : "") + (result.Chunks.Count > 0 ? " Double-click a chunk to see it on the map." : "");
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            Warn("The worlds could not be compared: " + ex.Message);
        }
        finally { _cts.Dispose(); _cts = null; UpdateButtons(); }
    }

    void Fill()
    {
        if (_result == null) return;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var c in _result.Chunks.Take(5000))
            _list.Items.Add(new ListViewItem([$"{c.Pos.X}, {c.Pos.Z}", c.Presence switch { ChunkPresence.OnlyFirst => "only this world", ChunkPresence.OnlySecond => "only the other", _ => "both" },
                c.Blocks.ToString("N0"), c.BlockEntities.ToString("N0"), c.Entities.ToString("N0"),
                string.Join(", ", c.Top.Select(t => $"{t.Count:N0} {WorldSearch.Nice(t.From)} → {WorldSearch.Nice(t.To)}"))]) { Tag = c });
        _list.EndUpdate();
        int onlyA = _result.Chunks.Count(c => c.Presence == ChunkPresence.OnlyFirst), onlyB = _result.Chunks.Count(c => c.Presence == ChunkPresence.OnlySecond);
        int both = _result.Chunks.Count - onlyA - onlyB;
        _summary.Text = _result.Chunks.Count == 0 ? $"✓  No differences: all {_result.Compared:N0} chunks are the same."
            : $"{both:N0} chunk{(both == 1 ? "" : "s")} differ ({_result.BlocksChanged:N0} blocks; most: {string.Join(", ", _result.Changes.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Value:N0} {WorldSearch.Nice(kv.Key.From)} → {WorldSearch.Nice(kv.Key.To)}"))})"
              + (onlyA > 0 ? $"; {onlyA:N0} only in this world" : "") + (onlyB > 0 ? $"; {onlyB:N0} only in the other" : "") + $"; {_result.Same:N0} the same.";
        _summary.ForeColor = _result.Chunks.Count == 0 ? C.GREEN : C.FG;
    }

    void ShowOnMap()
    {
        if (_result is not { Chunks.Count: > 0 } r || _resultDimension == null) return;
        var hits = r.Chunks.Take(5000).Select(c => new SearchHit(SearchKind.Block, $"{c.Total:N0} changes", c.Pos.X * 16 + 8, 64, c.Pos.Z * 16 + 8, $"chunk {c.Pos.X}, {c.Pos.Z}", c.Total)).ToList();
        int chosen = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : 0;
        _doc.ShowFound(_resultDimension, hits, hits[Math.Min(chosen, hits.Count - 1)]);
    }

    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
