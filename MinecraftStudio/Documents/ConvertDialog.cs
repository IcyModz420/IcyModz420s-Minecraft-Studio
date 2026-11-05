// MinecraftStudio/Documents/ConvertDialog.cs: Converts a world to another edition, platform or game version:
// source, target, output, options, progress and result.
// Owns: ConvertDialog.
using System.Diagnostics;
using System.IO.Compression;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>Converts a world to another edition, platform or game version: source, target, output, options, progress and result.</summary>
sealed partial class ConvertDialog : DarkDialog
{
    // Layout in 96-dpi units (the dialog scales everything for the screen).
    const int DialogWidth = 800, LabelWidth = 104, RowGap = 8;

    sealed record EditionInfo(string Title, string Caption, string Icon, Func<ConversionTarget, bool> Matches);

    static readonly EditionInfo[] AllEditions =
    [
        new("Java Edition", "PC, Mac and Linux, Classic to 26.x", "java", t => t.Kind == TargetKind.Java),
        new("Bedrock Edition", "Windows, phones, tablets, consoles", "bedrock", t => t.Kind == TargetKind.Bedrock && t.BedrockTerrain != BedrockTerrain.LegacyTerrain),
        new("Legacy Console", "Xbox, PlayStation, Wii U, Switch", "console", t => t.Kind == TargetKind.LegacyConsole),
        new("Pocket Edition", "0.2 – 0.16 and Pi Edition", "pocket", t => t.Kind == TargetKind.PocketLegacy || t.BedrockTerrain == BedrockTerrain.LegacyTerrain && t.Kind == TargetKind.Bedrock),
        new("Classic & Indev", "Finite worlds, 2009 – 2010", "classic", t => t.Kind is TargetKind.Classic or TargetKind.Indev),
    ];

    static readonly (string Text, int Chunks, bool NewGenOnly)[] ConsoleSizes =
    [
        ("Classic — 54 × 54 chunks (864 × 864 blocks)", 54, false),
        ("Small — 64 × 64 chunks (1,024 × 1,024 blocks)", 64, true),
        ("Medium — 192 × 192 chunks (3,072 × 3,072 blocks)", 192, true),
        ("Large — 320 × 320 chunks (5,120 × 5,120 blocks)", 320, true),
    ];

    readonly IReadOnlyList<SaveWorld> _openWorlds;
    readonly IReadOnlyList<FoundWorld> _found;
    /// <summary>What the rescue found, for the report, when the source is a world opened for rescue.</summary>
    readonly IReadOnlyList<string> _rescueFindings;
    readonly Func<string, Task<SaveWorld?>> _loadWorld;
    readonly ImageList _icons = Icons.Create(64);
    readonly EditionInfo[] _editions;
    readonly List<ChoiceTile> _tiles = [];
    readonly PictureBox _sourceIcon;
    readonly Label _sourceName, _sourceFormat, _sourceStats;
    readonly Button _change;
    readonly DarkComboBox _version, _consoleSize;
    readonly Label _versionInfo, _folderHint, _status, _radiusLabel;
    readonly DarkTextField _name, _folder, _template, _radius;
    readonly Button _browse, _browseTemplate, _rulesButton;
    readonly Label _rulesInfo;
    List<ReplaceRule> _blockRules = [];
    readonly FlowLayoutPanel _dimRow;
    readonly DarkCheckBox _entities, _blockEntities, _players, _extend, _mcworld, _limit, _depth;
    readonly Panel _javaBedrockOptions, _consoleOptions, _templateRow;
    readonly SlimProgress _bar;
    readonly TextBox _notes;
    readonly Button _convert, _close, _open, _explore, _reportButton;
    readonly List<Control> _inputs = [];
    SaveWorld? _source;
    EditionInfo? _edition;
    CancellationTokenSource? _cts;
    ConversionReport? _report;
    ConversionTarget? _reportTarget;
    string? _reportOutput, _mcworldPath;
    bool _folderEdited, _nameEdited, _settingText, _running, _closeWhenDone;

    /// <summary>Path to open in the studio after the dialog closes ("Open converted world").</summary>
    public string? OpenAfterClose { get; private set; }
    /// <summary>The target chosen last (remembered for next time).</summary>
    public string? LastTarget { get; private set; }

