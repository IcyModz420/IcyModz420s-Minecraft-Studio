// MinecraftStudio/Documents/RescueDialog.Reading.cs: reading for RescueDialog.
// Part of RescueDialog (main part in RescueDialog.cs).
using System.Diagnostics;
using System.IO.Compression;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

partial class RescueDialog
{
    // ------------------------------------------------------------------ reading the damaged world

    async Task OpenAsync()
    {
        _opening = true;
        UpdateState();
        _bar.Visible = true;
        _bar.Marquee = true;
        ShowMessage("Reading what can still be read…", C.FG_DIM);
        _text.Text = "Reading the world. Every file is read as far as it can be; nothing in the world is changed.";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<string>(s => { if (_opening && !IsDisposed) _status.Text = s; });
        var findings = new List<string>();
        SaveWorld? world = null;
        Exception? error = null;
        try { world = await Task.Run(() => WorldRescue.Open(_path, findings, progress, token), token); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            error = ex;
            Log.Error("Rescue could not read " + _path, ex);
        }
        _opening = false;
        _cts.Dispose();
        _cts = null;
        // Closed while reading: the world is not needed any more.
        if (IsDisposed || token.IsCancellationRequested)
        {
            world?.Dispose();
            if (!IsDisposed) Close();
            return;
        }
        _bar.Visible = false;
        _bar.Marquee = false;
        if (world == null)
        {
            _format.Text = "Nothing could be read";
            _text.Text = $"The world could not be read at all: {error?.Message}" + (findings.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", findings.Select(f => "• " + f)) : "");
            _intro.Text = "There is nothing to save. If this is a backup or a package, try the files inside it one by one; the world itself was not changed.";
            ShowMessage("Nothing could be read from this world.", C.RED);
            UpdateState();
            return;
        }
        _world = world;
        _findings.AddRange(findings);
        _icon.Image = _icons.Images[MainForm.IconFor(world)];
        _name.Text = world.Name;
        _format.Text = world.FormatDescription;
        _stats.Text = Stats(world);
        _text.Text = FindingsText();
        bool canRepair = WorldRepair.CanRepair(world);
        _intro.Text = canRepair
            ? "Save a repaired copy in the world's own format: everything that could be read, byte for byte. Nothing is converted, so blocks and data from mods and data packs are kept. Or convert it to another edition or version."
            : "Convert saves what could be read as a new world, for this edition or any other. Chunks that cannot be read are left out one by one and listed.";
        _settingText = true;
        _worldName.Text = $"{world.Name} (rescued)";
        _settingText = false;
        SuggestFolder();
        AcceptButton = canRepair ? _repair : _convert;
        ShowMessage(IdleHint, C.FG_FAINT);
        UpdateState();
        (canRepair ? _repair : _convert).Focus();
    }

    const string IdleHint = "The damaged world is never changed: the repaired copy is written to the folder above.";

    /// <summary>Chunks that can be read, per dimension.</summary>
    static string Stats(SaveWorld world)
    {
        var dims = world.Dimensions.Select(d => $"{d.DisplayName} {ConvertDialog.Readable(d):N0}").ToList();
        return dims.Count > 0 ? string.Join("  ·  ", dims) + " chunks can be read" : "No chunks could be read";
    }

    string FindingsText() => _findings.Count == 0
        ? "Nothing damaged was found: the world reads normally. A copy can still be saved below."
        : string.Join("\r\n", _findings.Select(f => "• " + f));

    // ------------------------------------------------------------------ output folder

    void SuggestFolder()
    {
        if (_world == null) return;
        string name = ConvertDialog.SafeName(_worldName.Text.Trim().Length > 0 ? _worldName.Text.Trim() : _world.Name);
        // Next to the damaged world when it is in the game's own folder, so the copy shows up beside it.
        string parent = Path.GetDirectoryName(Path.GetFullPath(_world.Location)) ?? "";
        string parentName = Path.GetFileName(parent);
        string root = parentName.Equals("saves", StringComparison.OrdinalIgnoreCase) || parentName.Equals("minecraftWorlds", StringComparison.OrdinalIgnoreCase)
            ? parent
            : (_world is JavaWorld ? ConvertDialog.JavaSaves() : _world is BedrockWorld ? ConvertDialog.BedrockWorlds() : null) ?? RescuedFolder;
        _settingText = true;
        _folder.Text = ConvertDialog.Unique(Path.Combine(root, name));
        _settingText = false;
        ConvertDialog.ShowPathEnd(_folder.Box);
        UpdateFolderHint();
    }

    void UpdateFolderHint()
    {
        string path = _folder.Text.Trim();
        string parentName = Path.GetFileName(Path.GetDirectoryName(path.TrimEnd('\\')) ?? "");
        _folderHint.Text = _world switch
        {
            JavaWorld => parentName.Equals("saves", StringComparison.OrdinalIgnoreCase)
                ? "This is a saves folder, so the copy shows up in Singleplayer." : "Copy the folder into .minecraft\\saves to play it.",
            BedrockWorld => parentName.Equals("minecraftWorlds", StringComparison.OrdinalIgnoreCase)
                ? "This is Bedrock's worlds folder, so the copy shows up on the Play screen." : "Copy the folder into minecraftWorlds, or open the .mcworld file on the device.",
            _ => "",
        };
    }

    void BrowseOutput()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose where to save the repaired copy (a new folder is made inside it)", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        string current = _folder.Text.Trim();
        if (current.Length > 0) dlg.InitialDirectory = Path.GetDirectoryName(current) ?? "";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string chosen = dlg.SelectedPath;
        // An empty folder is used as it is; otherwise the copy gets its own folder inside it.
        bool empty = Directory.Exists(chosen) && !Directory.EnumerateFileSystemEntries(chosen).Any();
        _folder.Text = empty ? chosen : ConvertDialog.Unique(Path.Combine(chosen, ConvertDialog.SafeName(_worldName.Text.Trim().Length > 0 ? _worldName.Text.Trim() : "Rescued World")));
        _folderEdited = true;
        ConvertDialog.ShowPathEnd(_folder.Box);
    }

    string? Validate(string output)
    {
        if (output.Length == 0) return "Choose a folder for the repaired copy.";
        try { output = Path.GetFullPath(output); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return "The folder path is not valid."; }
        if (File.Exists(output)) return "A file with that name already exists; choose another folder.";
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) return "That folder is not empty; choose a new folder name.";
        string source = Path.GetFullPath(_world!.Location).TrimEnd('\\');
        if (output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(output.TrimEnd('\\'), source, StringComparison.OrdinalIgnoreCase))
            return "The repaired copy cannot go inside the damaged world.";
        return null;
    }
}
