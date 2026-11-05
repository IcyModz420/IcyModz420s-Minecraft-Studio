// UI/Explorer.cs: shows a folder or file in File Explorer.
// Owns: Explorer.Open (a folder) and Explorer.Select (a file or folder, highlighted in its parent).
// Starts Windows' own explorer.exe by its full path, never a bare "explorer.exe" (Windows looks for that in the
// current folder first, where another program could be waiting under that name).
using System.Diagnostics;

namespace MinecraftStudio.UI;

static class Explorer
{
    static string Exe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>Opens <paramref name="folder"/> in File Explorer.</summary>
    public static void Open(string folder) => Start(Quoted(folder));

    /// <summary>Opens the folder holding <paramref name="path"/> with it selected.</summary>
    public static void Select(string path) => Start("/select," + Quoted(path));

    /// <summary>
    /// A full path for explorer's command line: without a separator at the end (explorer would read \" as a quote),
    /// in quotes when it has a space or comma. Windows paths never hold a quote themselves; a drive root (C:\) has
    /// no space, so it is never quoted.
    /// </summary>
    static string Quoted(string path)
    {
        string full = Path.GetFullPath(path);
        if (Path.GetPathRoot(full) != full) full = Path.TrimEndingDirectorySeparator(full);
        return full.Contains(' ') || full.Contains(',') ? $"\"{full}\"" : full;
    }

    static void Start(string arguments)
    {
        try { Process.Start(new ProcessStartInfo(Exe, arguments) { UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