    public ConvertDialog(SaveWorld? source, IReadOnlyList<SaveWorld> openWorlds, IReadOnlyList<FoundWorld> found, Func<string, Task<SaveWorld?>> loadWorld, string? lastTarget,
        IReadOnlyList<string>? rescueFindings = null)
        : base("Convert World", DialogWidth)
    {
        _rescueFindings = rescueFindings ?? [];
        _openWorlds = openWorlds;
        _found = found;
        _loadWorld = loadWorld;
        _editions = AllEditions.Where(e => TargetCatalog.All.Any(e.Matches)).ToArray();
        int content = DialogWidth - 32;
        var page = new Panel { Dock = DockStyle.Fill };
        int y = 0;

        // ---- source
        y = Header(page, "CONVERT FROM", y);
        var card = new Panel { Location = P(0, y), Size = S(content, 74), BackColor = C.BG3 };
        _sourceIcon = new PictureBox { Location = P(16, 17), Size = S(40, 40), SizeMode = PictureBoxSizeMode.Zoom };
        _sourceName = new Label { Location = P(68, 9), AutoSize = true, Font = Theme.Ui(12f, FontStyle.Bold), ForeColor = C.FG, UseMnemonic = false, MaximumSize = S(content - 200, 0), AutoEllipsis = true };
        _sourceFormat = new Label { Location = P(69, 33), AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MaximumSize = S(content - 200, 0) };
        _sourceStats = new Label { Location = P(69, 51), AutoSize = true, ForeColor = C.FG_FAINT, UseMnemonic = false, MaximumSize = S(content - 200, 0) };
        _change = Theme.Button("Change…");
        _change.MinimumSize = S(110, 30);
        _change.Location = P(content - 126, 21);
        _change.Click += (_, _) => ShowSourceMenu();
        card.Controls.AddRange([_sourceIcon, _sourceName, _sourceFormat, _sourceStats, _change]);
        page.Controls.Add(card);
        y += 74 + 14;

        // ---- target edition and version
        y = Header(page, "CONVERT TO", y);
        int gap = 8, tileW = (content - gap * (_editions.Length - 1)) / _editions.Length;
        for (int i = 0; i < _editions.Length; i++)
        {
            var ed = _editions[i];
            var tile = new ChoiceTile
            {
                Text = ed.Title, Caption = ed.Caption, Image = _icons.Images[ed.Icon], Location = P(i * (tileW + gap), y), Size = S(tileW, 100),
                Font = Theme.Ui(9.5f), TabIndex = i,
            };
            tile.Click += (_, _) => SelectEdition(ed);
            _tiles.Add(tile);
            page.Controls.Add(tile);
        }
        y += 100 + 12;
        _version = new DarkComboBox { Location = P(LabelWidth, y), Width = D(content - LabelWidth) };
        _version.SelectedIndexChanged += (_, _) => TargetChanged();
        page.Controls.Add(FieldLabel("Version", y));
        page.Controls.Add(_version);
        y += 32;
        _versionInfo = new Label { Location = P(LabelWidth, y), Size = S(content - LabelWidth, 58), ForeColor = C.FG_DIM, UseMnemonic = false };
        page.Controls.Add(_versionInfo);
        y += 58 + 4;

        // ---- output
        y = Header(page, "SAVE AS", y);
        _name = new DarkTextField { Location = P(LabelWidth, y), Size = S(content - LabelWidth, 28) };
        _name.Box.TextChanged += (_, _) => { if (!_settingText) { _nameEdited = true; if (!_folderEdited) SuggestFolder(); } };
        page.Controls.Add(FieldLabel("World name", y));
        page.Controls.Add(_name);
        y += 28 + RowGap;
        _folder = new DarkTextField { Location = P(LabelWidth, y), Size = S(content - LabelWidth - 118, 28) };
        _folder.Box.TextChanged += (_, _) => { if (!_settingText) _folderEdited = true; UpdateFolderHint(); };
        _browse = Theme.Button("Browse…");
        _browse.MinimumSize = S(110, 28);
        _browse.Location = P(content - 110, y);
        _browse.Click += (_, _) => BrowseOutput();
        page.Controls.Add(FieldLabel("Folder", y));
        page.Controls.Add(_folder);
        page.Controls.Add(_browse);
        y += 28 + 4;
        _folderHint = new Label { Location = P(LabelWidth, y), Size = S(content - LabelWidth, 20), ForeColor = C.FG_FAINT, UseMnemonic = false, AutoEllipsis = true };
        page.Controls.Add(_folderHint);
        y += 20 + 6;

        // ---- what to include
        y = Header(page, "INCLUDE", y);
        _dimRow = new FlowLayoutPanel { Location = P(0, y), Size = S(content, 28), WrapContents = false, Margin = Padding.Empty };
        page.Controls.Add(_dimRow);
        y += 28 + 2;
        var dataRow = new FlowLayoutPanel { Location = P(0, y), Size = S(content, 28), WrapContents = false };
        _entities = Check("Mobs, item frames and other entities");
        _blockEntities = Check("Chest contents, signs and other block data");
        _players = Check("Players and inventories");
        dataRow.Controls.AddRange([_entities, _blockEntities, _players]);
        page.Controls.Add(dataRow);
        y += 28 + 2;
        // Blocks changed as it converts (a mod's blocks, or blocks the target lacks): the Replace window's rules.
        var rulesRow = new FlowLayoutPanel { Location = P(0, y), Size = S(content, 32), WrapContents = false };
        _rulesButton = Theme.Button("Change blocks as it converts…");
        _rulesButton.Click += (_, _) => EditBlockRules();
        _rulesInfo = new Label { AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(8), this.Px(8), 0, 0) };
        rulesRow.Controls.AddRange([_rulesButton, _rulesInfo]);
        page.Controls.Add(rulesRow);
        y += 32 + 2;
        _blockRules = Backup.Settings.ConvertBlockRules.Select(r => r.Split('→', 2)).Where(p => p.Length == 2).Select(p => new ReplaceRule(p[0].Trim(), p[1].Trim())).ToList();
        ShowRulesInfo();

