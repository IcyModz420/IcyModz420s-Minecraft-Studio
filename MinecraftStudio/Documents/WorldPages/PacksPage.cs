// MinecraftStudio/Documents/WorldPages/PacksPage.cs: a world's Packs section — which packs a PC world has, and
// installing, switching on and off, ordering and uninstalling them.
// Owns: PacksPage. Java Edition (PC): data packs and the world resource pack; Bedrock Edition (Windows PC): behavior and
// resource packs (all through Core's WorldPacks). Every change backs the world up first (once a session) and is saved
// at once; uninstalled packs move to the backups folder's "Removed packs", never deleted.
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents.WorldPages;

sealed class PacksPage : WorldPage
{
    const int Width0 = 900;
    readonly ListView _list;
    readonly Label _status, _detail;
    readonly Button _install, _installFolder, _toggle, _up, _down, _uninstall, _check, _show;
    List<WorldPack> _packs = [];
    bool _busy;

    public PacksPage(WorldDocument owner) : base(owner)
    {
        var column = Column();
        string? edition = WorldPacks.EditionLabel(World);
        column.Controls.Add(Note("Packs", Width0, C.FG, 16f, FontStyle.Bold));
        column.Controls.Add(Note(edition != null ? $"{World.Name}  ·  {edition}" : World.Name, Width0, C.FG_DIM, 10.5f));
        column.Controls.Add(Note(World is JavaWorld
            ? "Data packs sit in the world's datapacks folder and are switched on and off in level.dat; the world resource pack is the world's resources.zip, offered when the world is opened. The top of the list is applied on top."
            : "The behavior and resource packs this world uses. The top of the list is applied on top. A pack installed in the game itself is shared with other worlds: here it is only switched for this world. Behavior packs turn off achievements for the world.",
            Width0));
        column.Controls.Add(Note("Changes are saved straight away (the world is backed up first). Uninstalled packs are moved to the backups folder, never deleted.", Width0, C.FG_FAINT, 9f));

        var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = S(Width0 + 10, 0), Margin = new Padding(0, this.Px(6), 0, this.Px(6)) };
        Button Btn(string text, Func<Task> click, bool primary = false)
        {
            var b = Theme.Button(text, primary);
            b.Margin = new Padding(0, 0, this.Px(8), this.Px(6));
            b.Click += async (_, _) => await click();
            bar.Controls.Add(b);
            return b;
        }
        _install = Btn("Install a pack…", InstallFile, primary: true);
        _installFolder = Btn("Install a pack folder…", InstallFolder);
        _toggle = Btn("Turn off", Toggle);
        _up = Btn("Higher priority", () => MovePack(+1));
        _down = Btn("Lower priority", () => MovePack(-1));
        _uninstall = Btn("Uninstall…", Uninstall);
        _check = Btn("Check…", CheckSelected);
        _show = Btn("Show in Explorer", ShowInExplorer);
        column.Controls.Add(bar);

