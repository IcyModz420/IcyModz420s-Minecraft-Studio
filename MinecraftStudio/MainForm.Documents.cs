// MinecraftStudio/MainForm.Documents.cs: documents for MainForm.
// Part of MainForm (main part in MainForm.cs).
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.Phone;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio;

partial class MainForm
{
    // ------------------------------------------------------------------ documents

    public void OpenDocument(Document doc)
    {
        if (doc.Key != null)
        {
            var existing = _docs.FirstOrDefault(d => Equals(d.Key, doc.Key));
            if (existing != null) { doc.Dispose(); Select(existing); return; }
        }
        doc.Main = this;
        doc.Visible = false;
        _docs.Add(doc);
        _host.Controls.Add(doc);
        doc.StateChanged += (_, _) => UpdateTab(doc);
        _tabs.Visible = true;
        _tabs.Add(new TabStrip.Tab { Text = doc.Title, ToolTip = doc.ToolTipText, Tag = doc });
        UpdateTab(doc);
    }

    void Select(Document doc)
    {
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Tag == doc);
        if (tab != null) _tabs.Selected = tab;
    }

    void UpdateTab(Document doc)
    {
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Tag == doc);
        if (tab == null) return;
        tab.Text = doc.Title;
        tab.Dirty = doc.IsDirty;
        tab.ToolTip = doc.ToolTipText;
        _tabs.Invalidate();
        UpdateTitle();
    }

    void ShowSelectedDocument()
    {
        var doc = _tabs.Selected?.Tag as Document;
        foreach (var d in _docs) d.Visible = d == doc;
        _welcome.Visible = doc == null;
        SetProgress(null);
        if (doc != null) { doc.BringToFront(); doc.OnActivated(); }
        UpdateTitle();
    }

    Document? Active => _tabs.Selected?.Tag as Document;

    void CloseActive() { if (Active is Document d) CloseDocument(d); }

    void CloseDocument(Document doc)
    {
        if (!doc.ConfirmClose()) return;
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Tag == doc);
        if (tab != null) _tabs.Remove(tab);
        _docs.Remove(doc);
        _host.Controls.Remove(doc);
        doc.Dispose();
        _tabs.Visible = _tabs.Tabs.Count > 0;
        ShowSelectedDocument();
    }

    void CloseWorld(SaveWorld? w)
    {
        if (w == null) return;
        foreach (var d in _docs.Where(d => d is WorldDocument wd && wd.World == w || d.Key is ValueTuple<SaveWorld, string> k && k.Item1 == w).ToList()) CloseDocument(d);
        if (_docs.OfType<WorldDocument>().Any(d => d.World == w)) return;
        _openWorlds.Remove(w);
        w.Dispose();
        PopulateTree();
    }

    async Task SaveActive()
    {
        if (Active is not Document d) return;
        if (!d.CanSave) { SetStatus("This tab cannot be saved."); return; }
        try { await d.SaveAsync(); }
        catch (Exception ex) { Dialogs.Error(this, "Save failed:\n\n" + ex.Message); }
        UpdateTab(d);
    }

    void UpdateTitle()
    {
        Text = Active != null ? $"{Active.Title}{(Active.IsDirty ? " ●" : "")} — {Core.AppInfo.WindowTitle}" : Core.AppInfo.WindowTitle;
        _statusRight.Text = _openWorlds.Count > 0 ? $"{_openWorlds.Count} world(s) open" : "";
    }
}
