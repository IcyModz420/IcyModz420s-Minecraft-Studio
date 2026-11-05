// MinecraftStudio/Backup.cs: Backups taken before anything is written to a save, and the backups of a world.
// Owns: Backup, BackupEntry. Each world zipped whole into the backups folder as "<name>_<date-time>.zip" ("_auto" for the
// automatic ones); index.json there says which world folder each came from (worlds on one PC often share a name; older
// backups without an entry are matched by name). Of() lists a world's backups, newest first; RestoreAsNewWorld unpacks
// one beside the world as a new world (never over it); Trim sends a world's oldest automatic backups to the Recycle Bin.
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio;

/// <summary>One backup of a world: its zip, when it was made, its size, whether it was automatic, and whether it was matched only by the world's name.</summary>
sealed record BackupEntry(string Path, DateTime Made, long Size, bool Auto, bool ByName);

/// <summary>Backups taken before anything is written to a save, and the backups of a world.</summary>
static class Backup
{
    static readonly HashSet<string> BackedUpThisSession = new(StringComparer.OrdinalIgnoreCase);

    public static AppSettings Settings { get; set; } = AppSettings.Load();

    static string Stamp => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

    /// <summary>Copies a single file once per session before it is first overwritten.</summary>
    public static void FileBeforeWrite(string path)
    {
        if (!Settings.BackupBeforeSave || !File.Exists(path) || !BackedUpThisSession.Add(path)) return;
        Directory.CreateDirectory(Settings.BackupFolder);
        string dest = Path.Combine(Settings.BackupFolder, $"{SafeName.For(Path.GetFileName(path), "file")}_{Stamp}{Path.GetExtension(path)}.bak");
        File.Copy(path, dest, true);
    }

    /// <summary>
    /// Zips a whole world (folder or single file) into the backup folder and returns the zip path. A backup that could
    /// not read every file throws (and is deleted), so nothing is written to a world without a complete backup.
    /// </summary>
    public static string World(SaveWorld world, IProgress<string>? progress = null) => Folder(world.Name, world.Location, progress, auto: false);

