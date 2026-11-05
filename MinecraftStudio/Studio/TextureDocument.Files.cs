// MinecraftStudio/Studio/TextureDocument.Files.cs: files for TextureDocument.
// Part of TextureDocument (main part in TextureDocument.cs).
using System.Diagnostics;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;

namespace MinecraftStudio.Studio;

partial class TextureDocument
{
    // ------------------------------------------------------------------ export and install

    void ExportJava(bool install) =>
        PackExport.Java(FindForm()!, Main, _editor.Pack, install, PacksFolder, target => TexturePackWriter.Java(_editor.Pack, target, _names, AtlasFill(target)));

    void ExportBedrock(bool install) =>
        PackExport.Bedrock(FindForm()!, Main, _editor.Pack, install, PacksFolder, TexturePackWriter.Bedrock(_editor.Pack, _names, DateTime.Now, BedrockAnimated.Value));

    // ------------------------------------------------------------------ new and open

    /// <summary>The way into Texture Studio: a new pack from a game's textures, or an existing pack.</summary>
    public static void Start(MainForm main)
    {
        int c = Dialogs.Choose(main, "Texture Studio", "Change block and item textures, then save the pack for any Java version or for Bedrock, or install it into your games.",
            "New pack from a game's textures…", "Open a texture pack…", "Open a texture pack folder…");
        switch (c)
        {
            case 0: NewPack(main); break;
            case 1: OpenDialog(main); break;
            case 2: OpenFolderDialog(main); break;
        }
    }

    /// <summary>A new, empty pack on top of the textures of a game installed on this PC.</summary>
    public static async void NewPack(MainForm main)
    {
        var sources = new List<(string Label, Func<TexturePack?> Load, PackEdition Edition)>();
        foreach (var (version, jar) in GameFolders.JavaJars().Take(10))
            sources.Add(($"Java {version}", () => TexturePackReader.Read(jar, TextureNames.Default), PackEdition.Java));
        if (GameFolders.BedrockGameData() != null) sources.Insert(Math.Min(1, sources.Count), ("Bedrock (the installed game)", () => TexturePackReader.ReadBedrockGame(TextureNames.Default), PackEdition.Bedrock));
        if (sources.Count == 0) { Dialogs.Info(main, "No installed Java versions or Bedrock game were found to start from. Open an existing texture pack instead."); return; }
        int pick = Dialogs.Choose(main, "New texture pack", "Start from the textures of:", sources.Select(s => s.Label).ToArray());
        if (pick < 0) return;
        string? name = Dialogs.Input(main, "New texture pack", "Name of the pack:", "My Pack");
        if (string.IsNullOrWhiteSpace(name)) return;
        TexturePack? basePack = null;
        if (!await main.RunAsync("Reading the textures of " + sources[pick].Label, _ => basePack = sources[pick].Load()) || basePack == null) return;
        var pack = new TexturePack { Name = name, Edition = sources[pick].Edition, JavaRelease = basePack.JavaRelease, Description = $"{name}, made with {Dialogs.AppName}" };
        main.OpenDocument(new TextureDocument(pack, basePack, null));
        main.SetStatus($"{basePack.Textures.Count} textures of {sources[pick].Label}: pick one on the left and paint.");
    }

    public static void OpenDialog(MainForm main)
    {
        using var dlg = new OpenFileDialog { Filter = "Texture packs|*.zip;*.mcpack;*.jar|All files|*.*", Title = "Open a texture pack", InitialDirectory = Directory.Exists(PacksFolder) ? PacksFolder : "" };
        if (dlg.ShowDialog(main) == DialogResult.OK) OpenFile(main, dlg.FileName);
    }

    public static void OpenFolderDialog(MainForm main)
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose the pack's folder (the one with pack.mcmeta or manifest.json)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(main) == DialogResult.OK) OpenFile(main, dlg.SelectedPath);
    }

    /// <summary>Opens a pack (file or folder) in a new Texture Studio tab, with the matching game's textures around it.</summary>
    public static async void OpenFile(MainForm main, string path)
    {
        TexturePack? pack = null, basePack = null;
        bool ok = await main.RunAsync("Reading " + Path.GetFileName(path), progress =>
        {
            pack = TexturePackReader.Read(path, TextureNames.Default);
            progress.Report("Reading the game's own textures");
            basePack = pack.Edition == PackEdition.Bedrock
                ? TexturePackReader.ReadBedrockGame(TextureNames.Default)
                : GameFolders.JavaJars().Select(j => j.Jar).FirstOrDefault() is { } jar ? TexturePackReader.Read(jar, TextureNames.Default) : null;
        });
        if (!ok || pack == null) return;
        // A game jar is a source, not a pack to save over.
        bool jar = File.Exists(path) && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        var doc = new TextureDocument(jar ? new TexturePack { Name = pack.Name + " pack", Edition = PackEdition.Java, JavaRelease = pack.JavaRelease } : pack, jar ? pack : basePack, jar ? null : path)
        {
            Key = jar ? null : ("pack", Path.GetFullPath(path).ToLowerInvariant()),
        };
        main.OpenDocument(doc);
        if (pack.Notes.Count > 0 && !jar)
            main.SetStatus($"Opened {Path.GetFileName(path)}: {pack.Textures.Count} textures. {pack.Notes.Count} note(s) while reading: {pack.Notes[0]}");
        else main.SetStatus($"Opened {Path.GetFileName(path)}: {pack.Textures.Count} textures.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _paint.Dispose();
        base.Dispose(disposing);
    }
}
