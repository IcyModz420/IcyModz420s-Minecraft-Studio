// MinecraftStudio.Core/Skins/SkinEditor.cs: the skin painting model: paint tools, layers and edits.
// Owns: PaintTool, SkinEditor (the skin, the tool and colours, what is shown, the selection and live changes through
// PixelSession, and the undo history — one step per stroke or change). Strokes are painted by PaintStroke.
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Skins;

/// <summary>
/// The tools of the pixel editors. Line, Rectangle and Ellipse draw from where the button went down to where it is
/// let go; Select marks a rectangle (dragging inside it moves its pixels); Dither paints every other pixel.
/// </summary>
public enum PaintTool { Pencil, Eraser, Fill, Picker, Shade, Noise, Line, Rectangle, Ellipse, Select, Dither }

/// <summary>
/// The skin being edited and everything both views share: the tool, the colours, which layer the 3D view paints, what
/// is shown, and the undo history (one step per stroke).
/// </summary>
public sealed class SkinEditor : IPixelEditor
{
    /// <summary>A state of the skin; the id tells states apart for "unsaved changes" (undo then a new edit is a new state).</summary>
    sealed record Step(string Name, RgbaImage Image, SkinModel Model, int State);

    const int HistoryLimit = 300;
    readonly List<Step> _undo = [], _redo = [];
    readonly Random _rng = new();
    int _state, _savedState, _nextState;

    // The stroke in progress, and how the skin was before it (or before the live change).
    Step? _strokeStart, _liveStart;
    PaintStroke? _stroke;
    bool _strokeSecondary;

    public SkinEditor(Skin skin)
    {
        Skin = skin;
        Pixels = new SkinPixels(skin.Model, skin.Image.Width);
        Session = new PixelSession(
            () => new PixelRect(0, 0, Skin.Image.Width, Skin.Image.Height),
            name =>
            {
                _liveStart = new Step(name, Skin.Image.Clone(), Skin.Model, _state);
                return (Skin.Image, new PixelRect(0, 0, Skin.Image.Width, Skin.Image.Height));
            },
            (name, commit, changed) =>
            {
                if (commit && changed && _liveStart != null) Push(_liveStart with { Name = name });
                _liveStart = null;
            },
            () => ImageChanged?.Invoke(), () => StateChanged?.Invoke());
    }

    public PaintSettings Settings { get; } = new();
    public PixelSession Session { get; }
    public RgbaImage? AreaPixels() => Skin.Image.Clone();
    public string AreaName => "the skin";

    public RgbaImage? Blank(bool white)
    {
        int size = Skin.Image.Width;
        var img = new RgbaImage(size, size);
        if (!white) return img;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (Pixels.At(x, y) is { Overlay: false }) img[x, y] = 0xFFFFFFFF;
        return img;
    }

    public Skin Skin { get; private set; }
    public SkinPixels Pixels { get; private set; }
    PaintTool _tool = PaintTool.Pencil, _lastPaintTool = PaintTool.Pencil;

