using System.IO.Compression;
using MinecraftStudio.Phone;

// A folder laid out like a phone: storages inside it, Minecraft's folders inside those. The real discovery, copy and
// checks run against it through the Windows Shell, exactly as they do for a USB phone.
int passed = 0, failed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("  [FAIL] " + what); } }

string root = Path.Combine(Path.GetTempPath(), "MinecraftStudio.Tests", "phone");
if (Directory.Exists(root)) Directory.Delete(root, true);
string phone = Path.Combine(root, "Fake Phone");
string storage = Path.Combine(phone, "Internal shared storage");
string modern = Path.Combine(storage, "Android", "data", "com.mojang.minecraftpe", "files", "games", "com.mojang", "minecraftWorlds");
string old = Path.Combine(storage, "games", "com.mojang", "minecraftWorlds");

// A world in the current place: level.dat, levelname.txt and a LevelDB folder with a big file.
string w1 = Path.Combine(modern, "AbCdEf+gH=");
Directory.CreateDirectory(Path.Combine(w1, "db"));
File.WriteAllBytes(Path.Combine(w1, "level.dat"), Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray());
File.WriteAllText(Path.Combine(w1, "levelname.txt"), "My Survival World");
var rng = new Random(1);
var big = new byte[3_000_000];
rng.NextBytes(big);
File.WriteAllBytes(Path.Combine(w1, "db", "000005.ldb"), big);
File.WriteAllBytes(Path.Combine(w1, "db", "CURRENT"), "MANIFEST-000004\n"u8.ToArray());
// A world in the old place, without levelname.txt.
string w2 = Path.Combine(old, "oldworld");
Directory.CreateDirectory(w2);
File.WriteAllBytes(Path.Combine(w2, "level.dat"), new byte[100]);
// Not a world (no level.dat): skipped.
Directory.CreateDirectory(Path.Combine(modern, "notaworld"));
File.WriteAllText(Path.Combine(modern, "notaworld", "readme.txt"), "x");
// An exported world in Download.
string download = Path.Combine(storage, "Download");
Directory.CreateDirectory(download);
using (var z = ZipFile.Open(Path.Combine(download, "Island.mcworld"), ZipArchiveMode.Create))
{
    using var s = z.CreateEntry("level.dat").Open();
    s.Write(new byte[500]);
}

var source = MtpPhone.ForFolder(phone, "Fake Phone");
var notes = new List<string>();
var worlds = source.FindWorlds(notes, new Progress<string>(), CancellationToken.None);
foreach (var w in worlds) Console.WriteLine($"  found: {w.Name} | {w.Where} | {w.Size} bytes | {w.Files} files | package={w.IsPackage}");
Check(worlds.Count == 3, $"3 worlds found ({worlds.Count})");
var survival = worlds.FirstOrDefault(w => w.Name == "My Survival World");
Check(survival != null && survival.Size == 3000 + big.Length + 16 + "My Survival World".Length && survival.Files == 4, "the world's name comes from levelname.txt; its size and file count are right");
Check(worlds.Any(w => w.Name == "oldworld" && w.Where.Contains("games/com.mojang")), "a world in the old folder, named after its folder");
Check(worlds.Any(w => w.IsPackage && w.Name == "Island"), "an exported .mcworld in Download");
Check(!worlds.Any(w => w.Name.Contains("notaworld")), "a folder without level.dat is not a world");

string dest = Path.Combine(root, "copies");
Directory.CreateDirectory(dest);
foreach (var w in worlds)
{
    string target = Path.Combine(dest, w.Name + (w.IsPackage ? ".mcworld" : ""));
    source.Copy(w, target, new Progress<string>(), CancellationToken.None);
}
bool Same(string a, string b) => File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
Check(Same(Path.Combine(w1, "db", "000005.ldb"), Path.Combine(dest, "My Survival World", "db", "000005.ldb")) && Same(Path.Combine(w1, "level.dat"), Path.Combine(dest, "My Survival World", "level.dat")),
    "the world arrived byte for byte");
Check(Same(Path.Combine(download, "Island.mcworld"), Path.Combine(dest, "Island.mcworld")), "the .mcworld arrived byte for byte");
Check(!Directory.EnumerateDirectories(dest).Any(d => Path.GetFileName(d).StartsWith(".incoming")), "no half-copied folders are left behind");
try
{
    source.Copy(survival!, Path.Combine(dest, "My Survival World"), new Progress<string>(), CancellationToken.None);
    Check(false, "copying onto an existing folder is refused");
}
catch (IOException) { Check(true, ""); }
Check(File.Exists(Path.Combine(dest, "My Survival World", "levelname.txt")), "the existing copy is untouched");

// Signatures: Chrome is signed by Google; a plain file is not.
string chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
if (File.Exists(chrome))
{
    Check(Signature.IsSignedBy(chrome, "Google"), "chrome.exe: a valid Google signature");
    Check(!Signature.IsSignedBy(chrome, "Microsoft"), "chrome.exe is not signed by Microsoft");
}
string unsigned = Path.Combine(root, "unsigned.exe");
File.WriteAllBytes(unsigned, new byte[1024]);
Check(!Signature.IsSignedBy(unsigned, "Google"), "an unsigned file is refused");
Check(AdbPhone.FindAdb() == null || File.Exists(AdbPhone.FindAdb()), "adb lookup");

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
