// MinecraftStudio/Studio/ExternalEdit.cs: editing a picture in another program (Paint, Paint.NET, GIMP, Aseprite …) and
// getting it back automatically.
// Owns: ExternalEdit — the pixels are saved as a PNG in this program's own temporary folder and opened with the program
// Windows uses to edit pictures; every time that program saves the file, the pixels come back into the editor as one
// undoable step (only while the same texture or skin is open; otherwise the editor says where to find it).
using System.Diagnostics;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Studio;

/// <summary>A picture being edited in another program; disposing it stops watching the file.</summary>
sealed class ExternalEdit : IDisposable
{
    static string Folder => Path.Combine(Path.GetTempPath(), "MinecraftStudio", "Edit");
    readonly IPixelEditor _editor;
    readonly Control _ui;
    readonly string _path, _area;
    readonly FileSystemWatcher _watcher;
    readonly System.Windows.Forms.Timer _settle;
    DateTime _seen;

    ExternalEdit(IPixelEditor editor, Control ui, string path)
    {
        _editor = editor;
        _ui = ui;
        _path = path;
        _area = editor.AreaName;
        _seen = File.GetLastWriteTimeUtc(path);
        // Programs save in bursts (write, rename, touch): wait until the file has been quiet for a moment.
        _settle = new System.Windows.Forms.Timer { Interval = 450 };
        _settle.Tick += (_, _) => { _settle.Stop(); TakeBack(); };
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Poke();
        _watcher.Created += (_, _) => Poke();
        _watcher.Renamed += (_, _) => Poke();
    }

    /// <summary>Opens the editor's pixels in another program; returns null (and the reason) when it cannot.</summary>
    public static ExternalEdit? Start(IPixelEditor editor, Control ui, out string? error)
    {
        error = null;
        if (editor.AreaPixels() is not { } pixels) { error = "Open a texture first."; return null; }
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"{SafeName.For(editor.AreaName, "picture")} {DateTime.Now:HHmmss}.png");
            File.WriteAllBytes(path, pixels.ToPng());
            var edit = new ExternalEdit(editor, ui, path);
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "edit" }); }
            catch (System.ComponentModel.Win32Exception)
            {
                // No "edit" program for pictures: the one that opens them.
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            return edit;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            error = "The picture could not be opened in another program: " + e.Message;
            return null;
        }
    }

    void Poke()
    {
        if (_ui.IsDisposed) return;
        try { _ui.BeginInvoke(() => { _settle.Stop(); _settle.Start(); }); }
        catch (InvalidOperationException) { }
    }

    void TakeBack()
    {
        DateTime now;
        try { now = File.GetLastWriteTimeUtc(_path); }
        catch (IOException) { return; }
        if (now == _seen) return;
        RgbaImage img;
        try { img = PixelClipboard.Load(_path); }
        catch (IOException) { _settle.Start(); return; }   // still being written: try again shortly
        catch (InvalidDataException e) { Say($"The picture saved by the other program could not be read: {e.Message}"); return; }
        _seen = now;
        if (_editor.AreaName != _area)
        {
            Say($"The other program saved its picture of {_area}; open {_area} again to bring the change in.");
            _seen = DateTime.MinValue;
            return;
        }
        var area = _editor.AreaPixels();
        if (area != null && area.Width == img.Width && area.Height == img.Height)
        {
            if (!area.SamePixels(img)) _editor.Session.Apply("Edited in another program", _ => img);
            Say("Brought in the picture saved by the other program (Ctrl+Z takes it back).");
        }
        else
        {
            // A different size: it floats, to be placed (or resized) and put down.
            _editor.Session.Paste(img, "Edited in another program");
            Say($"The other program saved a {img.Width} × {img.Height} picture; it is floating — drag it into place and press Enter.");
        }
    }

    void Say(string text) => _editor.Tell(text);

    public void Dispose()
    {
        _watcher.Dispose();
        _settle.Dispose();
    }
}
