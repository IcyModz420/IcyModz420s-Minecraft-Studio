// tools/ui-smoke/Clicker.cs: presses every button, menu item, tile, check box, list, combo and number field of a control tree (a studio, a world
// page, the start page) without showing it, pumping messages after each and drawing the tree again. Skipped: whatever
// would leave the test (another program, Explorer, the clipboard, the phone, installing into the game, closing).
// Exceptions (thrown at once, or later on the UI thread) and error-looking message boxes count as failures.
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using MinecraftStudio;

static class Clicker
{
    public static readonly List<string> Skipped = [];
    public static readonly List<Exception> Late = [];
    public static MainForm? Main;
    public static int Actions;
    /// <summary>What is being pressed now, and since when (for the watchdog).</summary>
    public static volatile string? Current;
    public static long CurrentSince;

    static readonly string[] SkipWords = ["Explorer", "Show the folder", "another program", "phone", "Android", "Copy", "Cut", "Install", "website", "Close", "Exit", "Show in"];
    static readonly string[] ErrorWords = ["failed:", "Object reference", "Index was outside", "Value cannot be null", "Sequence contains", "Collection was modified",
        "Specified argument", "Unable to cast", "Input string was not", "Exception", "must be non-negative", "out of range"];

    static readonly BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static readonly MethodInfo OnClickMethod = typeof(Control).GetMethod("OnClick", Any)!;
    static readonly MethodInfo? GetStateMethod = typeof(Control).GetMethods(Any).FirstOrDefault(m => m.Name == "GetState" && m.GetParameters().Length == 1);
    static readonly PropertyInfo EventsProp = typeof(Component).GetProperty("Events", Any)!;
    static readonly object? ClickKey = typeof(Control).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
        .FirstOrDefault(f => f.Name is "s_clickEvent" or "EventClick")?.GetValue(null);
    static readonly MethodInfo DropDownShow = typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", Any)!;

    public static bool OwnVisible(Control c)
    {
        if (GetStateMethod == null) return true;
        var pt = GetStateMethod.GetParameters()[0].ParameterType;
        object flag = pt.IsEnum ? Enum.ToObject(pt, 2) : 2;
        return (bool)GetStateMethod.Invoke(c, [flag])!;
    }

    static bool Shown(Control c, Control root)
    {
        for (var x = c; x != null; x = x.Parent)
        {
            if (!OwnVisible(x)) return false;
            if (x == root) return true;
        }
        return true;
    }

    static bool HasClick(Control c) => ClickKey != null && ((EventHandlerList)EventsProp.GetValue(c)!)[ClickKey] != null;

    public static void Click(Control c) => OnClickMethod.Invoke(c, [EventArgs.Empty]);

    public static IEnumerable<Control> All(Control c)
    {
        foreach (Control child in c.Controls)
        {
            yield return child;
            foreach (var g in All(child)) yield return g;
        }
    }

    public static bool Busy() => Main != null && (bool)typeof(MainForm).GetField("_busy", Any)!.GetValue(Main)!;

