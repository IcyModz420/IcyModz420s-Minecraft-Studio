// Documents/StructurePlaceDialog.cs: the building mover's place window.
// Owns: StructurePlaceDialog — loads a structure or schematic (.nbt, .mcstructure, .schem, .litematic, .schematic), turns or
// mirrors it, and places it into a new copy of a world through StructurePlacement, which copies the world and rewrites
// only the chunks the structure touches — the world placed into never changes. Where it goes is clicked on a map of the
// world beside the fields (MapPicker: its footprint as turned, the structure seen from above in it) or typed; while
// "Stand it on the ground" is ticked, y follows the ground under it (StructureGround). "Check the build" compares the
// structure at that spot with what the world has there instead (BuildCheck: right, missing, wrong, set wrong, in the way;
// the wrong cells marked on the map; the report with what is still to gather). Reads a world of its own off disk, so the
// map tab can keep using the same files.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>Places a saved structure (.nbt or .mcstructure) into a new copy of a world; the world placed into is never changed.</summary>
sealed class StructurePlaceDialog : DarkDialog
{
    readonly SaveWorld _world;
    readonly DarkTextField _file, _folder;
    readonly DarkComboBox _dimension;
    readonly NumberField _x, _y, _z;
    readonly DarkCheckBox _ground, _merge;
    readonly Label _status, _info;
    readonly Button _place, _browse, _browseOut, _check;
    readonly SlimProgress _bar;
    readonly StructurePreviewBox _preview;
    readonly StructureTurnChoice _turn;
    readonly MapPicker _map;
    int _shownFor;
    Structure? _structure, _framed;
    // The structure's footprint as turned (one block before one is chosen).
    (int X, int Z) _footprint = (1, 1);
    CancellationTokenSource? _cts;
    // _syncing: the fields are being set from the map (not typed). _groundPending: y still follows the ground (the map had
    // not drawn all of the footprint yet).
    bool _running, _syncing, _groundPending;

    public string? OpenAfterClose { get; private set; }

    public StructurePlaceDialog(SaveWorld world) : base("Place a structure into this world", 600)
    {
        _world = world;
        var info = StructureSaveDialogInfo(world);

        var column = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        column.Controls.Add(new Label
        {
            Text = $"Places a structure into a new copy of \"{world.Name}\": click the map where it should go, or drag it about (or type the low corner). "
                + "Your world is never changed: it is copied as it is, only the chunks the structure lands in are rewritten (in the world's own edition and version), and the copy is checked.",
            AutoSize = true, MaximumSize = new Size(this.Px(560), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });

        _file = new DarkTextField { Width = this.Px(400), Height = this.Px(28), AccessibleName = "Structure file" };
        _file.Box.PlaceholderText = "A structure .nbt or .mcstructure file";
        _browse = Theme.Button("Browse…");
        _browse.Click += (_, _) => PickFile();
        column.Controls.Add(Row("Structure", _file, _browse));

        _info = new Label { AutoSize = true, MaximumSize = new Size(this.Px(560), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(this.Px(110), 0, 0, this.Px(4)) };
        column.Controls.Add(_info);
        // What is about to be placed, as turned: from above and from the south, in the map's colours.
        _preview = new StructurePreviewBox { Margin = new Padding(this.Px(110), 0, 0, this.Px(6)) };
        column.Controls.Add(_preview);
        _turn = new StructureTurnChoice(this);
        _turn.Changed += () => _ = ShowLoaded();
        column.Controls.Add(Row("Turn and mirror", _turn.Turn, _turn.Mirror));

        _dimension = new DarkComboBox { Width = this.Px(300), AccessibleName = "Dimension" };
        foreach (var d in world.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, null, d));
        if (_dimension.Items.Count > 0) _dimension.SelectedIndex = 0;
        column.Controls.Add(Row("Into dimension", _dimension));

        _x = Num(info.SpawnX); _y = Num(StructureSaveDialog.SaneY(info.SpawnY)); _z = Num(info.SpawnZ);
        column.Controls.Add(Row("Low corner at (x, y, z)", _x, _y, _z));

        // Typing a y of one's own unticks it.
        _ground = new DarkCheckBox("Stand it on the ground there (y from the map)") { AutoSize = true };
        _merge = new DarkCheckBox("Keep the surrounding terrain (don't carve the box as air)") { AutoSize = true };
        column.Controls.Add(_ground);
        column.Controls.Add(_merge);

        _folder = new DarkTextField { Width = this.Px(400), Height = this.Px(28), AccessibleName = "Output folder" };
        _folder.Box.PlaceholderText = "A new, empty folder for the copy";
        _browseOut = Theme.Button("Browse…");
        _browseOut.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "A new folder for the copy with the structure", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = Path.Combine(dlg.SelectedPath, SafeName.For(world.Name + " with structure", "World"));
        };
        _folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Placed", SafeName.For(world.Name + " with structure", "World"));
        column.Controls.Add(Row("Save copy to", _folder, _browseOut));

        _bar = new SlimProgress { Width = this.Px(560), Height = this.Px(6), Visible = false, Margin = new Padding(0, this.Px(8), 0, 0) };
        column.Controls.Add(_bar);

        // The map beside the fields, as tall as they are.
        _map = new MapPicker(MapBoxTool.Place, "Click where it goes, or drag it about. Right-drag pans the map, the wheel zooms.")
        {
            Size = new Size(this.Px(460), Math.Max(this.Px(380), column.GetPreferredSize(Size.Empty).Height)),
            Margin = new Padding(this.Px(16), 0, 0, 0),
        };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        row.Controls.Add(column);
        row.Controls.Add(_map);

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(1036), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };

        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _place = Theme.Button("Place and save copy", true);
        _place.Click += async (_, _) => await PlaceAsync();
        buttons.Controls.Add(_place);
        // The build checker: the same spot, turn and mirror, compared instead of placed.
        _check = Theme.Button("Check the build");
        _check.Click += async (_, _) => await CheckBuildAsync();
        buttons.Controls.Add(_check);

