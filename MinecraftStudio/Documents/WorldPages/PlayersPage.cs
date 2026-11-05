// MinecraftStudio/Documents/WorldPages/PlayersPage.cs: Players & items: every player in the world, with an
// inventory editor laid out like the game's (armour, off-hand, storage, hotbar, ender chest).
// Owns: PlayersPage.
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents.WorldPages;

/// <summary>
/// Players & items: every player in the world, with an inventory editor laid out like the game's (armour, off-hand,
/// storage, hotbar, ender chest). Items are edited in the player's own format and saved, after a backup, where the
/// player came from.
/// </summary>
sealed partial class PlayersPage : WorldPage
{
    readonly ListView _players;
    readonly Panel _grid;
    readonly Label _gridTitle, _status, _formatNote;
    readonly Dictionary<(InventoryPart, int), SlotView> _slots = [];
    // Item editor.
    readonly Panel _editor;
    readonly Label _slotTitle;
    readonly DarkTextField _id, _name;
    readonly NumberField _count, _damage, _data;
    readonly Label _dataLabel;
    readonly ListView _enchantments;
    readonly DarkComboBox _enchantName;
    readonly NumberField _enchantLevel;
    readonly Button _save, _discard, _clear, _addEnchant, _removeEnchant, _nbt, _server, _moveTo, _progress, _copyTo;
    PlayerRecord? _player;
    PlayerInventory? _inv;
    (InventoryPart Part, int Index)? _selected;
    bool _dirty, _loading;

    public PlayersPage(WorldDocument owner) : base(owner)
    {
        // ---- players
        var left = new Panel { Dock = DockStyle.Left, Width = this.Px(240), BackColor = C.BG2, Padding = new Padding(this.Px(10)) };
        _players = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = C.BG2, ForeColor = C.FG, BorderStyle = BorderStyle.None, AccessibleName = "Players",
        };
        _players.Columns.Add("Player", this.Px(210));
        Theme.DarkList(_players);
        Theme.FillLastColumn(_players);
        _players.SelectedIndexChanged += (_, _) => { if (!_loading && _players.SelectedItems.Count > 0) SelectPlayer((PlayerRecord)_players.SelectedItems[0].Tag!); };
        var playersTitle = new SectionHeader("PLAYERS") { Dock = DockStyle.Top, Height = this.Px(26) };
        _nbt = Theme.Button("Edit as NBT…");
        _nbt.Dock = DockStyle.Bottom;
        _nbt.Click += (_, _) => { if (_player != null) Main.OpenPlayer(World, _player); };
        // Java: a player's things moved between single-player and a server (or offline and online mode).
        _server = Theme.Button("Single-player and server…");
        _server.Dock = DockStyle.Bottom;
        _server.Visible = World is JavaWorld;
        _server.Click += (_, _) => ShowServerPlayers();
        // A stuck or lost player put on safe ground (every world that keeps its players: Java, Bedrock, consoles, Pocket, Indev).
        _moveTo = Theme.Button("Move to safety…");
        _moveTo.Dock = DockStyle.Bottom;
        _moveTo.Visible = HardcoreRestore.CanMove(World);
        _moveTo.Click += (_, _) => ShowMovePlayer();
        // Java: the player's statistics and advancements (their own files in the world).
        _progress = Theme.Button("Statistics and advancements…");
        _progress.Dock = DockStyle.Bottom;
        _progress.Visible = World is JavaWorld;
        _progress.Click += (_, _) => ShowProgress();
        // Any edition's player: their things into a player of a Java or Bedrock world.
        _copyTo = Theme.Button("Copy items to another world…");
        _copyTo.Dock = DockStyle.Bottom;
        _copyTo.Click += (_, _) => ShowCopyItems();
        left.Controls.AddRange([_players, _nbt, _copyTo, _progress, _server, _moveTo, playersTitle]);

