// MinecraftStudio/UI/ToolStripHost.cs: Puts any control (dark combo box, text field, progress bar) on a tool
// strip at a fixed size.
// Owns: ToolStripHost.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>Puts any control (dark combo box, text field, progress bar) on a tool strip at a fixed size.</summary>
sealed class ToolStripHost : ToolStripControlHost
{
    public ToolStripHost(Control control, int width, int? height = null) : base(control)
    {
        AutoSize = false;
        control.Width = width;
        if (height is int h) control.Height = h;
        Size = control.Size;
        Margin = new Padding(2, 1, 2, 1);
    }
}
