// tools/ui-smoke/Clicks.cs: the "press every button" pass: a real MainForm (never shown) hosts the start page, every studio and world copies;
// Clicker presses everything in them while Answerer answers the dialogs. The Cheats page's hardcore restore and the
// Achievements Enabled box are driven on purpose and checked on disk, on copies (never the real saves).
using System.Reflection;
using MinecraftStudio;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

static class Clicks
{
    static readonly BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run(Action<bool, string> check, string temp, string? realDead)
    {
        // Dialog answers that go deeper than Cancel (all in memory; nothing here saves anywhere but the copies).
        Answerer.Rules.Add(s => s.Title == "Animation Studio" && s.Kind == "form" ? new("Design mobs of") : null);
        Answerer.Rules.Add(s => s.Title == "New animation pack" ? new("OK", "Smoke Anims") : null);
        Answerer.Rules.Add(s => s.Title == "Mob Studio" && s.Kind == "form" ? new("Paint") : null);
        Answerer.Rules.Add(s => s.Title == "New mob pack" ? new("OK", "Smoke Mobs") : null);
        Answerer.Rules.Add(s => s.Title == "New animation" ? new("OK", "smoke") : null);
        Answerer.Rules.Add(s => s.Title == "Copy the animation" ? new("OK", "smoke copy") : null);
        Answerer.Rules.Add(s => s.Title == "Rename the animation" ? new("OK", "smoke renamed") : null);
        Answerer.Rules.Add(s => s.Title == "Add a bone" ? new("OK", "smoke_bone") : null);
        Answerer.Rules.Add(s => s.Title == "Resize" ? new("OK", "50%") : null);
        Answerer.Rules.Add(s => s.Kind == "message" && (s.Text.StartsWith("Remove the bone") || s.Text.StartsWith("Remove the animation") || s.Text.StartsWith("Replace the")) ? new("Yes") : null);
        // Closing a test document: its changes are not saved.
        Answerer.Rules.Add(s => s.Kind == "message" && s.Text.StartsWith("Save changes to") ? new("No") : null);

        var main = new MainForm([]) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
        _ = main.Handle;
        Clicker.Main = main;
        Answerer.AddHost(main);
        Application.ThreadException += (_, e) => Clicker.Late.Add(e.Exception);
        var docs = (List<Document>)typeof(MainForm).GetField("_docs", Any)!.GetValue(main)!;
        var select = typeof(MainForm).GetMethod("Select", Any, [typeof(Document)])!;
        void Show(Document d) { select.Invoke(main, [d]); d.Visible = true; Clicker.Pump(100); }

        // ---- the start page: every tile (the phone's is skipped: it starts adb)
        var welcome = (Control)typeof(MainForm).GetField("_welcome", Any)!.GetValue(main)!;
        Clicker.Everything(welcome, "start page", check, budget: 60);
        Console.WriteLine($"  documents opened from the start page: {string.Join(", ", docs.Select(d => d.Title))}");

        // ---- the studios
        Studio(new SkinDocument(new Skin(new RgbaImage(64, 64), SkinModel.Classic) { Name = "Smoke" }, null), "Skin Studio");
        if (TexturePackReader.ReadBedrockGame(TextureNames.Default) is { } game)
            Studio(new TextureDocument(new TexturePack { Name = "Smoke" }, game, null), "Texture Studio");
        if (MobCatalog.Bedrock() is { } bedrock)
        {
            Studio(new MobDocument(new TexturePack { Name = "Mobs", Edition = PackEdition.Bedrock }, bedrock, null), "Mob Studio (Bedrock)");
            var jars = GameFolders.JavaJars();
            if (jars.Count > 0)
            {
                var java = MobCatalog.Java(jars[0].Jar, jars[0].Version);
                java.BorrowModels(bedrock);
                Studio(new MobDocument(new TexturePack { Name = "Mobs", Edition = PackEdition.Java, JavaRelease = java.Textures.JavaRelease }, java, null), "Mob Studio (Java)");
            }
            Studio(new AnimationDocument(MobCatalog.Bedrock()!, "Smoke Anims"), "Animation Studio");
        }

        void Studio(Document doc, string name)
        {
            int before = Clicker.Actions;
            try
            {
                main.OpenDocument(doc);
                Show(doc);
                Clicker.Draw(doc);
                Keys(doc, name, check);
                Show(doc);
                Clicker.Everything(doc, name, check, budget: 450);
                check(Clicker.Actions - before > 20, $"{name}: its buttons and menus were found and pressed ({Clicker.Actions - before})");
            }
            catch (Exception e) { check(false, $"{name}: {Clicker.Describe(e)}"); }
            Console.WriteLine($"  {name}: {Clicker.Actions - before} things pressed; windows in use: {Gui()}");
            // Closed again (not saved), so the windows of one studio do not pile up under the next.
            try { typeof(MainForm).GetMethod("CloseDocument", Any)!.Invoke(main, [doc]); Clicker.Pump(50); }
            catch (Exception e) { check(false, $"{name}: closing it — {Clicker.Describe(e.InnerException ?? e)}"); }
        }

        // ---- world copies: the Cheats page for real, then every page pressed through
        string data = Path.Combine(Smoke.Repo, "TestData", "amulet");
        foreach (var (source, label, makeDead) in new[]
        {
            (Path.Combine(data, "java", "vanilla", "1_16_5_dimension"), "Java world", true),
            (Path.Combine(data, "bedrock", "vanilla", "1_16"), "Bedrock world", true),
            (realDead ?? "", "the real dead world (copy)", false),
        })
        {
            if (source.Length == 0 || !Directory.Exists(source)) { Console.WriteLine($"  ({label}: not given or not found)"); continue; }
            string copy = Path.Combine(temp, "worlds", label.Replace(" ", "_").Replace("(", "").Replace(")", ""));
            CopyDir(source, copy);
            if (makeDead) MakeDead(copy);
            try { World(main, docs, copy, label, check, Show); }
            catch (Exception e) { check(false, $"{label}: {Clicker.Describe(e)}"); }
        }
        Console.WriteLine($"  {Clicker.Actions} things pressed in all; {Clicker.Skipped.Count} skipped on purpose:");
        foreach (var s in Clicker.Skipped.Distinct()) Console.WriteLine("      skipped " + s);
        foreach (var d in docs.ToList()) { try { d.Dispose(); } catch { } }
    }

