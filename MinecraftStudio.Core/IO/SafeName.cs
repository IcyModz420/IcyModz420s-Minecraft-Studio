// MinecraftStudio.Core/IO/SafeName.cs: File and folder names from text nobody checked: world names from save
// files, pack names, names on a phone.
// Owns: SafeName.
namespace MinecraftStudio.Core.IO;

/// <summary>
/// File and folder names from text nobody checked: world names from save files, pack names, names on a phone.
/// <see cref="For"/> makes any text into one name Windows can hold; <see cref="IsPlain"/> and
/// <see cref="IsPlainRelative"/> check names that must be used exactly as they are (a name that fails is refused,
/// never "fixed", so a copy cannot land somewhere other than meant).
/// </summary>
public static class SafeName
{
    static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>Device names Windows reserves in every folder, also with an extension (CON.txt is the console).</summary>
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// <paramref name="text"/> as one file or folder name: characters Windows does not allow become _, dots and spaces
    /// at the ends go, a reserved device name gets a _ in front, and it is at most <paramref name="maxLength"/> long.
    /// Never a path: no separators, never "." or "..".
    /// </summary>
    public static string For(string? text, string fallback, int maxLength = 80)
    {
        string s = new string((text ?? "").Select(c => Invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().Trim('.', ' ');
        if (s.Length > maxLength) s = s[..maxLength].TrimEnd('.', ' ');
        if (s.Length > 0 && char.IsHighSurrogate(s[^1])) s = s[..^1];
        if (s.Length == 0) return fallback;
        return IsReserved(s) ? "_" + s : s;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is one file or folder name Windows stores exactly as written: not empty or
    /// too long, no separator, colon (a hidden stream), wildcard or control character, not "." or "..", no dot or
    /// space at the end (Windows drops those) and not a reserved device name.
    /// </summary>
    public static bool IsPlain(string name) =>
        name.Length is > 0 and <= 255 &&
        name != "." && name != ".." &&
        !name.Any(c => Invalid.Contains(c) || char.IsControl(c)) &&
        name[^1] != '.' && name[^1] != ' ' &&
        !IsReserved(name);

    /// <summary>Whether a /-separated relative path is made only of plain names (see <see cref="IsPlain"/>).</summary>
    public static bool IsPlainRelative(string path) => path.Length is > 0 and <= 1024 && path.Split('/').All(IsPlain);

    /// <summary>
    /// The local path of a /-separated relative path inside <paramref name="root"/>; throws when it is not made of
    /// plain names or would end up anywhere else.
    /// </summary>
    public static string Inside(string root, string relative)
    {
        if (!IsPlainRelative(relative)) throw new IOException($"\"{Shown(relative)}\" is not a name Windows can hold safely.");
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string inside = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(inside, StringComparison.OrdinalIgnoreCase)) throw new IOException($"\"{Shown(relative)}\" would land outside its folder.");
        return full;
    }

    /// <summary>A name from outside, made safe to show in a message (control characters replaced, length limited).</summary>
    public static string Shown(string text)
    {
        string s = new string(text.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return s.Length > 120 ? s[..120] + "…" : s;
    }

    static bool IsReserved(string name)
    {
        int dot = name.IndexOf('.');
        return Reserved.Contains((dot < 0 ? name : name[..dot]).TrimEnd(' '));
    }
}
