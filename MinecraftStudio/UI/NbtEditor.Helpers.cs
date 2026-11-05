// MinecraftStudio/UI/NbtEditor.Helpers.cs: helpers for NbtEditor.
// Part of NbtEditor (main part in NbtEditor.cs).
using System.Globalization;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.UI;

partial class NbtEditor
{
    // ------------------------------------------------------------------ helpers

    NodeInfo? Selected => _tree.SelectedNode?.Tag as NodeInfo;

    void UpdateStatus()
    {
        var s = Selected;
        _status.Text = s == null ? "" : $"{NbtTypeInfo.Name(s.Tag.Type)}  ·  {PathOf(_tree.SelectedNode!)}";
    }

    static string PathOf(TreeNode node)
    {
        var parts = new List<string>();
        for (var n = node; n != null; n = n.Parent)
            if (n.Tag is NodeInfo i && i.Root == null) parts.Add(i.ListIndex >= 0 ? $"[{i.ListIndex}]" : i.Name ?? "");
        parts.Reverse();
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts)
        {
            if (p.StartsWith('[')) sb.Append(p);
            else { if (sb.Length > 0) sb.Append('.'); sb.Append(Snbt.QuoteKey(p)); }
        }
        return sb.ToString();
    }

    List<string> ExpandedPaths()
    {
        var list = new List<string>();
        void Walk(TreeNodeCollection nodes, string prefix)
        {
            foreach (TreeNode n in nodes)
            {
                if (!n.IsExpanded) continue;
                string p = prefix + "/" + n.Index;
                list.Add(p);
                Walk(n.Nodes, p);
            }
        }
        Walk(_tree.Nodes, "");
        return list;
    }

    void ExpandPath(string path)
    {
        TreeNodeCollection nodes = _tree.Nodes;
        TreeNode? n = null;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out int i) || i >= nodes.Count) return;
            n = nodes[i];
            EnsureLoaded(n);
            n.Expand();
            nodes = n.Nodes;
        }
    }

    List<NamedTag> Snapshot() => _roots.Select(r => new NamedTag(r.Name, r.Tag.Clone())).ToList();

    void BeginEdit()
    {
        _undo.Push(Snapshot());
        if (_undo.Count > 100)
        {
            var arr = _undo.ToArray();
            _undo.Clear();
            foreach (var s in arr.Take(100).Reverse()) _undo.Push(s);
        }
        _redo.Clear();
    }

    void Committed(TreeNode? select = null)
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        if (select != null) _tree.SelectedNode = select;
        UpdateStatus();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var paths = ExpandedPaths();
        _redo.Push(Snapshot());
        Restore(_undo.Pop());
        Rebuild(paths);
        Committed();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var paths = ExpandedPaths();
        _undo.Push(Snapshot());
        Restore(_redo.Pop());
        Rebuild(paths);
        Committed();
    }

    void Restore(List<NamedTag> snapshot)
    {
        for (int i = 0; i < _roots.Count && i < snapshot.Count; i++)
        {
            _roots[i].Name = snapshot[i].Name;
            _roots[i].Tag = snapshot[i].Tag;
        }
    }

    /// <summary>Refreshes a node after its tag changed (text and, for containers, children).</summary>
    void Refresh(TreeNode node)
    {
        if (node.Tag is not NodeInfo info) return;
        node.Text = Label(info);
        node.ImageKey = node.SelectedImageKey = Icons.KeyFor(info.Tag.Type);
        bool wasExpanded = node.IsExpanded;
        info.Loaded = false;
        node.Nodes.Clear();
        if (HasChildTags(info.Tag))
        {
            node.Nodes.Add(new TreeNode("…"));
            if (wasExpanded) { EnsureLoaded(node); node.Expand(); }
        }
    }

    /// <summary>Replaces the tag held by a node in its parent (compound key, list slot or root).</summary>
    void Replace(TreeNode node, NbtTag newTag)
    {
        var info = (NodeInfo)node.Tag!;
        if (info.Root != null) info.Root.Tag = newTag;
        else if (info.Parent is NbtCompound c) c[info.Name!] = newTag;
        else if (info.Parent is NbtList l) l[info.ListIndex] = newTag;
        info.Tag = newTag;
        Refresh(node);
        if (node.Parent != null) node.Parent.Text = Label((NodeInfo)node.Parent.Tag!);
    }
}
