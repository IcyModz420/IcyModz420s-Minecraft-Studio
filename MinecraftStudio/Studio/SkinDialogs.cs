// MinecraftStudio/Studio/SkinDialogs.cs: Skins to start from: the default skins of the games installed on this
// PC (read from them, never bundled).
// Owns: GameSkins, SkinPickDialog, ExportNotes, BedrockPackDialog, JavaPackDialog.
using System.IO.Compression;
using System.Text.Json;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>Skins to start from: the default skins of the games installed on this PC (read from them, never bundled).</summary>
static class GameSkins
{
    public sealed record Entry(string Title, string Caption, Skin Skin);

    public static List<Entry> Find(List<string> notes)
    {
        var list = new List<Entry>();
        // Java: the newest installed release that has default skins in its jar.
        foreach (var (version, jar) in GameFolders.JavaJars())
        {
            try
            {
                using var zip = ZipFile.OpenRead(jar);
                var found = zip.Entries.Where(e => e.FullName.StartsWith("assets/minecraft/textures/entity/") && e.FullName.EndsWith(".png") &&
                    (e.FullName.Contains("/player/wide/") || e.FullName.Contains("/player/slim/") || e.FullName is "assets/minecraft/textures/entity/steve.png" or "assets/minecraft/textures/entity/alex.png"))
                    .OrderBy(e => e.FullName).ToList();
                if (found.Count == 0) continue;
                foreach (var e in found)
                {
                    using var s = e.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    string name = char.ToUpperInvariant(e.Name[0]) + Path.GetFileNameWithoutExtension(e.Name)[1..];
                    bool slim = e.FullName.Contains("/slim/") || !e.FullName.Contains("/player/") && e.Name == "alex.png";
                    var skin = Skin.Load(ms.ToArray(), slim ? SkinModel.Slim : SkinModel.Classic, name);
                    list.Add(new Entry(name, $"Java {version}, {(slim ? "slim" : "classic")} arms", skin));
                }
                break;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                notes.Add($"Java {version}: {ex.Message}");
            }
        }
        // Bedrock: the game's own skin pack.
        if (GameFolders.BedrockGameData() is { } data)
        {
            string pack = Path.Combine(data, "skin_packs", "vanilla");
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack, "skins.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                foreach (var s in doc.RootElement.GetProperty("skins").EnumerateArray())
                {
                    if (s.TryGetProperty("type", out var t) && t.GetString() != "free") continue;
                    string name = s.GetProperty("localization_name").GetString() ?? "Skin";
                    bool slim = (s.GetProperty("geometry").GetString() ?? "").EndsWith("Slim", StringComparison.OrdinalIgnoreCase);
                    string file = Path.Combine(pack, s.GetProperty("texture").GetString() ?? "");
                    if (!File.Exists(file)) continue;
                    list.Add(new Entry(name, $"Bedrock, {(slim ? "slim" : "classic")} arms", Skin.Load(File.ReadAllBytes(file), slim ? SkinModel.Slim : SkinModel.Classic, name)));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or KeyNotFoundException or UnauthorizedAccessException)
            {
                notes.Add("Bedrock: " + ex.Message);
            }
        }
        return list;
    }
}

/// <summary>Picks one skin out of several (the game's default skins, or the skins in a skin pack).</summary>
sealed class SkinPickDialog : DarkDialog
{
    readonly List<(ChoiceTile Tile, Skin Skin)> _tiles = [];
    Skin? _chosen;

    public SkinPickDialog(string title, string intro, IReadOnlyList<(string Title, string Caption, Skin Skin)> skins) : base(title, 760)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(this.Px(520), this.Px(360));
        ClientSize = new Size(this.Px(760), this.Px(520));
        var label = new Label { Text = intro, Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(10)) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = C.BG2 };
        Theme.DarkControl(flow);
        foreach (var (t, caption, skin) in skins)
        {
            var tile = new ChoiceTile
            {
                Text = t, Caption = caption, Image = BitmapOf(SkinExport.Icon(skin.Image)), Font = Theme.Ui(9f),
                Size = new Size(this.Px(168), this.Px(96)), Margin = new Padding(this.Px(4)),
            };
            tile.Click += (_, _) => Select(tile);
            tile.DoubleClick += (_, _) => { Select(tile); DialogResult = DialogResult.OK; };
            _tiles.Add((tile, skin));
            flow.Controls.Add(tile);
        }
        AddButtons(("Use this skin", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        Controls.Add(flow);
        Controls.Add(label);
        if (_tiles.Count > 0) Select(_tiles[0].Tile);
    }

    void Select(ChoiceTile tile)
    {
        foreach (var (t, skin) in _tiles)
        {
            t.Selected = t == tile;
            if (t == tile) _chosen = skin;
        }
    }

    public Skin? Chosen => DialogResult == DialogResult.OK ? _chosen : null;

    public static Bitmap BitmapOf(RgbaImage img)
    {
        var b = new Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                b.SetPixel(x, y, Color.FromArgb(unchecked((int)img[x, y])));
        return b;
    }
}

