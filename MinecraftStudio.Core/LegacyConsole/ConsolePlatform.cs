// MinecraftStudio.Core/LegacyConsole/ConsolePlatform.cs: the Legacy Console platforms (Xbox 360/One, PS3/4/Vita, Wii U, Switch) and their differences.
// Owns: ConsolePlatform, ConsolePlatforms.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

public enum ConsolePlatform
{
    Xbox360,
    PS3,
    WiiU,
    PSVita,
    PS4,
    XboxOne,
    Switch,
    /// <summary>Decompressed archive with no wrapper (platform unknown).</summary>
    Raw,
}

public static class ConsolePlatforms
{
    public static bool IsLittleEndian(this ConsolePlatform p) => p is ConsolePlatform.PSVita or ConsolePlatform.PS4 or ConsolePlatform.XboxOne or ConsolePlatform.Switch;

    public static string DisplayName(this ConsolePlatform p) => p switch
    {
        ConsolePlatform.Xbox360 => "Xbox 360",
        ConsolePlatform.PS3 => "PlayStation 3",
        ConsolePlatform.WiiU => "Wii U",
        ConsolePlatform.PSVita => "PS Vita",
        ConsolePlatform.PS4 => "PlayStation 4",
        ConsolePlatform.XboxOne => "Xbox One",
        ConsolePlatform.Switch => "Nintendo Switch",
        _ => "console (raw archive)",
    };
}
