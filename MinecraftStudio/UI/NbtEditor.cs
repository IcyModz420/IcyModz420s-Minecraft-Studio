// MinecraftStudio/UI/NbtEditor.cs: Tree editor for NBT with typed icons, paging for huge lists, SNBT copy/paste
// and undo.
// Owns: NbtEditor.
using System.Globalization;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.UI;

/// <summary>Tree editor for NBT with typed icons, paging for huge lists, SNBT copy/paste and undo.</summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed partial class NbtEditor : UserControl
{
    const int PageSize = 500;
    const string ClipboardFormat = "MinecraftStudio.NbtNode";

    sealed class NodeInfo
    {
        public NbtTag Tag = null!;
        public NbtTag? Parent;          // compound or list; null for roots
        public NamedTag? Root;          // set for root nodes
        public string? Name;            // compound key or root name
        public int ListIndex = -1;
        public bool Loaded;
    }

    sealed class MoreInfo
    {
        public NbtList List = null!;
        public int Start;
    }

    readonly TreeView _tree;
    readonly ToolStrip _bar;
    readonly ImageList _icons;
    readonly List<NamedTag> _roots = [];
    readonly Stack<List<NamedTag>> _undo = new();
    readonly Stack<List<NamedTag>> _redo = new();
    readonly Font _mono;
    ToolStripLabel _status = null!;
    string _lastFind = "";

    public event EventHandler? Changed;

    public NbtEditor()
    {
        BackColor = C.BG;
        _mono = Theme.Mono(9.5f);
        _icons = Icons.Create(this.Px(16));
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = C.BG,
            ForeColor = C.FG,
            BorderStyle = BorderStyle.None,
            ImageList = _icons,
            Font = _mono,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = false,
            DrawMode = TreeViewDrawMode.OwnerDrawText,
            ItemHeight = this.Px(20),
        };
        Theme.DarkControl(_tree);
        _tree.BeforeExpand += (_, e) => EnsureLoaded(e.Node!);
        _tree.DrawNode += DrawNode;
        _tree.NodeMouseDoubleClick += (_, e) => { if (e.Node?.Tag is MoreInfo) LoadMore(e.Node); else EditSelected(); };
        _tree.KeyDown += OnTreeKey;
        _tree.NodeMouseClick += (_, e) => { if (e.Button == MouseButtons.Right) _tree.SelectedNode = e.Node; };
        _tree.AfterSelect += (_, _) => UpdateStatus();
        _tree.ContextMenuStrip = BuildContextMenu();

        _bar = Theme.ToolStrip();
        _bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        _bar.Items.AddRange(
        [
            Button("Add", Glyphs.Add, "Add a tag inside the selected compound or list (Insert)", null, BuildAddMenu()),
            Button("Edit", Glyphs.Edit, "Edit the selected value (Enter / F2)", (_, _) => EditSelected()),
            Button("Rename", Glyphs.Rename, "Rename the selected tag", (_, _) => RenameSelected()),
            Button("Delete", Glyphs.Delete, "Delete the selected tag (Del)", (_, _) => DeleteSelected()),
            new ToolStripSeparator(),
            Button("", Glyphs.Copy, "Copy as SNBT (Ctrl+C)", (_, _) => CopySelected()),
            Button("", Glyphs.Paste, "Paste SNBT into the selected compound/list (Ctrl+V)", (_, _) => PasteIntoSelected()),
            Button("", Glyphs.Up, "Move up (Alt+Up)", (_, _) => MoveSelected(-1)),
            Button("", Glyphs.Down, "Move down (Alt+Down)", (_, _) => MoveSelected(1)),
            new ToolStripSeparator(),
            Button("", Glyphs.Undo, "Undo (Ctrl+Z)", (_, _) => Undo()),
            Button("", Glyphs.Redo, "Redo (Ctrl+Y)", (_, _) => Redo()),
            new ToolStripSeparator(),
            Button("Find", Glyphs.Find, "Find a name or value (Ctrl+F, F3 for next)", (_, _) => Find(false)),
            Button("SNBT", Glyphs.Code, "Edit the selected tag as SNBT text", (_, _) => EditAsSnbt()),
            Button("", Glyphs.Expand, "Expand all below the selection", (_, _) => ExpandAll()),
            Button("", Glyphs.Collapse, "Collapse all", (_, _) => _tree.CollapseAll()),
        ]);
        _status = new ToolStripLabel("") { Alignment = ToolStripItemAlignment.Right, ForeColor = C.FG_DIM };
        _bar.Items.Add(_status);

        Controls.Add(_tree);
        Controls.Add(_bar);
    }

    ToolStripItem Button(string text, string glyph, string tip, EventHandler? click, ToolStripDropDown? dropDown = null)
    {
        var img = Theme.Glyph(glyph, C.FG, this.Px(16));
        ToolStripItem b = dropDown != null
            ? new ToolStripDropDownButton(text, img) { DropDown = dropDown }
            : new ToolStripButton(text, img, click);
        b.ToolTipText = tip;
        b.ForeColor = C.FG;
        b.DisplayStyle = text.Length > 0 ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
        return b;
    }

    // ------------------------------------------------------------------ public API

    public bool ReadOnly { get; set; }
    public bool IsDirty { get; private set; }
    public IReadOnlyList<NamedTag> Roots => _roots;

    public void LoadRoots(IEnumerable<NamedTag> roots)
    {
        _roots.Clear();
        _roots.AddRange(roots);
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
        Rebuild(null);
    }

    public void LoadTag(NbtTag tag, string name = "") => LoadRoots([new NamedTag(name, tag)]);

    public void MarkSaved() { IsDirty = false; }
}
