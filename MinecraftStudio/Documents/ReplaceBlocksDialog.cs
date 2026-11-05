// Documents/ReplaceBlocksDialog.cs: replace or fill blocks in a box of a world, change its biome or shape its land — saved
// as a new copy.
// Owns: ReplaceBlocksDialog — the box (filled in from the map's chunk selection); for blocks, up to five rules "pattern →
// block" (block names suggested as you type) and "Count" to see what would change without writing anything (BlockReplace);
// for the biome, any biome (BiomePaint); for the land, flatten / smooth / raise / lower with soft edges, Count showing how
// far it would move (LandShape). Every way the world is copied, only the box's chunks rewritten in its own format, and
// checked (IntoCopyAsync); the world itself never changes. Reads a world of its own off disk, like the structure dialogs.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class ReplaceBlocksDialog : DarkDialog
{
    readonly SaveWorld _world;
    readonly DarkComboBox _dimension;
    readonly NumberField _minX, _minY, _minZ, _maxX, _maxY, _maxZ;
    readonly BlockRulesEditor _rules;
    readonly DarkRadioButton _blocksMode, _biomeMode, _shapeMode;
    readonly DarkTextField _biome;
    readonly DarkComboBox _shapeKind;
    readonly NumberField _shapeAmount, _softEdge;
    readonly Label _shapeUnit;
    readonly List<Control> _blockControls = [], _biomeControls = [], _shapeControls = [];
    readonly DarkTextField _folder;
    readonly Label _status;
    readonly Button _count, _replace, _browseOut;
    readonly SlimProgress _bar;
    CancellationTokenSource? _cts;
    bool _running;

    public string? OpenAfterClose { get; private set; }

    public ReplaceBlocksDialog(SaveWorld world, (WorldDimension Dim, int MinX, int MinZ, int MaxX, int MaxZ)? selection = null) : base("Replace blocks, change the biome or shape the land", 640)
    {
        _world = world;
        var info = SafeInfo(world);
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new Label
        {
            Text = $"Swap blocks in a box of \"{world.Name}\" — every ore for stone, oak for spruce, or the whole box for one block — change its biome, or shape its land, in a new copy of the world. Your world is never changed: it is copied, only the box's chunks are rewritten (in the world's own edition and version), and every block is checked.",
            AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });

        _dimension = new DarkComboBox { Width = this.Px(300), AccessibleName = "Dimension" };
        foreach (var d in world.Dimensions) _dimension.Items.Add(new ComboItem(d.DisplayName, null, d));
        if (_dimension.Items.Count > 0) _dimension.SelectedIndex = 0;
        column.Controls.Add(Row("Dimension", _dimension));

        int spawnY = StructureSaveDialog.SaneY(info.SpawnY);
        _minX = Num(info.SpawnX - 8); _minY = Num(spawnY - 16); _minZ = Num(info.SpawnZ - 8);
        _maxX = Num(info.SpawnX + 8); _maxY = Num(spawnY + 16); _maxZ = Num(info.SpawnZ + 8);
        column.Controls.Add(Row("From (x, y, z)", _minX, _minY, _minZ));
        column.Controls.Add(Row("To (x, y, z)", _maxX, _maxY, _maxZ));
        if (selection is { } sel)
        {
            for (int i = 0; i < _dimension.Items.Count; i++)
                if (_dimension.Items[i] is ComboItem { Tag: WorldDimension d } && d.Id == sel.Dim.Id) { _dimension.SelectedIndex = i; break; }
            _minX.Value = sel.MinX; _minZ.Value = sel.MinZ; _maxX.Value = sel.MaxX; _maxZ.Value = sel.MaxZ;
            // A chunk selection is whole columns: the world's full height.
            try
            {
                var (low, high) = StructurePlacement.Height(world, CanonicalReader.DimensionId(sel.Dim));
                _minY.Value = low; _maxY.Value = high;
            }
            catch (ArgumentException) { }
        }

        _blocksMode = new DarkRadioButton("Replace or fill blocks (mobs stay as they are)") { Checked = true };
        _biomeMode = new DarkRadioButton("Change the biome (grass and leaf colour, weather, which mobs spawn)");
        _shapeMode = new DarkRadioButton("Shape the land: flatten it for a build, smooth it, raise or lower it");
        var modes = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, this.Px(8), 0, 0) };
        modes.Controls.AddRange([_blocksMode, _biomeMode, _shapeMode]);
        column.Controls.Add(modes);
        _biome = new DarkTextField { Width = this.Px(300), Height = this.Px(28), AccessibleName = "Biome" };
        _biome.Box.PlaceholderText = "e.g. cherry_grove, desert, mushroom_fields";
        var biomeNames = new AutoCompleteStringCollection();
        biomeNames.AddRange(BiomePaint.Names.Select(n => n.Replace("minecraft:", "")).ToArray());
        _biome.Box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _biome.Box.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _biome.Box.AutoCompleteCustomSource = biomeNames;
        var biomeRow = Row("Biome", _biome);
        var biomeHint = new Label
        {
            Text = "Biomes are kept in cells of 4 × 4 × 4 blocks (before 1.15, one per column), so the edges of the change follow those cells. A biome the world's version doesn't have becomes the closest one it has.",
            AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(0, this.Px(4), 0, this.Px(6)),
        };
        column.Controls.Add(biomeRow);
        column.Controls.Add(biomeHint);
        _biomeControls.AddRange([biomeRow, biomeHint]);

        _shapeKind = new DarkComboBox { Width = this.Px(220), AccessibleName = "Shape" };
        _shapeKind.Items.AddRange(["Flatten it to y", "Smooth it over a radius of", "Raise it by", "Lower it by"]);
        _shapeKind.SelectedIndex = 0;
        _shapeAmount = new NumberField { Width = this.Px(72), Height = this.Px(28), Minimum = -63, Maximum = 320, Value = spawnY, Margin = new Padding(this.Px(6), 0, 0, 0), AccessibleName = "How much" };
        _shapeUnit = new Label { AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(6), this.Px(7), 0, 0) };
        _softEdge = new NumberField { Width = this.Px(72), Height = this.Px(28), Minimum = 0, Maximum = 64, Value = 4, AccessibleName = "Soft edges" };
        var edgeNote = new Label { Text = "blocks blend into the land around (0: a sharp edge)", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(6), this.Px(7), 0, 0) };
        var shapeRow = Row("Land", _shapeKind, _shapeAmount, _shapeUnit);
        var edgeRow = Row("Soft edges", _softEdge, edgeNote);
        var shapeHint = new Label
        {
            Text = "Whole columns: the box's y is not used. Each column's ground (water, plants and trees left out) moves up or down with what stands on it — trees, chests, mobs; raised ground gets more of the block beneath it, and water keeps its level. Builds in the box move with the ground, so shape open land. Count shows how far it would move.",
            AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(0, this.Px(4), 0, this.Px(6)),
        };
        column.Controls.Add(shapeRow);
        column.Controls.Add(edgeRow);
        column.Controls.Add(shapeHint);
        _shapeControls.AddRange([shapeRow, edgeRow, shapeHint]);
        _shapeKind.SelectedIndexChanged += (_, _) => ShapeKindChanged(spawnY);

        var rulesTitle = new Label { Text = "REPLACE  →  WITH", AutoSize = true, ForeColor = C.FG_DIM, Font = Theme.Ui(8.5f, FontStyle.Bold), UseMnemonic = false, Margin = new Padding(0, this.Px(10), 0, this.Px(4)) };
        column.Controls.Add(rulesTitle);
        _blockControls.Add(rulesTitle);
        _rules = new BlockRulesEditor(this, 5);
        column.Controls.Add(_rules);
        _blockControls.Add(_rules);
        var rulesHint = new Label
        {
            Text = "A block name (stone), * for any letters (*_ore, *_log), * alone for every block — air too, to fill the box — and [key=value] for only some states (oak_log[axis=y]). The first rule that matches a block is used. The new block keeps the old one's facing and the other settings it also has; chest contents and signs go with blocks that are replaced.",
            AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(0, this.Px(4), 0, this.Px(6)),
        };
        column.Controls.Add(rulesHint);
        _blockControls.Add(rulesHint);

        _folder = new DarkTextField { Width = this.Px(400), Height = this.Px(28), AccessibleName = "Output folder" };
        _folder.Box.PlaceholderText = "A new, empty folder for the copy";
        _browseOut = Theme.Button("Browse…");
        _browseOut.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "A new folder for the copy with the blocks replaced", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = Path.Combine(dlg.SelectedPath, SafeName.For(world.Name + " replaced", "World"));
        };
        _folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Replaced", SafeName.For(world.Name + " replaced", "World"));
        column.Controls.Add(Row("Save copy to", _folder, _browseOut));

        _bar = new SlimProgress { Width = this.Px(600), Height = this.Px(6), Visible = false, Margin = new Padding(0, this.Px(8), 0, 0) };
        column.Controls.Add(_bar);
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };

        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _replace = Theme.Button("Replace and save copy", true);
        _replace.Click += async (_, _) => await ReplaceAsync();
        _count = Theme.Button("Count");
        _count.Click += async (_, _) => await CountAsync();
        foreach (var b in new[] { _replace, _count }) { b.MinimumSize = new Size(this.Px(110), this.Px(30)); buttons.Controls.Add(b); }
        Controls.Add(_status);
        Controls.Add(column);
        foreach (var mode in new[] { _blocksMode, _biomeMode, _shapeMode }) mode.CheckedChanged += (_, _) => { if (mode.Checked) ShowMode(); };
        ShowMode();
        // Sized for the tallest way (the block rules); the others leave room below.
        SizeToContent();
    }

    void ShowMode()
    {
        bool blocks = _blocksMode.Checked, shape = _shapeMode.Checked;
        foreach (var c in _blockControls) c.Visible = blocks;
        foreach (var c in _biomeControls) c.Visible = _biomeMode.Checked;
        foreach (var c in _shapeControls) c.Visible = shape;
        _count.Enabled = (blocks || shape) && !_running;
        _replace.Text = blocks ? "Replace and save copy" : shape ? "Shape and save copy" : "Change and save copy";
    }

    // What the number means for each shape: a y to flatten to, a radius, or how many blocks up or down.
    void ShapeKindChanged(int spawnY)
    {
        var kind = (LandShapeKind)Math.Max(0, _shapeKind.SelectedIndex);
        (_shapeAmount.Minimum, _shapeAmount.Maximum, _shapeAmount.Value, _shapeUnit.Text) = kind switch
        {
            LandShapeKind.Flatten => (-63m, 320m, (decimal)spawnY, ""),
            LandShapeKind.Smooth => (1m, 16m, 3m, "blocks"),
            _ => (1m, 256m, 5m, "blocks"),
        };
    }

    List<ReplaceRule> Rules() => _rules.Rules();

    /// <summary>The box, rules and dimension, checked; null (with the reason shown) when something is missing.</summary>
    (string DimId, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ, List<ReplaceRule> Rules)? Read()
    {
        if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension dim }) { Warn("Choose a dimension."); return null; }
        int minX = (int)_minX.Value, minY = (int)_minY.Value, minZ = (int)_minZ.Value, maxX = (int)_maxX.Value, maxY = (int)_maxY.Value, maxZ = (int)_maxZ.Value;
        if (maxX < minX || maxY < minY || maxZ < minZ) { Warn("The \"to\" corner must be at or past the \"from\" corner."); return null; }
        var rules = Rules();
        if (rules.Any(r => r.Pattern.Length == 0 || r.With.Length == 0)) { Warn("Each rule needs both sides: what to replace, and the block to put there."); return null; }
        if (rules.Count == 0) { Warn("Give at least one rule: what to replace, and the block to put there."); return null; }
        try { BlockReplace.Compile(rules); }
        catch (ArgumentException ex) { Warn(ex.Message); return null; }
        long volume = (long)(maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);
        if (volume > Structure.MaxVolume) { Warn($"That box is too large ({volume:N0} blocks; the limit is {Structure.MaxVolume:N0}). Choose a smaller area."); return null; }
        return (CanonicalReader.DimensionId(dim), minX, minY, minZ, maxX, maxY, maxZ, rules);
    }

    internal async Task CountAsync()
    {
        if (_shapeMode.Checked) { await CountShapeAsync(); return; }
        if (Read() is not var (dimId, minX, minY, minZ, maxX, maxY, maxZ, rules)) return;
        string location = _world.Location;
        bool rescue = _world.OpenedForRescue;
        SetBusy(true);
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Counting…";
        try
        {
            var summary = await Task.Run(() =>
            {
                using var own = rescue ? null : WorldLoader.Open(location);
                var w = own ?? _world;
                var d = w.Dimensions.First(x => CanonicalReader.DimensionId(x) == dimId);
                var (low, high) = StructurePlacement.Height(w, dimId);
                return BlockReplace.Count(w, d, minX, Math.Max(minY, low), minZ, maxX, Math.Min(maxY, high), maxZ, rules);
            });
            _status.ForeColor = summary.Total > 0 ? C.FG : C.ORANGE;
            _status.Text = summary.Total == 0 ? "Nothing in the box matches: no block would change."
                : $"{summary.Total:N0} block{(summary.Total == 1 ? "" : "s")} would change: " + string.Join(", ", summary.Changes.Take(6).Select(c => $"{c.Count:N0} {WorldSearch.Nice(c.From)} → {WorldSearch.Nice(c.To)}"))
                  + (summary.Changes.Count > 6 ? $", and {summary.Changes.Count - 6} more" : "") + "."
                  + (summary.BlockEntitiesRemoved > 0 ? $" {summary.BlockEntitiesRemoved:N0} chest, sign or other block entit{(summary.BlockEntitiesRemoved == 1 ? "y" : "ies")} would go with its block." : "");
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or ArgumentException or SaveFormatException)) Log.Error("Counting a replace failed", ex);
            Warn(ex.Message);
        }
        finally { SetBusy(false); }
    }

    async Task ReplaceAsync()
    {
        if (_biomeMode.Checked) { await BiomeAsync(); return; }
        if (_shapeMode.Checked) { await ShapeAsync(); return; }
        if (Read() is not var (dimId, minX, minY, minZ, maxX, maxY, maxZ, rules)) return;
        if (_world.OpenedForRescue) { Warn("This world was opened for rescue. Rescue it into a new world first, then replace blocks in that."); return; }
        if (Output() is not string output) return;
        await IntoCopyAsync(output, "Replacing blocks failed", "the blocks replaced", (world, progress, token) =>
        {
            var (result, summary) = BlockReplace.ReplaceIntoCopy(world, dimId, minX, minY, minZ, maxX, maxY, maxZ, rules, output, progress, token);
            return (result, $"Replaced {summary.Total:N0} blocks in a copy");
        });
    }

    /// <summary>The box given the chosen biome, in a new copy (BiomePaint through StructurePlacement.EditIntoCopy).</summary>
    async Task BiomeAsync()
    {
        if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension dim }) { Warn("Choose a dimension."); return; }
        int minX = (int)_minX.Value, minY = (int)_minY.Value, minZ = (int)_minZ.Value, maxX = (int)_maxX.Value, maxY = (int)_maxY.Value, maxZ = (int)_maxZ.Value;
        if (maxX < minX || maxY < minY || maxZ < minZ) { Warn("The \"to\" corner must be at or past the \"from\" corner."); return; }
        if (BiomePaint.Canonical(_biome.Text) is not string biome) { Warn($"There is no biome called \"{_biome.Text.Trim()}\". Start typing to see them."); return; }
        if (_world.OpenedForRescue) { Warn("This world was opened for rescue. Rescue it into a new world first, then change that."); return; }
        if (Output() is not string output) return;
        string dimId = CanonicalReader.DimensionId(dim);
        await IntoCopyAsync(output, "Changing a biome failed", "the new biome", (world, progress, token) =>
            (BiomePaint.PaintIntoCopy(world, dimId, minX, minY, minZ, maxX, maxY, maxZ, biome, output, progress, token), $"The box is {WorldSearch.Nice(biome)} in a copy"));
    }

    /// <summary>The box's land flattened, smoothed, raised or lowered in a new copy (LandShape through EditIntoCopy).</summary>
    async Task ShapeAsync()
    {
        if (ReadShape() is not var (dimId, minX, minZ, maxX, maxZ, options)) return;
        if (_world.OpenedForRescue) { Warn("This world was opened for rescue. Rescue it into a new world first, then shape that."); return; }
        if (Output() is not string output) return;
        await IntoCopyAsync(output, "Shaping the land failed", "the land shaped", (world, progress, token) =>
            (LandShape.ShapeIntoCopy(world, dimId, minX, minZ, maxX, maxZ, options, output, progress, token), "The land is shaped in a copy"));
    }

    /// <summary>The box and the shape, checked; null (with the reason shown) when something is missing.</summary>
    (string DimId, int MinX, int MinZ, int MaxX, int MaxZ, LandShapeOptions Options)? ReadShape()
    {
        if (_dimension.SelectedItem is not ComboItem { Tag: WorldDimension dim }) { Warn("Choose a dimension."); return null; }
        int minX = (int)_minX.Value, minZ = (int)_minZ.Value, maxX = (int)_maxX.Value, maxZ = (int)_maxZ.Value;
        if (maxX < minX || maxZ < minZ) { Warn("The \"to\" corner must be at or past the \"from\" corner."); return null; }
        var kind = (LandShapeKind)Math.Max(0, _shapeKind.SelectedIndex);
        return (CanonicalReader.DimensionId(dim), minX, minZ, maxX, maxZ, new LandShapeOptions(kind, (int)_shapeAmount.Value, (int)_softEdge.Value));
    }

    /// <summary>What shaping would do, without writing anything: how many columns move, and how far.</summary>
    async Task CountShapeAsync()
    {
        if (ReadShape() is not var (dimId, minX, minZ, maxX, maxZ, options)) return;
        string location = _world.Location;
        bool rescue = _world.OpenedForRescue;
        SetBusy(true);
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Reading the land…";
        try
        {
            var plan = await Task.Run(() =>
            {
                using var own = rescue ? null : WorldLoader.Open(location);
                return LandShape.Plan(own ?? _world, dimId, minX, minZ, maxX, maxZ, options);
            });
            _status.ForeColor = plan.Changed > 0 ? C.FG : C.ORANGE;
            _status.Text = plan.Columns == 0 ? "There is no ground in the box (no chunks there, or only air and water)."
                : plan.Changed == 0 ? "The land in the box is already that shape: nothing would change."
                : $"{plan.Changed:N0} of {plan.Columns:N0} columns would move: up to {plan.MostUp} block{(plan.MostUp == 1 ? "" : "s")} up and {plan.MostDown} down.";
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or ArgumentException or SaveFormatException)) Log.Error("Planning a land shape failed", ex);
            Warn(ex.Message);
        }
        finally { SetBusy(false); }
    }

    /// <summary>The copy's folder, checked: a new or empty folder, not inside the world; null (said) otherwise.</summary>
    string? Output()
    {
        string output = _folder.Text.Trim();
        if (output.Length == 0) { Warn("Choose a folder for the copy."); return null; }
        try { output = Path.GetFullPath(output); } catch { Warn("That folder path is not valid."); return null; }
        if (File.Exists(output)) { Warn("A file with that name already exists; choose another folder."); return null; }
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) { Warn("That folder is not empty; choose a new folder name."); return null; }
        string source = Path.GetFullPath(_world.Location).TrimEnd('\\');
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase)) { Warn("The copy cannot go inside the world itself."); return null; }
        return output;
    }

    /// <summary>
    /// Runs a change into a new copy from a world of its own off disk, with the bar and Stop; says what was done (or what the
    /// copy's check found) and offers to open it.
    /// </summary>
    async Task IntoCopyAsync(string output, string failure, string savedWith, Func<SaveWorld, IProgress<ConversionProgress>, CancellationToken, (PlacementResult Result, string Done)> run)
    {
        string location = _world.Location;
        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        _bar.Visible = true; _bar.Value = 0; _bar.BarColor = C.ACCENT2;
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Reading the box…";
        var progress = new Progress<ConversionProgress>(p =>
        {
            if (!_running) return;
            _bar.Marquee = p.Total <= 1;
            _bar.Value = p.Total > 0 ? p.Done / (double)p.Total : 0;
            _status.Text = p.Total > 1 && p.Done < p.Total ? $"{p.Stage} · {p.Done:N0} of {p.Total:N0}" : p.Stage;
        });
        try
        {
            var (result, done) = await Task.Run(() =>
            {
                using var world = WorldLoader.Open(location);
                return run(world, progress, token);
            }, token);
            _bar.Marquee = false; _bar.Value = 1;
            OpenAfterClose = result.OpenPath;
            string notes = result.Notes.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", result.Notes.Select(n => "• " + n)) : "";
            if (!result.Verified)
            {
                _bar.BarColor = C.RED;
                _status.ForeColor = C.RED;
                _status.Text = "Saved, but the check of the copy found a problem. Read the report before using it.";
                Dialogs.ShowText(this, "The copy did not pass its check", string.Join("\r\n", result.Problems.Select(p => "✗ " + p)) + notes);
            }
            else
            {
                _status.ForeColor = C.GREEN;
                _status.Text = $"✓  {done} ({result.ChunksChanged:N0} chunks rewritten, everything else unchanged, {ConvertDialog.Duration(result.Elapsed)}).";
                if (Dialogs.Ask(this, $"Saved the copy with {savedWith} to:\n{OpenAfterClose}{notes}\n\nOpen it now?") == DialogResult.Yes) Close();
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
            if (ex is not (IOException or ArgumentException or SaveFormatException or NotSupportedException or InvalidOperationException)) Log.Error(failure, ex);
            Warn(ex.Message);
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
        foreach (var c in new Control[] { _dimension, _minX, _minY, _minZ, _maxX, _maxY, _maxZ, _folder, _browseOut, _replace, _count }) c.Enabled = !busy;
        _rules.Enabled = !busy;
        _biome.Enabled = !busy;
        _shapeKind.Enabled = _shapeAmount.Enabled = _softEdge.Enabled = !busy;
        _blocksMode.Enabled = _biomeMode.Enabled = _shapeMode.Enabled = !busy;
        if (!busy) _count.Enabled = _blocksMode.Checked || _shapeMode.Checked;
    }

    static void TryRemove(string output)
    {
        try { if (Directory.Exists(output)) Directory.Delete(output, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static WorldInfo SafeInfo(SaveWorld w) { try { return WorldInfoReader.Read(w); } catch { return new WorldInfo(); } }
    NumberField Num(int v) => new() { Width = this.Px(72), Height = this.Px(28), Minimum = -30_000_000, Maximum = 30_000_000, Value = v, Margin = new Padding(0, 0, this.Px(6), 0) };

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(110), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
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
