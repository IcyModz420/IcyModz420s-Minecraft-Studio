// MinecraftStudio/Documents/WorldDocument.Selection.cs: selection for WorldDocument.
// Part of WorldDocument (main part in WorldDocument.cs).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

partial class WorldDocument
{
    // ------------------------------------------------------------------ selection tools

    void SetTool(MapTool tool)
    {
        _map.Tool = tool;
        foreach (var (t, b) in _toolButtons) b.Checked = t == tool;
        _brushSize.Enabled = tool == MapTool.Brush;
        _map.Focus();
    }

    void UpdateHistoryButtons()
    {
        _undoButton.Enabled = _map.CanUndo;
        _redoButton.Enabled = _map.CanRedo;
        _undoButton.ToolTipText = _map.UndoName is string u ? $"Undo {u.ToLowerInvariant()} (Ctrl+Z)" : "Nothing to undo";
        _redoButton.ToolTipText = _map.RedoName is string r ? $"Redo {r.ToLowerInvariant()} (Ctrl+Y)" : "Nothing to redo";
    }

    void SetSubtract(bool on)
    {
        _subtract.Checked = on;
        _map.Subtract = on;
    }

    void ToolKey(KeyEventArgs e)
    {
        if (e.Control || e.Alt) return;
        switch (e.KeyCode)
        {
            case Keys.H: SetTool(MapTool.Pan); break;
            case Keys.R: SetTool(MapTool.Rectangle); break;
            case Keys.C: SetTool(MapTool.Circle); break;
            case Keys.L: SetTool(MapTool.Lasso); break;
            case Keys.B: SetTool(MapTool.Brush); break;
            case Keys.OemOpenBrackets: _brushSize.Value = Math.Max(_brushSize.Minimum, _brushSize.Value - 1); break;
            case Keys.OemCloseBrackets: _brushSize.Value = Math.Min(_brushSize.Maximum, _brushSize.Value + 1); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Tool icons drawn as shapes (the icon font has no lasso or brush).</summary>
    static Bitmap ToolIcon(MapTool tool, int px, Color color)
    {
        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float w = Math.Max(1.2f, px / 12f), m = px * 0.14f, size = px - 2 * m;
        using var pen = new Pen(color, w) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round, StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        using var dashed = new Pen(color, w) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var brush = new SolidBrush(color);
        float c = px / 2f;
        switch (tool)
        {
            case MapTool.Pan:
                // Four arrows: move the map.
                g.DrawLine(pen, c, m, c, px - m);
                g.DrawLine(pen, m, c, px - m, c);
                float a = px * 0.16f;
                foreach (var (x, y, dx, dy) in new[] { (c, m, 0f, 1f), (c, px - m, 0f, -1f), (m, c, 1f, 0f), (px - m, c, -1f, 0f) })
                    g.FillPolygon(brush, [new PointF(x, y), new PointF(x + dx * a - dy * a, y + dy * a - dx * a), new PointF(x + dx * a + dy * a, y + dy * a + dx * a)]);
                break;
            case MapTool.Rectangle:
                g.DrawRectangle(dashed, m, m + px * 0.08f, size, size - px * 0.16f);
                break;
            case MapTool.Circle:
                g.DrawEllipse(dashed, m, m, size, size);
                break;
            case MapTool.Lasso:
                g.DrawBezier(pen, px * 0.30f, px * 0.62f, px * 0.02f, px * 0.10f, px * 0.98f, px * 0.05f, px * 0.80f, px * 0.48f);
                g.DrawBezier(pen, px * 0.80f, px * 0.48f, px * 0.66f, px * 0.75f, px * 0.36f, px * 0.70f, px * 0.30f, px * 0.62f);
                g.DrawLine(pen, px * 0.30f, px * 0.62f, px * 0.22f, px - m);
                break;
            case MapTool.Brush:
                g.FillEllipse(brush, m + size * 0.18f, m + size * 0.18f, size * 0.64f, size * 0.64f);
                g.DrawEllipse(pen, m, m, size, size);
                break;
        }
        return bmp;
    }

    /// <summary>Selects chunks inside or outside a square or circle around the spawn, a player or a point.</summary>
    void SelectArea()
    {
        var centres = new List<(string Name, double X, double Z)>();
        if (Spawn() is var (sx, sz)) centres.Add(("World spawn", sx, sz));
        centres.AddRange(PlayersInDimension().Select(p => ("Player " + p.Name, p.X, p.Z)));
        centres.Add(("Coordinates", 0, 0));
        using var f = new DarkDialog("Select an area", 480);
        var form = new FormBuilder(f);
        var centre = form.Combo("Centre", centres.Select(c => c.Name), 0);
        var x = form.Number("Block X", (decimal)Math.Round(centres[0].X), -30_000_000, 30_000_000);
        var z = form.Number("Block Z", (decimal)Math.Round(centres[0].Z), -30_000_000, 30_000_000);
        centre.SelectedIndexChanged += (_, _) =>
        {
            var c = centres[Math.Max(0, centre.SelectedIndex)];
            if (c.Name == "Coordinates") return;
            x.Value = (decimal)Math.Round(c.X);
            z.Value = (decimal)Math.Round(c.Z);
        };
        var shape = form.Combo("Shape", ["Square", "Circle"], 1);
        var radius = form.Number("Radius in chunks", 16, 0, 1_000_000);
        var where = form.Combo("Chunks", ["Inside the area", "Outside the area"], 0);
        var mode = form.Combo("Selection", ["Replace the selection", "Add to the selection", "Remove from the selection"], 0);
        f.AddButtons(("Select", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        if (f.ShowDialog(FindForm()) != DialogResult.OK) return;
        int cx = (int)Math.Floor((double)x.Value / 16), cz = (int)Math.Floor((double)z.Value / 16);
        long r = (long)radius.Value;
        bool circle = shape.SelectedIndex == 1, inside = where.SelectedIndex == 0;
        bool In(ChunkPos p) => circle
            ? (long)(p.X - cx) * (p.X - cx) + (long)(p.Z - cz) * (p.Z - cz) <= r * r
            : Math.Max(Math.Abs((long)p.X - cx), Math.Abs((long)p.Z - cz)) <= r;
        bool Test(ChunkPos p) => In(p) == inside;
        switch (mode.SelectedIndex)
        {
            case 1: _map.SelectWhere(Test, add: true, "Area selection (add)"); break;
            case 2: _map.SetSelection(_map.Selection.Where(p => !Test(p)).ToList(), "Area selection (remove)"); break;
            default: _map.SelectWhere(Test, add: false, "Area selection"); break;
        }
        Main.SetStatus($"{_map.Selection.Count:N0} chunks selected.");
    }

    /// <summary>The selection as an MCA Selector CSV: "rx;rz" for whole regions, "rx;rz;cx;cz" for single chunks.</summary>
    void ExportSelection()
    {
        if (_map.Selection.Count == 0) { Dialogs.Info(FindForm()!, "Select some chunks first."); return; }
        using var dlg = new SaveFileDialog { Filter = "Chunk selection (MCA Selector)|*.csv", FileName = $"{_world.Name} - {CurrentDim?.DisplayName} selection.csv" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var lines = new List<string>();
        foreach (var region in _map.Selection.GroupBy(p => (p.X >> 5, p.Z >> 5)))
        {
            if (region.Count() == 1024) lines.Add($"{region.Key.Item1};{region.Key.Item2}");
            else lines.AddRange(region.Select(p => $"{region.Key.Item1};{region.Key.Item2};{p.X};{p.Z}"));
        }
        File.WriteAllLines(dlg.FileName, lines);
        Main.SetStatus($"Saved {_map.Selection.Count:N0} chunks to {dlg.FileName}");
    }

    void ImportSelection()
    {
        using var dlg = new OpenFileDialog { Filter = "Chunk selection (MCA Selector)|*.csv|All files|*.*" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var chunks = new HashSet<ChunkPos>();
        bool inverted = false;
        int bad = 0;
        foreach (var raw in File.ReadLines(dlg.FileName))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Equals("inverted", StringComparison.OrdinalIgnoreCase)) { inverted = true; continue; }
            var p = line.Split(';');
            if (p.Length == 4 && int.TryParse(p[2], out int x) && int.TryParse(p[3], out int z)) chunks.Add(new ChunkPos(x, z));
            else if (p.Length == 2 && int.TryParse(p[0], out int rx) && int.TryParse(p[1], out int rz))
                for (int i = 0; i < 1024; i++) chunks.Add(new ChunkPos(rx * 32 + (i & 31), rz * 32 + (i >> 5)));
            else bad++;
        }
        _map.SetSelection(inverted ? _map.Chunks.Where(c => !chunks.Contains(c)).ToList() : chunks, "Load selection");
        Main.SetStatus($"Loaded a selection of {_map.Selection.Count:N0} existing chunks" + (bad > 0 ? $" ({bad} lines were not understood)." : "."));
    }

    bool SupportsInhabited() => _world switch
    {
        JavaWorld j => j.Storage == JavaStorage.Anvil,
        ConsoleWorld => true,
        _ => false,
    };

    void Prune()
    {
        var d = CurrentDim;
        if (d == null) return;
        if (_world is ThreeDsWorld) { Dialogs.Info(FindForm()!, "Deleting chunks is not supported for 3DS worlds yet; the 3DS chunk index is only partly understood."); return; }
        using var dlg = new PrunerDialog(d, Spawn(), PlayersInDimension().Select(p => (p.X, p.Z)), new HashSet<ChunkPos>(_map.Selection), SupportsInhabited());
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        _map.SetSelection(dlg.Result, "Prune selection");
        if (dlg.PreviewOnly) return;
        DeleteSelection(confirm: false);
    }

    void DeleteSelection(bool confirm = true)
    {
        var d = CurrentDim;
        if (d == null || _map.Selection.Count == 0) return;
        if (_world is ThreeDsWorld) { Dialogs.Info(FindForm()!, "Deleting chunks is not supported for 3DS worlds yet."); return; }
        int n = _map.Selection.Count;
        string what = d is Core.Legacy.FiniteDimension ? "cleared to air" : "deleted (the game regenerates them from the seed when visited)";
        string undo = d.CanRestoreChunks ? " Until then, Undo (Ctrl+Z) brings them back." : "";
        if (confirm && Dialogs.Ask(FindForm()!, $"{n:N0} chunks in {d.DisplayName} will be {what}.\n\nThe change is written when you save.{undo} Continue?") != DialogResult.Yes) return;
        var chunks = _map.Selection.ToList();
        var dim = d;
        void Delete()
        {
            lock (dim) dim.DeleteChunks(chunks);
            _pendingDeletions++;
            AfterChunksChanged();
            _map.SetSelection([], step: null);
        }
        void Restore()
        {
            IReadOnlyList<ChunkPos> back;
            lock (dim) back = dim.RestoreChunks(chunks);
            _pendingDeletions = Math.Max(0, _pendingDeletions - 1);
            AfterChunksChanged();
            _map.SetSelection(back, step: null);
            Main.SetStatus($"{back.Count:N0} chunks brought back" + (back.Count < chunks.Count ? $" ({chunks.Count - back.Count:N0} could not be: they were saved already)." : "."));
        }
        Delete();
        if (dim.CanRestoreChunks) _map.RecordAction($"Delete {n:N0} chunks", Restore, Delete);
        Main.SetStatus($"{n:N0} chunks marked for deletion — Save to write them." + (dim.CanRestoreChunks ? " Ctrl+Z brings them back until you save." : ""));
    }

    /// <summary>The chunk list, map, side panel and tab state after chunks were deleted or brought back.</summary>
    void AfterChunksChanged()
    {
        _dirty = _pendingDeletions > 0;
        _map.RefreshChunkList();
        _map.ClearCache();
        UpdateSelection();
        OnStateChanged();
    }

    /// <summary>Saves everything changed in the tab: the pages' changes and the map's deleted chunks.</summary>
    public override async Task SaveAsync()
    {
        bool pages = false;
        if (_cheatsPage?.IsDirty == true) { await _cheatsPage.SaveAsync(); pages = true; }
        if (_playersPage?.IsDirty == true) { await _playersPage.SaveAsync(); pages = true; }
        if (!_world.IsModified && !_dirty)
        {
            if (!pages) Main.SetStatus("Nothing to save.");
            return;
        }
        await SaveMapAsync();
    }

    async Task SaveMapAsync()
    {
        if (_world is BedrockWorld && LevelDbLocked()) { Dialogs.Error(FindForm()!, "The world is open in Minecraft. Close the game (or leave the world) and try again."); return; }
        await Main.RunAsync("Saving " + _world.Name, progress =>
        {
            var zip = Backup.WorldBeforeSave(_world, progress);
            if (zip != null) progress.Report("Backup: " + zip);
            progress.Report("Writing changes");
            foreach (var d in _world.Dimensions)
                lock (d)
                {
                    if (_world is BedrockWorld bw) { if (d.IsModified) { bw.Save(Core.LevelDb.CommitMode.Compact); break; } }
                    else if (d.IsModified) d.SaveChanges();
                }
            if (_world is ConsoleWorld cw && cw.IsModified) cw.Save();
        });
        _dirty = false;
        _pendingDeletions = 0;
        // Deletions are on disk now: they can no longer be taken back.
        _map.ClearHistory();
        _map.RefreshChunkList();
        _map.ClearCache();
        ForgetStructures();
        FillFacts();
        UpdateSelection();
        OnStateChanged();
        Main.SetStatus("Saved " + _world.Name);
    }

    bool LevelDbLocked() => _world is BedrockWorld bw && Core.LevelDb.LevelDatabase.IsLocked(bw.Db.Directory_);

    /// <summary>The map's right-click menu for one block: its coordinates, the world spawn there, its chunk's NBT.</summary>
    void ShowBlockMenu(MapBlockClick at)
    {
        string where = at.Y is int y ? $"{at.X} {y} {at.Z}" : $"{at.X} {at.Z}";
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        menu.Items.Add(new ToolStripLabel("Block " + where.Replace(" ", ", ")) { ForeColor = C.FG_DIM });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Copy the coordinates", (_, _) =>
        {
            try { Clipboard.SetText(where); Main?.SetStatus($"Copied {where} (for /tp {where} in the game)."); }
            catch (System.Runtime.InteropServices.ExternalException) { Main?.SetStatus("The clipboard is busy; try again."); }
        }));
        // The world spawn is in the overworld.
        bool overworld = CurrentDim is { } d && Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld";
        menu.Items.Add(Item("Set the world spawn here…", overworld && !_world.OpenedForRescue ? (_, _) => SetSpawnHere(at) : null));
        bool movable = overworld && Core.World.HardcoreRestore.CanMove(_world) && _world.Players.Count > 0;
        menu.Items.Add(Item("Move a player here…", movable ? (_, _) => MovePlayerHere(at) : null));
        menu.Items.Add(Item("Open this chunk in the NBT editor", (_, _) => OpenChunk(new ChunkPos(at.X >> 4, at.Z >> 4))));
        menu.Items.Add(Item("Find in this world…", (_, _) => ShowFind()));
        // The chunks selected on the map (or the one clicked) as the box: whole columns.
        var clicked = CurrentDim is { } cd ? (cd, (at.X >> 4) * 16, (at.Z >> 4) * 16, (at.X >> 4) * 16 + 15, (at.Z >> 4) * 16 + 15) : ((WorldDimension, int, int, int, int)?)null;
        menu.Items.Add(Item("Replace blocks, the biome or the land's shape here…", !_world.OpenedForRescue ? async (_, _) => { if (Main != null) await Main.ReplaceBlocks(_world, MapSelectionBox() ?? clicked); } : null));
        menu.Items.Add(Item("See this in 3D…", clicked != null ? async (_, _) => await Show3D(MapSelectionBox() ?? clicked!.Value) : null));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_map, at.Screen);
    }

    /// <summary>
    /// The chunks selected (or the one clicked) in 3D: from a little under the lowest ground to the highest top block, read
    /// from a world of its own off disk (a rescued world as it is), in the 3D view.
    /// </summary>
    async Task Show3D((WorldDimension Dim, int MinX, int MinZ, int MaxX, int MaxZ) box)
    {
        const int MaxSide = 256;
        if (box.MaxX - box.MinX + 1 > MaxSide || box.MaxZ - box.MinZ + 1 > MaxSide)
        {
            Dialogs.Info((IWin32Window?)FindForm() ?? this, $"The 3D view shows at most {MaxSide} × {MaxSide} blocks (16 × 16 chunks). Select fewer chunks on the map.");
            return;
        }
        string location = _world.Location, dimId = box.Dim.Id, name = _world.Name;
        bool rescue = _world.OpenedForRescue;
        Core.Structures.Structure? piece = null;
        bool ok = await Main.RunAsync("Reading the area", progress =>
        {
            using var own = rescue ? null : WorldLoader.Open(location);
            var w = own ?? _world;
            var dim = w.Dimensions.First(x => x.Id == dimId);
            int low = int.MaxValue, high = int.MinValue;
            var ground = new int[256];
            void Read()
            {
                var reader = new Core.Conversion.CanonicalReader(w, dim);
                for (int cz = box.MinZ >> 4; cz <= box.MaxZ >> 4; cz++)
                    for (int cx = box.MinX >> 4; cx <= box.MaxX >> 4; cx++)
                    {
                        if (reader.Get(new ChunkPos(cx, cz)) is not { } c) continue;
                        var tops = c.Heights();
                        MapRenderer.GroundHeights(c.Sections.Values, ground, bedrockNames: false);
                        for (int i = 0; i < 256; i++)
                        {
                            int x = cx * 16 + (i & 15), z = cz * 16 + (i >> 4);
                            if (x < box.MinX || x > box.MaxX || z < box.MinZ || z > box.MaxZ) continue;
                            if (ground[i] != MapRenderer.NoHeight) low = Math.Min(low, ground[i]);
                            if (tops[i] != int.MinValue) high = Math.Max(high, tops[i]);
                        }
                    }
                if (high == int.MinValue) throw new InvalidOperationException("There is nothing there to show: no chunks, or only air.");
                if (low == int.MaxValue) low = high;
                var (bottom, _) = Core.Structures.StructurePlacement.Height(w, Core.Conversion.CanonicalReader.DimensionId(dim));
                piece = Core.Structures.StructureClip.Extract(w, dim, box.MinX, Math.Max(bottom, low - 6), box.MinZ, box.MaxX, high, box.MaxZ, includeEntities: false);
            }
            if (own == null) lock (dim) Read();
            else Read();
        });
        if (ok && piece != null)
            Documents.Structure3DWindow.Open(FindForm(), piece, $"3D view — {name}, {box.Dim.DisplayName}: x {box.MinX} to {box.MaxX}, z {box.MinZ} to {box.MaxZ}");
    }

    // A stuck or lost player put on safe ground near the block clicked: the Move to safety window, near these coordinates.
    void MovePlayerHere(MapBlockClick at)
    {
        if (IsDirty)
        {
            Dialogs.Info(FindForm()!, "Save the world's changes first (Ctrl+S): moving a player saves them straight away.");
            return;
        }
        var players = _world.Players.ToList();
        int pick = players.Count == 1 ? 0 : Dialogs.Choose(FindForm()!, "Move a player here", $"Which player goes to safe ground near {at.X}, {at.Z}?", players.Select(p => p.Name).ToArray());
        if (pick < 0) return;
        using var dlg = new MovePlayerDialog(_world, players[pick], (at.X, at.Z));
        dlg.ShowDialog(FindForm());
        if (dlg.Moved) PlayersChanged();
    }

    /// <summary>The Move to safety window for one player (Check for problems' fix): beside their bed or near the world spawn.</summary>
    public void MovePlayerToSafety(string? name)
    {
        if (IsDirty)
        {
            Dialogs.Info(FindForm()!, "Save the world's changes first (Ctrl+S): moving a player saves them straight away.");
            return;
        }
        if (_world.Players.FirstOrDefault(p => p.Name == name) is not { } player)
        {
            Dialogs.Info(FindForm()!, $"{name ?? "That player"} is not among the world's players any more: check the world again.");
            return;
        }
        using var dlg = new MovePlayerDialog(_world, player);
        dlg.ShowDialog(FindForm());
        if (dlg.Moved) PlayersChanged();
    }

    // Filled in on the Cheats page (standing on the block clicked); written with its Save changes, after a backup.
    void SetSpawnHere(MapBlockClick at)
    {
        // A Classic or Indev level has no Cheats page: its spawn is saved into the level file straight away.
        if (_world is Core.Legacy.FiniteWorld finite) { SetLevelSpawn(finite, at); return; }
        ShowSection(WorldSection.Cheats);
        if (_cheatsPage?.SetSpawn(at.X, at.Y is int y ? y + 1 : null, at.Z) != true)
            Dialogs.Info(FindForm()!, "This world keeps no spawn point that can be changed here.");
    }

    void SetLevelSpawn(Core.Legacy.FiniteWorld level, MapBlockClick at)
    {
        var owner = (IWin32Window?)FindForm() ?? this;
        if (!level.HasSpawn) { Dialogs.Info(owner, "This level keeps no spawn point: the oldest Classic levels are only blocks."); return; }
        if (IsDirty) { Dialogs.Info(owner, "Save the level's other changes first (Ctrl+S): setting the spawn saves the whole level file."); return; }
        int y = at.Y is int top ? top + 1 : 64;
        if (Dialogs.Ask(owner, $"Put the level's spawn at {at.X}, {y}, {at.Z}?\n\nThe level file is saved straight away; a backup is made first.") != DialogResult.Yes) return;
        try
        {
            Backup.World(level);
            level.SetSpawn(at.X, y, at.Z);
            Main?.SetStatus($"The level's spawn is now {at.X}, {y}, {at.Z} (saved; a backup was made first).");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Dialogs.Error(owner, "The spawn could not be set: " + ex.Message);
        }
    }

    /// <summary>Opens the chunk holding a block of a dimension in the NBT editor (a Find result's "Open in the NBT editor").</summary>
    public void OpenChunkAt(string dimensionId, int blockX, int blockZ)
    {
        int index = _world.Dimensions.ToList().FindIndex(d => d.Id == dimensionId);
        if (index >= 0 && _dimBox.SelectedIndex != index) _dimBox.SelectedIndex = index;
        OpenChunk(new ChunkPos(blockX >> 4, blockZ >> 4));
    }

    void OpenChunk(ChunkPos pos)
    {
        var d = CurrentDim;
        if (d == null) return;
        NbtCompound? nbt;
        try { lock (d) nbt = d.LoadChunkNbt(pos); }
        catch (Exception ex) { Dialogs.Error(FindForm()!, ex.Message); return; }
        if (nbt == null) { Dialogs.Info(FindForm()!, "This chunk has no NBT data to show."); return; }
        var dim = d;
        var source = new DelegateNbtSource($"Chunk {pos.X}, {pos.Z} ({d.DisplayName})",
            () => { lock (dim) return [new NamedTag("", dim.LoadChunkNbt(pos)!)]; },
            d.CanSaveChunkNbt ? roots =>
            {
                lock (dim)
                {
                    Backup.WorldBeforeSave(_world);
                    dim.SaveChunkNbt(pos, (NbtCompound)roots[0].Tag);
                }
                _map.ClearCache();
            } : null,
            $"Chunk {pos.X}, {pos.Z} in {d.DisplayName} of {_world.Name}" + (d.CanSaveChunkNbt ? "" : " (read-only for this format)"));
        Main.OpenDocument(new NbtDocument(source) { Key = (_world, d.Id, pos) });
    }

    async Task ExportPng()
    {
        var d = CurrentDim;
        if (d == null) return;
        using var dlg = new SaveFileDialog { Filter = "PNG image|*.png", FileName = $"{_world.Name} - {d.DisplayName}.png" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        int? slice = _map.SliceY;
        await Main.RunAsync("Exporting map", progress =>
        {
            DimensionRenderer.Result r;
            lock (d) r = DimensionRenderer.Render(d, new MapRenderOptions { StartY = slice }, progress: new Progress<double>(v => progress.Report($"Rendering {v:P0}")));
            if (r.Width == 0) throw new InvalidOperationException("The dimension has no chunks.");
            File.WriteAllBytes(dlg.FileName, PngWriter.Encode(r.Pixels, r.Width, r.Height));
        });
        Main.SetStatus("Exported " + dlg.FileName);
    }

    /// <summary>
    /// The dimension's ground heights as a 16-bit grey picture (Heightmap: water, plants and trees left out; lowest black,
    /// highest white, the range and corner in the file) — for terrain programs, or to build land from again (Create ▸ New
    /// World from a Heightmap puts it back where it was).
    /// </summary>
    async Task ExportHeights()
    {
        var d = CurrentDim;
        if (d == null) return;
        using var dlg = new SaveFileDialog { Filter = "PNG image (16-bit grey)|*.png", FileName = $"{_world.Name} - {d.DisplayName} heights.png" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        int low = 0, high = 0;
        await Main.RunAsync("Exporting heights", progress =>
        {
            DimensionRenderer.Result r;
            lock (d) r = DimensionRenderer.Render(d, new MapRenderOptions(), progress: new Progress<double>(v => progress.Report($"Reading heights {v:P0}")), ground: true);
            if (r.Width == 0) throw new InvalidOperationException("The dimension has no chunks.");
            low = int.MaxValue; high = int.MinValue;
            foreach (int y in r.Ground!)
                if (y != MapRenderer.NoHeight) { low = Math.Min(low, y); high = Math.Max(high, y); }
            if (low > high) throw new InvalidOperationException("No ground was found in this dimension.");
            if (high == low) high = low + 1;
            File.WriteAllBytes(dlg.FileName, Heightmap.Encode(r.Ground, r.Width, r.Height, low, high, r.MinChunkX * 16, r.MinChunkZ * 16));
        });
        if (high > low) Main.SetStatus($"Exported the heights (black y {low}, white y {high}) to {dlg.FileName}");
    }

    /// <summary>
    /// The dimension as a web map (WebMap): a folder to open in any browser, with the spawn, the players and the structures
    /// marked; read from a world of its own off disk (a rescued world as it is), so the map here keeps drawing.
    /// </summary>
    async Task ExportWebMap()
    {
        var d = CurrentDim;
        if (d == null) return;
        using var dlg = new FolderBrowserDialog { Description = "An empty folder for the web map (it gets index.html, map.js and the tiles)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        string folder = Path.Combine(dlg.SelectedPath, Core.IO.SafeName.For($"{_world.Name} - {d.DisplayName} web map", "web map"));
        var markers = new List<WebMapMarker>();
        if (Spawn() is { } spawn) markers.Add(new WebMapMarker("Spawn", spawn.X, spawn.Z, "Spawn", 0xFFD54F));
        foreach (var (name, x, z) in PlayersInDimension()) markers.Add(new WebMapMarker(name, (int)Math.Floor(x), (int)Math.Floor(z), "Player", 0x4FC3F7));
        try
        {
            foreach (var s in await StructuresOf(d.Id))
                markers.Add(new WebMapMarker(s.Name, s.X, s.Z, "Structures", StructureKinds.Color(s.Key), s.Unlooted > 0 ? $"{s.Unlooted:N0} loot container{(s.Unlooted == 1 ? "" : "s")} never opened" : null));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException) { }
        string location = _world.Location, dimId = d.Id, title = _world.Name;
        bool rescue = _world.OpenedForRescue;
        int? slice = _map.SliceY;
        WebMapResult? result = null;
        bool ok = await Main.RunAsync("Making the web map", progress =>
        {
            using var own = rescue ? null : WorldLoader.Open(location);
            var dim = (own ?? _world).Dimensions.First(x => x.Id == dimId);
            if (own == null) lock (dim) result = WebMap.Export(dim, folder, title, markers, new MapRenderOptions { StartY = slice }, progress);
            else result = WebMap.Export(dim, folder, title, markers, new MapRenderOptions { StartY = slice }, progress);
        });
        if (!ok || result == null) return;
        Main.SetStatus($"Saved the web map ({result.Tiles:N0} tiles, {result.Levels} zoom levels) to {folder}");
        if (Dialogs.Ask((IWin32Window?)FindForm() ?? this, $"Saved the web map of the {d.DisplayName} to:\n{folder}\n\nOpen it in your browser now? (Copy the folder anywhere — onto a website, a USB stick — and open index.html.)") == DialogResult.Yes)
            Explorer.Open(result.IndexPath);
    }

    public void ReloadFacts() { FillFacts(); UpdateSelection(); }

    public void SelectDimension(WorldDimension d)
    {
        int i = _world.Dimensions.ToList().IndexOf(d);
        if (i >= 0) _dimBox.SelectedIndex = i;
    }
}
