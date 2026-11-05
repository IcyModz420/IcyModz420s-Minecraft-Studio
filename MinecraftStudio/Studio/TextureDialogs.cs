// MinecraftStudio/Studio/TextureDialogs.cs: Before a pack is saved for an edition or version: how many textures
// go in, which cannot (by name) and what changed, so nothing is lost without the player knowing. Returns true
// to go ahead.
// Owns: PackReport, JavaTargetDialog.
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// Before a pack is saved for an edition or version: how many textures go in, which cannot (by name) and what changed,
/// so nothing is lost without the player knowing. Returns true to go ahead.
/// </summary>
static class PackReport
{
    public static bool Confirm(IWin32Window owner, string title, PackResult r, string go)
    {
        var text = new System.Text.StringBuilder();
        if (r.Problems.Count > 0)
        {
            text.AppendLine("THE PACK DID NOT COME OUT RIGHT, SO IT CANNOT BE SAVED:");
            foreach (var p in r.Problems) text.AppendLine("  • " + p);
            text.AppendLine();
        }
        text.AppendLine($"{r.Written.Count} texture{(r.Written.Count == 1 ? "" : "s")} go into the pack.");
        if (r.NotIncluded.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"{r.NotIncluded.Count} cannot be in it (the edition or version has no such texture):");
            foreach (var n in r.NotIncluded) text.AppendLine("  • " + n);
        }
        var notes = r.Notes.Distinct().ToList();
        if (notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Good to know:");
            foreach (var n in notes) text.AppendLine("  • " + n);
        }
        using var f = new DarkDialog(title, 720) { FormBorderStyle = FormBorderStyle.Sizable };
        f.MinimumSize = new Size(f.Px(480), f.Px(300));
        f.ClientSize = new Size(f.Px(720), f.Px(480));
        var box = new TextBox
        {
            Text = text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9.5f),
        };
        if (r.Problems.Count > 0) f.AddButtons(("Close", DialogResult.Cancel, true));
        else f.AddButtons((go, DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        f.Controls.Add(box);
        f.Shown += (_, _) => box.Select(0, 0);
        return f.ShowDialog(owner) == DialogResult.OK && r.Problems.Count == 0;
    }
}

/// <summary>Which Java release a pack is made for, and (when installing) which game folders get it.</summary>
sealed class JavaTargetDialog : DarkDialog
{
    readonly DarkComboBox _release;
    readonly List<(DarkCheckBox Box, string Folder)> _folders = [];

    public JavaTargetDialog(string title, bool install, JavaRelease? suggested) : base(title, 600)
    {
        var form = new Panel { Dock = DockStyle.Top, AutoSize = true };
        var fb = new FormBuilder(form);
        _release = fb.Row("Java version", new DarkComboBox());
        var installed = GameFolders.JavaJars().Select(j => JavaReleases.Of(j.Version)).Where(r => r != null).ToHashSet();
        foreach (var r in JavaReleases.All.Reverse())
            _release.Items.Add(new ComboItem(r.Label, (installed.Contains(r) ? "on this PC  ·  " : "") + r.FormatText, r));
        var pick = suggested ?? installed.FirstOrDefault() ?? JavaReleases.Newest;
        _release.SelectedIndex = JavaReleases.All.Count - 1 - pick.Index;
        var info = new Label
        {
            Text = "Each version only gets the textures it has, under its own file names. The next screen lists anything that cannot go in.",
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(568), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(8)),
        };
        var folderPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        if (install)
        {
            var games = GameFolders.Java();
            foreach (var (label, folder) in games)
            {
                var box = new DarkCheckBox($"{label}   {folder}", label is "Minecraft Launcher" or "TLauncher");
                _folders.Add((box, folder));
                folderPanel.Controls.Add(box);
            }
            if (games.Count == 0) folderPanel.Controls.Add(Theme.Label("No Java game folders were found on this PC. Use Export to save the pack instead.", C.ORANGE));
        }
        AddButtons((install ? "Next" : "Next", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        if (install)
        {
            Controls.Add(folderPanel);
            Controls.Add(new Label { Text = "INSTALL INTO", Dock = DockStyle.Top, AutoSize = true, ForeColor = C.FG_DIM, Font = Theme.Ui(8.5f, FontStyle.Bold), Padding = new Padding(0, this.Px(8), 0, this.Px(4)) });
        }
        Controls.Add(form);
        Controls.Add(info);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    public JavaRelease Release => (JavaRelease)((ComboItem)_release.SelectedItem!).Tag!;
    public List<string> Folders => _folders.Where(f => f.Box.Checked).Select(f => f.Folder).ToList();
}
