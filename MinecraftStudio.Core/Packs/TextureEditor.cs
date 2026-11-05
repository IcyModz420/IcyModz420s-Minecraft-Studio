// MinecraftStudio.Core/Packs/TextureEditor.cs: A texture pack being edited: the pack (only the textures that
// were changed), the game's own textures it starts from, the texture and animation frame in view, the tool and
// colours, the selection and live changes (PixelSession), and the undo history across all textures.
// Owns: TextureEditor. Strokes are painted by PaintStroke. The Mob Studio uses it too, for a pack of mob textures.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;

namespace MinecraftStudio.Core.Packs;

/// <summary>
/// A texture pack being edited: the pack (only the textures that were changed), the game's own textures it starts
/// from, the texture and animation frame in view, the tool and colours, and the undo history across all textures.
/// A game texture becomes part of the pack the moment it is first changed.
/// </summary>
public sealed class TextureEditor : IPixelEditor
{
    /// <summary>A texture as it was before a change (null: it was not in the pack).</summary>
    sealed record Step(string Name, string Key, PackTexture? Before, int Frame, int State);

    const int HistoryLimit = 300;
    readonly List<Step> _undo = [], _redo = [];
    readonly Random _rng = new();
    int _state, _savedState, _nextState;
    PaintTool _tool = PaintTool.Pencil, _lastPaintTool = PaintTool.Pencil;

    Step? _strokeStart, _liveStart;
    PaintStroke? _stroke;
    bool _strokeSecondary;

    public TextureEditor(TexturePack pack, TexturePack? basePack)
    {
        Pack = pack;
        Base = basePack;
        Session = new PixelSession(
            () => Current is { } t ? Rect(FrameRect(t, Frame)) : null,
            name =>
            {
                if (Selected == null || Current == null) return null;
                _liveStart = new Step(name, Selected, Pack.Textures.GetValueOrDefault(Selected)?.Clone(), Frame, _state);
                var w = Writable();
                return (w.Image, Rect(FrameRect(w, Frame)));
            },
            (name, commit, changed) =>
            {
                if (_liveStart is not { } start) return;
                _liveStart = null;
                if (commit && changed) Push(start with { Name = name });
                else if (start.Before == null && Pack.Textures.Remove(start.Key)) PackChanged?.Invoke();
            },
            () => ImageChanged?.Invoke(), () => StateChanged?.Invoke());
    }

    static PixelRect Rect(FrameArea a) => new(a.X, a.Y, a.Width, a.Height);

    public PaintSettings Settings { get; } = new();
    public PixelSession Session { get; }

    /// <summary>A copy of the frame in view, or null.</summary>
    public RgbaImage? AreaPixels()
    {
        if (Current is not { } t) return null;
        var r = FrameRect(t, Frame);
        return t.Image.Crop(r.X, r.Y, r.Width, r.Height);
    }

    public string AreaName => Selected == null ? "the texture" : TextureNames.Display(Selected);

    public RgbaImage? Blank(bool white)
    {
        if (AreaPixels() is not { } a) return null;
        var img = new RgbaImage(a.Width, a.Height);
        if (white) img.Fill(0, 0, a.Width, a.Height, 0xFFFFFFFF);
        return img;
    }

    public TexturePack Pack { get; private set; }
    /// <summary>The game's own textures (the list of everything there is, and where new edits start), or null.</summary>
    public TexturePack? Base { get; private set; }
    public string? Selected { get; private set; }
    public int Frame { get; private set; }
    public uint Primary { get; set; } = 0xFF2E7DD1;
    public uint Secondary { get; set; } = 0xFFFFFFFF;
    /// <summary>Paint the mirror image across the texture's middle too.</summary>
    public bool MirrorX { get; set; }
    public bool ShowGrid { get; set; } = true;
    public (int X, int Y)? Hover { get; private set; }

    public PaintTool Tool
    {
        get => _tool;
        set { _tool = value; if (value != PaintTool.Picker) _lastPaintTool = value; }
    }

    public event Action? ImageChanged;
    public event Action? SelectionChanged;
    public event Action? StateChanged;
    public event Action? HoverChanged;
    /// <summary>A texture joined or left the pack (the list marks pack textures).</summary>
    public event Action? PackChanged;
    public event Action<uint, bool>? ColorPicked;
    public event Action<uint>? ColorUsed;
    public event Action<string>? Message;

    public bool IsDirty => _state != _savedState;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    public void MarkSaved() { _savedState = _state; StateChanged?.Invoke(); }

