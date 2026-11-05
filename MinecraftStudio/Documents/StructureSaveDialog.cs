// Documents/StructureSaveDialog.cs: the building mover's crop window.
// Owns: StructureSaveDialog — crops a box of blocks out of the open world into a structure file (Java .nbt, WorldEdit .schem,
// Litematica, Bedrock .mcstructure or MCEdit .schematic; Java files made for a chosen version). The box is dragged out on a
// map of the world beside the fields (MapPicker) or typed, each following the other; while "Heights from the map" is ticked
// the heights are the lowest to the highest top block the map shows in it. Reads a world of its own off disk, so the map
// tab can keep using the same files.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>Crops a block box out of the open world and saves it as a structure file.</summary>
sealed class StructureSaveDialog : DarkDialog
{
    readonly SaveWorld _world;
    readonly DarkComboBox _dimension, _version;
    readonly NumberField _minX, _minY, _minZ, _maxX, _maxY, _maxZ;
    readonly DarkCheckBox _heights, _blockEntities, _entities;
    readonly MapPicker _map;
    readonly Label _size, _status;
    readonly Button _save;
    // _syncing: the fields are being set from the map (not typed). _heightsPending: the heights still follow the box
    // (the map had not drawn all of it yet).
    bool _syncing, _heightsPending;

    /// <summary>The structure file saved most recently (this session), offered first when placing.</summary>
    public static string? LastSaved { get; private set; }

