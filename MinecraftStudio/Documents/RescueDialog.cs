// MinecraftStudio/Documents/RescueDialog.cs: Rescues a damaged world: reads whatever can still be read (the
// damaged world itself is never changed), shows what was damaged and what was used instead, and saves it as a
// repaired copy in the world's own format (Java and Bedrock: byte for byte, nothing converted), or hands it to
// the converter for any edition and version.
// Owns: RescueDialog.
using System.Diagnostics;
using System.IO.Compression;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>
/// Rescues a damaged world: reads whatever can still be read (the damaged world itself is never changed), shows what was
/// damaged and what was used instead, and saves it as a repaired copy in the world's own format (Java and Bedrock: byte
/// for byte, nothing converted), or hands it to the converter for any edition and version.
/// </summary>
sealed partial class RescueDialog : DarkDialog
{
    // Layout in 96-dpi units (the dialog scales everything for the screen).
    const int DialogWidth = 800, LabelWidth = 104;

    readonly string _path;
    readonly List<string> _findings = [];
    readonly ImageList _icons = Icons.Create(64);
    readonly PictureBox _icon;
    readonly Label _name, _format, _stats, _intro, _folderHint, _status;
    readonly SectionHeader _foundHeader;
    readonly TextBox _text;
    readonly DarkTextField _worldName, _folder;
    readonly DarkCheckBox _mcworld;
    readonly SlimProgress _bar;
    readonly Button _browse, _repair, _convert, _close, _open, _explore, _reportButton;
    readonly List<Control> _repairInputs = [];
    SaveWorld? _world;
    CancellationTokenSource? _cts;
    ConversionReport? _report;
    string? _output, _mcworldPath;
    bool _opening, _running, _closeWhenDone, _folderEdited, _settingText;

    /// <summary>The repaired copy to open in the studio after the dialog closes.</summary>
    public string? OpenAfterClose { get; private set; }
    /// <summary>"Convert…" was chosen: the caller takes the rescued world (<see cref="TakeWorld"/>) to the converter.</summary>
    public bool ConvertRequested { get; private set; }
    public IReadOnlyList<string> Findings => _findings;

    static string RescuedFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Rescued");

    public RescueDialog(string path) : base("Rescue a Damaged World", DialogWidth)
    {
        _path = path;
        int content = DialogWidth - 32;
        var page = new Panel { Dock = DockStyle.Fill };
        int y = 0;

        // ---- the damaged world
        y = Header(page, "DAMAGED WORLD", y);
        var card = new Panel { Location = P(0, y), Size = S(content, 74), BackColor = C.BG3 };
        _icon = new PictureBox { Location = P(16, 17), Size = S(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Image = _icons.Images["folder"] };
        _name = new Label { Location = P(68, 9), AutoSize = true, Font = Theme.Ui(12f, FontStyle.Bold), ForeColor = C.FG, UseMnemonic = false, MaximumSize = S(content - 84, 0), AutoEllipsis = true, Text = Path.GetFileName(path.TrimEnd('\\')) };
        _format = new Label { Location = P(69, 33), AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MaximumSize = S(content - 84, 0), Text = "Reading what can still be read…" };
        _stats = new Label { Location = P(69, 51), AutoSize = true, ForeColor = C.FG_FAINT, UseMnemonic = false, MaximumSize = S(content - 84, 0), AutoEllipsis = true, Text = path };
        card.Controls.AddRange([_icon, _name, _format, _stats]);
        page.Controls.Add(card);
        y += 74 + 14;

        // ---- what was found (and later the result)
        _foundHeader = new SectionHeader("WHAT THE RESCUE FOUND") { Location = P(0, y), Size = S(content, 18) };
        page.Controls.Add(_foundHeader);
        y += 18 + 8;
        _text = new TextBox
        {
            Location = P(0, y), Size = S(content, 150), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = C.BG3,
            ForeColor = C.FG, ScrollBars = ScrollBars.Vertical, WordWrap = true, AccessibleName = "What the rescue found",
        };
        Theme.DarkControl(_text);
        page.Controls.Add(_text);
        y += 150 + 14;

        // ---- saving what could be read
        y = Header(page, "SAVE WHAT COULD BE READ", y);
        _intro = new Label { Location = P(0, y), Size = S(content, 38), ForeColor = C.FG_DIM, UseMnemonic = false };
        page.Controls.Add(_intro);
        y += 38 + 4;
        _worldName = new DarkTextField { Location = P(LabelWidth, y), Size = S(content - LabelWidth, 28) };
        _worldName.Box.AccessibleName = "World name";
        _worldName.Box.TextChanged += (_, _) => { if (!_settingText && !_folderEdited) SuggestFolder(); };
        page.Controls.Add(FieldLabel("World name", y));
        page.Controls.Add(_worldName);
        y += 28 + 8;
        _folder = new DarkTextField { Location = P(LabelWidth, y), Size = S(content - LabelWidth - 118, 28) };
        _folder.Box.AccessibleName = "Folder";
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
        y += 20 + 4;
        _mcworld = new DarkCheckBox("Also save a .mcworld file (to open on a phone, tablet or console)", true) { Location = P(LabelWidth - 3, y), Visible = false };
        page.Controls.Add(_mcworld);
        y += 28 + 6;
        _repairInputs.AddRange([_worldName, _folder, _browse, _mcworld]);

        // ---- progress
        _bar = new SlimProgress { Location = P(0, y), Size = S(content, 6), Visible = false };
        page.Controls.Add(_bar);
        _status = new Label { Location = P(0, y + 10), Size = S(content, 22), ForeColor = C.FG_DIM, UseMnemonic = false, AutoEllipsis = true };
        page.Controls.Add(_status);
        y += 34;

        // ---- buttons
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = Pad(0, 10, 0, 0) };
        _close = DialogButton("Close", false);
        _close.Click += (_, _) => { if (_running || _opening) RequestCancel(); else Close(); };
        _repair = DialogButton("Save repaired copy", true);
        _repair.Click += async (_, _) => await RepairAsync();
        _convert = DialogButton("Convert…", false);
        _convert.Click += (_, _) => { ConvertRequested = true; Close(); };
        _open = DialogButton("Open the copy", true);
        _open.Visible = false;
        _open.Click += (_, _) => { OpenAfterClose = _output; Close(); };
        _explore = DialogButton("Show in Explorer", false);
        _explore.Visible = false;
        _explore.Click += (_, _) => { if (_output != null) Explorer.Open(_output); };
        _reportButton = DialogButton("Full report…", false);
        _reportButton.Visible = false;
        _reportButton.Click += (_, _) => Dialogs.ShowText(this, "Rescue report", ReportText());
        buttons.Controls.AddRange([_close, _repair, _convert, _open, _explore, _reportButton]);
        AcceptButton = null;
        CancelButton = null;

        Controls.Add(page);
        Controls.Add(buttons);
        ClientSize = S(DialogWidth, y + 16 + 16 + 46);
        KeyPreview = true;
        UpdateState();
        Shown += async (_, _) => await OpenAsync();
    }

