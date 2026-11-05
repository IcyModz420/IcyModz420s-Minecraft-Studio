// MinecraftStudio/Phone/MtpPhone.cs: A phone connected by USB in "File transfer" mode, reached through the
// Windows Shell like File Explorer does (no drivers or phone settings needed).
// Owns: MtpPhone.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MinecraftStudio.Phone;

/// <summary>
/// A phone connected by USB in "File transfer" mode, reached through the Windows Shell like File Explorer does (no
/// drivers or phone settings needed). What Android lets a computer see differs by phone and Android version.
/// </summary>
sealed class MtpPhone : PhoneSource
{
    readonly string _name, _path;
    /// <summary>For tests: a local folder laid out like a phone (storages inside it) instead of a USB device.</summary>
    readonly string? _folder;

    MtpPhone(string name, string path, string? folder = null) { _name = name; _path = path; _folder = folder; }

    internal static MtpPhone ForFolder(string folder, string name) => new(name, folder, folder);

    public override string Label => $"{_name} (USB file transfer)";

    static dynamic NewShell() => Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application") ?? throw new IOException("Windows Shell is not available."))!;

    /// <summary>Phones and other portable devices under This PC (not drives).</summary>
    public static List<PhoneSource> Find() => Sta.Run(() =>
    {
        var list = new List<PhoneSource>();
        dynamic shell = NewShell();
        try
        {
            dynamic pc = shell.NameSpace(17);
            foreach (dynamic item in pc.Items())
            {
                string path = item.Path ?? "", name = item.Name ?? "";
                bool fileSystem = item.IsFileSystem;
                // Drives are file-system items with a drive letter; portable devices are shell-only USB items.
                if (fileSystem || !(path.Contains("usb#", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\\?\", StringComparison.Ordinal))) continue;
                list.Add(new MtpPhone(name, path));
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return list;
    });

    /// <summary>A file's size (the 64-bit property; the plain one stops at 2 GB).</summary>
    static long Size(dynamic item)
    {
        try
        {
            object v = item.ExtendedProperty("System.Size");
            if (v is ulong u) return (long)u;
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is uint ui) return ui;
        }
        catch (COMException) { }
        return (long)item.Size;
    }

    static DateTime? Modified(dynamic item)
    {
        try { object d = item.ModifyDate; return d is DateTime dt && dt.Year > 1980 ? dt : null; }
        catch (COMException) { return null; }
    }

    static string FileName(dynamic item)
    {
        // Explorer can hide extensions in Name; the real file name is a property.
        try { if (item.ExtendedProperty("System.FileName") is string f && f.Length > 0) return f; } catch (COMException) { }
        return item.Name;
    }

    /// <summary>A real folder: the Shell also calls .zip files folders, but they are copied as files.</summary>
    static bool IsFolder(dynamic item)
    {
        if (!(bool)item.IsFolder) return false;
        string ext = Path.GetExtension(FileName(item)).ToLowerInvariant();
        return ext is not (".zip" or ".mcworld" or ".mcpack" or ".mcaddon" or ".mctemplate" or ".cab" or ".7z");
    }

    static dynamic? Child(dynamic folder, string name)
    {
        foreach (dynamic i in folder.Items())
            if (string.Equals(FileName(i), name, StringComparison.OrdinalIgnoreCase) || string.Equals((string)i.Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return null;
    }

    static dynamic? Walk(dynamic folder, string path)
    {
        dynamic current = folder;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            dynamic? next = Child(current, part);
            if (next is null) return null;
            dynamic found = next;
            if (!IsFolder(found)) return null;
            current = found.GetFolder;
        }
        return current;
    }

    dynamic Device(dynamic shell)
    {
        if (_folder != null) return shell.NameSpace(Path.GetDirectoryName(_folder)).ParseName(Path.GetFileName(_folder));
        dynamic pc = shell.NameSpace(17);
        foreach (dynamic item in pc.Items())
            if ((string)item.Path == _path) return item;
        throw new IOException($"{_name} is no longer connected. Plug it in again and choose File transfer on the phone.");
    }

    /// <summary>Every file below a folder: relative path and size.</summary>
    static void ListFiles(dynamic folder, string prefix, List<(string Path, long Size)> files, CancellationToken ct)
    {
        foreach (dynamic i in folder.Items())
        {
            ct.ThrowIfCancellationRequested();
            string name = FileName(i);
            if (IsFolder(i)) ListFiles(i.GetFolder, prefix + name + "/", files, ct);
            else files.Add((prefix + name, Size(i)));
        }
    }

    public override List<PhoneWorld> FindWorlds(List<string> notes, IProgress<string> progress, CancellationToken ct) => Sta.Run(() =>
    {
        var worlds = new List<PhoneWorld>();
        dynamic shell = NewShell();
        try
        {
            dynamic device = Device(shell);
            dynamic storages = device.GetFolder;
            int storageCount = 0;
            foreach (dynamic storage in storages.Items())
            {
                if (!IsFolder(storage)) continue;
                storageCount++;
                string storageName = storage.Name;
                dynamic root = storage.GetFolder;
                foreach (var rel in WorldFolders)
                {
                    progress.Report($"Looking in {storageName}/{rel}");
                    dynamic? folder = Walk(root, rel);
                    if (folder == null) continue;
                    foreach (dynamic w in folder.Items())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!IsFolder(w)) continue;
                        dynamic wf = w.GetFolder;
                        var files = new List<(string Path, long Size)>();
                        ListFiles(wf, "", files, ct);
                        if (!files.Any(f => f.Path.Equals("level.dat", StringComparison.OrdinalIgnoreCase))) continue;
                        string name = ReadText(shell, w, "levelname.txt") ?? (string)w.Name;
                        worlds.Add(new PhoneWorld(name, $"{storageName}/{rel}/{FileName(w)}", files.Sum(f => f.Size), files.Count, Modified(w), false, FileName(w)));
                    }
                    notes.Add($"Looked in {storageName}/{rel}.");
                }
                foreach (var rel in ExportFolders)
                {
                    dynamic? folder = Walk(root, rel);
                    if (folder == null) continue;
                    foreach (dynamic f in folder.Items())
                    {
                        string fn = FileName(f);
                        if (IsFolder(f) || !fn.EndsWith(".mcworld", StringComparison.OrdinalIgnoreCase)) continue;
                        worlds.Add(new PhoneWorld(Path.GetFileNameWithoutExtension(fn), $"{storageName}/{rel}/{fn}", Size(f), 1, Modified(f), true, fn));
                    }
                }
            }
            if (storageCount == 0) notes.Add("The phone shows no storage. Unlock it and choose File transfer (or MTP) in the USB notification, then look again.");
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return worlds;
    });

    static string? ReadText(dynamic shell, dynamic folderItem, string fileName)
    {
        dynamic? file = Child(folderItem.GetFolder, fileName);
        if (file == null) return null;
        string temp = Path.Combine(Path.GetTempPath(), "MinecraftStudio-phone-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            shell.NameSpace(temp).CopyHere(file, 4 | 16 | 512 | 1024);
            string local = Path.Combine(temp, fileName);
            for (int i = 0; i < 50 && !File.Exists(local); i++) Sta.Wait(100);
            return File.Exists(local) ? File.ReadAllText(local, Encoding.UTF8).Trim() is { Length: > 0 } s ? s : null : null;
        }
        catch (IOException) { return null; }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }

    public override void Copy(PhoneWorld world, string destination, IProgress<string> progress, CancellationToken ct) => Sta.Run(() =>
    {
        dynamic shell = NewShell();
        try
        {
            // Find the world again (the list may be a while old).
            string[] parts = world.Where.Split('/');
            dynamic device = Device(shell);
            dynamic? root = null;
            foreach (dynamic s in device.GetFolder.Items())
                if ((string)s.Name == parts[0]) root = s.GetFolder;
            if (root == null) throw new IOException($"The phone's storage \"{parts[0]}\" is gone. Is the phone still connected and unlocked?");
            dynamic? parent = Walk(root, string.Join('/', parts[1..^1]));
            dynamic? item = parent == null ? null : Child(parent, parts[^1]);
            if (item == null) throw new IOException($"{world.Where} is no longer on the phone.");
            var expected = new List<(string Path, long Size)>();
            if (world.IsPackage) expected.Add((parts[^1], Size(item)));
            else ListFiles(item.GetFolder, "", expected, ct);

            // Copy into a fresh folder next to the destination, check every file, then put it in place.
            string parentDir = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(parentDir);
            string staging = Path.Combine(parentDir, ".incoming-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(staging);
            try
            {
                shell.NameSpace(staging).CopyHere(item, 4 | 16 | 512 | 1024);
                string copied = Path.Combine(staging, parts[^1]);
                long total = expected.Sum(e => e.Size), last = -1;
                var stalled = Stopwatch.StartNew();
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    long done = 0;
                    bool complete = true;
                    foreach (var (rel, size) in expected)
                    {
                        string local = world.IsPackage ? copied : Path.Combine(copied, rel.Replace('/', Path.DirectorySeparatorChar));
                        long have = File.Exists(local) ? new FileInfo(local).Length : 0;
                        done += Math.Min(have, size);
                        if (have != size) complete = false;
                    }
                    if (complete) break;
                    if (done != last) { last = done; stalled.Restart(); progress.Report($"Copying {world.Name}: {Core.IO.BinaryUtil.FormatSize(done)} of {Core.IO.BinaryUtil.FormatSize(total)}"); }
                    if (stalled.Elapsed > TimeSpan.FromMinutes(2)) throw new IOException("The copy stopped making progress. Is the phone still connected and unlocked?");
                    Sta.Wait(250);
                }
                // Nothing extra, nothing missing.
                if (!world.IsPackage)
                {
                    var local = Directory.EnumerateFiles(copied, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(copied, f).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var missing = expected.Where(e => !local.Contains(e.Path)).ToList();
                    if (missing.Count > 0) throw new IOException($"{missing.Count} files did not arrive (first: {missing[0].Path}).");
                }
                if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException($"{destination} already exists.");
                if (world.IsPackage) File.Move(copied, destination);
                else Directory.Move(copied, destination);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return true;
    });
}
