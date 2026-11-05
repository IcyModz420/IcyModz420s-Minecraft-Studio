// MinecraftStudio/Studio/MobDocument.Files.cs: the Mob Studio's packs — new, open, save, export, install, into a world.
// Part of MobDocument (main part in MobDocument.cs).
// Owns: a new pack on top of the mobs of a game installed on this PC (Java's textures get the installed Bedrock game's
// models); opening a pack (its mob textures become editable; a Bedrock pack's own entities and models — an add-on's —
// are read too); saving for the pack's own edition; export and install through PackExport; adding the pack to a world
// (saved, then the world opens on its Packs page with it ready to install); and whole-texture picture actions.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class MobDocument
{
    public static string PacksFolder => TextureDocument.PacksFolder;

    public TextureEditor Editor => _editor;
    public override string Title => "Mobs: " + _editor.Pack.Name;
    public override string? ToolTipText => _path ?? "Not saved yet";
    public override bool IsDirty => _editor.IsDirty;
    public override bool CanSave => true;

    string EditionName => _editor.Pack.Edition == PackEdition.Bedrock ? "Bedrock" : (_editor.Pack.JavaRelease ?? JavaReleases.Newest).Label;

    /// <summary>The mob names the target edition's installed game has (to point out the pack's others), or null.</summary>
    IReadOnlySet<string>? MobsOf(PackEdition target) =>
        target == _catalog.Edition ? _catalog.Textures.Textures.Keys.ToHashSet(StringComparer.Ordinal) : _catalog.OtherEdition;

    PackResult Build(JavaRelease? release = null) => _editor.Pack.Edition == PackEdition.Bedrock && release == null
        ? TexturePackWriter.Bedrock(_editor.Pack, _names, DateTime.Now, null, MobsOf(PackEdition.Bedrock))
        : TexturePackWriter.Java(_editor.Pack, release ?? _editor.Pack.JavaRelease ?? JavaReleases.Newest, _names, null, MobsOf(PackEdition.Java));

    public override Task SaveAsync()
    {
        if (_path == null || Directory.Exists(_path)) { SaveAs(); return Task.CompletedTask; }
        var r = Build();
        if (r.Problems.Count > 0 || r.NotIncluded.Count > 0) { if (!PackReport.Confirm(FindForm()!, "Save " + _editor.Pack.Name, r, "Save")) return Task.CompletedTask; }
        if (PackExport.Write(FindForm()!, Main, _path, r.Data)) { _editor.MarkSaved(); OnStateChanged(); }
        return Task.CompletedTask;
    }

    void SaveAs()
    {
        var r = Build();
        if (r.Problems.Count > 0 || r.NotIncluded.Count > 0) { if (!PackReport.Confirm(FindForm()!, "Save " + _editor.Pack.Name, r, "Choose where to save…")) return; }
        Directory.CreateDirectory(PacksFolder);
        bool bedrock = _editor.Pack.Edition == PackEdition.Bedrock;
        using var dlg = new SaveFileDialog
        {
            Filter = bedrock ? "Bedrock pack|*.mcpack" : "Java pack|*.zip", FileName = r.FileName, Title = $"Save the pack ({EditionName})",
            InitialDirectory = _path != null && File.Exists(_path) ? Path.GetDirectoryName(_path) : PacksFolder,
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK || !PackExport.Write(FindForm()!, Main, dlg.FileName, r.Data)) return;
        _path = dlg.FileName;
        _editor.MarkSaved();
        OnStateChanged();
    }

    string? ExportJava(bool install) =>
        PackExport.Java(FindForm()!, Main, _editor.Pack, install, PacksFolder, target => Build(target));

    string? ExportBedrock(bool install) =>
        PackExport.Bedrock(FindForm()!, Main, _editor.Pack, install, PacksFolder, TexturePackWriter.Bedrock(_editor.Pack, _names, DateTime.Now, null, MobsOf(PackEdition.Bedrock)));

    /// <summary>Saves the pack for the world's edition and opens the world on its Packs page, ready to install it there.</summary>
    async Task AddToWorld()
    {
        int edition = Dialogs.Choose(FindForm()!, "Into a world", "Which kind of world should the pack go into?",
            "A Java Edition (PC) world (as its world resource pack)", "A Bedrock Edition (Windows PC) world");
        if (edition < 0) return;
        string? file = edition == 0 ? ExportJava(install: false) : ExportBedrock(install: false);
        if (file == null) return;
        await Main.ShowPacksOverview(file);
    }

    // ------------------------------------------------------------------ the texture as a whole

    void ReplacePicture()
    {
        if (_editor.Current == null) return;
        using var dlg = new OpenFileDialog { Filter = PixelClipboard.OpenFilter, Title = "Replace with a picture (used as it is)" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { _editor.ReplaceImage("Replace picture", PixelClipboard.Load(dlg.FileName), null); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Dialogs.Error(FindForm()!, $"Could not use {Path.GetFileName(dlg.FileName)}:\n\n{ex.Message}");
        }
    }

    void ExportPicture()
    {
        if (_editor.Current is not { } t || _editor.Selected is not { } key) return;
        using var dlg = new SaveFileDialog { Filter = "PNG image|*.png", FileName = SkinExport.SafeFileName(_catalog.Describe(key).Display) + ".png", Title = "Save the texture as a picture" };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) PackExport.Write(FindForm()!, Main, dlg.FileName, t.Image.ToPng());
    }

    void UseAsIcon()
    {
        if (_editor.Current is not { } t || _editor.Selected is not { } key) return;
        var img = t.FirstFrame();
        // The icon is square: the texture fitted inside it.
        int side = Math.Max(64, Math.Max(img.Width, img.Height));
        var icon = new RgbaImage(side, side);
        var fitted = Resample.Scale(img, img.Width * side / Math.Max(img.Width, img.Height), img.Height * side / Math.Max(img.Width, img.Height), smooth: false);
        icon.CopyFrom(fitted, 0, 0, fitted.Width, fitted.Height, (side - fitted.Width) / 2, (side - fitted.Height) / 2);
        _editor.Pack.Icon = icon;
        Main.SetStatus("The pack's icon is now " + _catalog.Describe(key).Display);
    }

    // ------------------------------------------------------------------ new and open

    void FillNewMenu(ToolStripDropDownButton menu)
    {
        menu.DropDownItems.Clear();
        foreach (var (label, start) in Sources())
            menu.DropDownItems.Add(MenuItem(label, (_, _) => NewPack(Main, label, start)));
        if (menu.DropDownItems.Count == 0) menu.DropDownItems.Add(new ToolStripMenuItem("No Java or Bedrock game is installed on this PC") { Enabled = false });
    }

    /// <summary>The games on this PC a mob pack can start from, newest first.</summary>
    internal static List<(string Label, Func<MobCatalog?> Load)> Sources()
    {
        var list = new List<(string, Func<MobCatalog?>)>();
        if (GameFolders.BedrockGameData() != null) list.Add(("Bedrock Edition (Windows PC)", () => Withjava(MobCatalog.Bedrock())));
        foreach (var (version, jar) in GameFolders.JavaJars().Take(10).Where(j => JavaReleases.Of(j.Version) is { Kind: JavaPackKind.ResourcePack }))
            list.Add(($"Java Edition {version} (PC)", () => WithBedrock(MobCatalog.Java(jar, version))));
        return list;
    }

    /// <summary>Java mobs get the installed Bedrock game's models (Java keeps its own in the game's code).</summary>
    internal static MobCatalog WithBedrock(MobCatalog java)
    {
        if (MobCatalog.Bedrock() is { } bedrock)
        {
            java.BorrowModels(bedrock);
            java.OtherEdition = bedrock.Textures.Textures.Keys.ToHashSet(StringComparer.Ordinal);
        }
        else java.Notes.Add("Minecraft Bedrock is not installed on this PC, so there are no 3D models: mobs are painted flat.");
        return java;
    }

    /// <summary>Bedrock mobs, with the newest Java release's mob names (to point out the ones only one edition has).</summary>
    internal static MobCatalog? Withjava(MobCatalog? bedrock)
    {
        if (bedrock != null && GameFolders.JavaJars().FirstOrDefault() is { Jar: { } jar }) bedrock.OtherEdition = MobCatalog.JavaNames(jar);
        return bedrock;
    }

    /// <summary>The way into the Mob Studio: a new pack from a game's mobs, or an existing pack.</summary>
    public static void Start(MainForm main)
    {
        var sources = Sources();
        var options = sources.Select(s => "New mob pack from " + s.Label).Append("Open a pack…").ToArray();
        int c = Dialogs.Choose(main, "Mob Studio", "Paint every mob — animals, monsters, villagers, bosses — on its 3D model, then save the pack for any Java version or Bedrock, install it, or put it in a world.", options);
        if (c < 0) return;
        if (c == sources.Count) OpenDialog(main);
        else NewPack(main, sources[c].Label, sources[c].Load);
    }

    static async void NewPack(MainForm main, string label, Func<MobCatalog?> load)
    {
        string? name = Dialogs.Input(main, "New mob pack", "Name of the pack:", "My Mobs");
        if (string.IsNullOrWhiteSpace(name)) return;
        MobCatalog? catalog = null;
        if (!await main.RunAsync("Reading the mobs of " + label, _ => catalog = load()) || catalog == null) return;
        var pack = new TexturePack
        {
            Name = name.Trim(), Edition = catalog.Edition, JavaRelease = catalog.Textures.JavaRelease, Description = $"{name.Trim()}, made with {Dialogs.AppName}",
        };
        main.OpenDocument(new MobDocument(pack, catalog, null));
        int models = catalog.Info.Values.Count(i => i.GeometryId != null);
        main.SetStatus($"{catalog.Textures.Textures.Count} mob textures of {label} ({models} with a 3D model): pick one on the left and paint." +
                       (catalog.Notes.Count > 0 ? "  " + catalog.Notes[^1] : ""));
    }

    public static void OpenDialog(MainForm main)
    {
        using var dlg = new OpenFileDialog { Filter = "Packs|*.zip;*.mcpack|All files|*.*", Title = "Open a pack to change its mobs", InitialDirectory = Directory.Exists(PacksFolder) ? PacksFolder : "" };
        if (dlg.ShowDialog(main) == DialogResult.OK) OpenFile(main, dlg.FileName);
    }

    /// <summary>Opens a pack (file or folder) in a Mob Studio tab, around the mobs of the matching installed game.</summary>
    public static async void OpenFile(MainForm main, string path)
    {
        TexturePack? pack = null;
        MobCatalog? catalog = null;
        int taken = 0;
        bool ok = await main.RunAsync("Reading " + Path.GetFileName(path), progress =>
        {
            pack = TexturePackReader.Read(path, TextureNames.Default);
            // A Bedrock pack's own entities and models (an add-on's mobs), read while its textures are still files.
            MobCatalog? own = pack.Edition == PackEdition.Bedrock
                ? MobCatalog.FromFiles(pack.OtherFiles.ToDictionary(f => f.Key, f => (Func<byte[]>)(() => f.Value), StringComparer.OrdinalIgnoreCase), PackEdition.Bedrock, "this pack")
                : null;
            taken = MobCatalog.TakeMobTextures(pack);
            progress.Report("Reading the game's own mobs");
            if (pack.Edition == PackEdition.Bedrock) catalog = Withjava(MobCatalog.Bedrock());
            else if (GameFolders.JavaJars().FirstOrDefault() is { Jar: { } jar, Version: { } version }) catalog = WithBedrock(MobCatalog.Java(jar, version));
            catalog ??= new MobCatalog { Edition = pack.Edition, Source = "this pack" };
            if (own != null)
            {
                foreach (var (id, m) in own.Models) catalog.Models[id] = m;
                foreach (var (id, p) in own.Poses) catalog.Poses[id] = p;
                foreach (var (key, info) in own.Info) catalog.Info[key] = info;
            }
        });
        if (!ok || pack == null || catalog == null) return;
        main.OpenDocument(new MobDocument(pack, catalog, path) { Key = ("mobs", Path.GetFullPath(path).ToLowerInvariant()) });
        main.SetStatus($"Opened {Path.GetFileName(path)}: {taken} mob texture(s){(pack.Textures.Count > taken ? $" and {pack.Textures.Count - taken} block/item texture(s) (kept as they are)" : "")}.");
    }
}