    /// <summary>The tool; after a colour is picked with the eyedropper, painting continues with the tool used before it.</summary>
    public PaintTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            if (value != PaintTool.Picker) _lastPaintTool = value;
        }
    }
    public uint Primary { get; set; } = 0xFF2E7DD1;
    public uint Secondary { get; set; } = 0xFFFFFFFF;
    public bool Mirror { get; set; }
    /// <summary>The 3D view paints the second layer (hat, jacket, sleeves, trousers) instead of the base.</summary>
    public bool PaintOverlay { get; set; }
    public bool ShowOverlay { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public HashSet<string> HiddenParts { get; } = [];
    /// <summary>The pixel under the mouse in either view (highlighted in both), or null.</summary>
    public (int X, int Y)? Hover { get; private set; }

    /// <summary>The pixels changed (repaint).</summary>
    public event Action? ImageChanged;
    /// <summary>The arm model or the size changed (the views rebuild their layout).</summary>
    public event Action? LayoutChanged;
    /// <summary>Tool, layer, visibility, history or saved state changed.</summary>
    public event Action? StateChanged;
    public event Action? HoverChanged;
    /// <summary>The eyedropper picked a colour: (colour, for the secondary colour).</summary>
    public event Action<uint, bool>? ColorPicked;
    /// <summary>A colour was painted with (for the recent colours).</summary>
    public event Action<uint>? ColorUsed;
    /// <summary>Something the player should know right away (shown in the status bar).</summary>
    public event Action<string>? Message;

    public bool IsDirty => _state != _savedState;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    public void MarkSaved() { _savedState = _state; StateChanged?.Invoke(); }

    public void SetHover((int X, int Y)? p)
    {
        if (Hover == p) return;
        Hover = p;
        HoverChanged?.Invoke();
    }

    public void NotifyState() => StateChanged?.Invoke();
    public void Tell(string message) => Message?.Invoke(message);

    // ------------------------------------------------------------------ whole-skin changes

    /// <summary>Records the skin as it is now, before a change (anything other than a stroke).</summary>
    void Checkpoint(string name)
    {
        Session.Commit();
        Push(new Step(name, Skin.Image.Clone(), Skin.Model, _state));
    }

    /// <summary>Makes a recorded state the newest undo step (the skin has changed since it).</summary>
    void Push(Step before)
    {
        _undo.Add(before);
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
        _state = ++_nextState;
    }

    public void SetModel(SkinModel model)
    {
        if (model == Skin.Model) return;
        Checkpoint(model == SkinModel.Slim ? "Slim arms" : "Classic arms");
        Skin.SetModel(model);
        Relayout();
    }

    public void SetSize(int size)
    {
        if (size == Skin.Image.Width) return;
        Checkpoint(size > 64 ? $"HD {size} × {size}" : "64 × 64");
        Skin.SetSize(size);
        Relayout();
    }

    /// <summary>Replaces the whole picture (a filter, a paste or a template) as one undoable step.</summary>
    public void ReplaceImage(string name, RgbaImage image, SkinModel model)
    {
        Checkpoint(name);
        bool relayout = image.Width != Skin.Image.Width || model != Skin.Model;
        Skin = new Skin(image, model) { Name = Skin.Name };
        if (relayout) Relayout(); else { ImageChanged?.Invoke(); StateChanged?.Invoke(); }
    }

    void Relayout()
    {
        if (Pixels.Size != Skin.Image.Width) Session.Reset();
        if (Pixels.Model != Skin.Model || Pixels.Size != Skin.Image.Width) Pixels = new SkinPixels(Skin.Model, Skin.Image.Width);
        Hover = null;
        LayoutChanged?.Invoke();
        ImageChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>Undoes the last step; while something is pasted or being moved, Undo takes that back instead.</summary>
    public void Undo()
    {
        if (Session.IsLive) { Session.Cancel(); return; }
        Travel(_undo, _redo);
    }

    public void Redo()
    {
        if (Session.IsLive) { Session.Commit(); return; }
        Travel(_redo, _undo);
    }

    void Travel(List<Step> from, List<Step> to)
    {
        if (from.Count == 0 || _stroke != null) return;
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(new Step(step.Name, Skin.Image.Clone(), Skin.Model, _state));
        _state = step.State;
        var name = Skin.Name;
        Skin = new Skin(step.Image.Clone(), step.Model) { Name = name };
        Relayout();
    }

    // ------------------------------------------------------------------ strokes

    public bool Stroking => _stroke != null;

    /// <summary>
    /// Starts a stroke at an image pixel. The eyedropper only picks; it never makes a step. Selections are made through
    /// <see cref="Session"/>; anything pasted is put down before painting starts.
    /// </summary>
    public void BeginStroke(int x, int y, bool secondary)
    {
        if (!Skin.Image.Contains(x, y) || _stroke != null) return;
        if (Tool == PaintTool.Picker)
        {
            PickColor(x, y, secondary);
            return;
        }
        if (Tool == PaintTool.Select) return;
        Session.Commit();
        _strokeStart = new Step(ToolName, Skin.Image.Clone(), Skin.Model, _state);
        _strokeSecondary = secondary;
        _stroke = new PaintStroke(Skin.Image, new PixelRect(0, 0, Skin.Image.Width, Skin.Image.Height), Tool, secondary ? Secondary : Primary, secondary, Settings, x, y,
            Mirror ? Mirrored : null, FillRegion, Session.Selection, _rng);
        if (_stroke.To(x, y, connect: false)) ImageChanged?.Invoke();
    }

    /// <summary>
    /// Continues the stroke to an image pixel. The 2D view connects successive points with a line; the 3D view passes
    /// each pixel it picks (neighbouring points may be on different faces there). Shapes are drawn from where the
    /// stroke started; constrain keeps them square or at 45°.
    /// </summary>
    public void StrokeTo(int x, int y, bool connect, bool constrain = false)
    {
        if (_stroke == null || !Skin.Image.Contains(x, y)) return;
        _stroke.Constrain = constrain;
        if (_stroke.To(x, y, connect)) ImageChanged?.Invoke();
    }

    /// <summary>
    /// Takes the colour of an image pixel as the first or second colour. A see-through pixel is not taken (painting
    /// with it would erase). With the eyedropper tool, the tool used before it comes back, ready to paint.
    /// </summary>
    public bool PickColor(int x, int y, bool secondary)
    {
        if (!Skin.Image.Contains(x, y)) return false;
        uint c = Skin.Image[x, y];
        if (RgbaImage.A(c) == 0)
        {
            Message?.Invoke("That pixel is see-through, so no colour was picked. (To make pixels see-through, use the eraser.)");
            return false;
        }
        ColorPicked?.Invoke(c, secondary);
        if (_tool == PaintTool.Picker)
        {
            _tool = _lastPaintTool;
            StateChanged?.Invoke();
        }
        Message?.Invoke($"Picked {Paint.ToHex(c)} as the {(secondary ? "second" : "first")} colour.");
        return true;
    }

    public void EndStroke()
    {
        if (_stroke == null) return;
        if (_stroke.Changed())
        {
            Push(_strokeStart!);
            if (Tool is not (PaintTool.Eraser or PaintTool.Shade or PaintTool.Noise)) ColorUsed?.Invoke(_strokeSecondary ? Secondary : Primary);
        }
        _stroke = null;
        _strokeStart = null;
        StateChanged?.Invoke();
    }

    IEnumerable<(int X, int Y)> Mirrored(int x, int y) => Pixels.Mirror(x, y) is { } m ? [m] : [];

    /// <summary>
    /// A fill stays on the face it starts on (on unused pixels: on the area of that colour), so it never runs into a
    /// neighbouring body part in the texture.
    /// </summary>
    Func<int, int, bool>? FillRegion(int x, int y) => Pixels.At(x, y) is { } p
        ? (ix, iy) => Pixels.At(ix, iy) is { } q && q.Face == p.Face
        : (ix, iy) => Pixels.At(ix, iy) == null;

    string ToolName => Tool switch
    {
        PaintTool.Pencil => "Paint",
        PaintTool.Eraser => "Erase",
        PaintTool.Fill => "Fill",
        PaintTool.Shade => "Shade",
        PaintTool.Noise => "Noise",
        PaintTool.Line => "Line",
        PaintTool.Rectangle => "Rectangle",
        PaintTool.Ellipse => "Ellipse",
        PaintTool.Dither => "Dither",
        _ => "Edit",
    };
}
