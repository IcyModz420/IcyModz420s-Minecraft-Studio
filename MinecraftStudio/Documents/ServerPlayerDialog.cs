// Documents/ServerPlayerDialog.cs: a player's things moved between single-player and a server (Java worlds).
// Owns: ServerPlayerDialog — pick a player of the world (names from a server's usercache.json when there is one); copy
// their things to the file a server will read: an offline-mode name (its UUID worked out here) or any UUID (online mode),
// for when a world moves to a server or a server switches modes; or make them the single-player player. Refused while
// the game has the world open; the world is backed up first; every write is read back (ServerPlayers).
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class ServerPlayerDialog : DarkDialog
{
    readonly JavaWorld _world;
    readonly Dictionary<Guid, string> _names;
    readonly DarkComboBox _player;
    readonly DarkRadioButton _byName, _byUuid;
    readonly DarkTextField _name, _uuid;
    readonly Label _uuidShown, _status;
    readonly DarkCheckBox _replace;
    readonly Button _copy, _host;
    bool _busy;

    /// <summary>Whether anything was written (the players page then reads the players again).</summary>
    public bool Changed { get; private set; }

    public ServerPlayerDialog(JavaWorld world, PlayerRecord? selected) : base("Single-player and server", 640)
    {
        _world = world;
        _names = ServerPlayers.KnownNames(world);
        bool byUuidWorld = ServerPlayers.ByUuid(world);
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Label Note(string text, Color? color = null) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = color ?? C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        };
        column.Controls.Add(Note("A server keeps each player's things — inventory, ender chest, position, health, experience — in a file named by their UUID; a single-player world keeps its player's in level.dat. When a world moves between the two, or a server switches between offline and online mode, the game looks in another file and the player arrives with nothing. Copy their things to the file the game will read."));

        _player = new DarkComboBox { Width = this.Px(420), AccessibleName = "Player" };
        foreach (var p in world.Players) _player.Items.Add(new ComboItem(Describe(p), null, p));
        int sel = selected == null ? 0 : Math.Max(0, world.Players.ToList().IndexOf(selected));
        if (_player.Items.Count > 0) _player.SelectedIndex = sel;
        column.Controls.Add(Row("Player", _player));

        column.Controls.Add(new SectionHeader("COPY TO A SERVER PLAYER") { Size = new Size(this.Px(600), this.Px(18)), Margin = new Padding(0, this.Px(12), 0, this.Px(8)) });
        _byName = new DarkRadioButton(byUuidWorld ? "Offline-mode server: the player's name" : "The player's name (this world keeps players by name: before Java 1.7.6)") { Checked = true };
        _name = new DarkTextField { Width = this.Px(220), Height = this.Px(28), AccessibleName = "Player name" };
        _name.Box.MaxLength = 16;
        _name.Box.TextChanged += (_, _) => ShowUuid();
        column.Controls.Add(_byName);
        column.Controls.Add(Row("Name", _name));
        _uuidShown = new Label { AutoSize = true, ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(this.Px(116), 0, 0, this.Px(8)) };
        column.Controls.Add(_uuidShown);
        _byUuid = new DarkRadioButton("Online-mode server (or any UUID): the player's UUID") { Enabled = byUuidWorld };
        _uuid = new DarkTextField { Width = this.Px(340), Height = this.Px(28), AccessibleName = "Player UUID", Enabled = byUuidWorld };
        _uuid.Box.PlaceholderText = "e.g. 069a79f4-44e9-4726-a5be-fca90e38aaf5";
        _uuid.Box.TextChanged += (_, _) => { if (_uuid.Text.Length > 0) _byUuid.Checked = true; };
        column.Controls.Add(_byUuid);
        column.Controls.Add(Row("UUID", _uuid));
        column.Controls.Add(Note("A player's UUID is in the server's usercache.json, or on their profile page.", C.FG_FAINT));
        _replace = new DarkCheckBox("Replace a player file that is already there (the old one is kept as .dat_old)", false) { AutoSize = true };
        column.Controls.Add(_replace);
        _copy = Theme.Button("Copy their things", true);
        _copy.MinimumSize = new Size(this.Px(170), this.Px(30));
        _copy.Click += async (_, _) => await CopyAsync();
        column.Controls.Add(_copy);

        column.Controls.Add(new SectionHeader("USE IN SINGLE-PLAYER") { Size = new Size(this.Px(600), this.Px(18)), Margin = new Padding(0, this.Px(16), 0, this.Px(8)) });
        column.Controls.Add(Note("Make the chosen player the one this world opens with in single-player: whoever plays it on this PC gets their inventory, position and the rest."));
        _host = Theme.Button("Make them the single-player player");
        _host.MinimumSize = new Size(this.Px(240), this.Px(30));
        _host.Click += async (_, _) => await MakeHostAsync();
        column.Controls.Add(_host);

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(10), 0, 0) };
        AddButtons(("Close", DialogResult.Cancel, false));
        Controls.Add(_status);
        Controls.Add(column);
        SizeToContent();
        _player.SelectedIndexChanged += (_, _) => UpdateButtons();
        ShowUuid();
        UpdateButtons();
    }

    string Describe(PlayerRecord p)
    {
        if (ServerPlayers.IsLevelDat(p)) return "The single-player player (level.dat)";
        var uuid = ServerPlayers.UuidOf(p);
        string host = ServerPlayers.Host(_world) == p ? " — the single-player player" : "";
        return (uuid is Guid g && _names.TryGetValue(g, out var name) ? $"{name} ({p.Name})" : p.Name) + host;
    }

    PlayerRecord? Selected => _player.SelectedItem is ComboItem { Tag: PlayerRecord p } ? p : null;

    void ShowUuid()
    {
        string name = _name.Text.Trim();
        _uuidShown.Text = name.Length == 0 ? "" : ServerPlayers.IsPlayerName(name)
            ? (ServerPlayers.ByUuid(_world) ? $"An offline-mode server knows {name} as {ServerPlayers.OfflineUuid(name)}" : $"Saved as players\\{name}.dat")
            : "A name has letters, digits and underscores only, up to 16.";
        if (name.Length > 0) _byName.Checked = true;
    }

    void UpdateButtons()
    {
        var p = Selected;
        _copy.Enabled = !_busy && p != null;
        _host.Enabled = !_busy && p != null && !ServerPlayers.IsLevelDat(p) && ServerPlayers.Host(_world) != p && _world.Data != null;
    }

    /// <summary>Refused while the game has the world open; backed up first; one change at a time.</summary>
    async Task<bool> Run(Func<string> change)
    {
        if (WorldUse.InUse(_world) is string busy) { Warn(busy); return false; }
        _busy = true;
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Backing up the world…";
        try
        {
            var progress = new Progress<string>(s => _status.Text = s);
            await Task.Run(() => Backup.WorldBeforeSave(_world, progress));
            string done = await Task.Run(change);
            Changed = true;
            _status.ForeColor = C.GREEN;
            _status.Text = "✓  " + done;
            // The list shows the files as they are now.
            var keep = Selected;
            _player.Items.Clear();
            foreach (var p in _world.Players) _player.Items.Add(new ComboItem(Describe(p), null, p));
            int i = keep == null ? 0 : Math.Max(0, _world.Players.ToList().IndexOf(keep));
            if (_player.Items.Count > 0) _player.SelectedIndex = i;
            return true;
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)) Log.Error("Moving a player failed", ex);
            Warn(ex.Message);
            return false;
        }
        finally { _busy = false; UpdateButtons(); }
    }

    async Task CopyAsync()
    {
        if (Selected is not PlayerRecord from) return;
        Guid uuid;
        string? name = null;
        if (_byName.Checked)
        {
            name = _name.Text.Trim();
            if (!ServerPlayers.IsPlayerName(name)) { Warn("Type the player's name: letters, digits and underscores, up to 16."); return; }
            uuid = ServerPlayers.OfflineUuid(name);
        }
        else if (ServerPlayers.ParseUuid(_uuid.Text) is Guid typed) uuid = typed;
        else { Warn("That is not a UUID (32 hexadecimal digits, with or without dashes)."); return; }
        bool replace = _replace.Checked;
        string who = name ?? uuid.ToString();
        await Run(() =>
        {
            string path = ServerPlayers.CopyTo(_world, from, uuid, name, replace);
            return $"Copied to {Path.GetRelativePath(_world.Folder, path)}: {who} has these things when they join{(name != null && ServerPlayers.ByUuid(_world) ? " an offline-mode server" : "")}.";
        });
    }

    async Task MakeHostAsync()
    {
        if (Selected is not PlayerRecord p) return;
        if (Dialogs.Ask(this, $"Make {Describe(p)} the single-player player?\n\nWhoever opens this world in single-player gets their inventory and position. The world is backed up first, and the old level.dat is kept as level.dat_old.") != DialogResult.Yes) return;
        await Run(() =>
        {
            ServerPlayers.MakeHost(_world, p);
            return $"{Describe(p).Replace(" — the single-player player", "")} is now the single-player player.";
        });
    }

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
        if (_busy) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }
}
