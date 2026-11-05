// Phone/AdbPhone.Root.cs: reading worlds from Minecraft's own private storage on a ROOTED phone.
// Owns: how root is reached (RootWay, DetectRoot, AsRoot), the private world search across every Minecraft package,
// private world folder and phone user (FindPrivateWorlds), and the copy out of the private folder (CopyAsRoot),
// which stages a world-readable copy under /data/local/tmp and then pulls it with the ordinary file check.
// Only ever reads the user's own worlds; never writes back to the app, and never touches anything but Minecraft's.
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Phone;

sealed partial class AdbPhone
{
    /// <summary>How this phone runs commands as root: not yet checked, not rooted, adb itself is root, su -c, su 0.</summary>
    enum RootWay { Unknown, None, Adbd, SuC, SuZero }
    RootWay _root = RootWay.Unknown;

    /// <summary>A world in an app's private folder, reached with root (the handle on its <see cref="PhoneWorld"/>).</summary>
    sealed record RootPath(string Path);

    /// <summary>Runs a command as root the way this phone allows, or null when the phone is not rooted.</summary>
    (int Code, string Out, string Err)? AsRoot(string command, int timeoutMs = 60000, CancellationToken ct = default, int maxChars = MaxAnswer) => _root switch
    {
        RootWay.Adbd => Shell(command, timeoutMs, ct, maxChars),
        RootWay.SuC => Shell("su -c " + Sh(command), timeoutMs, ct, maxChars),
        RootWay.SuZero => Shell("su 0 sh -c " + Sh(command), timeoutMs, ct, maxChars),
        _ => null,
    };

    /// <summary>
    /// Finds out whether commands can run as root: adb itself already root (emulators, "adb root" on debug builds),
    /// then su in the forms root managers use (Magisk, SuperSU, KernelSU: su -c; older ones: su 0). The first time,
    /// the root manager may ask on the phone whether to allow it.
    /// </summary>
    void DetectRoot(List<string> notes, CancellationToken ct)
    {
        if (_root != RootWay.Unknown) return;
        _root = RootWay.None;
        try
        {
            if (Shell("id", 15000, ct, 4096).Out.Contains("uid=0(")) { _root = RootWay.Adbd; return; }
            bool refused = false;
            foreach (var way in new[] { RootWay.SuC, RootWay.SuZero })
            {
                ct.ThrowIfCancellationRequested();
                _root = way;
                var answer = AsRoot("id", 30000, ct, 4096);
                if (answer is { } a)
                {
                    if (a.Out.Contains("uid=0(")) return;
                    refused |= (a.Out + a.Err).Contains("denied", StringComparison.OrdinalIgnoreCase) || (a.Out + a.Err).Contains("not allowed", StringComparison.OrdinalIgnoreCase);
                }
            }
            if (refused) notes.Add("The phone is rooted, but root was refused for this computer: allow \"Shell\" in the root app (Magisk, SuperSU or KernelSU) and look again.");
        }
        catch (IOException)
        {
            // su waited for an answer on the phone that did not come in time.
            notes.Add("If the phone is rooted, allow \"Shell\" when the root app asks on the phone, then look again.");
        }
        _root = RootWay.None;
    }

