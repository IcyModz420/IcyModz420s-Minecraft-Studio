// MinecraftStudio/UI/DarkComboBox.cs: An entry of a : text on the left, an optional dim detail on the right.
// Owns: ComboItem, DarkComboBox.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MinecraftStudio.UI;

/// <summary>An entry of a <see cref="DarkComboBox"/>: text on the left, an optional dim detail on the right.</summary>
sealed record ComboItem(string Text, string? Detail = null, object? Tag = null)
{
    public override string ToString() => Text;
}

/// <summary>Drop-down list painted in the dark palette (items, frame and arrow).</summary>
sealed class DarkComboBox : ComboBox
{
    bool _hover;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct COMBOBOXINFO
    {
        public int cbSize;
        public RECT rcItem, rcButton;
        public int stateButton;
        public IntPtr hwndCombo, hwndItem, hwndList;
    }

    [DllImport("user32.dll")]
    static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);

    public DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = C.BG;
        ForeColor = C.FG;
        MaxDropDownItems = 16;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = (int)Math.Round(22 * DeviceDpi / 96.0);
        var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };
        if (GetComboBoxInfo(Handle, ref info)) Theme.DarkWindow(info.hwndList);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool selected = !edit && (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(selected ? C.SELECTION : BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var item = Items[e.Index];
        string text = item is ComboItem ci ? ci.Text : GetItemText(item) ?? "";
        string? detail = (item as ComboItem)?.Detail;
        int pad = (int)Math.Round(7 * DeviceDpi / 96.0);
        var r = Rectangle.FromLTRB(e.Bounds.Left + pad, e.Bounds.Top, e.Bounds.Right - pad, e.Bounds.Bottom);
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        if (!string.IsNullOrEmpty(detail))
        {
            int w = TextRenderer.MeasureText(e.Graphics, detail, Font, r.Size, flags).Width;
            TextRenderer.DrawText(e.Graphics, detail, Font, r, !Enabled ? C.FG_FAINT : selected ? C.FG : C.FG_DIM, flags | TextFormatFlags.Right);
            r.Width = Math.Max(0, r.Width - w - pad * 2);
        }
        TextRenderer.DrawText(e.Graphics, text, Font, r, Enabled ? C.FG : C.FG_FAINT, flags | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x000F) // WM_PAINT: replace the system frame and the light drop-down button
        {
            using var g = Graphics.FromHwnd(Handle);
            PaintChrome(g);
        }
    }

    void PaintChrome(Graphics g)
    {
        float s = DeviceDpi / 96f;
        var r = ClientRectangle;
        int bw = (int)Math.Round(22 * s);
        var button = new Rectangle(r.Right - bw - 1, 1, bw, r.Height - 2);
        using (var b = new SolidBrush(_hover && Enabled ? C.BG4 : C.BG3)) g.FillRectangle(b, button);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = button.Left + button.Width / 2f, cy = button.Top + button.Height / 2f;
        using (var p = new Pen(Enabled ? C.FG_DIM : C.FG_FAINT, 1.5f * s))
            g.DrawLines(p, [new PointF(cx - 4 * s, cy - 2 * s), new PointF(cx, cy + 2 * s), new PointF(cx + 4 * s, cy - 2 * s)]);
        g.SmoothingMode = SmoothingMode.None;
        using (var p = new Pen(BackColor)) g.DrawRectangle(p, 1, 1, r.Width - 3, r.Height - 3);
        using (var p = new Pen(Focused || DroppedDown ? C.ACCENT2 : _hover && Enabled ? C.FG_FAINT : C.BORDER)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}
