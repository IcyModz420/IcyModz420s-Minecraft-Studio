// MinecraftStudio/MainForm.cs: the main window — menu, explorer tree, toolbar, status bar and the tabbed documents that host every editor.
// Owns: MainForm.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.Phone;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio;

[System.ComponentModel.DesignerCategory("Code")]
sealed partial class MainForm : Form
{
    readonly AppSettings _settings = Backup.Settings;
    AutoBackups? _autoBackups;
    readonly string[] _args;
    readonly List<SaveWorld> _openWorlds = [];
    readonly List<Document> _docs = [];
    bool _busy;

    MenuStrip _menu = null!;
    ToolStrip _toolbar = null!;
    StatusStrip _status = null!;
    ToolStripStatusLabel _statusText = null!, _statusRight = null!;
    ToolStripHost _progress = null!;
    SlimProgress _progressBar = null!;
    ToolStripMenuItem _recentMenu = null!;
    SplitContainer _split = null!;
    TreeView _tree = null!;
    DarkTextField _treeFilter = null!;
    ImageList _icons = null!;
    TabStrip _tabs = null!;
    Panel _host = null!, _welcome = null!;
    FlowLayoutPanel _recentCards = null!;
    SectionHeader _recentHeader = null!;
    ImageList _bigIcons = null!;
    List<FoundWorld> _found = [];

