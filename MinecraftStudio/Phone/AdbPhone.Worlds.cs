// Phone/AdbPhone.Worlds.cs: finding and copying worlds over adb.
// Owns: FindWorlds (shared storage and exported .mcworld files; private folders via AdbPhone.Root.cs), Copy, the
// phone's own checked list of a world's files (Listing: refuses names Windows cannot hold safely), Pull (adb pull
// into a fresh staging folder) and CheckArrived (every file whole, nothing extra, no links), then the move into place.
using System.Globalization;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Phone;

sealed partial class AdbPhone
{
    public override List<PhoneWorld> FindWorlds(List<string> notes, IProgress<string> progress, CancellationToken ct)
    {
        var worlds = new List<PhoneWorld>();
        progress.Report("Checking whether the phone is rooted");
        DetectRoot(notes, ct);
        if (_root != RootWay.None) worlds.AddRange(FindPrivateWorlds(notes, progress, ct));
        foreach (var rel in WorldFolders)
        {
            ct.ThrowIfCancellationRequested();
            string dir = "/sdcard/" + rel;
            progress.Report("Looking in " + dir);
            var (code, output, err) = Shell($"ls -1 {Sh(dir)}", ct: ct);
            string all = output + err;
            if (all.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"Android does not let a computer read {dir} on this phone. Use USB file transfer instead, or export the world in Minecraft (see below).");
                continue;
            }
            if (code != 0 || all.Contains("No such file", StringComparison.OrdinalIgnoreCase)) continue;
            notes.Add($"Looked in {dir}.");
            foreach (var folder in Lines(output))
            {
                ct.ThrowIfCancellationRequested();
                if (!SafeName.IsPlain(folder)) { notes.Add($"Skipped \"{SafeName.Shown(folder)}\" in {dir}: Windows cannot hold that name."); continue; }
                string w = dir + "/" + folder;
                var (lc, levelDat, _) = Shell($"ls {Sh(w + "/level.dat")}", ct: ct);
                if (lc != 0 || levelDat.Contains("No such file")) continue;
                var (count, kb) = Measure(w, asRoot: false, ct);
                worlds.Add(new PhoneWorld(WorldName(ReadName(w, asRoot: false, ct), folder), w, kb * 1024, count, null, false, w));
            }
        }
        foreach (var rel in ExportFolders)
        {
            string dir = "/sdcard/" + rel;
            var (code, output, _) = Shell($"ls -1 {Sh(dir)}", ct: ct);
            if (code != 0) continue;
            foreach (var file in Lines(output).Where(l => l.EndsWith(".mcworld", StringComparison.OrdinalIgnoreCase)))
            {
                if (!SafeName.IsPlain(file)) { notes.Add($"Skipped \"{SafeName.Shown(file)}\" in {dir}: Windows cannot hold that name."); continue; }
                var (_, size, _) = Shell($"stat -c %s {Sh(dir + "/" + file)}", ct: ct);
                worlds.Add(new PhoneWorld(Path.GetFileNameWithoutExtension(file), dir + "/" + file,
                    long.TryParse(size.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long s) ? s : 0, 1, null, true, dir + "/" + file));
            }
        }
        return worlds;
    }

    /// <summary>Runs a command as the shell user, or as root for a world in an app's private folder.</summary>
    (int Code, string Out, string Err) Ask(string command, bool asRoot, CancellationToken ct, int timeoutMs = 120000, int maxChars = MaxAnswer) =>
        asRoot ? AsRoot(command, timeoutMs, ct, maxChars) ?? throw new IOException("Root is no longer available on the phone.") : Shell(command, timeoutMs, ct, maxChars);

    /// <summary>The world's name from its levelname.txt, or "" (only shown; a folder on this PC gets a cleaned-up copy).</summary>
    string ReadName(string world, bool asRoot, CancellationToken ct)
    {
        try { return Ask($"cat {Sh(world + "/levelname.txt")}", asRoot, ct, 30000, 4096).Out; }
        catch (IOException) { return ""; }
    }

    static string WorldName(string fromFile, string folder)
    {
        string name = SafeName.Shown(fromFile.Trim());
        return name.Length > 0 && !name.Contains("No such file") ? name : folder;
    }

    /// <summary>How many files a world folder holds and how many KB they take.</summary>
    (int Files, long Kb) Measure(string world, bool asRoot, CancellationToken ct)
    {
        string du = Ask($"du -sk {Sh(world)}", asRoot, ct, 120000, 4096).Out;
        string count = Ask($"find {Sh(world)} -type f | wc -l", asRoot, ct, 120000, 4096).Out;
        return (int.TryParse(count.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int c) ? c : 0,
                long.TryParse(du.Split('\t', ' ').FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long k) ? k : 0);
    }

    public override void Copy(PhoneWorld world, string destination, IProgress<string> progress, CancellationToken ct)
    {
        if (world.Handle is RootPath root)
        {
            CopyAsRoot(world, root, destination, progress, ct);
            return;
        }
        string remote = (string)world.Handle;
        Pull(world, remote, destination, world.IsPackage ? null : Listing(remote, asRoot: false, ct), progress, ct);
    }

    /// <summary>
    /// The phone's own list of a world's files and their sizes (relative, / separated), to check the copy against.
    /// Every name in the world (folders too, since adb makes those as well) must be one Windows stores exactly as
    /// written, and no two may differ only in capital letters: otherwise adb would write a file somewhere other
    /// than meant (a \ in a name is a folder on Windows) or one file over another, so the world is refused.
    /// </summary>
    Dictionary<string, long> Listing(string remote, bool asRoot, CancellationToken ct)
    {
        string prefix = remote + "/";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(Ask($"find {Sh(remote)}", asRoot, ct).Out))
        {
            if (line == remote) continue;
            string rel = line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : "";
            if (!SafeName.IsPlainRelative(rel)) throw new IOException($"The world holds a file named \"{SafeName.Shown(line)}\", which Windows cannot hold safely, so it was not copied.");
            if (!names.Add(rel)) throw new IOException($"The world holds two files named \"{SafeName.Shown(rel)}\" that differ only in capital letters; Windows cannot keep both, so it was not copied.");
        }
        var expected = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(Ask($"find {Sh(remote)} -type f -exec stat -c '%s %n' {{}} +", asRoot, ct).Out))
        {
            int sp = line.IndexOf(' ');
            if (sp <= 0 || !long.TryParse(line[..sp], NumberStyles.None, CultureInfo.InvariantCulture, out long size) || !line[(sp + 1)..].StartsWith(prefix, StringComparison.Ordinal)) continue;
            string rel = line[(sp + 1 + prefix.Length)..];
            if (!names.Contains(rel)) throw new IOException($"The phone's list of the world's files changed while it was read (\"{SafeName.Shown(rel)}\"). Close Minecraft on the phone and try again.");
            expected[rel] = size;
        }
        if (expected.Count == 0) throw new IOException("The phone could not list the world's files to check the copy (its system has no find and stat tools; installing BusyBox adds them).");
        return expected;
    }

    /// <summary>
    /// Pulls a world (or .mcworld) into a fresh staging folder next to <paramref name="destination"/>, checks it
    /// against <paramref name="expected"/> (the phone's list; null for a .mcworld, checked by size), then moves it
    /// into place. Nothing is kept unless everything arrived.
    /// </summary>
    void Pull(PhoneWorld world, string remote, string destination, Dictionary<string, long>? expected, IProgress<string> progress, CancellationToken ct)
    {
        string name = remote[(remote.LastIndexOf('/') + 1)..];
        if (!SafeName.IsPlain(name)) throw new IOException($"Windows cannot hold the name \"{SafeName.Shown(name)}\", so it was not copied.");
        string parentDir = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parentDir);
        string staging = Path.Combine(parentDir, ".incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress.Report($"Copying {world.Name}…");
            var (code, o, e) = Run(_adb, ["-s", _serial, "pull", remote, staging], TransferTimeout(world.Size), ct);
            if (code != 0) throw new IOException(("adb could not copy the world: " + (e + o).Trim()).Trim());
            string copied = Path.Combine(staging, name);
            // adb was asked for one thing: anything else in the staging folder means a name was not what it seemed.
            if (Directory.EnumerateFileSystemEntries(staging, "*", new EnumerationOptions { AttributesToSkip = 0 }).Count() != 1)
                throw new IOException("adb wrote more than the world into the folder, so nothing was kept.");
            if (expected != null) CheckArrived(copied, expected);
            else if (!File.Exists(copied) || world.Size > 0 && new FileInfo(copied).Length != world.Size) throw new IOException("The .mcworld file did not arrive whole.");
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException($"{destination} already exists.");
            if (world.IsPackage) File.Move(copied, destination);
            else Directory.Move(copied, destination);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Every file on the phone's list arrived whole, nothing else did, and the copy holds no links.</summary>
    static void CheckArrived(string copied, Dictionary<string, long> expected)
    {
        if (!Directory.Exists(copied)) throw new IOException("The world did not arrive.");
        var arrived = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var all = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (var entry in new DirectoryInfo(copied).EnumerateFileSystemInfos("*", all))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"The copy holds a link ({entry.Name}) instead of a file, so nothing was kept.");
            if (entry is FileInfo f) arrived[Path.GetRelativePath(copied, f.FullName).Replace('\\', '/')] = f.Length;
        }
        foreach (var (rel, size) in expected)
        {
            if (!arrived.TryGetValue(rel, out long have)) throw new IOException($"{rel} did not arrive.");
            if (have != size) throw new IOException($"{rel} arrived incomplete.");
        }
        if (arrived.Keys.FirstOrDefault(k => !expected.ContainsKey(k)) is { } extra)
            throw new IOException($"{extra} arrived but is not on the phone's list of the world's files, so nothing was kept. Close Minecraft on the phone and try again.");
    }
}
