// MinecraftStudio.Core/World/PlayerStore.cs: writes an edited player's data back where it came from: a Java
// player file or the level.dat of a single-player world, a record in a Bedrock world's database, a file inside
// a console save, the level.dat of a Pocket Edition world, or the LocalPlayer inside an Indev level.
// Owns: PlayerStore.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>
/// Writes an edited player (<see cref="PlayerRecord.Data"/>) back where it came from: a Java player file or the
/// level.dat of a single-player world, a record in a Bedrock world's database, a file inside a console save, the
/// level.dat of a Pocket Edition world. The caller backs the world up first.
/// </summary>
public static class PlayerStore
{
    /// <summary>Whether players of this world can be saved.</summary>
    public static bool CanSave(SaveWorld world) => !world.OpenedForRescue && world is JavaWorld or BedrockWorld or ConsoleWorld or PocketLegacyWorld or FiniteWorld { Indev: not null };

    public static void Save(SaveWorld world, PlayerRecord player)
    {
        if (world.OpenedForRescue) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        switch (world)
        {
            case JavaWorld j when IsLevelDat(player.Source):
                // The single-player host lives in level.dat's Data.Player (the same compound the world holds).
                j.SaveLevelDat();
                break;
            case JavaWorld:
                WriteNbtFile(player.Source, player.Data);
                break;
            case BedrockWorld b:
                b.SavePlayer(player);
                b.Save(LevelDb.CommitMode.AppendLog);
                break;
            case ConsoleWorld c:
                var entry = c.Archive.Find(player.Source) ?? throw new IOException($"{player.Source} is no longer in the save.");
                // Keep the file's own compression and root name.
                var file = NbtFile.Parse(entry.Data, NbtFormat.Java);
                string rootName = file.Roots.Count > 0 ? file.Roots[0].Name : "";
                file.Roots.Clear();
                file.Roots.Add(new NamedTag(rootName, player.Data));
                entry.Data = file.ToBytes();
                c.Save();
                break;
            case PocketLegacyWorld p when IsLevelDat(player.Source):
                p.SaveLevel();
                break;
            case FiniteWorld { Indev: not null } f:
                f.SavePlayer(player);
                break;
            default:
                throw new NotSupportedException($"Players of {world.FormatDescription} worlds cannot be saved.");
        }
    }

    static bool IsLevelDat(string source) => Path.GetFileName(source).Equals("level.dat", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Java player file: gzip NBT, written through a temporary file; the previous one is kept as .dat_old like the game does.</summary>
    static void WriteNbtFile(string path, NbtCompound data)
    {
        var existing = File.Exists(path) ? NbtFile.Load(path) : null;
        var file = new NbtFile { Format = NbtFormat.Java, Compression = existing?.Compression ?? CompressionKind.Gzip };
        file.Roots.Add(new NamedTag(existing?.Roots.FirstOrDefault()?.Name ?? "", data));
        if (File.Exists(path)) File.Copy(path, path + "_old", overwrite: true);
        file.Save(path);
    }
}