    /// <summary>The same for a world by its name and folder (or save file); an automatic one's name ends in "_auto".</summary>
    public static string Folder(string name, string location, IProgress<string>? progress, bool auto)
    {
        Directory.CreateDirectory(Settings.BackupFolder);
        string zipPath = Path.Combine(Settings.BackupFolder, $"{SafeName.For(name, "World")}_{Stamp}{(auto ? "_auto" : "")}.zip");
        if (File.Exists(zipPath)) throw new IOException("A backup of this world was made this second already.");
        var unreadable = new List<string>();
        int written = 0;
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                if (File.Exists(location))
                {
                    zip.CreateEntryFromFile(location, Path.GetFileName(location), CompressionLevel.Fastest);
                    written++;
                }
                else
                {
                    var files = Directory.EnumerateFiles(location, "*", SearchOption.AllDirectories).ToList();
                    foreach (var f in files)
                    {
                        if (Path.GetFileName(f).Equals("LOCK", StringComparison.OrdinalIgnoreCase)) continue;
                        string rel = Path.GetRelativePath(location, f).Replace('\\', '/');
                        try
                        {
                            using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            var entry = zip.CreateEntry(rel, CompressionLevel.Fastest);
                            entry.LastWriteTime = File.GetLastWriteTime(f);
                            using var dst = entry.Open();
                            src.CopyTo(dst);
                            written++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable.Add($"{rel}: {ex.Message}"); }
                        if ((written & 31) == 0) progress?.Report($"Backing up {written:N0}/{files.Count:N0} files");
                    }
                }
            }
            if (unreadable.Count > 0)
                throw new IOException($"The backup could not read {unreadable.Count} file{(unreadable.Count == 1 ? "" : "s")} of the world, so nothing was changed.\n\n"
                    + string.Join("\n", unreadable.Take(5)) + "\n\nClose Minecraft (or anything else using the world) and try again.");
            // The zip must open and hold every file before the world is touched.
            using (var check = ZipFile.OpenRead(zipPath))
                if (check.Entries.Count != written) throw new IOException("The backup zip did not come out complete, so nothing was changed.");
        }
        catch
        {
            try { File.Delete(zipPath); } catch { }
            throw;
        }
        BackedUpThisSession.Add(location);
        Record(zipPath, location);
        return zipPath;
    }

    // ------------------------------------------------------------------ the backups of a world

    static readonly object IndexLock = new();
    static string IndexFile => Path.Combine(Settings.BackupFolder, "index.json");

    static Dictionary<string, string> LoadIndex()
    {
        try { return File.Exists(IndexFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(IndexFile)) ?? [] : []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    static void Record(string zipPath, string location)
    {
        lock (IndexLock)
        {
            var index = LoadIndex();
            index[Path.GetFileName(zipPath)] = Path.GetFullPath(location);
            try { SafeFile.WriteAllBytes(IndexFile, JsonSerializer.SerializeToUtf8Bytes(index, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>A world's backups in the backups folder, newest first.</summary>
    public static List<BackupEntry> Of(string name, string location)
    {
        var list = new List<BackupEntry>();
        if (!Directory.Exists(Settings.BackupFolder)) return list;
        var index = LoadIndex();
        string full = Path.GetFullPath(location);
        var pattern = new Regex("^" + Regex.Escape(SafeName.For(name, "World")) + @"_(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})(_auto)?\.zip$", RegexOptions.IgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Settings.BackupFolder, "*.zip"))
        {
            var m = pattern.Match(Path.GetFileName(file));
            if (!m.Success) continue;
            bool known = index.TryGetValue(Path.GetFileName(file), out var from);
            if (known && !string.Equals(from, full, StringComparison.OrdinalIgnoreCase)) continue;
            if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd_HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out var made)) continue;
            list.Add(new BackupEntry(file, made, new FileInfo(file).Length, m.Groups[2].Success, !known));
        }
        return list.OrderByDescending(b => b.Made).ToList();
    }

    /// <summary>
    /// A backup unpacked beside the world as a NEW world ("folder (restored date)"; a Bedrock world's shown name says so too);
    /// returns its path. Every entry must land inside the new folder; the world itself is never touched.
    /// </summary>
    public static string RestoreAsNewWorld(BackupEntry backup, string location, IProgress<string>? progress = null)
    {
        string full = Path.GetFullPath(location).TrimEnd('\\');
        string parent = Path.GetDirectoryName(full)!;
        bool file = File.Exists(full);
        string stem = (file ? Path.GetFileNameWithoutExtension(full) : Path.GetFileName(full)) + $" (restored {backup.Made:yyyy-MM-dd HH-mm})";
        string target = Path.Combine(parent, stem);
        for (int i = 2; Directory.Exists(target) || File.Exists(target); i++) target = Path.Combine(parent, $"{stem} {i}");
        using var zip = ZipFile.OpenRead(backup.Path);
        long size = zip.Entries.Sum(e => e.Length);
        var drive = new DriveInfo(Path.GetPathRoot(target)!);
        if (drive.IsReady && drive.AvailableFreeSpace < size + (64L << 20)) throw new IOException($"Not enough free space on {drive.Name} to unpack the backup ({size / 1048576.0:N0} MB).");
        Directory.CreateDirectory(target);
        try
        {
            int done = 0;
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                string to = SafeName.Inside(target, e.FullName) ?? throw new IOException($"The backup holds a file that would land outside the new world ({e.FullName}).");
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                e.ExtractToFile(to);
                if ((++done & 63) == 0) progress?.Report($"Unpacking the backup: {done:N0} of {zip.Entries.Count:N0} files");
            }
        }
        catch
        {
            try { Directory.Delete(target, true); } catch (IOException) { }
            throw;
        }
        // A Bedrock world shows its levelname.txt in the game's list: say it is the restored one.
        string levelName = Path.Combine(target, "levelname.txt");
        if (File.Exists(levelName)) File.WriteAllText(levelName, File.ReadAllText(levelName).Trim() + $" (restored {backup.Made:yyyy-MM-dd HH:mm})");
        // A single-file world (a console save) comes back as that file, in its new folder.
        return file && Directory.GetFiles(target) is [var only] ? only : target;
    }

    /// <summary>A world's oldest automatic backups past <paramref name="keep"/> sent to the Recycle Bin (0 keeps them all).</summary>
    public static int Trim(string name, string location, int keep)
    {
        if (keep <= 0) return 0;
        int gone = 0;
        foreach (var b in Of(name, location).Where(b => b.Auto && !b.ByName).Skip(keep))
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(b.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                gone++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        }
        return gone;
    }

    /// <summary>Backs up the world once per session (if enabled) before the first save.</summary>
    public static string? WorldBeforeSave(SaveWorld world, IProgress<string>? progress = null)
    {
        if (!Settings.BackupBeforeSave || BackedUpThisSession.Contains(world.Location)) return null;
        return World(world, progress);
    }
}
