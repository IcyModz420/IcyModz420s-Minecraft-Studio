// MinecraftStudio/UI/CopyFolderRow.cs: "Save copy to [folder] [Browse…]" — where a tool writes its new copy of a world.
// Owns: CopyFolderRow. Starts at Documents\Minecraft Studio\<kind>\<world name> <suffix>; Browse picks the parent folder
// and keeps the name; Check() refuses an empty or invalid path, a file, a folder with something in it, or one inside the
// world, before anything runs. Buttons given after the field sit on the same row (the tool's own "… into a copy").
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.UI;

sealed class CopyFolderRow : FlowLayoutPanel
{
    readonly DarkTextField _field;
    readonly Button _browse;
    readonly string _worldLocation;

    public CopyFolderRow(Control owner, string worldName, string worldLocation, string kind, string suffix, int labelWidth, params Control[] after)
    {
        _worldLocation = worldLocation;
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0, owner.Px(2), 0, owner.Px(2));
        Controls.Add(new Label { Text = "Save copy to", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(owner.Px(labelWidth), 0), Margin = new Padding(0, owner.Px(7), owner.Px(6), 0) });
        _field = new DarkTextField { Width = owner.Px(400), Height = owner.Px(28), AccessibleName = "Output folder", Margin = new Padding(0, 0, owner.Px(6), 0) };
        _field.Box.PlaceholderText = "A new, empty folder for the copy";
        string name = SafeName.For(worldName + " " + suffix, "World");
        _field.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", kind, name);
        _browse = Theme.Button("Browse…");
        _browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "A new folder for the copy", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) _field.Text = Path.Combine(dlg.SelectedPath, name);
        };
        Controls.Add(_field);
        Controls.Add(_browse);
        foreach (var c in after) Controls.Add(c);
    }

    public string Folder { get => _field.Text.Trim(); set => _field.Text = value; }

    /// <summary>Whether the field and Browse can be used (the tool's own buttons are left to the tool).</summary>
    public bool Editable { set { _field.Enabled = value; _browse.Enabled = value; } }

    /// <summary>The full path of a new, empty folder outside the world, or why it can't be one (null when it can).</summary>
    public string? Check(out string full)
    {
        full = "";
        string output = Folder;
        if (output.Length == 0) return "Choose a folder for the copy.";
        try { full = Path.GetFullPath(output); } catch { return "That folder path is not valid."; }
        if (File.Exists(full)) return "A file with that name already exists; choose another folder.";
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) return "That folder is not empty; choose a new folder name.";
        string source = Path.GetFullPath(_worldLocation).TrimEnd('\\');
        if (full.Equals(source, StringComparison.OrdinalIgnoreCase) || full.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
            return "The copy cannot go inside the world itself.";
        return null;
    }
}
