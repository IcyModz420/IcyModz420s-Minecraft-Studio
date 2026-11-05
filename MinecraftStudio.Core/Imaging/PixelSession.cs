// MinecraftStudio.Core/Imaging/PixelSession.cs: the selection and the changes being made live in a pixel editor.
// Owns: LiveEdit (a change drawn again from how the area was when it started — a floating paste or move, a colour
// change being tried — so every view shows it while it can still be cancelled) and PixelSession (the selection; paste,
// lift, move, flip/turn, resize, preview, put down, cancel, delete). Shared by the skin, texture and mob editors: each says
// where its picture is (and makes it writable) when a change starts, and turns a finished change into one undo step.
namespace MinecraftStudio.Core.Imaging;

/// <summary>A change to an area drawn again from how the area was when it started, every time something about it changes.</summary>
public sealed class LiveEdit
{
    internal LiveEdit(string name, RgbaImage target, PixelRect area)
    {
        Name = name;
        Target = target;
        Area = area;
        Before = target.Crop(area.X, area.Y, area.Width, area.Height);
    }

    public string Name { get; internal set; }
    public RgbaImage Target { get; }
    /// <summary>The area in the target picture (a frame of a texture, or the whole picture).</summary>
    public PixelRect Area { get; }
    /// <summary>The area's pixels as they were when the change started.</summary>
    public RgbaImage Before { get; }
    public FloatingPixels? Floating { get; internal set; }
    /// <summary>A colour change being tried (on <see cref="MapLimit"/> only, when set), or null.</summary>
    internal Func<uint, uint>? Map { get; set; }
    internal PixelRect? MapLimit { get; set; }

    internal void Restore() => Target.CopyFrom(Before, 0, 0, Area.Width, Area.Height, Area.X, Area.Y);

    internal void Redraw()
    {
        Restore();
        if (Map is { } map)
        {
            var r = (MapLimit ?? new PixelRect(0, 0, Area.Width, Area.Height)).Intersect(new PixelRect(0, 0, Area.Width, Area.Height));
            for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                    Target[Area.X + x, Area.Y + y] = map(Before[x, y]);
        }
        if (Floating is not { } f) return;
        var inside = new PixelRect(0, 0, Area.Width, Area.Height);
        if (f.LiftedFrom is { } from)
        {
            var hole = from.Intersect(inside);
            Target.Fill(Area.X + hole.X, Area.Y + hole.Y, hole.Width, hole.Height, 0);
        }
        var put = f.Bounds.Intersect(inside);
        for (int y = put.Y; y < put.Bottom; y++)
            for (int x = put.X; x < put.Right; x++)
            {
                int tx = Area.X + x, ty = Area.Y + y;
                Target[tx, ty] = Paint.Over(f.Pixels[x - f.X, y - f.Y], Target[tx, ty]);
            }
    }

    /// <summary>Whether the area differs from how it was.</summary>
    public bool Changed()
    {
        for (int y = 0; y < Area.Height; y++)
            if (!Target.Pixels.AsSpan((Area.Y + y) * Target.Width + Area.X, Area.Width).SequenceEqual(Before.Pixels.AsSpan(y * Area.Width, Area.Width))) return true;
        return false;
    }
}

/// <summary>
/// The selection of a pixel editor and the change being made live (pasted or moved pixels, a colour change being
/// tried). Coordinates are relative to the editor's area. The editor supplies three things: the area now, a way to start
/// a change (it records how things were and returns the writable picture), and a way to finish one (commit: one undo
/// step if anything changed; otherwise put back what starting it did).
/// </summary>
public sealed class PixelSession
{
    readonly Func<PixelRect?> _area;
    readonly Func<string, (RgbaImage Image, PixelRect Area)?> _begin;
    readonly Action<string, bool, bool> _end;
    readonly Action _imageChanged, _stateChanged;

    /// <param name="area">The area being edited now (in the picture), or null when nothing is open.</param>
    /// <param name="begin">Starts a change with that name: returns the writable picture and the area in it, or null.</param>
    /// <param name="end">Ends it: (name, commit, changed).</param>
    public PixelSession(Func<PixelRect?> area, Func<string, (RgbaImage Image, PixelRect Area)?> begin, Action<string, bool, bool> end, Action imageChanged, Action stateChanged)
    {
        _area = area;
        _begin = begin;
        _end = end;
        _imageChanged = imageChanged;
        _stateChanged = stateChanged;
    }

    /// <summary>The selected rectangle (relative to the area), or null.</summary>
    public PixelRect? Selection { get; private set; }
    public LiveEdit? Live { get; private set; }
    public FloatingPixels? Floating => Live?.Floating;
    public bool IsLive => Live != null;

    (int Width, int Height)? Size => _area() is { } a ? (a.Width, a.Height) : null;

    // ------------------------------------------------------------------ selection

    /// <summary>Selects a rectangle (clipped to the area; an empty one selects nothing). Anything floating is put down first.</summary>
    public void Select(PixelRect? r)
    {
        Commit();
        Selection = r is { } s && Size is { } size && s.Intersect(new PixelRect(0, 0, size.Width, size.Height)) is { IsEmpty: false } c ? c : null;
        _stateChanged();
    }

    public void SelectAll()
    {
        if (Size is { } size) Select(new PixelRect(0, 0, size.Width, size.Height));
    }

    /// <summary>Forgets the selection and anything live without a step (the area changed: another texture or size).</summary>
    public void Reset()
    {
        if (Live != null) Commit();
        Selection = null;
    }

    // ------------------------------------------------------------------ live changes

