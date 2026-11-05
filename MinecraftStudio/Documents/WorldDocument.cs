// MinecraftStudio/Documents/WorldDocument.cs: A world's tab: sections down the side (overview, map and chunks,
// players and items, cheats and game mode) and the chosen one beside them.
// Owns: WorldDocument.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>
/// A world's tab: sections down the side (overview, map and chunks, players and items, cheats and game mode) and
/// the chosen one beside them. The map with selection, pruning and world facts is this document's own page.
/// </summary>
sealed partial class WorldDocument : Document
{
    readonly SaveWorld _world;
    readonly MapView _map;
    readonly Panel _mapPage, _pages;
    readonly SectionNav _nav;
    OverviewPage? _overviewPage;
    PlayersPage? _playersPage;
    CheatsPage? _cheatsPage;
    PacksPage? _packsPage;
    readonly DarkComboBox _dimBox;
    readonly DarkComboBox _overlayBox;
    readonly Func<int, int, bool>? _slimeRule;
    readonly ToolStripMenuItem _showDeaths;
    readonly DarkTextField _sliceBox;
    readonly ListView _facts;
    readonly Label _selectionLabel;
    readonly Dictionary<MapTool, ToolStripButton> _toolButtons = [];
    readonly NumberField _brushSize;
    readonly ToolStripButton _subtract, _undoButton, _redoButton;
    /// <summary>Deletions made since the last save that are still in effect (undo takes them back).</summary>
    int _pendingDeletions;
    bool _dirty;

    public WorldDocument(SaveWorld world)
    {
        _world = world;
        Key = world;
        _map = new MapView { Dock = DockStyle.Fill, Font = Theme.Ui(9f) };
        _map.HoverInfo += (_, s) => Main?.SetStatus(s);
        _map.SelectionChanged += (_, _) => UpdateSelection();
        _map.ChunkActivated += (_, pos) => OpenChunk(pos);
        _map.BlockMenu += (_, at) => ShowBlockMenu(at);
        _map.Progress += (_, p) => { if (Visible) Main?.SetProgress(p < 1 ? p : null); };

        var bar = Theme.ToolStrip();
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        _dimBox = new DarkComboBox { AccessibleName = "Dimension" };
        foreach (var d in world.Dimensions) _dimBox.Items.Add(new ComboItem(d.DisplayName, $"{d.ChunkPositions.Count:N0}"));
        _dimBox.SelectedIndexChanged += (_, _) => ShowDimension();
        _overlayBox = new DarkComboBox { AccessibleName = "Overlay" };
        _overlayBox.Items.AddRange([new ComboItem("No overlay"), new ComboItem("Time spent (InhabitedTime)"), new ComboItem("Last update"), new ComboItem("Biomes")]);
        // Slime chunks come from the seed (Java) or the chunk's place (Bedrock): drawn over land not generated yet too.
        _slimeRule = SlimeChunks.For(world);
        if (_slimeRule != null) _overlayBox.Items.Add(new ComboItem("Slime chunks (overworld)"));
        _overlayBox.SelectedIndex = 0;
        _overlayBox.SelectedIndexChanged += (_, _) => _map.Overlay = _overlayBox.SelectedIndex switch { 1 => MapOverlay.InhabitedTime, 2 => MapOverlay.LastUpdate, 3 => MapOverlay.Biomes, 4 => MapOverlay.SlimeChunks, _ => MapOverlay.None };
        _sliceBox = new DarkTextField { Padding = new Padding(this.Px(6), this.Px(4), this.Px(4), 0) };
        _sliceBox.Box.AccessibleName = "Cave view Y";
        _sliceBox.Box.PlaceholderText = "top";
        _sliceBox.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { _map.SliceY = int.TryParse(_sliceBox.Text, out int y) ? y : null; e.SuppressKeyPress = true; } };

