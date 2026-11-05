// MinecraftStudio/UI/NbtEditor.Tree.cs: tree for NbtEditor.
// Part of NbtEditor (main part in NbtEditor.cs).
using System.Globalization;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.UI;

partial class NbtEditor
{
    // ------------------------------------------------------------------ tree building

    void Rebuild(List<string>? expandedPaths)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        foreach (var r in _roots)
        {
            var n = MakeNode(new NodeInfo { Tag = r.Tag, Root = r, Name = r.Name });
            _tree.Nodes.Add(n);
        }
        if (_tree.Nodes.Count == 1) _tree.Nodes[0].Expand();
        if (expandedPaths != null)
            foreach (var p in expandedPaths) ExpandPath(p);
        _tree.EndUpdate();
        UpdateStatus();
    }

    TreeNode MakeNode(NodeInfo info)
    {
        var n = new TreeNode { Tag = info };
        string key = Icons.KeyFor(info.Tag.Type);
        n.ImageKey = n.SelectedImageKey = key;
        n.Text = Label(info);
        if (HasChildTags(info.Tag)) n.Nodes.Add(new TreeNode("…"));
        return n;
    }

    static bool HasChildTags(NbtTag t) => t is NbtCompound { Count: > 0 } || t is NbtList { Count: > 0 };

    void EnsureLoaded(TreeNode node)
    {
        if (node.Tag is not NodeInfo info || info.Loaded) return;
        info.Loaded = true;
        _tree.BeginUpdate();
        node.Nodes.Clear();
        if (info.Tag is NbtCompound c)
        {
            int i = 0;
            foreach (var kv in c)
            {
                node.Nodes.Add(MakeNode(new NodeInfo { Tag = kv.Value, Parent = c, Name = kv.Key }));
                if (++i >= 5000 && c.Count > 5050) { node.Nodes.Add(new TreeNode($"… {c.Count - i:N0} more entries (edit as SNBT to see all)")); break; }
            }
        }
        else if (info.Tag is NbtList l) AddListPage(node, l, 0);
        _tree.EndUpdate();
    }

    void AddListPage(TreeNode node, NbtList list, int start)
    {
        int end = Math.Min(list.Count, start + PageSize);
        for (int i = start; i < end; i++)
            node.Nodes.Add(MakeNode(new NodeInfo { Tag = list[i], Parent = list, ListIndex = i }));
        if (end < list.Count)
            node.Nodes.Add(new TreeNode($"… show {Math.Min(PageSize, list.Count - end):N0} more of {list.Count - end:N0} (double-click)") { Tag = new MoreInfo { List = list, Start = end }, ForeColor = C.ACCENT2 });
    }

    void LoadMore(TreeNode moreNode)
    {
        var m = (MoreInfo)moreNode.Tag!;
        var parent = moreNode.Parent;
        _tree.BeginUpdate();
        moreNode.Remove();
        AddListPage(parent!, m.List, m.Start);
        _tree.EndUpdate();
    }

    static string NameOf(NodeInfo i) => i.ListIndex >= 0 ? $"[{i.ListIndex}]" : string.IsNullOrEmpty(i.Name) ? (i.Root != null ? "(root)" : "\"\"") : i.Name;

    static string Label(NodeInfo i) => NameOf(i) + ": " + ValueText(i.Tag);

    public static string ValueText(NbtTag t)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (t)
        {
            case NbtByte b: return b.Value.ToString(inv);
            case NbtShort s: return s.Value.ToString(inv);
            case NbtInt i: return i.Value.ToString(inv);
            case NbtLong l: return l.Value.ToString(inv);
            case NbtFloat f: return f.Value.ToString("R", inv);
            case NbtDouble d: return d.Value.ToString("R", inv);
            case NbtString s:
                {
                    string q = Snbt.Quote(s.Value);
                    return q.Length > 240 ? q[..240] + "…\"" : q;
                }
            case NbtByteArray ba: return $"[{ba.Value.Length:N0} bytes] {Preview(ba.Value.Take(12).Select(v => ((sbyte)v).ToString(inv)), ba.Value.Length)}";
            case NbtIntArray ia: return $"[{ia.Value.Length:N0} ints] {Preview(ia.Value.Take(8).Select(v => v.ToString(inv)), ia.Value.Length)}";
            case NbtLongArray la: return $"[{la.Value.Length:N0} longs] {Preview(la.Value.Take(4).Select(v => v.ToString(inv)), la.Value.Length)}";
            case NbtList l: return l.Count == 0 ? "[empty list]" : $"[{l.Count:N0} × {NbtTypeInfo.Name(l.ElementType)}]";
            case NbtCompound c: return c.Count == 0 ? "{ }" : $"{{{c.Count:N0} entries}}";
            default: return "";
        }
    }

    static string Preview(IEnumerable<string> items, int total)
    {
        var list = items.ToList();
        return list.Count == 0 ? "" : string.Join(", ", list) + (total > list.Count ? ", …" : "");
    }

    Color ValueColor(NbtTag t) => t switch
    {
        NbtString => C.STRING,
        NbtCompound or NbtList => C.FG_DIM,
        NbtByteArray or NbtIntArray or NbtLongArray => C.PURPLE,
        _ => C.NUMBER,
    };

    void DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        var node = e.Node!;
        var bounds = e.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        bool selected = (e.State & TreeNodeStates.Selected) != 0;
        var back = selected ? (_tree.Focused ? C.SELECTION : C.BG4) : C.BG;
        int width = Math.Max(bounds.Width, _tree.ClientSize.Width - bounds.Left);
        using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, bounds.Left, bounds.Top, width, bounds.Height);
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        if (node.Tag is not NodeInfo info)
        {
            TextRenderer.DrawText(e.Graphics, node.Text, _mono, new Rectangle(bounds.Left + 2, bounds.Top, width, bounds.Height), node.ForeColor.IsEmpty ? C.FG_DIM : node.ForeColor, flags);
            return;
        }
        string name = NameOf(info) + ": ";
        string value = ValueText(info.Tag);
        int x = bounds.Left + 2;
        var nameColor = info.ListIndex >= 0 ? C.FG_DIM : C.KEY;
        var size = TextRenderer.MeasureText(e.Graphics, name, _mono, new Size(int.MaxValue, bounds.Height), flags);
        TextRenderer.DrawText(e.Graphics, name, _mono, new Rectangle(x, bounds.Top, size.Width, bounds.Height), nameColor, flags);
        TextRenderer.DrawText(e.Graphics, value, _mono, new Rectangle(x + size.Width, bounds.Top, width, bounds.Height), ValueColor(info.Tag), flags);
    }
}
