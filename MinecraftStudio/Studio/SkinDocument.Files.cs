// MinecraftStudio/Studio/SkinDocument.Files.cs: files for SkinDocument.
// Part of SkinDocument (main part in SkinDocument.cs).
using System.Diagnostics;
using MinecraftStudio.Core.Imaging;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class SkinDocument
{
    // ------------------------------------------------------------------ new and open

    void OpenNew(Skin skin) => Main.OpenDocument(new SkinDocument(skin, null));

    public static Skin Blank(SkinModel model)
    {
        // A plain grey figure, each part a slightly different shade so the body is easy to read; the second layer is empty.
        var img = new RgbaImage(64, 64);
        var shades = new Dictionary<string, uint>
        {
            ["Head"] = 0xFFB4B4B4, ["Body"] = 0xFF9C9C9C, ["Right arm"] = 0xFFA8A8A8, ["Left arm"] = 0xFFA8A8A8, ["Right leg"] = 0xFF8C8C8C, ["Left leg"] = 0xFF8C8C8C,
        };
        foreach (var box in SkinLayout.Boxes(model).Where(b => !b.Overlay))
            foreach (var f in box.Faces())
                img.Fill(f.U, f.V, f.W, f.H, shades[box.Part]);
        return new Skin(img, model) { Name = "New Skin" };
    }

    /// <summary>The way into Skin Studio: start blank, from a game skin, or from a file.</summary>
    public static void Start(MainForm main)
    {
        int choice = Dialogs.Choose(main, "Skin Studio", "Make a skin for Java and Bedrock: paint it on the 3D model, then export it for any version or install it into your games.",
            "Blank skin, classic arms (4 pixels)", "Blank skin, slim arms (3 pixels)", "Start from one of the game's default skins…", "Open a skin file or skin pack…");
        switch (choice)
        {
            case 0: main.OpenDocument(new SkinDocument(Blank(SkinModel.Classic), null)); break;
            case 1: main.OpenDocument(new SkinDocument(Blank(SkinModel.Slim), null)); break;
            case 2: NewFromGame(main); break;
            case 3: OpenDialog(main); break;
        }
    }

    public static void NewFromGame(MainForm main)
    {
        var notes = new List<string>();
        var skins = GameSkins.Find(notes);
        if (skins.Count == 0)
        {
            Dialogs.Info(main, "No game skins were found on this PC (they are read from an installed Java version or Minecraft Bedrock)." + (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : ""));
            return;
        }
        using var dlg = new SkinPickDialog("New skin from the game's skins", "The default skins of the games on this PC. You get your own copy to change; the game's files are not touched.",
            skins.Select(s => (s.Title, s.Caption, s.Skin)).ToList());
        if (dlg.ShowDialog(main) != DialogResult.OK || dlg.Chosen is not { } chosen) return;
        var copy = chosen.Copy();
        copy.Name = chosen.Name + " copy";
        main.OpenDocument(new SkinDocument(copy, null));
    }

    public static void OpenDialog(MainForm main)
    {
        using var dlg = new OpenFileDialog { Filter = "Skins and skin packs|*.png;*.mcpack;*.zip|All files|*.*", Title = "Open a skin", InitialDirectory = Directory.Exists(SkinsFolder) ? SkinsFolder : "" };
        if (dlg.ShowDialog(main) == DialogResult.OK) OpenFile(main, dlg.FileName);
    }

    /// <summary>Opens a skin PNG, or a skin from a Bedrock skin pack, in a new Skin Studio tab.</summary>
    public static bool OpenFile(MainForm main, string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (Png.IsPng(data))
            {
                var skin = Skin.Load(data, null, Path.GetFileNameWithoutExtension(path));
                main.OpenDocument(new SkinDocument(skin, path) { Key = ("skin", Path.GetFullPath(path).ToLowerInvariant()) });
                main.SetStatus($"Opened {Path.GetFileName(path)}: {skin.Image.Width} × {skin.Image.Width}, {(skin.Model == SkinModel.Slim ? "slim" : "classic")} arms");
                return true;
            }
            var skins = SkinExport.ReadBedrockSkinPack(data, out var notes);
            if (skins.Count == 0) { Dialogs.Error(main, $"{Path.GetFileName(path)} holds no skins." + (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : "")); return false; }
            Skin? pick = skins.Count == 1 ? skins[0].Skin : null;
            if (pick == null)
            {
                using var dlg = new SkinPickDialog("Open a skin from the pack", $"{Path.GetFileName(path)} holds {skins.Count} skins. Pick one to open.",
                    skins.Select(s => (s.Name, s.Skin.Model == SkinModel.Slim ? "slim arms" : "classic arms", s.Skin)).ToList());
                if (dlg.ShowDialog(main) != DialogResult.OK || dlg.Chosen is not { } c) return false;
                pick = c;
            }
            pick.LoadNotes.AddRange(notes.Select(n => n.Replace("Problem: ", "")));
            main.OpenDocument(new SkinDocument(pick, null));
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            Dialogs.Error(main, $"Could not open {Path.GetFileName(path)} as a skin:\n\n{ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------ export and install

    void ExportFile(string title, SkinFile file, string filter)
    {
        if (!ExportNotes.Confirm(FindForm()!, title, file.Notes, "Choose where to save…")) return;
        if (file.Problems.Count > 0) { Write("", file); return; }
        Directory.CreateDirectory(SkinsFolder);
        using var dlg = new SaveFileDialog { Filter = filter, FileName = file.FileName, InitialDirectory = SkinsFolder, Title = title };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Write(dlg.FileName, file);
    }

    void BedrockPack(bool install)
    {
        using var dlg = new BedrockPackDialog(_editor.Skin.Copy(), install);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var file = SkinExport.BedrockSkinPack(dlg.Skins, dlg.PackName, DateTime.Now);
        if (!ExportNotes.Confirm(FindForm()!, install ? "Install skin pack" : "Bedrock skin pack", file.Notes, install ? "Continue" : "Choose where to save…")) return;
        if (file.Problems.Count > 0) { Write("", file); return; }
        if (!install)
        {
            Directory.CreateDirectory(SkinsFolder);
            using var save = new SaveFileDialog { Filter = "Bedrock pack|*.mcpack", FileName = file.FileName, InitialDirectory = SkinsFolder, Title = "Save skin pack" };
            if (save.ShowDialog(FindForm()) == DialogResult.OK) Write(save.FileName, file);
            return;
        }
        var targets = GameFolders.Bedrock();
        var options = new List<string> { "Open it with Minecraft: the game imports it (recommended)" };
        options.AddRange(targets.Select(t => $"Copy it into {t.Label} ({t.Folder})"));
        int choice = Dialogs.Choose(FindForm()!, "Install skin pack", $"How should \"{dlg.PackName}\" get into the game?", options.ToArray());
        if (choice < 0) return;
        try
        {
            if (choice == 0)
            {
                string path = Path.Combine(SkinsFolder, file.FileName);
                WriteVerified(path, file.Data);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Main.SetStatus("Opened " + path + " with Minecraft");
                Dialogs.Info(FindForm()!, $"Minecraft opens and imports \"{dlg.PackName}\" (a copy is kept in {path}).\n\nIn the game: Dressing Room, Classic Skins: the pack is listed there.");
            }
            else
            {
                string folder = PackInstaller.InstallBedrock(file.Data, targets[choice - 1].Folder);
                Main.SetStatus("Installed into " + folder);
                Dialogs.Info(FindForm()!, $"\"{dlg.PackName}\" was copied into\n{folder}\n\nIf Minecraft is running, restart it. The pack is listed in the Dressing Room under Classic Skins.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Dialogs.Error(FindForm()!, "Installing the skin pack failed:\n\n" + ex.Message + "\n\nYou can still use Export to save the .mcpack and open it yourself.");
        }
    }

    void JavaPack(bool install)
    {
        using var dlg = new JavaPackDialog(_editor.Skin, install);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        SkinFile file;
        try { file = SkinExport.JavaDefaultSkinPack(_editor.Skin, dlg.From, dlg.To, dlg.PackName); }
        catch (ArgumentException ex) { Dialogs.Error(FindForm()!, ex.Message); return; }
        if (!ExportNotes.Confirm(FindForm()!, install ? "Install Java pack" : "Java pack", file.Notes, install ? "Install" : "Choose where to save…")) return;
        if (file.Problems.Count > 0) { Write("", file); return; }
        if (!install)
        {
            Directory.CreateDirectory(SkinsFolder);
            using var save = new SaveFileDialog { Filter = "Java pack|*.zip", FileName = file.FileName, InitialDirectory = SkinsFolder, Title = "Save Java pack" };
            if (save.ShowDialog(FindForm()) == DialogResult.OK) Write(save.FileName, file);
            return;
        }
        var done = new List<string>();
        var failed = new List<string>();
        foreach (var folder in dlg.Folders)
        {
            try { done.Add(PackInstaller.InstallJava(file.FileName, file.Data, folder, dlg.TexturePack)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add($"{folder}: {ex.Message}"); }
        }
        string where = dlg.TexturePack ? "Mods and Texture Packs" : "Options, Resource Packs (move it to the right-hand list)";
        Main.SetStatus($"Installed {file.FileName} into {done.Count} game folder(s)");
        if (failed.Count > 0) Dialogs.Error(FindForm()!, "Some folders failed:\n\n" + string.Join("\n", failed) + (done.Count > 0 ? "\n\nInstalled into:\n" + string.Join("\n", done) : ""));
        else Dialogs.Info(FindForm()!, $"Installed into:\n{string.Join("\n", done)}\n\nIn the game: {where}.");
    }

    void UseAsAccountSkin()
    {
        var file = SkinExport.JavaPng(_editor.Skin);
        if (!ExportNotes.Confirm(FindForm()!, "Use as your Java skin", file.Notes, "Save it and show me")) return;
        string path = Path.Combine(SkinsFolder, file.FileName);
        if (!Write(path, file)) return;
        Explorer.Select(path);
        string arms = _editor.Skin.Model == SkinModel.Slim ? "Slim" : "Classic";
        Dialogs.Info(FindForm()!, $"The skin is saved as\n{path}\n(it is selected in File Explorer).\n\n" +
            $"To make it your skin online:\n• Minecraft Launcher: Minecraft: Java Edition, Skins, New skin. Browse to the file, choose {arms} arms, then Save & Use.\n" +
            "• Or on minecraft.net: sign in, open your profile and choose Change Skin under Java Edition.\n\n" +
            "Both use your own Microsoft account. This program never signs in for you.");
    }

    public override void OnActivated() => _view3d.Focus();

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _checkTimer.Dispose(); _paint.Dispose(); }
        base.Dispose(disposing);
    }
}