        Controls.Add(_status);
        Controls.Add(row);
        WideEnoughFor(row);

        // The footprint on the map and the low corner follow each other; y follows the ground while "Stand it on the ground" is ticked.
        _x.ValueChanged += (_, _) => CornerTyped();
        _z.ValueChanged += (_, _) => CornerTyped();
        _y.ValueChanged += (_, _) => { if (!_syncing) _ground.Checked = false; };
        _ground.CheckedChanged += (_, _) => { _groundPending = _ground.Checked; StandOnGround(); };
        _map.BoxChanged += (_, _) => FootprintDragged();
        _map.BoxDropped += (_, _) => { _groundPending = _ground.Checked; StandOnGround(); };
        _map.Drawn += (_, _) => StandOnGround();
        _dimension.SelectedIndexChanged += (_, _) =>
        {
            if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension d }) return;
            _map.ShowDimension(d.Id);
            _groundPending = _ground.Checked;
        };
        UpdateFootprint();
        _groundPending = _ground.Checked;
        _ = _map.OpenAsync(world, _dimension.SelectedItem is ComboItem { Tag: WorldDimension shown } ? shown.Id : null);
        // Last, once everything it shows the structure in is there.
        if (StructureSaveDialog.LastSaved is string last && File.Exists(last)) LoadFile(last);
        SizeToContent();
    }

    protected override void OnLoad(EventArgs e)
    {
        FitWidth(_map, this.Px(240));
        base.OnLoad(e);
    }

    void PickFile()
    {
        using var dlg = new OpenFileDialog { Filter = StructureFile.OpenFilter, Title = "Open a structure or schematic (.nbt, .mcstructure, .schem, .litematic, .schematic)" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadFile(dlg.FileName);
    }

    /// <summary>Loads a structure file to place (also used by the hidden-window checks).</summary>
    internal void LoadFile(string path)
    {
        _file.Text = path;
        try
        {
            _structure = StructureFile.Load(path);
        }
        catch (Exception ex)
        {
            // Structure files come from anywhere: whatever is wrong with one is reported, never a crash.
            if (ex is not (IOException or SaveFormatException or UnauthorizedAccessException or ArgumentException)) Log.Error("Reading a structure file failed", ex);
            _structure = null;
            _info.ForeColor = C.ORANGE;
            _info.Text = "Could not read that structure file: " + ex.Message;
        }
        _ = ShowLoaded();
    }

    /// <summary>The loaded structure as turned and mirrored now (worked out in the background: a big one takes a moment).</summary>
    async Task<Structure?> Turned()
    {
        var s = _structure;
        int turns = _turn.Turns;
        var mirror = _turn.MirrorChoice;
        return s == null ? null : await Task.Run(() => StructureTransform.Apply(s, turns, mirror));
    }

    async Task ShowLoaded()
    {
        int ticket = ++_shownFor;
        if (_structure == null)
        {
            await _preview.ShowAsync(null);
            if (!IsDisposed && ticket == _shownFor) ShowFootprint(null, null);
            return;
        }
        Structure turned;
        try { turned = (await Turned())!; }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { _info.ForeColor = C.ORANGE; _info.Text = ex.Message; return; }
        if (IsDisposed || ticket != _shownFor) return;
        _info.ForeColor = C.FG_FAINT;
        _info.Text = turned.Describe() + (_turn.IsIdentity ? "" : $", {_turn.Describe()}") + (_structure.Origin != null ? $" — from {_structure.Origin}" : "");
        var top = await _preview.ShowAsync(turned);
        if (IsDisposed || ticket != _shownFor) return;
        ShowFootprint(turned, top);
    }

    // The footprint on the map takes the structure's size as turned, the view from above inside it; the low corner stays.
    // A structure just loaded is framed on the map; turning it keeps the view (back into it only when it went out).
    void ShowFootprint(Structure? turned, RgbaImage? top)
    {
        _footprint = turned == null ? (1, 1) : (turned.SizeX, turned.SizeZ);
        _map.SetPicture(top);
        UpdateFootprint();
        if (!ReferenceEquals(_framed, _structure)) { _framed = _structure; _map.GoToBox(); }
        else _map.KeepBoxInView();
        _groundPending = _ground.Checked;
        StandOnGround();
    }

    void UpdateFootprint()
    {
        int x = (int)_x.Value, z = (int)_z.Value;
        _map.Box = (x, z, x + _footprint.X - 1, z + _footprint.Z - 1);
    }

    // The low corner typed: the footprint follows (back into view when it went out of it).
    void CornerTyped()
    {
        if (_syncing) return;
        UpdateFootprint();
        _map.KeepBoxInView();
        _groundPending = _ground.Checked;
        StandOnGround();
    }

    // The footprint put down or dragged on the map: the low corner follows (y when it is let go).
    void FootprintDragged()
    {
        if (_map.Box is not { } b) return;
        _syncing = true;
        try { _x.Value = b.MinX; _z.Value = b.MinZ; }
        finally { _syncing = false; }
    }

    /// <summary>While "Stand it on the ground" is ticked: y is the ground's height under the footprint, as the map has drawn it.</summary>
    void StandOnGround()
    {
        if (!_groundPending || !_ground.Checked || _map.Box is not { } b) return;
        int? ground = _map.GroundY(b, out long notDrawn);
        // Followed until the map has drawn all of the footprint (the part it has drawn counts meanwhile).
        if (notDrawn == 0 || _map.Failed) _groundPending = false;
        if (ground is not int g) return;
        _syncing = true;
        try { _y.Value = g; }
        finally { _syncing = false; }
    }

    /// <summary>
    /// The structure at the chosen spot compared with the world there (a build in progress, or a copy of one): how much is
    /// built right, the wrong cells marked on the map, and the report with what is still to gather.
    /// </summary>
    internal async Task CheckBuildAsync()
    {
        if (_structure == null) { Warn("Choose a structure file first."); return; }
        Structure structure;
        try { structure = (await Turned())!; }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { Warn(ex.Message); return; }
        if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension dim }) { Warn("Choose a dimension."); return; }
        string dimId = CanonicalReader.DimensionId(dim), location = _world.Location;
        int x = (int)_x.Value, y = (int)_y.Value, z = (int)_z.Value;
        bool airCounts = !_merge.Checked;
        bool rescue = _world.OpenedForRescue;
        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Checking the build…";
        var progress = new Progress<string>(s => { if (_running) _status.Text = s; });
        try
        {
            var r = await Task.Run(() =>
            {
                using var own = rescue ? null : WorldLoader.Open(location);
                return BuildCheck.Run(own ?? _world, dimId, structure, x, y, z, airCounts, progress, token);
            }, token);
            LastCheck = r;
            _map.SetFound(r.Problems.Select(p => (p.X, p.Z)).Distinct().Take(5000).ToList());
            _status.ForeColor = r.Wrong == 0 ? C.GREEN : C.FG;
            _status.Text = r.Checked == 0 ? "Nothing to check there: the spot is not generated yet."
                : r.Wrong == 0 ? $"✓  Built right: all {r.Checked:N0} blocks are as the structure has them."
                : $"{r.Done:P0} built right — {string.Join(", ", new[] { (r.Missing, "missing"), (r.WrongBlock, "the wrong block"), (r.WrongState, "set wrong"), (r.Extra, "in the way") }.Where(t => t.Item1 > 0).Select(t => $"{t.Item1:N0} {t.Item2}"))}; marked on the map.";
            if (r.Wrong > 0) Dialogs.ShowText(this, "The build checked against " + Path.GetFileName(_file.Text), BuildCheck.Report(r));
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SaveFormatException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            Warn("The build could not be checked: " + ex.Message);
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    /// <summary>The last build check's result (the hidden-window checks read it).</summary>
    internal BuildCheckResult? LastCheck { get; private set; }

    async Task PlaceAsync()
    {
        if (_structure == null) { Warn("Choose a structure file first."); return; }
        Structure structure;
        try { structure = (await Turned())!; }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { Warn(ex.Message); return; }
        if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension dim }) { Warn("Choose a dimension."); return; }
        string output = _folder.Text.Trim();
        if (output.Length == 0) { Warn("Choose a folder for the copy."); return; }
        try { output = Path.GetFullPath(output); } catch { Warn("That folder path is not valid."); return; }
        if (File.Exists(output)) { Warn("A file with that name already exists; choose another folder."); return; }
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) { Warn("That folder is not empty; choose a new folder name."); return; }
        string source = Path.GetFullPath(_world.Location).TrimEnd('\\');
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase)) { Warn("The copy cannot go inside the world being placed into."); return; }

        string dimId = CanonicalReader.DimensionId(dim), location = _world.Location;
        int x = (int)_x.Value, y = (int)_y.Value, z = (int)_z.Value;
        bool pasteAir = !_merge.Checked;
        if (_world.OpenedForRescue) { Warn("This world was opened for rescue. Rescue it into a new world first, then place the structure into that."); return; }

        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        _bar.Visible = true; _bar.Value = 0; _bar.BarColor = C.ACCENT2;
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Opening the world…";
        var progress = new Progress<ConversionProgress>(p =>
        {
            if (!_running) return;
            _bar.Marquee = p.Total <= 1;
            _bar.Value = p.Total > 0 ? p.Done / (double)p.Total : 0;
            _status.Text = p.Total > 1 && p.Done < p.Total ? $"{p.Stage} · {p.Done:N0} of {p.Total:N0} chunks" : p.Stage;
        });
        try
        {
            var result = await Task.Run(() =>
            {
                // A world of its own, read off disk: the map tab may be reading the same files.
                using var world = WorldLoader.Open(location);
                return StructurePlacement.PlaceIntoCopy(world, structure, dimId, x, y, z, pasteAir, output, progress, token);
            }, token);
            _bar.Marquee = false; _bar.Value = 1;
            OpenAfterClose = result.OpenPath;
            string notes = result.Notes.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", result.Notes.Select(n => "• " + n)) : "";
            if (!result.Verified)
            {
                _bar.BarColor = C.RED;
                _status.ForeColor = C.RED;
                _status.Text = "Placed, but the check of the copy found a problem. Read the report before using it.";
                Dialogs.ShowText(this, "The copy did not pass its check", string.Join("\r\n", result.Problems.Select(p => "✗ " + p)) + notes);
            }
            else
            {
                _bar.BarColor = C.ACCENT2;
                _status.ForeColor = result.BlocksLeftOut > 0 ? C.ORANGE : C.GREEN;
                _status.Text = $"✓  Placed {result.BlocksPlaced:N0} blocks into a copy ({result.ChunksChanged:N0} chunks changed, everything else unchanged, {ConvertDialog.Duration(result.Elapsed)})."
                    + (result.BlocksLeftOut > 0 ? $" {result.BlocksLeftOut:N0} blocks fell outside the world and were left out." : "");
                if (Dialogs.Ask(this, $"Saved the copy with the structure to:\n{OpenAfterClose}{notes}\n\nOpen it now?") == DialogResult.Yes) Close();
            }
        }
        catch (OperationCanceledException)
        {
            TryRemove(output);
            _status.ForeColor = C.YELLOW;
            _status.Text = "Stopped. The unfinished copy was removed.";
            _bar.Visible = false;
            OpenAfterClose = null;
        }
        catch (Exception ex)
        {
            TryRemove(output);
            _bar.BarColor = C.RED;
            _status.ForeColor = C.RED;
            _status.Text = "Placing failed: " + ex.Message;
            Log.Error("Structure placement failed", ex);
            OpenAfterClose = null;
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        foreach (var c in new Control[] { _file, _browse, _dimension, _x, _y, _z, _ground, _merge, _folder, _browseOut, _place, _check, _map }) c.Enabled = !busy;
        _turn.Enabled = !busy;
    }

    static void TryRemove(string output)
    {
        try { if (Directory.Exists(output)) Directory.Delete(output, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static WorldInfo StructureSaveDialogInfo(SaveWorld w) { try { return WorldInfoReader.Read(w); } catch { return new WorldInfo(); } }
    NumberField Num(int v) => new() { Width = this.Px(72), Height = this.Px(28), Minimum = -30_000_000, Maximum = 30_000_000, Value = v, Margin = new Padding(0, 0, this.Px(6), 0) };

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        // At least as wide as the widest label, so the fields line up (an auto-sized label is as wide as its text).
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(150), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running) { e.Cancel = true; _cts?.Cancel(); _status.Text = "Stopping…"; return; }
        base.OnFormClosing(e);
    }
}