    /// <summary>Lets the UI thread run: queued work, timers, async continuations, and anything the app is running.</summary>
    public static void Pump(int ms = 40)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms || Busy() && sw.ElapsedMilliseconds < 120_000)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Application.DoEvents();
    }

    public static void Draw(Control c)
    {
        if (c.Width <= 0 || c.Height <= 0 || c.IsDisposed) return;
        using var bmp = new Bitmap(Math.Min(c.Width, 4000), Math.Min(c.Height, 4000));
        c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
    }

    static string Label(Control c)
    {
        if (!string.IsNullOrWhiteSpace(c.AccessibleName)) return c.AccessibleName!;
        if (c is not (ComboBox or ListBox or ListView or TreeView or TextBoxBase) && !string.IsNullOrWhiteSpace(c.Text)) return c.Text;
        // A row's label: the label next to it in the same panel.
        if (c.Parent?.Controls.OfType<Label>().FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Text)) is { } l) return l.Text;
        return c.Name is { Length: > 0 } n ? n : c.GetType().Name;
    }

    static string Short(string s) => s.Replace("\r", " ").Replace("\n", " ") is var t && t.Length > 70 ? t[..70] + "…" : t;

    // Every action of the tree as it is now: (key, action).
    static IEnumerable<(string Key, Action Act, object Target)> Find(Control root)
    {
        var seen = new Dictionary<string, int>();
        string Unique(string key) { int n = seen.GetValueOrDefault(key); seen[key] = n + 1; return n == 0 ? key : $"{key} #{n + 1}"; }
        foreach (var c in All(root).ToList())
        {
            if (c.IsDisposed || !Shown(c, root) || !c.Enabled) continue;
            // A recent world's card opens the real world: never pressed (the test works on copies only).
            if (c.GetType().Name == "WorldCard") { Skipped.Add("a recent world's card (opens a real world)"); continue; }
            switch (c)
            {
                case ToolStrip strip:
                    foreach (var (key, act, item) in Items(strip.Items, strip.GetType().Name)) yield return (Unique(key), act, item);
                    break;
                case Button b:
                    yield return (Unique("button: " + Short(b.Text)), () => Click(b), b);
                    break;
                case CheckBox box:
                    yield return (Unique("check: " + Short(box.Text)), () => { box.Checked = !box.Checked; Pump(); if (!box.IsDisposed) box.Checked = !box.Checked; }, box);
                    break;
                case ComboBox combo when combo.Items.Count > 0:
                    yield return (Unique("combo: " + Short(Label(combo))), () =>
                    {
                        int was = combo.SelectedIndex;
                        for (int i = 0; i < Math.Min(combo.Items.Count, 6) && !combo.IsDisposed; i++) { combo.SelectedIndex = i; Pump(); }
                        if (!combo.IsDisposed && was < combo.Items.Count) combo.SelectedIndex = was;
                    }, combo);
                    break;
                case ListBox list when list.Items.Count > 0:
                    yield return (Unique("list: " + Short(Label(list))), () =>
                    {
                        for (int i = 0; i < Math.Min(list.Items.Count, 5) && !list.IsDisposed; i++) { list.SelectedIndex = i; Pump(); }
                    }, list);
                    break;
                case ListView view when (view.VirtualMode ? view.VirtualListSize : view.Items.Count) > 0:
                    yield return (Unique("listview: " + Short(Label(view))), () =>
                    {
                        // By index (a virtual list has no item objects to select).
                        for (int i = 0; i < Math.Min(view.VirtualMode ? view.VirtualListSize : view.Items.Count, 5) && !view.IsDisposed; i++)
                        {
                            view.SelectedIndices.Clear();
                            view.SelectedIndices.Add(i);
                            Pump();
                        }
                    }, view);
                    break;
                case TreeView tree when tree.Nodes.Count > 0:
                    yield return (Unique("tree: " + Short(Label(tree))), () =>
                    {
                        foreach (var node in Nodes(tree.Nodes).Take(8).ToList()) { if (tree.IsDisposed) break; tree.SelectedNode = node; Pump(); }
                    }, tree);
                    break;
                case TextBoxBase text when !text.ReadOnly:
                    yield return (Unique("text: " + Short(Label(text))), () => { string was = text.Text; text.Text = "a"; Pump(); if (!text.IsDisposed) text.Text = was; }, text);
                    break;
                default:
                    if (Numeric(c) is { } n) yield return (Unique("number: " + Short(Label(c))), n, c);
                    else if (HasClick(c)) yield return (Unique($"{c.GetType().Name}: {Short(Label(c))}"), () => Click(c), c);
                    break;
            }
        }
    }

    static IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            yield return n;
            foreach (var g in Nodes(n.Nodes)) yield return g;
        }
    }

    // A number field or slider: Minimum, Maximum, then back.
    static Action? Numeric(Control c)
    {
        var t = c.GetType();
        var value = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        var min = t.GetProperty("Minimum", BindingFlags.Public | BindingFlags.Instance);
        var max = t.GetProperty("Maximum", BindingFlags.Public | BindingFlags.Instance);
        if (value == null || min == null || max == null || !value.CanWrite || value.PropertyType != min.PropertyType) return null;
        return () =>
        {
            var was = value.GetValue(c);
            foreach (var v in new[] { min.GetValue(c), max.GetValue(c), was })
            {
                if (c.IsDisposed) break;
                value.SetValue(c, v);
                Pump();
            }
        };
    }

    static IEnumerable<(string Key, Action Act, ToolStripItem Item)> Items(ToolStripItemCollection items, string path)
    {
        foreach (ToolStripItem item in items.Cast<ToolStripItem>().ToList())
        {
            if (item is ToolStripSeparator || !item.Available) continue;
            string text = string.IsNullOrWhiteSpace(item.Text) ? item.ToolTipText ?? item.GetType().Name : item.Text;
            string key = $"{path} › {Short(text)}";
            switch (item)
            {
                case ToolStripDropDownItem drop:
                    // Fill it as opening it would (some menus are filled then), then walk what is in it.
                    try { DropDownShow.Invoke(drop, [EventArgs.Empty]); } catch { }
                    if (drop.DropDownItems.Count > 0)
                    {
                        foreach (var x in Items(drop.DropDownItems, key)) yield return x;
                        try { typeof(ToolStripDropDownItem).GetMethod("OnDropDownHide", Any)!.Invoke(drop, [EventArgs.Empty]); } catch { }
                    }
                    else if (drop.Enabled) yield return (key, drop.PerformClick, drop);
                    break;
                case ToolStripComboBox combo when combo.Items.Count > 0:
                    yield return (key, () => { for (int i = 0; i < Math.Min(combo.Items.Count, 6); i++) { combo.SelectedIndex = i; Pump(); } }, combo);
                    break;
                case ToolStripControlHost:
                case ToolStripLabel:
                    break;
                default:
                    if (item.Enabled) yield return (key, item.PerformClick, item);
                    break;
            }
        }
    }

    /// <summary>Presses everything under <paramref name="root"/>, a few rounds (pressing can show more), each thing once.</summary>
    public static void Everything(Control root, string where, Action<bool, string> check, int budget = 500, Func<string, bool>? skip = null)
    {
        var done = new HashSet<string>();
        for (int round = 0; round < 5 && budget > 0 && !root.IsDisposed; round++)
        {
            var found = Find(root).ToList();
            bool any = false;
            foreach (var (key, act, target) in found)
            {
                if (root.IsDisposed || budget <= 0) break;
                if (Gone(target)) continue;
                if (!done.Add(key)) continue;
                if (SkipWords.Any(w => key.Contains(w, StringComparison.OrdinalIgnoreCase)) || skip?.Invoke(key) == true) { Skipped.Add($"{where}: {key}"); continue; }
                any = true;
                budget--;
                Actions++;
                int logged = Answerer.Log.Count, late = Late.Count;
                Current = $"{where}: {key}";
                CurrentSince = Environment.TickCount64;
                Console.WriteLine("    → " + Current);
                string? error = null;
                try
                {
                    act();
                    Pump();
                    if (!root.IsDisposed) Draw(root);
                }
                catch (Exception e) { error = Describe(e is TargetInvocationException { InnerException: { } inner } ? inner : e); }
                Pump(10);
                if (Late.Count > late) error = (error == null ? "" : error + "; ") + string.Join("; ", Late.Skip(late).Select(Describe));
                foreach (var seen in Answerer.Log.Skip(logged))
                {
                    Console.WriteLine($"      {where}: {key} → {seen}");
                    if (seen.Kind == "message" && ErrorWords.Any(w => seen.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        error = (error == null ? "" : error + "; ") + "error shown: " + Short(seen.Text);
                }
                check(error == null, $"{where}: {key}" + (error != null ? " — " + error : ""));
            }
            if (!any) break;
        }
    }

    // Rebuilt away by an earlier press: found again (under the same key) in the next round, if still there.
    static bool Gone(object target) => target switch
    {
        Control c => c.IsDisposed,
        ToolStripItem i => i.IsDisposed || i.Owner == null && i.OwnerItem == null,
        _ => false,
    };

    public static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message} @ {e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("MinecraftStudio"))?.Trim()}";
}