    // ------------------------------------------------------------------ building blocks

    // Layout values are written at 96 dpi and scaled for the screen here.
    Point P(int x, int y) => new(this.Px(x), this.Px(y));
    Size S(int w, int h) => new(this.Px(w), this.Px(h));
    Padding Pad(int l, int t, int r, int b) => new(this.Px(l), this.Px(t), this.Px(r), this.Px(b));

    int Header(Control page, string text, int y)
    {
        page.Controls.Add(new SectionHeader(text) { Location = P(0, y), Size = S(DialogWidth - 32, 18) });
        return y + 18 + 8;
    }

    Label FieldLabel(string text, int y) => new() { Text = text, Location = P(0, y + 5), AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false };

    Button DialogButton(string text, bool primary)
    {
        var b = Theme.Button(text, primary);
        b.MinimumSize = S(100, 32);
        b.Margin = Pad(8, 0, 0, 0);
        return b;
    }

    void ShowMessage(string text, Color color)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }

    /// <summary>Hands the rescued world to the caller (for the converter); the dialog no longer disposes it.</summary>
    public SaveWorld? TakeWorld()
    {
        var w = _world;
        _world = null;
        return w;
    }

    void UpdateState()
    {
        bool ready = _world != null && !_opening && !_running;
        bool canRepair = _world != null && WorldRepair.CanRepair(_world);
        foreach (var c in _repairInputs) c.Enabled = ready && canRepair;
        _repair.Visible = _world == null || canRepair;
        _repair.Enabled = ready && canRepair;
        _convert.Enabled = ready;
        _mcworld.Visible = _world is BedrockWorld;
        _close.Text = _opening || _running ? "Stop" : "Close";
    }

    // ------------------------------------------------------------------ closing

    void RequestCancel()
    {
        if (_cts == null) return;
        ShowMessage("Stopping…", C.YELLOW);
        _cts.Cancel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            if (Dialogs.Ask(this, "Stop writing the repaired copy? The unfinished copy is removed.") == DialogResult.Yes)
            {
                _closeWhenDone = true;
                RequestCancel();
            }
            return;
        }
        // Closing while the world is read stops the reading; whatever it returns is let go.
        if (_opening) _cts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (_running) RequestCancel(); else Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _world?.Dispose();
            _world = null;
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }
}
