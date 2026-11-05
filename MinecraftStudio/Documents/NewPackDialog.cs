// Documents/NewPackDialog.cs: start a new pack set up right for a version — a Java data pack or a Bedrock add-on.
// Owns: NewPackDialog — the kind, name, description, namespace (Java; made from the name), the version (a Java release's
// data pack format, or the oldest Bedrock version the add-on is for) and a new folder; PackTemplates writes it, and the
// folder is shown. Install it into a world from that world's Packs page.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class NewPackDialog : DarkDialog
{
    readonly DarkRadioButton _java, _bedrock;
    readonly DarkTextField _name, _description, _namespace, _folder;
    readonly DarkComboBox _version;
    readonly DarkCheckBox _examples;
    readonly Label _status, _nsLabel;
    readonly Button _create;
    bool _nsEdited;

    public NewPackDialog() : base("New data pack or add-on", 600)
    {
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new Label
        {
            Text = "Start a pack with everything in the right place for the version: a Java data pack (pack.mcmeta with the version's format, a load function that runs when the world opens and a tick function that runs every tick) or a Bedrock add-on (a behavior pack and a resource pack that go together). Then add your own functions, recipes, loot tables, advancements, textures and more.",
            AutoSize = true, MaximumSize = new Size(this.Px(560), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _java = new DarkRadioButton("Java data pack") { Checked = true };
        _bedrock = new DarkRadioButton("Bedrock add-on (behavior pack + resource pack)");
        var kinds = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        kinds.Controls.AddRange([_java, _bedrock]);
        column.Controls.Add(kinds);

        _name = Field("Name", "My pack");
        _description = Field("Description", "Made with " + Core.AppInfo.Name);
        _namespace = Field("Namespace", PackTemplates.NamespaceFor("My pack"));
        // The namespace follows the name until it is typed in itself.
        _name.Box.TextChanged += (_, _) => { if (!_nsEdited) _namespace.Text = PackTemplates.NamespaceFor(_name.Text); UpdateFolder(); };
        _namespace.Box.TextChanged += (_, _) => _nsEdited = _namespace.Box.Focused || _nsEdited;
        column.Controls.Add(Row("Name", _name));
        column.Controls.Add(Row("Description", _description));
        _nsLabel = new Label { Text = "Its functions are called namespace:load and namespace:tick (lower case letters, digits, _ - . only).", AutoSize = true, MaximumSize = new Size(this.Px(560), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(this.Px(116), 0, 0, this.Px(6)) };
        column.Controls.Add(Row("Namespace", _namespace));
        column.Controls.Add(_nsLabel);
        _version = new DarkComboBox { Width = this.Px(360), AccessibleName = "Version" };
        column.Controls.Add(Row("For", _version));
        // Java: a recipe, a loot table and an advancement to start from, written the way the version reads them.
        _examples = new DarkCheckBox("Also add examples: a recipe, a loot table and an advancement") { AutoSize = true, Margin = new Padding(this.Px(116), this.Px(2), 0, this.Px(4)) };
        column.Controls.Add(_examples);

        _folder = new DarkTextField { Width = this.Px(360), Height = this.Px(28), AccessibleName = "Folder" };
        var browse = Theme.Button("Browse…");
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Where to make the pack (a new folder is made inside)", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = Path.Combine(dlg.SelectedPath, SafeName.For(_name.Text, "Pack"));
        };
        column.Controls.Add(Row("Make it in", _folder, browse));

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(560), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _create = Theme.Button("Make the pack", true);
        _create.MinimumSize = new Size(this.Px(140), this.Px(30));
        _create.Click += (_, _) => Create();
        buttons.Controls.Add(_create);
        Controls.Add(_status);
        Controls.Add(column);
        SizeToContent();
        _java.CheckedChanged += (_, _) => FillVersions();
        FillVersions();
        UpdateFolder();
    }

    DarkTextField Field(string name, string text)
    {
        var f = new DarkTextField { Width = this.Px(360), Height = this.Px(28), AccessibleName = name };
        f.Text = text;
        return f;
    }

    void FillVersions()
    {
        _version.Items.Clear();
        if (_java.Checked)
            foreach (var f in PackTemplates.DataPackFormats.Reverse()) _version.Items.Add(new ComboItem($"Java {f.Versions}", $"data pack format {f.Text}", f));
        else
            foreach (var t in TargetCatalog.All.Where(t => t.Kind == TargetKind.Bedrock && t.BedrockVersion.Length >= 3 && t.BedrockVersion[0] >= 1 && (t.BedrockVersion[1] > 16 || t.BedrockVersion[0] > 1)))
                _version.Items.Add(new ComboItem($"Bedrock {t.BedrockVersion[0]}.{t.BedrockVersion[1]}.{t.BedrockVersion[2]} or newer", null, t.BedrockVersion.Take(3).ToArray()));
        if (_version.Items.Count > 0) _version.SelectedIndex = 0;
        _namespace.Enabled = _java.Checked;
        _nsLabel.Visible = _java.Checked;
        _examples.Visible = _java.Checked;
        UpdateFolder();
    }

    void UpdateFolder()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", _java.Checked ? "Data packs" : "Add-ons");
        if (_folder.Text.Length == 0 || _folder.Text.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio"), StringComparison.OrdinalIgnoreCase))
            _folder.Text = Path.Combine(root, SafeName.For(_name.Text, "Pack"));
    }

    void Create()
    {
        string name = _name.Text.Trim(), description = _description.Text.Trim(), folder = _folder.Text.Trim();
        if (name.Length == 0) { Warn("Give the pack a name."); return; }
        if (folder.Length == 0) { Warn("Choose where to make it."); return; }
        try { folder = Path.GetFullPath(folder); } catch { Warn("That folder path is not valid."); return; }
        try
        {
            if (_java.Checked)
            {
                if (_version.SelectedItem is not ComboItem { Tag: DataPackFormat format }) return;
                string ns = _namespace.Text.Trim();
                if (!PackTemplates.IsNamespace(ns)) { Warn("The namespace is lower case letters, digits, _ - and . only (and not \"minecraft\")."); return; }
                PackTemplates.JavaDataPack(folder, name, description, ns, format, _examples.Checked);
                Done($"Made the data pack in {folder} (format {format.Text}). Its load function says it is on; put commands in {ns}:tick. Install it from a world's Packs page, or copy the folder into a world's datapacks folder.", folder);
            }
            else
            {
                if (_version.SelectedItem is not ComboItem { Tag: int[] version }) return;
                var (bp, rp) = PackTemplates.BedrockAddOn(Path.GetDirectoryName(folder)!, name, description, version);
                Done($"Made the add-on: {Path.GetFileName(bp)} and {Path.GetFileName(rp)}. Install it from a Bedrock world's Packs page; try /function hello in the game.", Path.GetDirectoryName(folder)!);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Warn(ex.Message); }
    }

    void Done(string text, string show)
    {
        _status.ForeColor = C.GREEN;
        _status.Text = "✓  " + text;
        Explorer.Open(show);
    }

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(110), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }
}
