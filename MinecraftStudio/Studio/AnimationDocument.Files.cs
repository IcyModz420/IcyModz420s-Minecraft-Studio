// MinecraftStudio/Studio/AnimationDocument.Files.cs: the Animation Studio's packs — new, open, save, export, install,
// into a world.
// Part of AnimationDocument (main part in AnimationDocument.cs).
// Owns: starting from a game installed on this PC (Bedrock's mobs, or Java's textures on Bedrock's models), opening a
// Bedrock pack (its animations and models are shown and kept), saving every changed mob into one pack (AnimationPack,
// checked by reading it back), export and install through PackExport, and adding the pack to a world's Packs page.
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class AnimationDocument
{
    public override string Title => "Animations: " + _packName;
    public override string? ToolTipText => _path ?? "Not saved yet";
    public override bool IsDirty => _projects.Values.Any(p => p.IsDirty);
    public override bool CanSave => true;

    List<Core.Models.AnimationProject> Changed => _projects.Values.Where(p => p.ChangedAnimations.Count > 0 || p.ModelChanged).ToList();

    PackResult Build() => AnimationPack.Bedrock(Changed, _packName, $"{_packName}, made with {Dialogs.AppName}", DateTime.Now, Texture());

    public override Task SaveAsync()
    {
        if (_path == null || !File.Exists(_path)) { SaveAs(); return Task.CompletedTask; }
        var r = Build();
        if (r.Problems.Count > 0 && !PackReport.Confirm(FindForm()!, "Save " + _packName, r, "Save")) return Task.CompletedTask;
        if (PackExport.Write(FindForm()!, Main, _path, r.Data)) MarkSaved();
        return Task.CompletedTask;
    }

    void SaveAs()
    {
        var r = Build();
        if (!PackReport.Confirm(FindForm()!, "Save " + _packName, r, "Choose where to save…")) return;
        Directory.CreateDirectory(MobDocument.PacksFolder);
        using var dlg = new SaveFileDialog { Filter = "Bedrock pack|*.mcpack", FileName = r.FileName, InitialDirectory = MobDocument.PacksFolder, Title = "Save the animation pack (Bedrock)" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK || !PackExport.Write(FindForm()!, Main, dlg.FileName, r.Data)) return;
        _path = dlg.FileName;
        MarkSaved();
    }

    void MarkSaved()
    {
        foreach (var p in _projects.Values) p.MarkSaved();
        UpdateState();
    }

    string? Export(bool install) => PackExport.Bedrock(FindForm()!, Main, _packName, install, MobDocument.PacksFolder, Build());

    /// <summary>Saves the pack and opens a world's Packs page with it ready to install.</summary>
    async Task IntoWorld()
    {
        if (Export(install: false) is not { } file) return;
        await Main.ShowPacksOverview(file);
    }

    // ------------------------------------------------------------------ new and open

    void FillNewMenu(ToolStripDropDownButton menu)
    {
        menu.DropDownItems.Clear();
        foreach (var (label, load) in MobDocument.Sources())
            menu.DropDownItems.Add(MenuItem(label, (_, _) => NewStudio(Main, label, load)));
        if (menu.DropDownItems.Count == 0) menu.DropDownItems.Add(new ToolStripMenuItem("No Java or Bedrock game is installed on this PC") { Enabled = false });
    }

    /// <summary>The way into the Animation Studio.</summary>
    public static void Start(MainForm main)
    {
        var sources = MobDocument.Sources();
        var options = sources.Select(s => "Design mobs of " + s.Label).Append("Open a pack with animations…").ToArray();
        int c = Dialogs.Choose(main, "Animation Studio", "See every mob move, and design its animations and model: keyframes, formulas, timing, bones and boxes. Saved as a Bedrock pack that replaces the game's own (Java Edition keeps its mobs' animations in the game's code).", options);
        if (c < 0) return;
        if (c == sources.Count) OpenDialog(main);
        else NewStudio(main, sources[c].Label, sources[c].Load);
    }

    static async void NewStudio(MainForm main, string label, Func<MobCatalog?> load)
    {
        string? name = Dialogs.Input(main, "New animation pack", "Name of the pack:", "My Animations");
        if (string.IsNullOrWhiteSpace(name)) return;
        MobCatalog? catalog = null;
        if (!await main.RunAsync("Reading the mobs of " + label, _ => catalog = load()) || catalog == null) return;
        if (catalog.Animations.Count == 0) { Dialogs.Info(main, "No mob animations were found: they come from Minecraft Bedrock installed on this PC."); return; }
        main.OpenDocument(new AnimationDocument(catalog, name.Trim()));
        main.SetStatus($"{catalog.Animations.Count} animations of {label}: choose a mob, pick an animation and press Play (Space).");
    }

    public static void OpenDialog(MainForm main)
    {
        using var dlg = new OpenFileDialog { Filter = "Bedrock packs|*.mcpack;*.zip|All files|*.*", Title = "Open a pack with animations", InitialDirectory = Directory.Exists(MobDocument.PacksFolder) ? MobDocument.PacksFolder : "" };
        if (dlg.ShowDialog(main) == DialogResult.OK) OpenFile(main, dlg.FileName);
    }

    /// <summary>Opens a Bedrock pack: the game's mobs with the pack's animations and models over them (kept when saved again).</summary>
    public static async void OpenFile(MainForm main, string path)
    {
        MobCatalog? catalog = null;
        var fromPack = new List<string>();
        string name = Path.GetFileNameWithoutExtension(path);
        bool ok = await main.RunAsync("Reading " + Path.GetFileName(path), progress =>
        {
            var files = TexturePackReader.Files(path);
            var own = MobCatalog.FromFiles(files, PackEdition.Bedrock, "this pack");
            progress.Report("Reading the game's own mobs");
            catalog = MobDocument.Withjava(MobCatalog.Bedrock()) ?? own;
            if (catalog != own)
            {
                foreach (var (id, m) in own.Models) catalog.Models[id] = m;
                foreach (var (id, a) in own.Animations) catalog.Animations[id] = a;
                foreach (var (id, map) in own.AnimationsOf) catalog.AnimationsOf[id] = map;
                foreach (var (key, info) in own.Info) catalog.Info[key] = info;
                foreach (var (key, t) in own.Textures.Textures) catalog.Textures.Textures[key] = t;
            }
            fromPack.AddRange(own.Animations.Keys);
            fromPack.AddRange(own.Models.Keys);
        });
        if (!ok || catalog == null) return;
        if (fromPack.Count == 0) { Dialogs.Info(main, $"{Path.GetFileName(path)} has no animations or mob models in it."); return; }
        main.OpenDocument(new AnimationDocument(catalog, name, path, fromPack) { Key = ("animations", Path.GetFullPath(path).ToLowerInvariant()) });
        main.SetStatus($"Opened {Path.GetFileName(path)}: {fromPack.Count} animation(s) and model(s), shown over the game's own.");
    }
}