        // Edition-specific options share one area.
        _javaBedrockOptions = new Panel { Location = P(0, y), Size = S(content, 94) };
        _extend = Check("Let Minecraft add new terrain below y = 0 under chunks from before 1.18");
        _extend.Location = P(0, 0);
        _mcworld = Check("Also save a .mcworld file", true);
        _mcworld.Location = P(content - 250, 0);
        _limit = Check("Only convert chunks within", false);
        _limit.Location = P(0, 31);
        _radius = new DarkTextField("64") { Size = S(60, 26) };
        _radiusLabel = new Label { Text = "chunks of the spawn (leave off to convert everything)", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false };
        _limit.CheckedChanged += (_, _) => _radius.Enabled = _limit.Checked;
        _radius.Enabled = false;
        // 1.18+ worlds into versions 256 blocks high: keep y −64…191 instead of 0…255.
        _depth = Check("Raise the world 64 blocks to keep the deep caves below y 0", false);
        _depth.Location = P(0, 0);
        _depth.Visible = false;
        _javaBedrockOptions.Controls.AddRange([_extend, _depth, _mcworld, _limit, _radius, _radiusLabel]);
        _javaBedrockOptions.Layout += (_, _) =>
        {
            // Rows move up when the first row's options do not apply.
            _limit.Top = _extend.Visible || _mcworld.Visible || (_depth.Visible && _depth.Parent == _javaBedrockOptions) ? this.Px(31) : 0;
            // Centred on the tick box's row, never above the top of the area.
            _radius.Location = new Point(_limit.Right + this.Px(2), Math.Max(0, _limit.Top + (_limit.Height - _radius.Height) / 2));
            _radiusLabel.Location = new Point(_radius.Right + this.Px(8), Math.Max(0, _limit.Top + (_limit.Height - _radiusLabel.Height) / 2));
        };
        page.Controls.Add(_javaBedrockOptions);

        _consoleOptions = new Panel { Location = P(0, y), Size = S(content, 94), Visible = false };
        _consoleSize = new DarkComboBox { Location = P(LabelWidth, 0), Width = D(380) };
        _consoleSize.SelectedIndexChanged += (_, _) => UpdateVersionInfo();
        _consoleOptions.Controls.Add(FieldLabel("World size", 0));
        _consoleOptions.Controls.Add(_consoleSize);
        _templateRow = new Panel { Location = P(0, 32), Size = S(content, 30) };
        _template = new DarkTextField { Location = P(LabelWidth, 0), Size = S(content - LabelWidth - 118, 28) };
        _template.Box.PlaceholderText = "Optional: one of your own Xbox 360 save packages to put the world in";
        _browseTemplate = Theme.Button("Browse…");
        _browseTemplate.MinimumSize = S(110, 28);
        _browseTemplate.Location = P(content - 110, 0);
        _browseTemplate.Click += (_, _) => BrowseTemplate();
        _templateRow.Controls.AddRange([FieldLabel("Package", 0), _template, _browseTemplate]);
        _consoleOptions.Controls.Add(_templateRow);
        page.Controls.Add(_consoleOptions);
        y += 94 + 6;