    /// <summary>A file of the pack other than a texture changed (a block model): the pack has changes to save.</summary>
    public void OtherFileChanged(string what)
    {
        _state = ++_nextState;
        StateChanged?.Invoke();
        Message?.Invoke(what);
    }
    public void NotifyState() => StateChanged?.Invoke();
    public void Tell(string message) => Message?.Invoke(message);

    /// <summary>Every texture there is: the game's and the pack's own, by readable name.</summary>
    public List<string> AllKeys()
    {
        var keys = new HashSet<string>(Pack.Textures.Keys, StringComparer.Ordinal);
        if (Base != null) keys.UnionWith(Base.Textures.Keys);
        return keys.OrderBy(TextureNames.Display, StringComparer.OrdinalIgnoreCase).ThenBy(k => k, StringComparer.Ordinal).ToList();
    }

    public bool InPack(string key) => Pack.Textures.ContainsKey(key);

    /// <summary>The texture as it looks now: the pack's version, or the game's.</summary>
    public PackTexture? Texture(string key) => Pack.Textures.GetValueOrDefault(key) ?? Base?.Textures.GetValueOrDefault(key);

    public PackTexture? Current => Selected == null ? null : Texture(Selected);

    public void Select(string? key)
    {
        if (_stroke != null || key == Selected) return;
        Session.Reset();
        Selected = key;
        Frame = 0;
        Hover = null;
        SelectionChanged?.Invoke();
        ImageChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void SelectFrame(int frame)
    {
        if (Current is not { } t || _stroke != null) return;
        Session.Commit();
        Frame = Math.Clamp(frame, 0, FrameCount(t) - 1);
        ImageChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void SetHover((int X, int Y)? p)
    {
        if (Hover == p) return;
        Hover = p;
        HoverChanged?.Invoke();
    }

    // ------------------------------------------------------------------ frames

    public static int FrameCount(PackTexture t) => t.Animation?.FrameCount(t.Image) ?? 1;

    /// <summary>Where a frame sits in the texture's image (frames run left to right, then down).</summary>
    public static FrameArea FrameRect(PackTexture t, int frame)
    {
        if (t.Animation == null) return new FrameArea(0, 0, t.Image.Width, t.Image.Height);
        var (w, h) = t.Animation.FrameSize(t.Image);
        int cols = Math.Max(1, t.Image.Width / w);
        return new FrameArea(frame % cols * w, frame / cols * h, w, h);
    }

    public readonly record struct FrameArea(int X, int Y, int Width, int Height)
    {
        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    }

    // ------------------------------------------------------------------ history

    /// <summary>
    /// Makes sure the texture is in the pack (copied from the game the first time) and records how it was, as one
    /// undoable step.
    /// </summary>
    PackTexture Edit(string key, string name)
    {
        Session.Commit();
        var before = Pack.Textures.GetValueOrDefault(key);
        Push(new Step(name, key, before?.Clone(), Frame, _state));
        if (before != null) return before;
        var copy = Base?.Textures.GetValueOrDefault(key)?.Clone() ?? throw new InvalidOperationException("There is no texture called " + key);
        Pack.Textures[key] = copy;
        PackChanged?.Invoke();
        return copy;
    }

    /// <summary>Makes a recorded texture state the newest undo step (the texture has changed since it).</summary>
    void Push(Step before)
    {
        _undo.Add(before);
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
        _state = ++_nextState;
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
        to.Add(step with { Before = Pack.Textures.GetValueOrDefault(step.Key)?.Clone(), State = _state });
        bool wasIn = Pack.Textures.ContainsKey(step.Key);
        if (step.Before == null) Pack.Textures.Remove(step.Key);
        else Pack.Textures[step.Key] = step.Before.Clone();
        _state = step.State;
        if (wasIn != Pack.Textures.ContainsKey(step.Key)) PackChanged?.Invoke();
        // Show what changed back.
        if (Selected != step.Key) Session.Reset();
        Selected = step.Key;
        Frame = Math.Clamp(step.Frame, 0, Current is { } c ? FrameCount(c) - 1 : 0);
        SelectionChanged?.Invoke();
        ImageChanged?.Invoke();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ painting (frame coordinates)

    public bool Stroking => _stroke != null;

    /// <summary>
    /// Starts a stroke at a pixel of the frame in view. The eyedropper only picks; selections are made through
    /// <see cref="Session"/>; anything pasted is put down first. A game texture joins the pack for the stroke and leaves
    /// it again if the stroke changes nothing.
    /// </summary>
    public void BeginStroke(int x, int y, bool secondary)
    {
        if (Current is not { } t || Selected == null || _stroke != null) return;
        var r = FrameRect(t, Frame);
        if (x < 0 || y < 0 || x >= r.Width || y >= r.Height) return;
        if (Tool == PaintTool.Picker) { PickColor(x, y, secondary); return; }
        if (Tool == PaintTool.Select) return;
        Session.Commit();
        _strokeStart = new Step(ToolName, Selected, Pack.Textures.GetValueOrDefault(Selected)?.Clone(), Frame, _state);
        _strokeSecondary = secondary;
        var target = Writable();
        int w = r.Width;
        _stroke = new PaintStroke(target.Image, Rect(r), Tool, secondary ? Secondary : Primary, secondary, Settings, x, y,
            MirrorX ? (px, py) => [(w - 1 - px, py)] : null, null, Session.Selection, _rng);
        if (_stroke.To(x, y, connect: false)) ImageChanged?.Invoke();
    }

    /// <summary>Continues the stroke (frame coordinates); shapes are drawn from where it started, constrain keeps them square or at 45°.</summary>
    public void StrokeTo(int x, int y, bool connect, bool constrain = false)
    {
        if (_stroke == null || Selected == null || Texture(Selected) is not { } t) return;
        var r = FrameRect(t, Frame);
        if (x < 0 || y < 0 || x >= r.Width || y >= r.Height) return;
        _stroke.Constrain = constrain;
        if (_stroke.To(x, y, connect)) ImageChanged?.Invoke();
    }

    public void EndStroke()
    {
        if (_stroke == null) return;
        if (_stroke.Changed())
        {
            Push(_strokeStart!);
            if (Tool is not (PaintTool.Eraser or PaintTool.Shade or PaintTool.Noise)) ColorUsed?.Invoke(_strokeSecondary ? Secondary : Primary);
        }
        else if (_strokeStart!.Before == null && Pack.Textures.Remove(_strokeStart.Key))
        {
            // Nothing changed: a game texture copied in for this stroke goes back out of the pack.
            PackChanged?.Invoke();
        }
        _stroke = null;
        _strokeStart = null;
        StateChanged?.Invoke();
    }

    /// <summary>The selected texture as part of the pack (copied from the game the first time, without a step: the change is the step).</summary>
    PackTexture Writable()
    {
        if (Pack.Textures.TryGetValue(Selected!, out var t)) return t;
        var copy = Base!.Textures[Selected!].Clone();
        Pack.Textures[Selected!] = copy;
        PackChanged?.Invoke();
        return copy;
    }

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

    /// <summary>Takes a colour from the frame; see-through pixels are not taken. The eyedropper hands back to the tool before it.</summary>
    public bool PickColor(int x, int y, bool secondary)
    {
        if (Current is not { } t) return false;
        var r = FrameRect(t, Frame);
        if (x < 0 || y < 0 || x >= r.Width || y >= r.Height) return false;
        uint c = t.Image[r.X + x, r.Y + y];
        if (RgbaImage.A(c) == 0)
        {
            Message?.Invoke("That pixel is see-through, so no colour was picked. (To make pixels see-through, use the eraser.)");
            return false;
        }
        ColorPicked?.Invoke(c, secondary);
        if (_tool == PaintTool.Picker) { _tool = _lastPaintTool; StateChanged?.Invoke(); }
        Message?.Invoke($"Picked {Paint.ToHex(c)} as the {(secondary ? "second" : "first")} colour.");
        return true;
    }

    // ------------------------------------------------------------------ whole-texture changes (each one step)

    /// <summary>Takes the texture out of the pack, so the game's own shows again.</summary>
    public void Revert()
    {
        Session.Commit();
        if (Selected == null || !Pack.Textures.ContainsKey(Selected) || _stroke != null) return;
        if (Base?.Textures.ContainsKey(Selected) != true)
        {
            Message?.Invoke("This texture is only in the pack (the game has no texture of that name), so there is nothing to go back to.");
            return;
        }
        Push(new Step("Use the game's texture", Selected, Pack.Textures[Selected].Clone(), Frame, _state));
        Session.Reset();
        Pack.Textures.Remove(Selected);
        Frame = 0;
        PackChanged?.Invoke();
        ImageChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>Replaces the selected texture's picture (an imported image, a new resolution) as one step.</summary>
    public void ReplaceImage(string name, RgbaImage image, TextureAnimation? animation)
    {
        if (Selected == null || _stroke != null) return;
        var t = Edit(Selected, name);
        t.Image = image;
        t.Animation = animation;
        Frame = Math.Clamp(Frame, 0, FrameCount(t) - 1);
        if (Session.Selection is { } sel && !sel.Intersect(Rect(FrameRect(t, Frame)) with { X = 0, Y = 0 }).Equals(sel)) Session.Reset();
        ImageChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>Changes the resolution of every frame (nearest pixel up, averaged down by halves).</summary>
    public void Resize(int frameSize)
    {
        if (Current is not { } t || frameSize < 1) return;
        int frames = FrameCount(t);
        var r0 = FrameRect(t, 0);
        if (r0.Width == frameSize && r0.Height == frameSize) return;
        var image = new RgbaImage(frameSize, frameSize * frames);
        for (int i = 0; i < frames; i++)
        {
            var r = FrameRect(t, i);
            var frame = t.Image.Crop(r.X, r.Y, r.Width, r.Height);
            while (frame.Width / 2 >= frameSize && frame.Width % 2 == 0 && frame.Height % 2 == 0) frame = frame.Halved();
            if (frame.Width != frameSize || frame.Height != frameSize) frame = frame.Scaled(frameSize, frameSize);
            image.CopyFrom(frame, 0, 0, frameSize, frameSize, 0, i * frameSize);
        }
        var anim = t.Animation?.Clone();
        if (anim != null) { anim.FrameWidth = null; anim.FrameHeight = null; }
        ReplaceImage($"{frameSize} × {frameSize}", image, anim);
    }

    /// <summary>Adds a copy of the frame in view after it (a still texture becomes animated).</summary>
    public void AddFrame()
    {
        if (Current is not { } t) return;
        var (frames, w, h) = Strip(t);
        frames.Insert(Frame + 1, frames[Frame].Clone());
        var anim = t.Animation?.Clone() ?? new TextureAnimation { FrameTime = 4 };
        if (anim.Frames != null) anim.Frames = null;
        ReplaceImage("Add frame", Join(frames, w, h), anim);
        SelectFrame(Frame + 1);
    }

    public void RemoveFrame()
    {
        if (Current is not { } t || FrameCount(t) < 2) return;
        var (frames, w, h) = Strip(t);
        frames.RemoveAt(Frame);
        var anim = frames.Count > 1 ? t.Animation!.Clone() : null;
        if (anim?.Frames != null) anim.Frames = null;
        int keep = Math.Min(Frame, frames.Count - 1);
        ReplaceImage("Remove frame", Join(frames, w, h), anim);
        SelectFrame(keep);
    }

    public void SetTiming(int frameTime, bool interpolate)
    {
        if (Current is not { Animation: not null } t) return;
        if (t.Animation.FrameTime == Math.Max(1, frameTime) && t.Animation.Interpolate == interpolate) return;
        var anim = t.Animation.Clone();
        anim.FrameTime = Math.Max(1, frameTime);
        anim.Interpolate = interpolate;
        ReplaceImage("Animation timing", t.Image.Clone(), anim);
    }

    static (List<RgbaImage> Frames, int W, int H) Strip(PackTexture t)
    {
        var r0 = FrameRect(t, 0);
        var list = new List<RgbaImage>();
        for (int i = 0; i < FrameCount(t); i++)
        {
            var r = FrameRect(t, i);
            list.Add(t.Image.Crop(r.X, r.Y, r.Width, r.Height));
        }
        return (list, r0.Width, r0.Height);
    }

    static RgbaImage Join(List<RgbaImage> frames, int w, int h)
    {
        var img = new RgbaImage(w, h * frames.Count);
        for (int i = 0; i < frames.Count; i++) img.CopyFrom(frames[i], 0, 0, w, h, 0, i * h);
        return img;
    }

    /// <summary>Switches to another pack (after New or Open): history starts over.</summary>
    public void Load(TexturePack pack, TexturePack? basePack)
    {
        Session.Reset();
        Pack = pack;
        Base = basePack;
        _undo.Clear();
        _redo.Clear();
        _state = _savedState = ++_nextState;
        Selected = null;
        Frame = 0;
        PackChanged?.Invoke();
        SelectionChanged?.Invoke();
        ImageChanged?.Invoke();
        StateChanged?.Invoke();
    }
}
