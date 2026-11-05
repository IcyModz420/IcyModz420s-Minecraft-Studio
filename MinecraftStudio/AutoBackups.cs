// MinecraftStudio/AutoBackups.cs: worlds backed up by themselves after each play session, while the program is open.
// Owns: AutoBackups. Once a minute (when Settings.AutoBackup is on): a Java or Bedrock world of this PC the game has saved
// since the program started, newer than its newest backup, closed again (no session.lock or database lock held) and quiet
// for a minute, is zipped into the backups folder as an automatic backup — one at a time, in the background. With a limit
// set, that world's oldest automatic backups go to the Recycle Bin. A world is never backed up while the game writes it.
using MinecraftStudio.Core.World;

namespace MinecraftStudio;

sealed class AutoBackups : IDisposable
{
    readonly System.Threading.Timer _timer;
    readonly Func<IReadOnlyList<FoundWorld>> _worlds;
    readonly Action<string> _status;
    readonly DateTime _started = DateTime.Now;
    int _busy;

    public AutoBackups(Func<IReadOnlyList<FoundWorld>> worlds, Action<string> status)
    {
        _worlds = worlds;
        _status = status;
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
    }

    /// <summary>When the game last saved the world (its level.dat), or default when it can't be told.</summary>
    static DateTime LastSaved(string folder)
    {
        string level = Path.Combine(folder, "level.dat");
        return File.Exists(level) ? File.GetLastWriteTime(level) : default;
    }

    void Tick()
    {
        if (!Backup.Settings.AutoBackup || Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            foreach (var w in _worlds())
            {
                if (w.Kind is not (WorldKind.Java or WorldKind.Bedrock) || !Directory.Exists(w.Path)) continue;
                var saved = LastSaved(w.Path);
                // Played since the program started, settled for a minute, and closed in the game.
                if (saved == default || saved < _started || DateTime.Now - saved < TimeSpan.FromMinutes(1)) continue;
                if (WorldUse.InUse(w.Path) != null) continue;
                if (Backup.Of(w.Name, w.Path).FirstOrDefault() is { } newest && newest.Made >= saved) continue;
                _status($"Backing up {w.Name} by itself (it was played since its last backup)…");
                string zip = Backup.Folder(w.Name, w.Path, null, auto: true);
                int gone = Backup.Trim(w.Name, w.Path, Backup.Settings.AutoBackupKeep);
                _status($"Backed up {w.Name} by itself: {Path.GetFileName(zip)}" + (gone > 0 ? $" ({gone} older automatic backup{(gone == 1 ? "" : "s")} to the Recycle Bin)" : ""));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status("An automatic backup could not be made: " + ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Automatic backup failed", ex);
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    public void Dispose() => _timer.Dispose();
}