        var select = new ToolStripDropDownButton("Select", Theme.Glyph(Glyphs.Select, C.FG, this.Px(16))) { ForeColor = C.FG, ToolTipText = "Select chunks by rule, grow or shrink the selection, save or load it" };
        select.DropDown.BackColor = C.BG2;
        select.DropDownItems.AddRange(
        [
            Item("All chunks", (_, _) => _map.SelectAll()),
            Item("None", (_, _) => _map.SetSelection([], "Select none")),
            Item("Invert", (_, _) => _map.InvertSelection()),
            new ToolStripSeparator(),
            Item("Area around the spawn, a player or a point…", (_, _) => SelectArea()),
            Item("Grow by one chunk", (_, _) => _map.GrowSelection(1)),
            Item("Shrink by one chunk", (_, _) => _map.ShrinkSelection(1)),
            new ToolStripSeparator(),
            Item("Save selection… (MCA Selector .csv)", (_, _) => ExportSelection()),
            Item("Load selection… (MCA Selector .csv)", (_, _) => ImportSelection()),
            new ToolStripSeparator(),
            Item("See the selection in 3D…", async (_, _) =>
            {
                if (MapSelectionBox() is { } box) await Show3D(box);
                else Main?.SetStatus("Select chunks on the map first (or right-click a chunk: See this in 3D).");
            }),
            new ToolStripSeparator(),
            Item("Tips: Ctrl+click toggles one chunk · Alt removes with any tool · Esc clears", null),
        ]);

        // What the map marks (Show), what it can be saved as (Export) and the windows that look through the world (Tools).
        _showStructures = new ToolStripMenuItem("Structures (gold ring: loot not taken yet)", Theme.Glyph(Glyphs.Pin, C.FG_DIM, this.Px(16)))
        {
            CheckOnClick = true, ForeColor = C.FG,
            ToolTipText = "Villages, outposts, shipwrecks, buried treasure, temples, fortresses and every other structure marked on the map",
        };
        _showStructures.CheckedChanged += async (_, _) => await ShowStructuresAsync();
        _showDeaths = new ToolStripMenuItem("Where players last died") { CheckOnClick = true, Checked = true, ForeColor = C.FG, ToolTipText = "A red cross where each player last died (Java 1.19 and newer, Bedrock)" };
        _showDeaths.CheckedChanged += (_, _) => _map.SetDeaths(_showDeaths.Checked ? MapMarks.Deaths(_world, CurrentDim) : null);
        var show = DropDown("Show", Glyphs.Pin, "What the map marks besides the land", _showStructures, _showDeaths);
        var export = DropDown("Export", Glyphs.Picture, "Save the map",
            Item("Picture of this dimension (PNG)…", async (_, _) => await ExportPng()),
            Item("Heights of this dimension (16-bit grey PNG)…", async (_, _) => await ExportHeights()),
            Item("Web map of this dimension (a folder to open in any browser)…", async (_, _) => await ExportWebMap()));
        var toolsMenu = DropDown("Tools", Glyphs.Stats, "Windows that look through the whole world and mark what they find on this map",
            Item("Structures…", (_, _) => ShowStructuresWindow()),
            Item("Busiest chunks (lag finder)…", (_, _) => ShowBusyChunks()),
            Item("Lag machines…", (_, _) => ShowLagMachines()),
            Item("Mods and add-ons…", (_, _) => ShowMods()),
            Item("Compare with another world…", (_, _) => ShowCompare()));
        bar.Items.AddRange(
        [
            new ToolStripHost(_dimBox, this.Px(190)),
            new ToolStripSeparator(),
            Button("", Glyphs.ZoomIn, "Zoom in (+ / wheel)", (_, _) => _map.ZoomBy(1.5)),
            Button("", Glyphs.ZoomOut, "Zoom out (- / wheel)", (_, _) => _map.ZoomBy(1 / 1.5)),
            Button("Fit", Glyphs.Home, "Show the whole dimension", (_, _) => _map.ZoomToFit()),
            new ToolStripSeparator(),
            show,
            new ToolStripHost(_overlayBox, this.Px(180)),
            new ToolStripLabel("Cave Y") { ForeColor = C.FG_DIM, ToolTipText = "Cave / slice view: start scanning at this Y (empty = from the top). Press Enter." },
            new ToolStripHost(_sliceBox, this.Px(56), this.Px(24)),
            new ToolStripSeparator(),
            Button("Save", Glyphs.Save, "Write the changes to the save (a backup is made first)", async (_, _) => await SaveAsync()),
            export,
            new ToolStripSeparator(),
            Button("Find…", Glyphs.Find, "Find blocks, items (in chests and the shulker boxes in them too), mobs and words on signs (Ctrl+F)", (_, _) => ShowFind()),
            toolsMenu,
        ]);

