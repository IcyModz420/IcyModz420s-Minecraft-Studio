// MinecraftStudio/Documents/WorldPages/OverviewPage.cs: A world at a glance: what it is, whether achievements
// still count, and everything that can be done with it.
// Owns: OverviewPage.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents.WorldPages;

/// <summary>A world at a glance: what it is, whether achievements still count, and everything that can be done with it.</summary>
sealed class OverviewPage : WorldPage
{
    const int Width0 = 900;
    readonly FlowLayoutPanel _column;
    readonly ListView _facts;
    readonly Label _achievements;
    readonly ToolTip _tips = new();

    public OverviewPage(WorldDocument owner) : base(owner)
    {
        _column = Column();
        var title = Note(World.Name, Width0, C.FG, 16f, FontStyle.Bold);
        var format = Note(World.FormatDescription, Width0, C.FG_DIM, 10.5f);
        var where = Note(World.Location, Width0, C.FG_FAINT, 9f);
        _achievements = Note("", Width0, C.FG_DIM, 10f);
        _achievements.Cursor = Cursors.Hand;
        _achievements.Click += (_, _) => Owner.ShowSection(WorldSection.Cheats);
        _column.Controls.AddRange([title, format, where, _achievements]);

        // The tools in the World menu's groups (the world's pages are in the list down the side).
        FlowLayoutPanel? tiles = null;
        void Group(string heading)
        {
            _column.Controls.Add(Header(heading, Width0));
            tiles = new FlowLayoutPanel { AutoSize = true, MaximumSize = S(Width0 + 10, 0), WrapContents = true, Margin = Padding.Empty };
            _column.Controls.Add(tiles);
        }
        // A tool this world's edition or console doesn't have stays in its place, switched off, saying why (PlatformSupport).
        void Tile(string text, string caption, string glyph, Func<Task> click, string? feature = null)
        {
            string? why = feature != null ? PlatformSupport.WhyNot(feature, World, out _) : null;
            var t = new ChoiceTile
            {
                Text = text, Caption = why != null ? $"Not for {PlatformSupport.Describe(World)}" + (PlatformSupport.Get(feature!)[PlatformSupport.Of(World)!.Value].Note is string n ? ": " + n : "") : caption,
                Image = Theme.Glyph(glyph, C.FG, this.Px(30)), Size = S(212, 112), Margin = new Padding(0, 0, this.Px(10), this.Px(10)), Font = Theme.Ui(9.5f), AccessibleName = text,
                Enabled = why == null, AccessibleDescription = why ?? caption,
            };
            if (feature != null && why == null) _tips.SetToolTip(t, "Works on: " + PlatformSupport.WorksOn(PlatformSupport.Get(feature)));
            t.Click += async (_, _) => await click();
            tiles!.Controls.Add(t);
        }
        Task Do(Action action) { action(); return Task.CompletedTask; }
        Group("LOOK AROUND");
        Tile("Find", "Blocks, items in chests and shulker boxes, mobs, words on signs", Glyphs.Find, () => Do(() => Owner.ShowFind()), "find");
        Tile("Structures", "Villages, temples, shipwrecks, treasure … on the map", Glyphs.Pin, () => Do(() => Owner.ShowStructuresWindow()), "structures");
        Tile("Busiest chunks", "Where lag comes from: mobs, dropped items, hoppers", Glyphs.Stats, () => Do(() => Owner.ShowBusyChunks()), "busy");
        Tile("Lag machines", "Devices built to make it lag or crash, on the map", Glyphs.Warning, () => Do(() => Owner.ShowLagMachines()), "lag");
        Tile("Mods and add-ons", "Which mods it needs, what it holds of each, which are on this PC", Glyphs.Tag, () => Do(() => Owner.ShowMods()), "mods");
        Tile("Compare", "With a backup, a copy or a conversion: what changed", Glyphs.Filter, () => Do(() => Owner.ShowCompare()), "compare");
        Group("BUILD AND CHANGE");
        Tile("Save a structure", "Crop a building out: .nbt, WorldEdit, Litematica …", Glyphs.Copy, () => Main.SaveStructure(World), "crop");
        Tile("Place a structure", "Add a structure or schematic into a copy", Glyphs.Paste, () => Main.PlaceStructure(World), "place");
        Tile("Replace, biome or land", "Swap or fill blocks, change a biome, flatten or smooth the land, in a copy", Glyphs.Edit, () => Main.ReplaceBlocks(World), "replace");
        Tile("Map art", "The world's maps as pictures; pictures as maps", Glyphs.Picture, () => Main.ShowMapArt(World), "mapsee");
        Group("CONVERT AND KEEP SAFE");
        Tile("Convert", "To another edition, console or version", Glyphs.Convert, () => Main.ShowConvert(World), "convertfrom");
        Tile("Backups", "Back up now; every backup, restored as a new world or compared", Glyphs.Backup, () => Main.ShowBackups(World), "backups");
        Tile("Share as a file", World is BedrockWorld ? "A .mcworld: opened, Minecraft imports it" : "A .zip with the world, to send or keep", Glyphs.Export, () => Main.ShareWorld(World), "share");
        Tile("Check for damage", "Read every chunk and report problems", Glyphs.Check, () => Main.CheckWorld(World), "check");
        Tile("Rescue", "Save what can be read from a damaged world", Glyphs.Repair, () => Main.ShowRescue(World.Location), "rescue");
        Tile("Optimize", "Rewrite the storage compactly", Glyphs.Broom, () => Main.Optimize(World), "optimize");
        Group("ADVANCED");
        Tile("Settings file", "Edit level.dat directly", Glyphs.Document, () => Do(() => Main.OpenLevelDat(World)), "leveldat");
        Tile("Database", "Browse the world's records", Glyphs.Database, () => Do(() => { if (World is BedrockWorld b) Main.OpenDocument(new DatabaseDocument(b)); }), "database");
        Tile("Show in Explorer", "Open the world's folder", Glyphs.Folder, () => Do(() => Explorer.Select(World.Location)));

        _column.Controls.Add(Header("ABOUT THIS WORLD", Width0));
        _facts = new ListView
        {
            View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, BackColor = C.BG2, ForeColor = C.FG,
            BorderStyle = BorderStyle.None, Size = S(Width0, 260), Font = Theme.Ui(9.5f), AccessibleName = "About this world",
        };
        _facts.Columns.Add("Key", this.Px(200));
        _facts.Columns.Add("Value", this.Px(680));
        Theme.DarkControl(_facts);
        // The seed and the rest can be copied: the chosen lines' values (Ctrl+C), or every line.
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        menu.Items.Add("Copy", null, (_, _) => CopyFacts(all: false));
        menu.Items.Add("Copy all", null, (_, _) => CopyFacts(all: true));
        _facts.ContextMenuStrip = menu;
        _facts.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyFacts(all: false); e.SuppressKeyPress = true; } };
        _column.Controls.Add(_facts);
    }

    void CopyFacts(bool all)
    {
        var rows = (all || _facts.SelectedItems.Count == 0 ? _facts.Items.Cast<ListViewItem>() : _facts.SelectedItems.Cast<ListViewItem>()).ToList();
        if (rows.Count == 0) return;
        // One chosen line copies its value alone (a seed to paste); more copy "name: value" lines.
        string text = rows.Count == 1 && !all ? rows[0].SubItems[1].Text : string.Join(Environment.NewLine, rows.Select(r => $"{r.Text}: {r.SubItems[1].Text}"));
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    public override void OnShown()
    {
        _facts.BeginUpdate();
        _facts.Items.Clear();
        foreach (var (k, v) in World.Summary())
        {
            var item = new ListViewItem(k) { ForeColor = C.FG_DIM };
            item.SubItems.Add(v).ForeColor = C.FG;
            _facts.Items.Add(item);
        }
        _facts.EndUpdate();
        var flags = WorldFlags.Read(World);
        var blockers = flags.AchievementBlockers();
        _achievements.Text = !flags.LocksAchievements
            ? flags.Cheats == true ? "Cheats are allowed in this world. (Java Edition never locks advancements.)" : ""
            : blockers.Count == 0 ? "✓  Achievements can be earned in this world." : $"✗  Achievements are off: {string.Join("; ", blockers)}.  Change it under Cheats & game mode.";
        _achievements.ForeColor = blockers.Count == 0 ? C.GREEN : C.ORANGE;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
