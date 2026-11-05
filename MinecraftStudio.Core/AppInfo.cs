// MinecraftStudio.Core/AppInfo.cs: the program's display name and the credit it writes into files it creates.
// Owns: AppInfo.Name (dialogs, About — the app's Dialogs.AppName is this), TitleNote and WindowTitle (the main window's
// title: the name and the author's note), AppInfo.Organization (also the Company in both projects' file properties) and
// AppInfo.Credit (written into packs, Classic/Indev levels and structures, and used to recognise packs this program
// installed). Storage folders (settings, backups, Documents\Minecraft Studio\...) deliberately keep their old names so
// existing files stay found.
namespace MinecraftStudio.Core;

public static class AppInfo
{
    /// <summary>The program's display name.</summary>
    public const string Name = "IcyModz420's Minecraft Studio";

    /// <summary>What the main window's title says after the name (the author's wording).</summary>
    public const string TitleNote = "This Tool costs 1 youtube subscription to IcyModz420 on youtube";

    /// <summary>The main window's title: the name and the note.</summary>
    public const string WindowTitle = Name + " — " + TitleNote;

    /// <summary>Who makes it (the Company shown in the program file's properties is the same).</summary>
    public const string Organization = "IcyModz420";

    /// <summary>The credit written into files this program creates.</summary>
    public const string Credit = "Made with " + Name;

    /// <summary>The credit older versions wrote (still recognised, so packs installed before the rename are known).</summary>
    public const string LegacyCredit = "Made with Minecraft Studio";

    /// <summary>Every credit this program has written under its names so far, newest first.</summary>
    public static readonly string[] Credits = [Credit, "Made with IcyModz420's Minecraft Save Studio", LegacyCredit];
}