        // ---- progress and result
        _bar = new SlimProgress { Location = P(0, y), Size = S(content, 6), Visible = false };
        page.Controls.Add(_bar);
        _status = new Label { Location = P(0, y + 10), Size = S(content, 22), ForeColor = C.FG_DIM, UseMnemonic = false, AutoEllipsis = true };
        page.Controls.Add(_status);
        _notes = new TextBox
        {
            Location = P(0, y + 34), Size = S(content, 132), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = C.BG2, ForeColor = C.FG_DIM, ScrollBars = ScrollBars.Vertical, Visible = false, TabStop = false, WordWrap = true,
        };
        Theme.DarkControl(_notes);
        page.Controls.Add(_notes);
        y += 166;

        // ---- buttons
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = Pad(0, 10, 0, 0) };
        _convert = DialogButton("Convert", true);
        _convert.Click += async (_, _) => await StartAsync();
        _close = DialogButton("Close", false);
        _close.Click += (_, _) => { if (_running) RequestCancel(); else Close(); };
        _open = DialogButton("Open converted world", true);
        _open.Visible = false;
        _open.Click += (_, _) => { OpenAfterClose = _report?.OpenPath ?? _reportOutput; Close(); };
        _explore = DialogButton("Show in Explorer", false);
        _explore.Visible = false;
        _explore.Click += (_, _) => { if (_reportOutput != null) Explorer.Open(_reportOutput); };
        _reportButton = DialogButton("Full report…", false);
        _reportButton.Visible = false;
        _reportButton.Click += (_, _) => Dialogs.ShowText(this, "Conversion report", ReportText());
        buttons.Controls.AddRange([_close, _convert, _open, _explore, _reportButton]);
        AcceptButton = null;
        CancelButton = null;

        _inputs.AddRange([_change, _version, _name, _folder, _browse, _dimRow, dataRow, _javaBedrockOptions, _consoleOptions]);
        _inputs.AddRange(_tiles);

        Controls.Add(page);
        Controls.Add(buttons);
        ClientSize = S(DialogWidth, y + 16 + 16 + 42);
        // A laptop screen is shorter than the window: it opens as tall as the screen and the page scrolls.
        FitScreen();
        KeyPreview = true;
        _version.AccessibleName = "Version";
        _name.Box.AccessibleName = "World name";
        _folder.Box.AccessibleName = "Folder";
        _radius.Box.AccessibleName = "Chunks around the spawn";
        _template.Box.AccessibleName = "Xbox 360 package";
        _consoleSize.AccessibleName = "World size";
        _notes.AccessibleName = "Conversion result";
        _depth.AccessibleName = "Raise the world 64 blocks";

        SetSource(source);
        ShowMessage(IdleHint, C.FG_FAINT);
        var last = TargetCatalog.All.FirstOrDefault(t => t.Name == lastTarget);
        var initial = last ?? DefaultTarget(source);
        SelectEdition(_editions.First(e => e.Matches(initial)), initial);
    }

    // ------------------------------------------------------------------ building blocks

    // Layout values are written at 96 dpi and scaled for the screen here.
    int D(int v) => this.Px(v);
    Point P(int x, int y) => new(this.Px(x), this.Px(y));
    Size S(int w, int h) => new(this.Px(w), this.Px(h));
    Padding Pad(int l, int t, int r, int b) => new(this.Px(l), this.Px(t), this.Px(r), this.Px(b));

    int Header(Control page, string text, int y)
    {
        page.Controls.Add(new SectionHeader(text) { Location = P(0, y), Size = S(DialogWidth - 32, 18) });
        return y + 18 + 8;
    }

    Label FieldLabel(string text, int y) => new() { Text = text, Location = P(0, y + 5), AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false };

    DarkCheckBox Check(string text, bool on = true) => new(text, on) { Margin = Pad(0, 0, 22, 0) };

    Button DialogButton(string text, bool primary)
    {
        var b = Theme.Button(text, primary);
        b.MinimumSize = S(100, 32);
        b.Margin = Pad(8, 0, 0, 0);
        return b;
    }

    static string IconKey(SaveWorld w) => MainForm.IconFor(w);

    /// <summary>Chunks that can be read (a rescued Java world still lists the chunks it found no readable copy of).</summary>
    internal static int Readable(WorldDimension d) => d.ChunkPositions.Count - (d is Core.Java.JavaRegionDimension j ? j.LostChunks : 0);

    ConversionTarget? Target => (_version.SelectedItem as ComboItem)?.Tag as ConversionTarget;

    static ConversionTarget DefaultTarget(SaveWorld? source) =>
        source is Core.Java.JavaWorld or Core.Legacy.FiniteWorld
            ? TargetCatalog.All.First(t => t.Kind == TargetKind.Bedrock)
            : TargetCatalog.All.First(t => t.Kind == TargetKind.Java);
}