    bool Start(string name)
    {
        if (Live != null) return true;
        if (_begin(name) is not { } t) return false;
        Live = new LiveEdit(name, t.Image, t.Area);
        return true;
    }

    void Changed()
    {
        Live?.Redraw();
        if (Floating is { } f) Selection = f.Bounds;
        _imageChanged();
        _stateChanged();
    }

    /// <summary>
    /// Pastes pixels as a floating layer: at the given spot, or where the selection is, or in the middle of the area
    /// (top-left when larger). They become part of the picture when put down.
    /// </summary>
    public bool Paste(RgbaImage pixels, string name, (int X, int Y)? at = null)
    {
        Commit();
        if (Size is not { } size || !Start(name)) return false;
        var (x, y) = at ?? (Selection is { } s ? (s.X, s.Y) : (Math.Max(0, (size.Width - pixels.Width) / 2), Math.Max(0, (size.Height - pixels.Height) / 2)));
        Live!.Floating = new FloatingPixels(pixels, x, y);
        Changed();
        return true;
    }

    /// <summary>The selection's pixels start floating (to be moved); where they were becomes see-through until they are put down there again.</summary>
    public bool Lift(bool keepCopy = false)
    {
        if (Floating != null) return true;
        if (Selection is not { } s || Live != null || !Start(keepCopy ? "Copy and move" : "Move")) return false;
        var pixels = Live!.Before.Crop(s.X, s.Y, s.Width, s.Height);
        Live.Floating = new FloatingPixels(pixels, s.X, s.Y, keepCopy ? null : s);
        Changed();
        return true;
    }

    public void MoveFloating(int x, int y)
    {
        if (Floating is not { } f || (f.X == x && f.Y == y)) return;
        f.X = x;
        f.Y = y;
        Changed();
    }

    /// <summary>Flips or turns what floats, or the selection (lifted first), or the whole area.</summary>
    public bool Transform(PixelTransform t)
    {
        if (Floating == null)
        {
            if (Selection == null)
            {
                // Turning a whole picture that is not square would cut parts of it off.
                if (t is PixelTransform.RotateRight or PixelTransform.RotateLeft && Size is { } size && size.Width != size.Height) return false;
                SelectAll();
            }
            if (!Lift()) return false;
            Live!.Name = t switch { PixelTransform.FlipX => "Flip left to right", PixelTransform.FlipY => "Flip upside down", _ => "Turn" };
        }
        Floating!.Transform(t);
        Changed();
        return true;
    }

    public void ResizeFloating(int width, int height, bool smooth)
    {
        if (Floating is not { } f) return;
        f.Resize(width, height, smooth);
        Changed();
    }

    /// <summary>Tries a colour change on the selection (or the whole area); call again to change it, then put it down or cancel.</summary>
    public bool Preview(string name, Func<uint, uint> map)
    {
        if (Live is { Map: null }) Commit();
        if (!Start(name)) return false;
        Live!.Name = name;
        Live.Map = map;
        Live.MapLimit = Selection;
        Changed();
        return true;
    }

    /// <summary>Puts down what is live: it becomes one undo step (if it changed anything).</summary>
    public void Commit()
    {
        if (Live is not { } l) return;
        bool changed = l.Changed();
        Live = null;
        _end(l.Name, true, changed);
        _imageChanged();
        _stateChanged();
    }

    /// <summary>Throws away what is live: the area goes back to how it was.</summary>
    public void Cancel()
    {
        if (Live is not { } l) return;
        l.Restore();
        Live = null;
        if (l.Floating != null) Selection = l.Floating.LiftedFrom;
        _end(l.Name, false, false);
        _imageChanged();
        _stateChanged();
    }

    // ------------------------------------------------------------------ whole changes

    /// <summary>The pixels to copy: what floats, else the selection, else the whole area (null when nothing is open).</summary>
    public RgbaImage? Copy(RgbaImage? areaNow)
    {
        if (Floating is { } f) return f.Pixels.Clone();
        if (areaNow == null) return null;
        return Selection is { } s ? areaNow.Crop(s.X, s.Y, s.Width, s.Height) : areaNow.Clone();
    }

    /// <summary>Makes the selection (or what floats) see-through, as one step.</summary>
    public bool Delete()
    {
        if (Floating is { } f)
        {
            // What floats goes; a moved selection leaves its hole.
            Live!.Name = "Delete";
            Live.Floating = new FloatingPixels(new RgbaImage(1, 1), f.X, f.Y, f.LiftedFrom);
            Changed();
            Selection = null;
            Commit();
            return true;
        }
        if (Selection is not { } s || !Start("Delete")) return false;
        Live!.Floating = new FloatingPixels(new RgbaImage(1, 1), s.X, s.Y, s);
        Changed();
        Selection = null;
        Commit();
        return true;
    }

    /// <summary>Replaces the area's pixels with a picture of the same size made from them (shift, flip of the whole area …), as one step.</summary>
    public bool Apply(string name, Func<RgbaImage, RgbaImage> change)
    {
        Commit();
        if (!Start(name)) return false;
        var result = change(Live!.Before.Clone());
        if (result.Width == Live.Area.Width && result.Height == Live.Area.Height)
            Live.Target.CopyFrom(result, 0, 0, result.Width, result.Height, Live.Area.X, Live.Area.Y);
        // The picture is drawn now; nothing must redraw it from how it was.
        var l = Live;
        bool changed = l.Changed();
        Live = null;
        _end(l.Name, true, changed);
        _imageChanged();
        _stateChanged();
        return changed;
    }
}
