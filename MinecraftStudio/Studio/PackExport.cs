// MinecraftStudio/Studio/PackExport.cs: saving and installing a texture pack — shared by the Texture Studio and the Mob
// Studio.
// Owns: PackExport — the pack for a Java version (saved, or installed into chosen game folders) or for Bedrock (saved,
// opened with the game, or copied into its packs folder), each after the honest report of what goes in; and writing a
// pack file safely (checked after writing).
using System.Diagnostics;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

static class PackExport
{
    /// <summary>Writes a file safely (checked after writing); an error is shown and false returned when it cannot be.</summary>
    public static bool Write(IWin32Window owner, MainForm? main, string path, byte[] data)
    {
        try
        {
            SkinDocument.WriteVerified(path, data);
            main?.SetStatus("Saved " + path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(owner, $"Could not save {path}:\n\n{ex.Message}");
            return false;
        }
    }

    /// <summary>The pack for a Java version: saved where the player chooses, or installed into game folders.</summary>
    /// <param name="build">Makes the pack for a release (the caller adds what it needs, such as the old atlas fill).</param>
    /// <returns>The file saved, or null.</returns>
    public static string? Java(IWin32Window owner, MainForm? main, TexturePack pack, bool install, string folder, Func<JavaRelease, PackResult> build)
    {
        using var dlg = new JavaTargetDialog(install ? "Install into Minecraft Java" : "Export for Java", install, pack.JavaRelease);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        if (install && dlg.Folders.Count == 0) { Dialogs.Info(owner, "Choose at least one game folder."); return null; }
        var target = dlg.Release;
        var r = build(target);
        if (!PackReport.Confirm(owner, $"{pack.Name} for {target.Label}", r, install ? "Install" : "Choose where to save…")) return null;
        bool texturePack = target.Kind != JavaPackKind.ResourcePack;
        if (!install)
        {
            Directory.CreateDirectory(folder);
            using var save = new SaveFileDialog { Filter = "Java pack|*.zip", FileName = r.FileName, InitialDirectory = folder, Title = $"Save the pack for {target.Label}" };
            return save.ShowDialog(owner) == DialogResult.OK && Write(owner, main, save.FileName, r.Data) ? save.FileName : null;
        }
        var done = new List<string>();
        var failed = new List<string>();
        foreach (var gameFolder in dlg.Folders)
        {
            try { done.Add(PackInstaller.InstallJava(r.FileName, r.Data, gameFolder, texturePack)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add($"{gameFolder}: {ex.Message}"); }
        }
        string where = texturePack ? "Mods and Texture Packs" : "Options, Resource Packs (move it to the right-hand list)";
        if (failed.Count > 0) Dialogs.Error(owner, "Some folders failed:\n\n" + string.Join("\n", failed) + (done.Count > 0 ? "\n\nInstalled into:\n" + string.Join("\n", done) : ""));
        else Dialogs.Info(owner, $"Installed into:\n{string.Join("\n", done)}\n\nIn the game: {where}.");
        return done.FirstOrDefault();
    }

    /// <summary>The pack for Bedrock: saved where the player chooses, or put into the game.</summary>
    /// <returns>The file saved (or kept while installing), or null.</returns>
    public static string? Bedrock(IWin32Window owner, MainForm? main, TexturePack pack, bool install, string folder, PackResult r) =>
        Bedrock(owner, main, pack.Name, install, folder, r);

    /// <summary>A Bedrock pack (of any kind) by its name: saved where the player chooses, or put into the game.</summary>
    public static string? Bedrock(IWin32Window owner, MainForm? main, string packName, bool install, string folder, PackResult r)
    {
        var pack = new TexturePack { Name = packName };
        if (!PackReport.Confirm(owner, $"{pack.Name} for Bedrock", r, install ? "Continue" : "Choose where to save…")) return null;
        Directory.CreateDirectory(folder);
        if (!install)
        {
            using var save = new SaveFileDialog { Filter = "Bedrock pack|*.mcpack", FileName = r.FileName, InitialDirectory = folder, Title = "Save the Bedrock pack" };
            return save.ShowDialog(owner) == DialogResult.OK && Write(owner, main, save.FileName, r.Data) ? save.FileName : null;
        }
        var targets = GameFolders.Bedrock();
        var options = new List<string> { "Open it with Minecraft: the game imports it (recommended)" };
        options.AddRange(targets.Select(t => $"Copy it into {t.Label} ({t.Folder})"));
        int choice = Dialogs.Choose(owner, "Install into Bedrock", $"How should \"{pack.Name}\" get into the game?", options.ToArray());
        if (choice < 0) return null;
        try
        {
            if (choice == 0)
            {
                string path = Path.Combine(folder, r.FileName);
                SkinDocument.WriteVerified(path, r.Data);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Dialogs.Info(owner, $"Minecraft opens and imports \"{pack.Name}\" (a copy is kept in {path}).\n\nThen turn it on: Settings, Global Resources.");
                return path;
            }
            string installed = PackInstaller.InstallBedrock(r.Data, targets[choice - 1].Folder);
            Dialogs.Info(owner, $"\"{pack.Name}\" was copied into\n{installed}\n\nRestart Minecraft if it is running, then turn the pack on: Settings, Global Resources.");
            return installed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Dialogs.Error(owner, "Installing the pack failed:\n\n" + ex.Message + "\n\nYou can still use Export to save the .mcpack and open it yourself.");
            return null;
        }
    }
}
