// MinecraftStudio/Studio/PaintToolbars.Actions.cs: what the tool bars and their keys do.
// Part of PaintToolbars (main part in PaintToolbars.cs).
// Owns: copy, cut and paste (a picture larger than the area opens the picture dialog to fit it), inserting or replacing
// with a picture (from a file, the clipboard, a drop or My tiles; into the selection when there is one), making it blank,
// colour changes, flipping and turning, resizing what floats, wrap-around shift, editing in another program, and every
// keyboard shortcut of the pixel editors.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class PaintToolbars
{
    ExternalEdit? _external;

    void Tell(string text) => _editor.Tell(text);
    IWin32Window Owner => _host.FindForm() ?? (IWin32Window)_host;

    public void Copy(bool cut)
    {
        if (_editor.Session.Copy(_editor.AreaPixels()) is not { } pixels) return;
        if (!PixelClipboard.Copy(pixels)) { Tell("The clipboard is busy (another program is using it); try again."); return; }
        if (cut && (_editor.Session.Floating != null || _editor.Session.Selection != null))
        {
            _editor.Session.Delete();
            Tell($"Cut {pixels.Width} × {pixels.Height} pixels.");
        }
        else Tell($"Copied {pixels.Width} × {pixels.Height} pixels.");
    }

    /// <summary>Pastes the clipboard's picture: as it is when it fits, otherwise through the picture dialog.</summary>
    public void Paste()
    {
        (RgbaImage Image, string? Name)? clip;
        try { clip = PixelClipboard.FromClipboard(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { Tell("The picture on the clipboard could not be read: " + e.Message); return; }
        if (clip is not { } c) { Tell("There is no picture on the clipboard. (Copy one in any program, or with Ctrl+C here.)"); return; }
        if (_editor.AreaPixels() is not { } area) return;
        if (c.Image.Width <= area.Width && c.Image.Height <= area.Height) { _editor.Session.Paste(c.Image, "Paste"); return; }
        InsertPicture(c.Image, c.Name ?? "the picture on the clipboard");
    }

    /// <summary>
    /// Opens the picture dialog (for a file chosen there, or the picture given). Inserted, the result is pasted to be
    /// placed; replacing, it takes the place of the whole picture (or the selected part) at once, as one step.
    /// </summary>
    public void InsertPicture(RgbaImage? picture = null, string? name = null, bool replace = false)
    {
        if (_editor.AreaPixels() is not { } area) { Tell("Open something to paint on first."); return; }
        _editor.Session.Commit();
        using var dlg = new PictureDialog(_editor, area, picture, name, replace);
        if (dlg.ShowDialog(Owner) != DialogResult.OK || dlg.Result is not { } result) return;
        if (replace) { Replace(result, dlg.Placement, dlg.Target, "Replace with a picture"); return; }
        _editor.Session.Paste(result, "Insert picture", dlg.Placement);
        Tell($"The picture ({result.Width} × {result.Height}) is floating: drag it into place, then press Enter (Esc takes it back).");
    }

    /// <summary>Clears a part of the picture and puts pixels there, as one step.</summary>
    void Replace(RgbaImage pixels, (int X, int Y) at, PixelRect target, string name)
    {
        bool changed = _editor.Session.Apply(name, a =>
        {
            a.Fill(target.X, target.Y, target.Width, target.Height, 0);
            for (int y = 0; y < pixels.Height; y++)
                for (int x = 0; x < pixels.Width; x++)
                    if (target.Contains(at.X + x, at.Y + y) && a.Contains(at.X + x, at.Y + y)) a[at.X + x, at.Y + y] = pixels[x, y];
            return a;
        });
        Tell(changed ? $"Replaced {(target.X == 0 && target.Y == 0 && _editor.AreaPixels() is { } a && a.Width == target.Width && a.Height == target.Height ? _editor.AreaName : "the selected part")} with the picture (Ctrl+Z takes it back)."
                     : "The picture is the same as what was there: nothing changed.");
    }

    /// <summary>Makes the whole picture (or the selected part) blank — white or see-through — as one step.</summary>
    public bool MakeBlank(bool white)
    {
        if (_editor.Blank(white) is not { } blank) { Tell("Open something to paint on first."); return false; }
        _editor.Session.Commit();
        var whole = new PixelRect(0, 0, blank.Width, blank.Height);
        var target = _editor.Session.Selection is { } s && s.Intersect(whole) is { IsEmpty: false } part ? part : whole;
        bool changed = _editor.Session.Apply(white ? "Blank (white)" : "Blank (see-through)", a =>
        {
            a.CopyFrom(blank, target.X, target.Y, target.Width, target.Height, target.X, target.Y);
            return a;
        });
        Tell(changed ? $"{(target == whole ? char.ToUpperInvariant(_editor.AreaName[0]) + _editor.AreaName[1..] : "The selected part")} is blank{(white ? " white" : " (see-through)")} now (Ctrl+Z takes it back)."
                     : "It was blank already.");
        return true;
    }

    /// <summary>My tiles: use a saved tile (as the whole picture, or pasted to place), or save the picture being edited.</summary>
    public void MyTiles(bool save = false)
    {
        if (_editor.AreaPixels() is not { } area) { Tell("Open something to paint on first."); return; }
        _editor.Session.Commit();
        using var dlg = new TileLibraryDialog(_editor) { SaveOnOpen = save };
        if (dlg.ShowDialog(Owner) != DialogResult.OK || dlg.Chosen is not { } c) return;
        var whole = new PixelRect(0, 0, area.Width, area.Height);
        var target = _editor.Session.Selection is { } s && s.Intersect(whole) is { IsEmpty: false } part ? part : whole;
        // A tile of exactly the right size goes in at once; any other size is fitted in the picture dialog.
        if (c.Image.Width == target.Width && c.Image.Height == target.Height)
        {
            if (c.Replace) Replace(c.Image, (target.X, target.Y), target, "Use tile " + c.Name);
            else _editor.Session.Paste(c.Image, "Paste tile " + c.Name, (target.X, target.Y));
            return;
        }
        if (!c.Replace && c.Image.Width <= area.Width && c.Image.Height <= area.Height) { _editor.Session.Paste(c.Image, "Paste tile " + c.Name); return; }
        InsertPicture(c.Image, c.Name, c.Replace);
    }

    /// <summary>Something was dropped on a view: a picture file or a picture from another program goes through the picture dialog.</summary>
    public void HandleDrop(IDataObject data)
    {
        try
        {
            if (PixelClipboard.Read(data) is { } p) InsertPicture(p.Image, p.Name ?? "the dropped picture");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { Dialogs.Error(Owner, "That picture could not be read:\n\n" + e.Message); }
    }

    public void AdjustColours()
    {
        if (_editor.AreaPixels() == null) return;
        using var dlg = new AdjustDialog(_editor);
        dlg.ShowDialog(Owner);
    }

    void QuickColours(string name, ColorAdjustment a)
    {
        if (!_editor.Session.Preview(name, c => ColorAdjust.Apply(c, a))) return;
        _editor.Session.Commit();
    }

    void Wrap(double fraction)
    {
        if (_editor.AreaPixels() is not { } area) return;
        int dx = (int)(area.Width * fraction), dy = (int)(area.Height * fraction);
        _editor.Session.Apply("Shift with wrap-around", a => Resample.Wrap(a, dx, dy));
        Tell("Shifted by half: the edges now meet in the middle, so seams show. Do it again to shift back.");
    }

    void Transform(PixelTransform t)
    {
        if (!_editor.Session.Transform(t)) Tell("Select a part first: a picture that is not square cannot be turned as a whole.");
    }

    void ResizeFloating()
    {
        if (_editor.Session.Floating is not { } f) return;
        string? text = Dialogs.Input(Owner, "Resize", "New size of the floating pixels (width × height, or a percentage such as 50%):", $"{f.Pixels.Width} × {f.Pixels.Height}");
        if (text == null) return;
        if (!TryParseSize(text, f.Pixels.Width, f.Pixels.Height, out int w, out int h)) { Tell("Type a size such as 16 × 16, or a percentage such as 50%."); return; }
        // Shrinking averages (so detail is not just dropped); enlarging keeps pixels crisp.
        _editor.Session.ResizeFloating(w, h, smooth: w < f.Source.Width || h < f.Source.Height);
    }

    internal static bool TryParseSize(string text, int w0, int h0, out int w, out int h)
    {
        w = h = 0;
        string t = text.Trim().Replace(" ", "");
        if (t.EndsWith('%') && double.TryParse(t[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct) && pct > 0)
        {
            w = Math.Max(1, (int)Math.Round(w0 * pct / 100));
            h = Math.Max(1, (int)Math.Round(h0 * pct / 100));
            return w <= 4096 && h <= 4096;
        }
        var parts = t.Split(['×', 'x', 'X', '*', ','], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) && w is > 0 and <= 4096 && h is > 0 and <= 4096;
    }

    public void EditElsewhere()
    {
        _external?.Dispose();
        _external = ExternalEdit.Start(_editor, _host, out string? error);
        Tell(error ?? $"Opened {_editor.AreaName} in another program. Each time you save it there, it comes back here (one Ctrl+Z each).");
    }

    /// <summary>The pixel editors' keys; returns true when the key was used.</summary>
    public bool HandleKey(Keys keyData)
    {
        var session = _editor.Session;
        switch (keyData)
        {
            case Keys.Control | Keys.A: session.SelectAll(); return true;
            case Keys.Control | Keys.D: session.Select(null); return true;
            case Keys.Control | Keys.C: Copy(cut: false); return true;
            case Keys.Control | Keys.X: Copy(cut: true); return true;
            case Keys.Control | Keys.V: Paste(); return true;
            case Keys.Control | Keys.I: InsertPicture(); return true;
            case Keys.Control | Keys.Shift | Keys.I: InsertPicture(replace: true); return true;
            case Keys.Control | Keys.U: AdjustColours(); return true;
            case Keys.Control | Keys.E: EditElsewhere(); return true;
            case Keys.Delete:
            case Keys.Back:
                return session.Delete();
            case Keys.Enter:
                if (!session.IsLive) return false;
                session.Commit();
                return true;
            case Keys.Escape:
                if (session.IsLive) { session.Cancel(); return true; }
                if (session.Selection == null) return false;
                session.Select(null);
                return true;
            case Keys.Shift | Keys.H: Transform(PixelTransform.FlipX); return true;
            case Keys.Shift | Keys.V: Transform(PixelTransform.FlipY); return true;
            case Keys.Shift | Keys.R: Transform(PixelTransform.RotateRight); return true;
            case Keys.Shift | Keys.L: Transform(PixelTransform.RotateLeft); return true;
            case Keys.OemOpenBrackets: _editor.Settings.BrushSize = PrevBrush(); _editor.NotifyState(); return true;
            case Keys.OemCloseBrackets: _editor.Settings.BrushSize = NextBrush(); _editor.NotifyState(); return true;
            case Keys.M: _toggleMirror(); Update(); return true;
            case Keys.X: _swapColours(); return true;
        }
        // Arrow keys nudge what floats (the selection starts floating); Shift moves 8 pixels.
        var arrow = keyData & ~Keys.Shift;
        if (arrow is Keys.Left or Keys.Right or Keys.Up or Keys.Down && (session.Floating != null || session.Selection != null && session.Lift()))
        {
            int step = (keyData & Keys.Shift) != 0 ? 8 : 1;
            var f = session.Floating!;
            var (dx, dy) = arrow switch { Keys.Left => (-step, 0), Keys.Right => (step, 0), Keys.Up => (0, -step), _ => (0, step) };
            session.MoveFloating(f.X + dx, f.Y + dy);
            return true;
        }
        foreach (var (tool, _, key, _) in ToolList)
            if (keyData == (Keys)key[0]) { SetTool(tool); return true; }
        return false;
    }

    int PrevBrush() => BrushSizes.LastOrDefault(n => n < _editor.Settings.BrushSize, 1);
    int NextBrush() => BrushSizes.FirstOrDefault(n => n > _editor.Settings.BrushSize, BrushSizes[^1]);

    public void Dispose() => _external?.Dispose();
}