    // Every key the studio answers, as if pressed with the canvas focused (clipboard and outside-program keys left out).
    static void Keys(Document doc, string name, Action<bool, string> check)
    {
        var process = doc.GetType().GetMethod("ProcessCmdKey", Any)!;
        var keys = doc is AnimationDocument
            ? new[] { System.Windows.Forms.Keys.Space, System.Windows.Forms.Keys.Space, System.Windows.Forms.Keys.Right, System.Windows.Forms.Keys.Left, System.Windows.Forms.Keys.K, System.Windows.Forms.Keys.Delete,
                System.Windows.Forms.Keys.Home, System.Windows.Forms.Keys.End, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y }
            : new[] { "B", "E", "G", "I", "S", "N", "D", "L", "R", "C", "V", "M", "X", "O", "OemOpenBrackets", "OemCloseBrackets", "Control+A", "Shift+H", "Shift+V",
                "Shift+R", "Shift+L", "Left", "Shift+Right", "Up", "Down", "Return", "Control+A", "Delete", "Control+Z", "Control+Y", "Control+D", "Escape", "Control+V", "Escape", "Control+Z" }
                .Select(k => (System.Windows.Forms.Keys)Enum.Parse(typeof(System.Windows.Forms.Keys), k.Replace("+", ", "))).ToArray();
        foreach (var k in keys)
        {
            int late = Clicker.Late.Count;
            try
            {
                process.Invoke(doc, [new Message(), k]);
                Clicker.Pump(20);
                Clicker.Draw(doc);
                check(Clicker.Late.Count == late, $"{name}: key {k}" + (Clicker.Late.Count > late ? " — " + Clicker.Describe(Clicker.Late[^1]) : ""));
            }
            catch (Exception e) { check(false, $"{name}: key {k} — {Clicker.Describe(e.InnerException ?? e)}"); }
        }
    }