        _list = new ListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BackColor = C.BG2, ForeColor = C.FG,
            BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable, Size = S(Width0, 330), Font = Theme.Ui(9.5f),
            AccessibleName = "The world's packs",
        };
        _list.Columns.Add("Pack", this.Px(250));
        _list.Columns.Add("Type", this.Px(140));
        _list.Columns.Add("State", this.Px(80));
        _list.Columns.Add("Version", this.Px(70));
        _list.Columns.Add("Where", this.Px(350));
        Theme.DarkList(_list);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += async (_, _) => await Toggle();
        column.Controls.Add(_list);

        _detail = Note("", Width0, C.FG_DIM);
        _status = Note("", Width0, C.FG_DIM);
        column.Controls.Add(_detail);
        column.Controls.Add(_status);

        if (WorldPacks.WhyNot(World) is string why)
        {
            bar.Visible = _list.Visible = false;
            Status(why, C.ORANGE);
        }
    }

    public override void OnShown() => Fill();

    WorldPack? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as WorldPack : null;
    string RemovedFolder => Path.Combine(Backup.Settings.BackupFolder, "Removed packs", SafeName.For(World.Name, "World"));

    void Fill()
    {
        if (WorldPacks.WhyNot(World) != null) return;
        string? keep = Selected?.Id;
        try { _packs = WorldPacks.Read(World); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { _packs = []; Status(ex.Message, C.ORANGE); }
        // Top of the list = applied on top: Java applies its enabled data packs last-first, Bedrock first-first.
        IEnumerable<WorldPack> ordered = World is JavaWorld
            ? _packs.Where(p => p.Kind == WorldPackKind.DataPack && p.Active).Reverse().Concat(_packs.Where(p => !(p.Kind == WorldPackKind.DataPack && p.Active)))
            : _packs;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in ordered)
        {
            var item = new ListViewItem(p.Name) { Tag = p, ForeColor = p.Missing ? C.ORANGE : p.Active ? C.FG : C.FG_DIM };
            item.SubItems.Add(p.KindText);
            item.SubItems.Add(p.Missing ? "Missing" : p.Active ? "On" : "Off");
            item.SubItems.Add(p.Version);
            item.SubItems.Add(p.Where);
            _list.Items.Add(item);
            if (p.Id == keep) item.Selected = true;
        }
        _list.EndUpdate();
        if (_list.Items.Count == 0) Status("This world has no packs yet. Install one with the button above.", C.FG_DIM);
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var p = Selected;
        foreach (var b in new[] { _install, _installFolder }) b.Enabled = !_busy;
        _toggle.Text = p is { Active: true } ? "Turn off" : "Turn on";
        _toggle.Enabled = !_busy && p != null && p.Kind != WorldPackKind.WorldResourcePack && !p.Missing && !(p.Active && p.Id == "vanilla");
        _up.Enabled = _down.Enabled = !_busy && p is { Active: true, Kind: not WorldPackKind.WorldResourcePack } && p.Id != "vanilla";
        _uninstall.Enabled = !_busy && p is { CanUninstall: true };
        _show.Enabled = !_busy && p?.Location != null;
        _check.Enabled = !_busy && p?.Location is string at && (File.Exists(at) || Directory.Exists(at));
        _detail.Text = p == null ? "" : (p.Description.Length > 0 ? p.Description + "  ·  " : "") + p.Id;
    }

    void Status(string text, Color color) { _status.ForeColor = color; _status.Text = text; }

    /// <summary>Runs a change: refused while the world is open in the game or (Java) has other unsaved changes; backs up first.</summary>
    async Task Change(string doing, Func<string> work)
    {
        if (_busy) return;
        if (World is JavaWorld && Owner.IsDirty)
        {
            Status("Save the world's other changes first (Ctrl+S): pack changes write level.dat straight away and should not take unsaved changes with them.", C.ORANGE);
            return;
        }
        _busy = true;
        UpdateButtons();
        Status(doing + "…", C.FG_DIM);
        try
        {
            string done = await Task.Run(() =>
            {
                Backup.WorldBeforeSave(World);
                return work();
            });
            Status("✓  " + done, C.GREEN);
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or InvalidDataException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)) Log.Error("Changing a world's packs failed", ex);
            Status(ex.Message, C.ORANGE);
        }
        finally
        {
            _busy = false;
            Fill();
            Owner.ReloadFacts();
        }
    }

    async Task InstallFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Install a pack into " + World.Name,
            Filter = World is JavaWorld ? "Java packs (*.zip)|*.zip|All files|*.*" : "Bedrock packs (*.mcpack;*.mcaddon;*.zip)|*.mcpack;*.mcaddon;*.zip|All files|*.*",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        await Install(dlg.FileName);
    }

    async Task InstallFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "The pack's folder (it has a " + (World is JavaWorld ? "pack.mcmeta" : "manifest.json") + ")", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        await Install(dlg.SelectedPath);
    }

    /// <summary>A pack made elsewhere in the program (the Mob Studio) offered for this world: installed when the player agrees.</summary>
    public Task InstallOffered(string path)
    {
        if (WorldPacks.WhyNot(World) is string why) { Status(why, C.ORANGE); return Task.CompletedTask; }
        if (Dialogs.Ask(FindForm()!, $"Install {Path.GetFileName(path)} into {World.Name}?\n\nThe world is backed up first; the pack can be turned off or uninstalled here later.") != DialogResult.Yes)
            return Task.CompletedTask;
        return Install(path);
    }

    Task Install(string path) => Change("Installing " + Path.GetFileName(path.TrimEnd('\\')), () =>
    {
        var added = WorldPacks.Install(World, path, RemovedFolder);
        return "Installed and turned on: " + string.Join(", ", added.Select(p => $"{p.Name} ({p.KindText.ToLowerInvariant()})"));
    });

    Task Toggle()
    {
        if (Selected is not WorldPack p || !_toggle.Enabled) return Task.CompletedTask;
        bool on = !p.Active;
        return Change((on ? "Turning on " : "Turning off ") + p.Name, () => { WorldPacks.SetActive(World, p, on); return $"{p.Name} is {(on ? "on" : "off")}."; });
    }

    Task MovePack(int delta)
    {
        if (Selected is not WorldPack p) return Task.CompletedTask;
        return Change("Moving " + p.Name, () => { WorldPacks.Move(World, p, delta); return $"{p.Name} moved {(delta > 0 ? "up (applied more on top)" : "down")}."; });
    }

    Task Uninstall()
    {
        if (Selected is not WorldPack p) return Task.CompletedTask;
        string what = p.InWorld
            ? $"Uninstall {p.Name} from {World.Name}?\n\nIts files are moved out of the world into\n{RemovedFolder}\n(not deleted — you can put them back)."
            : $"Take {p.Name} off {World.Name}?\n\nIt is installed in the game itself, so it stays there for other worlds; this world just stops using it.";
        if (Dialogs.Ask(FindForm()!, what) != DialogResult.Yes) return Task.CompletedTask;
        return Change("Uninstalling " + p.Name, () =>
        {
            string? moved = WorldPacks.Uninstall(World, p, RemovedFolder);
            return moved != null ? $"{p.Name} uninstalled; its files are in {moved}." : $"{p.Name} is no longer used by this world.";
        });
    }

    // What the game would ignore or show wrong in the selected pack (it is only read).
    async Task CheckSelected()
    {
        if (Selected is not { Location: string where } pack) return;
        Core.Packs.PackReport? report = null;
        if (!await Main.RunAsync("Checking " + pack.Name, _ => report = Core.Packs.PackCheck.Run(where)) || report == null) return;
        Status($"{pack.Name}: {report.Problems} problem(s), {report.Warnings} warning(s).", report.Problems > 0 ? C.ORANGE : C.FG_DIM);
        Dialogs.ShowText(FindForm()!, "Pack check — " + pack.Name, report.Text());
    }

    Task ShowInExplorer()
    {
        if (Selected?.Location is string where) Explorer.Select(where);
        return Task.CompletedTask;
    }
}
