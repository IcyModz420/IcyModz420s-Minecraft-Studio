// MinecraftStudio/UI/AppSettings.cs: Per-user settings in %APPDATA%\MinecraftStudio\settings.json.
// Owns: AppSettings.
using System.Text.Json;

namespace MinecraftStudio.UI;

/// <summary>Per-user settings in %APPDATA%\MinecraftStudio\settings.json.</summary>
sealed class AppSettings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftStudio");

    /// <summary>Settings of the app's earlier name (Minecraft Save Studio), carried over once.</summary>
    static readonly string OldFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftSaveStudio");
    static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public List<string> Recent { get; set; } = new();
    public List<string> ExtraWorldFolders { get; set; } = new();
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool Maximized { get; set; }
    public int TreeWidth { get; set; } = 300;
    public bool BackupBeforeSave { get; set; } = true;
    /// <summary>Back up each world by itself after a play session (the game saved it, then closed it) while the program is open.</summary>
    public bool AutoBackup { get; set; }
    /// <summary>How many automatic backups of each world are kept (older ones go to the Recycle Bin); 0 keeps them all.</summary>
    public int AutoBackupKeep { get; set; }
    /// <summary>The Convert window's block rules ("pattern → block", one each), kept for the next conversion.</summary>
    public List<string> ConvertBlockRules { get; set; } = new();
    public string? LastConvertTarget { get; set; }
    public string BackupFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio Backups");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            // First start under the new name: keep the recent worlds, folders and backup location chosen before.
            string old = Path.Combine(OldFolder, "settings.json");
            if (File.Exists(old) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(old)) is AppSettings carried)
            {
                carried.Save();
                return carried;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > 15) Recent.RemoveRange(15, Recent.Count - 15);
        Save();
    }
}
