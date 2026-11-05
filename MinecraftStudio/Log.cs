// MinecraftStudio/Log.cs: Append-only diagnostic log in %APPDATA%\MinecraftStudio\log.txt.
// Owns: Log.
namespace MinecraftStudio;

/// <summary>Append-only diagnostic log in %APPDATA%\MinecraftStudio\log.txt.</summary>
static class Log
{
    static readonly object Sync = new();
    static readonly string FilePath = Path.Combine(UI.AppSettings.Folder, "log.txt");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string context, Exception ex) => Write("ERROR", $"{context}: {ex}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(UI.AppSettings.Folder);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 4 << 20) fi.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
