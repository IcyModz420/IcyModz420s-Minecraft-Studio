// Phone/AdbPhone.cs: a phone reached through Google's adb.exe (USB debugging or Wireless debugging).
// Owns: finding adb.exe (FindAdb), running it safely (Run: arguments passed one by one, answers capped, stopped on
// timeout or Stop), the phone list (Find), wireless Pair/Connect with checked addresses, and Sh/Shell for commands.
// Worlds on shared storage and the copy itself: AdbPhone.Worlds.cs. Rooted phones (app's private folder):
// AdbPhone.Root.cs. Rule for every phone command: anything that came from the phone goes in through Sh().
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MinecraftStudio.Phone;

/// <summary>
/// A phone reached through Android's debug bridge (adb.exe from Google's platform tools): over USB with USB debugging
/// on, or over Wi-Fi with Wireless debugging (Android 11 and newer, after pairing with a code).
/// </summary>
sealed partial class AdbPhone : PhoneSource
{
    readonly string _adb, _serial, _model;

    AdbPhone(string adb, string serial, string model) { _adb = adb; _serial = serial; _model = model; }

    public override string Label => $"{_model} (debugging, {(_serial.Contains(':') ? "wireless" : "USB")})";

    /// <summary>Where this program keeps its own copy of the platform tools (when the player downloaded them here).</summary>
    public static string ToolsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Minecraft Studio", "platform-tools");

    /// <summary>The most text one adb answer may hold (a file list of a very big world fits many times over).</summary>
    const int MaxAnswer = 64 << 20;

