// MinecraftStudio/Documents/InventoryCopyDialog.cs: a player's things copied into a player of another world — from any
// edition or console into a Java or Bedrock world (Players page).
// Owns: InventoryCopyDialog — the world to copy into (the Java and Bedrock worlds on this PC, or a folder chosen), read off
// disk; its players; the inventory with armour and off-hand, and the ender chest; Copy: refused while that world is open in
// the game or in a tab here, asked first, a backup of it made, InventoryTransfer.Copy, saved (PlayerStore.Save), and what
// stayed behind listed.
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class InventoryCopyDialog : DarkDialog
{
    readonly SaveWorld _source;
    readonly PlayerRecord _from;
    readonly Func<string, bool> _openInTab;
    readonly DarkComboBox _world, _player;
    readonly DarkCheckBox _items, _ender;
    readonly Button _browse, _copy;
    readonly Label _status;
    SaveWorld? _target;
    int _ticket;

    /// <summary>Items were saved into another world (its folder).</summary>
    public string? Changed { get; private set; }

    public InventoryCopyDialog(SaveWorld source, PlayerRecord from, IEnumerable<FoundWorld> found, Func<string, bool> openInTab) : base("Copy items to another world", 640)
    {
        _source = source;
        _from = from;
        _openInTab = openInTab;
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new Label
        {
            Text = $"Puts {from.Name}'s things from \"{source.Name}\" ({PlatformSupport.Describe(source)}) into a player of another world — a Java, Bedrock, console or Pocket 0.x world, from any edition or console. Each item is changed into that world's own form and version; what it can't hold (a mod's item in Bedrock, an item its version doesn't have) becomes paper named after it and is listed. The other player's items are replaced; a backup of that world is made first.",
            AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _world = new DarkComboBox { Width = this.Px(380), AccessibleName = "World to copy into" };
        foreach (var w in found.Where(w => w.Kind is WorldKind.Java or WorldKind.Bedrock && !SamePlace(w.Path, source.Location)).OrderByDescending(w => w.LastWrite))
            _world.Items.Add(new ComboItem($"{w.Name}  ({(w.Kind == WorldKind.Java ? "Java" : "Bedrock")})", w.Path, w.Path));
        _browse = Theme.Button("Folder…");
        _browse.Click += async (_, _) => await BrowseAsync();
        // A console save is one file (an Xbox 360 save package or savegame.dat, a PS3 or Wii U save's file).
        var saveFile = Theme.Button("Save file…");
        saveFile.Click += (_, _) => BrowseFile();
        column.Controls.Add(Row("Into the world", _world));
        // Another world off disk: a world folder (Java, Bedrock, Pocket) or a console's save file; under the list, lined up with it.
        column.Controls.Add(Row("", _browse, saveFile));
        _player = new DarkComboBox { Width = this.Px(380), AccessibleName = "Player to copy into" };
        column.Controls.Add(Row("Into the player", _player));
        _items = new DarkCheckBox("The inventory, armour and off-hand") { AutoSize = true, Margin = new Padding(this.Px(130), this.Px(6), 0, 0) };
        _ender = new DarkCheckBox("The ender chest") { AutoSize = true, Margin = new Padding(this.Px(130), this.Px(4), 0, 0) };
        column.Controls.Add(_items);
        column.Controls.Add(_ender);
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _copy = Theme.Button("Copy items", true);
        _copy.MinimumSize = new Size(this.Px(130), this.Px(30));
        _copy.Click += async (_, _) => await CopyAsync();
        buttons.Controls.Add(_copy);
        Controls.Add(_status);
        Controls.Add(column);
        SizeToContent();
        _world.SelectedIndexChanged += async (_, _) => { if (_world.SelectedItem is ComboItem { Tag: string path }) await LoadAsync(path); };
        _copy.Enabled = false;
        if (_world.Items.Count > 0) _world.SelectedIndex = 0;
        else Say("Choose the folder of the Java or Bedrock world to copy into.");
    }

    static bool SamePlace(string a, string b)
    {
        try { return Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(124), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    void Say(string text, Color? color = null) { _status.ForeColor = color ?? C.FG_DIM; _status.Text = text; }

    async Task BrowseAsync()
    {
        using var dlg = new FolderBrowserDialog { Description = "The Java or Bedrock world folder to copy the items into", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var item = new ComboItem(Path.GetFileName(dlg.SelectedPath), dlg.SelectedPath, dlg.SelectedPath);
        _world.Items.Insert(0, item);
        _world.SelectedIndex = 0;
        await Task.CompletedTask;
    }

    void BrowseFile()
    {
        using var dlg = new OpenFileDialog { Title = "The console save to copy the items into", Filter = "Console saves|*.dat;*.bin;*.*|All files|*.*", CheckFileExists = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var item = new ComboItem(Path.GetFileName(dlg.FileName), dlg.FileName, dlg.FileName);
        _world.Items.Insert(0, item);
        _world.SelectedIndex = 0;
    }

    /// <summary>Reads the world to copy into (off disk, in the background) and lists its players.</summary>
    internal async Task LoadAsync(string path)
    {
        int ticket = ++_ticket;
        _copy.Enabled = false;
        _player.Items.Clear();
        _target?.Dispose();
        _target = null;
        if (SamePlace(path, _source.Location)) { Say("That is this world. Choose another one.", C.ORANGE); return; }
        Say("Reading the world…");
        SaveWorld? w = null;
        try { w = await Task.Run(() => WorldLoader.Open(path)); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or Core.IO.SaveFormatException or NotSupportedException)
        {
            if (ticket == _ticket) Say("That world can't be read: " + e.Message, C.ORANGE);
            return;
        }
        if (ticket != _ticket || IsDisposed) { w.Dispose(); return; }
        _target = w;
        if (InventoryTransfer.WhyNotInto(w) is string why) { Say(why, C.ORANGE); return; }
        foreach (var p in w.Players) _player.Items.Add(new ComboItem(p.Name, p.Source, p));
        if (_player.Items.Count == 0) { Say($"\"{w.Name}\" has no player yet: play it once, then copy the items in.", C.ORANGE); return; }
        _player.SelectedIndex = 0;
        _copy.Enabled = true;
        Say($"{w.FormatDescription}: {w.Players.Count} player{(w.Players.Count == 1 ? "" : "s")}.");
    }

    /// <summary>Copies into the chosen player, after the checks and a backup.</summary>
    internal async Task CopyAsync(bool ask = true)
    {
        if (_target is not SaveWorld target || _player.SelectedItem is not ComboItem { Tag: PlayerRecord to }) return;
        if (_openInTab(target.Location)) { Say($"\"{target.Name}\" is open in a tab here. Close it first, so this doesn't write behind it.", C.ORANGE); return; }
        if (WorldUse.InUse(target) is string busy) { Say(busy + " Close it in the game first.", C.ORANGE); return; }
        bool items = _items.Checked, ender = _ender.Checked;
        if (!items && !ender) { Say("Choose what to copy: the inventory, the ender chest, or both.", C.ORANGE); return; }
        string what = items && ender ? "inventory, armour, off-hand and ender chest" : items ? "inventory, armour and off-hand" : "ender chest";
        if (ask && Dialogs.Ask(this, $"Replace {to.Name}'s {what} in \"{target.Name}\" with {_from.Name}'s?\n\nA backup of \"{target.Name}\" is made first.") != DialogResult.Yes) return;
        foreach (var c in new Control[] { _world, _player, _items, _ender, _browse, _copy }) c.Enabled = false;
        Say("Backing up the world…");
        try
        {
            await Task.Run(() => MinecraftStudio.Backup.World(target));
            // The items are read and written here (a moment), then the world is saved.
            var result = InventoryTransfer.Copy(_source, _from, target, to, items, ender);
            await Task.Run(() => PlayerStore.Save(target, to));
            Changed = target.Location;
            Say($"✓  Copied {result.Items} item{(result.Items == 1 ? "" : "s")} into {to.Name} in \"{target.Name}\" (backed up first)." +
                (result.Notes.Count > 0 ? $" {result.Notes.Count} note{(result.Notes.Count == 1 ? "" : "s")}: see the list." : ""), C.GREEN);
            if (result.Notes.Count > 0 && ask) Dialogs.ShowText(this, "What stayed behind or changed", string.Join("\r\n", result.Notes.Select(n => "• " + n)));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or Core.IO.SaveFormatException)
        {
            Say("Not copied: " + e.Message, C.ORANGE);
        }
        finally
        {
            foreach (var c in new Control[] { _world, _player, _items, _ender, _browse }) c.Enabled = true;
            _copy.Enabled = _target != null && _player.Items.Count > 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _target?.Dispose();
        base.Dispose(disposing);
    }
}
