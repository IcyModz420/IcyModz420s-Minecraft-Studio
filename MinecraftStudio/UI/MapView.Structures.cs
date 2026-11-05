// MinecraftStudio/UI/MapView.Structures.cs: structures on the map for MapView.
// Part of MapView (main part in MapView.cs). Owns: MapStructure. A marker per structure — a square in its kind's colour, a
// gold ring while loot is still in it — with its outline once close enough in and its name beside it while there is room
// (names that would cover others are left out); the hover line names the structures under the pointer.
using System.Drawing.Drawing2D;

namespace MinecraftStudio.UI;

/// <summary>A structure to mark: its name, middle, box (x, z), colour, and how many of its loot containers were never opened.</summary>
public readonly record struct MapStructure(string Name, int X, int Y, int Z, (int MinX, int MinZ, int MaxX, int MaxZ) Box, Color Color, int Unlooted);

partial class MapView
{
    IReadOnlyList<MapStructure> _structures = [];

    /// <summary>The structures marked on the map (null or none: no markers).</summary>
    public void SetStructures(IReadOnlyList<MapStructure>? structures)
    {
        _structures = structures ?? [];
        Invalidate();
    }

    public int StructureCount => _structures.Count;

    void DrawStructures(Graphics g)
    {
        if (_structures.Count == 0) return;
        float r = this.Px(5);
        var view = new RectangleF(-r * 4, -r * 4, Width + r * 8, Height + r * 8);
        var labels = new List<RectangleF>();
        using var font = Theme.Ui(8f, FontStyle.Bold);
        using var edge = new Pen(Color.FromArgb(220, 0, 0, 0), Math.Max(1f, this.Px(1)));
        using var gold = new Pen(Color.FromArgb(255, 255, 205, 50), Math.Max(1.5f, this.Px(1) * 2f));
        using var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var s in _structures)
        {
            var p = ToScreen(s.X + 0.5, s.Z + 0.5);
            if (!view.Contains(p)) continue;
            // Its outline once it is big enough on screen to tell from its marker.
            var a = ToScreen(s.Box.MinX, s.Box.MinZ);
            var b = ToScreen(s.Box.MaxX + 1, s.Box.MaxZ + 1);
            if (b.X - a.X >= r * 6 && b.Y - a.Y >= r * 6)
            {
                using var outline = new Pen(Color.FromArgb(170, s.Color), Math.Max(1f, this.Px(1))) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(outline, a.X, a.Y, b.X - a.X, b.Y - a.Y);
            }
            using (var fill = new SolidBrush(s.Color)) g.FillRectangle(fill, p.X - r, p.Y - r, 2 * r, 2 * r);
            g.DrawRectangle(edge, p.X - r, p.Y - r, 2 * r, 2 * r);
            if (s.Unlooted > 0) g.DrawEllipse(gold, p.X - r * 1.9f, p.Y - r * 1.9f, r * 3.8f, r * 3.8f);
            // Its name beside it, where it covers no other.
            var size = TextRenderer.MeasureText(s.Name, font);
            var box = new RectangleF(p.X + r * 2, p.Y - size.Height / 2f, size.Width, size.Height);
            if (labels.Any(l => l.IntersectsWith(box))) continue;
            labels.Add(box);
            g.FillRectangle(back, box);
            TextRenderer.DrawText(g, s.Name, font, Rectangle.Round(box), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    /// <summary>The names of the structures whose box holds a column (for the hover line), or "".</summary>
    string StructuresAt(int bx, int bz)
    {
        if (_structures.Count == 0) return "";
        var names = _structures.Where(s => bx >= s.Box.MinX && bx <= s.Box.MaxX && bz >= s.Box.MinZ && bz <= s.Box.MaxZ)
            .Select(s => s.Unlooted > 0 ? $"{s.Name} ({s.Unlooted} not looted yet)" : s.Name).Distinct().Take(3).ToList();
        return names.Count == 0 ? "" : "   ·   " + string.Join(", ", names);
    }
}
