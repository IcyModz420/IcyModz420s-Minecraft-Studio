// MinecraftStudio/Documents/PacksOverviewDialog.cs: which packs every world on this PC has.
// Owns: PacksOverviewDialog — every Java Edition (PC) and Bedrock Edition (Windows PC) world the program found, with the
// packs each has turned on and how many are off or missing (read straight from each world folder, without opening the
// worlds). Choosing a world opens it on its Packs section, where packs are installed, switched and uninstalled.
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class PacksOverviewDialog : DarkDialog
{
    readonly ListView _list;
    readonly Label _status;
    readonly CancellationTokenSource _cts = new();

    /// <summary>The world the person chose to open (on its Packs section), or null.</summary>
    public string? OpenWorld { get; private set; }

    /// <param name="installPack">A pack about to be installed: the dialog asks which world it goes into.</param>
    public PacksOverviewDialog(IReadOnlyList<FoundWorld> worlds, string? installPack = null) : base(installPack == null ? "Packs on this PC" : "Into which world?", 900)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(this.Px(640), this.Px(420));
        ClientSize = new Size(this.Px(900), this.Px(560));
        var intro = new Label
        {
            Text = installPack != null
                ? $"Choose the world to put {Path.GetFileName(installPack)} into (double-click it). The world opens on its Packs section and asks before installing; the world is backed up first."
                : "Every Java Edition (PC) and Bedrock Edition (Windows PC) world on this PC, and the packs it has. Double-click a world to open its Packs section, where packs are installed, switched on and off, and uninstalled.",
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(8)),
        };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, BackColor = C.BG, ForeColor = C.FG,
            BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable, AccessibleName = "Worlds on this PC and their packs",
        };
        _list.Columns.Add("World", this.Px(220));
        _list.Columns.Add("Edition", this.Px(190));
        _list.Columns.Add("Packs turned on", this.Px(300));
        _list.Columns.Add("Off or missing", this.Px(150));
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.DoubleClick += (_, _) => OpenSelected();
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        var open = Theme.Button(installPack == null ? "Open its packs" : "Put it in this world", true);
        open.Click += (_, _) => OpenSelected();
        buttons.Controls.Add(open);
        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(intro);
        Shown += async (_, _) => await FillAsync(worlds);
        FormClosed += (_, _) => _cts.Cancel();
    }

    async Task FillAsync(IReadOnlyList<FoundWorld> worlds)
    {
        var folders = worlds.Where(w => Directory.Exists(w.Path)).ToList();
        _status.Text = $"Reading {folders.Count} world(s)…";
        int shown = 0, withPacks = 0;
        foreach (var w in folders)
        {
            if (_cts.IsCancellationRequested || IsDisposed) return;
            var read = await Task.Run(() =>
            {
                try { return WorldPacks.ReadFolder(w.Path); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
            });
            if (read is not { } r || IsDisposed) continue;
            var on = r.Packs.Where(p => p.Active && !p.BuiltIn).Select(p => p.Name).ToList();
            int offOrMissing = r.Packs.Count(p => !p.BuiltIn && (!p.Active || p.Missing));
            var item = new ListViewItem(w.Name) { Tag = w.Path, ForeColor = on.Count > 0 ? C.FG : C.FG_DIM };
            item.SubItems.Add(r.Edition);
            item.SubItems.Add(on.Count == 0 ? "—" : string.Join(", ", on));
            item.SubItems.Add(offOrMissing == 0 ? "" : $"{offOrMissing} off or missing");
            _list.Items.Add(item);
            shown++;
            if (on.Count > 0) withPacks++;
        }
        _status.Text = shown == 0 ? "No Java Edition or Bedrock worlds were found on this PC." : $"{shown} world(s) on this PC; {withPacks} with packs turned on.";
    }

    void OpenSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        OpenWorld = (string)_list.SelectedItems[0].Tag!;
        Close();
    }
}
