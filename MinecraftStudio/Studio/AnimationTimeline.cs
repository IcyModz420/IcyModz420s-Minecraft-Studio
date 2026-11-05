// MinecraftStudio/Studio/AnimationTimeline.cs: the Animation Studio's timeline — time across, one row per bone, every
// keyframe as a diamond.
// Owns: AnimationTimeline — the time ruler and the current time (click or drag to move it), each bone's keys coloured by
// what they change (rotation orange, position green, scale blue; a formula for the whole animation is a band), clicking
// a key to select it, dragging keys to new times (snapped to twentieths of a second, the game's ticks), a right click
// to delete a key or make it smooth or linear, and the wheel to scroll through the bones.
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Models;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

[System.ComponentModel.DesignerCategory("Code")]
sealed class AnimationTimeline : Control
{
    const double Snap = 0.05;
    EntityAnimation? _animation;
    IReadOnlyList<string> _bones = [];
    int _scroll;
    enum DragKind { None, Time, Key }
    DragKind _drag;
    (string Bone, string Channel, double Time) _dragKey;
    double _dragTo;

    public AnimationTimeline()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = C.BG;
        AccessibleName = "Animation timeline";
    }

    public static readonly Color RotationColor = Color.FromArgb(240, 160, 70), PositionColor = Color.FromArgb(110, 200, 120), ScaleColor = Color.FromArgb(110, 170, 250);

    /// <summary>The animation shown, and the model's bones (rows in this order; bones the animation moves come first).</summary>
    public void ShowAnimation(EntityAnimation? animation, IReadOnlyList<string> bones)
    {
        _animation = animation;
        var moved = animation?.Bones.Select(b => b.Bone).ToList() ?? [];
        _bones = moved.Concat(bones.Where(b => !moved.Contains(b, StringComparer.OrdinalIgnoreCase))).ToList();
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _bones.Count - 1));
        Invalidate();
    }

    public double Time { get; set; }
    public string? SelectedBone { get; set; }
    public (string Bone, string Channel, double Time)? SelectedKey { get; set; }

    public event Action<double>? TimeChanged;
    public event Action<string>? BoneClicked;
    public event Action<string, string, double>? KeyClicked;
    public event Action<string, string, double, double>? KeyMoved;
    public event Action<string, string, double>? KeyDeleted;
    public event Action<string, string, double, KeyLerp>? KeyLerpChanged;

    int Ruler => this.Px(24);
    int RowHeight => this.Px(20);
    int LabelWidth => this.Px(130);
    double Length => Math.Max(0.05, _animation?.PlayLength ?? 1);
    float X(double t) => LabelWidth + (float)(t / Length) * Math.Max(1, Width - LabelWidth - this.Px(12));
    double TimeAt(int x) => Math.Clamp((x - LabelWidth) / (double)Math.Max(1, Width - LabelWidth - this.Px(12)) * Length, 0, Length);
    int RowTop(int i) => Ruler + (i - _scroll) * RowHeight;

    static Color ColorOf(string channel) => channel switch { "rotation" => RotationColor, "position" => PositionColor, _ => ScaleColor };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using var f = Theme.Ui(8.25f);
        if (_animation == null)
        {
            TextRenderer.DrawText(g, "Choose an animation on the right.", f, ClientRectangle, C.FG_DIM, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        // Ruler: a tick every twentieth, a label every quarter (or every second for long animations).
        using (var bg = new SolidBrush(C.BG3)) g.FillRectangle(bg, 0, 0, Width, Ruler);
        double label = Length > 4 ? 1 : Length > 1.5 ? 0.5 : 0.25;
        using var tick = new Pen(C.BORDER);
        for (double t = 0; t <= Length + 1e-9; t += Snap)
        {
            float x = X(t);
            bool major = Math.Abs(t / label - Math.Round(t / label)) < 1e-6;
            g.DrawLine(tick, x, Ruler - (major ? this.Px(10) : this.Px(4)), x, Ruler);
            if (major) TextRenderer.DrawText(g, $"{t:0.##}s", f, new Point((int)x + this.Px(2), this.Px(2)), C.FG_DIM);
        }
        // Rows.
        for (int i = _scroll; i < _bones.Count; i++)
        {
            int top = RowTop(i);
            if (top > Height) break;
            string bone = _bones[i];
            bool selected = string.Equals(bone, SelectedBone, StringComparison.OrdinalIgnoreCase);
            using (var bg = new SolidBrush(selected ? C.SELECTION : i % 2 == 0 ? C.BG : C.BG2)) g.FillRectangle(bg, 0, top, Width, RowHeight);
            TextRenderer.DrawText(g, bone, f, new Rectangle(this.Px(6), top, LabelWidth - this.Px(8), RowHeight), _animation.Bone(bone) != null ? C.FG : C.FG_FAINT,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (_animation.Bone(bone) is not { } ba) continue;
            int lane = 0;
            foreach (var (name, ch) in ba.Channels())
            {
                var color = ColorOf(name);
                float y = top + this.Px(4) + lane * this.Px(5);
                lane++;
                if (!ch.IsKeyed)
                {
                    // A value or formula for the whole animation.
                    using var band = new SolidBrush(Color.FromArgb(90, color));
                    g.FillRectangle(band, X(0), y, X(Length) - X(0), this.Px(3));
                    continue;
                }
                foreach (var k in ch.Keys)
                {
                    double time = _drag == DragKind.Key && _dragKey == (bone, name, k.Time) ? _dragTo : k.Time;
                    bool isSel = SelectedKey is { } sk && sk.Bone.Equals(bone, StringComparison.OrdinalIgnoreCase) && sk.Channel == name && Math.Abs(sk.Time - k.Time) < 1e-6;
                    Diamond(g, X(time), top + RowHeight / 2f, isSel ? Color.White : color, k.Lerp == KeyLerp.Smooth);
                }
            }
        }
        // The current time.
        using var now = new Pen(C.ACCENT2, 2);
        g.DrawLine(now, X(Time), 0, X(Time), Height);
        using var sep = new Pen(C.BORDER);
        g.DrawLine(sep, LabelWidth - 1, 0, LabelWidth - 1, Height);
    }

    void Diamond(Graphics g, float x, float y, Color color, bool smooth)
    {
        float r = this.Px(5);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(color);
        if (smooth) g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
        else g.FillPolygon(b, [new PointF(x, y - r), new PointF(x + r, y), new PointF(x, y + r), new PointF(x - r, y)]);
        using var outline = new Pen(Color.FromArgb(160, 0, 0, 0));
        if (smooth) g.DrawEllipse(outline, x - r, y - r, 2 * r, 2 * r);
        else g.DrawPolygon(outline, [new PointF(x, y - r), new PointF(x + r, y), new PointF(x, y + r), new PointF(x - r, y)]);
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>The key (bone, channel, time) near a point, or null.</summary>
    (string Bone, string Channel, double Time)? KeyAt(Point p)
    {
        if (_animation == null || p.Y < Ruler) return null;
        int row = (p.Y - Ruler) / RowHeight + _scroll;
        if (row < 0 || row >= _bones.Count || _animation.Bone(_bones[row]) is not { } ba) return null;
        float r = this.Px(7);
        (string, string, double)? best = null;
        float bestD = r;
        foreach (var (name, ch) in ba.Channels())
            foreach (var k in ch.Keys)
            {
                float d = Math.Abs(X(k.Time) - p.X);
                if (d <= bestD) { bestD = d; best = (_bones[row], name, k.Time); }
            }
        return best;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_animation == null) return;
        if (e.X < LabelWidth && e.Y >= Ruler)
        {
            int row = (e.Y - Ruler) / RowHeight + _scroll;
            if (row >= 0 && row < _bones.Count) BoneClicked?.Invoke(_bones[row]);
            return;
        }
        var key = KeyAt(e.Location);
        if (e.Button == MouseButtons.Right && key is { } rk) { KeyMenu(rk, e.Location); return; }
        if (e.Button != MouseButtons.Left) return;
        if (key is { } k)
        {
            _drag = DragKind.Key;
            _dragKey = k;
            _dragTo = k.Time;
            KeyClicked?.Invoke(k.Bone, k.Channel, k.Time);
        }
        else
        {
            _drag = DragKind.Time;
            TimeChanged?.Invoke(SnapTime(TimeAt(e.X)));
        }
        Capture = true;
    }

    double SnapTime(double t) => (ModifierKeys & Keys.Shift) != 0 ? t : Math.Round(t / Snap) * Snap;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        switch (_drag)
        {
            case DragKind.Time:
                TimeChanged?.Invoke(SnapTime(TimeAt(e.X)));
                break;
            case DragKind.Key:
                _dragTo = SnapTime(TimeAt(e.X));
                Invalidate();
                break;
            default:
                Cursor = KeyAt(e.Location) != null ? Cursors.SizeWE : Cursors.Default;
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == DragKind.Key && Math.Abs(_dragTo - _dragKey.Time) > 1e-6) KeyMoved?.Invoke(_dragKey.Bone, _dragKey.Channel, _dragKey.Time, _dragTo);
        _drag = DragKind.None;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta), 0, Math.Max(0, _bones.Count - 1));
        Invalidate();
    }

    void KeyMenu((string Bone, string Channel, double Time) k, Point at)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        void Add(string text, Action a) => menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => a()) { ForeColor = C.FG });
        Add($"Delete this {k.Channel} key ({k.Time:0.##}s)", () => KeyDeleted?.Invoke(k.Bone, k.Channel, k.Time));
        Add("Smooth from here", () => KeyLerpChanged?.Invoke(k.Bone, k.Channel, k.Time, KeyLerp.Smooth));
        Add("Straight from here", () => KeyLerpChanged?.Invoke(k.Bone, k.Channel, k.Time, KeyLerp.Linear));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, at);
    }
}
