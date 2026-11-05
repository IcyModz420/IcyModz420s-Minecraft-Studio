// MinecraftStudio.Core/World/WorldUse.cs: Whether the game has a world open: changes written then would be
// overwritten when the game closes it.
// Owns: WorldUse.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LevelDb;

namespace MinecraftStudio.Core.World;

/// <summary>Whether the game has a world open: changes written then would be overwritten when the game closes it.</summary>
public static class WorldUse
{
    public const string OpenInGame = "Minecraft has this world open. Close the world in the game first: the game writes its own copy when it closes the world, which would undo these changes.";

    /// <summary>The reason the world should not be written now, or null.</summary>
    public static string? InUse(SaveWorld world)
    {
        switch (world)
        {
            case BedrockWorld b:
                return LevelDatabase.IsLocked(b.Db.Directory_) ? OpenInGame : null;
            case JavaWorld j:
                return SessionLocked(j.Folder) ? OpenInGame : null;
            default:
                return null;
        }
    }

    /// <summary>The same for a world folder that is not opened here (Java's session.lock, Bedrock's database lock).</summary>
    public static string? InUse(string folder)
    {
        string db = Path.Combine(folder, "db");
        if (Directory.Exists(db)) return LevelDatabase.IsLocked(db) ? OpenInGame : null;
        return SessionLocked(folder) ? OpenInGame : null;
    }

    // The game keeps session.lock open (1.16+) while the world is loaded.
    static bool SessionLocked(string folder)
    {
        string lockFile = Path.Combine(folder, "session.lock");
        if (!File.Exists(lockFile)) return false;
        try
        {
            using var fs = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
