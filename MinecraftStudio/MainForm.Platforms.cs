// MinecraftStudio/MainForm.Platforms.cs: which PCs, phones and consoles the menus' tools work on.
// Part of MainForm (main part in MainForm.cs).
// Owns: the menus' feature tags ("Works on" tips from PlatformSupport, the same table as FEATURES.md and Help → What Works
// Where), the World menu fitted to the chosen world when it opens (a tool its edition or console doesn't have is switched
// off, its tip saying why; a limit is said in the tip), the start page tiles' tips, and opening What Works Where.
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;

namespace MinecraftStudio;

partial class MainForm
{
    // Menu items and start tiles by their text, and the feature each one is.
    static readonly Dictionary<string, string> FeatureOf = new(StringComparer.Ordinal)
    {
        ["Get Worlds from an Android Phone…"] = "phone", ["Open in the NBT Editor… (any NBT file, level.dat too)"] = "nbt",
        ["Map && Chunks"] = "map", ["Players && Items"] = "players", ["Cheats && Game Mode"] = "cheats", ["Packs (PC)"] = "worldpacks", ["Packs on This PC…"] = "packspc",
        ["Find Blocks, Items and Mobs…"] = "find", ["Structures…"] = "structures", ["Busiest Chunks (Lag Finder)…"] = "busy", ["Lag Machines…"] = "lag",
        ["Mods and Add-ons…"] = "mods",
        ["Compare with Another World…"] = "compare", ["Save a Structure (Crop a Building)…"] = "crop", ["Place a Structure…"] = "place",
        ["Open a Structure File (Turn, Convert, Materials)…"] = "structurefile", ["Replace Blocks, Change the Biome or Shape the Land…"] = "replace", ["Map Art…"] = "mapsee",
        ["Convert World…"] = "convertfrom", ["Back Up Now"] = "backups", ["Backups (Timeline, Restore)…"] = "backups", ["Save World as a File (.mcworld / .zip)…"] = "share",
        ["Check for Problems…"] = "check", ["Rescue a Damaged World…"] = "rescue", ["Optimize / Compact Storage"] = "optimize", ["Edit level.dat"] = "leveldat",
        ["Bedrock Database Browser"] = "database",
        ["New World from a Heightmap…"] = "heightmap", ["Skin Studio…"] = "skins", ["Texture Studio…"] = "textures", ["Mob Studio…"] = "mobs", ["Animation Studio…"] = "animations",
        ["Check a Pack (Resource or Data)…"] = "packcheck", ["New Data Pack or Add-on…"] = "newpack",
        // Start page tiles.
        ["Open a world"] = "open", ["Open a file"] = "open", ["Worlds from a phone"] = "phone", ["NBT editor"] = "nbt", ["Convert a world"] = "convertfrom",
        ["Rescue a damaged world"] = "rescue", ["Packs on this PC"] = "packspc", ["Crop a building"] = "crop", ["Place a building"] = "place", ["Structure file"] = "structurefile",
        ["Replace, biome or land"] = "replace", ["Skin Studio"] = "skins", ["Texture Studio"] = "textures", ["Mob Studio"] = "mobs", ["Animation Studio"] = "animations",
        ["Check a pack"] = "packcheck", ["Map art"] = "mapsee", ["World from a heightmap"] = "heightmap", ["New data pack or add-on"] = "newpack",
    };

    readonly ToolTip _tileTips = new();

    static string WorksOnTip(string feature) => "Works on: " + PlatformSupport.WorksOn(PlatformSupport.Get(feature));

    /// <summary>Gives every menu item that is a feature its "Works on" tip; the World menu is fitted to the world as it opens.</summary>
    void TagFeatures()
    {
        foreach (var top in _menu.Items.OfType<ToolStripMenuItem>())
        {
            top.DropDown.ShowItemToolTips = true;
            foreach (var item in top.DropDownItems.OfType<ToolStripMenuItem>())
                if (FeatureOf.TryGetValue(item.Text ?? "", out var id))
                {
                    item.Tag = id;
                    item.ToolTipText = WorksOnTip(id);
                }
            if (top.Text == "&World") top.DropDownOpening += (_, _) => FitWorldMenu(top);
        }
    }

    // The World menu for the world chosen now: a tool its edition or console doesn't have is switched off, its tip saying
    // why and where it works; a limit there is said in the tip.
    void FitWorldMenu(ToolStripMenuItem menu)
    {
        var w = SelectedWorld();
        foreach (var item in menu.DropDownItems.OfType<ToolStripMenuItem>())
        {
            if (item.Tag is not string id) continue;
            string? limit = null;
            string? why = w == null ? null : PlatformSupport.WhyNot(id, w, out limit);
            item.Enabled = why == null;
            item.ToolTipText = why ?? (w != null && limit != null ? $"For {PlatformSupport.Describe(w)}: {limit}. {WorksOnTip(id)}" : WorksOnTip(id));
        }
    }

    /// <summary>A start page tile's "Works on" tip.</summary>
    void TipTile(Control tile, string text)
    {
        if (FeatureOf.TryGetValue(text, out var id)) _tileTips.SetToolTip(tile, WorksOnTip(id));
    }

    /// <summary>The Java and Bedrock worlds (and the rest) the program found on this PC.</summary>
    internal IReadOnlyList<FoundWorld> FoundWorlds => _found;

    /// <summary>Whether a world (by its folder or file) is open in a tab here.</summary>
    internal bool IsOpenInTab(string location) =>
        _openWorlds.Any(w => string.Equals(Path.GetFullPath(w.Location).TrimEnd('\\'), Path.GetFullPath(location).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    /// <summary>Help → What Works Where, starting on the chosen world's platform.</summary>
    void ShowWhatWorksWhere()
    {
        using var dlg = new WhatWorksWhereDialog(SelectedWorld() is SaveWorld w ? PlatformSupport.Of(w) : null);
        dlg.ShowDialog(this);
    }
}
