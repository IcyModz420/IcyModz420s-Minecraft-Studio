// MinecraftStudio/UI/DarkTextField.cs: Single-line text box in a flat dark frame that lights up while focused.
// Owns: DarkTextField.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>Single-line text box in a flat dark frame that lights up while focused.</summary>
class DarkTextField : Panel
{
    public DarkTextField(string text = "")
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = C.BG;
        Padding = new Padding(this.Px(7), this.Px(6), this.Px(7), this.Px(3));
        Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = C.BG, ForeColor = C.FG, Text = text };
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.EnabledChanged += (_, _) => { Box.BackColor = BackColor = Box.Enabled ? C.BG : C.BG2; Invalidate(); };
        Controls.Add(Box);
        Cursor = Cursors.IBeam;
    }

    public TextBox Box { get; }

    [System.ComponentModel.Browsable(true)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // Setting Padding in the constructor lays out before the text box exists.
        if (Box is not TextBox box) return;
        // Single-line text boxes have a fixed height: centre it in the frame.
        int h = box.Height;
        box.SetBounds(Padding.Left, Math.Max(1, (ClientSize.Height - h) / 2), Math.Max(1, ClientSize.Width - Padding.Horizontal), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Box.Focused ? C.ACCENT2 : C.BORDER);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}
