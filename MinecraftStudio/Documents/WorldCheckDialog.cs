// MinecraftStudio/Documents/WorldCheckDialog.cs: "Check for problems" — what would stop a world opening in its game, crash
// it or kick its players, lose their things, or leave a player stuck.
// Owns: WorldCheckDialog. A window beside the world's tab (not modal, like Find and Mods) that reads a world of its own off
// disk (WorldCheck.Run: every chunk, how a Bedrock world's chunks are stored, lag machines and book or chunk bans, mods not
// on this PC, the packs it uses, the game version, every player), lists each problem the worst first with what fixes it,
// and "Fix…" opens the window that does it: Rescue (a repaired copy), Convert, Lag machines (Clean up), Move to safety,
// the Cheats page (Restore Hardcore world from death), the Packs page or Mods and add-ons. "Show on the map" marks the
// chunks; the report can be copied. Only reads: every fix writes a copy, or backs the world up first.
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class WorldCheckDialog : DarkDialog
{
    readonly WorldDocument _doc;
    readonly Button _check, _stop, _fix, _show, _copy;
    readonly ListView _list;
    readonly TextBox _details;
    readonly Label _summary, _status;
    CheckReport? _report;
    CancellationTokenSource? _cts;

    public WorldCheckDialog(WorldDocument doc) : base("Check for problems in " + doc.World.Name, 900)
    {
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(this.Px(900), this.Px(640));
        MinimumSize = new Size(this.Px(620), this.Px(420));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Looks for what would stop the world opening in its game, crash the game or throw its players out, lose things, or leave a player stuck: "
                + "every chunk, how the chunks are stored, lag machines and book or chunk bans, mods that aren't on this PC, the packs it uses, the game version and every player. "
                + "Each problem says what fixes it. The world is only read; every fix writes a new copy or backs the world up first.",
            AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _check = Theme.Button("Check the world", true);
        _check.MinimumSize = new Size(this.Px(140), this.Px(30));
        _check.Click += async (_, _) => await CheckAsync();
        _stop = Theme.Button("Stop");
        _stop.MinimumSize = new Size(this.Px(80), this.Px(30));
        _stop.Enabled = false;
        _stop.Click += (_, _) => _cts?.Cancel();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_check, _stop]);
        top.Controls.Add(row);

        _summary = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, this.Px(6)) };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Problems found",
        };
        _list.Columns.Add("How bad", this.Px(140));
        _list.Columns.Add("Problem", this.Px(600));
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.SelectedIndexChanged += (_, _) => { ShowDetails(); UpdateButtons(); };
        _list.DoubleClick += async (_, _) => await FixAsync();

        _details = new TextBox
        {
            Dock = DockStyle.Bottom, Height = this.Px(150), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
            BackColor = C.BG3, ForeColor = C.FG, BorderStyle = BorderStyle.None, Font = Theme.Ui(9f), AccessibleName = "What is wrong and how to fix it",
        };
        Theme.DarkControl(_details);
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _fix = Theme.Button("Fix…", true);
        _fix.Click += async (_, _) => await FixAsync();
        _show = Theme.Button("Show on the map");
        _show.Click += (_, _) => ShowOnMap();
        _copy = Theme.Button("Copy the report");
        _copy.Click += (_, _) => CopyReport();
        foreach (var b in new[] { _copy, _show, _fix }) { b.MinimumSize = new Size(this.Px(130), this.Px(30)); buttons.Controls.Add(b); }

        // Docked last first: the buttons and status at the bottom, the details above them, the controls on top, the list filling the rest.
        Controls.Add(_list);
        Controls.Add(_details);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);
        _status.Text = "Press Check the world: every chunk is read, which takes about a minute for a big world.";
        UpdateButtons();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Checking is what the window is for: it starts by itself.
        if (_report == null) _ = CheckAsync();
    }

    CheckProblem? Chosen => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as CheckProblem : null;

    void UpdateButtons()
    {
        bool idle = _cts == null;
        _check.Enabled = idle;
        _stop.Enabled = !idle;
        _copy.Enabled = idle && _report != null;
        var p = Chosen;
        _fix.Enabled = idle && p is { Fix: not CheckFix.None };
        _fix.Text = p?.Fix switch
        {
            CheckFix.Repair => "Rescue…",
            CheckFix.Convert => "Convert…",
            CheckFix.CleanUp => "Clean up…",
            CheckFix.MoveToSafety => "Move to safety…",
            CheckFix.RestoreHardcore => "Restore…",
            CheckFix.Packs => "Packs…",
            CheckFix.Mods => "Mods…",
            CheckFix.CloseGame => "What to do",
            _ => "Fix…",
        };
        _show.Enabled = idle && p is { DimensionId: not null, Chunks.Count: > 0 };
    }

    /// <summary>Checks a world of its own read off disk (the tab may be reading the same files), so the tab stays usable.</summary>
    internal async Task CheckAsync()
    {
        if (_cts != null) return;
        string location = _doc.World.Location;
        bool rescue = _doc.World.OpenedForRescue;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Reading the world…";
        var progress = new Progress<string>(s => { if (!IsDisposed) _status.Text = s; });
        try
        {
            var report = await Task.Run(() =>
            {
                using var own = rescue ? null : WorldLoader.Open(location);
                var w = own ?? _doc.World;
                ((IProgress<string>)progress).Report("Looking for the mods on this PC…");
                var installed = w is JavaWorld ? InstalledMods.Find(WorldMods.NearbyGameFolders(w), token) : [];
                return WorldCheck.Run(w, installed, progress, token, shared: own == null);
            }, token);
            if (IsDisposed) return;
            _report = report;
            Fill();
            _status.ForeColor = report.Serious ? C.ORANGE : C.FG_DIM;
            _status.Text = $"Read {report.ChunksRead:N0} chunks in {report.Elapsed.TotalSeconds:N0} s. " + (report.Problems.Count == 0 ? "Nothing found." : "Choose a problem to read what fixes it.");
        }
        catch (OperationCanceledException) { if (!IsDisposed) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped."; } }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (IsDisposed) return;
            _status.ForeColor = C.ORANGE;
            _status.Text = "The world could not be read: " + ex.Message + " (Rescue a damaged world reads what still can be.)";
        }
        finally { _cts?.Dispose(); _cts = null; if (!IsDisposed) UpdateButtons(); }
    }

    internal CheckReport? Report => _report;

    void Fill()
    {
        if (_report is not { } r) return;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in r.Problems)
        {
            var item = new ListViewItem([CheckReport.SeverityText(p.Severity), p.Title]) { Tag = p, UseItemStyleForSubItems = true };
            item.ForeColor = p.Severity switch
            {
                CheckSeverity.WontOpen or CheckSeverity.Crashes => C.RED,
                CheckSeverity.LosesThings or CheckSeverity.Stuck => C.ORANGE,
                _ => C.YELLOW,
            };
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        int serious = r.Problems.Count(p => p.Severity != CheckSeverity.Warning);
        _summary.Text = r.Problems.Count == 0
            ? "Nothing found that stops the world opening, crashes it, loses things or leaves a player stuck.  Looked at: " + string.Join("  ", r.Checked)
            : $"{r.Problems.Count} problem(s): {serious} serious, {r.Problems.Count - serious} worth a look.";
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        ShowDetails();
    }

    void ShowDetails()
    {
        if (Chosen is not { } p)
        {
            _details.Text = _report == null ? "" : "Looked at:\r\n" + string.Join("\r\n", _report.Checked.Select(c => "• " + c));
            return;
        }
        _details.Text = $"{p.Title}\r\n\r\n{p.Detail}\r\n\r\nWhat fixes it: {CheckReport.FixText(p.Fix)}";
        _details.Select(0, 0);
    }

    async Task FixAsync()
    {
        if (Chosen is not { Fix: not CheckFix.None } p || _cts != null) return;
        switch (p.Fix)
        {
            case CheckFix.Repair: await _doc.Main.ShowRescue(_doc.World.Location); break;
            case CheckFix.Convert: await _doc.Main.ShowConvert(_doc.World); break;
            case CheckFix.CleanUp: _doc.ShowLagMachines(); break;
            case CheckFix.MoveToSafety: _doc.MovePlayerToSafety(p.Player); break;
            case CheckFix.RestoreHardcore:
                _doc.ShowSection(WorldSection.Cheats);
                _status.ForeColor = C.FG_DIM;
                _status.Text = "On the Cheats page: press Restore Hardcore world from death, then Save changes.";
                break;
            case CheckFix.Packs: _doc.ShowSection(WorldSection.Packs); break;
            case CheckFix.Mods: _doc.ShowMods(); break;
            case CheckFix.CloseGame: Dialogs.Info(this, WorldUse.OpenInGame); break;
        }
    }

    const int MostMarked = 4000;

    void ShowOnMap()
    {
        if (Chosen is not { DimensionId: string dim, Chunks: { Count: > 0 } chunks } p) return;
        var hits = chunks.Take(MostMarked).Select(c => new SearchHit(SearchKind.Block, p.Title, c.X * 16 + 8, 64, c.Z * 16 + 8, $"chunk {c.X}, {c.Z}", 1)).ToList();
        _doc.ShowFound(dim, hits, hits[0]);
        _status.ForeColor = C.FG_DIM;
        _status.Text = $"Marked {hits.Count:N0} chunk{(hits.Count == 1 ? "" : "s")} on the map" + (chunks.Count > MostMarked ? $" (the first {MostMarked:N0} of {chunks.Count:N0})." : ".");
    }

    void CopyReport()
    {
        if (_report == null) return;
        try { Clipboard.SetText(_report.Text()); _status.ForeColor = C.FG_DIM; _status.Text = "Copied the report."; }
        catch (System.Runtime.InteropServices.ExternalException) { _status.Text = "The clipboard is busy; try again."; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
