// MinecraftStudio/UI/BlockRulesEditor.cs: rows of "blocks → block" rules (the Replace window's, and the converter's).
// Owns: BlockRulesEditor — a few rows of two fields (what to change, what to put there) with the newest Java block names
// offered as they are typed; Rules() gives the rules filled in (the first that matches a block wins: BlockReplace).
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.UI;

sealed class BlockRulesEditor : FlowLayoutPanel
{
    readonly List<(DarkTextField Pattern, DarkTextField With)> _rows = [];

    public BlockRulesEditor(Control owner, int rows, string replaceWord = "replace", string withWord = "put there")
    {
        AutoSize = true;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Margin = Padding.Empty;
        var names = new AutoCompleteStringCollection();
        names.AddRange(JavaBlockData.For(StructureJava.Newest).Blocks.Keys.Select(k => k.Replace("minecraft:", "")).OrderBy(k => k).ToArray());
        for (int i = 0; i < rows; i++)
        {
            var pattern = new DarkTextField { Width = owner.Px(240), Height = owner.Px(28), AccessibleName = $"Rule {i + 1}: blocks to {replaceWord}" };
            var with = new DarkTextField { Width = owner.Px(240), Height = owner.Px(28), AccessibleName = $"Rule {i + 1}: block to {withWord}" };
            foreach (var box in new[] { pattern.Box, with.Box })
            {
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.AutoCompleteCustomSource = names;
            }
            if (i == 0) { pattern.Box.PlaceholderText = "e.g. *_ore"; with.Box.PlaceholderText = "e.g. stone"; }
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, owner.Px(1), 0, owner.Px(1)) };
            row.Controls.AddRange([pattern, new Label { Text = "→", AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(owner.Px(6), owner.Px(6), owner.Px(6), 0) }, with]);
            Controls.Add(row);
            _rows.Add((pattern, with));
        }
    }

    /// <summary>The rules filled in (a row with either field typed counts, so a half-filled one is reported).</summary>
    public List<ReplaceRule> Rules() => _rows.Select(r => new ReplaceRule(r.Pattern.Text.Trim(), r.With.Text.Trim())).Where(r => r.Pattern.Length > 0 || r.With.Length > 0).ToList();

    /// <summary>Fills the rows with rules (the first ones; the rest empty).</summary>
    public void SetRules(IReadOnlyList<ReplaceRule> rules)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Pattern.Text = i < rules.Count ? rules[i].Pattern : "";
            _rows[i].With.Text = i < rules.Count ? rules[i].With : "";
        }
    }

    public new bool Enabled
    {
        get => base.Enabled;
        set { foreach (var (p, w) in _rows) { p.Enabled = value; w.Enabled = value; } }
    }
}
