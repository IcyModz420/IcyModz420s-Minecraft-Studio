// MinecraftStudio/UI/NbtEditor.Commands.cs: commands for NbtEditor.
// Part of NbtEditor (main part in NbtEditor.cs).
using System.Globalization;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.UI;

partial class NbtEditor
{
    // ------------------------------------------------------------------ editing commands

    bool CheckWritable()
    {
        if (!ReadOnly) return true;
        Dialogs.Info(FindForm()!, "This view is read-only.");
        return false;
    }

    public void EditSelected()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || !CheckWritable()) return;
        if (info.Tag is NbtCompound or NbtList) { EditAsSnbt(); return; }
        string current = info.Tag switch
        {
            NbtString s => s.Value,
            NbtByteArray or NbtIntArray or NbtLongArray => Snbt.Write(info.Tag, SnbtStyle.Compact),
            _ => ValueText(info.Tag),
        };
        bool multi = info.Tag is NbtString { Value.Length: > 60 } || info.Tag is NbtString s2 && s2.Value.Contains('\n') || NbtTypeInfo.IsArray(info.Tag.Type);
        string? text = Dialogs.Input(FindForm()!, $"Edit {NbtTypeInfo.Name(info.Tag.Type)}", $"{NameOf(info)} ({NbtTypeInfo.Name(info.Tag.Type)})", current, multi);
        if (text == null) return;
        NbtTag newTag;
        try
        {
            newTag = info.Tag is NbtString ? new NbtString(text)
                : NbtTypeInfo.IsArray(info.Tag.Type) ? ParseArray(text, info.Tag.Type)
                : Snbt.ParseAs(text, info.Tag.Type);
        }
        catch (Exception ex) { Dialogs.Error(FindForm()!, ex.Message); return; }
        BeginEdit();
        Replace(node, newTag);
        Committed(node);
    }

    static NbtTag ParseArray(string text, NbtType type)
    {
        string t = text.Trim();
        if (!t.StartsWith('['))
        {
            string prefix = type switch { NbtType.ByteArray => "B", NbtType.IntArray => "I", _ => "L" };
            t = $"[{prefix};{t}]";
        }
        var parsed = Snbt.Parse(t);
        if (parsed.Type != type) throw new SnbtException($"Expected a {NbtTypeInfo.Name(type)}.", 0, 1, 1);
        return parsed;
    }

    public void EditAsSnbt()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || !CheckWritable()) return;
        string text = Snbt.Write(info.Tag, SnbtStyle.Pretty);
        while (true)
        {
            string? edited = Dialogs.Input(FindForm()!, $"Edit {NameOf(info)} as SNBT", "Stringified NBT (the same syntax as /data commands):", text, multiline: true);
            if (edited == null) return;
            try
            {
                var tag = Snbt.Parse(edited);
                if (info.Parent is NbtList l && l.Count > 1 && tag.Type != l.ElementType)
                    throw new SnbtException($"This list holds {NbtTypeInfo.Name(l.ElementType)} values.", 0, 1, 1);
                BeginEdit();
                Replace(node, tag);
                Committed(node);
                return;
            }
            catch (SnbtException ex)
            {
                Dialogs.Error(FindForm()!, ex.Message);
                text = edited;
            }
        }
    }

    public void RenameSelected()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || !CheckWritable()) return;
        if (info.ListIndex >= 0) { Dialogs.Info(FindForm()!, "List items have no names."); return; }
        string? name = Dialogs.Input(FindForm()!, "Rename", "New name:", info.Name ?? "");
        if (name == null || name == info.Name) return;
        BeginEdit();
        if (info.Root != null) info.Root.Name = name;
        else if (info.Parent is NbtCompound c)
        {
            if (c.ContainsKey(name) && Dialogs.Ask(FindForm()!, $"A tag named \"{name}\" already exists here. Replace it?") != DialogResult.Yes) { _undo.Pop(); return; }
            c.Rename(info.Name!, name);
            if (node.Parent != null) { var p = node.Parent; Refresh(p); p.Expand(); Committed(FindChild(p, name)); return; }
        }
        info.Name = name;
        node.Text = Label(info);
        Committed(node);
    }

    static TreeNode? FindChild(TreeNode parent, string name)
    {
        foreach (TreeNode n in parent.Nodes) if (n.Tag is NodeInfo i && i.Name == name) return n;
        return null;
    }

    public void DeleteSelected()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || !CheckWritable()) return;
        if (info.Root != null) { Dialogs.Info(FindForm()!, "The root tag cannot be deleted."); return; }
        BeginEdit();
        var parentNode = node.Parent!;
        int index = node.Index;
        if (info.Parent is NbtCompound c) c.Remove(info.Name!);
        else if (info.Parent is NbtList l) l.RemoveAt(info.ListIndex);
        Refresh(parentNode);
        parentNode.Expand();
        parentNode.Text = Label((NodeInfo)parentNode.Tag!);
        Committed(parentNode.Nodes.Count == 0 ? parentNode : parentNode.Nodes[Math.Min(index, parentNode.Nodes.Count - 1)]);
    }

    public void MoveSelected(int delta)
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || info.Root != null || !CheckWritable()) return;
        int from = info.ListIndex >= 0 ? info.ListIndex : ((NbtCompound)info.Parent!).IndexOf(info.Name!);
        int count = info.Parent is NbtList l ? l.Count : ((NbtCompound)info.Parent!).Count;
        int to = from + delta;
        if (to < 0 || to >= count) return;
        BeginEdit();
        if (info.Parent is NbtList list) list.Move(from, to);
        else ((NbtCompound)info.Parent!).Move(from, to);
        var parentNode = node.Parent!;
        Refresh(parentNode);
        parentNode.Expand();
        Committed(to < parentNode.Nodes.Count ? parentNode.Nodes[to] : null);
    }

    ToolStripDropDown BuildAddMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        foreach (NbtType t in Enum.GetValues<NbtType>())
        {
            if (t == NbtType.End) continue;
            var type = t;
            menu.Items.Add(new ToolStripMenuItem(NbtTypeInfo.Name(t), _icons.Images[Icons.KeyFor(t)], (_, _) => AddChild(type)) { ForeColor = C.FG });
        }
        return menu;
    }

    public void AddChild(NbtType type)
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || !CheckWritable()) return;
        // Adding to a value adds a sibling in its container.
        if (info.Tag is not (NbtCompound or NbtList))
        {
            if (node.Parent == null) return;
            node = node.Parent;
            info = (NodeInfo)node.Tag!;
        }
        var tag = NbtTypeInfo.CreateDefault(type);
        if (info.Tag is NbtList l && l.Count > 0 && l.ElementType != type)
        {
            Dialogs.Error(FindForm()!, $"This list holds {NbtTypeInfo.Name(l.ElementType)} values; a {NbtTypeInfo.Name(type)} cannot be added.");
            return;
        }
        string? name = null;
        if (info.Tag is NbtCompound c)
        {
            name = Dialogs.Input(FindForm()!, $"Add {NbtTypeInfo.Name(type)}", "Name of the new tag:", UniqueName(c, "new" + type));
            if (name == null) return;
            if (c.ContainsKey(name)) { Dialogs.Error(FindForm()!, $"\"{name}\" already exists in this compound."); return; }
        }
        BeginEdit();
        if (info.Tag is NbtCompound comp) comp[name!] = tag;
        else ((NbtList)info.Tag).Add(tag);
        Refresh(node);
        node.Text = Label(info);
        node.Expand();
        var added = node.Nodes.Count > 0 ? node.Nodes[^1] : node;
        if (added.Tag is MoreInfo && node.Nodes.Count > 1) added = node.Nodes[^2];
        Committed(added);
        if (!NbtTypeInfo.IsContainer(type)) EditSelected();
    }

    static string UniqueName(NbtCompound c, string baseName)
    {
        if (!c.ContainsKey(baseName)) return baseName;
        for (int i = 2; ; i++) if (!c.ContainsKey(baseName + i)) return baseName + i;
    }

    public void DuplicateSelected()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || info.Root != null || !CheckWritable()) return;
        BeginEdit();
        var copy = info.Tag.Clone();
        if (info.Parent is NbtCompound c)
        {
            string name = UniqueName(c, info.Name + "_copy");
            c.Insert(c.IndexOf(info.Name!) + 1, name, copy);
        }
        else if (info.Parent is NbtList l) l.Insert(info.ListIndex + 1, copy);
        var parent = node.Parent!;
        Refresh(parent);
        parent.Expand();
        parent.Text = Label((NodeInfo)parent.Tag!);
        Committed(node.Index + 1 < parent.Nodes.Count ? parent.Nodes[node.Index + 1] : null);
    }

    public void CopySelected()
    {
        if (Selected is not NodeInfo info) return;
        var data = new DataObject();
        string snbt = Snbt.Write(info.Tag, SnbtStyle.Compact);
        data.SetText(snbt);
        data.SetData(ClipboardFormat, (info.ListIndex >= 0 ? "" : info.Name ?? "") + "\n" + snbt);
        Clipboard.SetDataObject(data, true);
    }

    public void PasteIntoSelected()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag is not NodeInfo info || !CheckWritable()) return;
        string? name = null, text = null;
        if (Clipboard.GetData(ClipboardFormat) is string own)
        {
            int nl = own.IndexOf('\n');
            name = own[..nl];
            text = own[(nl + 1)..];
        }
        else if (Clipboard.ContainsText()) text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text)) return;
        NbtTag tag;
        try { tag = Snbt.Parse(text); }
        catch (SnbtException ex) { Dialogs.Error(FindForm()!, "The clipboard does not hold valid SNBT: " + ex.Message); return; }
        if (info.Tag is not (NbtCompound or NbtList))
        {
            if (node.Parent == null) return;
            node = node.Parent;
            info = (NodeInfo)node.Tag!;
        }
        if (info.Tag is NbtCompound c)
        {
            name = Dialogs.Input(FindForm()!, "Paste", "Name for the pasted tag:", UniqueName(c, string.IsNullOrEmpty(name) ? "pasted" : name));
            if (name == null) return;
            BeginEdit();
            c[name] = tag;
        }
        else if (info.Tag is NbtList l)
        {
            if (l.Count > 0 && l.ElementType != tag.Type) { Dialogs.Error(FindForm()!, $"This list holds {NbtTypeInfo.Name(l.ElementType)} values."); return; }
            BeginEdit();
            l.Add(tag);
        }
        Refresh(node);
        node.Text = Label(info);
        node.Expand();
        Committed(node.Nodes.Count > 0 ? node.Nodes[^1] : node);
    }

    public void ExpandAll()
    {
        var start = _tree.SelectedNode ?? (_tree.Nodes.Count > 0 ? _tree.Nodes[0] : null);
        if (start == null) return;
        _tree.BeginUpdate();
        int budget = 20000;
        void Walk(TreeNode n)
        {
            if (budget-- <= 0) return;
            EnsureLoaded(n);
            n.Expand();
            foreach (TreeNode child in n.Nodes) if (child.Tag is NodeInfo) Walk(child);
        }
        Walk(start);
        _tree.EndUpdate();
    }

    /// <summary>Finds the next tag whose name or value contains the text, loading nodes as needed.</summary>
    public void Find(bool next)
    {
        if (!next || _lastFind.Length == 0)
        {
            string? q = Dialogs.Input(FindForm()!, "Find", "Find a tag name or value:", _lastFind);
            if (string.IsNullOrEmpty(q)) return;
            _lastFind = q;
        }
        var order = new List<TreeNode>();
        void Walk(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is not NodeInfo) continue;
                order.Add(n);
                if (order.Count > 200_000) return;
                EnsureLoaded(n);
                Walk(n.Nodes);
            }
        }
        _tree.BeginUpdate();
        Walk(_tree.Nodes);
        int start = _tree.SelectedNode != null ? order.IndexOf(_tree.SelectedNode) + 1 : 0;
        TreeNode? hit = null;
        for (int k = 0; k < order.Count && hit == null; k++)
        {
            var n = order[(start + k) % order.Count];
            var i = (NodeInfo)n.Tag!;
            if ((i.Name ?? "").Contains(_lastFind, StringComparison.OrdinalIgnoreCase) || ValueText(i.Tag).Contains(_lastFind, StringComparison.OrdinalIgnoreCase)) hit = n;
        }
        _tree.EndUpdate();
        if (hit == null) { Dialogs.Info(FindForm()!, $"\"{_lastFind}\" was not found."); return; }
        hit.EnsureVisible();
        _tree.SelectedNode = hit;
        _tree.Focus();
    }

    ContextMenuStrip BuildContextMenu()
    {
        var m = new ContextMenuStrip { Renderer = new DarkRenderer() };
        ToolStripMenuItem Item(string text, string? keys, EventHandler click) => new(text, null, click) { ForeColor = C.FG, ShortcutKeyDisplayString = keys };
        var add = new ToolStripMenuItem("Add") { ForeColor = C.FG };
        foreach (NbtType t in Enum.GetValues<NbtType>())
        {
            if (t == NbtType.End) continue;
            var type = t;
            add.DropDownItems.Add(new ToolStripMenuItem(NbtTypeInfo.Name(t), _icons.Images[Icons.KeyFor(t)], (_, _) => AddChild(type)) { ForeColor = C.FG });
        }
        add.DropDown.BackColor = C.BG2;
        m.Items.AddRange(
        [
            Item("Edit value", "Enter", (_, _) => EditSelected()),
            Item("Edit as SNBT…", null, (_, _) => EditAsSnbt()),
            Item("Rename", "F2", (_, _) => RenameSelected()),
            add,
            Item("Duplicate", "Ctrl+D", (_, _) => DuplicateSelected()),
            Item("Delete", "Del", (_, _) => DeleteSelected()),
            new ToolStripSeparator(),
            Item("Copy as SNBT", "Ctrl+C", (_, _) => CopySelected()),
            Item("Copy path", null, (_, _) => { if (_tree.SelectedNode != null) Clipboard.SetText(PathOf(_tree.SelectedNode)); }),
            Item("Paste", "Ctrl+V", (_, _) => PasteIntoSelected()),
            new ToolStripSeparator(),
            Item("Move up", "Alt+Up", (_, _) => MoveSelected(-1)),
            Item("Move down", "Alt+Down", (_, _) => MoveSelected(1)),
            new ToolStripSeparator(),
            Item("Expand all", null, (_, _) => ExpandAll()),
        ]);
        return m;
    }

    void OnTreeKey(object? sender, KeyEventArgs e)
    {
        bool handled = true;
        switch (e.KeyData)
        {
            case Keys.Enter: EditSelected(); break;
            case Keys.F2: RenameSelected(); break;
            case Keys.Delete: DeleteSelected(); break;
            case Keys.Insert: if (Selected?.Tag is NbtList l && l.Count > 0) AddChild(l.ElementType); else handled = false; break;
            case Keys.Control | Keys.C: CopySelected(); break;
            case Keys.Control | Keys.V: PasteIntoSelected(); break;
            case Keys.Control | Keys.D: DuplicateSelected(); break;
            case Keys.Control | Keys.Z: Undo(); break;
            case Keys.Control | Keys.Y: Redo(); break;
            case Keys.Control | Keys.F: Find(false); break;
            case Keys.F3: Find(true); break;
            case Keys.Alt | Keys.Up: MoveSelected(-1); break;
            case Keys.Alt | Keys.Down: MoveSelected(1); break;
            default: handled = false; break;
        }
        if (handled) { e.Handled = true; e.SuppressKeyPress = true; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _icons.Dispose(); _mono.Dispose(); }
        base.Dispose(disposing);
    }
}
