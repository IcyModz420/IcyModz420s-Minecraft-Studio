// MinecraftStudio.Core/World/PlayerMoveCopy.cs: a player moved to safety in a NEW copy of the world (the command line's
// move; the app moves in place, after a backup).
// Owns: PlayerMoveCopy.Run — the checks placing a structure makes (not open in the game, an empty new folder outside the
// world, room on the drive), the world copied byte for byte, the same player found in the copy and moved to the place
// chosen (HardcoreRestore), then read back from disk: standing there, in the overworld. The world itself is only read.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

public static class PlayerMoveCopy
{
    /// <summary>What a move into a copy did: the copy, where the player stands, what changed, and what the check found.</summary>
    public sealed record Result(string OpenPath, HardcoreRestore.Spot Spot, IReadOnlyList<string> Done, IReadOnlyList<string> Problems)
    {
        public bool Verified => Problems.Count == 0;
    }

    /// <summary>
    /// Copies <paramref name="world"/> to <paramref name="output"/> and moves <paramref name="players"/> there (one person's
    /// records: a single-player Java world keeps them in level.dat and in playerdata) to the place <paramref name="pick"/>
    /// finds in the copy for the first of them (null: no safe ground, and nothing is left behind).
    /// </summary>
    public static Result Run(SaveWorld world, IReadOnlyList<PlayerRecord> players, Func<SaveWorld, PlayerRecord, HardcoreRestore.Spot?> pick, string output)
    {
        if (players.Count == 0) throw new ArgumentException("No player to move.");
        if (!HardcoreRestore.CanMove(world)) throw new NotSupportedException("Players can be moved in Java, Bedrock, console, Pocket Edition 0.x and Indev worlds.");
        if (world.OpenedForRescue) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        if (WorldUse.InUse(world) is string busy) throw new IOException(busy + " Close it first, so the copy is made from a world that is not being written to.");
        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("A file with that name already exists; choose another folder.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("That folder is not empty; choose a new folder name.");
        string source = Path.GetFullPath(world.Location).TrimEnd('\\');
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The copy cannot go inside the world.");
        StructurePlacement.EnsureRoomForCopy(world.Location, output);
        string path = StructurePlacement.CopyWorld(world.Location, output, null, default);
        try
        {
            var keys = players.Select(p => Key(world, p.Source)).ToList();
            HardcoreRestore.Spot at;
            List<string> done;
            using (var copy = WorldLoader.Open(path))
            {
                var records = keys.Select(k => copy.Players.FirstOrDefault(r => Key(copy, r.Source) == k) ?? throw new IOException("The player is not in the copy of the world.")).ToList();
                at = pick(copy, records[0]) ?? throw new IOException("No safe ground there: that part of the world isn't generated, or it is all water, lava or trees.");
                done = [];
                foreach (var r in records)
                    foreach (var line in HardcoreRestore.MoveAndSave(copy, r, at))
                        if (!done.Contains(line)) done.Add(line);
            }
            var problems = new List<string>();
            using (var check = WorldLoader.Open(path))
                foreach (var key in keys)
                {
                    var p = check.Players.FirstOrDefault(r => Key(check, r.Source) == key);
                    if (p == null) { problems.Add($"{key}: could not be read back from the copy."); continue; }
                    var pos = p.Data.GetList("Pos");
                    double feet = HardcoreRestore.EyeHeight(check);
                    if (pos is not { Count: 3 } || Math.Abs(pos[0].AsDouble - (at.X + 0.5)) > 1e-3 || Math.Abs(pos[1].AsDouble - (at.Y + feet)) > 1e-3 || Math.Abs(pos[2].AsDouble - (at.Z + 0.5)) > 1e-3)
                        problems.Add($"{p.Name} read back from the copy is not standing at {at}.");
                    bool overworld = check is BedrockWorld
                        ? p.Data.GetLong("DimensionId") == 0
                        : p.Data["Dimension"] switch { NbtString s => s.Value is "minecraft:overworld" or "overworld", { IsNumeric: true } n => n.AsLong == 0, _ => true };
                    if (!overworld) problems.Add($"{p.Name} read back from the copy is not in the overworld.");
                }
            return new Result(path, at, done, problems);
        }
        catch
        {
            StructurePlacement.TryDelete(output);
            throw;
        }
    }

    // A player's place in a world, the same in a copy: a file relative to the world's folder (whether the world was opened by
    // a full path or a relative one); else a name inside the save as it is (a database key, a console save's players/…,
    // Indev's Entities/LocalPlayer).
    static string Key(SaveWorld w, string source) =>
        Path.IsPathRooted(source)
            ? Path.GetRelativePath(Path.GetFullPath(w.Location), Path.GetFullPath(source)).Replace('/', '\\').ToLowerInvariant()
            : source;
}