        // Selection tools for pruning: shapes, lasso and brush, with add or remove.
        var tools = Theme.ToolStrip();
        tools.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        tools.Items.Add(new ToolStripLabel("Select") { ForeColor = C.FG_DIM });
        foreach (var (tool, text, tip) in new[]
        {
            (MapTool.Pan, "Pan", "Drag to move the map; Shift+drag still selects a rectangle (H)"),
            (MapTool.Rectangle, "Rectangle", "Drag a rectangle of chunks (R)"),
            (MapTool.Circle, "Circle", "Drag from the centre outwards (C)"),
            (MapTool.Lasso, "Lasso", "Draw a loop around the chunks you want (L)"),
            (MapTool.Brush, "Brush", "Paint over chunks; [ and ] change the size (B)"),
        })
        {
            var b = new ToolStripButton(text, ToolIcon(tool, this.Px(16), C.FG), (_, _) => SetTool(tool)) { ToolTipText = tip, ForeColor = C.FG };
            _toolButtons[tool] = b;
            tools.Items.Add(b);
        }
        _brushSize = new NumberField { Minimum = 1, Maximum = 32, Value = 2, AccessibleName = "Brush size in chunks" };
        _brushSize.ValueChanged += (_, _) => _map.BrushRadius = (int)_brushSize.Value;
        _subtract = new ToolStripButton("Remove", Theme.Glyph(Glyphs.Clear, C.FG, this.Px(16)), (_, _) => SetSubtract(!_subtract!.Checked))
        {
            ToolTipText = "The tools take chunks out of the selection instead of adding them (or hold Alt while selecting)", ForeColor = C.FG,
        };
        _undoButton = Button("Undo", Glyphs.Undo, "Undo (Ctrl+Z)", (_, _) => _map.Undo());
        _redoButton = Button("", Glyphs.Redo, "Redo (Ctrl+Y)", (_, _) => _map.Redo());
        _map.HistoryChanged += (_, _) => UpdateHistoryButtons();
        tools.Items.AddRange(
        [
            new ToolStripLabel("Size") { ForeColor = C.FG_DIM, ToolTipText = "Brush radius in chunks" },
            new ToolStripHost(_brushSize, this.Px(56), this.Px(24)),
            new ToolStripSeparator(),
            _subtract,
            _undoButton,
            _redoButton,
            select,
            new ToolStripSeparator(),
            Button("Prune…", Glyphs.Broom, "Delete chunks by time spent, distance or selection", (_, _) => Prune()),
            Button("Delete selected", Glyphs.Delete, "Delete the selected chunks (written when you save; a backup is made first)", (_, _) => DeleteSelection()),
        ]);
        _map.KeyDown += (_, e) => ToolKey(e);
        SetTool(MapTool.Pan);
        UpdateHistoryButtons();

