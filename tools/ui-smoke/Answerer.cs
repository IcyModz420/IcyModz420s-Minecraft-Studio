// tools/ui-smoke/Answerer.cs: answers every dialog the app opens while the UI test drives it: WinForms dialogs and native ones (message boxes,
// file dialogs). Every new window of this thread is moved far off the screen and never activated, so nothing flashes up
// or takes the focus from whatever else is running on the PC. Each dialog is logged; a rule may pick a button to press
// (and text to type first), otherwise the dialog is cancelled (Cancel, No, Close, else OK).
using System.Runtime.InteropServices;
using System.Text;

static class Answerer
{
    public sealed record Seen(string Kind, string Title, string Text, string[] Buttons)
    {
        public override string ToString() => $"[{Kind}] {Title}: {Text.Replace("\r", " ").Replace("\n", " ")} {{{string.Join(" | ", Buttons)}}}";
    }

    /// <summary>One answer: the button to press (a prefix of its text) and text typed into the first text box first.</summary>
    public sealed record Reply(string Button, string? Type = null);

    public static readonly List<Seen> Log = [];
    public static readonly List<Func<Seen, Reply?>> Rules = [];
    static readonly HashSet<Form> Hosts = [];
    static readonly Dictionary<IntPtr, DateTime> Answered = [];
    static System.Windows.Forms.Timer? _timer;
    static IntPtr _hook;
    static HookProc? _proc;
    static bool _ticking;

    public static void Start(Form host)
    {
        Hosts.Add(host);
        _proc = Cbt;
        _hook = SetWindowsHookEx(WH_CBT, _proc, IntPtr.Zero, GetCurrentThreadId());
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public static void AddHost(Form f) => Hosts.Add(f);

    public static void Stop()
    {
        _timer?.Stop();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    // Off the screen as it is activated. Activation itself is let through: refusing it left ShowWindow waiting for good
    // (and a process started in the background cannot take the foreground from other programs anyway).
    static IntPtr Cbt(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HCBT_ACTIVATE) SetWindowPos(wParam, IntPtr.Zero, -32000, -32000, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    static void Tick()
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            foreach (var f in Application.OpenForms.Cast<Form>().ToList())
            {
                if (Hosts.Contains(f) || !f.Visible || f.IsDisposed) continue;
                if (Answered.TryGetValue(f.Handle, out var when) && (DateTime.Now - when).TotalSeconds < 2) continue;
                var buttons = All(f).OfType<Button>().Where(b => b.Visible && b.Enabled).ToList();
                var seen = new Seen("form", f.Text, string.Join(" / ", All(f).Where(c => c is Label or TextBox).Select(c => c.Text).Where(t => t.Length > 0).Take(8)), buttons.Select(b => b.Text).ToArray());
                Log.Add(seen);
                Answered[f.Handle] = DateTime.Now;
                var reply = Rules.Select(r => r(seen)).FirstOrDefault(r => r != null);
                var press = reply != null ? buttons.FirstOrDefault(b => b.Text.StartsWith(reply.Button, StringComparison.OrdinalIgnoreCase)) : null;
                if (press != null)
                {
                    if (reply!.Type != null && All(f).OfType<TextBox>().FirstOrDefault() is { } box) box.Text = reply.Type;
                    f.BeginInvoke(() => press.PerformClick());
                }
                else if (f.Modal) f.BeginInvoke(() => f.DialogResult = DialogResult.Cancel);
                else f.BeginInvoke(f.Close);
            }
            var natives = new List<IntPtr>();
            EnumThreadWindows(GetCurrentThreadId(), (h, _) => { if (IsWindowVisible(h) && Class(h) == "#32770") natives.Add(h); return true; }, IntPtr.Zero);
            foreach (var h in natives)
            {
                if (Answered.TryGetValue(h, out var when) && (DateTime.Now - when).TotalSeconds < 2) continue;
                var buttons = new List<(IntPtr Handle, string Text)>();
                EnumChildWindows(h, (c, _) => { if (Class(c) == "Button" && IsWindowVisible(c)) buttons.Add((c, Text(c).Replace("&", ""))); return true; }, IntPtr.Zero);
                var sb = new StringBuilder(4096);
                GetDlgItemText(h, 0xFFFF, sb, sb.Capacity);
                var seen = new Seen(sb.Length > 0 ? "message" : "native", Text(h), sb.ToString(), buttons.Select(b => b.Text).ToArray());
                Log.Add(seen);
                Answered[h] = DateTime.Now;
                var reply = Rules.Select(r => r(seen)).FirstOrDefault(r => r != null);
                (IntPtr Handle, string Text) press = default;
                if (reply != null) press = buttons.FirstOrDefault(b => b.Text.StartsWith(reply.Button, StringComparison.OrdinalIgnoreCase));
                if (press.Handle == IntPtr.Zero)
                    foreach (var name in (string[])["Cancel", "No", "Close", "OK"])
                        if (buttons.FirstOrDefault(b => b.Text.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Handle: not 0 } b) { press = b; break; }
                if (press.Handle != IntPtr.Zero) PostMessage(h, WM_COMMAND, (IntPtr)GetDlgCtrlID(press.Handle), press.Handle);
                else PostMessage(h, WM_COMMAND, (IntPtr)2, IntPtr.Zero);
            }
        }
        finally { _ticking = false; }
    }

    static IEnumerable<Control> All(Control c)
    {
        foreach (Control child in c.Controls)
        {
            yield return child;
            foreach (var g in All(child)) yield return g;
        }
    }

    static string Class(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
    static string Text(IntPtr h) { var sb = new StringBuilder(1024); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

    const int WH_CBT = 5, HCBT_ACTIVATE = 5, WM_COMMAND = 0x0111, WM_CLOSE = 0x0010;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    /// <summary>For the watchdog (another thread): what is open on the UI thread, and a WM_CLOSE to each dialog.</summary>
    public static List<string> CloseEverything(uint uiThread, IntPtr host)
    {
        var seen = new List<string>();
        EnumThreadWindows(uiThread, (h, _) =>
        {
            if (IsWindowVisible(h) && h != host)
            {
                seen.Add($"{Class(h)} \"{Text(h)}\"");
                PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        return seen;
    }

    public static uint UiThread() => GetCurrentThreadId();
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint threadId, EnumProc fn, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc fn, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetDlgItemText(IntPtr hDlg, int id, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
