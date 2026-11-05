// MinecraftStudio/Documents/WorldPages/WorldPage.cs: The sections of a world's tab.
// Owns: WorldSection, WorldPage.
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents.WorldPages;

/// <summary>The sections of a world's tab.</summary>
enum WorldSection { Overview, Map, Players, Cheats, Packs }

/// <summary>A page of a world's tab (overview, players, cheats …); the map page is the world document's own.</summary>
abstract class WorldPage : UserControl
{
    protected WorldPage(WorldDocument owner)
    {
        Owner = owner;
        Dock = DockStyle.Fill;
        BackColor = C.BG;
        ForeColor = C.FG;
        Font = Theme.Ui(9.5f);
        AutoScaleMode = AutoScaleMode.None;
    }

    public WorldDocument Owner { get; }
    public SaveWorld World => Owner.World;
    public MainForm Main => Owner.Main;

    public virtual bool IsDirty => false;
    public virtual Task SaveAsync() => Task.CompletedTask;
    /// <summary>Called each time the page is shown.</summary>
    public virtual void OnShown() { }

    /// <summary>Tells the tab that the page's unsaved state changed.</summary>
    protected void Changed() => Owner.PageChanged();

    // Layout values are written at 96 dpi and scaled for the screen here.
    protected Point P(int x, int y) => new(this.Px(x), this.Px(y));
    protected Size S(int w, int h) => new(this.Px(w), this.Px(h));

    /// <summary>A scrolling column for the page's content, with a readable maximum width.</summary>
    protected FlowLayoutPanel Column()
    {
        var column = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(this.Px(28), this.Px(20), this.Px(28), this.Px(20)), BackColor = C.BG,
        };
        Theme.DarkControl(column);
        Controls.Add(column);
        return column;
    }

    protected SectionHeader Header(string text, int width) => new(text) { Size = S(width, 18), Margin = new Padding(0, this.Px(14), 0, this.Px(8)) };

    protected Label Note(string text, int width, Color? color = null, float size = 9.5f, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, AutoSize = true, MaximumSize = S(width, 0), ForeColor = color ?? C.FG_DIM, Font = Theme.Ui(size, style),
        Margin = new Padding(0, 0, 0, this.Px(6)), UseMnemonic = false,
    };
}