        _facts = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None,
            BackColor = C.BG2, ForeColor = C.FG, BorderStyle = BorderStyle.None, Font = Theme.Ui(9f),
        };
        _facts.Columns.Add("Key", this.Px(110));
        _facts.Columns.Add("Value", this.Px(260));
        Theme.DarkControl(_facts);
        Theme.FillLastColumn(_facts);
        _selectionLabel = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = this.Px(64), ForeColor = C.FG_DIM, BackColor = C.BG2, Padding = new Padding(this.Px(8)), UseMnemonic = false };
        var side = new Panel { Dock = DockStyle.Left, Width = this.Px(340), BackColor = C.BG2 };
        var sideTitle = new Label { Text = world.Name, Dock = DockStyle.Top, Height = this.Px(30), Font = Theme.Ui(11f, FontStyle.Bold), ForeColor = C.FG, Padding = new Padding(this.Px(8), this.Px(6), 0, 0), UseMnemonic = false, AutoEllipsis = true };
        side.Controls.Add(_facts);
        side.Controls.Add(_selectionLabel);
        side.Controls.Add(sideTitle);
        var splitter = new Splitter { Dock = DockStyle.Left, Width = 4, BackColor = C.BG3 };

        _mapPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        _mapPage.Controls.Add(_map);
        _mapPage.Controls.Add(splitter);
        _mapPage.Controls.Add(side);
        _mapPage.Controls.Add(tools);
        _mapPage.Controls.Add(bar);
        _pages = new Panel { Dock = DockStyle.Fill };
        _pages.Controls.Add(_mapPage);
        _nav = new SectionNav { Dock = DockStyle.Left, Width = this.Px(210) };
        _nav.AddHeading("This world");
        _nav.Add("Overview", Glyphs.Home, WorldSection.Overview, "What the world is and everything you can do with it");
        _nav.Add("Map & chunks", Glyphs.Map, WorldSection.Map, "Look around, select and delete chunks");
        _nav.Add("Players & items", Glyphs.People, WorldSection.Players, "Every player's inventory, armour and ender chest");
        _nav.Add("Cheats & game mode", Glyphs.Shield, WorldSection.Cheats, "Cheats, Creative, achievements and game modes");
        _nav.Add("Packs (PC)", Glyphs.Picture, WorldSection.Packs, "Data packs, texture / resource packs and behavior packs: see, install and uninstall");
        _nav.SelectedChanged += (_, _) => { if (_nav.SelectedTag is WorldSection s) ShowSection(s); };
        Controls.Add(_pages);
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = C.BORDER });
        Controls.Add(_nav);
        FillFacts();
        if (_dimBox.Items.Count > 0) _dimBox.SelectedIndex = 0;
        else _map.SetDimension(null);
    }

    public SaveWorld World => _world;

    FindDialog? _find;
    BusyChunksDialog? _busy;
    LagMachinesDialog? _lag;
    CompareDialog? _compare;
    ModsDialog? _mods;

    /// <summary>The Find window of this world (one per world; it stays open beside the map until closed).</summary>
    public FindDialog ShowFind()
    {
        if (_find == null || _find.IsDisposed)
        {
            _find = new FindDialog(this);
            _find.Show(FindForm());
        }
        else _find.Activate();
        return _find;
    }

    /// <summary>The "Busiest chunks" window of this world (one per world, beside the map like Find).</summary>
    public BusyChunksDialog ShowBusyChunks()
    {
        if (_busy == null || _busy.IsDisposed)
        {
            _busy = new BusyChunksDialog(this);
            _busy.Show(FindForm());
        }
        else _busy.Activate();
        return _busy;
    }

    /// <summary>The "Lag machines" window of this world (one per world, beside the map like Find).</summary>
    public LagMachinesDialog ShowLagMachines()
    {
        if (_lag == null || _lag.IsDisposed)
        {
            _lag = new LagMachinesDialog(this);
            _lag.Show(FindForm());
        }
        else _lag.Activate();
        return _lag;
    }

    /// <summary>The "Mods and add-ons" window of this world (one per world, beside the map like Find).</summary>
    public ModsDialog ShowMods()
    {
        if (_mods == null || _mods.IsDisposed)
        {
            _mods = new ModsDialog(this);
            _mods.Show(FindForm());
        }
        else _mods.Activate();
        return _mods;
    }

    /// <summary>The "Compare with another world" window of this world (one per world, beside the map like Find).</summary>
    public CompareDialog ShowCompare()
    {
        if (_compare == null || _compare.IsDisposed)
        {
            _compare = new CompareDialog(this);
            _compare.Show(FindForm());
        }
        else _compare.Activate();
        return _compare;
    }

    /// <summary>The id of the dimension on the map, or null.</summary>
    public string? MapDimensionId => _map.Dimension?.Id;

    /// <summary>The chunks selected on the map when it shows that dimension (else none).</summary>
    public IReadOnlyCollection<ChunkPos> SelectedChunks(string dimensionId) => _map.Dimension?.Id == dimensionId ? _map.Selection.ToList() : [];

    /// <summary>Shows what a search found on the map: that dimension, every place marked, the one chosen circled in the middle.</summary>
    public void ShowFound(string dimensionId, IReadOnlyList<SearchHit> hits, SearchHit? focus)
    {
        ShowSection(WorldSection.Map);
        int index = _world.Dimensions.ToList().FindIndex(d => d.Id == dimensionId);
        if (index >= 0 && _dimBox.SelectedIndex != index) _dimBox.SelectedIndex = index;
        var here = hits.Where(h => h.Dimension == null).ToList();
        _map.SetFound(here.Select(h => (h.X, h.Z)), focus is { } f ? (f.X, f.Z) : null);
        var centre = focus ?? (here.Count > 0 ? here[0] : null);
        if (centre != null) _map.CenterOn(centre.X + 0.5, centre.Z + 0.5);
        Main?.SetStatus(focus is { } a ? $"{WorldSearch.Nice(a.What)} at {a.X}, {a.Y}, {a.Z}" + (a.Where.Length > 0 ? $" ({a.Where})" : "") : $"{here.Count:N0} found: marked on the map");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { ShowFind(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        // The Find window goes with its world's tab.
        if (disposing && _find is { IsDisposed: false } find) find.Close();
        if (disposing && _busy is { IsDisposed: false } busy) busy.Close();
        if (disposing && _lag is { IsDisposed: false } lag) lag.Close();
        if (disposing && _structuresWindow is { IsDisposed: false } structures) structures.Close();
        if (disposing) { _structuresCts.Cancel(); _structuresCts.Dispose(); }
        if (disposing && _compare is { IsDisposed: false } compare) compare.Close();
        if (disposing && _mods is { IsDisposed: false } mods) mods.Close();
        base.Dispose(disposing);
    }

    /// <summary>The dimension on the map and the XZ block bounds of its current chunk selection, or null when nothing is selected (used to prefill a structure crop).</summary>
    public (WorldDimension Dim, int MinX, int MinZ, int MaxX, int MaxZ)? MapSelectionBox()
    {
        if (_map.Dimension is not { } dim || _map.Selection.Count == 0) return null;
        int minX = _map.Selection.Min(p => p.X), maxX = _map.Selection.Max(p => p.X);
        int minZ = _map.Selection.Min(p => p.Z), maxZ = _map.Selection.Max(p => p.Z);
        return (dim, minX * 16, minZ * 16, maxX * 16 + 15, maxZ * 16 + 15);
    }
    public override string Title => _world.Name;
    public override string? ToolTipText => $"{_world.Location}\n{_world.FormatDescription}";
    public override bool IsDirty => _dirty || _world.IsModified || _cheatsPage?.IsDirty == true || _playersPage?.IsDirty == true;

    /// <summary>Something outside the players page changed players (map art given): it reads them again.</summary>
    public void PlayersChanged() => _playersPage?.Reload();

    /// <summary>Shows a section (the first time the tab is shown it opens on the overview).</summary>
    /// <summary>Offers a pack file for installing into this world (on its Packs section).</summary>
    public Task InstallPack(string path)
    {
        ShowSection(WorldSection.Packs);
        return _packsPage?.InstallOffered(path) ?? Task.CompletedTask;
    }

    public void ShowSection(WorldSection section)
    {
        if (_nav.SelectedTag is not WorldSection current || current != section) { _nav.Select(section); return; }
        Control page = section switch
        {
            WorldSection.Overview => _overviewPage ??= AddPage(new OverviewPage(this)),
            WorldSection.Players => _playersPage ??= AddPage(new PlayersPage(this)),
            WorldSection.Cheats => _cheatsPage ??= AddPage(new CheatsPage(this)),
            WorldSection.Packs => _packsPage ??= AddPage(new PacksPage(this)),
            _ => _mapPage,
        };
        foreach (Control c in _pages.Controls) c.Visible = c == page;
        (page as WorldPage)?.OnShown();
        if (page == _mapPage) _map.Focus();
    }

    T AddPage<T>(T page) where T : WorldPage
    {
        page.Visible = false;
        _pages.Controls.Add(page);
        return page;
    }

    public override void OnActivated()
    {
        if (_nav.SelectedIndex < 0) ShowSection(WorldSection.Overview);
    }

    /// <summary>A page's unsaved state changed: the tab shows it.</summary>
    public void PageChanged() => OnStateChanged();

    /// <summary>The world's facts changed (settings saved): the map page's list and the overview show them again.</summary>
    public void RefreshFacts()
    {
        FillFacts();
        if (_overviewPage?.Visible == true) _overviewPage.OnShown();
    }
    public override bool CanSave => true;

    ToolStripMenuItem Item(string text, EventHandler? click) => new(text, null, click) { ForeColor = click == null ? C.FG_DIM : C.FG, Enabled = click != null };

    ToolStripDropDownButton DropDown(string text, string glyph, string tip, params ToolStripItem[] items)
    {
        var b = new ToolStripDropDownButton(text, Theme.Glyph(glyph, C.FG, this.Px(16))) { ForeColor = C.FG, ToolTipText = tip };
        b.DropDown.BackColor = C.BG2;
        b.DropDownItems.AddRange(items);
        return b;
    }

    ToolStripButton Button(string text, string glyph, string tip, EventHandler click) => new(text, Theme.Glyph(glyph, C.FG, this.Px(16)), click)
    {
        ToolTipText = tip,
        ForeColor = C.FG,
        DisplayStyle = text.Length > 0 ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image,
    };

    WorldDimension? CurrentDim => _dimBox.SelectedIndex >= 0 && _dimBox.SelectedIndex < _world.Dimensions.Count ? _world.Dimensions[_dimBox.SelectedIndex] : null;

    (int X, int Z)? Spawn() => MapMarks.Spawn(_world, CurrentDim);

    List<(string Name, double X, double Z)> PlayersInDimension() => MapMarks.Players(_world, CurrentDim);

    void ShowDimension()
    {
        var d = CurrentDim;
        _map.SetDimension(d, Spawn(), PlayersInDimension());
        _map.SlimeChunks = d != null && Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld" ? _slimeRule : null;
        _map.SetDeaths(_showDeaths?.Checked == true ? MapMarks.Deaths(_world, d) : null);
        _sliceBox.Text = d != null && !d.HasSky ? "100" : "";
        UpdateSelection();
        _ = ShowStructuresAsync();
    }

    void FillFacts()
    {
        _facts.BeginUpdate();
        _facts.Items.Clear();
        foreach (var (k, v) in _world.Summary())
        {
            var item = new ListViewItem(k) { ForeColor = C.FG_DIM };
            item.SubItems.Add(v).ForeColor = C.FG;
            _facts.Items.Add(item);
        }
        _facts.EndUpdate();
    }

    void UpdateSelection()
    {
        var d = CurrentDim;
        int total = _map.Chunks.Count, sel = _map.Selection.Count;
        _selectionLabel.Text = d == null ? "" :
            $"{d.DisplayName}: {total:N0} chunks\n" +
            (sel > 0 ? $"{sel:N0} selected ({sel * 100.0 / Math.Max(1, total):0.#}%)" : "No selection") +
            (_world.IsModified ? "\nUnsaved changes" : "");
    }
}
