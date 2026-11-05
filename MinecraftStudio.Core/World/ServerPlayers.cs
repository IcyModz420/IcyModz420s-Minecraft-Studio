// MinecraftStudio.Core/World/ServerPlayers.cs: a player's things moved between single-player and a server (Java).
// Owns: ServerPlayers — OfflineUuid (the UUID an offline-mode server gives a name: v3 of "OfflinePlayer:<name>", as the
// game's UUID.nameUUIDFromBytes), ParseUuid, the player folder (playerdata; players/data in 26.1+; players/<name>.dat before
// 1.7.6), CopyTo (a player's data — the single-player host's too — written as another UUID's file: for a server, or when a
// server goes from offline to online mode) and MakeHost (that player becomes the single-player one: level.dat's Player,
// or singleplayer_uuid in 26.1+). The file written carries the world's DataVersion and its own UUID; an old one stays as
// .dat_old. Names for UUIDs come from a server's usercache.json beside the world, when there is one.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static class ServerPlayers
{
    /// <summary>The UUID an offline-mode server (or a cracked launcher) gives this name.</summary>
    public static Guid OfflineUuid(string name)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3: name based
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // the IETF variant
        return new Guid(hash, bigEndian: true);
    }

    /// <summary>A UUID typed with or without dashes (or braces); null when it is not one.</summary>
    public static Guid? ParseUuid(string text)
    {
        text = text.Trim().Trim('{', '}');
        return Guid.TryParseExact(text, text.Contains('-') ? "D" : "N", out var g) ? g : null;
    }

    /// <summary>The name a player of this edition may have (letters, digits and underscores, up to 16).</summary>
    public static bool IsPlayerName(string name) => name.Length is >= 1 and <= 16 && name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    /// <summary>Whether players' things are kept per UUID (1.7.6+) rather than per name (older worlds with a players folder).</summary>
    public static bool ByUuid(JavaWorld w) =>
        w.IsNewLayout || Directory.Exists(Path.Combine(w.Folder, "playerdata")) || !Directory.Exists(Path.Combine(w.Folder, "players")) || w.Data?.ContainsKey("DataVersion") == true;

    /// <summary>The folder players' files are in: players/data (26.1+), playerdata (1.7.6+) or players (older, by name).</summary>
    public static string PlayerFolder(JavaWorld w) => Path.Combine(w.Folder, w.IsNewLayout ? Path.Combine("players", "data") : ByUuid(w) ? "playerdata" : "players");

    /// <summary>The single-player host's record: level.dat's Player, or (26.1+) the file singleplayer_uuid names.</summary>
    public static PlayerRecord? Host(JavaWorld w)
    {
        if (w.Data?.GetCompound("Player") is NbtCompound local) return w.Players.FirstOrDefault(p => ReferenceEquals(p.Data, local)) ?? w.Players.FirstOrDefault(p => IsLevelDat(p));
        if (w.Data?["singleplayer_uuid"] is NbtIntArray { Value.Length: 4 } id)
        {
            var uuid = Players.UuidFromInts(id.Value);
            return w.Players.FirstOrDefault(p => UuidOf(p) == uuid);
        }
        return null;
    }

    public static bool IsLevelDat(PlayerRecord p) => Path.GetFileName(p.Source).Equals("level.dat", StringComparison.OrdinalIgnoreCase);

    /// <summary>The UUID a player's file is named by (or its own UUID tag); null for level.dat's host without one.</summary>
    public static Guid? UuidOf(PlayerRecord p)
    {
        if (!IsLevelDat(p) && ParseUuid(Path.GetFileNameWithoutExtension(p.Source)) is Guid named) return named;
        if (p.Data["UUID"] is NbtIntArray { Value.Length: 4 } a) return Players.UuidFromInts(a.Value);
        if (p.Data.GetLongOrNull("UUIDMost") is long most && p.Data.GetLongOrNull("UUIDLeast") is long least)
            return Players.UuidFromInts([(int)(most >> 32), (int)most, (int)(least >> 32), (int)least]);
        return null;
    }

    /// <summary>The file a UUID's (or, in old worlds, a name's) things go to.</summary>
    public static string FileFor(JavaWorld w, Guid uuid, string? name = null)
    {
        if (ByUuid(w)) return Path.Combine(PlayerFolder(w), uuid.ToString("D") + ".dat");
        if (name == null || !IsPlayerName(name)) throw new ArgumentException("This world keeps players' things by name; type the player's name.");
        return Path.Combine(PlayerFolder(w), name + ".dat");
    }

    /// <summary>
    /// Writes <paramref name="from"/>'s data as the file of <paramref name="uuid"/> (and, in worlds older than 1.7.6, of
    /// <paramref name="name"/>): same inventory, ender chest, position, health, experience. Returns the file written. An
    /// existing file is kept as .dat_old (and only replaced when <paramref name="replace"/> is set).
    /// </summary>
    public static string CopyTo(JavaWorld w, PlayerRecord from, Guid uuid, string? name = null, bool replace = false)
    {
        if (w.OpenedForRescue) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        string path = FileFor(w, uuid, name);
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(from.Source), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("That is this player's own file already.");
        if (File.Exists(path) && !replace) throw new IOException($"{Path.GetFileName(path)} already holds a player's things. Choose to replace it (the old file is kept as .dat_old).");
        var data = (NbtCompound)from.Data.Clone();
        // The file's own UUID and the world's data version, so the game reads it as this player's and as current.
        data.Remove("UUID"); data.Remove("UUIDMost"); data.Remove("UUIDLeast");
        var ints = Players.UuidToInts(uuid);
        int dv = (int)(w.Data?.GetLongOrNull("DataVersion") ?? 0);
        if (dv >= 2515) data["UUID"] = new NbtIntArray(ints);
        else
        {
            data.SetLong("UUIDMost", ((long)ints[0] << 32) | (uint)ints[1]);
            data.SetLong("UUIDLeast", ((long)ints[2] << 32) | (uint)ints[3]);
        }
        if (dv > 0 && !data.ContainsKey("DataVersion")) data.SetInt("DataVersion", dv);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = new NbtFile { Format = NbtFormat.Java, Compression = CompressionKind.Gzip };
        file.Roots.Add(new NamedTag("", data));
        if (File.Exists(path)) File.Copy(path, path + "_old", overwrite: true);
        file.Save(path);
        // Read back: the copy must be the player's data under its new identity.
        var back = NbtFile.Load(path).Root as NbtCompound ?? throw new IOException("The player file did not read back.");
        if (back.GetList("Inventory")?.Count != from.Data.GetList("Inventory")?.Count) throw new IOException("The player file did not read back the same.");
        w.ReloadPlayers();
        return path;
    }

    /// <summary>
    /// Makes <paramref name="player"/> the one single-player opens with: copied into level.dat's Player (without its own UUID,
    /// so the game keeps the account's), or in 26.1+ named by singleplayer_uuid. level.dat is saved (the old one kept).
    /// </summary>
    public static void MakeHost(JavaWorld w, PlayerRecord player)
    {
        if (w.OpenedForRescue) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        if (w.Data == null) throw new InvalidOperationException("This world has no level.dat to keep the single-player player in.");
        if (IsLevelDat(player)) throw new ArgumentException("That is the single-player player already.");
        if (w.IsNewLayout)
        {
            var uuid = UuidOf(player) ?? throw new ArgumentException("That player's file is not named by a UUID.");
            w.Data["singleplayer_uuid"] = new NbtIntArray(Players.UuidToInts(uuid));
        }
        else
        {
            var data = (NbtCompound)player.Data.Clone();
            data.Remove("UUID"); data.Remove("UUIDMost"); data.Remove("UUIDLeast"); data.Remove("DataVersion");
            w.Data["Player"] = data;
        }
        w.SaveLevelDat();
        w.ReloadPlayers();
    }

    /// <summary>Names for UUIDs from a usercache.json beside the world (a server) or above its saves folder (the game).</summary>
    public static Dictionary<Guid, string> KnownNames(JavaWorld w)
    {
        var names = new Dictionary<Guid, string>();
        // A server keeps it beside its world; the game (single-player) in .minecraft, above saves.
        string? parent = Path.GetDirectoryName(w.Folder);
        foreach (var dir in new[] { parent, parent == null ? null : Path.GetDirectoryName(parent), w.Folder })
        {
            if (dir == null) continue;
            string path = Path.Combine(dir, "usercache.json");
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) continue;
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("uuid", out var u) && e.TryGetProperty("name", out var n)
                        && u.ValueKind == JsonValueKind.String && n.ValueKind == JsonValueKind.String && ParseUuid(u.GetString()!) is Guid g && IsPlayerName(n.GetString()!))
                        names[g] = n.GetString()!;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return names;
    }
}
