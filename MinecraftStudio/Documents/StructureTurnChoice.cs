// Documents/StructureTurnChoice.cs: the "Turn" and "Mirror" pickers for a structure (seen from above).
// Owns: StructureTurnChoice — two combo boxes (as it is / a quarter turn clockwise / half a turn / a quarter turn
// anticlockwise; no mirror / north ↔ south / east ↔ west), Changed when either changes, and Apply (StructureTransform:
// mirror first, then the turn, the way structure blocks do it).
using MinecraftStudio.Core.Structures;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class StructureTurnChoice
{
    public DarkComboBox Turn { get; }
    public DarkComboBox Mirror { get; }

    public event Action? Changed;

    public StructureTurnChoice(Control owner)
    {
        Turn = new DarkComboBox { Width = owner.Px(190), AccessibleName = "Turn (seen from above)", Margin = new Padding(0, 0, owner.Px(6), 0) };
        foreach (var (text, turns) in new[] { ("As it is", 0), ("A quarter turn clockwise", 1), ("Half a turn", 2), ("A quarter turn anticlockwise", 3) })
            Turn.Items.Add(new ComboItem(text, null, turns));
        Turn.SelectedIndex = 0;
        Mirror = new DarkComboBox { Width = owner.Px(170), AccessibleName = "Mirror" };
        foreach (var (text, m) in new[] { ("No mirror", StructureMirror.None), ("Mirror north ↔ south", StructureMirror.NorthSouth), ("Mirror east ↔ west", StructureMirror.EastWest) })
            Mirror.Items.Add(new ComboItem(text, null, m));
        Mirror.SelectedIndex = 0;
        Turn.SelectedIndexChanged += (_, _) => Changed?.Invoke();
        Mirror.SelectedIndexChanged += (_, _) => Changed?.Invoke();
    }

    public int Turns => Turn.SelectedItem is ComboItem { Tag: int t } ? t : 0;
    public StructureMirror MirrorChoice => Mirror.SelectedItem is ComboItem { Tag: StructureMirror m } ? m : StructureMirror.None;
    public bool IsIdentity => Turns == 0 && MirrorChoice == StructureMirror.None;

    /// <summary>The structure as chosen (the same object when nothing is chosen).</summary>
    public Structure Apply(Structure s) => StructureTransform.Apply(s, Turns, MirrorChoice);

    public string Describe() => IsIdentity ? "" : string.Join(", ", new[]
    {
        Turns == 0 ? null : ((ComboItem)Turn.SelectedItem!).Text.ToLowerInvariant(),
        MirrorChoice == StructureMirror.None ? null : ((ComboItem)Mirror.SelectedItem!).Text.ToLowerInvariant(),
    }.Where(t => t != null));

    public bool Enabled
    {
        set { Turn.Enabled = value; Mirror.Enabled = value; }
    }
}
