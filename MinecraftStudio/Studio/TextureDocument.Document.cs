// MinecraftStudio/Studio/TextureDocument.Document.cs: document for TextureDocument.
// Part of TextureDocument (main part in TextureDocument.cs).
using System.Diagnostics;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;

namespace MinecraftStudio.Studio;

partial class TextureDocument
{
    // ------------------------------------------------------------------ document

    public TextureEditor Editor => _editor;
    public override string Title => "Pack: " + _editor.Pack.Name;
    public override string? ToolTipText => _path ?? "Not saved yet";
    public override bool IsDirty => _editor.IsDirty;
    public override bool CanSave => true;

    string EditionName => _editor.Pack.Edition == PackEdition.Bedrock ? "Bedrock" : (_editor.Pack.JavaRelease ?? JavaReleases.Newest).Label;

    /// <summary>The pack as a file for its own edition and version.</summary>
    PackResult Build() => _editor.Pack.Edition == PackEdition.Bedrock
        ? TexturePackWriter.Bedrock(_editor.Pack, _names, DateTime.Now, BedrockAnimated.Value)
        : TexturePackWriter.Java(_editor.Pack, _editor.Pack.JavaRelease ?? JavaReleases.Newest, _names, AtlasFill(_editor.Pack.JavaRelease ?? JavaReleases.Newest));

    public override Task SaveAsync()
    {
        if (_path == null || Directory.Exists(_path)) { SaveAs(); return Task.CompletedTask; }
        var r = Build();
        if (r.Problems.Count > 0 || r.NotIncluded.Count > 0) { if (!PackReport.Confirm(FindForm()!, "Save " + _editor.Pack.Name, r, "Save")) return Task.CompletedTask; }
        if (Write(_path, r.Data)) { _editor.MarkSaved(); OnStateChanged(); }
        return Task.CompletedTask;
    }

