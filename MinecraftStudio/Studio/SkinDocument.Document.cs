// MinecraftStudio/Studio/SkinDocument.Document.cs: document for SkinDocument.
// Part of SkinDocument (main part in SkinDocument.cs).
using System.Diagnostics;
using MinecraftStudio.Core.Imaging;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Documents;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class SkinDocument
{
    // ------------------------------------------------------------------ document

    public SkinEditor Editor => _editor;
    public override string Title => "Skin: " + _editor.Skin.Name;
    public override string? ToolTipText => _path ?? "Not saved yet";
    public override bool IsDirty => _editor.IsDirty;
    public override bool CanSave => true;

    public override Task SaveAsync()
    {
        if (_path == null) { SaveAs(); return Task.CompletedTask; }
        if (_fileIsLegacy)
        {
            var r = Dialogs.Ask(FindForm()!, $"{Path.GetFileName(_path)} is an old 64 × 32 skin.\n\nYes: save it in the 64 × 64 layout (what Java 1.8+ and Bedrock use).\nNo: keep it 64 × 32 (for old versions; slim arms and the second layer of the body, arms and legs cannot be kept).",
                Dialogs.AppName, MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return Task.CompletedTask;
            if (r == DialogResult.No)
            {
                var legacy = SkinExport.LegacyPng(_editor.Skin);
                if (!ExportNotes.Confirm(FindForm()!, "Save as 64 × 32", legacy.Notes, "Save")) return Task.CompletedTask;
                if (!Write(_path, legacy)) return Task.CompletedTask;
                _editor.MarkSaved();
                OnStateChanged();
                return Task.CompletedTask;
            }
        }
        var file = new SkinFile { FileName = Path.GetFileName(_path), Data = _editor.Skin.Image.ToPng() };
        if (!Write(_path, file)) return Task.CompletedTask;
        _fileIsLegacy = false;
        _editor.MarkSaved();
        OnStateChanged();
        return Task.CompletedTask;
    }

    void SaveAs()
    {
        Directory.CreateDirectory(SkinsFolder);
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG image|*.png", FileName = SkinExport.SafeFileName(_editor.Skin.Name) + ".png",
            InitialDirectory = _path != null ? Path.GetDirectoryName(_path) : SkinsFolder, Title = "Save skin",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var file = new SkinFile { FileName = Path.GetFileName(dlg.FileName), Data = _editor.Skin.Image.ToPng() };
        if (!Write(dlg.FileName, file)) return;
        _path = dlg.FileName;
        _fileIsLegacy = false;
        _editor.MarkSaved();
        OnStateChanged();
    }

    /// <summary>
    /// Writes a file safely: to a temporary file first, read back and compared, then moved into place; an existing
    /// file is backed up before it is replaced. Returns false (after telling the player) when anything went wrong.
    /// </summary>
    bool Write(string path, SkinFile file)
    {
        if (file.Problems.Count > 0)
        {
            Dialogs.Error(FindForm()!, "The file did not come out right, so nothing was saved:\n\n" + string.Join("\n", file.Problems));
            return false;
        }
        try
        {
            WriteVerified(path, file.Data);
            Main.SetStatus("Saved " + path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(FindForm()!, $"Could not save {path}:\n\n{ex.Message}");
            return false;
        }
    }

    public static void WriteVerified(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path)) Backup.FileBeforeWrite(path);
        string temp = path + ".partial";
        File.WriteAllBytes(temp, data);
        if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(data))
        {
            File.Delete(temp);
            throw new IOException("The saved file does not match what was written (is the disk full?).");
        }
        File.Move(temp, path, overwrite: true);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var focused = ActiveControl;
        while (focused is ContainerControl cc && cc.ActiveControl != null) focused = cc.ActiveControl;
        bool typing = focused is TextBoxBase;
        if (!typing)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: _editor.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: _editor.Redo(); return true;
                // O switches which layer the 3D view paints (L is the line tool).
                case Keys.O: if (_paintOverlay.Checked) _paintBase.Checked = true; else _paintOverlay.Checked = true; return true;
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
        _arms.SelectedIndex = _editor.Skin.Model == SkinModel.Slim ? 1 : 0;
        _size.SelectedIndex = _editor.Skin.Image.Width > 64 ? 1 : 0;
        if (_name.Text.Trim() != _editor.Skin.Name) _name.Text = _editor.Skin.Name;
        _updating = false;
        OnStateChanged();
    }

    void UpdateChecks()
    {
        var lines = new List<string>(_editor.Skin.Check());
        if (_editor.Skin.Image.Width > 64) lines.Add("HD skin: Bedrock takes 128 × 128 skins; Java needs 64 × 64, so it is scaled down when exported for Java.");
        foreach (var n in _editor.Skin.LoadNotes) lines.Add(n);
        _checks.Text = lines.Count == 0 ? "No problems: every base-layer pixel is solid." : string.Join("\n\n", lines);
        _checks.ForeColor = _editor.Skin.SeeThroughBasePixels() > 0 ? C.ORANGE : C.FG_DIM;
    }

    void UpdateStatus()
    {
        if (_editor.Hover is not { } h) { _status.Text = $"{_editor.Skin.Image.Width} × {_editor.Skin.Image.Width}, {(_editor.Skin.Model == SkinModel.Slim ? "slim" : "classic")} arms"; return; }
        uint c = _editor.Skin.Image[h.X, h.Y];
        string where = _editor.Pixels.At(h.X, h.Y) is { } p
            ? $"{p.Part}, {p.Face.Side.ToString().ToLowerInvariant()} face, {(p.Overlay ? "second layer" : "base layer")}"
            : "not used by the model";
        _status.Text = $"{h.X}, {h.Y}  ·  {where}  ·  {PaintOps.ToHex(c)}{(RgbaImage.A(c) == 0 ? " (see-through)" : "")}";
    }
}
