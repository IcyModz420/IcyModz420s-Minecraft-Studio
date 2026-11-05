// MinecraftStudio/Documents/ConvertDialog.Target.cs: target for ConvertDialog.
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
    // ------------------------------------------------------------------ target

    void SelectEdition(EditionInfo edition, ConversionTarget? target = null)
    {
        _edition = edition;
        for (int i = 0; i < _tiles.Count; i++) _tiles[i].Selected = _editions[i] == edition;
        var targets = TargetCatalog.All.Where(edition.Matches).ToList();
        _version.BeginUpdate();
        _version.Items.Clear();
        for (int i = 0; i < targets.Count; i++) _version.Items.Add(new ComboItem(VersionText(targets[i], i == 0), VersionDetail(targets[i]), targets[i]));
        _version.EndUpdate();
        int index = target != null ? targets.IndexOf(target) : 0;
        _version.SelectedIndex = Math.Max(0, index);
        bool console = targets.Count > 0 && targets[0].Kind == TargetKind.LegacyConsole;
        _consoleOptions.Visible = console;
        _javaBedrockOptions.Visible = !console;
    }

    static string VersionText(ConversionTarget t, bool newest) => t.Kind switch
    {
        TargetKind.LegacyConsole => t.Platform.DisplayName(),
        _ => t.VersionName + (newest ? "  (newest)" : ""),
    };

    static string VersionDetail(ConversionTarget t) => t.Kind switch
    {
        TargetKind.Java => t.JavaFormat switch
        {
            JavaFormat.AnvilContainers => "y −64 to 319",
            JavaFormat.AnvilPalette => "y 0 to 255",
            JavaFormat.AnvilNumeric => "y 0 to 255 · block ids",
            JavaFormat.McRegion => "y 0 to 127 · McRegion",
            _ => "y 0 to 127 · chunk files",
        },
        TargetKind.Bedrock => t.BedrockTerrain switch
        {
            BedrockTerrain.LegacyTerrain => "y 0 to 127 · LevelDB",
            BedrockTerrain.States => t.BedrockChunkVersion >= 39 ? "y −64 to 319" : "y 0 to 255",
            _ => "y 0 to 255 · block ids",
        },
        TargetKind.LegacyConsole => ConsoleFileName(t.Platform),
        TargetKind.PocketLegacy => "256 × 256 · chunks.dat",
        TargetKind.Classic => "64 high · level.dat + .cw",
        TargetKind.Indev => "128 high · .mclevel",
        _ => "",
    };

    static string ConsoleFileName(ConsolePlatform p) => p switch
    {
        ConsolePlatform.Xbox360 => "savegame.dat",
        ConsolePlatform.PSVita => "GAMEDATA.bin",
        _ => "GAMEDATA",
    };

    void TargetChanged()
    {
        var t = Target;
        if (t == null) return;
        LastTarget = t.Name;
        if (t.Kind == TargetKind.LegacyConsole)
        {
            int previous = (_consoleSize.SelectedItem as ComboItem)?.Tag is int n ? n : 54;
            bool newGen = t.Platform is not (ConsolePlatform.Xbox360 or ConsolePlatform.PS3 or ConsolePlatform.PSVita);
            _consoleSize.BeginUpdate();
            _consoleSize.Items.Clear();
            foreach (var (text, chunks, newGenOnly) in ConsoleSizes)
                if (newGen || !newGenOnly) _consoleSize.Items.Add(new ComboItem(text, null, chunks));
            _consoleSize.EndUpdate();
            int keep = _consoleSize.Items.Cast<ComboItem>().ToList().FindIndex(i => (int)i.Tag! == previous);
            _consoleSize.SelectedIndex = Math.Max(0, keep);
            _templateRow.Visible = t.Platform == ConsolePlatform.Xbox360;
        }
        bool modernHeight = t.Kind == TargetKind.Java && t.JavaFormat == JavaFormat.AnvilContainers || t.Kind == TargetKind.Bedrock && t.BedrockChunkVersion >= 39;
        bool finite = t.Kind is TargetKind.PocketLegacy or TargetKind.Classic or TargetKind.Indev;
        // Only the options that mean something for this target are shown.
        _extend.Visible = modernHeight;
        _mcworld.Visible = t.Kind == TargetKind.Bedrock && t.BedrockTerrain != BedrockTerrain.LegacyTerrain;
        _limit.Visible = _radius.Visible = _radiusLabel.Visible = !finite;
        UpdateDepthOption();
        UpdateVersionInfo();
        if (!_folderEdited) SuggestFolder();
        UpdateFolderHint();
        UpdateState();
    }

    void UpdateDimensions()
    {
        var t = Target;
        foreach (var c in _dimRow.Controls.OfType<DarkCheckBox>())
        {
            bool ok = t == null || WorldConverter.SupportsDimension(t, (string)c.Tag!);
            // Unsupported dimensions are unticked; one the previous target could not hold is ticked again.
            if (!ok) c.Checked = false;
            else if (!c.Enabled) c.Checked = true;
            c.Enabled = ok;
        }
    }

    void UpdateVersionInfo()
    {
        var t = Target;
        if (t == null) { _versionInfo.Text = ""; return; }
        const string what = "Blocks, biomes, chests, signs, mobs and players are converted";
        _versionInfo.Text = t.Kind switch
        {
            TargetKind.Java when t.JavaFormat == JavaFormat.AnvilContainers =>
                $"Opens in Java Edition {t.VersionName}. {what}; anything this version lacks becomes the closest block it has.",
            TargetKind.Java =>
                $"Opens in Java Edition {t.VersionName}. {what}; newer blocks become the closest older block, and terrain outside {(t.JavaFormat is JavaFormat.McRegion or JavaFormat.Alpha ? "y 0–127" : "y 0–255")} is cut off. Everything changed or lost is listed afterwards.",
            TargetKind.Bedrock when t.BedrockTerrain == BedrockTerrain.LegacyTerrain =>
                $"Opens in {t.VersionName} (infinite LevelDB worlds, 128 blocks high). Blocks newer than that become the closest block it has.",
            TargetKind.Bedrock when t.BedrockChunkVersion >= 42 =>
                $"Opens in Bedrock Edition {t.VersionName} on Windows, Android and iOS (and consoles through Realms). {what}.",
            TargetKind.Bedrock =>
                $"Opens in Bedrock Edition {t.VersionName} and every later version. {what}; blocks newer than {t.VersionName} become the closest block it has.",
            TargetKind.LegacyConsole => ConsoleInfo(t),
            TargetKind.PocketLegacy =>
                "A 256 × 256 world centred on the spawn, 128 blocks high, for Pocket Edition 0.2 – 0.8 and the Raspberry Pi Edition. Chests and signs are kept; newer blocks become the closest block 0.8 had.",
            TargetKind.Classic =>
                "A 256 × 64 × 256 Classic level centred on the spawn with the water line at sea level: level.dat for the Classic game and a .cw file for ClassiCube and Classic servers.",
            TargetKind.Indev =>
                "A 256 × 128 × 256 Indev level (.mclevel) centred on the spawn. Blocks Indev did not have become the closest block it had.",
            _ => what + ".",
        };
    }

    string ConsoleInfo(ConversionTarget t)
    {
        int size = (_consoleSize.SelectedItem as ComboItem)?.Tag is int n ? n : 54;
        string where = t.Platform == ConsolePlatform.Xbox360
            ? "Pick one of your own Xbox 360 saves as the package to get a ready-to-copy file."
            : "The save data is written decrypted, ready for your save manager to import.";
        return $"A {size} × {size}-chunk {t.Platform.DisplayName()} world centred on the spawn, in the TU17 layout that every later update loads. {where}" +
               (size > 54 ? " Larger sizes need TU46 or newer." : "");
    }
}