    void SaveAs()
    {
        var r = Build();
        if (r.Problems.Count > 0 || r.NotIncluded.Count > 0) { if (!PackReport.Confirm(FindForm()!, "Save " + _editor.Pack.Name, r, "Choose where to save…")) return; }
        Directory.CreateDirectory(PacksFolder);
        bool bedrock = _editor.Pack.Edition == PackEdition.Bedrock;
        using var dlg = new SaveFileDialog
        {
            Filter = bedrock ? "Bedrock pack|*.mcpack" : "Java pack|*.zip", FileName = r.FileName, Title = $"Save the pack ({EditionName})",
            InitialDirectory = _path != null && File.Exists(_path) ? Path.GetDirectoryName(_path) : PacksFolder,
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (!Write(dlg.FileName, r.Data)) return;
        _path = dlg.FileName;
        _editor.MarkSaved();
        OnStateChanged();
    }

    bool Write(string path, byte[] data) => PackExport.Write(FindForm()!, Main, path, data);

    /// <summary>For terrain.png (Java up to 1.4.7): the game's own textures for the tiles the pack does not change.</summary>
    TexturePack? AtlasFill(JavaRelease target)
    {
        if (target.Kind != JavaPackKind.TerrainAtlas) return null;
        // An installed jar of that time first; else the oldest one with separate texture files (similar art).
        var jars = GameFolders.JavaJars();
        foreach (var (version, jar) in jars.AsEnumerable().Reverse())
        {
            try
            {
                var p = TexturePackReader.Read(jar, _names);
                if (p.Textures.Count > 0) return p;
            }
            catch (Exception e) when (e is IOException or InvalidDataException) { Main.SetStatus($"Java {version}: {e.Message}"); }
        }
        return null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Keys a focused list, box or slider uses stay with it (StudioKeys); the rest are the studio's shortcuts.
        if (!StudioKeys.UsesKey(StudioKeys.Focused(this), keyData))
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: _editor.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: _editor.Redo(); return true;
                case Keys.Oemcomma: _editor.SelectFrame(_editor.Frame - 1); return true;
                case Keys.OemPeriod: _editor.SelectFrame(_editor.Frame + 1); return true;
            }
            if (_paint.HandleKey(keyData)) return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveAs(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------ state

    void SetTool(PaintTool tool) => _paint.SetTool(tool);

    void UpdateState()
    {
        _updating = true;
        _undo.Enabled = _editor.CanUndo || _editor.Session.IsLive;
        _redo.Enabled = _editor.CanRedo;
        _undo.ToolTipText = _editor.UndoName is { } u ? $"Undo {u} (Ctrl+Z)" : "Nothing to undo";
        _redo.ToolTipText = _editor.RedoName is { } r ? $"Redo {r} (Ctrl+Y)" : "Nothing to redo";
        var t = _editor.Current;
        int count = t == null ? 0 : TextureEditor.FrameCount(t);
        _frameLabel.Text = t == null ? "" : count > 1 ? $"Frame {_editor.Frame + 1} of {count}" : "Not animated";
        _prevFrame.Enabled = _editor.Frame > 0;
        _nextFrame.Enabled = _editor.Frame < count - 1;
        _addFrame.Enabled = t != null;
        _removeFrame.Enabled = count > 1;
        _frameTime.Enabled = _smooth.Enabled = t?.Animation != null;
        if (t?.Animation != null) { _frameTime.Value = t.Animation.FrameTime; _smooth.Checked = t.Animation.Interpolate; }
        _info.Text = t == null || _editor.Selected == null ? "" : Describe(_editor.Selected, t);
        _updating = false;
        UpdateStatus();
        OnStateChanged();
    }

    string Describe(string key, PackTexture t)
    {
        var lines = new List<string>();
        var r = TextureEditor.FrameRect(t, 0);
        lines.Add($"{TextureNames.Display(key)}: {r.Width} × {r.Height}{(TextureEditor.FrameCount(t) > 1 ? $", {TextureEditor.FrameCount(t)} frames" : "")}");
        lines.Add(_editor.InPack(key) ? "In this pack." : "The game's own texture: painting on it adds it to the pack.");
        if (_names.IsKnown(key))
        {
            var newest = _names.JavaNames(key, JavaReleases.Newest);
            lines.Add(newest.Count > 0 ? "Java: " + string.Join(", ", newest) : "Java: only in older versions");
            var targets = _names.BedrockTargets(key);
            var main = targets.Where(x => x.Role.Length == 0).Select(x => x.Name).ToList();
            lines.Add(main.Count > 0 ? "Bedrock: " + string.Join(", ", main) : "Bedrock: no such texture");
            foreach (var d in targets.Where(x => x.Role.Length > 0))
                lines.Add(d.Role == "opaque" ? $"Bedrock's fast-graphics picture ({d.Name}) is made from it, solid."
                    : d.Tint != null ? $"Bedrock's inventory picture ({d.Name}) is made from it, coloured." : $"Bedrock's inventory picture ({d.Name}) stays the game's own.");
            if (targets.Any(x => x.Evidence == "uncertain"))
                lines.Add("The Bedrock match is by look only: check it in the game.");
        }
        else lines.Add(key.StartsWith("bedrock:") ? "Only Bedrock has this file." : "Not a texture of the game (a mod's or the pack's own).");
        return string.Join("\n", lines);
    }

    void UpdateStatus()
    {
        if (_editor.Current is not { } t || _editor.Hover is not { } h) { _status.Text = $"{_editor.Pack.Textures.Count} texture(s) in the pack  ·  made for {EditionName}"; return; }
        var r = TextureEditor.FrameRect(t, _editor.Frame);
        uint c = t.Image[r.X + h.X, r.Y + h.Y];
        _status.Text = $"{h.X}, {h.Y}  ·  {PaintOps.ToHex(c)}{(RgbaImage.A(c) == 0 ? " (see-through)" : "")}";
    }

    // ------------------------------------------------------------------ texture as a whole

    void ImportPicture()
    {
        if (_editor.Current is not { } t) return;
        using var dlg = new OpenFileDialog { Filter = PixelClipboard.OpenFilter, Title = "Replace with a picture (used as it is; Insert a picture fits and places one)" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var img = PixelClipboard.Load(dlg.FileName);
            var r0 = TextureEditor.FrameRect(t, 0);
            // A strip as tall as several frames keeps the animation; anything else becomes a single picture.
            TextureAnimation? anim = img.Width == r0.Width && img.Height % r0.Height == 0 && img.Height > r0.Height
                ? t.Animation?.Clone() ?? new TextureAnimation { FrameTime = 4 } : null;
            if (anim?.Frames != null) anim.Frames = null;
            _editor.ReplaceImage("Replace picture", img, anim);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Dialogs.Error(FindForm()!, $"Could not use {Path.GetFileName(dlg.FileName)}:\n\n{ex.Message}");
        }
    }

    void ExportPicture()
    {
        if (_editor.Current is not { } t || _editor.Selected == null) return;
        using var dlg = new SaveFileDialog { Filter = "PNG image|*.png", FileName = SkinExport.SafeFileName(TextureNames.Display(_editor.Selected)) + ".png", Title = "Save the texture as a picture" };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Write(dlg.FileName, t.Image.ToPng());
    }

    void UseAsIcon()
    {
        if (_editor.Current is not { } t) return;
        var frame = t.FirstFrame();
        _editor.Pack.Icon = frame.Width < 64 ? frame.Scaled(frame.Width * (64 / frame.Width), frame.Height * (64 / frame.Width)) : frame;
        Main.SetStatus("The pack's icon is now " + TextureNames.Display(_editor.Selected!));
    }
}
