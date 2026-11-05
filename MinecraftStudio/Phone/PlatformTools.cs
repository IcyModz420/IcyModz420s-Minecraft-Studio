// MinecraftStudio/Phone/PlatformTools.cs: Google's Android platform tools (adb.exe), fetched only when the
// player asks: from Google's own download server over HTTPS, unpacked into this program's folder, and used only
// if adb.exe carries a valid signature from Google.
// Owns: PlatformTools, Signature.
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MinecraftStudio.Phone;

/// <summary>
/// Google's Android platform tools (adb.exe), fetched only when the player asks: from Google's own download server over
/// HTTPS, unpacked into this program's folder, and used only if adb.exe carries a valid signature from Google.
/// </summary>
static class PlatformTools
{
    public const string Url = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    const long MaxSize = 100L * 1024 * 1024;

    public static async Task<string> Download(IProgress<string> progress, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), "MinecraftStudio-platform-tools-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            string zipPath = Path.Combine(temp, "platform-tools.zip");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftStudio/1.0");
                using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct);
                // Only Google's server, only over HTTPS (also after redirects).
                var final = response.RequestMessage?.RequestUri;
                if (final == null || final.Scheme != Uri.UriSchemeHttps || !(final.Host == "dl.google.com" || final.Host.EndsWith(".google.com") || final.Host.EndsWith(".googleapis.com")))
                    throw new IOException($"The download was redirected to {final}, which is not Google's server; it was stopped.");
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                if (total > MaxSize) throw new IOException("The download is far larger than expected; it was stopped.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zipPath);
                var buffer = new byte[81920];
                long done = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    done += n;
                    if (done > MaxSize) throw new IOException("The download is far larger than expected; it was stopped.");
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    progress.Report(total is long t ? $"Downloading Google's platform tools: {done * 100 / Math.Max(1, t)}%" : $"Downloading Google's platform tools: {done / 1024} KB");
                }
            }
            progress.Report("Unpacking and checking the platform tools…");
            string staging = Path.Combine(temp, "unpacked");
            string stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith("platform-tools/", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith('/')) continue;
                    string dest = Path.GetFullPath(Path.Combine(staging, e.FullName["platform-tools/".Length..]));
                    if (!dest.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase)) throw new IOException("The download holds a file that would land outside its folder; it was not used.");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }
            string adb = Path.Combine(staging, "adb.exe");
            if (!File.Exists(adb)) throw new IOException("The download does not contain adb.exe.");
            if (!Signature.IsSignedBy(adb, "Google"))
                throw new IOException("adb.exe in the download is not signed by Google, so it was not used.");
            string target = AdbPhone.ToolsFolder;
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(staging, target);
            return Path.Combine(target, "adb.exe");
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Windows' own check of a program's digital signature (Authenticode).</summary>
static class Signature
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData, pSIPClientData;
        public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData, pwszURLReference;
        public uint dwProvFlags, dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref TrustData data);

    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>Whether the file has a valid signature whose signer's organisation contains <paramref name="organization"/>.</summary>
    public static bool IsSignedBy(string path, string organization)
    {
        var file = new FileInfo { cbStruct = (uint)Marshal.SizeOf<FileInfo>(), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = new TrustData
            {
                cbStruct = (uint)Marshal.SizeOf<TrustData>(),
                dwUIChoice = 2,          // no UI
                fdwRevocationChecks = 0,
                dwUnionChoice = 1,       // a file
                pFile = pFile,
                dwStateAction = 0,
            };
            if (WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) != 0) return false;
        }
        finally
        {
            Marshal.DestroyStructure<FileInfo>(pFile);
            Marshal.FreeHGlobal(pFile);
        }
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            string org = cert.GetNameInfo(X509NameType.SimpleName, false) + " " + cert.Subject;
            return org.Contains(organization, StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }
}
