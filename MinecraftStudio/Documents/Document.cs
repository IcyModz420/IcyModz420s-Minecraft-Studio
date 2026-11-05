// MinecraftStudio/Documents/Document.cs: A tab in the main window.
// Owns: Document, NbtSource, FileNbtSource, DelegateNbtSource, NbtDocument.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>A tab in the main window.</summary>
[System.ComponentModel.DesignerCategory("Code")]
abstract class Document : UserControl
{
    protected Document()
    {
        BackColor = C.BG;
        ForeColor = C.FG;
        Dock = DockStyle.Fill;
    }

    public abstract string Title { get; }
    public virtual string? ToolTipText => null;
    public virtual bool IsDirty => false;
    public virtual bool CanSave => false;
    /// <summary>Identity used to avoid opening the same thing twice.</summary>
    public object? Key { get; init; }
    public MainForm Main { get; set; } = null!;
    public event EventHandler? StateChanged;

    protected void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public virtual Task SaveAsync() => Task.CompletedTask;
    public virtual void OnActivated() { }
    /// <summary>Called before closing; return false to keep the tab.</summary>
    public virtual bool ConfirmClose()
    {
        if (!IsDirty) return true;
        var r = Dialogs.Ask(FindForm()!, $"Save changes to {Title}?", Dialogs.AppName, MessageBoxButtons.YesNoCancel);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) SaveAsync().GetAwaiter().GetResult();
        return true;
    }
}

/// <summary>Where an NBT document comes from and goes back to.</summary>
abstract class NbtSource
{
    public abstract string Title { get; }
    public virtual string? Description => null;
    public virtual bool ReadOnly => false;
    public abstract List<NamedTag> Load();
    public abstract void Save(List<NamedTag> roots);
}

/// <summary>An NBT file on disk; keeps compression, dialect and Bedrock header.</summary>
sealed class FileNbtSource(string path) : NbtSource
{
    NbtFile? _file;
    public string Path { get; } = path;
    public NbtFile? File => _file;
    public override string Title => System.IO.Path.GetFileName(Path);
    public override string? Description => _file == null ? null : $"{Path}  ·  {_file.Describe()}";

    public override List<NamedTag> Load()
    {
        _file = NbtFile.Load(Path);
        return _file.Roots;
    }

    public override void Save(List<NamedTag> roots)
    {
        if (_file == null) return;
        Backup.FileBeforeWrite(Path);
        _file.Save(Path);
    }
}

/// <summary>NBT that lives inside something else (a database record, a chunk, a player in level.dat).</summary>
sealed class DelegateNbtSource(string title, Func<List<NamedTag>> load, Action<List<NamedTag>>? save, string? description = null) : NbtSource
{
    public override string Title => title;
    public override string? Description => description;
    public override bool ReadOnly => save == null;
    public override List<NamedTag> Load() => load();
    public override void Save(List<NamedTag> roots) => save?.Invoke(roots);
}

sealed class NbtDocument : Document
{
    readonly NbtSource _source;
    readonly NbtEditor _editor;
    readonly Label _header;

    public NbtDocument(NbtSource source)
    {
        _source = source;
        _editor = new NbtEditor { Dock = DockStyle.Fill, ReadOnly = source.ReadOnly };
        _header = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = this.Px(26), Padding = new Padding(this.Px(8), this.Px(5), 0, 0),
            BackColor = C.BG2, ForeColor = C.FG_DIM, UseMnemonic = false, AutoEllipsis = true,
        };
        _editor.Changed += (_, _) => OnStateChanged();
        Controls.Add(_editor);
        Controls.Add(_header);
        Reload();
    }

    public NbtEditor Editor => _editor;
    public override string Title => _source.Title;
    public override string? ToolTipText => _source.Description;
    public override bool IsDirty => _editor.IsDirty;
    public override bool CanSave => !_source.ReadOnly;

    public void Reload()
    {
        var roots = _source.Load();
        _editor.LoadRoots(roots);
        _header.Text = (_source.ReadOnly ? "Read-only  ·  " : "") + (_source.Description ?? _source.Title);
        OnStateChanged();
    }

    public override Task SaveAsync()
    {
        if (_source.ReadOnly) return Task.CompletedTask;
        _source.Save(_editor.Roots.ToList());
        _editor.MarkSaved();
        OnStateChanged();
        Main.SetStatus($"Saved {Title}");
        return Task.CompletedTask;
    }
}
