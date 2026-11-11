// tools/ui-smoke/Program.cs: hidden-window UI checks. Builds the studios and dialogs in forms that are never shown, lays
// them out, draws them and drives the shared tools, catching exceptions; each dialog's picture is saved (windows/*.png in
// the test folder) and checked that its content fits, also squeezed for a small screen. "quick" stops there and shows
// nothing; without it the press-everything pass follows (Clicks: windows off-screen, never activated — still, not while
// someone is using the PC or a game is running). Arguments: quick, log=<file>, dead=<a copy of a dead hardcore world>.
// The press-everything pass backs the app's settings.json up and puts it back byte for byte; "quick" writes none and leaves
// it alone. Run: dotnet tools/ui-smoke/bin/Debug/net8.0-windows/uismoke.exe quick
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Studio;

static class Smoke
{
    static int _ok, _bad;
    static readonly bool Verbose = Environment.GetEnvironmentVariable("SMOKE_VERBOSE") == "1";

    /// <summary>The repository (the folder above this program with MinecraftStudio.sln), for TestData; else the current folder.</summary>
    public static string Repo { get; } = FindRepo();

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MinecraftStudio.sln"))) return d.FullName;
        return Environment.CurrentDirectory;
    }

    static void Check(bool ok, string what)
    {
        if (ok) _ok++; else _bad++;
        if (!ok || Verbose) Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    }

    static void Try(string what, Action a)
    {
        try { a(); Check(true, what); }
        catch (Exception e) { Check(false, what + ": " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace); }
    }

    /// <summary>Paints a control (and its children) into a bitmap, as the screen would.</summary>
    static void Draw(Control c)
    {
        if (c.Width <= 0 || c.Height <= 0) return;
        using var bmp = new Bitmap(c.Width, c.Height);
        c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    /// <summary>A left press at one point, moved to another in a few steps and let go there (window messages: nothing is shown).</summary>
    static void Drag(Control c, Point from, Point to, int steps = 4)
    {
        static IntPtr At(Point p) => (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
        SendMessage(c.Handle, 0x0201, (IntPtr)1, At(from)); // WM_LBUTTONDOWN, the left button down
        for (int i = 1; i <= steps; i++)
            SendMessage(c.Handle, 0x0200, (IntPtr)1, At(new Point(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps))); // WM_MOUSEMOVE
        SendMessage(c.Handle, 0x0202, IntPtr.Zero, At(to)); // WM_LBUTTONUP
    }

    /// <summary>Lets work posted back to the window's thread run until <paramref name="done"/>, or gives up after a while.</summary>
    static bool Pump(Func<bool> done, int seconds = 60)
    {
        long until = Environment.TickCount64 + seconds * 1000L;
        while (!done())
        {
            if (Environment.TickCount64 > until) return false;
            Application.DoEvents();
            Thread.Sleep(25);
        }
        // What the last of it posted back (the heights follow the map's last tile).
        for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(25); }
        return true;
    }

    /// <summary>
    /// A window built and laid out (never shown): its picture saved to look at, and checked that what is docked in it fits
    /// inside it — the content above ends before the buttons and status below start, and nothing runs off its edges.
    /// </summary>
    static void Look(Form f, string name, string folder)
    {
        f.CreateControl();
        f.PerformLayout();
        var client = f.ClientRectangle;
        var docked = f.Controls.Cast<Control>().ToList();
        var problems = new List<string>();
        foreach (var c in docked)
            if (c.Bottom > client.Bottom + 1 || c.Right > client.Right + 1 || c.Top < -1 || c.Left < -1)
                problems.Add($"{c.GetType().Name} {c.Bounds} is outside the window's {client.Width}×{client.Height}");
        foreach (var top in docked.Where(c => c.Dock == DockStyle.Top))
            foreach (var bottom in docked.Where(c => c.Dock == DockStyle.Bottom))
                if (top.Bottom > bottom.Top + 1) problems.Add($"{top.GetType().Name} (to y {top.Bottom}) runs under {bottom.GetType().Name} (from y {bottom.Top})");
        // Nor anything inside: a control laid out past its parent's edge is cut off (what scrolls is left alone).
        Snapshot.Create(f);
        f.PerformLayout();
        foreach (var c in All(f))
        {
            if (c.Parent is not { } parent || !Shown(c, f) || Scrolls(parent, f)) continue;
            var room = parent.ClientRectangle;
            if (c.Right > room.Right + 1 || c.Bottom > room.Bottom + 1 || c.Left < -1 || c.Top < -1)
                problems.Add($"{Name(c)} {c.Bounds} runs past the edge of {Name(parent)} ({room.Width}×{room.Height})");
        }
        Check(problems.Count == 0, $"  {name}: its content fits ({client.Width}×{client.Height}){(problems.Count > 0 ? ": " + string.Join("; ", problems) : "")}");
        using var bmp = Snapshot.Of(f);
        bmp.Save(Path.Combine(folder, name + ".png"));
        // A window that can be resized only has to be able to get small enough for a 1366×768 screen (at this display scaling).
        if (f.FormBorderStyle == FormBorderStyle.Sizable)
        {
            double scale = f.DeviceDpi / 96.0;
            Check(f.MinimumSize.Width <= 1366 * scale && f.MinimumSize.Height <= 728 * scale,
                $"  {name}: can be made small enough for a 1366×768 screen (its smallest: {f.MinimumSize.Width}×{f.MinimumSize.Height})");
            return;
        }
        // On a screen too small for it: as tall as allowed, the content scrolling above the buttons, which stay in reach.
        if (f is MinecraftStudio.UI.DarkDialog d && f.Height > 500)
        {
            int max = f.Height / 2;
            d.FitHeight(max);
            f.PerformLayout();
            var scroll = f.Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Fill && p.AutoScroll);
            var bottoms = f.Controls.Cast<Control>().Where(c => c.Dock == DockStyle.Bottom).ToList();
            bool fits = f.Height <= max && scroll != null && scroll.DisplayRectangle.Height > scroll.ClientSize.Height && !scroll.HorizontalScroll.Visible
                && bottoms.All(b => b.Top >= 0 && b.Bottom <= f.ClientSize.Height + 1) && (bottoms.Count == 0 || scroll.Bottom <= bottoms.Min(b => b.Top) + 1);
            Check(fits, $"  {name}: on a screen {max} px high the content scrolls above the buttons (window {f.Height}, content {scroll?.DisplayRectangle.Height} in {scroll?.ClientSize.Height}, buttons from y {(bottoms.Count > 0 ? bottoms.Min(b => b.Top) : -1)} of {f.ClientSize.Height})");
            if (scroll != null && scroll.Controls.Count > 0)
            {
                var inner = scroll.Controls[0];
                using var ib = new Bitmap(inner.Width, inner.Height);
                inner.DrawToBitmap(ib, new Rectangle(0, 0, ib.Width, ib.Height));
                ib.Save(Path.Combine(folder, name + "-small-content.png"));
            }
            using var small = Snapshot.Of(f);
            small.Save(Path.Combine(folder, name + "-small.png"));
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    static bool IsWindowVisibleStyle(IntPtr h) => (GetWindowLong(h, -16) & 0x10000000) != 0;

    // Shown when its window and every one up to the form are set to show (the form itself never is).
    static bool Shown(Control c, Form f)
    {
        for (var p = c; p != null && p != f; p = p.Parent)
            if (!IsWindowVisibleStyle(p.Handle)) return false;
        return true;
    }

    // Inside something that scrolls its content (it may reach past the edge on purpose).
    static bool Scrolls(Control c, Form f)
    {
        for (var p = c; p != null && p != f; p = p.Parent)
            if (p is ScrollableControl { AutoScroll: true }) return true;
        return false;
    }

    /// <summary>
    /// Side panels (docked left or right, scrolling up and down) whose content is wider than they are: what is past the edge
    /// is cut off, or needs a sideways scroll. The Animation Studio's was, at 200 % (its Smooth box and Remove buttons).
    /// </summary>
    static List<string> Sideways(Control root)
    {
        var problems = new List<string>();
        foreach (var panel in All(root).OfType<Panel>().Where(p => p.AutoScroll && p.Dock is DockStyle.Left or DockStyle.Right && p.Visible))
        {
            panel.PerformLayout();
            int room = panel.ClientSize.Width;
            foreach (var c in All(panel).Where(c => c.Visible))
            {
                int left = 0;
                for (var a = c; a != null && a != panel; a = a.Parent) left += a.Left;
                if (left + c.Width > room + 1) problems.Add($"{Name(c)} reaches {left + c.Width} in a side panel {room} wide");
            }
        }
        return problems;
    }

    static string Name(Control c) => $"{c.GetType().Name}{(c.AccessibleName ?? (c.Text.Length > 0 ? c.Text : null)) switch { null => "", var n => $" \"{(n.Length > 40 ? n[..40] + "…" : n)}\"" }}";

    static IEnumerable<Control> All(Control c)
    {
        foreach (Control child in c.Controls)
        {
            yield return child;
            foreach (var g in All(child)) yield return g;
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        // The press-everything pass runs a real main window, which saves the app's settings (opened worlds join the recent
        // list): they are backed up first and put back byte for byte at the end; world backups made while saving go to the
        // test folder. The quick pass writes no settings, so it leaves the file alone — an app window open meanwhile keeps
        // whatever it saves.
        bool quick = args.Contains("quick");
        string settings = Path.Combine(MinecraftStudio.UI.AppSettings.Folder, "settings.json");
        string temp = Path.Combine(Path.GetTempPath(), "MinecraftStudio.Tests", "uismoke", DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(temp);
        byte[]? kept = !quick && File.Exists(settings) ? File.ReadAllBytes(settings) : null;
        if (kept != null) File.WriteAllBytes(Path.Combine(MinecraftStudio.UI.AppSettings.Folder, $"settings.before-uismoke-{DateTime.Now:yyyyMMdd-HHmmss}.json"), kept);
        void PutBack()
        {
            if (quick) return;
            if (kept != null) File.WriteAllBytes(settings, kept);
            else if (File.Exists(settings)) File.Delete(settings);
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => PutBack();
        // The log goes to a file (this is a windowless program); every line is written at once.
        if (args.FirstOrDefault(a => a.StartsWith("log=")) is string log)
            Console.SetOut(TextWriter.Synchronized(new StreamWriter(log[4..], false, new System.Text.UTF8Encoding(false)) { AutoFlush = true }));
        // A watchdog: a press that does not come back within two minutes gets its dialogs closed; after five the test
        // gives up (the settings are still put back).
        uint ui = Answerer.UiThread();
        new Thread(() =>
        {
            string? last = null;
            bool nudged = false;
            while (true)
            {
                Thread.Sleep(5000);
                string? now = Clicker.Current;
                long seconds = (Environment.TickCount64 - Clicker.CurrentSince) / 1000;
                if (now == null || !ReferenceEquals(now, last)) { last = now; nudged = false; continue; }
                if (seconds > 120 && !nudged)
                {
                    nudged = true;
                    Console.WriteLine($"  FAIL [watchdog] {now} has not come back after {seconds}s; closing: {string.Join(", ", Answerer.CloseEverything(ui, IntPtr.Zero))}");
                    _bad++;
                }
                else if (seconds > 300)
                {
                    Console.WriteLine($"  FAIL [watchdog] still stuck on {now} after {seconds}s: giving up. {_ok} passed, {_bad + 1} failed so far");
                    PutBack();
                    Environment.Exit(3);
                }
            }
        }) { IsBackground = true, Name = "watchdog" }.Start();
        try
        {
            // Everything runs inside an application message loop, as in the app: dialogs nest inside it and async code
            // comes back to the UI thread (without it, the first closed dialog takes the UI thread's context away).
            var context = new ApplicationContext();
            var start = new System.Windows.Forms.Timer { Interval = 1 };
            start.Tick += (_, _) =>
            {
                start.Stop();
                try { Run(args, temp); }
                catch (Exception e) { Check(false, "the test itself threw: " + e); }
                finally { context.ExitThread(); }
            };
            start.Start();
            Application.Run(context);
        }
        finally
        {
            Answerer.Stop();
            PutBack();
            Console.WriteLine(quick ? "settings.json left alone (the quick pass writes none)"
                : kept == null || File.ReadAllBytes(settings).AsSpan().SequenceEqual(kept) ? "settings.json put back as it was" : "settings.json DIFFERS from before");
        }
    }

    static void Run(string[] args, string temp)
    {
        MinecraftStudio.Backup.Settings.BackupFolder = Path.Combine(temp, "backups");
        using var form = new Form { Width = 1600, Height = 1000, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
        _ = form.Handle;
        Answerer.Start(form);

        // ---- Skin Studio
        Try("Skin Studio builds, lays out and draws", () =>
        {
            var skin = new Skin(new RgbaImage(64, 64), SkinModel.Classic) { Name = "Test" };
            var doc = new SkinDocument(skin, null) { Dock = DockStyle.Fill };
            form.Controls.Add(doc);
            form.PerformLayout();
            doc.CreateControl();
            Draw(form);
            var canvas = All(doc).OfType<PixelCanvas>().First();
            Draw(canvas);
            var sideways = Sideways(form);
            Check(sideways.Count == 0, $"  Skin Studio: nothing in a side panel is cut off{(sideways.Count > 0 ? ": " + string.Join("; ", sideways.Take(4)) : "")}");
            form.Controls.Remove(doc);
            doc.Dispose();
        });

        // ---- Texture Studio over the installed Bedrock game's textures
        Try("Texture Studio builds over the game's textures and draws", () =>
        {
            var basePack = TexturePackReader.ReadBedrockGame(TextureNames.Default) ?? new TexturePack();
            var doc = new TextureDocument(new TexturePack { Name = "Test" }, basePack, null) { Dock = DockStyle.Fill };
            form.Controls.Add(doc);
            form.PerformLayout();
            Draw(form);
            var ed = doc.Editor;
            Check(ed.Current != null, "  a texture is selected to start");
            // Tools from the shared bar: every tool draws a stroke without error.
            foreach (var tool in Enum.GetValues<PaintTool>())
            {
                ed.Tool = tool;
                ed.BeginStroke(1, 1, false);
                ed.StrokeTo(5, 4, true, constrain: true);
                ed.EndStroke();
            }
            ed.Session.SelectAll();
            ed.Session.Lift();
            ed.Session.MoveFloating(2, 2);
            ed.Session.Transform(PixelTransform.RotateRight);
            Draw(form);
            ed.Session.Commit();
            ed.Session.Preview("Adjust colours", c => ColorAdjust.Apply(c, new ColorAdjustment { Hue = 90 }));
            Draw(form);
            ed.Session.Cancel();
            Check(ed.CanUndo, "  strokes made undo steps");
            while (ed.CanUndo) ed.Undo();
            Check(ed.Pack.Textures.Count == 0, "  undoing everything leaves an empty pack (game textures untouched)");
            form.Controls.Remove(doc);
            doc.Dispose();
        });

        // ---- Mob Studio over the installed games
        Try("Mob Studio builds over Bedrock's mobs, draws the 3D model and the flat texture", () =>
        {
            var catalog = MobCatalog.Bedrock();
            if (catalog == null) { Console.WriteLine("  (Bedrock not installed)"); return; }
            var doc = new MobDocument(new TexturePack { Name = "Mobs", Edition = PackEdition.Bedrock }, catalog, null) { Dock = DockStyle.Fill };
            form.Controls.Add(doc);
            form.PerformLayout();
            Draw(form);
            var ed = doc.Editor;
            Check(ed.Selected != null && MobCatalog.IsMobKey(ed.Selected), $"  a mob is selected to start ({ed.Selected})");
            var view = All(doc).OfType<MobModelView>().First();
            Draw(view);
            foreach (var key in new[] { "entity/cat/tabby", "entity/panda/panda", "entity/zombie/zombie", "entity/chest/normal", "entity/dragon/dragon", "entity/cow/cow_v2" })
            {
                ed.Select(key);
                Draw(form);
            }
            ed.Tool = PaintTool.Pencil;
            ed.BeginStroke(10, 10, false);
            ed.EndStroke();
            var built = TexturePackWriter.Bedrock(ed.Pack, TextureNames.Default, DateTime.Now);
            Console.WriteLine($"    pack textures: {string.Join(", ", ed.Pack.Textures.Keys)}; known to the table: {TextureNames.Default.IsKnown(ed.Selected!)}; written: {string.Join(", ", built.Written)}; not included: {string.Join(" | ", built.NotIncluded)}");
            Check(built.Problems.Count == 0 && built.Written.Count == 1, $"  the painted mob is written into a Bedrock pack ({string.Join("; ", built.Problems)})");
            var sideways = Sideways(form);
            Check(sideways.Count == 0, $"  Mob Studio: nothing in a side panel is cut off{(sideways.Count > 0 ? ": " + string.Join("; ", sideways.Take(4)) : "")}");
            form.Controls.Remove(doc);
            doc.Dispose();
        });

        Try("Animation Studio builds over Bedrock's mobs; its side panel fits", () =>
        {
            var catalog = MobCatalog.Bedrock();
            if (catalog == null) { Console.WriteLine("  (Bedrock not installed)"); return; }
            var doc = new MinecraftStudio.Studio.AnimationDocument(catalog, "Smoke Anims") { Dock = DockStyle.Fill };
            form.Controls.Add(doc);
            form.PerformLayout();
            Snapshot.Create(form);
            form.PerformLayout();
            Draw(form);
            var sideways = Sideways(form);
            Check(sideways.Count == 0, $"  nothing in its side panel is cut off{(sideways.Count > 0 ? ": " + string.Join("; ", sideways.Take(6)) : "")}");
            string folder = Path.Combine(temp, "windows");
            Directory.CreateDirectory(folder);
            using (var bmp = Snapshot.Of(form)) bmp.Save(Path.Combine(folder, "animation-studio.png"));
            form.Controls.Remove(doc);
            doc.Dispose();
        });

        Try("Java mobs with Bedrock models build and draw", () =>
        {
            var jars = GameFolders.JavaJars();
            if (jars.Count == 0 || MobCatalog.Bedrock() is not { } bedrock) { Console.WriteLine("  (needs a Java jar and Bedrock)"); return; }
            var java = MobCatalog.Java(jars[0].Jar, jars[0].Version);
            java.BorrowModels(bedrock);
            var doc = new MobDocument(new TexturePack { Name = "Mobs", Edition = PackEdition.Java, JavaRelease = java.Textures.JavaRelease }, java, null) { Dock = DockStyle.Fill };
            form.Controls.Add(doc);
            form.PerformLayout();
            Draw(form);
            doc.Editor.Select("entity/creeper/creeper");
            Draw(form);
            form.Controls.Remove(doc);
            doc.Dispose();
        });

        // ---- dialogs (built and laid out, not shown)
        Try("Insert a picture dialog builds with a picture and prepares it", () =>
        {
            var area = new RgbaImage(16, 16);
            var editor = new TextureEditor(new TexturePack(), null);
            var photo = new RgbaImage(400, 300);
            for (int i = 0; i < photo.Pixels.Length; i++) photo.Pixels[i] = 0xFF000000 | (uint)(i * 2654435761u >> 8);
            using var dlg = new PictureDialog(editor, area, photo, "photo.jpg");
            dlg.CreateControl();
            dlg.PerformLayout();
            Draw(dlg);
            Check(dlg.Result is { Width: 16 } r && r.Height <= 16, "  the result fits the 16 × 16 texture");
        });

        Try("Replace with a picture fits the selection", () =>
        {
            var game = new TexturePack();
            game.Textures["block/stone"] = new PackTexture { Key = "block/stone", Image = new RgbaImage(16, 16) };
            var editor = new TextureEditor(new TexturePack(), game);
            editor.Select("block/stone");
            editor.Session.Select(new PixelRect(4, 4, 8, 6));
            var photo = new RgbaImage(300, 200);
            for (int i = 0; i < photo.Pixels.Length; i++) photo.Pixels[i] = 0xFFFF0000;
            using var dlg = new PictureDialog(editor, editor.AreaPixels()!, photo, "photo.png", replace: true);
            dlg.CreateControl();
            Draw(dlg);
            Check(dlg.Target == new PixelRect(4, 4, 8, 6) && dlg.Result is { Width: 8, Height: 6 } && dlg.Placement == (4, 4), $"  stretched exactly into the selection ({dlg.Result?.Width} × {dlg.Result?.Height} at {dlg.Placement})");
        });

        Try("My tiles dialog builds (lists the folder, writes nothing)", () =>
        {
            var editor = new SkinEditor(new Skin(new RgbaImage(64, 64), SkinModel.Classic));
            using var dlg = new TileLibraryDialog(editor);
            dlg.CreateControl();
            Draw(dlg);
        });

        Try("every studio has the Picture menu on its tool bar", () =>
        {
            var skin = new SkinDocument(new Skin(new RgbaImage(64, 64), SkinModel.Classic) { Name = "T" }, null);
            var tex = new TextureDocument(new TexturePack { Name = "T" }, null, null);
            foreach (Control doc in new Control[] { skin, tex })
            {
                var bar = All(doc).OfType<ToolStrip>().First(t => t.Dock == DockStyle.Top && t.Items.OfType<ToolStripItem>().Any(i => i.Text == "Undo"));
                Check(bar.Items.OfType<ToolStripDropDownButton>().Any(i => i.Text == "Picture" && i.DropDownItems.Count >= 10), $"  {doc.GetType().Name}: Picture menu with its items");
                doc.Dispose();
            }
        });

        Try("Adjust colours dialog builds", () =>
        {
            var skin = new Skin(new RgbaImage(64, 64), SkinModel.Classic);
            var editor = new SkinEditor(skin);
            using var dlg = new AdjustDialog(editor);
            dlg.CreateControl();
            dlg.PerformLayout();
            Draw(dlg);
        });

        // ---- the day's new windows: built, laid out and drawn (never shown), over the sample worlds (only read). Each is
        // saved as a picture to look at, and checked that its content fits inside it (nothing runs under the buttons).
        string data = Path.Combine(Repo, "TestData", "amulet");
        string java = Path.Combine(data, "java", "vanilla", "1_16_5_dimension"), bedrock = Path.Combine(data, "bedrock", "vanilla", "1_16");
        string pictures = Path.Combine(temp, "windows");
        Directory.CreateDirectory(pictures);
        Console.WriteLine($"  window pictures: {pictures}");
        Try("Structure file window builds over a schematic (previews, material list, turn, save as)", () =>
        {
            var s = new MinecraftStudio.Core.Structures.Structure(3, 2, 3);
            s.Set(0, 0, 0, MinecraftStudio.Core.World.BlockState.Parse("minecraft:oak_stairs[facing=north,half=bottom,shape=straight,waterlogged=false]"));
            s.Set(1, 1, 1, MinecraftStudio.Core.World.BlockState.Parse("minecraft:stone"));
            string file = Path.Combine(temp, "smoke.schem");
            MinecraftStudio.Core.Structures.StructureFile.Save(s, file);
            using var dlg = new MinecraftStudio.Documents.StructureFileDialog(file);
            Look(dlg, "structure-file", pictures);
            var list = All(dlg).OfType<ListView>().First();
            Check(list.Items.Count == 2, $"  its material list has the two blocks ({list.Items.Count})");
        });
        Try("Save a structure window builds over a Java world: the box is dragged on its map and the fields follow", () =>
        {
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(java);
            using var dlg = new MinecraftStudio.Documents.StructureSaveDialog(w);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            var picker = All(dlg).OfType<MinecraftStudio.UI.MapPicker>().Single();
            var map = All(dlg).OfType<MinecraftStudio.UI.MapView>().Single();
            var f = All(dlg).OfType<MinecraftStudio.UI.NumberField>().ToList(); // from x, y, z, then to x, y, z
            var heights = All(dlg).OfType<MinecraftStudio.UI.DarkCheckBox>().First(c => c.Text.StartsWith("Heights from the map", StringComparison.Ordinal));
            (int, int, int, int) Typed() => ((int)f[0].Value, (int)f[2].Value, (int)f[3].Value, (int)f[5].Value);
            Check(f.Count == 6 && Pump(() => picker.Ready && map.AllDrawn), "  its map reads a world of its own and draws it");
            Check(picker.Box == Typed(), $"  the box on the map is the one in the fields ({picker.Box} / {Typed()})");
            void HeightsFollow(string when)
            {
                var range = picker.HeightRange(picker.Box!.Value, out long notDrawn);
                Check(heights.Checked && range is { } r && notDrawn == 0 && f[1].Value == r.Low && f[4].Value == r.High,
                    $"  {when}: the heights are the lowest and highest top block in the box (y {f[1].Value} to {f[4].Value}; the map: {range})");
            }
            HeightsFollow("at first");

            // Dragged by its middle: moved by as many blocks as the pointer went, the same size.
            var before = picker.Box!.Value;
            var mid = map.ScreenOf((before.MinX + before.MaxX + 1) / 2.0, (before.MinZ + before.MaxZ + 1) / 2.0);
            var from = new Point((int)mid.X, (int)mid.Y);
            var to = new Point(from.X + 37, from.Y - 21);
            int dx = map.BlockUnder(to).X - map.BlockUnder(from).X, dz = map.BlockUnder(to).Z - map.BlockUnder(from).Z;
            Drag(map, from, to);
            var moved = (before.MinX + dx, before.MinZ + dz, before.MaxX + dx, before.MaxZ + dz);
            Check(dx != 0 && dz != 0 && picker.Box == moved && Typed() == moved, $"  dragged by its middle it moves {dx}, {dz} blocks and the fields follow ({Typed()})");
            HeightsFollow("moved");

            // Its right edge pulled out: only the "to" x changes, to the block boundary nearest the pointer.
            var box = picker.Box!.Value;
            var edge = map.ScreenOf(box.MaxX + 1, (box.MinZ + box.MaxZ + 1) / 2.0);
            from = new Point((int)Math.Round(edge.X), (int)edge.Y);
            to = new Point(from.X + 26, from.Y + 3);
            int maxX = (int)Math.Floor(map.WorldAt(to).X + 0.5) - 1;
            Drag(map, from, to);
            var pulled = (box.MinX, box.MinZ, maxX, box.MaxZ);
            Check(maxX > box.MaxX && picker.Box == pulled && Typed() == pulled, $"  its right edge pulled out to x {maxX} ({Typed()})");

            // A new box drawn beside it: from the block pressed to the block let go.
            box = picker.Box!.Value;
            var corner = map.ScreenOf(box.MinX, box.MinZ);
            from = new Point(Math.Max(4, (int)corner.X - 60), Math.Max(4, (int)corner.Y - 50));
            to = new Point(from.X + 30, from.Y + 20);
            var (a, c) = (map.BlockUnder(from), map.BlockUnder(to));
            Drag(map, from, to);
            var drawn = (Math.Min(a.X, c.X), Math.Min(a.Z, c.Z), Math.Max(a.X, c.X), Math.Max(a.Z, c.Z));
            Check(picker.Box == drawn && Typed() == drawn, $"  a new box drawn beside it ({Typed()})");
            HeightsFollow("drawn");

            // Corners typed: the map follows; a height typed unticks "Heights from the map".
            f[0].Value = drawn.Item1 - 5;
            Check(picker.Box is { } t && t.MinX == drawn.Item1 - 5, "  a corner typed: the box on the map follows");
            f[1].Value -= 3;
            Check(!heights.Checked, "  a height typed unticks \"Heights from the map\"");
            Look(dlg, "structure-save", pictures);
        });
        Try("Place a structure window builds over a Java world: the structure is put down on its map", () =>
        {
            var s = new MinecraftStudio.Core.Structures.Structure(3, 2, 5);
            for (int z = 0; z < 5; z++)
                for (int x = 0; x < 3; x++) s.Set(x, 0, z, MinecraftStudio.Core.World.BlockState.Parse("minecraft:oak_planks"));
            s.Set(1, 1, 2, MinecraftStudio.Core.World.BlockState.Parse("minecraft:stone"));
            string file = Path.Combine(temp, "smoke-place.nbt");
            MinecraftStudio.Core.Structures.StructureFile.Save(s, file);
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(java);
            using var dlg = new MinecraftStudio.Documents.StructurePlaceDialog(w);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            dlg.LoadFile(file);
            var picker = All(dlg).OfType<MinecraftStudio.UI.MapPicker>().Single();
            var map = All(dlg).OfType<MinecraftStudio.UI.MapView>().Single();
            var f = All(dlg).OfType<MinecraftStudio.UI.NumberField>().ToList(); // the low corner's x, y, z
            var ground = All(dlg).OfType<MinecraftStudio.UI.DarkCheckBox>().First(cb => cb.Text.StartsWith("Stand it on the ground", StringComparison.Ordinal));
            bool Footprint(int sx, int sz) => picker.Box is { } b && b.MaxX - b.MinX + 1 == sx && b.MaxZ - b.MinZ + 1 == sz;
            Check(f.Count == 3 && Pump(() => picker.Ready && map.AllDrawn && Footprint(3, 5)), "  its map is drawn and the footprint is the structure's 3 × 5");
            Check(picker.Box is { } first && first.MinX == f[0].Value && first.MinZ == f[2].Value, "  the footprint starts at the low corner in the fields");
            void OnGround(string when)
            {
                int? g = picker.GroundY(picker.Box!.Value, out long notDrawn);
                Check(ground.Checked && g is int y && notDrawn == 0 && f[1].Value == y, $"  {when}: y is the ground under it ({f[1].Value}; the map: {g})");
            }
            OnGround("at first");

            // A click beside it: put down there, centred on that block.
            var at = new Point(70, 60);
            var block = map.BlockUnder(at);
            Drag(map, at, at, steps: 0);
            var put = (block.X - 1, block.Z - 2, block.X + 1, block.Z + 2);
            Check(picker.Box == put && f[0].Value == put.Item1 && f[2].Value == put.Item2, $"  a click beside it puts it down there, centred on the block ({picker.Box})");
            OnGround("put down");

            // Dragged: it follows the pointer, and so does the low corner.
            var mid = map.ScreenOf(block.X + 0.5, block.Z + 0.5);
            var from = new Point((int)mid.X, (int)mid.Y);
            var to = new Point(from.X + 33, from.Y + 18);
            int dx = map.BlockUnder(to).X - map.BlockUnder(from).X, dz = map.BlockUnder(to).Z - map.BlockUnder(from).Z;
            Drag(map, from, to);
            var dragged = (put.Item1 + dx, put.Item2 + dz, put.Item3 + dx, put.Item4 + dz);
            Check(dx != 0 && picker.Box == dragged && f[0].Value == dragged.Item1 && f[2].Value == dragged.Item2, $"  dragged {dx}, {dz} blocks: the low corner follows ({picker.Box})");
            OnGround("dragged");

            // A quarter turn: the footprint becomes 5 × 3 from the same low corner.
            var turn = All(dlg).OfType<MinecraftStudio.UI.DarkComboBox>().First(cb => cb.AccessibleName == "Turn (seen from above)");
            turn.SelectedIndex = 1;
            Check(Pump(() => Footprint(5, 3), 20) && picker.Box!.Value.MinX == f[0].Value && picker.Box!.Value.MinZ == f[2].Value, "  turned a quarter: the footprint is 5 × 3 from the same low corner");
            // A y typed unticks "Stand it on the ground".
            f[1].Value += 4;
            Check(!ground.Checked, "  a y typed unticks \"Stand it on the ground\"");
            Look(dlg, "structure-place", pictures);
        });
        Try("Place window checks a build: a structure where it was cropped from is built right", () =>
        {
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(java);
            var dim = w.Dimensions.First(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var c = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).First();
            var (low, _) = MinecraftStudio.Core.Structures.StructurePlacement.Height(w, "minecraft:overworld");
            int ox = c.X * 16, oy = low + 50, oz = c.Z * 16;
            var cropped = MinecraftStudio.Core.Structures.StructureClip.Extract(w, dim, ox, oy, oz, ox + 7, oy + 20, oz + 7);
            string file = Path.Combine(temp, "check-build.nbt");
            MinecraftStudio.Core.Structures.StructureFile.Save(cropped, file);
            using var dlg = new MinecraftStudio.Documents.StructurePlaceDialog(w);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            dlg.LoadFile(file);
            All(dlg).OfType<MinecraftStudio.UI.DarkCheckBox>().First(cb => cb.Text.StartsWith("Stand it on the ground", StringComparison.Ordinal)).Checked = false;
            var f = All(dlg).OfType<MinecraftStudio.UI.NumberField>().ToList();
            f[0].Value = ox; f[1].Value = oy; f[2].Value = oz;
            var run = dlg.CheckBuildAsync();
            Check(Pump(() => run.IsCompleted, 120) && run.Exception == null && dlg.LastCheck is { Wrong: 0, Right: > 0 },
                $"  the 8×21×8 box checks as built right where it came from ({dlg.LastCheck?.Right:N0} of {dlg.LastCheck?.Checked:N0})");
            Look(dlg, "structure-place-checked", pictures);
        });
        Try("Lag machines window looks through a world and lists what it finds", () =>
        {
            // The newest test world McTests "lag" built (machines beside ordinary builds), else the plain sample (none).
            string lagRoot = Path.Combine(Path.GetTempPath(), "MinecraftStudio.Tests", "lag");
            string? built = Directory.Exists(lagRoot) ? Directory.GetDirectories(lagRoot).OrderByDescending(d => d, StringComparer.Ordinal).Select(d => Path.Combine(d, "java16")).FirstOrDefault(Directory.Exists) : null;
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(built ?? java);
            using var doc = new MinecraftStudio.Documents.WorldDocument(w);
            using var dlg = new MinecraftStudio.Documents.LagMachinesDialog(doc);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            var look = dlg.LookAsync();
            Check(Pump(() => look.IsCompleted, 180) && look.Exception == null, $"  it looks through every chunk of {(built != null ? "the lag test world" : "the sample world")}");
            var list = All(dlg).OfType<ListView>().Single();
            Check(built != null ? list.Items.Count >= 10 : list.Items.Count == 0, $"  {(built != null ? "the test world's lag machines are listed" : "the sample world has none")} ({list.Items.Count})");
            var which = All(dlg).OfType<MinecraftStudio.UI.DarkComboBox>().First(cb => cb.AccessibleName == "Machines to clean up");
            var clean = All(dlg).OfType<Button>().First(b => b.Text == "Clean up into a copy");
            Check(built == null ? !clean.Enabled : which.Items.Count >= 2 && clean.Enabled, $"  {(built != null ? "the picked machine, or every one of the dimension, can be cleaned up" : "nothing to clean up")} ({which.Items.Count} choices)");
            if (list.Items.Count > 0) dlg.ShowOnMap();
            Look(dlg, "lag-machines", pictures);
        });
        Try("Mods and add-ons window reads a modded world and names its mods", () =>
        {
            string tf = Path.Combine(Repo, "TestData", "amulet", "java", "forge", "1_16_5_twilight_forest");
            if (!Directory.Exists(tf)) { Console.WriteLine("  (no Forge sample)"); return; }
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(tf);
            using var doc = new MinecraftStudio.Documents.WorldDocument(w);
            using var dlg = new MinecraftStudio.Documents.ModsDialog(doc);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            var read = dlg.ReadAsync();
            Check(Pump(() => read.IsCompleted, 180) && read.Exception == null && dlg.Report != null, "  it reads every chunk, and looks for the mods on this PC");
            var list = All(dlg).OfType<ListView>().Single();
            Check(list.Items.Count == 1 && list.Items[0].Text == "twilightforest" && list.Items[0].SubItems[1].Text == "364,265" && list.Items[0].ForeColor == MinecraftStudio.UI.C.ORANGE,
                $"  the Twilight Forest is listed with its 364,265 blocks, in orange: not on this PC ({string.Join(" | ", list.Items.Cast<ListViewItem>().Select(i => i.Text + " " + i.SubItems[1].Text))})");
            var details = All(dlg).OfType<TextBox>().Single(t => t.AccessibleName == "What the world holds of it");
            Check(details.Text.Contains("canopy leaves") && details.Text.Contains("twilightforest:twilightforest"), "  its details list its blocks and its dimension");
            var buttons = All(dlg).OfType<Button>().ToDictionary(b => b.Text);
            Check(buttons["Find its things"].Enabled && buttons["Show on the map"].Enabled && !buttons["Show the mod's file"].Enabled && buttons["Copy the report"].Enabled,
                "  Find, Show on the map and Copy are on; Show the mod's file is off (no jar on this PC)");
            Look(dlg, "mods", pictures);
        });
        Try("Check for problems window finds what stops a world opening, and offers its fix", () =>
        {
            string bad = Path.Combine(Repo, "TestData", "regressions", "bedrock-blank-old-chunks");
            if (!Directory.Exists(bad)) { Console.WriteLine("  (no world that didn't open in TestData)"); return; }
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(bad);
            using var doc = new MinecraftStudio.Documents.WorldDocument(w);
            using var dlg = new MinecraftStudio.Documents.WorldCheckDialog(doc);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            var check = dlg.CheckAsync();
            Check(Pump(() => check.IsCompleted, 300) && check.Exception == null && dlg.Report != null, "  it reads every chunk and looks at how they are stored");
            var list = All(dlg).OfType<ListView>().Single();
            Check(list.Items.Count >= 1 && list.Items[0].SubItems[1].Text.StartsWith("3,307 chunks are stored", StringComparison.Ordinal) && list.Items[0].Text == "Won't open",
                $"  the 3,307 blank chunks come first, as \"Won't open\" ({string.Join(" | ", list.Items.Cast<ListViewItem>().Select(i => i.Text + ": " + i.SubItems[1].Text))})");
            var buttons = All(dlg).OfType<Button>().ToList();
            Check(buttons.Any(b => b.Text == "Rescue…" && b.Enabled) && buttons.Any(b => b.Text == "Show on the map" && b.Enabled) && buttons.Any(b => b.Text == "Copy the report" && b.Enabled),
                "  its fix is Rescue (a repaired copy), and Show on the map and Copy are on");
            var details = All(dlg).OfType<TextBox>().Single(t => t.AccessibleName == "What is wrong and how to fix it");
            Check(details.Text.Contains("What fixes it: Rescue", StringComparison.Ordinal), "  the details say what fixes it");
            Look(dlg, "world-check", pictures);
        });
        Try("Mods and add-ons window says why not for a console save", () =>
        {
            string x360 = Path.Combine(Repo, "TestData", "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");
            if (!File.Exists(x360)) { Console.WriteLine("  (no Xbox 360 sample)"); return; }
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(x360);
            using var doc = new MinecraftStudio.Documents.WorldDocument(w);
            using var dlg = new MinecraftStudio.Documents.ModsDialog(doc);
            var read = All(dlg).OfType<Button>().First(b => b.Text == "Read the world");
            var status = All(dlg).OfType<Label>().First(l => l.Dock == DockStyle.Bottom && l.Text.Length > 0);
            Check(!read.Enabled && status.Text.StartsWith("Not for", StringComparison.Ordinal) && status.Text.Contains("no mods"), $"  Read is off and it says why ({status.Text})");
        });
        Try("Structures window lists a world's structures, and the map marks them", () =>
        {
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(java);
            using var host = new Form { ClientSize = new Size(1400, 900), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            var doc = new MinecraftStudio.Documents.WorldDocument(w) { Dock = DockStyle.Fill };
            host.Controls.Add(doc);
            Snapshot.Create(host);
            host.PerformLayout();
            using var dlg = new MinecraftStudio.Documents.StructuresDialog(doc);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            var load = dlg.FirstLoad;
            Check(Pump(() => load.IsCompleted, 180) && load.Exception == null, "  it reads the structures of every dimension");
            var list = All(dlg).OfType<ListView>().Single();
            Check(list.Items.Count == 36, $"  the sample world's 36 structures are listed ({list.Items.Count})");
            Look(dlg, "structures", pictures);
            // Shown on the map: the markers on, the chosen one circled (the village, with its unopened chests).
            var village = list.Items.Cast<ListViewItem>().First(i => i.Text == "Village");
            village.Selected = true;
            dlg.ShowOnMap();
            var map = All(host).OfType<MinecraftStudio.UI.MapView>().First();
            Check(Pump(() => map.StructureCount == 36 && map.AllDrawn, 120), $"  the map marks them ({map.StructureCount})");
            using var picture = Snapshot.Of(host);
            picture.Save(Path.Combine(pictures, "structures-on-map.png"));
            // The slime chunk overlay (from the seed): offered, and drawn over the explored and the unexplored land alike.
            // Toolbar boxes are found through the toolbars' items (a hosted control is parented by WinForms only once laid out).
            var overlay = All(host).OfType<ToolStrip>().SelectMany(ts => ts.Items.OfType<ToolStripControlHost>()).Select(h => h.Control)
                .OfType<MinecraftStudio.UI.DarkComboBox>().First(cb => cb.AccessibleName == "Overlay");
            int slimeItem = Enumerable.Range(0, overlay.Items.Count).FirstOrDefault(i => overlay.Items[i]?.ToString()?.StartsWith("Slime", StringComparison.Ordinal) == true, -1);
            Check(slimeItem > 0 && map.SlimeChunks != null, "  the overlay offers the slime chunks of the seed");
            if (slimeItem > 0)
            {
                overlay.SelectedIndex = slimeItem;
                map.ZoomBy(1 / 4.0);
                Pump(() => map.AllDrawn, 60);
                using var slime = Snapshot.Of(host);
                slime.Save(Path.Combine(pictures, "slime-chunks-on-map.png"));
                int green = 0;
                for (int y = 0; y < slime.Height; y += 3) for (int x = 0; x < slime.Width; x += 3) { var c = slime.GetPixel(x, y); if (c.G > 120 && c.G > c.R + 40 && c.G > c.B + 20) green++; }
                Check(green > 50, $"  slime chunks are drawn in green ({green} samples)");
            }
        });
        Try("Compare window compares with a backup and offers to bring the changed chunks back", () =>
        {
            // Today's world: the sample with ores turned to stone in a box; the backup: the sample itself.
            string changed = Path.Combine(temp, "compare-changed");
            using (var w = MinecraftStudio.Core.World.WorldLoader.Open(java))
            {
                var dim = w.Dimensions.First(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld");
                var c = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).First();
                var (low, _) = MinecraftStudio.Core.Structures.StructurePlacement.Height(w, "minecraft:overworld");
                MinecraftStudio.Core.Structures.BlockReplace.ReplaceIntoCopy(w, "minecraft:overworld", c.X * 16, low, c.Z * 16, c.X * 16 + 31, low + 80, c.Z * 16 + 31, [new("*_ore", "stone")], changed);
            }
            using var cw = MinecraftStudio.Core.World.WorldLoader.Open(changed);
            using var host = new Form { ClientSize = new Size(1400, 900), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            var doc = new MinecraftStudio.Documents.WorldDocument(cw) { Dock = DockStyle.Fill };
            host.Controls.Add(doc);
            Snapshot.Create(host);
            using var dlg = new MinecraftStudio.Documents.CompareDialog(doc);
            dlg.CreateControl();
            dlg.PerformLayout();
            Snapshot.Create(dlg);
            All(dlg).OfType<MinecraftStudio.UI.DarkTextField>().First(f => f.AccessibleName == "The world to compare with").Text = java;
            var run = dlg.CompareAsync();
            Check(Pump(() => run.IsCompleted, 180) && run.Exception == null, "  it compares the world with its backup");
            var list = All(dlg).OfType<ListView>().Single();
            var which = All(dlg).OfType<MinecraftStudio.UI.DarkComboBox>().First(cb => cb.AccessibleName == "Chunks to bring back");
            var restore = All(dlg).OfType<Button>().First(b => b.Text == "Restore into a copy");
            Check(list.Items.Count > 0 && which.Items.Count == 1 && restore.Enabled, $"  the changed chunks are listed ({list.Items.Count}) and offered to bring back");
            list.Items[0].Selected = true;
            Check(which.Items.Count == 2, "  a chunk picked in the list is offered on its own too");
            Look(dlg, "compare-restore", pictures);
        });
        Try("Texture Studio: a block texture on its block in 3D; the model window changes a model and saves it into the pack", () =>
        {
            var jar = GameFolders.JavaJars().FirstOrDefault(j => JavaReleases.Of(j.Version)?.FlatNames == true);
            if (jar.Jar == null) { Console.WriteLine("  (no Java 1.13+ installed)"); return; }
            var basePack = TexturePackReader.Read(jar.Jar, TextureNames.Default);
            var doc = new TextureDocument(new TexturePack { Name = "Models", JavaRelease = basePack.JavaRelease }, basePack, null) { Dock = DockStyle.Fill };
            form.Controls.Add(doc);
            form.PerformLayout();
            var ed = doc.Editor;
            ed.Select(TextureNames.Default.FromJava("block/oak_log", basePack.JavaRelease));
            Check(Pump(() => doc.BlockMesh != null, 90), $"  oak_log shows on its block ({doc.BlockModelShown}, {doc.BlockMesh?.Quads.Count} faces)");
            Draw(form);
            var models = doc.Models();
            Check(Pump(() => models.IsCompleted, 30), "  the game's models are read");
            using var dlg = new BlockModelDialog(doc, ed, models.Result, "block/stone");
            Check(dlg.Model?.Id == "block/stone" && dlg.Mesh?.Quads.Count == 6, $"  the model window opens block/stone (6 faces: {dlg.Mesh?.Quads.Count})");
            Look(dlg, "block-models", pictures);
            All(dlg).OfType<TextBox>().First(t => t.AccessibleName == "Texture variables").Text = "all = block/granite";
            dlg.Save();
            string path = "assets/minecraft/models/block/stone.json";
            Check(ed.Pack.OtherFiles.TryGetValue(path, out var json) && System.Text.Encoding.UTF8.GetString(json).Contains("block/granite") && ed.IsDirty && models.Result.InPack("block/stone"),
                "  saved into the pack (stone with granite's texture), the pack has changes to save");
            dlg.Revert();
            Check(!ed.Pack.OtherFiles.ContainsKey(path) && !models.Result.InPack("block/stone"), "  \"Use the game's model\" takes it out of the pack again");
            form.Controls.Remove(doc);
            doc.Dispose();
        });

        Try("Replace blocks window builds over a Java world", () =>
        {
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(java);
            using (var dlg = new MinecraftStudio.Documents.ReplaceBlocksDialog(w)) Look(dlg, "replace", pictures);
            // The land's shape (a window of its own: the small-screen check squeezes a window once): Count says how far
            // it would move, nothing written.
            using var shape = new MinecraftStudio.Documents.ReplaceBlocksDialog(w);
            All(shape).OfType<MinecraftStudio.UI.DarkRadioButton>().First(r => r.Text.StartsWith("Shape the land")).Checked = true;
            All(shape).OfType<MinecraftStudio.UI.DarkComboBox>().First(cb => cb.AccessibleName == "Shape").SelectedIndex = 1;
            var count = All(shape).OfType<Button>().First(b => b.Text == "Count");
            Check(count.Enabled && All(shape).OfType<Button>().Any(b => b.Text == "Shape and save copy"), "  shape mode: Count and \"Shape and save copy\"");
            var counting = shape.CountAsync();
            var status = All(shape).OfType<Label>().First(l => l.Dock == DockStyle.Bottom);
            Check(Pump(() => counting.IsCompleted, 60) && (status.Text.Contains("would move") || status.Text.Contains("already") || status.Text.Contains("no ground")), $"  Count plans the smoothing ({status.Text})");
            Look(shape, "replace-shape", pictures);
        });
        Try("3D view: a piece of a Java world turned in 3D", () =>
        {
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(java);
            var dim = w.Dimensions.First(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var info = MinecraftStudio.Core.Conversion.WorldInfoReader.Read(w);
            var piece = MinecraftStudio.Core.Structures.StructureClip.Extract(w, dim, info.SpawnX - 16, 50, info.SpawnZ - 16, info.SpawnX + 15, 90, info.SpawnZ + 15, includeEntities: false);
            using var view = new MinecraftStudio.Documents.Structure3DWindow(piece, "3D view — test");
            Check(Pump(() => view.Mesh != null, 60) && view.Mesh!.Quads.Count > 0, $"  the mesh is made ({view.Mesh?.Quads.Count:N0} rectangles)");
            Look(view, "structure-3d", pictures);
        });
        Try("New world from a heightmap: a saved heights file reads back with its range and corner", () =>
        {
            int w = 96, h = 64;
            var heights = new int[w * h];
            for (int i = 0; i < heights.Length; i++) heights[i] = 50 + (i % w) / 2 + (i / w) / 3;
            heights[0] = MinecraftStudio.Core.Rendering.MapRenderer.NoHeight;
            string png = Path.Combine(temp, "heights.png");
            File.WriteAllBytes(png, MinecraftStudio.Core.Rendering.Heightmap.Encode(heights, w, h, 50, 120, -48, 32));
            using var dlg = new MinecraftStudio.Documents.HeightmapWorldDialog();
            var load = dlg.LoadPicture(png);
            Check(Pump(() => load.IsCompleted, 30) && load.Exception == null, "  the picture is read");
            var place = All(dlg).OfType<MinecraftStudio.UI.DarkComboBox>().First(cb => cb.AccessibleName == "Where it goes");
            var low = All(dlg).OfType<MinecraftStudio.UI.NumberField>().First();
            Check(place.Items.Count == 2 && place.SelectedIndex == 1 && low.Value == 50, $"  filled in from the file: black y {low.Value}, placed where it was saved from");
            var next = All(dlg).OfType<Button>().First(b => b.Text.StartsWith("Next"));
            Check(next.Enabled, "  Next is ready");
            Look(dlg, "heightmap-world", pictures);
            // Next hands the land to the Convert window as its source: it shows it, and is ready to convert.
            var land = new MinecraftStudio.Core.World.HeightmapWorld(new MinecraftStudio.Core.World.HeightmapLand
            {
                Picture = MinecraftStudio.Core.Rendering.Heightmap.Read(File.ReadAllBytes(png)), LowY = 50, HighY = 120, WestX = -48, NorthZ = 32, Name = "Heights test",
            }, png);
            using var convert = new MinecraftStudio.Documents.ConvertDialog(land, [], [], _ => Task.FromResult<MinecraftStudio.Core.World.SaveWorld?>(null), null);
            var labels = All(convert).OfType<Label>().Select(l => l.Text).ToList();
            var go = All(convert).OfType<Button>().First(b => b.Text == "Convert");
            Check(labels.Contains("Heights test") && labels.Any(t => t.StartsWith("Land from a heightmap")) && go.Enabled,
                $"  the Convert window takes the land as its source ({labels.FirstOrDefault(t => t.StartsWith("Land from"))}; Convert {(go.Enabled ? "ready" : "not ready")})");
            Look(convert, "convert-heightmap", pictures);
        });
        Try("What works where: every feature per platform; an Xbox 360 save's Overview switches off what it lacks, saying why", () =>
        {
            var ps3 = MinecraftStudio.Core.World.Platform.PS3;
            using var table = new MinecraftStudio.Documents.WhatWorksWhereDialog(ps3);
            int there = MinecraftStudio.Core.World.PlatformSupport.All.Count(f => f[ps3].Support != MinecraftStudio.Core.World.Support.No);
            Check(table.Rows == there && there > 10, $"  shown for PS3: the {there} features that work there ({table.Rows} rows)");
            Look(table, "what-works-where", pictures);
            string x360 = Path.Combine(Repo, "TestData", "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");
            if (!File.Exists(x360)) { Console.WriteLine("  (no Xbox 360 sample)"); return; }
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(x360);
            using var doc = new MinecraftStudio.Documents.WorldDocument(w);
            using var page = new MinecraftStudio.Documents.WorldPages.OverviewPage(doc);
            var tiles = All(page).OfType<MinecraftStudio.UI.ChoiceTile>().ToList();
            var share = tiles.First(t => t.Text == "Share as a file");
            var place = tiles.First(t => t.Text == "Place a structure");
            var database = tiles.First(t => t.Text == "Database");
            Check(!share.Enabled && share.Caption.StartsWith("Not for Xbox 360") && !database.Enabled && place.Enabled,
                $"  switched off: \"{share.Caption}\", \"{database.Caption}\"; Place a structure stays on");
        });
        Try("Copy items to another world: an Xbox 360 save's player into a copy of a Java world", () =>
        {
            string x360 = Path.Combine(Repo, "TestData", "lce", "mcsaves_x360_fgh_tu70", "savegame.dat");
            if (!File.Exists(x360)) { Console.WriteLine("  (no Xbox 360 sample)"); return; }
            using var source = MinecraftStudio.Core.World.WorldLoader.Open(x360);
            var from = source.Players.First();
            string into = Path.Combine(temp, "copy-items-java");
            foreach (var f in Directory.EnumerateFiles(java, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(into, Path.GetRelativePath(java, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            using var dlg = new MinecraftStudio.Documents.InventoryCopyDialog(source, from, [], _ => false);
            var load = dlg.LoadAsync(into);
            Check(Pump(() => load.IsCompleted, 60) && load.Exception == null && All(dlg).OfType<MinecraftStudio.UI.DarkComboBox>().First(c => c.AccessibleName == "Player to copy into").Items.Count > 0, "  the Java world's players are listed");
            Look(dlg, "copy-items", pictures);
            string was = MinecraftStudio.Backup.Settings.BackupFolder;
            MinecraftStudio.Backup.Settings.BackupFolder = Path.Combine(temp, "backups-copy");
            try
            {
                var copy = dlg.CopyAsync(ask: false);
                Check(Pump(() => copy.IsCompleted, 120) && copy.Exception == null && dlg.Changed != null, $"  copied and saved ({All(dlg).OfType<Label>().First(l => l.Dock == DockStyle.Bottom).Text})");
                using var reopened = MinecraftStudio.Core.World.WorldLoader.Open(into);
                int items = reopened.Players.Select(p => new MinecraftStudio.Core.World.PlayerInventory(reopened, p)).Max(inv => inv.Parts.Sum(part => Enumerable.Range(0, MinecraftStudio.Core.World.PlayerInventory.Size(part)).Count(i => inv.Get(part, i) is { IsEmpty: false })));
                Check(items > 0 && Directory.EnumerateFiles(Path.Combine(temp, "backups-copy")).Any(), $"  the Java world holds them after reopening ({items} items) and was backed up first");
            }
            finally { MinecraftStudio.Backup.Settings.BackupFolder = was; }
        });
        Try("Map art window builds over a Bedrock world", () =>
        {
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(bedrock);
            using var dlg = new MinecraftStudio.Documents.MapArtDialog(w, () => false);
            Look(dlg, "map-art", pictures);
        });
        Try("Single-player and server window builds over a Java world", () =>
        {
            using var w = (MinecraftStudio.Core.Java.JavaWorld)MinecraftStudio.Core.World.WorldLoader.Open(java);
            using var dlg = new MinecraftStudio.Documents.ServerPlayerDialog(w, w.Players.FirstOrDefault());
            Look(dlg, "server-players", pictures);
            Check(All(dlg).OfType<MinecraftStudio.UI.DarkComboBox>().First().Items.Count == w.Players.Count, "  it lists the world's players");
        });
        Try("Statistics and advancements window builds over a Java world's player", () =>
        {
            using var w = (MinecraftStudio.Core.Java.JavaWorld)MinecraftStudio.Core.World.WorldLoader.Open(java);
            var player = w.Players.First(p => MinecraftStudio.Core.World.ServerPlayers.UuidOf(p) is Guid g && File.Exists(MinecraftStudio.Core.World.PlayerProgress.Files(w, g).Stats));
            var catalog = MinecraftStudio.Core.World.AdvancementCatalog.ForWorld(w);
            Console.WriteLine($"  (a {w.VersionName} world: advancement titles from the installed Minecraft {catalog.Version})");
            var progress = MinecraftStudio.Core.World.PlayerProgress.Read(w, player, catalog)!;
            using var dlg = new MinecraftStudio.Documents.ProgressDialog(w, player, progress, catalog);
            var list = All(dlg).OfType<ListView>().Single();
            Check(list.Items.Count > 10, $"  the advancements are listed ({list.Items.Count}, titles from Minecraft {catalog.Version})");
            Look(dlg, "progress-advancements", pictures);
            var show = All(dlg).OfType<MinecraftStudio.UI.DarkComboBox>().First(cb => cb.AccessibleName == "Show");
            show.SelectedIndex = 1;
            Check(list.Items.Count > 5, $"  and a category of statistics ({show.SelectedItem}: {list.Items.Count})");
            Look(dlg, "progress-statistics", pictures);
        });
        Try("Backups: the index tells same-named worlds apart, a backup comes back as a new world, the window lists them", () =>
        {
            // The backups go to the test folder (in memory only: the settings file is not written).
            string was = MinecraftStudio.Backup.Settings.BackupFolder;
            MinecraftStudio.Backup.Settings.BackupFolder = Path.Combine(temp, "backups");
            try
            {
                string saves = Path.Combine(temp, "saves");
                string a = Path.Combine(saves, "World A"), b = Path.Combine(saves, "World B");
                foreach (var to in new[] { a, b })
                    foreach (var f in Directory.EnumerateFiles(java, "*", SearchOption.AllDirectories))
                    {
                        string dst = Path.Combine(to, Path.GetRelativePath(java, f));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        File.Copy(f, dst);
                    }
                using var wa = MinecraftStudio.Core.World.WorldLoader.Open(a);
                MinecraftStudio.Backup.Folder(wa.Name, a, null, auto: false);
                Thread.Sleep(1100);
                MinecraftStudio.Backup.Folder(wa.Name, a, null, auto: true);
                Thread.Sleep(1100);
                MinecraftStudio.Backup.Folder(wa.Name, b, null, auto: false);   // the same name, another world
                var listed = MinecraftStudio.Backup.Of(wa.Name, a);
                Check(listed.Count == 2 && listed[0].Auto && !listed[1].Auto && listed.All(x => !x.ByName), $"  World A's two backups are listed, newest (automatic) first, World B's same-named one not ({listed.Count})");
                string restored = MinecraftStudio.Backup.RestoreAsNewWorld(listed[1], a);
                bool same = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "session.lock")
                    .All(f => File.Exists(Path.Combine(restored, Path.GetRelativePath(a, f))) && File.ReadAllBytes(f).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(restored, Path.GetRelativePath(a, f)))));
                Check(Path.GetDirectoryName(restored) == saves && Path.GetFileName(restored).StartsWith("World A (restored", StringComparison.Ordinal) && same, $"  a backup comes back beside the world as a new one, byte for byte ({Path.GetFileName(restored)})");
                using var dlg = new MinecraftStudio.Documents.BackupsDialog(wa, null);
                Check(All(dlg).OfType<ListView>().Single().Items.Count == 2, "  the Backups window lists them");
                Look(dlg, "backups", pictures);
            }
            finally { MinecraftStudio.Backup.Settings.BackupFolder = was; }
        });
        Try("New data pack or add-on window builds", () =>
        {
            using var dlg = new MinecraftStudio.Documents.NewPackDialog();
            Look(dlg, "new-pack", pictures);
        });
        foreach (var (name, path) in new[] { ("java", java), ("bedrock", bedrock) })
            Try($"Move to safety window builds over a {name} world's player", () =>
            {
                using var w = MinecraftStudio.Core.World.WorldLoader.Open(path);
                if (w.Players.Count == 0) { Console.WriteLine("  (no players)"); return; }
                using var dlg = new MinecraftStudio.Documents.MovePlayerDialog(w, w.Players[0]);
                Look(dlg, "move-player-" + name, pictures);
                Check(All(dlg).OfType<RadioButton>().Count() == 3 && All(dlg).OfType<RadioButton>().Count(r => r.Checked) == 1, "  three places to choose from, one chosen");
            });
        Try("Move to safety from the map (a block right-clicked) starts near it", () =>
        {
            using var w = MinecraftStudio.Core.World.WorldLoader.Open(java);
            if (w.Players.Count == 0) { Console.WriteLine("  (no players)"); return; }
            using var dlg = new MinecraftStudio.Documents.MovePlayerDialog(w, w.Players[0], (123, -45));
            Look(dlg, "move-player-near", pictures);
            var radios = All(dlg).OfType<RadioButton>().ToList();
            var fields = All(dlg).OfType<MinecraftStudio.UI.NumberField>().ToList();
            Check(radios.Count(r => r.Checked) == 1 && radios.Single(r => r.Checked).Text.StartsWith("Near these coordinates", StringComparison.Ordinal)
                && fields.Count == 2 && fields[0].Value == 123 && fields[1].Value == -45, "  near these coordinates chosen, x 123 and z -45 filled in");
        });

        // ---- every button, menu item, tile and key, in a real (hidden) main window
        if (!args.Contains("quick"))
            Try("the press-everything pass ran to the end", () => Clicks.Run(Check, temp, args.FirstOrDefault(a => a.StartsWith("dead="))?[5..]));
        Console.WriteLine($"  dialogs seen: {Answerer.Log.Count}");
        Console.WriteLine($"{_ok} passed, {_bad} failed");
        Environment.ExitCode = _bad == 0 ? 0 : 1;
    }
}
