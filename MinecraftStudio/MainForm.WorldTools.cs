// MinecraftStudio/MainForm.WorldTools.cs: world tools for MainForm.
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
    // ------------------------------------------------------------------ world tools

    void ShowPhone()
    {
        using var dlg = new PhoneDialog(this);
        dlg.ShowDialog(this);
        _ = ScanWorlds();
    }

    /// <summary>A new world from a heightmap: the land's settings in their window, then the Convert window writes it as any edition.</summary>
    async Task NewWorldFromHeightmap()
    {
        HeightmapWorld? land;
        using (var dlg = new HeightmapWorldDialog())
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            land = dlg.World;
        }
        if (land != null) await ShowConvert(land);
    }

    public async Task ShowConvert(SaveWorld? source = null)
    {
        var world = source ?? SelectedWorld();
        // A world picked under "Worlds on this PC" is opened first.
        if (world == null && _tree.SelectedNode?.Tag is FoundWorld f) world = await LoadWorld(f.Path);
        using var dlg = new ConvertDialog(world, _openWorlds, _found, LoadWorld, _settings.LastConvertTarget);
        dlg.ShowDialog(this);
        if (dlg.LastTarget != null && dlg.LastTarget != _settings.LastConvertTarget)
        {
            _settings.LastConvertTarget = dlg.LastTarget;
            _settings.Save();
        }
        _ = ScanWorlds();
        if (dlg.OpenAfterClose is string path) await OpenPath(path);
    }

    /// <summary>
    /// Rescues a damaged world: the rescue dialog reads what can still be read and saves it as a repaired copy, or hands
    /// it to the converter. The damaged world itself is never changed, and the rescued world is never opened for editing.
    /// </summary>
    /// <summary>
    /// Every world on this PC and the packs it has; choosing one opens it on its Packs section. With a pack file, that
    /// pack is offered for installing into the chosen world.
    /// </summary>
    public async Task ShowPacksOverview(string? installPack = null)
    {
        string? open;
        using (var dlg = new PacksOverviewDialog(_found, installPack)) { dlg.ShowDialog(this); open = dlg.OpenWorld; }
        if (open == null) return;
        await OpenPath(open);
        string full = Path.GetFullPath(open);
        if (_docs.OfType<WorldDocument>().FirstOrDefault(d => string.Equals(Path.GetFullPath(d.World.Location), full, StringComparison.OrdinalIgnoreCase)) is WorldDocument doc)
        {
            doc.ShowSection(WorldSection.Packs);
            if (installPack != null) await doc.InstallPack(installPack);
        }
    }

    /// <summary>Crops a block box out of a world and saves it as a structure .nbt (the world is only read).</summary>
    public async Task SaveStructure(SaveWorld? world = null)
    {
        var w = world ?? SelectedWorld();
        if (w == null && _tree.SelectedNode?.Tag is FoundWorld f) w = await LoadWorld(f.Path);
        if (w == null) { Dialogs.Info(this, "Open a world first, then crop a structure out of it."); return; }
        var selection = _docs.OfType<WorldDocument>().FirstOrDefault(d => d.World == w)?.MapSelectionBox();
        using var dlg = new StructureSaveDialog(w, selection);
        dlg.ShowDialog(this);
    }

    /// <summary>Places a saved structure into a new copy of a world; the world placed into is never changed.</summary>
    public async Task PlaceStructure(SaveWorld? world = null)
    {
        var w = world ?? SelectedWorld();
        if (w == null && _tree.SelectedNode?.Tag is FoundWorld f) w = await LoadWorld(f.Path);
        if (w == null) { Dialogs.Info(this, "Open the world you want to place a structure into first."); return; }
        if (w is ThreeDsWorld) { Dialogs.Info(this, Core.Structures.StructurePlacement.ThreeDsNotWritable); return; }
        string? openAfter;
        using (var dlg = new StructurePlaceDialog(w)) { dlg.ShowDialog(this); openAfter = dlg.OpenAfterClose; }
        _ = ScanWorlds();
        if (openAfter != null) await OpenPath(openAfter);
    }

    /// <summary>A world's maps as pictures, and map art made from any picture (added to the world after a backup).</summary>
    public async Task ShowMapArt(SaveWorld? world = null)
    {
        var w = world ?? SelectedWorld();
        if (w == null && _tree.SelectedNode?.Tag is FoundWorld f) w = await LoadWorld(f.Path);
        if (w == null) { Dialogs.Info(this, "Open a world first: its maps are shown, and map art is added to it."); return; }
        var doc = _docs.OfType<WorldDocument>().FirstOrDefault(d => d.World == w);
        using var dlg = new MapArtDialog(w, () => doc?.IsDirty == true);
        dlg.ShowDialog(this);
        if (dlg.Changed) doc?.PlayersChanged();
    }

    /// <summary>Replaces or fills blocks in a box, into a new copy of a world (the world itself is never changed).</summary>
    public async Task ReplaceBlocks(SaveWorld? world = null, (WorldDimension Dim, int MinX, int MinZ, int MaxX, int MaxZ)? box = null)
    {
        var w = world ?? SelectedWorld();
        if (w == null && _tree.SelectedNode?.Tag is FoundWorld f) w = await LoadWorld(f.Path);
        if (w == null) { Dialogs.Info(this, "Open the world you want to replace blocks in first."); return; }
        if (w is ThreeDsWorld) { Dialogs.Info(this, Core.Structures.StructurePlacement.ThreeDsNotWritable); return; }
        var selection = box ?? _docs.OfType<WorldDocument>().FirstOrDefault(d => d.World == w)?.MapSelectionBox();
        string? openAfter;
        using (var dlg = new ReplaceBlocksDialog(w, selection)) { dlg.ShowDialog(this); openAfter = dlg.OpenAfterClose; }
        _ = ScanWorlds();
        if (openAfter != null) await OpenPath(openAfter);
    }

    /// <summary>
    /// One structure or schematic file on its own: see it, count its materials, turn or mirror it, save it as another
    /// format or version (the file opened is never changed).
    /// </summary>
    public Task OpenStructureFile(string? path = null)
    {
        string? nbt;
        using (var dlg = new StructureFileDialog(path)) { dlg.ShowDialog(this); nbt = dlg.OpenNbtAfterClose; }
        if (nbt != null)
            try { OpenDocument(new NbtDocument(new FileNbtSource(nbt)) { Key = ("nbt", Path.GetFullPath(nbt).ToLowerInvariant()) }); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException or FormatException)
            {
                Dialogs.Error(this, $"{Path.GetFileName(nbt)} could not be read as NBT:\n\n{ex.Message}");
            }
        return Task.CompletedTask;
    }

    public async Task ShowRescue(string? path = null)
    {
        path ??= PickRescueSource();
        if (path == null) return;
        var open = _openWorlds.FirstOrDefault(w => string.Equals(Path.GetFullPath(w.Location), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (open != null && open.IsModified &&
            Dialogs.Ask(this, $"{open.Name} has unsaved changes in the studio. The rescue reads the world as it is saved on disk, without them.\n\nRescue it anyway?") != DialogResult.Yes) return;
        string? openAfter;
        using (var dlg = new RescueDialog(path))
        {
            dlg.ShowDialog(this);
            openAfter = dlg.OpenAfterClose;
            if (dlg.ConvertRequested && dlg.TakeWorld() is SaveWorld rescued)
            {
                using (rescued)
                {
                    // The converter starts on the world's own edition and version.
                    var own = await Task.Run(() => TargetCatalog.ClosestTo(rescued));
                    using var convert = new ConvertDialog(rescued, _openWorlds, _found, LoadWorld, own.Name, dlg.Findings);
                    convert.ShowDialog(this);
                    openAfter = convert.OpenAfterClose;
                }
            }
        }
        _ = ScanWorlds();
        if (openAfter != null) await OpenPath(openAfter);
    }

    /// <summary>The world to rescue: the one selected in the list, or a folder or file chosen here.</summary>
    string? PickRescueSource()
    {
        var found = _tree.SelectedNode?.Tag as FoundWorld;
        string? selected = SelectedWorld()?.Location ?? found?.Path;
        string? selectedName = SelectedWorld()?.Name ?? found?.Name;
        var options = new List<string>();
        if (selected != null) options.Add($"{selectedName}   ({selected})");
        options.Add("A world folder…");
        options.Add("A world file or package (.mcworld, .zip, level.dat, a console save)…");
        int pick = Dialogs.Choose(this, "Rescue a damaged world",
            "Which world? It is only read: what can still be read is saved as a new world, and the damaged world itself is never changed.", options.ToArray());
        if (pick < 0) return null;
        if (selected != null)
        {
            if (pick == 0) return selected;
            pick--;
        }
        if (pick == 0)
        {
            using var folder = new FolderBrowserDialog { Description = "Choose the damaged world's folder (the one with level.dat, db or region)", UseDescriptionForTitle = true };
            return folder.ShowDialog(this) == DialogResult.OK ? folder.SelectedPath : null;
        }
        using var file = new OpenFileDialog
        {
            Title = "Choose the damaged world's file or package",
            Filter = "Worlds and saves|*.mcworld;*.mctemplate;*.zip;level.dat;level.dat_old;*.dat;*.bin;*.mcs;*.mclevel;*.mine;GAMEDATA*;savegame*|All files|*.*",
        };
        return file.ShowDialog(this) == DialogResult.OK ? file.FileName : null;
    }

    /// <summary>A world that fails to open: the error, and a rescue when damage could be the reason.</summary>
    async Task OfferRescue(string path, Exception failure)
    {
        if (failure is NotSupportedException or UnauthorizedAccessException)
        {
            Dialogs.Error(this, $"Opening {Path.GetFileName(path)} failed:\n\n{failure.Message}");
            return;
        }
        if (Dialogs.Ask(this, $"Opening {Path.GetFileName(path)} failed:\n\n{failure.Message}\n\nIf the world is damaged, {Dialogs.AppName} can rescue what can still be read " +
                              "from it into a new copy. The damaged world itself is not changed.\n\nTry a rescue?", icon: MessageBoxIcon.Warning) == DialogResult.Yes)
            await ShowRescue(path);
    }

    /// <summary>Opens a world without opening its map tab (the converter's source picker).</summary>
    async Task<SaveWorld?> LoadWorld(string path)
    {
        var existing = _openWorlds.FirstOrDefault(w => string.Equals(w.Location, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var kind = await Task.Run(() => WorldLoader.Detect(path));
        if (kind == WorldKind.Unknown) { Dialogs.Error(this, $"\"{path}\" is not a Minecraft world this tool recognises."); return null; }
        SaveWorld? world = null;
        if (!await RunAsync("Opening " + Path.GetFileName(path), progress => { world = WorldLoader.Open(path, progress); }) || world == null) return null;
        _settings.AddRecent(world.Location);
        BuildRecent();
        _openWorlds.Add(world);
        PopulateTree();
        SetStatus($"Opened {world.Name} — {world.FormatDescription}" + UnpackedNote(path, world));
        return world;
    }

    /// <summary>A packed world (.mcworld, .zip) is opened from an unpacked copy: say where it is.</summary>
    static string UnpackedNote(string path, SaveWorld world) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(world.Location), StringComparison.OrdinalIgnoreCase) || !File.Exists(path)
            ? "" : $"  ·  unpacked from {Path.GetFileName(path)} into {world.Location} (the package itself is not changed)";

    /// <summary>
    /// The world as one file to send or keep: a Bedrock world as a .mcworld (opened, the game imports it — on Windows,
    /// phones and tablets), a Java world as a .zip with its folder inside (unzipped into saves, it plays). The world is only
    /// read; the file is written under a temporary name, checked, then given its name.
    /// </summary>
    public async Task ShareWorld(SaveWorld? world = null)
    {
        if ((world ?? SelectedWorld()) is not SaveWorld w) { Dialogs.Info(this, "Open or select a world first."); return; }
        if (!WorldShare.CanPack(w))
        {
            Dialogs.Info(this, "Java and Bedrock world folders can be saved as one file here; console saves and Classic levels are single files already.");
            return;
        }
        if (WorldUse.InUse(w) is string busy) { Dialogs.Info(this, busy + "\n\nClose it in the game first, so the file holds the world as it is saved."); return; }
        if (_docs.OfType<WorldDocument>().FirstOrDefault(d => d.World == w) is { IsDirty: true } &&
            Dialogs.Ask(this, $"{w.Name} has changes not saved yet in its tab. The file holds the world as it is saved on disk, without them.\n\nSave it as a file anyway?") != DialogResult.Yes) return;
        bool bedrock = w is BedrockWorld;
        using var dlg = new SaveFileDialog
        {
            Filter = bedrock ? "Bedrock world (*.mcworld)|*.mcworld" : "Zip file (*.zip)|*.zip",
            FileName = Core.IO.SafeName.For(w.Name, "World") + WorldShare.Extension(w),
            Title = bedrock ? "Save the world as a .mcworld (opened, Minecraft imports it)" : "Save the world as a .zip (unzip it into the game's saves folder)",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string target = dlg.FileName;
        long size = 0;
        bool ok = await RunAsync("Packing " + w.Name, _ => size = WorldShare.Pack(w, target));
        if (ok) SetStatus($"Saved {target} ({size / 1048576.0:0.#} MB)" + (bedrock ? ": open it to import the world into Minecraft." : ": unzip it into the game's saves folder."));
    }

    public async Task BackupNow(SaveWorld? world = null)
    {
        if ((world ?? SelectedWorld()) is not SaveWorld w) { Dialogs.Info(this, "Open or select a world first."); return; }
        string? zip = null;
        await RunAsync("Backing up " + w.Name, progress => zip = Backup.World(w, progress));
        if (zip != null) SetStatus("Backup written: " + zip);
    }

    /// <summary>A world's backups: back up now, restore one as a new world, compare with one (to bring chunks back).</summary>
    public async Task ShowBackups(SaveWorld? world = null)
    {
        var w = world ?? SelectedWorld();
        if (w == null && _tree.SelectedNode?.Tag is FoundWorld f) w = await LoadWorld(f.Path);
        if (w == null) { Dialogs.Info(this, "Open or select a world first."); return; }
        string? openAfter;
        using (var dlg = new BackupsDialog(w, _docs.OfType<WorldDocument>().FirstOrDefault(d => d.World == w))) { dlg.ShowDialog(this); openAfter = dlg.OpenAfterClose; }
        if (openAfter != null) { await ScanWorlds(); await OpenPath(openAfter); }
    }

    public async Task Optimize(SaveWorld? world = null)
    {
        if ((world ?? SelectedWorld()) is not SaveWorld w) { Dialogs.Info(this, "Open or select a world first."); return; }
        if (Dialogs.Ask(this, $"Rewrite {w.Name}'s storage compactly?\n\n• Java: region files are rewritten without free space and empty regions are removed.\n• Bedrock: the database is rebuilt into fresh tables.\n\nA backup is made first.") != DialogResult.Yes) return;
        long before = 0, after = 0;
        await RunAsync("Optimizing " + w.Name, progress =>
        {
            Backup.WorldBeforeSave(w, progress);
            before = FolderSize(w.Location);
            switch (w)
            {
                case JavaWorld jw:
                    foreach (var d in jw.Dimensions.OfType<JavaRegionDimension>())
                        foreach (var f in d.RegionFiles.ToList())
                        {
                            progress.Report("Compacting " + Path.GetFileName(f));
                            lock (d) RegionFile.Open(f).Save();
                        }
                    break;
                case BedrockWorld bw:
                    if (Core.LevelDb.LevelDatabase.IsLocked(bw.Db.Directory_)) throw new IOException("The world is open in Minecraft.");
                    bw.Save(Core.LevelDb.CommitMode.Compact, progress);
                    break;
                case ConsoleWorld cw:
                    cw.Save();
                    break;
                default:
                    throw new NotSupportedException("Optimizing is available for Java, Bedrock and console saves.");
            }
            after = FolderSize(w.Location);
        });
        foreach (var d in _docs.OfType<WorldDocument>().Where(d => d.World == w)) d.ReloadFacts();
        SetStatus($"Optimized {w.Name}: {Core.IO.BinaryUtil.FormatSize(before)} → {Core.IO.BinaryUtil.FormatSize(after)}");
    }

    static long FolderSize(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    public async Task CheckWorld(SaveWorld? world = null)
    {
        if ((world ?? SelectedWorld()) is not SaveWorld w) { Dialogs.Info(this, "Open or select a world first."); return; }
        var report = new System.Text.StringBuilder();
        bool damaged = false;
        await RunAsync("Checking " + w.Name, progress =>
        {
            report.AppendLine($"{w.Name} — {w.FormatDescription}");
            report.AppendLine();
            foreach (var d in w.Dimensions)
            {
                int ok = 0, bad = 0;
                var positions = d.ChunkPositions.ToList();
                for (int i = 0; i < positions.Count; i++)
                {
                    if ((i & 255) == 0) progress.Report($"{d.DisplayName}: {i:N0}/{positions.Count:N0} chunks");
                    try { lock (d) { var c = d.LoadChunk(positions[i]); if (c != null) foreach (var s in c.Sections.Values) _ = s.Indices; } ok++; }
                    catch (Exception ex) { bad++; if (bad <= 50) report.AppendLine($"  {d.DisplayName} chunk {positions[i]}: {ex.Message}"); }
                }
                report.AppendLine($"{d.DisplayName}: {ok:N0} chunks read correctly, {bad:N0} damaged.");
                if (bad > 0) damaged = true;
                if (d is JavaRegionDimension jd)
                    foreach (var f in jd.RegionFiles)
                        foreach (var issue in RegionFile.Open(f).Validate(false))
                        {
                            report.AppendLine($"  {Path.GetFileName(f)} slot {issue.Index}: {issue.Message}");
                            damaged |= issue.Fatal;
                        }
            }
            if (w is BedrockWorld bw)
            {
                report.AppendLine();
                report.AppendLine($"Database: {bw.Db.Count:N0} records, {bw.Db.Manifest.Files.Count} tables, {bw.Db.CorruptLogFragments} damaged log fragments.");
                foreach (var warn in bw.Db.Warnings) report.AppendLine("  " + warn);
                damaged |= bw.Db.Warnings.Count > 0 || bw.Db.CorruptLogFragments > 0;
            }
            if (w is ConsoleWorld cw) foreach (var warn in cw.Warnings) report.AppendLine("  " + warn);
            if (damaged)
            {
                report.AppendLine();
                report.AppendLine("World → Rescue a Damaged World saves everything that can still be read into a new copy, including chunks the");
                report.AppendLine("region headers have lost and older copies of damaged chunks. The world itself is not changed.");
            }
        });
        Dialogs.ShowText(this, "World check — " + w.Name, report.ToString());
    }

    void ShowSettings()
    {
        using var f = new DarkDialog("Settings", 560);
        var panel = new Panel { Dock = DockStyle.Top, AutoSize = true };
        var form = new FormBuilder(panel);
        var backup = form.Check("Back up before saving", _settings.BackupBeforeSave);
        var auto = form.Check("Back up each world by itself after a play session (while this program is open)", _settings.AutoBackup);
        var keep = form.Text("Automatic backups kept per world (0: all; older ones go to the Recycle Bin)", _settings.AutoBackupKeep.ToString());
        var folder = form.Text("Backup folder", _settings.BackupFolder);
        var extra = form.Text("Extra world folders (;)", string.Join(";", _settings.ExtraWorldFolders));
        f.AddButtons(("OK", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        f.Controls.Add(panel);
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _settings.BackupBeforeSave = backup.Checked;
        _settings.AutoBackup = auto.Checked;
        _settings.AutoBackupKeep = int.TryParse(keep.Text.Trim(), out int k) && k >= 0 ? k : 0;
        _settings.BackupFolder = folder.Text.Trim();
        _settings.ExtraWorldFolders = extra.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _settings.Save();
        _ = ScanWorlds();
    }

    static string EditionName(WorldKind k) => k switch
    {
        WorldKind.Java => "Java Edition",
        WorldKind.JavaFinite => "Classic / Indev",
        WorldKind.Bedrock => "Bedrock Edition",
        WorldKind.PocketLegacy => "Pocket Edition",
        WorldKind.ThreeDS => "New Nintendo 3DS",
        WorldKind.LegacyConsole => "Legacy Console",
        _ => k.ToString(),
    };

    /// <summary>Start page: the worlds opened most recently, as cards.</summary>
    void RefreshRecentCards()
    {
        _recentCards.SuspendLayout();
        foreach (var old in _recentCards.Controls.Cast<Control>().ToList()) old.Dispose();
        foreach (var path in _settings.Recent.Where(p => Directory.Exists(p) || File.Exists(p)).Take(9))
        {
            var f = _found.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) ?? WorldFinder.Describe(path, "Recent");
            if (f == null) continue;
            string target = path;
            var card = new WorldCard
            {
                Text = f.Name, Detail = f.Detail ?? EditionName(f.Kind), PathText = f.Path, Image = _bigIcons.Images[IconFor(f.Kind)],
                Size = new Size(this.Px(320), this.Px(70)), Margin = new Padding(0, 0, this.Px(10), this.Px(10)), Font = Theme.Ui(9f),
                AccessibleName = $"Open {f.Name}",
            };
            card.Click += async (_, _) => await OpenPath(target);
            _recentCards.Controls.Add(card);
        }
        _recentHeader.Visible = _recentCards.Controls.Count > 0;
        _recentCards.ResumeLayout();
    }

    void BuildRecent()
    {
        _recentMenu.DropDownItems.Clear();
        foreach (var r in _settings.Recent)
        {
            string path = r;
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem(path, null, async (_, _) => await OpenPath(path)) { ForeColor = C.FG });
        }
        _recentMenu.Enabled = _settings.Recent.Count > 0;
    }
}