    /// <summary>Worlds in the apps' private folders, for every Minecraft package and every phone user (rooted phones only).</summary>
    List<PhoneWorld> FindPrivateWorlds(List<string> notes, IProgress<string> progress, CancellationToken ct)
    {
        var worlds = new List<PhoneWorld>();
        var users = AsRoot("ls -1 /data/user", 30000, ct, 1 << 16)?.Out is { } u
            ? Lines(u).Select(l => l.Trim()).Where(l => l.Length > 0 && l.All(char.IsDigit)).ToList()
            : [];
        if (users.Count == 0) users.Add("0");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in users)
            foreach (var pkg in Packages)
                foreach (var rel in PrivateWorldFolders)
                {
                    ct.ThrowIfCancellationRequested();
                    // /data/data is user 0's folder on every version; /data/user/N exists from Android 4.2 on.
                    var bases = user == "0" ? new[] { $"/data/user/0/{pkg}", $"/data/data/{pkg}" } : new[] { $"/data/user/{user}/{pkg}" };
                    foreach (string baseDir in bases)
                    {
                        string dir = baseDir + "/" + rel;
                        progress.Report("Looking in " + dir);
                        // One command per folder: every immediate subfolder that has a level.dat, as root.
                        var found = AsRoot($"for d in {Sh(dir)}/*; do [ -f \"$d/level.dat\" ] && echo \"$d\"; done", 60000, ct, 1 << 20);
                        if (found is not { } f) continue;
                        var paths = Lines(f.Out).Select(l => l.Trim()).Where(l => l.StartsWith(dir + "/", StringComparison.Ordinal)).ToList();
                        if (paths.Count == 0) continue;
                        notes.Add($"Looked in {dir} (Minecraft's private folder, read with root).");
                        foreach (var w in paths)
                        {
                            ct.ThrowIfCancellationRequested();
                            string folder = w[(w.LastIndexOf('/') + 1)..];
                            // /data/data and /data/user/0 are the same folder: each world once.
                            if (!seen.Add(pkg + "/" + rel + "/" + folder + "@" + user)) continue;
                            if (!SafeName.IsPlain(folder)) { notes.Add($"Skipped \"{SafeName.Shown(folder)}\" in {dir}: Windows cannot hold that name."); continue; }
                            var (count, kb) = Measure(w, asRoot: true, ct);
                            worlds.Add(new PhoneWorld(WorldName(ReadName(w, asRoot: true, ct), folder), w + "  (private, read with root)", kb * 1024, count, null, false, new RootPath(w)));
                        }
                    }
                }
        if (worlds.Count > 0 && Packages.Any(p => AsRoot($"pidof {Sh(p)}", 15000, ct, 4096)?.Out.Trim().Length > 0))
            notes.Add("Minecraft is running on the phone: close it before copying, so the worlds are not written to while they are copied.");
        return worlds;
    }

    /// <summary>
    /// Copies a world out of an app's private folder: as root, into a folder of its own under /data/local/tmp (which
    /// every Android version has and the computer can read), made readable to the ordinary shell user, then the normal
    /// pull from there, checked file by file against root's own list. The staged copy is removed afterwards.
    /// </summary>
    void CopyAsRoot(PhoneWorld world, RootPath source, string destination, IProgress<string> progress, CancellationToken ct)
    {
        string leaf = source.Path[(source.Path.LastIndexOf('/') + 1)..];
        if (!SafeName.IsPlain(leaf)) throw new IOException($"Windows cannot hold the name \"{SafeName.Shown(leaf)}\", so it was not copied.");
        string temp = "/data/local/tmp/mcstudio-" + Guid.NewGuid().ToString("N");
        try
        {
            progress.Report($"Copying {world.Name} out of Minecraft's private folder…");
            string own = $"chmod -R 755 {Sh(temp)} && chown -R 2000:2000 {Sh(temp)}"; // make the staged copy readable to the shell user (uid 2000)
            _ = AsRoot($"mkdir -p {Sh(temp)} && cp -R {Sh(source.Path)} {Sh(temp + "/")} && {own}", 30 * 60 * 1000, ct)
                ?? throw new IOException("Root is no longer available on the phone.");
            string staged = temp + "/" + leaf;
            if (!StagedOk(staged, ct))
            {
                // Phones whose toolbox has no cp: busybox or toybox has one.
                AsRoot($"mkdir -p {Sh(temp)} && (busybox cp -R {Sh(source.Path)} {Sh(temp + "/")} || toybox cp -R {Sh(source.Path)} {Sh(temp + "/")}) && {own}", 30 * 60 * 1000, ct);
                if (!StagedOk(staged, ct)) throw new IOException("The world could not be copied out of Minecraft's private folder.");
            }
            var expected = Listing(source.Path, asRoot: true, ct);
            ct.ThrowIfCancellationRequested();
            Pull(world with { Handle = staged }, staged, destination, expected, progress, ct);
        }
        finally
        {
            // Always clean up the staged copy, even after Stop.
            try { AsRoot($"rm -rf {Sh(temp)}", 120000, CancellationToken.None); }
            catch (IOException) { }
        }
    }

    bool StagedOk(string staged, CancellationToken ct)
    {
        var r = AsRoot($"ls {Sh(staged + "/level.dat")}", 30000, ct, 4096);
        return r is { Code: 0 } && !r.Value.Out.Contains("No such file");
    }
}
