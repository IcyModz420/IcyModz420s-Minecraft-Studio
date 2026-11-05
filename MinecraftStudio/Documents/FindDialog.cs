// MinecraftStudio/Documents/FindDialog.cs: "Find in this world" — blocks, items (in chests, in shulker boxes inside them,
// in item frames, dropped, on mobs, in players' inventories and ender chests), mobs and other entities, and text (signs,
// command blocks, names, books).
// Owns: FindDialog. A window beside the world's tab, not modal, so the map can be looked at while it is open. The search
// reads a world of its own off disk (as the structure dialogs do), so the tab's world is never touched; the results are
// marked on the map, can be copied or saved as a list, and open in the NBT editor (their chunk) or the Players page.
// "Only pets" lists the tamed mobs with their owners (villagers always show their job and trades); "Bring the pets home"
// puts each beside its owner in a new copy of the world (PetsHome).
using System.Text;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class FindDialog : DarkDialog
{
    readonly WorldDocument _doc;
    readonly DarkTextField _query;
    readonly DarkCheckBox _blocks, _items, _entities, _text, _players, _whole, _selectionOnly, _pets;
    readonly DarkComboBox _dimension;
    readonly Button _find, _stop, _show, _copy, _save, _nbt, _home;
    readonly ListView _list;
    readonly Label _summary, _status;
    List<SearchHit> _hits = [];
    SearchResult? _result;
    string? _resultDimension;
    int _sortColumn = -1;
    bool _sortDown;
    CancellationTokenSource? _cts;

    public FindDialog(WorldDocument doc) : base("Find in " + doc.World.Name, 860)
    {
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(this.Px(860), this.Px(620));
        MinimumSize = new Size(this.Px(640), this.Px(420));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Type part of a name — diamond, elytra, spawner, villager, shulker — or words written on a sign or in a book. Items are found in chests, in shulker boxes inside them, in item frames, dropped on the ground, on mobs, and in every player's inventory and ender chest. A * stands for anything: *_ore finds every ore, and * alone counts everything (the blocks of the chunks selected, or every mob).",
            AutoSize = true, MaximumSize = new Size(this.Px(820), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _query = new DarkTextField { Width = this.Px(470), Height = this.Px(30), Margin = new Padding(0, 0, this.Px(8), 0) };
        _query.Box.AccessibleName = "What to find";
        _query.Box.PlaceholderText = "diamond, elytra, spawner, villager, a sign's words…";
        _query.Box.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Find(); } };
        _find = Theme.Button("Find", true);
        _find.MinimumSize = new Size(this.Px(90), this.Px(30));
        _find.Click += async (_, _) => await Find();
        _stop = Theme.Button("Stop");
        _stop.MinimumSize = new Size(this.Px(80), this.Px(30));
        _stop.Enabled = false;
        _stop.Click += (_, _) => _cts?.Cancel();
        top.Controls.Add(Row(_query, _find, _stop));

        _blocks = Check("Blocks", true);
        _items = Check("Items", true);
        _entities = Check("Mobs and entities", true);
        _text = Check("Text", true);
        _players = Check("Players' inventories", true);
        _whole = Check("Whole name only", false);
        top.Controls.Add(Row(_blocks, _items, _entities, _text, _players, _whole));
        // Pets: only the tamed mobs (with who owns them); the owners can have them brought home, in a copy.
        _pets = Check("Only pets — tamed animals and who owns them", false);
        _pets.CheckedChanged += (_, _) =>
        {
            foreach (var c in new Control[] { _blocks, _items, _text, _players }) c.Enabled = !_pets.Checked;
            if (_pets.Checked) _entities.Checked = true;
            if (_pets.Checked && _query.Text.Trim().Length == 0) _query.Text = "*";
        };
        top.Controls.Add(Row(_pets));

        _dimension = new DarkComboBox { Width = this.Px(230), AccessibleName = "Dimension to search" };
        foreach (var d in doc.World.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, $"{d.ChunkPositions.Count:N0} chunks", d.Id));
        int current = doc.MapDimensionId is string id ? doc.World.Dimensions.ToList().FindIndex(d => d.Id == id) : -1;
        if (_dimension.Items.Count > 0) _dimension.SelectedIndex = Math.Max(0, current);
        _selectionOnly = Check("Only the chunks selected on the map", false);
        top.Controls.Add(Row(new Label { Text = "In", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, this.Px(7), this.Px(6), 0) }, _dimension, _selectionOnly));

        _summary = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(820), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, this.Px(6)) };

        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, MultiSelect = false, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "What was found",
        };
        foreach (var (text, width) in new[] { ("What", 210), ("How many", 80), ("x", 70), ("y", 50), ("z", 70), ("Where", 330) })
            _list.Columns.Add(text, this.Px(width));
        Theme.DarkControl(_list);
        Theme.FillLastColumn(_list);
        _list.RetrieveVirtualItem += (_, e) => e.Item = Item(_hits[e.ItemIndex]);
        _list.ColumnClick += (_, e) => Sort(e.Column);
        _list.DoubleClick += (_, _) => ShowOnMap();
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(820), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _save = Theme.Button("Save the list…");
        _save.Click += (_, _) => SaveList();
        _copy = Theme.Button("Copy coordinates");
        _copy.Click += (_, _) => CopyCoordinates();
        _show = Theme.Button("Show on the map");
        _show.Click += (_, _) => ShowOnMap();
        _nbt = Theme.Button("Open in the NBT editor");
        _nbt.Click += (_, _) => OpenNbt();
        _home = Theme.Button("Bring the pets home…");
        _home.Click += async (_, _) => await BringPetsHomeAsync();
        foreach (var b in new[] { _save, _copy, _nbt, _home, _show }) { b.MinimumSize = new Size(this.Px(120), this.Px(30)); buttons.Controls.Add(b); }

        Controls.Add(_list);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);
        CancelButton = null;
        UpdateButtons();
        Shown += (_, _) => _query.Box.Focus();
        FormClosing += (_, _) => _cts?.Cancel();
    }

    DarkCheckBox Check(string text, bool on) => new(text, on) { AutoSize = true, Margin = new Padding(0, this.Px(4), this.Px(14), this.Px(4)) };

    FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.AddRange(controls);
        return row;
    }

    /// <summary>
    /// Starts a search for the text given (from another part of the program, such as a player's item), in the dimension
    /// given (its id as the world names it) or the one chosen.
    /// </summary>
    public void FindNow(string query, string? dimensionId = null)
    {
        _query.Text = query;
        if (dimensionId != null)
            for (int i = 0; i < _dimension.Items.Count; i++)
                if (_dimension.Items[i] is ComboItem { Tag: string id } && id == dimensionId) { _dimension.SelectedIndex = i; break; }
        _ = Find();
    }

    async Task Find()
    {
        if (_cts != null) return;
        string q = _query.Text.Trim();
        if (q.Length == 0) { Status("Type what to look for: part of a name (diamond, elytra, spawner, villager) or words from a sign.", C.ORANGE); return; }
        if (!(_blocks.Checked || _items.Checked || _entities.Checked || _text.Checked)) { Status("Tick at least one kind of thing to look for.", C.ORANGE); return; }
        if (_dimension.SelectedItem is not ComboItem { Tag: string dimensionId }) return;
        IReadOnlyCollection<ChunkPos>? chunks = null;
        if (_selectionOnly.Checked)
        {
            chunks = _doc.SelectedChunks(dimensionId);
            if (chunks.Count == 0) { Status("No chunks of that dimension are selected on the map: select some there first, or untick \"Only the chunks selected\".", C.ORANGE); return; }
        }
        bool pets = _pets.Checked;
        var options = new SearchOptions
        {
            Query = q, WholeName = _whole.Checked, Blocks = _blocks.Checked && !pets, Items = _items.Checked && !pets, Entities = _entities.Checked || pets,
            Text = _text.Checked && !pets, Players = _players.Checked && chunks == null && !pets, Chunks = chunks, PetsOnly = pets,
        };
        var world = _doc.World;
        string location = world.Location;
        bool rescue = world.OpenedForRescue;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        var progress = new Progress<string>(s => { if (_cts != null) Status(s, C.FG_DIM); });
        try
        {
            var result = await Task.Run(() =>
            {
                // A world of its own, read off disk (a world opened for rescue is only ever read, so that one is used).
                using var own = rescue ? null : WorldLoader.Open(location);
                var w = own ?? world;
                var dim = w.Dimensions.FirstOrDefault(d => d.Id == dimensionId) ?? w.Dimensions[0];
                return WorldSearch.Run(w, dim, options, progress, token);
            }, token);
            if (!IsDisposed) ShowResult(result, dimensionId);
        }
        catch (OperationCanceledException) { if (!IsDisposed) Status("Stopped. Nothing in the world was changed.", C.FG_DIM); }
        catch (Exception ex)
        {
            if (ex is not (IOException or SaveFormatException or UnauthorizedAccessException or InvalidDataException)) Log.Error("Find in world failed", ex);
            if (!IsDisposed) Status("The search stopped: " + ex.Message, C.ORANGE);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        _find.Enabled = !busy;
        _stop.Enabled = busy;
        _query.Enabled = !busy;
        UseWaitCursor = busy;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bool any = _hits.Count > 0 && _cts == null;
        _show.Enabled = any;
        _copy.Enabled = any && _list.SelectedIndices.Count > 0;
        _nbt.Enabled = any && _list.SelectedIndices.Count > 0;
        _save.Enabled = any;
        // Pets with an owner who is a player of the world can be brought to them.
        _home.Enabled = any && _hits.Any(h => h.Kind == SearchKind.Entity && h.Where.Contains("tamed by ", StringComparison.Ordinal) && !h.Where.Contains("not in this world", StringComparison.Ordinal));
    }

    /// <summary>
    /// The pets of the dimension searched whose owners stand in it brought beside them, in a new copy (PetsHome); the world
    /// itself is only read. Offered to open afterwards.
    /// </summary>
    async Task BringPetsHomeAsync()
    {
        if (_resultDimension == null || _cts != null) return;
        var world = _doc.World;
        if (world.OpenedForRescue) { Status("This world was opened for rescue. Rescue it into a new world first, then bring the pets home in that.", C.ORANGE); return; }
        using var dlg = new FolderBrowserDialog { Description = "Where the copy with the pets brought home goes (a new folder is made in it)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string output = Path.Combine(dlg.SelectedPath, SafeName.For(world.Name + " pets home", "World"));
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) { Status($"{output} is there already; choose another folder.", C.ORANGE); return; }
        string location = world.Location, dimensionId = _resultDimension;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        var progress = new Progress<ConversionProgress>(p => { if (_cts != null) Status(p.Stage, C.FG_DIM); });
        try
        {
            var (moves, result) = await Task.Run(() =>
            {
                using var w = WorldLoader.Open(location);
                var dim = w.Dimensions.First(d => d.Id == dimensionId);
                var moves = PetsHome.Plan(w, dim, cancel: token);
                return (moves, moves.Count == 0 ? null : PetsHome.BringIntoCopy(w, dim, moves, output, progress, token));
            }, token);
            if (IsDisposed) return;
            if (result == null) { Status("Every pet is beside its owner already (or its owner is in another dimension): nothing to bring home.", C.FG); return; }
            string notes = string.Join("\r\n", result.Notes.Select(n => "• " + n));
            if (!result.Verified)
            {
                Status("Brought home, but the check of the copy found a problem. Read the report before using it.", C.RED);
                Dialogs.ShowText(this, "The copy did not pass its check", string.Join("\r\n", result.Problems.Select(p => "✗ " + p)) + "\r\n\r\n" + notes);
            }
            else
            {
                Status($"✓  Brought {moves.Count:N0} pet{(moves.Count == 1 ? "" : "s")} home in a copy ({ConvertDialog.Duration(result.Elapsed)}).", C.GREEN);
                if (Dialogs.Ask(this, $"Saved the copy with the pets beside their owners to:\n{result.OpenPath}\n\n{notes}\n\nOpen it now?") == DialogResult.Yes && _doc.Main is { } main)
                    await main.OpenPath(result.OpenPath);
            }
        }
        catch (OperationCanceledException) { if (!IsDisposed) Status("Stopped. The unfinished copy was removed.", C.FG_DIM); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SaveFormatException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!IsDisposed) Status("The pets could not be brought home: " + ex.Message, C.ORANGE);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            if (!IsDisposed) SetBusy(false);
        }
    }

    void ShowResult(SearchResult r, string dimensionId)
    {
        _result = r;
        _resultDimension = dimensionId;
        _hits = r.Hits.OrderBy(h => Order(h.Kind)).ThenBy(h => h.What).ThenBy(h => h.X).ThenBy(h => h.Z).ThenBy(h => h.Y).ToList();
        _sortColumn = -1;
        _list.VirtualListSize = _hits.Count;
        _list.SelectedIndices.Clear();
        _list.Invalidate();
        _summary.Text = Summary(r);
        var status = new StringBuilder($"Searched {r.ChunksRead:N0} chunk{(r.ChunksRead == 1 ? "" : "s")}.");
        // The search reads the world as it is saved: changes not saved yet in its tab are not in it.
        if (_doc.IsDirty) status.Append(" (As saved: changes not saved yet in the world's tab are not searched.)");
        if (r.Cut) status.Append($" The list stops at {_hits.Count:N0}; the counts above have everything.");
        if (_hits.Count > 0) status.Append(" Double-click one to see it on the map.");
        if (r.Warnings.Count > 0) status.Append($" {r.Warnings.Count} part(s) could not be read: {r.Warnings[0]}");
        Status(status.ToString(), r.Warnings.Count > 0 ? C.ORANGE : C.FG_DIM);
        UpdateButtons();
        if (_hits.Count > 0) _doc.ShowFound(dimensionId, _hits, null);
    }

    static int Order(SearchKind k) => k switch { SearchKind.Item => 0, SearchKind.Entity => 1, SearchKind.Text => 2, _ => 3 };

    /// <summary>What was found, in a few words: names with how many, and in how many places.</summary>
    static string Summary(SearchResult r)
    {
        if (r.BlockCounts.Count + r.ItemCounts.Count + r.EntityCounts.Count + r.TextCount == 0) return "Nothing was found.";
        var parts = new List<string>();
        string Names(Dictionary<string, long> counts, SearchKind kind)
        {
            var places = r.Hits.Where(h => h.Kind == kind).GroupBy(h => h.What).ToDictionary(g => g.Key, g => g.Count());
            var shown = counts.OrderByDescending(kv => kv.Value).Take(8)
                .Select(kv => $"{kv.Value:N0} {WorldSearch.Nice(kv.Key)}" + (kind != SearchKind.Block && places.TryGetValue(kv.Key, out int n) ? $" ({n:N0} place{(n == 1 ? "" : "s")})" : ""));
            string text = string.Join(", ", shown);
            return counts.Count > 8 ? text + $" and {counts.Count - 8} more" : text;
        }
        if (r.ItemCounts.Count > 0) parts.Add("Items: " + Names(r.ItemCounts, SearchKind.Item));
        if (r.EntityCounts.Count > 0) parts.Add("Mobs and entities: " + Names(r.EntityCounts, SearchKind.Entity));
        if (r.TextCount > 0) parts.Add($"Text: in {r.TextCount:N0} place{(r.TextCount == 1 ? "" : "s")}");
        if (r.BlockCounts.Count > 0) parts.Add("Blocks: " + Names(r.BlockCounts, SearchKind.Block));
        return string.Join("\n", parts);
    }

    ListViewItem Item(SearchHit h)
    {
        string what = h.Kind == SearchKind.Text ? $"\"{h.What}\"" : WorldSearch.Nice(h.What);
        var item = new ListViewItem(what) { ForeColor = h.Kind switch { SearchKind.Item => C.FG, SearchKind.Entity => C.GREEN, SearchKind.Text => C.ORANGE, _ => C.FG_DIM } };
        item.SubItems.Add(h.Count.ToString("N0"));
        item.SubItems.Add(h.X.ToString());
        item.SubItems.Add(h.Y.ToString());
        item.SubItems.Add(h.Z.ToString());
        item.SubItems.Add(h.Kind == SearchKind.Block ? h.Where : h.Where.Length == 0 ? KindName(h.Kind) : h.Where);
        return item;
    }

    static string KindName(SearchKind k) => k switch { SearchKind.Item => "item", SearchKind.Entity => "entity", SearchKind.Text => "text", _ => "block" };

    void Sort(int column)
    {
        if (_hits.Count == 0) return;
        _sortDown = column == _sortColumn && !_sortDown;
        _sortColumn = column;
        Func<SearchHit, IComparable> key = column switch
        {
            1 => h => h.Count, 2 => h => h.X, 3 => h => h.Y, 4 => h => h.Z, 5 => h => h.Where,
            _ => h => $"{Order(h.Kind)}{h.What}",
        };
        _hits = (_sortDown ? _hits.OrderByDescending(key) : _hits.OrderBy(key)).ToList();
        _list.SelectedIndices.Clear();
        _list.Invalidate();
    }

    SearchHit? Selected => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _hits.Count ? _hits[_list.SelectedIndices[0]] : null;

    void ShowOnMap()
    {
        if (_hits.Count == 0 || _resultDimension == null) return;
        var hit = Selected ?? _hits[0];
        if (hit.Dimension != null) { Status($"{WorldSearch.Nice(hit.What)} is with a player in {WorldSearch.Nice(hit.Dimension)}: search that dimension to see it on the map.", C.FG_DIM); return; }
        _doc.ShowFound(_resultDimension, _hits, hit);
        Status($"{(hit.Kind == SearchKind.Text ? $"\"{hit.What}\"" : WorldSearch.Nice(hit.What))} at {hit.X}, {hit.Y}, {hit.Z}: circled on the map.", C.FG_DIM);
    }

    // The chunk the thing is in, opened in the NBT editor (where a chest's contents or a command can be edited); what a
    // player carries is on the Players page instead.
    void OpenNbt()
    {
        if (Selected is not { } h || _resultDimension == null) return;
        if (h.OnPlayer)
        {
            _doc.ShowSection(WorldPages.WorldSection.Players);
            Status($"{WorldSearch.Nice(h.What)} is in {h.Where}: the Players page shows it.", C.FG_DIM);
            return;
        }
        _doc.OpenChunkAt(_resultDimension, h.X, h.Z);
        Status($"Chunk {h.X >> 4}, {h.Z >> 4} opened in the NBT editor (the block is at {h.X}, {h.Y}, {h.Z}). A backup is made before it is saved.", C.FG_DIM);
    }

    void CopyCoordinates()
    {
        if (Selected is not { } h) return;
        string text = $"{h.X} {h.Y} {h.Z}";
        try
        {
            Clipboard.SetText(text);
            Status($"Copied {text} (for /tp {text} in the game).", C.FG_DIM);
        }
        catch (System.Runtime.InteropServices.ExternalException) { Status("The clipboard is busy; try again.", C.ORANGE); }
    }

    void SaveList()
    {
        if (_hits.Count == 0) return;
        using var dlg = new SaveFileDialog { Filter = "List (CSV)|*.csv", FileName = SafeName.For($"{_doc.World.Name} - {_query.Text.Trim()}", "found") + ".csv", Title = "Save the list of what was found" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        static string Cell(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder("Kind,What,How many,x,y,z,Where\r\n");
        foreach (var h in _hits)
            sb.Append(string.Join(",", KindName(h.Kind), Cell(h.What), h.Count, h.X, h.Y, h.Z, Cell(h.Where + (h.Dimension != null ? $" ({h.Dimension})" : "")))).Append("\r\n");
        // Then every name with its total (complete even when the list above was cut short): block counts of an area, say.
        if (_result != null)
        {
            sb.Append("\r\nKind,What,Total\r\n");
            foreach (var (kind, counts) in new[] { ("item", _result.ItemCounts), ("entity", _result.EntityCounts), ("block", _result.BlockCounts) })
                foreach (var (name, total) in counts.OrderByDescending(kv => kv.Value))
                    sb.Append(kind).Append(',').Append(Cell(name)).Append(',').Append(total).Append("\r\n");
        }
        try
        {
            SafeFile.WriteAllBytes(dlg.FileName, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
            Status($"Saved {_hits.Count:N0} line(s) to {dlg.FileName}", C.GREEN);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Could not save the list: " + ex.Message, C.ORANGE); }
    }

    void Status(string text, Color color)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }
}
