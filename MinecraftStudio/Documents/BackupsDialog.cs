// MinecraftStudio/Documents/BackupsDialog.cs: a world's backups — its timeline.
// Owns: BackupsDialog — every backup of the world, newest first (when, made automatically or by you, how big); "Back up
// now"; "Restore as a new world" (unpacked beside it, never over it, then opened); "Compare with this backup" (the Compare
// window, which can bring chunks back from it); "Show in Explorer". Automatic backups are switched on in Settings.
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class BackupsDialog : DarkDialog
{
    readonly SaveWorld _world;
    readonly WorldDocument? _doc;
    readonly ListView _list;
    readonly Label _status;
    readonly Button _now, _restore, _compare, _explorer;
    bool _busy;

    /// <summary>The world restored from a backup, to open once the window closes.</summary>
    public string? OpenAfterClose { get; private set; }

    public BackupsDialog(SaveWorld world, WorldDocument? doc) : base("Backups of " + world.Name, 760)
    {
        _world = world;
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ClientSize = new Size(this.Px(760), this.Px(480));
        MinimumSize = new Size(this.Px(560), this.Px(340));
        var intro = new Label
        {
            Text = $"Every backup of this world in {Backup.Settings.BackupFolder}, newest first. A backup comes back as a new world beside this one — this world is never written over — or can be compared with this world to bring back just the chunks that changed."
                + (Backup.Settings.AutoBackup ? " Automatic backups are on: the world is backed up after each play session while this program is open." : " Automatic backups (after each play session) can be switched on in Settings."),
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(8)),
        };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Backups",
        };
        foreach (var (text, width, right) in new[] { ("Made", 170, false), ("How", 190, false), ("Size", 90, true), ("File", 280, false) })
            _list.Columns.Add(text, this.Px(width), right ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += async (_, _) => await RestoreAsync();
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _now = Theme.Button("Back up now", true);
        _now.Click += async (_, _) => await BackUpNowAsync();
        _restore = Theme.Button("Restore as a new world");
        _restore.Click += async (_, _) => await RestoreAsync();
        _compare = Theme.Button("Compare with this backup");
        _compare.Click += (_, _) => CompareWithChosen();
        _explorer = Theme.Button("Show in Explorer");
        _explorer.Click += (_, _) => { if (Chosen is { } b) Explorer.Select(b.Path); };
        foreach (var b in new[] { _explorer, _compare, _restore, _now }) { b.MinimumSize = new Size(this.Px(120), this.Px(30)); buttons.Controls.Add(b); }
        Controls.Add(_list);
        Controls.Add(intro);
        Controls.Add(_status);
        Controls.Add(buttons);
        Fill();
    }

    BackupEntry? Chosen => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as BackupEntry : null;

    void Fill()
    {
        var backups = Backup.Of(_world.Name, _world.Location);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var b in backups)
            _list.Items.Add(new ListViewItem([b.Made.ToString("ddd d MMM yyyy, HH:mm"), b.Auto ? "automatically, after play" : b.ByName ? "matched by the world's name" : "by you, or before a change",
                $"{b.Size / 1048576.0:N1} MB", Path.GetFileName(b.Path)]) { Tag = b });
        _list.EndUpdate();
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        _status.ForeColor = C.FG_DIM;
        _status.Text = backups.Count == 0 ? "No backups of this world yet." : $"{backups.Count} backup{(backups.Count == 1 ? "" : "s")}, {backups.Sum(b => b.Size) / 1048576.0:N0} MB in all.";
        UpdateButtons();
    }

    void UpdateButtons()
    {
        _now.Enabled = !_busy;
        _restore.Enabled = _compare.Enabled = _explorer.Enabled = !_busy && Chosen != null;
        _compare.Enabled &= _doc != null;
    }

    async Task BackUpNowAsync()
    {
        _busy = true;
        UpdateButtons();
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            string zip = await Task.Run(() => Backup.World(_world, progress));
            Fill();
            _status.ForeColor = C.GREEN;
            _status.Text = "✓  Backed up: " + Path.GetFileName(zip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.ForeColor = C.ORANGE; _status.Text = ex.Message; }
        finally { _busy = false; UpdateButtons(); }
    }

    async Task RestoreAsync()
    {
        if (Chosen is not { } b) return;
        _busy = true;
        UpdateButtons();
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            string restored = await Task.Run(() => Backup.RestoreAsNewWorld(b, _world.Location, progress));
            _status.ForeColor = C.GREEN;
            _status.Text = "✓  Restored as a new world: " + restored;
            if (Dialogs.Ask(this, $"The backup of {b.Made:d MMM yyyy, HH:mm} is a new world now, beside this one:\n{restored}\n\nOpen it now?") == DialogResult.Yes)
            {
                OpenAfterClose = restored;
                Close();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { _status.ForeColor = C.ORANGE; _status.Text = "The backup could not be restored: " + ex.Message; }
        finally { _busy = false; if (!IsDisposed) UpdateButtons(); }
    }

    /// <summary>The Compare window, comparing this world with the chosen backup (it can then bring chunks back from it).</summary>
    void CompareWithChosen()
    {
        if (Chosen is not { } b || _doc == null) return;
        _doc.ShowCompare().CompareWith(b.Path);
        Close();
    }
}
