// MinecraftStudio/Studio/StudioKeys.cs: which keys a focused control keeps for itself in the studios.
// Owns: StudioKeys — Focused (the control that has the keyboard, inside any panels) and UsesKey: typing keeps every key, a
// check box its Space, a list or drop-down its arrows, paging keys and letters (type to find), a slider its arrows.
// Everything else is the studio's shortcut — on the canvas, the mob, the timeline or a button (after clicking "1 tick ▶"
// the arrows still step). Before this a key pressed in a list could edit the picture (Down nudged a selection).
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

static class StudioKeys
{
    /// <summary>The control that has the keyboard, looking inside nested containers.</summary>
    public static Control? Focused(ContainerControl root)
    {
        var focused = root.ActiveControl;
        while (focused is ContainerControl cc && cc.ActiveControl != null) focused = cc.ActiveControl;
        return focused;
    }

    /// <summary>Whether the focused control uses this key itself (then the studio leaves it alone).</summary>
    public static bool UsesKey(Control? focused, Keys key) => focused switch
    {
        TextBoxBase => true,
        CheckBox or RadioButton => key == Keys.Space,
        ListBox or ListView => key is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || Plain(key),
        ComboBox => key is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || Plain(key),
        DarkSlider => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down,
        _ => false,
    };

    /// <summary>A letter or digit without Ctrl, Alt or Shift: typing to find an item in a list.</summary>
    static bool Plain(Keys key) => key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9;
}
