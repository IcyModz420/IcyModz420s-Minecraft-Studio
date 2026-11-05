// McTests/ServerTests.cs: "server" — a player's things moved between single-player and a server (Java).
// Owns: ServerTests — offline-mode UUIDs as the game makes them ("Notch", "jeb_"), UUIDs typed either way; on copies of
// the Java 1.16.5 and 1.12.2 sample worlds: the host's data copied to an offline player's file (UUID tag and DataVersion
// of the world, same inventory, a .dat_old kept when replacing, refused over the player's own file or without "replace"),
// reopened as a player of the world; that player made the single-player one (level.dat's Player without a UUID of its
// own) and reopened the same; the originals unchanged.
using System.Security.Cryptography;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class ServerTests
{
    public static void Run(string[] args)
    {
        Program.Check(ServerPlayers.OfflineUuid("Notch") == Guid.Parse("b50ad385-829d-3141-a216-7e7d7539ba7f")
            && ServerPlayers.OfflineUuid("jeb_") == Guid.Parse("a762f560-4fce-3236-812a-b80efff0b62b"),
            $"offline-mode UUIDs as the game makes them (Notch → {ServerPlayers.OfflineUuid("Notch")})");
        Program.Check(ServerPlayers.ParseUuid("b50ad385829d3141a2167e7d7539ba7f") == ServerPlayers.ParseUuid("{B50AD385-829D-3141-A216-7E7D7539BA7F}")
            && ServerPlayers.ParseUuid("not a uuid") == null, "a UUID typed with or without dashes reads the same; other text is not one");
        Program.Check(ServerPlayers.IsPlayerName("Steve_99") && !ServerPlayers.IsPlayerName("bad name") && !ServerPlayers.IsPlayerName("..\\x") && !ServerPlayers.IsPlayerName(new string('a', 17)),
            "player names: letters, digits and underscores, up to 16");
        foreach (var (label, source) in new[]
        {
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
        })
        {
            if (!Directory.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            var before = Hashes(source);
            string copy = Path.Combine(TestPaths.Output, "server", string.Concat(label.Where(char.IsLetterOrDigit)));
            if (Directory.Exists(copy)) Directory.Delete(copy, true);
            CopyDir(source, copy);
            try { One(label, copy); }
            catch (Exception ex) { Program.Check(false, $"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
            finally { try { Directory.Delete(copy, true); } catch (IOException) { } }
            Program.Check(before.SequenceEqual(Hashes(source)), $"{label}: the original world is byte for byte unchanged");
        }
    }

    static void One(string label, string folder)
    {
        var steve = ServerPlayers.OfflineUuid("Steve");
        int inventory;
        string written;
        using (var w = (JavaWorld)WorldLoader.Open(folder))
        {
            var host = ServerPlayers.Host(w);
            Program.Check(host != null, $"{label}: the single-player player is found ({host?.Name})");
            if (host == null) return;
            inventory = host.Data.GetList("Inventory")?.Count ?? 0;
            int dv = (int)(w.Data?.GetLongOrNull("DataVersion") ?? 0);
            written = ServerPlayers.CopyTo(w, host, steve);
            var file = (NbtCompound)NbtFile.Load(written).Root!;
            bool uuidRight = dv >= 2515 ? file["UUID"] is NbtIntArray a && Players.UuidFromInts(a.Value) == steve
                : file.GetLongOrNull("UUIDMost") is long m && file.GetLongOrNull("UUIDLeast") is long l && Players.UuidFromInts([(int)(m >> 32), (int)m, (int)(l >> 32), (int)l]) == steve;
            Program.Check(Path.GetFileName(written) == steve.ToString("D") + ".dat" && uuidRight && file.GetInt("DataVersion") == dv && (file.GetList("Inventory")?.Count ?? 0) == inventory,
                $"{label}: the host's things are written as offline \"Steve\" ({Path.GetFileName(written)}: its own UUID{(dv >= 2515 ? "" : " as UUIDMost/UUIDLeast")}, DataVersion {dv}, {inventory} inventory slots)");
            Program.Check(w.Players.Any(p => p.Source == written), $"{label}: the world lists the new player at once");
            Program.Check(Throws(() => ServerPlayers.CopyTo(w, host, steve)), $"{label}: writing over an existing player's file needs \"replace\"");
            var stevePlayer = w.Players.First(p => p.Source == written);
            Program.Check(Throws(() => ServerPlayers.CopyTo(w, stevePlayer, steve, replace: true)), $"{label}: a player cannot be copied over their own file");
            ServerPlayers.CopyTo(w, host, steve, replace: true);
            Program.Check(File.Exists(written + "_old"), $"{label}: replacing keeps the old file as .dat_old");
            // Steve becomes the single-player player.
            ServerPlayers.MakeHost(w, w.Players.First(p => p.Source == written));
            var newHost = ServerPlayers.Host(w);
            Program.Check(newHost != null && ServerPlayers.IsLevelDat(newHost) && !newHost.Data.ContainsKey("UUID") && !newHost.Data.ContainsKey("UUIDMost") && !newHost.Data.ContainsKey("DataVersion"),
                $"{label}: made the single-player player: level.dat's Player, without a UUID of its own (the game keeps the account's)");
        }
        using (var again = (JavaWorld)WorldLoader.Open(folder))
        {
            var host = ServerPlayers.Host(again);
            Program.Check(host != null && (host.Data.GetList("Inventory")?.Count ?? 0) == inventory && again.Players.Any(p => Path.GetFileName(p.Source) == Path.GetFileName(written)),
                $"{label}: reopened, the single-player player and Steve's file are there with the same things");
        }
    }

    static bool Throws(Action a) { try { a(); return false; } catch (Exception ex) when (ex is IOException or ArgumentException) { return true; } }

    static List<string> Hashes(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => Path.GetRelativePath(folder, f) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToList();

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
