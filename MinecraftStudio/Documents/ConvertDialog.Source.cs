// MinecraftStudio/Documents/ConvertDialog.Source.cs: source for ConvertDialog.
// Part of ConvertDialog (main part in ConvertDialog.cs).
using System.Diagnostics;
using System.IO.Compression;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

partial class ConvertDialog
{
    // ------------------------------------------------------------------ source

    void SetSource(SaveWorld? world)
    {
        _source = world;
        if (world == null)
        {
            _sourceIcon.Image = _icons.Images["folder"];
            _sourceName.Text = "Choose a world to convert";
            _sourceFormat.Text = "Any edition: Java, Bedrock, Pocket Edition, 3DS or a console save.";
            _sourceStats.Text = "";
            _change.Text = "Choose…";
        }
        else
        {
            _sourceIcon.Image = _icons.Images[IconKey(world)];
            _sourceName.Text = world.Name;
            _sourceFormat.Text = world.FormatDescription + (world.OpenedForRescue ? "  ·  rescued: only what could be read" : "");
            var dims = world.Dimensions.Select(d => $"{d.DisplayName} {Readable(d):N0}").ToList();
            _sourceStats.Text = (dims.Count > 0 ? string.Join("  ·  ", dims) + " chunks" : "No chunks") +
                                (world.Players.Count > 0 ? $"  ·  {world.Players.Count} player{(world.Players.Count == 1 ? "" : "s")}" : "");
            _change.Text = "Change…";
        }
        foreach (var old in _dimRow.Controls.Cast<Control>().ToList()) old.Dispose();
        if (world == null)
            _dimRow.Controls.Add(new Label { Text = "The dimensions to convert appear here once you choose a world.", AutoSize = true, ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = Pad(0, 5, 0, 0) });
        else
            foreach (var d in world.Dimensions)
            {
                var c = Check($"{d.DisplayName}  ({Readable(d):N0})");
                c.Tag = CanonicalReader.DimensionId(d);
                _dimRow.Controls.Add(c);
            }
        if (!_nameEdited)
        {
            _settingText = true;
            _name.Text = world == null ? "" : world.OpenedForRescue ? $"{world.Name} (rescued)" : world.Name;
            _settingText = false;
        }
        UpdateDimensions();
        UpdateDepthOption();
        if (!_folderEdited) SuggestFolder();
        UpdateState();
    }

    /// <summary>Whether the source has blocks below y 0 (Java 1.18+, Bedrock 1.18+).</summary>
    static bool SourceHasDepth(SaveWorld? w) => w switch
    {
        Core.Java.JavaWorld j => j.DataVersion >= 2825,
        Core.Bedrock.BedrockWorld b => b.LevelRoot.GetList("lastOpenedWithVersion") is { Count: >= 2 } v && (v[0].AsLong > 1 || v[0].AsLong == 1 && v[1].AsLong >= 18),
        _ => false,
    };

    /// <summary>The depth option shows for a 1.18+ source going to a version 256 blocks high (Java, Bedrock or console).</summary>
    void UpdateDepthOption()
    {
        var t = Target;
        bool show = t != null && SourceHasDepth(_source) && VerticalShift.Applies(t);
        var host = t?.Kind == TargetKind.LegacyConsole ? _consoleOptions : _javaBedrockOptions;
        if (_depth.Parent != host)
        {
            _depth.Parent?.Controls.Remove(_depth);
            host.Controls.Add(_depth);
        }
        _depth.Location = host == _consoleOptions ? P(0, _templateRow.Visible ? 64 : 32) : P(0, 0);
        _depth.Visible = show;
        _javaBedrockOptions.PerformLayout();
    }

    void ShowSourceMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ImageScalingSize = new Size(this.Px(16), this.Px(16)) };
        var small = Icons.Create(this.Px(16));
        menu.Disposed += (_, _) => small.Dispose();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        ToolStripMenuItem Entry(string text, Image? image, Func<Task> click, string? tip = null) =>
            new(text, image, async (_, _) => await click()) { ForeColor = C.FG, ToolTipText = tip };
        if (_openWorlds.Count > 0)
        {
            menu.Items.Add(new ToolStripLabel("OPEN WORLDS") { ForeColor = C.FG_FAINT, Font = Theme.Ui(8f, FontStyle.Bold) });
            foreach (var w in _openWorlds)
            {
                var world = w;
                menu.Items.Add(Entry(w.Name, small.Images[IconKey(w)], () => { SetSource(world); return Task.CompletedTask; }, w.Location));
            }
            menu.Items.Add(new ToolStripSeparator());
        }
        foreach (var g in _found.GroupBy(f => f.Group))
        {
            var group = new ToolStripMenuItem(g.Key, small.Images["folder"]) { ForeColor = C.FG };
            group.DropDown.BackColor = C.BG2;
            ((ToolStripDropDownMenu)group.DropDown).ImageScalingSize = menu.ImageScalingSize;
            foreach (var f in g.OrderByDescending(f => f.LastWrite))
            {
                string path = f.Path;
                group.DropDownItems.Add(Entry(f.Name, small.Images[MainForm.IconFor(f.Kind)], () => LoadSource(path), $"{f.Path}\n{f.Detail ?? f.Kind.ToString()}"));
            }
            menu.Items.Add(group);
        }
        if (_found.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Entry("Browse for a world folder…", Theme.Glyph(Glyphs.Folder, C.FG_DIM, this.Px(16)), BrowseSourceFolder));
        menu.Items.Add(Entry("Browse for a save file…", Theme.Glyph(Glyphs.Open, C.FG_DIM, this.Px(16)), BrowseSourceFile));
        menu.Show(_change, new Point(0, _change.Height));
    }

    async Task LoadSource(string path)
    {
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Opening " + Path.GetFileName(path) + "…";
        UseWaitCursor = true;
        try
        {
            var w = await _loadWorld(path);
            if (w != null) SetSource(w);
        }
        finally
        {
            UseWaitCursor = false;
            ShowMessage(IdleHint, C.FG_FAINT);
        }
    }

    async Task BrowseSourceFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose the world folder to convert (the folder with level.dat, db, region or chunks.dat)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) await LoadSource(dlg.SelectedPath);
    }

    async Task BrowseSourceFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a save file to convert",
            Filter = "Minecraft saves|*.dat;*.bin;*.mcs;*.mclevel;*.mine;GAMEDATA*;savegame*|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) await LoadSource(dlg.FileName);
    }
}
