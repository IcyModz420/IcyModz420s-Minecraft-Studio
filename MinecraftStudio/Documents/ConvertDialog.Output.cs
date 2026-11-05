// MinecraftStudio/Documents/ConvertDialog.Output.cs: output for ConvertDialog.
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
    // ------------------------------------------------------------------ output

    static string Documents => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "Converted");

    internal static string? JavaSaves()
    {
        string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        return Directory.Exists(Path.GetDirectoryName(saves)) ? saves : null;
    }

    internal static string? BedrockWorlds() => WorldFinder.Locations()
        .Where(l => l.Group is WorldGroups.Bedrock or WorldGroups.BedrockShared or WorldGroups.BedrockOldStore)
        .OrderBy(l => l.Group switch { WorldGroups.Bedrock => 0, WorldGroups.BedrockShared => 1, _ => 2 })
        .Select(l => l.Folder).FirstOrDefault(Directory.Exists);

    internal static string SafeName(string name) => Core.IO.SafeName.For(name, "Converted World", 60);

    internal static string Unique(string folder)
    {
        string path = folder;
        for (int i = 2; Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any() || File.Exists(path); i++) path = $"{folder} ({i})";
        return path;
    }

    void SuggestFolder()
    {
        var t = Target;
        if (t == null) return;
        string name = SafeName(_name.Text.Trim().Length > 0 ? _name.Text.Trim() : _source?.Name ?? "Converted World");
        string folder = t.Kind switch
        {
            TargetKind.Java => Path.Combine(JavaSaves() ?? Documents, name),
            // Worlds for the current game go straight into its worlds folder; worlds for older versions are kept aside.
            TargetKind.Bedrock when t.BedrockChunkVersion >= 41 => Path.Combine(BedrockWorlds() ?? Documents, name),
            TargetKind.Bedrock => Path.Combine(Documents, $"{name} ({t.Name})"),
            TargetKind.LegacyConsole => Path.Combine(Documents, $"{name} ({t.Platform.DisplayName()})"),
            _ => Path.Combine(Documents, $"{name} ({t.VersionName})"),
        };
        _settingText = true;
        _folder.Text = Unique(folder);
        _settingText = false;
        ShowPathEnd(_folder.Box);
        UpdateFolderHint();
    }

    /// <summary>Long paths: show the end (the world's own folder) rather than the drive.</summary>
    internal static void ShowPathEnd(TextBox box)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        if (box.IsHandleCreated) box.ScrollToCaret();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowPathEnd(_folder.Box);
    }

    void UpdateFolderHint()
    {
        var t = Target;
        string path = _folder.Text.Trim();
        bool inside(string? root) => root != null && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        _folderHint.Text = t?.Kind switch
        {
            TargetKind.Java => inside(JavaSaves()) ? "This is the Java Edition saves folder, so the world shows up in Singleplayer." : "Copy the folder into .minecraft\\saves to play it.",
            TargetKind.Bedrock => inside(BedrockWorlds()) ? "This is Bedrock's worlds folder, so the world shows up on the Play screen."
                : t.BedrockChunkVersion >= 41 ? "Copy the folder into minecraftWorlds, or open the .mcworld file on Windows, Android or iOS."
                : $"Copy the folder into games/com.mojang/minecraftWorlds on the device running {t.VersionName}.",
            TargetKind.LegacyConsole => $"The folder will contain {ConsoleFileName(t.Platform)}" + (t.Platform == ConsolePlatform.Xbox360 ? " (and the rebuilt package when you choose one)." : "."),
            TargetKind.PocketLegacy => "Copy the folder into games/com.mojang/minecraftWorlds on the device running 0.2 – 0.8.",
            TargetKind.Classic => "Contains the level as .dat (Classic: Load file…) and .cw (ClassiCube: put it in the maps folder).",
            TargetKind.Indev => "Contains the .mclevel file: load it from Indev's Load level menu.",
            _ => "",
        };
    }

    void BrowseOutput()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose where to create the converted world (a new folder is made inside it)", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        string current = _folder.Text.Trim();
        if (current.Length > 0) dlg.InitialDirectory = Path.GetDirectoryName(current) ?? "";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string chosen = dlg.SelectedPath;
        // An empty folder is used as it is; otherwise the world gets its own folder inside it.
        bool empty = Directory.Exists(chosen) && !Directory.EnumerateFileSystemEntries(chosen).Any();
        _folder.Text = empty ? chosen : Unique(Path.Combine(chosen, SafeName(_name.Text.Trim().Length > 0 ? _name.Text.Trim() : "Converted World")));
        _folderEdited = true;
        ShowPathEnd(_folder.Box);
    }

    void BrowseTemplate()
    {
        using var dlg = new OpenFileDialog { Title = "Choose one of your own Xbox 360 Minecraft save packages", Filter = "All files|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) _template.Text = dlg.FileName;
    }
}