        // ---- item editor (right): a column, so fields hidden for a format take no room
        _editor = new Panel { Dock = DockStyle.Right, Width = this.Px(340), BackColor = C.BG2 };
        var column = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = C.BG2, Padding = new Padding(this.Px(14)) };
        Theme.DarkControl(column);
        _editor.Controls.Add(column);
        const int w = 300;
        T Add<T>(T c, int height, int gap = 6) where T : Control
        {
            c.Size = S(w, height);
            c.Margin = new Padding(0, 0, 0, this.Px(gap));
            column.Controls.Add(c);
            return c;
        }
        Label Caption(string text) => Add(new Label { Text = text, ForeColor = C.FG_DIM, UseMnemonic = false }, 18, 0);
        _slotTitle = Add(new Label { Text = "Choose a slot", ForeColor = C.FG, Font = Theme.Ui(11f, FontStyle.Bold), UseMnemonic = false, AutoEllipsis = true }, 26, 10);
        Caption("Item");
        _id = Add(new DarkTextField(), 28);
        _id.Box.AccessibleName = "Item";
        _id.Box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _id.Box.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _id.Box.Leave += (_, _) => Edited();
        _id.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Edited(); e.SuppressKeyPress = true; } };
        Caption("Count");
        _count = Add(new NumberField { Minimum = 1, Maximum = 127, Value = 1, AccessibleName = "Count" }, 26);
        _count.ValueChanged += (_, _) => Edited();
        Caption("Damage (durability used)");
        _damage = Add(new NumberField { Minimum = 0, Maximum = 32767, Value = 0, AccessibleName = "Damage" }, 26);
        _damage.ValueChanged += (_, _) => Edited();
        _dataLabel = Caption("Data value");
        _data = Add(new NumberField { Minimum = 0, Maximum = 32767, Value = 0, AccessibleName = "Data value" }, 26);
        _data.ValueChanged += (_, _) => Edited();
        Caption("Name (empty for the normal name)");
        _name = Add(new DarkTextField(), 28);
        _name.Box.AccessibleName = "Name";
        _name.Box.Leave += (_, _) => Edited();
        _name.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Edited(); e.SuppressKeyPress = true; } };
        Caption("Enchantments");
        _enchantments = Add(new ListView
        {
            View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.None, AccessibleName = "Enchantments",
        }, 96, 4);
        _enchantments.Columns.Add("Enchantment", this.Px(220));
        _enchantments.Columns.Add("Level", this.Px(60));
        Theme.DarkList(_enchantments);
        _enchantments.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveEnchantment(); };
        _removeEnchant = Theme.Button("Remove the selected enchantment");
        _removeEnchant.Click += (_, _) => RemoveEnchantment();
        Add(_removeEnchant, 30, 4);
        var enchantRow = Add(new Panel(), 30, 12);
        _enchantName = new DarkComboBox { Location = P(0, 0), Width = this.Px(160), AccessibleName = "Enchantment to add" };
        _enchantLevel = new NumberField { Location = P(166, 0), Size = S(52, 26), Minimum = 1, Maximum = 255, Value = 1, AccessibleName = "Level" };
        _addEnchant = Theme.Button("Add");
        _addEnchant.Location = P(224, 0);
        _addEnchant.MinimumSize = S(76, 26);
        _addEnchant.Click += (_, _) => AddEnchantment();
        enchantRow.Controls.AddRange([_enchantName, _enchantLevel, _addEnchant]);
        var slotButtons = Add(new FlowLayoutPanel { WrapContents = false }, 36, 10);
        _clear = Theme.Button("Empty slot");
        _clear.Click += (_, _) => ClearSlot();
        var fill = Theme.Button("Full stack");
        fill.Click += (_, _) => { if (_inv != null && _selected != null) _count.Value = _count.Maximum; };
        var repair = Theme.Button("Repair");
        repair.Click += (_, _) => _damage.Value = 0;
        slotButtons.Controls.AddRange([_clear, fill, repair]);
        _formatNote = Add(new Label { ForeColor = C.FG_FAINT, Font = Theme.Ui(8.5f), UseMnemonic = false }, 64);

        // ---- inventory grid (middle) and save bar
        var middle = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(this.Px(20), this.Px(14), this.Px(10), this.Px(10)) };
        Theme.DarkControl(middle);
        _gridTitle = new Label { Location = P(20, 12), AutoSize = true, ForeColor = C.FG, Font = Theme.Ui(13f, FontStyle.Bold), UseMnemonic = false };
        _grid = new Panel { Location = P(20, 48), Size = S(560, 470) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = this.Px(54), Padding = new Padding(this.Px(18), this.Px(10), 0, 0), BackColor = C.BG };
        _save = Theme.Button("Save inventory", true);
        _save.MinimumSize = S(140, 32);
        _save.Click += async (_, _) => await SaveAsync();
        _discard = Theme.Button("Discard changes");
        _discard.MinimumSize = S(140, 32);
        _discard.Click += (_, _) => { if (_player != null) { _dirty = false; SelectPlayer(_player, force: true); Changed(); } };
        _status = new Label { AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(this.Px(12), this.Px(8), 0, 0), UseMnemonic = false };
        bar.Controls.AddRange([_save, _discard, _status]);
        middle.Controls.AddRange([_gridTitle, _grid]);
        Controls.Add(middle);
        Controls.Add(_editor);
        Controls.Add(left);
        Controls.Add(bar);
        UpdateEditor();
    }

    public override bool IsDirty => _dirty;

    /// <summary>Reads the players again after something else changed them (map art given, a player moved); never over unsaved edits.</summary>
    public void Reload()
    {
        if (!_dirty && _players.Items.Count > 0) FillPlayers();
    }

    public override void OnShown()
    {
        if (_players.Items.Count == 0 || _player == null) FillPlayers();
    }

    void FillPlayers()
    {
        _loading = true;
        _players.BeginUpdate();
        _players.Items.Clear();
        // A server world's usercache.json (beside it) names the UUIDs.
        var names = World is JavaWorld j ? ServerPlayers.KnownNames(j) : [];
        foreach (var p in World.Players)
        {
            string text = World is JavaWorld && ServerPlayers.UuidOf(p) is Guid g && names.TryGetValue(g, out var name) && !ServerPlayers.IsLevelDat(p) ? $"{name}" : p.Name;
            _players.Items.Add(new ListViewItem(text) { Tag = p, ToolTipText = p.Source });
        }
        _players.EndUpdate();
        _loading = false;
        if (_players.Items.Count == 0)
        {
            _gridTitle.Text = "This world has no players yet";
            _status.Text = World.Players.Count == 0 && World is not (JavaWorld or Core.Bedrock.BedrockWorld or Core.LegacyConsole.ConsoleWorld or Core.Bedrock.PocketLegacyWorld)
                ? "Players of this kind of world cannot be read." : "A player appears once someone has played the world.";
            return;
        }
        // Keep the player that was open (players are read again after a Bedrock world is saved).
        int keep = _player == null ? 0 : Math.Max(0, World.Players.ToList().FindIndex(p => p.Source == _player.Source));
        _players.Items[keep].Selected = true;
        SelectPlayer(World.Players[keep], force: true);
    }

    void SelectPlayer(PlayerRecord player, bool force = false)
    {
        if (!force && player == _player) return;
        if (!force && _dirty)
        {
            var r = Dialogs.Ask(FindForm()!, $"Save the changes to {_player?.Name}'s inventory first?", Dialogs.AppName, MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) { ReselectCurrent(); return; }
            if (r == DialogResult.Yes) { _ = SaveThenSelect(player); return; }
            _dirty = false;
            Changed();
        }
        _player = player;
        _inv = new PlayerInventory(World, player);
        _selected = null;
        _gridTitle.Text = player.Name;
        _id.Box.AutoCompleteCustomSource = Suggestions(_inv);
        _enchantName.Items.Clear();
        foreach (var e in Items.EnchantmentNames.Where(_inv.KnowsEnchantment)) _enchantName.Items.Add(new ComboItem(Pretty(e), null, e));
        if (_enchantName.Items.Count > 0) _enchantName.SelectedIndex = 0;
        _count.Maximum = _inv.Format == ItemFormat.Components ? 99 : 127;
        _formatNote.Text = FormatNote(_inv);
        BuildGrid();
        UpdateEditor();
        UpdateButtons();
    }

    void ShowMovePlayer()
    {
        if (_player == null) return;
        if (Owner.IsDirty)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "Save the world's changes first (Ctrl+S): moving a player saves them straight away.";
            return;
        }
        using var dlg = new MovePlayerDialog(World, _player);
        dlg.ShowDialog(FindForm());
        if (dlg.Moved) FillPlayers();
    }

    void ShowProgress()
    {
        if (World is not JavaWorld java || _player == null) return;
        // Titles and what each advancement takes, from the installed Minecraft of the world's version (read when asked).
        var catalog = AdvancementCatalog.ForWorld(java);
        PlayerProgress? progress;
        try { progress = PlayerProgress.Read(java, _player, catalog); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "The statistics or advancements could not be read: " + ex.Message;
            return;
        }
        if (progress == null) { _status.ForeColor = C.ORANGE; _status.Text = "This player has no UUID in the world, so their statistics and advancements can't be found."; return; }
        using var dlg = new ProgressDialog(java, _player, progress, catalog);
        dlg.ShowDialog(FindForm());
    }

    void ShowCopyItems()
    {
        if (_player == null) return;
        if (_dirty) { _status.ForeColor = C.ORANGE; _status.Text = "Save this player's changes first: the copy takes the items as they are saved."; return; }
        using var dlg = new InventoryCopyDialog(World, _player, Main.FoundWorlds, Main.IsOpenInTab);
        dlg.ShowDialog(FindForm());
        if (dlg.Changed is string where) Main.SetStatus($"Copied {_player.Name}'s items into the world at {where}.");
    }

    void ShowServerPlayers()
    {
        if (World is not JavaWorld java) return;
        if (Owner.IsDirty)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "Save the world's changes first (Ctrl+S): moving a player writes the world's files straight away.";
            return;
        }
        using var dlg = new ServerPlayerDialog(java, _player);
        dlg.ShowDialog(FindForm());
        if (dlg.Changed) FillPlayers();
    }

    async Task SaveThenSelect(PlayerRecord next)
    {
        await SaveAsync();
        if (!_dirty) SelectPlayer(next, force: true);
    }

    void ReselectCurrent()
    {
        _loading = true;
        foreach (ListViewItem item in _players.Items) item.Selected = item.Tag == _player;
        _loading = false;
    }

    static AutoCompleteStringCollection Suggestions(PlayerInventory inv)
    {
        var c = new AutoCompleteStringCollection();
        if (inv.NumericIds)
            foreach (var (name, id) in JavaBlockData.LegacyItemIds) { c.Add(name); c.Add(id.ToString()); }
        else c.AddRange(inv.KnownIds().ToArray());
        return c;
    }

    static string FormatNote(PlayerInventory inv) => inv.Format switch
    {
        ItemFormat.Numeric => "Items are stored by number in this version: type a number or a name such as minecraft:stone. Damage is also the item's data value (wool colour, wood type …).",
        ItemFormat.Named => "Java 1.8–1.12 style: Damage is also the item's data value (wool colour, wood type …).",
        ItemFormat.Flattened => "Java 1.13–1.20.4 style.",
        ItemFormat.Components => $"Java 1.20.5+ item components (data version {inv.DataVersion}).",
        _ => "Bedrock: the data value is kept apart from durability.",
    };

    static string Pretty(string id) => (id.StartsWith("minecraft:") ? id[10..] : id).Replace('_', ' ');
}
