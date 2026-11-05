// MinecraftStudio/Program.cs: entry point — hardens the DLL search path, refuses to run elevated, then starts the main window.
// Owns: Program.
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace MinecraftStudio;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Before anything else loads a DLL, and before any window shows.
        HardenDllSearchPath();

        if (IsElevated())
        {
            // A save editor never needs administrator rights, and running with them would only widen the damage if a
            // bad world file or a planted DLL ever ran through the program (it could then touch any file on the
            // machine, and worlds it wrote would get administrator-only permissions). So it refuses to start elevated
            // rather than carry those rights into the work. The user's worlds live in their own folders, which an
            // ordinary program can already read and write.
            MessageBox.Show(
                UI.Dialogs.AppName + " does not need administrator rights, and will not run with them for your safety.\n\n" +
                "Start it normally — not with \"Run as administrator\". Your worlds are in your own user folders, " +
                "which the program can already open without administrator rights.",
                UI.Dialogs.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message + "\n\n" + e.Exception.StackTrace, UI.Dialogs.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm(args));
    }

    /// <summary>Whether this process is running with a full administrator token (an elevated "Run as administrator").</summary>
    static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException)
        {
            // Can't tell — don't block startup over it.
            return false;
        }
    }

    /// <summary>
    /// Takes the program's start folder, the current folder and the user's PATH out of the search Windows uses to
    /// find native DLLs, leaving only Windows' own System32 and this program's own folder. This stops a DLL dropped
    /// next to the program, or in whatever folder it happened to be launched from, from being picked up in place of
    /// a real system DLL (DLL hijacking). It only affects DLLs loaded after this call, so it runs first thing.
    /// </summary>
    static void HardenDllSearchPath()
    {
        try
        {
            // System32 and the program's own folder (the .NET runtime's own native files live there), nothing else.
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_APPLICATION_DIR);
            // Also drop the current folder for the older search that SetDefaultDllDirectories does not cover.
            SetDllDirectory("");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // Very old Windows without these calls: the manifest still asks for a current OS, so this is only a guard.
        }
    }

    const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
    const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetDllDirectory(string? lpPathName);
}
