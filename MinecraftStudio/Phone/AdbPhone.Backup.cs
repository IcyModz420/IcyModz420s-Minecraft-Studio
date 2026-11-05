// Phone/AdbPhone.Backup.cs: getting worlds out of Minecraft's own app storage WITHOUT root, on Android 11 and older.
// Owns: BackupWorlds (runs Android's own "adb backup" per Minecraft package, which the person confirms on the phone)
// and ExtractWorlds (pulls each minecraftWorlds folder out of the .ab via Core's AndroidBackup reader). On Android 12+
// Android no longer backs up an ordinary app this way, so nothing comes back and the caller's notes say root is the
// only route. This only reads a backup the person allowed; it never installs, changes, downgrades or re-signs the app.
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Phone;

sealed partial class AdbPhone
{
    const string WorldsMarker = "/minecraftWorlds/";

    /// <summary>
    /// Gets worlds out of Minecraft's own app storage without root, through Android's backup. For each Minecraft
    /// package the phone shows a "Full backup" screen: the person taps "Back up my data" and leaves the password
    /// blank. Every world found is written into <paramref name="destination"/> as its own folder; their paths come
    /// back. Finds nothing on Android 12 and newer, where Android no longer backs up an ordinary app this way.
    /// </summary>
    public List<string> BackupWorlds(string destination, IProgress<string> progress, CancellationToken ct, List<string> notes)
    {
        Directory.CreateDirectory(destination);
        var copied = new List<string>();
        string host = Path.Combine(Path.GetTempPath(), "MinecraftStudio-backup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(host);
        try
        {
            foreach (var pkg in Packages)
            {
                ct.ThrowIfCancellationRequested();
                string ab = Path.Combine(host, pkg + ".ab");
                progress.Report($"On the phone: tap \"Back up my data\" and leave the password blank (for {pkg})…");
                // Android writes the .ab on this computer; app data only (no apk, obb or shared storage).
                var (code, o, e) = Run(_adb, ["-s", _serial, "backup", "-f", ab, "-noapk", "-noobb", "-noshared", pkg], 30 * 60 * 1000, ct);
                if (!File.Exists(ab) || new FileInfo(ab).Length == 0) continue; // nothing backed up (cancelled, or Android 12+)
                try { ExtractWorlds(ab, destination, ct, notes, copied); }
                catch (AndroidBackup.EncryptedException) { notes.Add("The phone put a password on the backup, so the worlds cannot be read. Run it again and leave the password box on the phone blank."); }
                catch (InvalidDataException) { notes.Add($"The backup for {pkg} could not be read (it was not in the expected form)."); }
                finally { try { File.Delete(ab); } catch (IOException) { } }
            }
            if (copied.Count == 0)
                notes.Add("Nothing came back from the app's own storage. On Android 12 and newer, Android no longer lets a computer back up an ordinary app this way — on those phones a rooted phone is the only way to reach worlds kept inside the app. On Android 11 and older, make sure you tapped \"Back up my data\" on the phone and left the password box blank.");
            return copied;
        }
        finally
        {
            try { Directory.Delete(host, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Writes every Minecraft world inside an Android backup (.ab) into <paramref name="destination"/>. Worlds sit at
    /// any ".../minecraftWorlds/&lt;world&gt;/..." in the backup. Extracts into a staging folder on the destination's
    /// own drive, then moves each world that has a level.dat into place.
    /// </summary>
    void ExtractWorlds(string ab, string destination, CancellationToken ct, List<string> notes, List<string> copied)
    {
        string staging = Path.Combine(destination, ".incoming-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            var worldDirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var raw = File.OpenRead(ab))
                AndroidBackup.Read(raw, (name, data) =>
                {
                    ct.ThrowIfCancellationRequested();
                    int idx = name.IndexOf(WorldsMarker, StringComparison.Ordinal);
                    if (idx < 0) return;
                    string after = name[(idx + WorldsMarker.Length)..];
                    int slash = after.IndexOf('/');
                    if (slash <= 0) return;                       // the folder entry itself, or a stray file
                    string worldFolder = after[..slash], rel = after[(slash + 1)..];
                    if (rel.Length == 0) return;
                    if (!SafeName.IsPlain(worldFolder) || !SafeName.IsPlainRelative(rel)) { notes.Add($"Skipped \"{SafeName.Shown(worldFolder + "/" + rel)}\": Windows cannot hold that name."); return; }
                    if (!worldDirs.TryGetValue(worldFolder, out string? wdir))
                        worldDirs[worldFolder] = wdir = Path.Combine(staging, (worldDirs.Count + 1).ToString());
                    string outPath = SafeName.Inside(wdir, rel);
                    EnsureFreeSpace(destination, data.CanSeek ? data.Length : 0);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    using var file = File.Create(outPath);
                    data.CopyTo(file);
                });

            foreach (var (worldFolder, wdir) in worldDirs)
            {
                if (!File.Exists(Path.Combine(wdir, "level.dat"))) continue;
                string dest = UniqueFolder(destination, worldFolder);
                Directory.Move(wdir, dest);
                copied.Add(dest);
            }
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Stops before a write that would nearly fill the drive (keeps about half a gigabyte free).</summary>
    static void EnsureFreeSpace(string path, long needed)
    {
        DriveInfo? drive = null;
        try { drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? ""); }
        catch (ArgumentException) { }
        if (drive is { IsReady: true } && drive.AvailableFreeSpace < needed + (512L << 20))
            throw new IOException("There is not enough free space on the drive to hold the worlds.");
    }

    static string UniqueFolder(string parent, string worldFolder)
    {
        string name = SafeName.For(worldFolder, "World");
        string path = Path.Combine(parent, name);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(parent, $"{name} ({n})");
        return path;
    }
}
