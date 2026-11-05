// MinecraftStudio/Documents/BlockRulesDialog.cs: the Convert window's "Change blocks as it converts" rules.
// Owns: BlockRulesDialog — the rules in a BlockRulesEditor (a mod's blocks, or any block, and what the new world gets in
// their place); OK only with rules BlockReplace takes (an unknown block or property is said there, the window stays).
using MinecraftStudio.Core.Structures;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class BlockRulesDialog : DarkDialog
{
    readonly BlockRulesEditor _editor;

    /// <summary>The rules, both sides filled in, once OK is pressed.</summary>
    public List<ReplaceRule> Rules => _editor.Rules().Where(r => r.Pattern.Length > 0 && r.With.Length > 0).ToList();

    public BlockRulesDialog(IReadOnlyList<ReplaceRule> rules) : base("Change blocks as it converts", 560)
    {
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new Label
        {
            Text = "Blocks the new world gets in place of others: a mod's blocks the target can't keep (thermal:ore → copper_ore), or any block (*_ore → stone). A block name, * for any letters, [key=value] for only some states; the first rule that matches wins, and the new block keeps the old one's facing and the rest it shares. The names are the ones Find and the conversion report show.",
            AutoSize = true, MaximumSize = new Size(this.Px(520), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _editor = new BlockRulesEditor(this, 6, "change", "put there");
        _editor.SetRules(rules);
        column.Controls.Add(_editor);
        var status = new Label { AutoSize = true, MaximumSize = new Size(this.Px(520), 0), ForeColor = C.ORANGE, UseMnemonic = false, Margin = new Padding(0, this.Px(6), 0, 0) };
        column.Controls.Add(status);
        Controls.Add(column);
        AddButtons(("OK", DialogResult.OK, true), ("Cancel", DialogResult.Cancel, false));
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            var typed = _editor.Rules();
            if (typed.Count == 0) return;
            try { BlockReplace.Compile(typed); }
            catch (ArgumentException ex) { status.Text = ex.Message; e.Cancel = true; }
        };
        SizeToContent();
    }
}
