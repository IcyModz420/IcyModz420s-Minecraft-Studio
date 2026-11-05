// Conversion/LongRun.cs: making very large conversions (200 GB+ worlds) actually finish.
// Owns: EnsureRoom (refuse up front when the output drive clearly cannot hold a copy, instead of filling the disk and
// failing part-way) and KeepAwake (stop Windows sleeping during a multi-hour conversion). Both are best-effort and
// safe no-ops when they cannot apply (unknown size, drive not ready, non-Windows). Memory is already bounded
// elsewhere: the Bedrock DB reads values on demand with an LRU block cache, the Java writer keeps only 6 regions open,
// and the conversion log dedups losses by category.
using System.Runtime.InteropServices;

namespace MinecraftStudio.Core.Conversion;

public static class LongRun
{
    /// <summary>Total bytes of a world folder (or single file); 0 when it cannot be measured.</summary>
    public static long EstimateSize(string location)
    {
        try
        {
            if (File.Exists(location)) return new FileInfo(location).Length;
            if (!Directory.Exists(location)) return 0;
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(location, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return total;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// Throws before the conversion starts if the output drive plainly cannot hold a world the size of the source
    /// plus a margin — so a 200 GB world fails fast with a clear message instead of filling the disk. Skipped when
    /// the source size or the drive's free space cannot be read (then the write itself reports any out-of-space).
    /// </summary>
    public static void EnsureRoom(string sourceLocation, string output)
    {
        long sourceSize = EstimateSize(sourceLocation);
        // Only guard genuinely large, whole-world conversions; small worlds can't meaningfully fill a disk, and an
        // area-limited conversion writes far less than the source (the caller gates on that).
        if (sourceSize < (2L << 30)) return;
        long needed = sourceSize + Math.Max(1L << 30, sourceSize / 10);   // source size + max(1 GB, 10%)
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(output));
            if (string.IsNullOrEmpty(root)) return;
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;
            if (drive.AvailableFreeSpace < needed)
                throw new IOException($"Not enough free space on {drive.Name} for the converted world: it needs about " +
                    $"{Human(needed)} (the source is {Human(sourceSize)}) and {Human(drive.AvailableFreeSpace)} is free. " +
                    "Free up space or choose a folder on another drive.");
        }
        catch (ArgumentException) { }   // odd path/drive: leave it to the write to report any shortfall
    }

    static string Human(long bytes)
    {
        string[] u = ["bytes", "KB", "MB", "GB", "TB"];
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }

    /// <summary>
    /// Keeps the computer awake (no sleep, no display timeout triggering sleep) until the returned handle is disposed.
    /// A no-op on non-Windows or if the call fails.
    /// </summary>
    public static IDisposable KeepAwake()
    {
        if (OperatingSystem.IsWindows())
        {
            try { SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED); }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        }
        return new Waker();
    }

    sealed class Waker : IDisposable
    {
        public void Dispose()
        {
            if (!OperatingSystem.IsWindows()) return;
            try { SetThreadExecutionState(ES_CONTINUOUS); } catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        }
    }

    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001, ES_AWAYMODE_REQUIRED = 0x00000040;

    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint esFlags);
}
