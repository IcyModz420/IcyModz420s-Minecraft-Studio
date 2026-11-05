// MinecraftStudio/Documents/WhatWorksWhereDialog.cs: Help → What Works Where — every feature against every platform: PC
// (Java), Bedrock on every device, Pocket 0.x, each of the seven Legacy consoles, the 3DS and Classic/Indev.
// Owns: WhatWorksWhereDialog — the table (PlatformSupport, the same one FEATURES.md is written from): ✓ works, ~ works with
// a limit, "look" shows it without changing it, — not there; "Show for" one platform lists only what works there (opened
// from a world it starts on that world's platform); the chosen row's details below (what it does, where it works, why not).
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class WhatWorksWhereDialog : DarkDialog
{
    readonly ListView _list;
    readonly DarkComboBox _for;
    readonly Label _details, _count;

    public WhatWorksWhereDialog(Platform? start = null) : base("What works where", 1180)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        ClientSize = new Size(this.Px(1180), this.Px(760));
        MinimumSize = new Size(this.Px(760), this.Px(480));
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Every feature, and the games and devices it works on. ✓ works · ~ works with a limit (choose the row to read it) · look: shows it but can't change it there · — not there.\n" +
                   "Bedrock is today's Minecraft on every device (Windows PC, phones and tablets, Xbox, PlayStation, Switch): the same world files everywhere. " +
                   "The consoles' own editions — Xbox 360, Xbox One Edition, PS3, PS4 Edition, PS Vita, Wii U and Switch Edition — came before Bedrock on those consoles.",
            AutoSize = true, MaximumSize = new Size(this.Px(1140), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _for = new DarkComboBox { Width = this.Px(420), AccessibleName = "Show for" };
        _for.Items.Add(new ComboItem("Every platform", null, null));
        foreach (var (p, shortName, longName) in PlatformSupport.Platforms) _for.Items.Add(new ComboItem($"{shortName} — {longName}", null, p));
        _count = new Label { AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(10), this.Px(7), 0, 0) };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, this.Px(6)) };
        row.Controls.AddRange([new Label { Text = "Show for", AutoSize = true, ForeColor = C.FG_DIM, Margin = new Padding(0, this.Px(7), this.Px(8), 0) }, _for, _count]);
        top.Controls.Add(row);

        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.None, AccessibleName = "Features" };
        Theme.DarkList(_list);
        _list.Columns.Add("Feature", this.Px(290));
        // Headings short enough for their column (the Xbox One, PS4 and Switch columns are those consoles' own editions).
        foreach (var (p, _, _) in PlatformSupport.Platforms) _list.Columns.Add(Heading(p), this.Px(70), HorizontalAlignment.Center);
        _list.SelectedIndexChanged += (_, _) => ShowDetails();

        _details = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = this.Px(92), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };
        AddButtons(("Close", DialogResult.Cancel, true));
        Controls.Add(_details);
        Controls.Add(top);
        Controls.Add(_list);
        // Filled first, so it takes what the rows above and below leave.
        _list.BringToFront();
        _for.SelectedIndexChanged += (_, _) => Fill();
        _for.SelectedIndex = start is Platform p0 ? 1 + PlatformSupport.Platforms.ToList().FindIndex(x => x.Platform == p0) : 0;
    }

    static string Heading(Platform p) => p switch
    {
        Platform.Pocket => "Pocket", Platform.Xbox360 => "X360", Platform.XboxOne => "XB One", Platform.PS4 => "PS4",
        Platform.PSVita => "Vita", Platform.Switch => "Switch", Platform.Classic => "Classic", _ => PlatformSupport.ShortName(p),
    };

    static string Mark(Support s) => s switch { Support.Yes => "✓", Support.Limited => "~", Support.Look => "look", _ => "—" };

    static Color Tint(Support s) => s switch { Support.Yes => C.GREEN, Support.Limited => C.ORANGE, Support.Look => C.FG_DIM, _ => C.FG_FAINT };

    void Fill()
    {
        var only = (_for.SelectedItem as ComboItem)?.Tag as Platform?;
        _list.BeginUpdate();
        _list.Items.Clear();
        int shown = 0;
        foreach (var g in PlatformSupport.All.GroupBy(f => f.Group))
        {
            var features = g.Where(f => only is not Platform p || f[p].Support != Support.No).ToList();
            if (features.Count == 0) continue;
            var head = new ListViewItem(g.Key.ToUpperInvariant()) { ForeColor = C.FG_DIM, Font = Theme.Ui(8.5f, FontStyle.Bold) };
            _list.Items.Add(head);
            foreach (var f in features)
            {
                var item = new ListViewItem("   " + f.Name) { UseItemStyleForSubItems = false, Tag = f };
                foreach (var (p, _, _) in PlatformSupport.Platforms)
                {
                    var cell = f[p];
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item, Mark(cell.Support)) { ForeColor = Tint(cell.Support) });
                }
                _list.Items.Add(item);
                shown++;
            }
        }
        _list.EndUpdate();
        _count.Text = only is Platform pl ? $"{shown} of {PlatformSupport.All.Count} features work on {PlatformSupport.ShortName(pl)}" : $"{PlatformSupport.All.Count} features";
        _details.Text = "Choose a feature to read what it does and where it works.";
    }

    void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not Feature f) return;
        _details.Text = $"{f.Name} — {f.What}.\r\nWorks on: {PlatformSupport.WorksOn(f)}";
    }

    /// <summary>The rows shown (for the checks).</summary>
    internal int Rows => _list.Items.Cast<ListViewItem>().Count(i => i.Tag is Feature);
}
