// Documents/MapArtDialog.cs: a world's maps as pictures, and any picture made into map art for it (Java and Bedrock, PC).
// Owns: MapArtDialog — every map of the world as a thumbnail (save one, or all, as PNG); a picture (PNG, JPEG, BMP …) cut
// into a wall of maps — 1 to 16 across and down, fit / fill / stretch, dithered or not, previewed in the colours the
// world's version shows — added to the world under new ids (MapArt) and put into a chosen player's empty slots. The world
// is backed up first; refused while the game has it open or the world's tab has unsaved changes (they share its files).
// "Colours: buildable in survival" makes it from blocks a player can place (with steps for three shades, or flat), and
// "Save as schematics to build" writes one structure file per map (MapArt.Building) and the material list beside them.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.World;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class MapArtDialog : DarkDialog
{
    readonly SaveWorld _world;
    readonly Func<bool> _hasUnsavedChanges;
    readonly FlowLayoutPanel _thumbs;
    readonly Label _mapsInfo, _status;
    readonly Button _savePicture, _saveAll, _browse, _add, _build;
    readonly DarkTextField _picturePath;
    readonly NumberField _across, _down;
    readonly DarkComboBox _fit, _player, _colours;
    readonly DarkCheckBox _dither;
    readonly PictureBox _preview;
    readonly ToolTip _tip = new();
    List<CanonicalMap> _maps = [];
    CanonicalMap? _chosen;
    RgbaImage? _picture;
    List<CanonicalMap>? _made;
    int _previewTicket;
    bool _busy;

    /// <summary>Whether maps were added (the world's tab then reads its players again).</summary>
    public bool Changed { get; private set; }

    public MapArtDialog(SaveWorld world, Func<bool> hasUnsavedChanges) : base("Map art", 760)
    {
        _world = world;
        _hasUnsavedChanges = hasUnsavedChanges;
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new SectionHeader("MAPS IN THIS WORLD") { Size = new Size(this.Px(720), this.Px(18)), Margin = new Padding(0, 0, 0, this.Px(6)) });
        _mapsInfo = new Label { AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(4)) };
        column.Controls.Add(_mapsInfo);
        _thumbs = new FlowLayoutPanel { Size = new Size(this.Px(720), this.Px(150)), AutoScroll = true, WrapContents = true, BackColor = C.BG, Margin = new Padding(0, 0, 0, this.Px(4)) };
        Theme.DarkControl(_thumbs);
        column.Controls.Add(_thumbs);
        var mapButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _savePicture = Theme.Button("Save the chosen map as a picture…");
        _savePicture.Click += (_, _) => SaveChosen();
        _saveAll = Theme.Button("Save every map as a picture…");
        _saveAll.Click += (_, _) => SaveAll();
        mapButtons.Controls.AddRange([_savePicture, _saveAll]);
        column.Controls.Add(mapButtons);

        column.Controls.Add(new SectionHeader("MAKE MAP ART FROM A PICTURE") { Size = new Size(this.Px(720), this.Px(18)), Margin = new Padding(0, this.Px(14), 0, this.Px(6)) });
        _picturePath = new DarkTextField { Width = this.Px(480), Height = this.Px(28), AccessibleName = "Picture" };
        _picturePath.Box.ReadOnly = true;
        _picturePath.Box.PlaceholderText = "A picture: PNG, JPEG, BMP, GIF, TGA or TIFF";
        _browse = Theme.Button("Choose…");
        _browse.Click += (_, _) => PickPicture();
        column.Controls.Add(Row("Picture", _picturePath, _browse));
        _across = new NumberField { Width = this.Px(64), Height = this.Px(28), Minimum = 1, Maximum = 16, Value = 1, AccessibleName = "Maps across", Margin = new Padding(0, 0, this.Px(6), 0) };
        _down = new NumberField { Width = this.Px(64), Height = this.Px(28), Minimum = 1, Maximum = 16, Value = 1, AccessibleName = "Maps down", Margin = new Padding(0, 0, this.Px(12), 0) };
        _fit = new DarkComboBox { Width = this.Px(200), AccessibleName = "How the picture fits" };
        foreach (var (text, mode) in new[] { ("Fit inside (keep it all)", PictureFitMode.Fit), ("Fill (cut the edges)", PictureFitMode.Fill), ("Stretch", PictureFitMode.Stretch) })
            _fit.Items.Add(new ComboItem(text, null, mode));
        _fit.SelectedIndex = 1;
        column.Controls.Add(Row("Maps across × down", _across, _down, _fit));
        _dither = new DarkCheckBox("Dither: smoother shading from the map colours (off: flat areas of colour)") { AutoSize = true };
        column.Controls.Add(_dither);
        _colours = new DarkComboBox { Width = this.Px(480), AccessibleName = "Colours" };
        _colours.Items.Add(new ComboItem("Every map colour (the maps are made straight into the world)", null, 0));
        _colours.Items.Add(new ComboItem("Buildable in survival, with steps (three shades, more colours)", null, 1));
        _colours.Items.Add(new ComboItem("Buildable in survival, flat (one layer, fewer colours)", null, 2));
        _colours.SelectedIndex = 0;
        column.Controls.Add(Row("Colours", _colours));
        _preview = new PictureBox { Size = new Size(this.Px(256), this.Px(256)), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = C.BG, Margin = new Padding(this.Px(116), this.Px(6), 0, this.Px(6)), AccessibleName = "Map art preview" };
        column.Controls.Add(_preview);
        _player = new DarkComboBox { Width = this.Px(320), AccessibleName = "Give the maps to" };
        _player.Items.Add(new ComboItem("Nobody — only add the maps to the world", null, null));
        foreach (var p in world.Players) _player.Items.Add(new ComboItem(p.Name, null, p));
        _player.SelectedIndex = _player.Items.Count > 1 ? 1 : 0;
        column.Controls.Add(Row("Give the maps to", _player));
        _add = Theme.Button("Add the map art to the world", true);
        _add.MinimumSize = new Size(this.Px(220), this.Px(30));
        _add.Click += async (_, _) => await AddAsync();
        _build = Theme.Button("Save as schematics to build…");
        _build.MinimumSize = new Size(this.Px(220), this.Px(30));
        _build.Click += (_, _) => SaveToBuild();
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.AddRange([_add, _build]);
        column.Controls.Add(actions);

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };
        AddButtons(("Close", DialogResult.Cancel, false));
        Controls.Add(_status);
        Controls.Add(column);
        SizeToContent();
        foreach (var n in new[] { _across, _down }) n.ValueChanged += (_, _) => _ = ShowPreview();
        _fit.SelectedIndexChanged += (_, _) => _ = ShowPreview();
        _dither.CheckedChanged += (_, _) => _ = ShowPreview();
        _colours.SelectedIndexChanged += (_, _) => _ = ShowPreview();
        if (MapArt.CannotAdd(world) is string why) { _add.Enabled = false; _status.Text = why; }
        UpdateButtons();
        _ = LoadMaps();
    }

    async Task LoadMaps()
    {
        _mapsInfo.Text = "Reading the maps…";
        // Read from a world of its own off disk (the map tab may be reading the same files); a rescued world as it is.
        string location = _world.Location;
        bool rescue = _world.OpenedForRescue;
        try
        {
            _maps = (await Task.Run(() =>
            {
                using var own = rescue ? null : WorldLoader.Open(location);
                return Maps.Read(own ?? _world);
            })).OrderBy(m => m.Id).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SaveFormatException) { _maps = []; _mapsInfo.Text = "The maps could not be read: " + ex.Message; return; }
        if (IsDisposed) return;
        foreach (Control c in _thumbs.Controls) { if (c is PictureBox pb) pb.Image?.Dispose(); }
        _thumbs.Controls.Clear();
        const int MaxShown = 300;
        foreach (var m in _maps.Take(MaxShown))
        {
            var box = new PictureBox
            {
                Size = new Size(this.Px(68), this.Px(68)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = C.BG2, Margin = new Padding(this.Px(3)), Cursor = Cursors.Hand,
                Image = PixelClipboard.ToBitmap(MapArt.Picture(m)), Tag = m, AccessibleName = $"Map {m.Id}", Padding = new Padding(this.Px(2)),
            };
            _tip.SetToolTip(box, $"map #{m.Id}{(m.Locked ? ", locked" : "")}");
            box.Click += (_, _) => Choose(box);
            _thumbs.Controls.Add(box);
        }
        _mapsInfo.Text = _maps.Count == 0 ? "This world has no maps yet." : $"{_maps.Count:N0} map{(_maps.Count == 1 ? "" : "s")}{(_maps.Count > MaxShown ? $" (the first {MaxShown} shown)" : "")}. Click one to choose it.";
        UpdateButtons();
    }

    void Choose(PictureBox box)
    {
        foreach (Control c in _thumbs.Controls) c.BackColor = C.BG2;
        box.BackColor = C.ACCENT;
        _chosen = (CanonicalMap)box.Tag!;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        _savePicture.Enabled = !_busy && _chosen != null;
        _saveAll.Enabled = !_busy && _maps.Count > 0;
        _add.Enabled = !_busy && _made != null && MapArt.CannotAdd(_world) == null;
        _build.Enabled = !_busy && _made != null && _madeBuildable;
        _browse.Enabled = !_busy;
    }

    /// <summary>A map picture four times as large, crisp (whole pixels), so it can be looked at or printed.</summary>
    static byte[] PngOf(CanonicalMap m) => Png.Encode(Resample.Scale(MapArt.Picture(m), 512, 512, smooth: false));

    void SaveChosen()
    {
        if (_chosen == null) return;
        using var dlg = new SaveFileDialog { Filter = "PNG picture|*.png", FileName = $"map_{_chosen.Id}.png", Title = "Save the map as a picture" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { SafeFile.WriteAllBytes(dlg.FileName, PngOf(_chosen)); Done($"Saved map #{_chosen.Id} to {dlg.FileName}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn(ex.Message); }
    }

    void SaveAll()
    {
        using var dlg = new FolderBrowserDialog { Description = "A folder for the map pictures (map_<number>.png)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int n = 0;
            foreach (var m in _maps)
            {
                string path = Path.Combine(dlg.SelectedPath, $"map_{m.Id}.png");
                if (File.Exists(path)) continue; // never over a file that is there
                SafeFile.WriteAllBytes(path, PngOf(m));
                n++;
            }
            Done($"Saved {n:N0} map picture{(n == 1 ? "" : "s")} to {dlg.SelectedPath}{(n < _maps.Count ? $" ({_maps.Count - n} were there already and were left)" : "")}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn(ex.Message); }
    }

    void PickPicture()
    {
        using var dlg = new OpenFileDialog { Filter = PixelClipboard.OpenFilter, Title = "Choose a picture to make into map art" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _picture = PixelClipboard.Load(dlg.FileName);
            _picturePath.Text = dlg.FileName;
            // A size that keeps the picture's shape: as many maps as its longer side needs, at most 4 to start with.
            double aspect = _picture.Width / (double)_picture.Height;
            _across.Value = aspect >= 1 ? Math.Min(4, Math.Max(1, (int)Math.Round(aspect))) : 1;
            _down.Value = aspect < 1 ? Math.Min(4, Math.Max(1, (int)Math.Round(1 / aspect))) : 1;
            _ = ShowPreview();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or OutOfMemoryException)
        {
            _picture = null;
            Warn("That picture could not be opened: " + ex.Message);
        }
    }

    async Task ShowPreview()
    {
        int ticket = ++_previewTicket;
        if (_picture == null) return;
        var pic = _picture;
        int across = (int)_across.Value, down = (int)_down.Value;
        var fit = _fit.SelectedItem is ComboItem { Tag: PictureFitMode f } ? f : PictureFitMode.Fill;
        bool dither = _dither.Checked;
        int maxBase = MapArt.MaxBase(_world), mode = _colours.SelectedItem is ComboItem { Tag: int m } ? m : 0, dv = BuildVersion;
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Making the map art…";
        try
        {
            var (made, wall) = await Task.Run(() =>
            {
                var options = new PictureOptions { Fit = fit, Smooth = true };
                var maps = mode == 0 ? MapArt.Make(pic, across, down, options, maxBase, dither) : MapArt.MakeBuildable(pic, across, down, options, dv, staircase: mode == 1, dither);
                return (maps, MapArt.Wall(maps, across));
            });
            if (IsDisposed || ticket != _previewTicket) return;
            _made = made;
            _madeBuildable = mode != 0;
            _madeAcross = across;
            var old = _preview.Image;
            int box = _preview.Width - this.Px(4);
            double scale = Math.Min(box / (double)wall.Width, box / (double)wall.Height);
            _preview.Image = PixelClipboard.ToBitmap(Resample.Scale(wall, Math.Max(1, (int)(wall.Width * scale)), Math.Max(1, (int)(wall.Height * scale)), smooth: scale < 1));
            old?.Dispose();
            _status.Text = $"{across * down} map{(across * down == 1 ? "" : "s")} ({across * 128} × {down * 128} pixels), in the colours this world's version shows.";
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { Warn(ex.Message); }
        UpdateButtons();
    }

    bool _madeBuildable;
    int _madeAcross = 1;

    /// <summary>The data version the blocks are chosen for: the Java world's own, or the newest (Bedrock).</summary>
    int BuildVersion => _world is Core.Java.JavaWorld j ? j.DataVersion ?? 0 : 9999;

    /// <summary>The map art as structures to build in survival: one file per map, and the material list of them all.</summary>
    void SaveToBuild()
    {
        if (_made is not { Count: > 0 } made || !_madeBuildable) return;
        string name = SafeName.For(Path.GetFileNameWithoutExtension(_picturePath.Text), "map art");
        using var dlg = new SaveFileDialog { Filter = Core.Structures.StructureFile.SaveFilter, FileName = name + ".litematic", Title = "Save the map art to build" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int dv = BuildVersion;
        string dir = Path.GetDirectoryName(dlg.FileName)!, stem = Path.GetFileNameWithoutExtension(dlg.FileName), ext = Path.GetExtension(dlg.FileName);
        try
        {
            var all = new List<Core.Structures.Structure>();
            var standIns = new List<string>();
            var files = new List<string>();
            for (int i = 0; i < made.Count; i++)
            {
                var s = MapArt.Building(made[i], dv);
                string file = made.Count == 1 ? dlg.FileName : Path.Combine(dir, $"{stem} {i / _madeAcross + 1}-{i % _madeAcross + 1}{ext}");
                standIns.AddRange(Core.Structures.StructureFile.SaveChecked(s, file, dv).Select(t => t.From));
                files.Add(Path.GetFileName(file));
                all.Add(s);
            }
            var lines = all.SelectMany(Core.Structures.StructureMaterials.Count).GroupBy(l => l.Item)
                .Select(g => new Core.Structures.MaterialLine(g.Key, g.Sum(l => l.Count))).OrderByDescending(l => l.Count).ToList();
            string materials = Path.Combine(dir, stem + " materials.txt");
            File.WriteAllText(materials, Core.Structures.StructureMaterials.Text(lines, $"Map art \"{stem}\": {made.Count} map{(made.Count == 1 ? "" : "s")} to build"));
            int tallest = all.Max(s => s.SizeY);
            Done($"Saved {files.Count} schematic{(files.Count == 1 ? "" : "s")} ({string.Join(", ", files.Take(3))}{(files.Count > 3 ? " …" : "")}) and the material list. Each is 128 × {tallest} × 129: "
                + "its first row (z 0) goes just north of the map's area, the picture starts one block south of it. Maps go left to right, then down." + (standIns.Count > 0 ? $" Not in this version: {string.Join(", ", standIns.Distinct().Take(4))}." : ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warn("The schematics could not be saved: " + ex.Message);
        }
    }

    async Task AddAsync()
    {
        if (_made == null) return;
        if (_hasUnsavedChanges()) { Warn("Save the world's other changes first (Ctrl+S): map art writes the world's files straight away."); return; }
        if (WorldUse.InUse(_world) is string busy) { Warn(busy); return; }
        var made = _made;
        var player = _player.SelectedItem is ComboItem { Tag: PlayerRecord p } ? p : null;
        _busy = true;
        UpdateButtons();
        try
        {
            var progress = new Progress<string>(s => _status.Text = s);
            _status.ForeColor = C.FG_DIM;
            _status.Text = "Backing up the world…";
            await Task.Run(() => Backup.WorldBeforeSave(_world, progress));
            var (ids, given) = await Task.Run(() =>
            {
                // Fresh copies: the preview's maps stay as they are if this fails.
                var maps = made.Select(m => new CanonicalMap { Scale = m.Scale, Locked = m.Locked, TrackingPosition = m.TrackingPosition, UnlimitedTracking = m.UnlimitedTracking, XCenter = m.XCenter, ZCenter = m.ZCenter, Colors = (byte[])m.Colors.Clone() }).ToList();
                var ids = MapArt.Add(_world, maps);
                int given = player != null ? MapArt.Give(_world, player, ids) : 0;
                return (ids, given);
            });
            Changed = true;
            string range = ids.Count == 1 ? $"#{ids[0]}" : $"#{ids.First()}–#{ids.Last()}";
            string give = player == null ? "" : given == ids.Count ? $" {player.Name} has them in their inventory (left to right, then down)." : $" {given} fitted in {player.Name}'s empty slots; the rest are in the world (maps {range}).";
            Done($"Added {ids.Count} map{(ids.Count == 1 ? "" : "s")} ({range}).{give} Put them in item frames, left to right and top to bottom, to show the picture.");
            await LoadMaps();
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)) Log.Error("Adding map art failed", ex);
            Warn(ex.Message);
        }
        finally { _busy = false; UpdateButtons(); }
    }

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(110), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    void Done(string text) { _status.ForeColor = C.GREEN; _status.Text = "✓  " + text; }
    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image?.Dispose();
            foreach (Control c in _thumbs.Controls) if (c is PictureBox pb) pb.Image?.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
