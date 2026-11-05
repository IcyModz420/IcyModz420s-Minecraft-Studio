// MinecraftStudio/MainForm.Opening.cs: opening for MainForm.
// Part of MainForm (main part in MainForm.cs).
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.Phone;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio;

partial class MainForm
{
    // ------------------------------------------------------------------ opening

    async Task OpenFolderDialog()
    {
        using var dlg = new FolderBrowserDialog { Description = "Choose a world folder (the folder that contains level.dat, db, region, chunks.dat or a console save)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) await OpenPath(dlg.SelectedPath);
    }

    async Task OpenFileDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open a Minecraft file",
            Filter = "Minecraft files|*.dat;*.dat_old;*.nbt;*.mca;*.mcr;*.mcs;*.bin;*.mclevel;*.mine;*.schematic;*.schem;*.litematic;*.mcstructure;*.mcworld;*.mctemplate;*.zip;GAMEDATA*;savegame*|Skins and skin packs|*.png;*.mcpack|Texture packs|*.zip;*.mcpack|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) await OpenPath(dlg.FileName);
    }

    /// <summary>The NBT editor on any file, level.dat too (which Open would open as a whole world).</summary>
    Task OpenNbtEditor()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open a file in the NBT editor",
            Filter = "NBT files|*.dat;*.dat_old;*.nbt;*.mcstructure;*.schematic;*.schem;*.litematic;*.mclevel;*.mine|All files|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        try { OpenDocument(new NbtDocument(new FileNbtSource(dlg.FileName)) { Key = ("nbt", Path.GetFullPath(dlg.FileName).ToLowerInvariant()) }); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException or FormatException)
        {
            Dialogs.Error(this, $"{Path.GetFileName(dlg.FileName)} could not be read as NBT:\n\n{ex.Message}");
        }
        return Task.CompletedTask;
    }

    /// <summary>A resource pack (file or folder) checked for what the game would ignore or show wrong; the pack is only read.</summary>
    async Task CheckPack()
    {
        int c = Dialogs.Choose(this, "Check a pack", "Find what the game would ignore or show wrong in a resource pack — Java Edition (any version) or Bedrock — before you share it: names it cannot load, folders of the wrong version, broken JSON, odd-sized textures, manifest mistakes. The pack is only read.",
            "A pack file (.zip, .mcpack)…", "A pack folder…");
        string? path = null;
        if (c == 0)
        {
            using var dlg = new OpenFileDialog { Filter = "Resource packs|*.zip;*.mcpack|All files|*.*", Title = "Check a pack" };
            if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.FileName;
        }
        else if (c == 1)
        {
            using var dlg = new FolderBrowserDialog { Description = "The pack's folder (the one with pack.mcmeta or manifest.json in it)", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.SelectedPath;
        }
        if (path == null) return;
        Core.Packs.PackReport? report = null;
        if (!await RunAsync("Checking " + Path.GetFileName(path), _ => report = Core.Packs.PackCheck.Run(path)) || report == null) return;
        SetStatus($"{Path.GetFileName(path)}: {report.Problems} problem(s), {report.Warnings} warning(s).");
        Dialogs.ShowText(this, "Pack check — " + Path.GetFileName(path), report.Text());
    }

    public async Task OpenPath(string path)
    {
        // Skins and packs open in their studios; they are not worlds, so they stay out of the recent worlds.
        string kindExt = Path.GetExtension(path).ToLowerInvariant();
        if (File.Exists(path) && (kindExt == ".png" || kindExt == ".mcpack" && !TextureDocument.LooksLikePack(path)))
        {
            SkinDocument.OpenFile(this, path);
            return;
        }
        if (kindExt is ".mcpack" or ".zip" && File.Exists(path) && TextureDocument.LooksLikePack(path) || Directory.Exists(path) && TextureDocument.LooksLikePack(path))
        {
            TextureDocument.OpenFile(this, path);
            return;
        }
        // Structures and schematics open in their own window (it can hand the raw file to the NBT editor); not worlds either.
        if (File.Exists(path) && kindExt is ".schem" or ".litematic" or ".mcstructure" or ".schematic" or ".nbt"
            && await Task.Run(() => Core.Structures.StructureFile.IsStructure(path)))
        {
            await OpenStructureFile(path);
            return;
        }
        _settings.AddRecent(path);
        BuildRecent();
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (File.Exists(path) && ext is ".mca" or ".mcr" or ".mcc")
        {
            OpenDocument(new RegionDocument(path) { Key = path });
            return;
        }
        var existing = _openWorlds.FirstOrDefault(w => string.Equals(w.Location, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { OpenWorldDocument(existing); return; }
        var kind = await Task.Run(() => WorldLoader.Detect(path));
        // A world too damaged to be recognised (a Bedrock database without its index, a package that does not unpack):
        // offer the rescue instead of only saying what it is not.
        if (kind == WorldKind.Unknown && (WorldRescue.LooksLikeWorld(path) || File.Exists(path) && ext is ".mcworld" or ".mctemplate"))
        {
            await OfferRescue(path, new Core.IO.SaveFormatException("It is not recognised as a world any more: the files that say what it is are damaged or missing."));
            return;
        }
        if (kind == WorldKind.Unknown && File.Exists(path))
        {
            // Any other file: try it as NBT.
            try
            {
                OpenDocument(new NbtDocument(new FileNbtSource(path)) { Key = path });
                return;
            }
            catch (Exception ex) { Dialogs.Error(this, $"{Path.GetFileName(path)} is not a Minecraft world or NBT file:\n\n{ex.Message}"); return; }
        }
        if (kind == WorldKind.Unknown) { Dialogs.Error(this, $"\"{path}\" is not a Minecraft world this tool recognises."); return; }
        SaveWorld? world = null;
        Exception? failure = null;
        await RunAsync("Opening " + Path.GetFileName(path), progress => { world = WorldLoader.Open(path, progress); }, ex => failure = ex);
        if (failure != null) { await OfferRescue(path, failure); return; }
        if (world == null) return;
        _openWorlds.Add(world);
        PopulateTree();
        OpenWorldDocument(world);
        SetStatus($"Opened {world.Name} — {world.FormatDescription}" + UnpackedNote(path, world));
    }

    WorldDocument? OpenWorldDocument(SaveWorld w)
    {
        var existing = _docs.OfType<WorldDocument>().FirstOrDefault(d => d.World == w);
        if (existing != null) { Select(existing); return existing; }
        var doc = new WorldDocument(w);
        OpenDocument(doc);
        return doc;
    }

    public void OpenLevelDat(SaveWorld w)
    {
        switch (w)
        {
            case JavaWorld jw when jw.Level != null:
                OpenDocument(new NbtDocument(new FileNbtSource(Path.Combine(jw.Folder, "level.dat"))) { Key = (w, "level") });
                break;
            case BedrockWorld bw:
                OpenDocument(new NbtDocument(new DelegateNbtSource("level.dat", () => bw.Level.Roots, r => { Backup.WorldBeforeSave(bw); bw.SaveLevelDat(); }, $"{bw.Folder}\\level.dat  ·  {bw.Level.Describe()}")) { Key = (w, "level") });
                break;
            case ThreeDsWorld tw:
                OpenDocument(new NbtDocument(new DelegateNbtSource("level.dat", () => tw.Level.Roots, r => { Backup.WorldBeforeSave(tw); tw.SaveLevelDat(); }, $"{tw.Folder}\\level.dat")) { Key = (w, "level") });
                break;
            case PocketLegacyWorld pw when pw.Level != null:
                OpenDocument(new NbtDocument(new DelegateNbtSource("level.dat", () => pw.Level.Roots, r => { Backup.WorldBeforeSave(pw); pw.SaveLevel(); }, $"{pw.Folder}\\level.dat")) { Key = (w, "level") });
                break;
            case PocketLegacyWorld pw when pw.RawLevel != null:
                Dialogs.Info(this, $"This Pocket Edition 0.1 world uses a raw binary level.dat:\n\nName: {pw.RawLevel.Name}\nSeed: {pw.RawLevel.Seed}\nSpawn: {pw.RawLevel.SpawnX}, {pw.RawLevel.SpawnY}, {pw.RawLevel.SpawnZ}");
                break;
            case FiniteWorld fw when fw.Indev != null:
                OpenDocument(new NbtDocument(new FileNbtSource(fw.Location)) { Key = (w, "level") });
                break;
            case FiniteWorld fw when fw.Classic != null:
                Dialogs.ShowText(this, "Classic level", fw.Classic.Format == ClassicFormat.Serialized ? fw.Classic.DescribeSerialized() : fw.Classic.Describe());
                break;
            case ConsoleWorld cw when cw.Level != null:
                OpenDocument(new NbtDocument(new DelegateNbtSource("level.dat", () => cw.Level.Roots, r => { cw.LevelModified = true; SaveConsole(cw); }, $"level.dat inside {Path.GetFileName(cw.FilePath)}")) { Key = (w, "level") });
                break;
            default:
                Dialogs.Info(this, "This world has no level.dat.");
                break;
        }
    }

    public void OpenPlayer(SaveWorld w, PlayerRecord p)
    {
        NbtSource source = w switch
        {
            BedrockWorld bw => new DelegateNbtSource(p.Name, () => [new NamedTag("", bw.ReadNbt(p.Source) ?? p.Data)], r => { Backup.WorldBeforeSave(bw); p.Data = (NbtCompound)r[0].Tag; bw.SavePlayer(p); bw.Save(Core.LevelDb.CommitMode.AppendLog); }, $"{p.Source} in {w.Name}"),
            JavaWorld jw when p.Source.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase) => new FileNbtSource(Path.Combine(jw.Folder, "level.dat")),
            JavaWorld => new FileNbtSource(p.Source),
            ConsoleWorld cw => new DelegateNbtSource(p.Name, () => [new NamedTag("", p.Data)], r =>
            {
                var entry = cw.Archive.Find(p.Source);
                if (entry != null) entry.Data = NbtBinary.Serialize(r[0].Tag, NbtFormat.Java, r[0].Name);
                SaveConsole(cw);
            }, $"{p.Source} inside {Path.GetFileName(cw.FilePath)}"),
            _ => new DelegateNbtSource(p.Name, () => [new NamedTag("", p.Data)], null, p.Source),
        };
        OpenDocument(new NbtDocument(source) { Key = (w, "player", p.Source, p.Name) });
    }

    void OpenArchiveFile(ConsoleWorld cw, ConsoleArchive.FileEntry entry)
    {
        if (entry.Name.EndsWith(".mcr", StringComparison.OrdinalIgnoreCase) || entry.Data.Length == 0)
        {
            OpenDocument(new HexDocument(entry.Name, entry.Data) { Key = (cw, entry.Name) });
            return;
        }
        try
        {
            var file = NbtFile.Parse(entry.Data, NbtFormat.Java);
            OpenDocument(new NbtDocument(new DelegateNbtSource(entry.Name, () => file.Roots, r =>
            {
                entry.Data = file.ToBytes();
                entry.Modified = ConsoleArchive.Now1601Ms();
                if (entry.Name.Equals("level.dat", StringComparison.OrdinalIgnoreCase)) cw.SetLevel(file);
                SaveConsole(cw);
            }, $"{entry.Name} inside {Path.GetFileName(cw.FilePath)}")) { Key = (cw, entry.Name) });
        }
        catch
        {
            OpenDocument(new HexDocument(entry.Name, entry.Data) { Key = (cw, entry.Name) });
        }
    }

    void SaveConsole(ConsoleWorld cw)
    {
        Backup.WorldBeforeSave(cw);
        cw.Save();
        SetStatus($"Saved {Path.GetFileName(cw.FilePath)} ({cw.Wrapper}{(cw.Package != null ? ", package rehashed" : "")})");
    }
}
