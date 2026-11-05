// MinecraftStudio/MainForm.Window.cs: window for MainForm.
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
    // ------------------------------------------------------------------ status and background work

    public void SetStatus(string text) => _statusText.Text = text;

    public void SetProgress(double? fraction)
    {
        _progress.Visible = fraction != null;
        if (fraction is double f) _progressBar.Value = f;
    }

    /// <summary>Runs work off the UI thread with status-bar progress; errors are shown in a dialog unless <paramref name="onError"/> takes them.</summary>
    public async Task<bool> RunAsync(string title, Action<IProgress<string>> work, Action<Exception>? onError = null)
    {
        if (_busy) { Dialogs.Info(this, "Another operation is still running."); return false; }
        _busy = true;
        UseWaitCursor = true;
        SetStatus(title + "…");
        _progressBar.Marquee = true;
        _progress.Visible = true;
        var progress = new Progress<string>(s => SetStatus(s));
        try
        {
            await Task.Run(() => work(progress));
            return true;
        }
        catch (Exception ex)
        {
            if (onError != null) onError(ex);
            else Dialogs.Error(this, $"{title} failed:\n\n{ex.Message}");
            SetStatus(title + " failed.");
            return false;
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            _progressBar.Marquee = false;
            _progress.Visible = false;
        }
    }

    // ------------------------------------------------------------------ keys and window state

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S: _ = SaveActive(); return true;
            case Keys.Control | Keys.O: _ = OpenFolderDialog(); return true;
            case Keys.Control | Keys.Shift | Keys.O: _ = OpenFileDialog(); return true;
            case Keys.Control | Keys.W: CloseActive(); return true;
            case Keys.Control | Keys.Tab: _tabs.SelectNext(false); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: _tabs.SelectNext(true); return true;
            case Keys.F5: _ = ScanWorlds(); return true;
            case Keys.Control | Keys.Shift | Keys.C: _ = ShowConvert(); return true;
            case Keys.Control | Keys.Shift | Keys.K: SkinDocument.Start(this); return true;
            case Keys.Control | Keys.Shift | Keys.T: TextureDocument.Start(this); return true;
            case Keys.Control | Keys.Shift | Keys.M: MobDocument.Start(this); return true;
            case Keys.Control | Keys.Shift | Keys.A: AnimationDocument.Start(this); return true;
            case Keys.Control | Keys.F when _tabs.Selected?.Tag is WorldDocument world: world.ShowFind(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void RestoreWindow()
    {
        if (_settings.WindowWidth > 400 && _settings.WindowHeight > 300)
        {
            var r = new Rectangle(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = r;
            }
        }
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (var d in _docs.ToList())
            if (!d.ConfirmClose()) { e.Cancel = true; return; }
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = b.X; _settings.WindowY = b.Y; _settings.WindowWidth = b.Width; _settings.WindowHeight = b.Height;
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        _settings.Save();
        _autoBackups?.Dispose();
        foreach (var w in _openWorlds) w.Dispose();
        base.OnFormClosing(e);
    }

    /// <summary>Help → What's New: where to find the newer tools.</summary>
    const string WhatsNew =
        "WHAT WORKS WHERE (Help menu, start page)\n" +
        "  Every feature against every platform: Java (PC), Bedrock on every device, Pocket 0.x, Xbox 360, Xbox One, PS3, PS4, PS Vita,\n" +
        "  Wii U, Switch and the 3DS. Show for your console lists only what works there. Every menu item says where it works, and a\n" +
        "  tool the open world's edition doesn't have is switched off with the reason. The same list: FEATURES.md.\n" +
        "  Copy items to another world (Players page): a player's inventory, armour and ender chest from any edition or console\n" +
        "  into a Java or Bedrock world — your Xbox 360 world's things in your new Java world.\n" +
        "  Rename a world (Cheats & game mode): the name in the game's world list, for Java, Bedrock, Pocket and the 3DS.\n" +
        "  More now works on more platforms: Move to safety on every console save, Pocket 0.x and Indev (with what a console\n" +
        "  player rides); a console save's own villages, temples, mansions, strongholds, ocean ruins, fortresses and end cities\n" +
        "  on the map, and its Nether portals' links (its Nether is 1:3); a console chunk's NBT (its mobs and chests) edited and\n" +
        "  saved back from the NBT editor; items copied into console and Pocket 0.x saves too; a 3DS world's player (to look\n" +
        "  at, and to copy their things into another world) and its maps; Pocket 0.x players' items now come across in the\n" +
        "  slots they were in (the hotbar's links were read as items before); Classic and Indev\n" +
        "  levels: set the spawn from the map, shape the land, and bring chunks back from a backup.\n\n" +
        "MODS AND ADD-ONS (World menu, map Tools, Overview)\n" +
        "  Which mods a world needs before you open it: every block, mob, item, biome and dimension of each mod (Forge, NeoForge,\n" +
        "  Fabric, Quilt), add-on or data pack, counted; the mod list Forge saves; and which launcher on this PC (official,\n" +
        "  CurseForge, Prism, Modrinth, MultiMC, GDLauncher, ATLauncher) has each mod's file. A mod that isn't on this PC is shown in\n" +
        "  orange: without it the game would turn its blocks into air. Find its things, or mark its chunks on the map.\n\n" +
        "BUILDINGS (World menu, start page)\n" +
        "  Crop and place on a map: drag a box around a building (pull its edges, drag it about) and its heights follow the ground and\n" +
        "  roofs there; or click where a structure should go (it is shown there from above, turned as chosen) and it stands on the ground.\n" +
        "  Turn and mirror a structure when placing it, or in the Structure File window (any .nbt .schem .litematic .mcstructure .schematic):\n" +
        "  previews, the material list (copy, or save as text or CSV), save as another format or Java version, or as a 3D model (.obj) for Blender.\n" +
        "  Saving a structure reads the file back and lists any block the format or version doesn't have.\n" +
        "  3D view: click a structure's picture (Place, Structure File) or right-click the map → See this in 3D, and turn it, zoom in,\n" +
        "  and look from every side.\n" +
        "  Check a build (Place a Structure → Check the build): a schematic laid over your build shows every missing or wrong block\n" +
        "  on the map, how much is done, and the material list of what is still to gather.\n" +
        "  Replace or fill blocks in an area (*_ore → stone, * for every block), or change its biome — into a copy, checked. Map right-click too.\n" +
        "  Shape the land (the same window): flatten an area for a build, smooth it, raise or lower it, with soft edges — trees, chests and\n" +
        "  mobs move with the ground and water keeps its level; Count shows how far it would move first.\n\n" +
        "WORLDS\n" +
        "  Structures on the map (map toolbar, World menu): villages, outposts, shipwrecks, buried treasure, temples, monuments, strongholds,\n" +
        "  mansions, fortresses, bastions, end cities, ancient cities, trail ruins, trial chambers, dungeons … in every edition; a gold ring\n" +
        "  where loot has never been taken. The Structures window lists them (only those with loot left, for treasure hunting).\n" +
        "  Nether portals you built are among them, each saying where it leads: to which portal on the other side, whether that one\n" +
        "  leads back, or that it makes a new portal (and where) — the usual reason a portal takes you somewhere unexpected.\n" +
        "  Slime chunks on the map (Overlay): from the world's seed in Java, Bedrock's fixed ones — also where nobody has been yet.\n" +
        "  Web map (map toolbar, Export): the whole dimension as a zoomable map that opens in any browser, even offline — with the\n" +
        "  spawn, the players and the structures marked. Put the folder on a website or a USB stick and share it.\n" +
        "  Heights (map toolbar, Export): the ground of a dimension as a 16-bit grey picture for terrain programs — and New World from a\n" +
        "  Heightmap (Create menu) turns any grey picture or real elevation data into land (grass, beaches, cliffs, snowy peaks, water)\n" +
        "  as any edition and version, checked block by block; a saved heights file comes back exactly, where it was.\n" +
        "  Lag Machines: the devices built to make a world or server lag or crash — minecart and mob piles, item floods, solid blocks of\n" +
        "  pistons or observers, machines left running, summoning command blocks, book and chunk bans — never ordinary redstone or farms;\n" +
        "  Clean up takes them out of a copy (piles thinned to what fits in a block, named pets kept) and checks none is left.\n" +
        "  Map Art: every map of a world as a picture (PNG), and any picture made into map art (1–16 maps across and down), given to a player;\n" +
        "  or made from blocks you can place in survival (with steps or flat) and saved as schematics to build, with the material list.\n" +
        "  Busiest Chunks (lag finder): where the mobs, dropped items, hoppers and chests pile up — marked on the map; clear their\n" +
        "  dropped items and experience orbs in a copy.\n" +
        "  Backups (World menu, Overview): every backup of a world, newest first — restore one as a new world beside it, or compare\n" +
        "  with it; switch on automatic backups (Settings) and each world is backed up by itself after you play it.\n" +
        "  Compare with Another World: what changed since a backup, or what a conversion changed — marked on the map; then bring\n" +
        "  those chunks (or the ones you pick) back from the backup into a copy: griefing, a creeper hole or a bad update undone.\n" +
        "  Players page: right-click an item → Copy /give command (or the item as SNBT); \"Single-player and server…\" moves a player's\n" +
        "  things between level.dat and a server's player files (offline-mode names, online UUIDs). \"Move to safety…\" puts a player\n" +
        "  stuck in a wall, lost in the void or trapped in the Nether on safe ground (beside their bed, at the spawn or near x, z;\n" +
        "  on the map: right-click a spot → Move a player here).\n" +
        "  Statistics and advancements (Players page, Java): every statistic (change any), every advancement with its title from your\n" +
        "  installed Minecraft and the world's data packs — mark one, or all, done or not done.\n" +
        "  Lost pets (Find → Only pets): every tamed dog, cat, parrot and horse with who owns it; Bring the pets home puts them\n" +
        "  beside their owners in a copy. Villagers show their job and trades. The map marks where each player last died.\n" +
        "  Overview: right-click the facts to copy the seed or anything else.\n\n" +
        "CREATE\n" +
        "  New Data Pack or Add-on: set up right for any Java version (1.13 → 26.3) or Bedrock version; with examples of a recipe, a\n" +
        "  loot table and an advancement written the way that version reads them.\n" +
        "  Check a Pack: resource packs and now data packs too — wrong folder names for the version, functions that won't load\n" +
        "  (a line starting with /, a misspelt command), load and tick tags naming functions that aren't there.\n" +
        "  Texture Studio: the texture you paint shows on its block in 3D as you paint (ON A BLOCK); Models opens any Java block or\n" +
        "  item model with your pack's textures — change its boxes, faces and textures and save it into the pack.\n\n" +
        "COMMAND LINE (for scripts and builds)\n" +
        "  mcstudio.exe (beside the app when built from MinecraftStudio.Cli): info, targets, convert, find, structures, lag, restore, replace, crop, maps, share,\n" +
        "  structure, materials, obj, biome, shape (flatten or smooth land), heights, heightmap (a new world from one), move (a stuck\n" +
        "  player to safety, in a copy), packcheck, newpack. Run mcstudio help.\n\n" +
        "CONVERSIONS\n" +
        "  Change blocks as it converts (Convert window): say what a mod's blocks, or any blocks, become in the new world\n" +
        "  (thermal:ore → copper_ore, *_ore → stone) — kept for next time; the check after converting knows about them.\n" +
        "  Paintings keep their art, facing and place between every Java version and Bedrock; mobs keep their drop chances, beds,\n" +
        "  homes and the rest across Java 1.21.5; mod dimensions get names the game can use. A horse, boat or minecart a player\n" +
        "  was riding comes along (still ridden in Java; elsewhere it stands where they were), as do the parrots on their\n" +
        "  shoulders and their thrown ender pearls. Between old and new versions mobs keep what they ride, villagers their jobs,\n" +
        "  armour stands their armour, mobs their potion effects, beacons their powers, and donkeys, zombie villagers, wither\n" +
        "  skeletons and elder guardians stay what they are. Java to Java also brings each player's advancements and\n" +
        "  statistics, the scoreboard, data packs, the world border and the world's icon.";

    const string SupportedFormats =
        "JAVA EDITION\n" +
        "  Pre-Classic and Classic levels (raw, format 1, serialized Level objects; .dat/.mine/server_level.dat)\n" +
        "  Indev .mclevel, Infdev/Alpha chunk folders, McRegion (.mcr), Anvil (.mca) 1.2 → 26.x incl. 1.18 height, 26.1 folder layout\n" +
        "  Region compression: gzip, zlib, none, LZ4 (24w04a+), external .mcc chunks\n" +
        "  level.dat, players, data/*.dat, entities/ and poi/ regions\n\n" +
        "BEDROCK / POCKET EDITION\n" +
        "  Pocket Edition 0.1 (raw level.dat, chunks.dat), 0.2–0.8 and Pi Edition (chunks.dat, entities.dat)\n" +
        "  LevelDB worlds 0.9 → 26.x: LegacyTerrain, sub-chunks v0–v9, Data2D/Data3D, actor storage\n" +
        "  Own LevelDB reader/writer (zlib/raw-deflate tables, bloom filters), append or compact\n\n" +
        "NEW NINTENDO 3DS EDITION\n" +
        "  Extdata world dumps (Checkpoint/JKSM): versions 1.0–1.9, read; level.dat editable\n\n" +
        "LEGACY CONSOLE EDITION\n" +
        "  Xbox 360: STFS packages (read + rehash), savegame.dat, .mcs, XMemCompress LZX\n" +
        "  PS3 (decrypted GAMEDATA), PS Vita, Wii U, PS4, Xbox One, Switch wrappers and split region files\n" +
        "  Chunk formats: NBT 128/256, binary v8–v11 grids, v12/v13 Aquatic sections\n\n" +
        "NBT\n" +
        "  Java big-endian, Bedrock little-endian, Bedrock network varint; gzip/zlib/raw; multi-root files; SNBT\n\n" +
        "CONVERSION (World → Convert World…, any source above to any target below)\n" +
        "  Java: 26.1 · 1.21.x · 1.20.x · 1.19.x · 1.18 · 1.17 · 1.16 · 1.15 · 1.14 · 1.13 · 1.12 → 1.2 (Anvil) · 1.1 → Beta 1.3 (McRegion)\n" +
        "        · Beta 1.2 → Infdev (chunk folders)\n" +
        "  Bedrock: 1.21.120 – 26.x · 1.21.40 · 1.21.0 · 1.20 · 1.19.40 · 1.18.30 · 1.18.0 · 1.17 · 1.16 · 1.13 – 1.15 · 1.4 – 1.12 ({name, val})\n" +
        "        · 1.2.13 – 1.3 · 1.1 – 1.2.10 (numeric) · .mcworld export\n" +
        "  Pocket Edition: 0.9 – 0.16 (LegacyTerrain) · 0.2 – 0.8 and Pi Edition (chunks.dat, 256 × 256)\n" +
        "  Legacy Console: Xbox 360 (into your own STFS package, rehashed), PS3, Wii U, PS Vita, PS4, Xbox One, Switch\n" +
        "  Classic 0.0.13a – 0.30 (.dat) and ClassiCube (.cw) · Indev (.mclevel)\n" +
        "  Blocks, biomes, chests and other block data, mobs, item frames, signs, banners and players are carried over;\n" +
        "  anything a target version lacks becomes the closest block it has. The original world is never changed.\n";
}