    /// <summary>adb.exe from this program's folder, the Android SDK, or the PATH; null when there is none.</summary>
    public static string? FindAdb()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string>
        {
            Path.Combine(ToolsFolder, "adb.exe"),
            Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "platform-tools", "adb.exe"),
        };
        // Only full paths from the PATH: a relative entry (such as ".") would run whatever adb.exe is in the current folder.
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir = entry.Trim().Trim('"');
            if (Path.IsPathFullyQualified(dir)) candidates.Add(Path.Combine(dir, "adb.exe"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Runs adb with each argument handed over on its own (Windows quotes them, so nothing in them, a name from the
    /// phone included, can change which arguments adb gets). adb is stopped when it takes longer than
    /// <paramref name="timeoutMs"/>, when <paramref name="ct"/> is cancelled, or when it answers with more than
    /// <paramref name="maxChars"/> characters.
    /// </summary>
    public static (int Code, string Out, string Err) Run(string adb, IEnumerable<string> args, int timeoutMs = 60000, CancellationToken ct = default, int maxChars = MaxAnswer)
    {
        var psi = new ProcessStartInfo(adb)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException("adb could not be started.");
        var output = new StringBuilder();
        var error = new StringBuilder();
        var reading = Task.WhenAll(Read(p.StandardOutput, output, maxChars, p), Read(p.StandardError, error, maxChars, p));
        bool exited;
        using (ct.Register(() => Stop(p))) exited = p.WaitForExit(timeoutMs);
        if (!exited) Stop(p);
        // The first adb starts adb's server, which may keep the output open: wait for the rest only a little while
        // (everything adb itself wrote is there once it has exited).
        try { reading.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { }
        if (reading.IsFaulted) throw reading.Exception!.GetBaseException();
        ct.ThrowIfCancellationRequested();
        if (!exited) throw new IOException("adb did not answer in time.");
        lock (output) lock (error) return (p.ExitCode, output.ToString(), error.ToString());
    }

    static async Task Read(StreamReader reader, StringBuilder into, int maxChars, Process p)
    {
        var buffer = new char[16384];
        int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            lock (into)
            {
                if (into.Length + n > maxChars)
                {
                    Stop(p);
                    throw new IOException("adb answered with far more than expected, so it was stopped.");
                }
                into.Append(buffer, 0, n);
            }
    }

    /// <summary>Ends this adb (only this one: adb's server, which other programs may use, keeps running).</summary>
    static void Stop(Process p)
    {
        try { if (!p.HasExited) p.Kill(); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    /// <summary>The lines of an answer (phones before Android 7 end them with \r\n).</summary>
    static IEnumerable<string> Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    /// <summary>Phones adb sees (USB and connected wireless ones), plus why others are not usable yet.</summary>
    public static List<PhoneSource> Find(string adb, List<string> notes)
    {
        var list = new List<PhoneSource>();
        var (_, output, err) = Run(adb, ["devices", "-l"], 30000);
        foreach (var line in Lines(output).Skip(1).Select(l => l.Trim()))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            string serial = parts[0], state = parts[1];
            string model = Core.IO.SafeName.Shown(parts.FirstOrDefault(p => p.StartsWith("model:"))?[6..].Replace('_', ' ') ?? serial);
            switch (state)
            {
                case "device": list.Add(new AdbPhone(adb, serial, model)); break;
                case "unauthorized": notes.Add($"{model}: allow this computer on the phone (a \"Allow USB debugging?\" question is waiting there), then look again."); break;
                case "offline": notes.Add($"{model} is offline. Unplug and plug it in again, or turn USB debugging off and on."); break;
                default: notes.Add($"{model}: {Core.IO.SafeName.Shown(state)}."); break;
            }
        }
        if (err.Contains("error", StringComparison.OrdinalIgnoreCase)) notes.Add(err.Trim());
        return list;
    }

    /// <summary>
    /// An address as the phone's Wireless debugging screen shows it: an IPv4 address or host name, or an IPv6
    /// address in brackets, then a port.
    /// </summary>
    public static bool IsAddress(string s)
    {
        var m = Regex.Match(s, @"\A(?:(?<host>[A-Za-z0-9](?:[A-Za-z0-9.\-]{0,251}[A-Za-z0-9])?)|\[(?<v6>[0-9A-Fa-f:.]{2,45}(?:%[A-Za-z0-9]{1,16})?)\]):(?<port>[0-9]{1,5})\z");
        return m.Success && int.Parse(m.Groups["port"].Value) is > 0 and <= 65535 &&
               (!m.Groups["v6"].Success || System.Net.IPAddress.TryParse(m.Groups["v6"].Value, out _));
    }

    /// <summary>The 6-digit code the phone shows for pairing.</summary>
    public static bool IsPairingCode(string s) => Regex.IsMatch(s, @"\A[0-9]{6}\z");

    public static string Pair(string adb, string hostPort, string code)
    {
        if (!IsAddress(hostPort) || !IsPairingCode(code)) throw new IOException("Type the address and the 6-digit code as the phone shows them.");
        var (_, o, e) = Run(adb, ["pair", hostPort, code], 30000);
        return (o + e).Trim();
    }

    public static string Connect(string adb, string hostPort)
    {
        if (!IsAddress(hostPort)) throw new IOException("Type the address as the phone shows it, such as 192.168.1.20:37099.");
        var (_, o, e) = Run(adb, ["connect", hostPort], 30000);
        return (o + e).Trim();
    }

    /// <summary>Text as one word for the phone's shell: in single quotes, where nothing is special (a ' inside is closed, escaped and reopened).</summary>
    static string Sh(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Runs a command in the phone's shell (as one argument, so adb hands it to the shell unchanged).</summary>
    (int Code, string Out, string Err) Shell(string command, int timeoutMs = 60000, CancellationToken ct = default, int maxChars = MaxAnswer) =>
        Run(_adb, ["-s", _serial, "shell", command], timeoutMs, ct, maxChars);

    /// <summary>How long a copy of <paramref name="bytes"/> may take: at least half an hour, and 2 MB a second at the slowest.</summary>
    static int TransferTimeout(long bytes) => (int)Math.Clamp(bytes / (2L << 20) * 1000, 30 * 60_000L, int.MaxValue);
}
