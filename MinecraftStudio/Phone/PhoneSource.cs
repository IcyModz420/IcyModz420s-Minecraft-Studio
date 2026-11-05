// MinecraftStudio/Phone/PhoneSource.cs: A Minecraft world (a world folder, or an exported .mcworld file) found
// on a phone.
// Owns: PhoneWorld, PhoneSource.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MinecraftStudio.Phone;

/// <summary>A Minecraft world (a world folder, or an exported .mcworld file) found on a phone.</summary>
sealed record PhoneWorld(string Name, string Where, long Size, int Files, DateTime? Modified, bool IsPackage, object Handle);

/// <summary>A phone this PC can see, and how to reach it.</summary>
abstract class PhoneSource
{
    public abstract string Label { get; }
    /// <summary>Finds worlds; <paramref name="notes"/> says where the program looked and what it could not open.</summary>
    public abstract List<PhoneWorld> FindWorlds(List<string> notes, IProgress<string> progress, CancellationToken ct);
    /// <summary>Copies one world into <paramref name="destination"/> (a new folder, or a .mcworld file) and checks every file arrived whole.</summary>
    public abstract void Copy(PhoneWorld world, string destination, IProgress<string> progress, CancellationToken ct);

    /// <summary>Folders (under the phone's shared storage) where Minecraft keeps worlds, newest layout first.</summary>
    public static readonly string[] WorldFolders =
    [
        "Android/data/com.mojang.minecraftpe/files/games/com.mojang/minecraftWorlds",
        "Android/data/com.mojang.minecraftedu/files/games/com.mojang/minecraftWorlds",
        "Android/data/com.mojang.minecrafttrialpe/files/games/com.mojang/minecraftWorlds",
        "games/com.mojang/minecraftWorlds",
    ];

    /// <summary>Minecraft's Android apps: Bedrock, Trial, Education, Preview.</summary>
    public static readonly string[] Packages = ["com.mojang.minecraftpe", "com.mojang.minecrafttrialpe", "com.mojang.minecraftedu", "com.mojang.minecraftpe.preview"];

    /// <summary>
    /// Where the apps keep worlds in their private folder (the "Application" storage setting, the default on newer
    /// versions), relative to the app's data folder: only root can read these.
    /// </summary>
    public static readonly string[] PrivateWorldFolders = ["files/games/com.mojang/minecraftWorlds", "games/com.mojang/minecraftWorlds"];

    /// <summary>Where exported worlds (.mcworld) usually end up.</summary>
    public static readonly string[] ExportFolders = ["Download", "Documents", "Download/Minecraft"];
}

// ====================================================================== USB file transfer (MTP)

// ====================================================================== ADB (USB debugging or wireless debugging)
