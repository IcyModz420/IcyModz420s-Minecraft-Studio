// MinecraftStudio.Core/Imaging/IPixelEditor.cs: what every pixel editor (skin, texture, mob) offers the shared tools.
// Owns: IPixelEditor — the tool and its settings, the two colours, the pixels being edited, and the selection/live-change
// session, so the tool bar, picture import, colour changes, clipboard and drag-and-drop are written once for all of them.
using MinecraftStudio.Core.Skins;

namespace MinecraftStudio.Core.Imaging;

/// <summary>What every pixel editor offers the shared tools.</summary>
public interface IPixelEditor
{
    PaintTool Tool { get; set; }
    PaintSettings Settings { get; }
    uint Primary { get; set; }
    uint Secondary { get; set; }
    /// <summary>The selection and the change being made live (paste, move, colour change being tried).</summary>
    PixelSession Session { get; }
    /// <summary>A copy of the pixels being edited (the skin, or the texture's frame in view), or null when nothing is open.</summary>
    RgbaImage? AreaPixels();
    /// <summary>What is being edited, in a few words (for dialogs: "the skin", "Stone", "Cow").</summary>
    string AreaName { get; }
    /// <summary>
    /// A blank picture the size of the area, white or see-through (a skin's white is its base layer only, so the second
    /// layer does not cover it); null when nothing is open.
    /// </summary>
    RgbaImage? Blank(bool white);
    bool ShowGrid { get; }
    /// <summary>The pixel under the mouse in any view (relative to the area), or null.</summary>
    (int X, int Y)? Hover { get; }
    void SetHover((int X, int Y)? p);

    // Strokes, in area coordinates (the views turn mouse positions into pixels).
    bool Stroking { get; }
    void BeginStroke(int x, int y, bool secondary);
    void StrokeTo(int x, int y, bool connect, bool constrain = false);
    void EndStroke();
    bool PickColor(int x, int y, bool secondary);

    void NotifyState();
    /// <summary>Shows a message to the player (the status bar).</summary>
    void Tell(string message);
    event Action? ImageChanged;
    event Action? StateChanged;
    event Action? HoverChanged;
    event Action<string>? Message;
}