    public MainForm(string[] args)
    {
        _args = args;
        SuspendLayout();
        // Every size below is scaled explicitly with Px(); automatic scaling would scale them a second time.
        AutoScaleMode = AutoScaleMode.None;
        Text = Core.AppInfo.WindowTitle;
        BackColor = C.BG;
        ForeColor = C.FG;
        Font = Theme.Ui(9f);
        Size = new Size(this.Px(1500), this.Px(920));
        MinimumSize = new Size(this.Px(960), this.Px(600));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;
        _icons = Icons.Create(this.Px(16));
        _bigIcons = Icons.Create(this.Px(32));
        Icon = MakeIcon();

        BuildMenu();
        BuildToolbar();
        BuildStatus();
        BuildLayout();
        // Docking: the last added control is docked first (menu on top, then toolbar, status at the bottom, split fills).
        Controls.Add(_split);
        Controls.Add(_status);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        ResumeLayout(false);
        PerformLayout();

        RestoreWindow();
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                foreach (var p in paths) await OpenPath(p);
        };
        BuildRecent();
        UpdateTitle();
    }

    Icon MakeIcon()
    {
        using var list = Icons.Create(32);
        using var bmp = new Bitmap(list.Images["java"]!);
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Worlds backed up by themselves after each play session (when switched on in Settings).
        _autoBackups = new AutoBackups(() => _found, s => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => SetStatus(s)); });
        _split.SplitterDistance = Math.Clamp(this.Px(_settings.TreeWidth), this.Px(200), Math.Max(this.Px(200), Width / 2));
        await ScanWorlds();
        foreach (var a in _args) if (File.Exists(a) || Directory.Exists(a)) await OpenPath(a);
    }

    // ------------------------------------------------------------------ construction

    ToolStripMenuItem Item(string text, EventHandler onClick, string? shortcut = null, string? glyph = null) =>
        new(text, glyph != null ? Theme.Glyph(glyph, C.FG_DIM, this.Px(16)) : null, onClick) { ForeColor = C.FG, ShortcutKeyDisplayString = shortcut };

    /// <summary>A heading inside a menu: the group the items under it belong to (not clickable; keys skip it).</summary>
    ToolStripLabel Heading(string text) => new(text)
    {
        ForeColor = C.FG_FAINT, Font = Theme.Ui(7.5f, FontStyle.Bold), Margin = new Padding(0, this.Px(6), 0, this.Px(1)),
    };

    /// <summary>A menu action on the selected world's tab (opened first when it isn't).</summary>
    EventHandler OnWorldTab(Action<WorldDocument> action) => (_, _) =>
    {
        if (SelectedWorld() is not SaveWorld w) { Dialogs.Info(this, "Open or select a world first."); return; }
        if (OpenWorldDocument(w) is { } doc) action(doc);
    };

    static ToolStripMenuItem Menu(string text, params ToolStripItem[] items)
    {
        var m = new ToolStripMenuItem(text) { ForeColor = C.FG };
        m.DropDownItems.AddRange(items);
        m.DropDown.BackColor = C.BG2;
        return m;
    }

    void BuildMenu()
    {
        _menu = new MenuStrip { Renderer = new DarkRenderer(), BackColor = C.BG3, ForeColor = C.FG, Padding = new Padding(this.Px(4), this.Px(2), 0, this.Px(2)), Font = Theme.Ui(9f) };
        _recentMenu = new ToolStripMenuItem("Recent") { ForeColor = C.FG };
        _recentMenu.DropDown.BackColor = C.BG2;
        _menu.Items.Add(Menu("&File",
            Item("Open World Folder…", async (_, _) => await OpenFolderDialog(), "Ctrl+O", Glyphs.Folder),
            Item("Open File… (NBT, level.dat, region, console save, Classic/Indev level)", async (_, _) => await OpenFileDialog(), "Ctrl+Shift+O", Glyphs.Open),
            Item("Open in the NBT Editor… (any NBT file, level.dat too)", async (_, _) => await OpenNbtEditor(), null, Glyphs.Database),
            _recentMenu,
            Item("Get Worlds from an Android Phone…", (_, _) => ShowPhone(), null, Glyphs.Import),
            new ToolStripSeparator(),
            Item("Save", async (_, _) => await SaveActive(), "Ctrl+S", Glyphs.Save),
            Item("Close Tab", (_, _) => CloseActive(), "Ctrl+W"),
            Item("Close World", (_, _) => CloseWorld(SelectedWorld()), null),
            new ToolStripSeparator(),
            Item("Settings…", (_, _) => ShowSettings(), null, Glyphs.Settings),
            Item("Exit", (_, _) => Close(), "Alt+F4")));
        _menu.Items.Add(Menu("&World",
            Heading("PAGES"),
            Item("Overview", (_, _) => ShowWorldSection(WorldSection.Overview), null, Glyphs.Home),
            Item("Map && Chunks", (_, _) => ShowWorldSection(WorldSection.Map), null, Glyphs.Map),
            Item("Players && Items", (_, _) => ShowWorldSection(WorldSection.Players), null, Glyphs.People),
            Item("Cheats && Game Mode", (_, _) => ShowWorldSection(WorldSection.Cheats), null, Glyphs.Shield),
            Item("Packs (PC)", (_, _) => ShowWorldSection(WorldSection.Packs), null, Glyphs.Picture),
            Item("Packs on This PC…", async (_, _) => await ShowPacksOverview(), null, Glyphs.Find),
            Heading("LOOK AROUND"),
            Item("Find Blocks, Items and Mobs…", OnWorldTab(d => d.ShowFind()), "Ctrl+F", Glyphs.Find),
            Item("Structures…", OnWorldTab(d => d.ShowStructuresWindow()), null, Glyphs.Pin),
            Item("Busiest Chunks (Lag Finder)…", OnWorldTab(d => d.ShowBusyChunks()), null, Glyphs.Stats),
            Item("Lag Machines…", OnWorldTab(d => d.ShowLagMachines()), null, Glyphs.Warning),
            Item("Mods and Add-ons…", OnWorldTab(d => d.ShowMods()), null, Glyphs.Tag),
            Item("Compare with Another World…", OnWorldTab(d => d.ShowCompare()), null, Glyphs.Filter),
            Heading("BUILD AND CHANGE"),
            Item("Save a Structure (Crop a Building)…", async (_, _) => await SaveStructure(), null, Glyphs.Copy),
            Item("Place a Structure…", async (_, _) => await PlaceStructure(), null, Glyphs.Paste),
            Item("Open a Structure File (Turn, Convert, Materials)…", async (_, _) => await OpenStructureFile(), null, Glyphs.Open),
            Item("Replace Blocks, Change the Biome or Shape the Land…", async (_, _) => await ReplaceBlocks(), null, Glyphs.Edit),
            Item("Map Art…", async (_, _) => await ShowMapArt(), null, Glyphs.Picture),
            Heading("CONVERT AND KEEP SAFE"),
            Item("Convert World…", async (_, _) => await ShowConvert(), "Ctrl+Shift+C", Glyphs.Convert),
            Item("Back Up Now", async (_, _) => await BackupNow(), null, Glyphs.Backup),
            Item("Backups (Timeline, Restore)…", async (_, _) => await ShowBackups(), null, Glyphs.Backup),
            Item("Save World as a File (.mcworld / .zip)…", async (_, _) => await ShareWorld(), null, Glyphs.Export),
            Item("Check for Problems…", async (_, _) => await CheckWorld(), null, Glyphs.Check),
            Item("Rescue a Damaged World…", async (_, _) => await ShowRescue(), null, Glyphs.Repair),
            Item("Optimize / Compact Storage", async (_, _) => await Optimize(), null, Glyphs.Broom),
            Heading("ADVANCED"),
            Item("Edit level.dat", (_, _) => { if (SelectedWorld() is SaveWorld w) OpenLevelDat(w); }, null, Glyphs.Document),
            Item("Bedrock Database Browser", (_, _) => { if (SelectedWorld() is BedrockWorld b) OpenDocument(new DatabaseDocument(b)); }, null, Glyphs.Database),
            Item("Show in Explorer", (_, _) => { if (SelectedWorld() is SaveWorld w) Explorer.Select(w.Location); }, null, Glyphs.Folder),
            Item("Refresh World List", async (_, _) => await ScanWorlds(), "F5", Glyphs.Refresh)));
        _menu.Items.Add(Menu("&Create",
            Heading("WORLDS"),
            Item("New World from a Heightmap…", async (_, _) => await NewWorldFromHeightmap(), null, Glyphs.Map),
            Heading("SKINS"),
            Item("Skin Studio…", (_, _) => SkinDocument.Start(this), "Ctrl+Shift+K", Glyphs.Person),
            Item("New Skin from the Game's Skins…", (_, _) => SkinDocument.NewFromGame(this), null),
            Item("Open Skin…", (_, _) => SkinDocument.OpenDialog(this), null, Glyphs.Open),
            Heading("TEXTURE PACKS"),
            Item("Texture Studio…", (_, _) => TextureDocument.Start(this), "Ctrl+Shift+T", Glyphs.Picture),
            Item("New Texture Pack from a Game's Textures…", (_, _) => TextureDocument.NewPack(this), null),
            Item("Open Texture Pack…", (_, _) => TextureDocument.OpenDialog(this), null, Glyphs.Open),
            Item("Open Texture Pack Folder…", (_, _) => TextureDocument.OpenFolderDialog(this), null, Glyphs.Folder),
            Heading("MOBS"),
            Item("Mob Studio…", (_, _) => MobDocument.Start(this), "Ctrl+Shift+M", Glyphs.Picture),
            Item("Open Mob Pack…", (_, _) => MobDocument.OpenDialog(this), null, Glyphs.Open),
            Item("Animation Studio…", (_, _) => AnimationDocument.Start(this), "Ctrl+Shift+A", Glyphs.Person),
            Item("Open Animation Pack…", (_, _) => AnimationDocument.OpenDialog(this), null, Glyphs.Open),
            Heading("PACKS"),
            Item("Check a Pack (Resource or Data)…", async (_, _) => await CheckPack(), null, Glyphs.Check),
            Item("New Data Pack or Add-on…", (_, _) => { using var d = new NewPackDialog(); d.ShowDialog(this); }, null, Glyphs.Add)));
        _menu.Items.Add(Menu("&Help",
            Item("What Works Where (PC, Phones, Consoles)…", (_, _) => ShowWhatWorksWhere(), null, Glyphs.List),
            Item("Moving Worlds Between Devices", (_, _) => Dialogs.ShowText(this, "Moving worlds between devices", Core.World.PlatformSupport.MovingWorldsText()), null, Glyphs.Info),
            Item("Supported Formats", (_, _) => Dialogs.ShowText(this, "Supported formats", SupportedFormats), "F1", Glyphs.Info),
            Item("What's New", (_, _) => Dialogs.ShowText(this, "What's new", WhatsNew), null, Glyphs.Info),
            Item("About", (_, _) => Dialogs.Info(this, $"{Dialogs.AppName} 1.0\nby {Core.AppInfo.Organization}\n\nAn NBT editor, map viewer, pruner, converter and save tool for every Minecraft edition:\nJava (Classic to 26.x), Bedrock / Pocket Edition (0.1 to 26.x), New 3DS Edition and Legacy Console Edition (Xbox 360, Xbox One, PS3, PS4, PS Vita, Wii U, Switch).\n\nUnofficial — not affiliated with, endorsed by, or associated with Mojang or Microsoft. \"Minecraft\" is a trademark of Mojang Synergies AB. No game files are bundled: textures and skins are read from your own installation. Conversion data from minecraft-data (MIT), GeyserMC (MIT) and PocketMine BedrockData (CC0) — see THIRD-PARTY-NOTICES.\n\nBackups are written to:\n{_settings.BackupFolder}"))));
        TagFeatures();
    }

    ToolStripButton ToolButton(string text, string glyph, string tip, EventHandler click) => new(text, Theme.Glyph(glyph, C.FG, this.Px(16)), click)
    {
        ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ForeColor = C.FG, Padding = new Padding(this.Px(4), 0, this.Px(4), 0),
    };

    void BuildToolbar()
    {
        _toolbar = Theme.ToolStrip();
        _toolbar.Padding = new Padding(this.Px(6), this.Px(3), this.Px(6), this.Px(3));
        _toolbar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        _toolbar.Items.AddRange(
        [
            ToolButton("Open World", Glyphs.Folder, "Open a world folder (Java, Bedrock, Pocket Edition, 3DS, console folders)", async (_, _) => await OpenFolderDialog()),
            ToolButton("Open File", Glyphs.Open, "Open an NBT file, region file, console save or Classic/Indev level", async (_, _) => await OpenFileDialog()),
            ToolButton("Save", Glyphs.Save, "Save the active tab (Ctrl+S)", async (_, _) => await SaveActive()),
            ToolButton("From Phone", Glyphs.Import, "Copy Minecraft worlds off an Android phone (USB cable or wireless; rooted phones too)", (_, _) => ShowPhone()),
            new ToolStripSeparator(),
            ToolButton("Convert", Glyphs.Convert, "Convert a world to another edition, console or game version (Ctrl+Shift+C)", async (_, _) => await ShowConvert()),
            ToolButton("Rescue", Glyphs.Repair, "Save what can still be read from a damaged world", async (_, _) => await ShowRescue()),
            new ToolStripSeparator(),
            ToolButton("Skin Studio", Glyphs.Person, "Make, convert and install skins for Java and Bedrock (Ctrl+Shift+K)", (_, _) => SkinDocument.Start(this)),
            ToolButton("Texture Studio", Glyphs.Picture, "Make, convert and install texture packs for any Java version and Bedrock (Ctrl+Shift+T)", (_, _) => TextureDocument.Start(this)),
            ToolButton("Mob Studio", Glyphs.Person, "Paint every mob on its 3D model and make a mob pack for Java (PC) and Bedrock (Windows PC) (Ctrl+Shift+M)", (_, _) => MobDocument.Start(this)),
            ToolButton("Animations", Glyphs.Person, "Animation Studio: see every mob move and design its animations and model (Ctrl+Shift+A)", (_, _) => AnimationDocument.Start(this)),
        ]);
    }

    void BuildStatus()
    {
        _status = new StatusStrip { Renderer = new DarkRenderer(), BackColor = C.ACCENT, SizingGrip = false, Font = Theme.Ui(9f), Padding = new Padding(this.Px(8), 0, this.Px(8), 0) };
        _statusText = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White };
        _progressBar = new SlimProgress { BackColor = Color.FromArgb(28, 88, 32), BarColor = Color.FromArgb(220, 245, 222), Thickness = this.Px(6) };
        _progress = new ToolStripHost(_progressBar, this.Px(200), this.Px(18)) { Visible = false };
        _statusRight = new ToolStripStatusLabel("") { ForeColor = Color.White };
        _status.Items.AddRange([_statusText, _progress, _statusRight]);
    }

    void BuildLayout()
    {
        _split = new SplitContainer { Dock = DockStyle.Fill, BackColor = C.BG3, SplitterWidth = this.Px(4), FixedPanel = FixedPanel.Panel1 };
        _split.Panel1.BackColor = C.BG2;
        _split.Panel2.BackColor = C.BG;
        _tree = new TreeView
        {
            Dock = DockStyle.Fill, BackColor = C.BG2, ForeColor = C.FG, BorderStyle = BorderStyle.None, ImageList = _icons,
            HideSelection = false, FullRowSelect = true, ShowLines = false, Font = Theme.Ui(9f), ItemHeight = this.Px(22),
            // Set once here: changing it later recreates the tree and collapses every node.
            ShowNodeToolTips = true,
        };
        Theme.DarkControl(_tree);
        _tree.NodeMouseDoubleClick += async (_, e) => await Activate(e.Node);
        _tree.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter && _tree.SelectedNode != null) { e.SuppressKeyPress = true; await Activate(_tree.SelectedNode); } };
        _tree.NodeMouseClick += (_, e) => { if (e.Button == MouseButtons.Right) _tree.SelectedNode = e.Node; };
        _tree.ContextMenuStrip = BuildTreeMenu();
        _treeFilter = new DarkTextField { Dock = DockStyle.Top, Height = this.Px(30), Padding = new Padding(this.Px(8), this.Px(6), this.Px(6), 0) };
        _treeFilter.Box.PlaceholderText = "Filter worlds…";
        _treeFilter.Box.AccessibleName = "Filter worlds";
        _treeFilter.Box.TextChanged += (_, _) => PopulateTree();
        var treeHeader = new Label
        {
            Text = "EXPLORER", Dock = DockStyle.Top, Height = this.Px(28), ForeColor = C.FG_DIM, Font = Theme.Ui(8.5f, FontStyle.Bold),
            Padding = new Padding(this.Px(8), this.Px(7), 0, 0),
        };
        _split.Panel1.Controls.Add(_tree);
        _split.Panel1.Controls.Add(_treeFilter);
        _split.Panel1.Controls.Add(treeHeader);

        _tabs = new TabStrip { Dock = DockStyle.Top, Height = this.Px(30), Visible = false };
        _tabs.SelectedChanged += (_, _) => ShowSelectedDocument();
        _tabs.CloseRequested += (_, t) => CloseDocument((Document)t.Tag!);
        _host = new Panel { Dock = DockStyle.Fill, BackColor = C.BG };
        _welcome = BuildWelcome();
        _host.Controls.Add(_welcome);
        _split.Panel2.Controls.Add(_host);
        _split.Panel2.Controls.Add(_tabs);
        _split.SplitterMoved += (_, _) => { _settings.TreeWidth = (int)(_split.SplitterDistance * 96.0 / DeviceDpi); };
    }

    Panel BuildWelcome()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = C.BG };
        var column = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = C.BG,
            Padding = new Padding(this.Px(40), this.Px(30), this.Px(40), this.Px(30)),
        };
        int width = this.Px(1000);
        column.Controls.Add(new Label { Text = Dialogs.AppName, Font = Theme.Ui(22f, FontStyle.Bold), ForeColor = C.FG, AutoSize = true, MaximumSize = new Size(width, 0), UseMnemonic = false });
        column.Controls.Add(new Label
        {
            Text = "Every Minecraft edition: Java (Classic to 26.x), Bedrock and Pocket Edition, New 3DS, and Legacy Console (Xbox 360 and One, PS3, PS4, Vita, Wii U, Switch).",
            ForeColor = C.FG_DIM, AutoSize = true, MaximumSize = new Size(width, 0), Font = Theme.Ui(10.5f), Margin = new Padding(0, this.Px(4), 0, this.Px(4)), UseMnemonic = false,
        });
        SectionHeader Header(string text) => new(text) { Size = new Size(width, this.Px(18)), Margin = new Padding(0, this.Px(20), 0, this.Px(10)) };
        FlowLayoutPanel Tiles(params (string Text, string Caption, string Glyph, Func<Task> Click)[] tiles)
        {
            var row = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(width + this.Px(12), 0), WrapContents = true, Margin = Padding.Empty };
            foreach (var (text, caption, glyph, click) in tiles)
            {
                var t = new ChoiceTile
                {
                    Text = text, Caption = caption, Image = Theme.Glyph(glyph, C.FG, this.Px(30)), Size = new Size(this.Px(236), this.Px(112)),
                    Margin = new Padding(0, 0, this.Px(12), this.Px(12)), Font = Theme.Ui(9.5f), AccessibleName = text,
                };
                t.Click += async (_, _) => await click();
                TipTile(t, text);
                row.Controls.Add(t);
            }
            return row;
        }
        column.Controls.Add(Header("START"));
        column.Controls.Add(Tiles(
            ("Open a world", "A world folder: Java, Bedrock, Pocket, 3DS or console", Glyphs.Folder, OpenFolderDialog),
            ("Open a file", "level.dat, NBT, a region file, .mcworld or a console save", Glyphs.Open, OpenFileDialog),
            ("Worlds from a phone", "Copy worlds off an Android phone, by cable or Wi-Fi", Glyphs.Import, () => { ShowPhone(); return Task.CompletedTask; }),
            ("NBT editor", "Edit any NBT file directly: level.dat, players, structures", Glyphs.Database, OpenNbtEditor),
            ("What works where", "Which tools work for Java, Bedrock, each console and the 3DS", Glyphs.List, () => { ShowWhatWorksWhere(); return Task.CompletedTask; })));
        column.Controls.Add(Header("WORLD TOOLS"));
        column.Controls.Add(Tiles(
            ("Convert a world", "To any edition, console or version, checked afterwards", Glyphs.Convert, () => ShowConvert()),
            ("Rescue a damaged world", "Save everything that can still be read", Glyphs.Repair, () => ShowRescue()),
            ("Packs on this PC", "Which worlds have which packs; install, switch, uninstall (PC)", Glyphs.Find, () => ShowPacksOverview()),
            ("Crop a building", "From any edition to .nbt, WorldEdit, Litematica, .mcstructure or MCEdit", Glyphs.Copy, () => SaveStructure()),
            ("Place a building", "A structure or schematic into a copy of any world, turned or mirrored", Glyphs.Paste, () => PlaceStructure()),
            ("Structure file", "See a schematic, count its materials, turn it, save it as any format", Glyphs.Open, () => OpenStructureFile()),
            ("Replace, biome or land", "Swap or fill blocks, change the biome, flatten or smooth the land, in a copy", Glyphs.Edit, () => ReplaceBlocks())));
        column.Controls.Add(Header("CREATE"));
        column.Controls.Add(Tiles(
            ("Skin Studio", "Make and install skins for Java and Bedrock", Glyphs.Person, () => { SkinDocument.Start(this); return Task.CompletedTask; }),
            ("Texture Studio", "Make texture packs and convert them between versions", Glyphs.Picture, () => { TextureDocument.Start(this); return Task.CompletedTask; }),
            ("Mob Studio", "Paint animals, monsters and every mob in 3D (PC)", Glyphs.Person, () => { MobDocument.Start(this); return Task.CompletedTask; }),
            ("Animation Studio", "See every mob move; design animations and models", Glyphs.Person, () => { AnimationDocument.Start(this); return Task.CompletedTask; }),
            ("Check a pack", "What the game would ignore in a resource pack (Java or Bedrock) or a data pack", Glyphs.Check, CheckPack),
            ("Map art", "See a world's maps as pictures; make map art from any picture", Glyphs.Picture, () => ShowMapArt()),
            ("World from a heightmap", "Land from a grey picture or real elevation data, as any edition", Glyphs.Map, NewWorldFromHeightmap),
            ("New data pack or add-on", "Set up right for any version: Java data pack or Bedrock add-on", Glyphs.Add, () => { using var d = new NewPackDialog(); d.ShowDialog(this); return Task.CompletedTask; })));
        _recentHeader = new SectionHeader("RECENT WORLDS") { Size = new Size(width, this.Px(18)), Margin = new Padding(0, this.Px(20), 0, this.Px(10)), Visible = false };
        _recentCards = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(width + this.Px(12), 0), WrapContents = true, Margin = Padding.Empty, Padding = Padding.Empty };
        column.Controls.Add(_recentHeader);
        column.Controls.Add(_recentCards);
        column.Controls.Add(new Label
        {
            Text = "Tip: double-click a world in the list on the left, or drop a world folder or save file here. A backup is made before the first change to a world each session.",
            ForeColor = C.FG_FAINT, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, this.Px(20), 0, 0), UseMnemonic = false,
        });
        p.Controls.Add(column);
        return p;
    }

    /// <summary>Opens the selected world's tab on one of its sections.</summary>
    void ShowWorldSection(WorldSection section)
    {
        if (SelectedWorld() is not SaveWorld w) { Dialogs.Info(this, "Open or select a world first."); return; }
        OpenWorldDocument(w)?.ShowSection(section);
    }

    ContextMenuStrip BuildTreeMenu()
    {
        var m = new ContextMenuStrip { Renderer = new DarkRenderer() };
        m.Items.AddRange(
        [
            new ToolStripMenuItem("Open", null, async (_, _) => { if (_tree.SelectedNode != null) await Activate(_tree.SelectedNode); }) { ForeColor = C.FG },
            new ToolStripMenuItem("Show in Explorer", null, (_, _) =>
            {
                if (_tree.SelectedNode?.Tag is FoundWorld f) Explorer.Open(f.Path);
                else if (SelectedWorld() is SaveWorld w) Explorer.Select(w.Location);
            }) { ForeColor = C.FG },
            new ToolStripMenuItem("Convert…", Theme.Glyph(Glyphs.Convert, C.FG_DIM, this.Px(16)), async (_, _) => await ShowConvert()) { ForeColor = C.FG },
            new ToolStripMenuItem("Back Up Now", null, async (_, _) => await BackupNow()) { ForeColor = C.FG },
            new ToolStripMenuItem("Rescue…", Theme.Glyph(Glyphs.Shield, C.FG_DIM, this.Px(16)), async (_, _) =>
            {
                if ((SelectedWorld()?.Location ?? (_tree.SelectedNode?.Tag as FoundWorld)?.Path) is string path) await ShowRescue(path);
            }) { ForeColor = C.FG },
            new ToolStripMenuItem("Close World", null, (_, _) => CloseWorld(SelectedWorld())) { ForeColor = C.FG },
        ]);
        return m;
    }
}