    static void World(MainForm main, List<Document> docs, string copy, string label, Action<bool, string> check, Action<Document> show)
    {
        var open = main.OpenPath(copy);
        while (!open.IsCompleted) Clicker.Pump(20);
        var doc = docs.OfType<WorldDocument>().LastOrDefault(d => string.Equals(d.World.Location, copy, StringComparison.OrdinalIgnoreCase));
        check(doc != null, $"{label}: opened in a world tab");
        if (doc == null) return;
        show(doc);
        doc.ShowSection(WorldSection.Cheats);
        Clicker.Pump(100);
        CheatsPage Page() => Clicker.All(doc).OfType<CheatsPage>().First();
        Button Btn(string text) => Clicker.All(Page()).OfType<Button>().First(b => b.Text == text);
        string Status() => ((Control)typeof(CheatsPage).GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Page())!).Text;
        DarkCheckBox Achievements() => Clicker.All(Page()).OfType<DarkCheckBox>().First(c => c.Text == "Achievements Enabled");
        // Save changes, and wait until the page says it saved (the save runs in the background).
        void SaveChanges()
        {
            Clicker.Click(Btn("Save changes"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            do Clicker.Pump(50); while (!Status().StartsWith("Saved") && sw.Elapsed.TotalSeconds < 60);
        }

        // Restore Hardcore world from death, then Save changes; checked on a copy of the saved world.
        var restore = Btn("Restore Hardcore world from death");
        check(restore.Enabled, $"{label}: the restore button is on (a player died)");
        Clicker.Click(restore);
        Clicker.Pump(100);
        check(Status().Contains("back in Survival"), $"{label}: restore says where the player will stand — {Status()}");
        check(Achievements().Checked, $"{label}: Achievements Enabled ticked by the restore");
        SaveChanges();
        check(Status().StartsWith("Saved"), $"{label}: saved — {Status()}");
        using (var w = Reopen(copy, "restored"))
        {
            var dead = HardcoreRestore.Dead(w);
            check(HardcoreRestore.IsHardcore(w) && dead.Count == 0, $"{label}: on disk still hardcore, nobody dead ({dead.Count})");
            var flags = WorldFlags.Read(w);
            check(!flags.LocksAchievements || flags.AchievementBlockers().Count == 0, $"{label}: on disk achievements on ({string.Join("; ", flags.AchievementBlockers())})");
            foreach (var p in w.Players) Console.WriteLine($"      {label}: {p.Name} game mode {WorldFlags.PlayerGameMode(w, p.Data)}, Pos {string.Join(", ", p.Data.GetList("Pos")?.Select(t => t.AsDouble.ToString("0.##")) ?? [])}");
        }

        // Achievements Enabled off, saved, on again, saved (only where the world can lock them).
        if (Achievements().Enabled)
        {
            Achievements().Checked = false;
            Clicker.Pump(50);
            SaveChanges();
            using (var w = Reopen(copy, "off"))
                check(WorldFlags.Read(w).AchievementBlockers().Count > 0, $"{label}: unticked and saved → achievements off on disk");
            Achievements().Checked = true;
            Clicker.Pump(50);
            SaveChanges();
            using (var w = Reopen(copy, "on"))
                check(WorldFlags.Read(w).AchievementBlockers().Count == 0, $"{label}: ticked and saved → achievements on on disk ({string.Join("; ", WorldFlags.Read(w).AchievementBlockers())})");
        }
        else Console.WriteLine($"      {label}: Achievements Enabled is greyed ({(doc.World is JavaWorld ? "Java never locks advancements" : "no achievements")})");

        // The world spawn from the map's right-click menu, then saved: on disk afterwards, standing on the block clicked.
        if (WorldSettings.Read(doc.World) is { SpawnX: int sx0, SpawnZ: int sz0 })
        {
            typeof(WorldDocument).GetMethod("SetSpawnHere", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(doc, [new MapBlockClick(sx0 + 3, 70, sz0 - 2, Point.Empty)]);
            Clicker.Pump(50);
            check(Status().Contains("world spawn goes to"), $"{label}: Set the world spawn here fills in the Cheats page — {Status()}");
            SaveChanges();
            using (var w = Reopen(copy, "spawn"))
            {
                var s = WorldSettings.Read(w);
                check(s.SpawnX == sx0 + 3 && s.SpawnZ == sz0 - 2 && s.SpawnY == 71, $"{label}: on disk the spawn is {s.SpawnX}, {s.SpawnY}, {s.SpawnZ}");
            }
        }
        // A game rule ticked and saved.
        if (Clicker.All(Page()).OfType<DarkCheckBox>().FirstOrDefault(c => c.Text == "Keep inventory after death") is { } keep)
        {
            bool was = keep.Checked;
            keep.Checked = !was;
            Clicker.Pump(50);
            SaveChanges();
            using (var w = Reopen(copy, "rules"))
                check(GameRules.Read(w).First(r => r.Label == "Keep inventory after death").On == !was, $"{label}: keep inventory saved as {(!was ? "on" : "off")}");
        }

        // Find in this world: diamonds (ore is in every world), marked on the map; then everything in the window pressed.
        var find = doc.ShowFind();
        Answerer.AddHost(find);
        Clicker.Pump(100);
        var cts = typeof(FindDialog).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = Clicker.All(find).OfType<ListView>().First();
        string FindStatus() => ((Control)typeof(FindDialog).GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(find)!).Text;
        find.FindNow("diamond");
        var searching = System.Diagnostics.Stopwatch.StartNew();
        do Clicker.Pump(50); while (cts.GetValue(find) != null && searching.Elapsed.TotalSeconds < 300);
        check(list.VirtualListSize > 0 && FindStatus().StartsWith("Searched"), $"{label}: Find \"diamond\" lists {list.VirtualListSize:N0} in {searching.Elapsed.TotalSeconds:0.0}s — {FindStatus()}");
        var map = Clicker.All(doc).OfType<MapView>().First();
        var marks = (System.Collections.ICollection)typeof(MapView).GetField("_found", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(map)!;
        check(marks.Count > 0, $"{label}: what was found is marked on the map ({marks.Count:N0} places)");
        Clicker.Everything(find, $"{label} › Find", check, budget: 60);
        do Clicker.Pump(50); while (cts.GetValue(find) != null && searching.Elapsed.TotalSeconds < 600);
        find.Close();
        Clicker.Pump(50);

        // Then every page, everything pressed (the world is a copy in the test folder).
        foreach (var section in Enum.GetValues<WorldSection>())
        {
            doc.ShowSection(section);
            Clicker.Pump(150);
            Clicker.Everything(doc, $"{label} › {section}", check, budget: 220);
        }
        Console.WriteLine($"  {label}: windows in use: {Gui()}");
        // The tab closed again (unsaved changes thrown away: it is a copy).
        try { typeof(MainForm).GetMethod("CloseDocument", Any)!.Invoke(main, [doc]); Clicker.Pump(50); }
        catch (Exception e) { check(false, $"{label}: closing the tab — {Clicker.Describe(e.InnerException ?? e)}"); }
    }

    static int _reopen;
    // The saved world copied once more and opened from there (the world stays open in its tab).
    static SaveWorld Reopen(string copy, string what)
    {
        string again = copy + $".{what}.{++_reopen}";
        CopyDir(copy, again);
        return WorldLoader.Open(again);
    }

    static void MakeDead(string copy)
    {
        using var w = WorldLoader.Open(copy);
        switch (w)
        {
            case JavaWorld j: (j.Data ?? throw new InvalidDataException("The test world has no level.dat Data.")).SetByte("hardcore", 1); j.SaveLevelDat(); break;
            case BedrockWorld b: b.LevelRoot.SetByte("IsHardcore", 1); b.SaveLevelDat(); break;
        }
        foreach (var p in w.Players.ToList())
        {
            if (w is JavaWorld) { p.Data.SetInt("playerGameType", 3); p.Data["Health"] = new NbtFloat(0); }
            else p.Data.SetInt("PlayerGameMode", 6);
            PlayerStore.Save(w, p);
        }
    }

    // Window (USER) and drawing (GDI) objects this process holds: a process may have 10,000 of each.
    static string Gui()
    {
        var h = System.Diagnostics.Process.GetCurrentProcess().Handle;
        return $"{GetGuiResources(h, 1)} USER, {GetGuiResources(h, 0)} GDI";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