    public StructureSaveDialog(SaveWorld world, (WorldDimension Dim, int MinX, int MinZ, int MaxX, int MaxZ)? selection = null) : base("Save a structure (crop a building)", 560)
    {
        _world = world;
        var info = SafeInfo(world);

        var column = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        column.Controls.Add(new Label
        {
            Text = "Drag a box on the map around what to crop out (pull its edges or corners to change it, drag it to move it), or type its corners. "
                + "The heights follow the top blocks the map shows in it: lower \"From y\" to take a cellar too. It is saved as a structure file you can place into another world.",
            AutoSize = true, MaximumSize = new Size(this.Px(520), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });

        _dimension = new DarkComboBox { Width = this.Px(300), AccessibleName = "Dimension" };
        foreach (var d in world.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, null, d));
        if (_dimension.Items.Count > 0) _dimension.SelectedIndex = 0;
        column.Controls.Add(Row("Dimension", _dimension));

        int spawnY = SaneY(info.SpawnY);
        _minX = Num(info.SpawnX - 8); _minY = Num(Math.Max(0, spawnY - 8)); _minZ = Num(info.SpawnZ - 8);
        _maxX = Num(info.SpawnX + 8); _maxY = Num(spawnY + 24); _maxZ = Num(info.SpawnZ + 8);
        column.Controls.Add(Row("From (x, y, z)", _minX, _minY, _minZ));
        column.Controls.Add(Row("To (x, y, z)", _maxX, _maxY, _maxZ));
        _size = new Label { AutoSize = true, MaximumSize = new Size(this.Px(410), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(this.Px(110), 0, 0, this.Px(4)) };
        column.Controls.Add(_size);

        // Typing a height of one's own unticks it.
        _heights = new DarkCheckBox("Heights from the map (the lowest to the highest top block in the box)") { AutoSize = true };
        _blockEntities = new DarkCheckBox("Include chest contents, signs and other block data") { AutoSize = true };
        _entities = new DarkCheckBox("Include entities (mobs, item frames, armour stands)") { AutoSize = true };
        column.Controls.Add(_heights);
        column.Controls.Add(_blockEntities);
        column.Controls.Add(_entities);

        // Java files (.nbt, WorldEdit .schem, Litematica) are made for one Java version: its block names, items and text.
        _version = new DarkComboBox { Width = this.Px(300), AccessibleName = "Java version the file is made for" };
        foreach (var t in TargetCatalog.All.Where(t => t.Kind == TargetKind.Java && t.DataVersion >= StructureJava.Flattening).OrderByDescending(t => t.DataVersion))
            _version.Items.Add(new ComboItem(t.Name + (t.DataVersion == StructureJava.Newest ? " (newest)" : ""), null, t.DataVersion));
        if (_version.Items.Count > 0) _version.SelectedIndex = 0;
        column.Controls.Add(Row("Made for", _version));
        column.Controls.Add(new Label
        {
            Text = "Save as a Java structure (.nbt, for structure blocks), a WorldEdit .schem, a Litematica .litematic, a Bedrock .mcstructure, or an MCEdit .schematic for 1.12 and older: choose the type in the save box. The version applies to the Java files.",
            AutoSize = true, MaximumSize = new Size(this.Px(520), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(0, this.Px(4), 0, 0),
        });

        // Pre-fill the box (and dimension) from the map selection, if the world is open with chunks selected.
        if (selection is { } sel)
        {
            for (int i = 0; i < _dimension.Items.Count; i++)
                if (_dimension.Items[i] is ComboItem { Tag: WorldDimension d } && d.Id == sel.Dim.Id) { _dimension.SelectedIndex = i; break; }
            _minX.Value = sel.MinX; _minZ.Value = sel.MinZ; _maxX.Value = sel.MaxX; _maxZ.Value = sel.MaxZ;
        }

        // The map beside the fields, as tall as they are.
        _map = new MapPicker(MapBoxTool.Draw, "Drag out a box; pull its edges or corners, or drag it to move it. Right-drag pans the map, the wheel zooms.")
        {
            Size = new Size(this.Px(460), Math.Max(this.Px(380), column.GetPreferredSize(Size.Empty).Height)),
            Margin = new Padding(this.Px(16), 0, 0, 0),
        };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        row.Controls.Add(column);
        row.Controls.Add(_map);

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(996), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };

        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _save = Theme.Button("Save structure…", true);
        _save.Click += async (_, _) => await SaveAsync();
        buttons.Controls.Add(_save);

        Controls.Add(_status);
        Controls.Add(row);
        WideEnoughFor(row);

        // The map and the fields follow each other; the heights follow the box while "Heights from the map" is ticked.
        foreach (var f in new[] { _minX, _minZ, _maxX, _maxZ }) f.ValueChanged += (_, _) => BoxTyped();
        foreach (var f in new[] { _minY, _maxY }) f.ValueChanged += (_, _) => { if (!_syncing) _heights.Checked = false; ShowSize(); };
        _heights.CheckedChanged += (_, _) => { _heightsPending = _heights.Checked; FitHeights(); };
        _map.BoxChanged += (_, _) => BoxDragged();
        _map.BoxDropped += (_, _) => { _heightsPending = _heights.Checked; FitHeights(); };
        _map.Drawn += (_, _) => FitHeights();
        _dimension.SelectedIndexChanged += (_, _) =>
        {
            if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension d }) return;
            _map.ShowDimension(d.Id);
            _heightsPending = _heights.Checked;
            ShowSize();
        };
        _map.Box = TypedBox();
        _heightsPending = _heights.Checked;
        ShowSize();
        _ = _map.OpenAsync(world, _dimension.SelectedItem is ComboItem { Tag: WorldDimension shown } ? shown.Id : null);
        SizeToContent();
    }

    protected override void OnLoad(EventArgs e)
    {
        FitWidth(_map, this.Px(240));
        base.OnLoad(e);
    }

    (int MinX, int MinZ, int MaxX, int MaxZ) TypedBox() => ((int)_minX.Value, (int)_minZ.Value, (int)_maxX.Value, (int)_maxZ.Value);

    // A corner typed: the box on the map follows (back into view when it went out of it).
    void BoxTyped()
    {
        if (_syncing) return;
        _map.Box = TypedBox();
        _map.KeepBoxInView();
        _heightsPending = _heights.Checked;
        FitHeights();
        ShowSize();
    }

    // The box dragged on the map: the corners follow (the heights when it is let go).
    void BoxDragged()
    {
        if (_map.Box is not { } b) return;
        _syncing = true;
        try { _minX.Value = b.MinX; _minZ.Value = b.MinZ; _maxX.Value = b.MaxX; _maxZ.Value = b.MaxZ; }
        finally { _syncing = false; }
        ShowSize();
    }

    /// <summary>While "Heights from the map" is ticked: From y and To y become the lowest and highest top block the map shows in the box.</summary>
    void FitHeights()
    {
        if (!_heightsPending || !_heights.Checked || _map.Box is not { } box) return;
        var range = _map.HeightRange(box, out long notDrawn);
        // Followed until the map has drawn all of the box (the part it has drawn counts meanwhile).
        if (notDrawn == 0 || _map.Failed) _heightsPending = false;
        if (range is { } r)
        {
            _syncing = true;
            try { _minY.Value = r.Low; _maxY.Value = r.High; }
            finally { _syncing = false; }
        }
        ShowSize();
    }

    /// <summary>The box's size (and whether its heights still wait for the map).</summary>
    void ShowSize()
    {
        int x0 = (int)_minX.Value, y0 = (int)_minY.Value, z0 = (int)_minZ.Value, x1 = (int)_maxX.Value, y1 = (int)_maxY.Value, z1 = (int)_maxZ.Value;
        if (x1 < x0 || y1 < y0 || z1 < z0) { _size.ForeColor = C.ORANGE; _size.Text = "The \"to\" corner must be at or past the \"from\" corner."; return; }
        long volume = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        bool tooLarge = volume > Structure.MaxVolume;
        _size.ForeColor = tooLarge ? C.ORANGE : C.FG_FAINT;
        _size.Text = $"{x1 - x0 + 1} × {y1 - y0 + 1} × {z1 - z0 + 1} blocks ({volume:N0})"
            + (tooLarge ? $": too large, at most {Structure.MaxVolume:N0}" : "")
            + (_heights.Checked && _heightsPending && !_map.Failed ? " · the heights follow once the map has drawn the box" : "");
    }

    async Task SaveAsync()
    {
        if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension dim }) { Warn("Choose a dimension."); return; }
        int minX = (int)_minX.Value, minY = (int)_minY.Value, minZ = (int)_minZ.Value;
        int maxX = (int)_maxX.Value, maxY = (int)_maxY.Value, maxZ = (int)_maxZ.Value;
        if (maxX < minX || maxY < minY || maxZ < minZ) { Warn("The \"to\" corner must be at or past the \"from\" corner."); return; }
        long volume = (long)(maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);
        if (volume > Structure.MaxVolume) { Warn($"That box is too large ({volume:N0} blocks; the limit is {Structure.MaxVolume:N0}). Crop a smaller area."); return; }

        using var dlg = new SaveFileDialog { Filter = StructureFile.SaveFilter, FileName = SafeName.For(_world.Name, "structure") + ".nbt", Title = "Save the structure" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string path = dlg.FileName;
        string dimId = dim.Id;
        bool be = _blockEntities.Checked, ent = _entities.Checked;
        int dataVersion = _version.SelectedItem is ComboItem { Tag: int v } ? v : StructureJava.Newest;
        string location = _world.Location;
        bool rescue = _world.OpenedForRescue;

        _save.Enabled = false;
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Reading the world…";
        try
        {
            var s = await Task.Run(() =>
            {
                using var copy = rescue ? null : WorldLoader.Open(location);
                var w = copy ?? _world;
                var d = w.Dimensions.FirstOrDefault(x => x.Id == dimId) ?? w.Dimensions[0];
                // A world opened for rescue is shared with the maps drawing it: they read a chunk at a time under its dimension's lock.
                Structure structure;
                if (copy == null) lock (d) structure = StructureClip.Extract(w, d, minX, minY, minZ, maxX, maxY, maxZ, be, ent);
                else structure = StructureClip.Extract(w, d, minX, minY, minZ, maxX, maxY, maxZ, be, ent);
                return (Structure: structure, StandIns: StructureFile.SaveChecked(structure, path, dataVersion));
            });
            _status.ForeColor = s.StandIns.Count == 0 ? C.GREEN : C.ORANGE;
            _status.Text = $"✓  Saved {s.Structure.Describe()} to {path}. " + StructureFileDialog.StandInText(s.StandIns);
            LastSaved = path;
        }
        catch (Exception ex)
        {
            // Any failure is shown (and logged) rather than taking the program down; nothing was written to the world.
            if (ex is not (IOException or SaveFormatException or UnauthorizedAccessException or ArgumentException)) Log.Error("Saving a structure failed", ex);
            _status.ForeColor = C.ORANGE;
            _status.Text = ex.Message;
        }
        finally { _save.Enabled = true; }
    }

    static WorldInfo SafeInfo(SaveWorld w) { try { return WorldInfoReader.Read(w); } catch { return new WorldInfo(); } }

    /// <summary>A usable height from a spawn Y: Bedrock and 3DS store 32767 for "the surface", which is no block height.</summary>
    internal static int SaneY(int y) => y is > -64 and < 320 ? y : 64;
    NumberField Num(int v) => new() { Width = this.Px(72), Height = this.Px(28), Minimum = -30_000_000, Maximum = 30_000_000, Value = v, Margin = new Padding(0, 0, this.Px(6), 0) };

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        // At least as wide as the widest label, so the fields line up (an auto-sized label is as wide as its text).
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(110), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }
}