/// <summary>Shows what an export changes or leaves out before anything is written; the player can stop there.</summary>
static class ExportNotes
{
    public static bool Confirm(IWin32Window owner, string title, IReadOnlyList<string> notes, string go)
    {
        if (notes.Count == 0) return true;
        using var f = new DarkDialog(title, 600);
        var text = new Label
        {
            Text = "Before you save:\n\n" + string.Join("\n\n", notes.Select(n => "• " + n)), Dock = DockStyle.Top, AutoSize = true,
            MaximumSize = new Size(f.Px(568), 0), UseMnemonic = false,
        };
        f.AddButtons((go, DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        f.Controls.Add(text);
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        return f.ShowDialog(owner) == DialogResult.OK;
    }
}

/// <summary>A Bedrock skin pack's name and the skins in it (the one being edited, plus any skin files added).</summary>
sealed class BedrockPackDialog : DarkDialog
{
    readonly DarkTextField _name;
    readonly ListBox _list;
    readonly List<Skin> _skins = [];

    public BedrockPackDialog(Skin current, bool install) : base(install ? "Install into Minecraft Bedrock" : "Bedrock skin pack", 560)
    {
        _skins.Add(current);
        var form = new Panel { Dock = DockStyle.Top, AutoSize = true };
        var fb = new FormBuilder(form);
        _name = fb.Text("Pack name", current.Name);
        var intro = new Label
        {
            Text = "The pack appears in the Dressing Room under Classic Skins (Windows, Android and iOS). A pack with the same name replaces " +
                   "the one installed before, so you can update it.", Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(528), 0),
            ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(8)),
        };
        _list = new ListBox { Dock = DockStyle.Top, Height = this.Px(150), BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        Theme.DarkControl(_list);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, this.Px(4), 0, 0) };
        var add = Theme.Button("Add skin files…");
        var remove = Theme.Button("Remove");
        add.Click += (_, _) => AddFiles();
        remove.Click += (_, _) =>
        {
            if (_list.SelectedIndex > 0) { _skins.RemoveAt(_list.SelectedIndex); Refill(); }
        };
        buttons.Controls.AddRange([add, remove]);
        AddButtons((install ? "Install" : "Save pack…", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        Controls.Add(buttons);
        Controls.Add(_list);
        Controls.Add(new Label { Text = "SKINS IN THE PACK", Dock = DockStyle.Top, AutoSize = true, ForeColor = C.FG_DIM, Font = Theme.Ui(8.5f, FontStyle.Bold), Padding = new Padding(0, this.Px(8), 0, this.Px(4)) });
        Controls.Add(form);
        Controls.Add(intro);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Refill();
    }

    public string PackName => _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "My Skins";
    public IReadOnlyList<Skin> Skins => _skins;

    void Refill()
    {
        _list.Items.Clear();
        for (int i = 0; i < _skins.Count; i++)
            _list.Items.Add($"{_skins[i].Name}  ·  {(_skins[i].Model == SkinModel.Slim ? "slim" : "classic")} arms, {_skins[i].Image.Width} × {_skins[i].Image.Width}{(i == 0 ? "  (this skin)" : "")}");
    }

    void AddFiles()
    {
        using var dlg = new OpenFileDialog { Filter = "Skin images|*.png", Multiselect = true, Title = "Add skins to the pack" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var problems = new List<string>();
        foreach (var file in dlg.FileNames)
        {
            try { _skins.Add(Skin.Load(File.ReadAllBytes(file), null, Path.GetFileNameWithoutExtension(file))); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        Refill();
        if (problems.Count > 0) Dialogs.Error(this, "Some files were not added:\n\n" + string.Join("\n", problems));
    }
}

/// <summary>
/// A Java pack that replaces the default skins: its name, the releases it is for, and (when installing) which game
/// folders on this PC get it.
/// </summary>
sealed class JavaPackDialog : DarkDialog
{
    readonly DarkTextField _name;
    readonly DarkComboBox _from, _to;
    readonly Label _error;
    readonly List<(DarkCheckBox Box, string Folder)> _folders = [];
    readonly Button _ok;
    readonly Skin _skin;

    public JavaPackDialog(Skin skin, bool install) : base(install ? "Install into Minecraft Java" : "Java default-skin pack", 600)
    {
        _skin = skin;
        var intro = new Label
        {
            Text = "Java only shows a skin from your account online. This pack makes it the default skin instead, so it shows in single " +
                   "player and on LAN, also without an account: every player without a skin of their own gets it. Turn the pack on " +
                   "under Options, Resource Packs.", Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(568), 0),
            ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(8)),
        };
        var form = new Panel { Dock = DockStyle.Top, AutoSize = true };
        var fb = new FormBuilder(form);
        _name = fb.Text("Pack name", skin.Name);
        _from = fb.Row("Oldest version", new DarkComboBox());
        _to = fb.Row("Newest version", new DarkComboBox());
        foreach (var r in JavaReleases.All)
        {
            _from.Items.Add(new ComboItem(r.Label, r.FormatText, r));
            _to.Items.Add(new ComboItem(r.Label, r.FormatText, r));
        }
        // The newest release installed on this PC, or the newest one there is.
        var newest = GameFolders.JavaJars().Select(j => JavaReleases.Of(j.Version)).FirstOrDefault(r => r != null) ?? JavaReleases.Newest;
        _from.SelectedIndex = _to.SelectedIndex = newest.Index;
        _error = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = C.ORANGE, MaximumSize = new Size(this.Px(568), 0), UseMnemonic = false, Padding = new Padding(0, this.Px(4), 0, this.Px(4)) };
        var folderPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        if (install)
        {
            var games = GameFolders.Java();
            foreach (var (label, folder) in games)
            {
                var box = new DarkCheckBox($"{label}   {folder}", label is "Minecraft Launcher" or "TLauncher");
                _folders.Add((box, folder));
                folderPanel.Controls.Add(box);
            }
            if (games.Count == 0) folderPanel.Controls.Add(Theme.Label("No Java game folders were found on this PC. Use Export to save the pack instead.", C.ORANGE));
        }
        var buttons = AddButtons((install ? "Install" : "Save pack…", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        _ok = (Button)buttons.Controls.Cast<Control>().First(c => c is Button b && b.DialogResult == DialogResult.OK);
        if (install)
        {
            Controls.Add(folderPanel);
            Controls.Add(new Label { Text = "INSTALL INTO", Dock = DockStyle.Top, AutoSize = true, ForeColor = C.FG_DIM, Font = Theme.Ui(8.5f, FontStyle.Bold), Padding = new Padding(0, this.Px(8), 0, this.Px(4)) });
        }
        Controls.Add(_error);
        Controls.Add(form);
        Controls.Add(intro);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _from.SelectedIndexChanged += (_, _) => Validate_();
        _to.SelectedIndexChanged += (_, _) => Validate_();
        foreach (var (box, _) in _folders) box.CheckedChanged += (_, _) => Validate_();
        Validate_();
    }

    public string PackName => _name.Text.Trim().Length > 0 ? _name.Text.Trim() : _skin.Name;
    public JavaRelease From => (JavaRelease)((ComboItem)_from.SelectedItem!).Tag!;
    public JavaRelease To => (JavaRelease)((ComboItem)_to.SelectedItem!).Tag!;
    public List<string> Folders => _folders.Where(f => f.Box.Checked).Select(f => f.Folder).ToList();
    public bool TexturePack => From.Kind != JavaPackKind.ResourcePack;

    void Validate_()
    {
        string? problem = null;
        var (from, to) = From.Index <= To.Index ? (From, To) : (To, From);
        if ((from.Kind != JavaPackKind.ResourcePack) != (to.Kind != JavaPackKind.ResourcePack))
            problem = "Java 1.5.2 and older use texture packs and 1.6.1 and newer use resource packs: choose versions on one side, and make a second pack for the other.";
        else if (!from.ModernSkins && to.ModernSkins)
            problem = "Java 1.7.10 and older read 64 × 32 skins and 1.8 and newer 64 × 64 ones from the same file: choose versions on one side, and make a second pack for the other.";
        else if (_folders.Count > 0 && !_folders.Any(f => f.Box.Checked))
            problem = "Choose at least one game folder.";
        _error.Text = problem ?? (from.Index != to.Index && from.Kind == JavaPackKind.ResourcePack && from.Major < 18
            ? "Versions before 1.20.2 only read one pack format, so some of them will list the pack as made for another version. It still works when you turn it on."
            : "");
        _error.ForeColor = problem != null ? C.ORANGE : C.FG_DIM;
        _ok.Enabled = problem == null;
    }
}
