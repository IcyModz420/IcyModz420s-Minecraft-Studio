// MinecraftStudio/Documents/ConvertDialog.Running.cs: running for ConvertDialog.
// Part of ConvertDialog (main part in ConvertDialog.cs).
using System.Diagnostics;
using System.IO.Compression;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

partial class ConvertDialog
{
    // ------------------------------------------------------------------ running

    void UpdateState()
    {
        _convert.Enabled = !_running && _source != null && Target != null;
        foreach (var c in _inputs) c.Enabled = !_running;
        if (!_running) UpdateDimensions();
        _radius.Enabled = !_running && _limit.Checked;
        _close.Text = _running ? "Cancel" : "Close";
    }

    ConversionOptions BuildOptions()
    {
        var dims = _dimRow.Controls.OfType<DarkCheckBox>().ToList();
        var options = new ConversionOptions
        {
            ConvertEntities = _entities.Checked,
            ConvertBlockEntities = _blockEntities.Checked,
            ConvertPlayers = _players.Checked,
            LetGameExtendOldTerrain = _extend.Checked,
            VerticalShift = _depth.Visible && _depth.Checked ? 64 : 0,
            Dimensions = dims.All(c => c.Checked) ? null : dims.Where(c => c.Checked).Select(c => (string)c.Tag!).ToHashSet(),
            WorldName = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : null,
        };
        if ((_consoleSize.SelectedItem as ComboItem)?.Tag is int size) options.ConsoleWorldChunks = size;
        if (_blockRules.Count > 0) options.BlockRules = _blockRules;
        if (_templateRow.Visible && _template.Text.Trim().Length > 0) options.ConsoleTemplatePackage = _template.Text.Trim();
        return options;
    }

    /// <summary>The block rules in one line beside their button.</summary>
    void ShowRulesInfo()
    {
        _rulesInfo.Text = _blockRules.Count == 0 ? "No block is changed: blocks the target lacks become the closest it has."
            : string.Join(", ", _blockRules.Take(3).Select(r => $"{r.Pattern} → {r.With}")) + (_blockRules.Count > 3 ? $" and {_blockRules.Count - 3} more" : "");
    }

    /// <summary>The block rules edited in a window of their own (an unknown block is refused there), kept for next time.</summary>
    void EditBlockRules()
    {
        using var dlg = new BlockRulesDialog(_blockRules);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _blockRules = dlg.Rules;
        Backup.Settings.ConvertBlockRules = _blockRules.Select(r => $"{r.Pattern} → {r.With}").ToList();
        ShowRulesInfo();
    }

    string? Validate(ConversionTarget target, string output, ConversionOptions options)
    {
        if (_source == null) return "Choose a world to convert first.";
        if (output.Length == 0) return "Choose a folder for the converted world.";
        try { output = Path.GetFullPath(output); }
        catch { return "The folder path is not valid."; }
        if (File.Exists(output)) return "A file with that name already exists; choose another folder.";
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) return "That folder is not empty; choose a new folder name.";
        string source = Path.GetFullPath(_source.Location).TrimEnd('\\');
        if (output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(output, source, StringComparison.OrdinalIgnoreCase))
            return "The converted world cannot go inside the world being converted.";
        if (options.Dimensions is { Count: 0 }) return "Choose at least one dimension.";
        if (_limit.Visible && _limit.Checked && (!int.TryParse(_radius.Text.Trim(), out int r) || r < 1)) return "Enter how many chunks around the spawn to convert.";
        if (options.ConsoleTemplatePackage is string pkg && !File.Exists(pkg)) return "The Xbox 360 package file does not exist.";
        return null;
    }

    async Task StartAsync()
    {
        var target = Target;
        if (target == null || _source == null) return;
        string output = _folder.Text.Trim();
        var options = BuildOptions();
        if (Validate(target, output, options) is string problem) { ShowMessage(problem, C.ORANGE); return; }
        output = Path.GetFullPath(output);
        int? radius = _limit.Visible && _limit.Checked && int.TryParse(_radius.Text.Trim(), out int r) ? r : null;
        bool makeMcworld = target.Kind == TargetKind.Bedrock && _mcworld.Checked;
        bool existed = Directory.Exists(output);
        string location = _source.Location;
        bool unsaved = _source.IsModified;
        // A rescued world is converted as it was read: it is open nowhere else, and opening it again would repeat the search.
        var rescued = _source.OpenedForRescue ? _source : null;

        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _report = null;
        _mcworldPath = null;
        _open.Visible = _explore.Visible = _reportButton.Visible = _notes.Visible = false;
        _convert.Text = "Convert";
        _bar.Visible = true;
        _bar.BarColor = C.ACCENT2;
        _bar.Value = 0;
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Opening the source world…";
        UpdateState();
        var clock = Stopwatch.StartNew();
        var progress = new Progress<ConversionProgress>(p => ShowProgress(p, clock.Elapsed));
        try
        {
            var report = await Task.Run(() =>
            {
                // A separate copy of the world: the map tab may be reading the same files at the same time.
                using var copy = rescued == null ? WorldLoader.Open(location) : null;
                var world = copy ?? rescued!;
                if (radius is int rr)
                {
                    var info = WorldInfoReader.Read(world);
                    int cx = info.SpawnX >> 4, cz = info.SpawnZ >> 4;
                    options.Area = (cx - rr, cz - rr, cx + rr, cz + rr);
                }
                var result = WorldConverter.Run(world, target, output, options, progress, token);
                if (unsaved) result.Notes.Insert(0, "The world had unsaved changes in the studio; the copy saved on disk was converted.");
                if (makeMcworld)
                {
                    ((IProgress<ConversionProgress>)progress).Report(new ConversionProgress { Stage = "Packing the .mcworld file", Done = 1, Total = 1 });
                    Directory.CreateDirectory(Documents);
                    string file = Path.Combine(Documents, SafeName(options.WorldName ?? world.Name) + ".mcworld");
                    for (int i = 2; File.Exists(file); i++) file = Path.Combine(Documents, $"{SafeName(options.WorldName ?? world.Name)} ({i}).mcworld");
                    ZipFile.CreateFromDirectory(output, file, CompressionLevel.Optimal, includeBaseDirectory: false);
                    _mcworldPath = file;
                    result.Notes.Add("Saved " + file);
                }
                return result;
            }, token);
            _report = report;
            _reportTarget = target;
            _reportOutput = output;
            ShowResult(report, target);
        }
        catch (OperationCanceledException)
        {
            RemoveOutput(output, existed);
            ShowMessage("Cancelled. The unfinished output was removed.", C.YELLOW);
            _bar.Visible = false;
        }
        catch (Exception ex)
        {
            RemoveOutput(output, existed);
            _bar.BarColor = C.RED;
            ShowMessage("Conversion failed: " + ex.Message, C.RED);
            Log.Error("Conversion failed", ex);
        }
        finally
        {
            _running = false;
            _cts.Dispose();
            _cts = null;
            UpdateState();
            if (_closeWhenDone) Close();
        }
    }

    void ShowProgress(ConversionProgress p, TimeSpan elapsed)
    {
        if (!_running) return;
        double fraction = p.Total > 0 ? p.Done / (double)p.Total : 0;
        // Checking stages without a count animate; counted ones show how far the comparison is.
        _bar.Marquee = p.Stage.StartsWith("Checking", StringComparison.Ordinal) && p.Total <= 1;
        _bar.Value = fraction;
        string text = p.Stage;
        if (p.Total > 1 && p.Done < p.Total)
        {
            text += $"  ·  {p.Done:N0} of {p.Total:N0} chunks  ·  {Duration(elapsed)}";
            if (p.Done > 64 && elapsed.TotalSeconds > 3)
                text += $"  ·  about {Duration(TimeSpan.FromSeconds(elapsed.TotalSeconds * (p.Total - p.Done) / p.Done))} left";
        }
        _status.ForeColor = C.FG_DIM;
        _status.Text = text;
    }

    internal static string Duration(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{Math.Max(1, (int)t.TotalSeconds)} s";

    void ShowResult(ConversionReport report, ConversionTarget target)
    {
        _bar.Marquee = false;
        _bar.Value = 1;
        var failed = report.Checks.Where(c => !c.Passed).ToList();
        var lost = report.Losses.Where(l => l.Lost).ToList();
        var changed = report.Losses.Where(l => !l.Lost && l.Kind != LossKind.World).ToList();
        string done = $"{report.Chunks:N0} chunks to {target.Name} in {Duration(report.Elapsed)}";
        if (failed.Count > 0)
        {
            _bar.BarColor = C.RED;
            _status.ForeColor = C.RED;
            _status.Text = $"✗  Converted {done}, but the check found a problem: {failed[0].Name}.";
        }
        else if (lost.Count > 0)
        {
            _bar.BarColor = C.ORANGE;
            _status.ForeColor = C.ORANGE;
            _status.Text = $"⚠  Converted and checked {done}. {lost.Count} thing{(lost.Count == 1 ? "" : "s")} could not be carried over — listed below.";
        }
        else
        {
            _bar.BarColor = C.ACCENT2;
            _status.ForeColor = C.GREEN;
            _status.Text = $"✓  Converted and checked {done}." + (changed.Count > 0 ? " Nothing was lost; what changed to fit is listed below." : " Nothing was lost.");
        }
        _notes.Text = ResultText(report);
        _notes.Visible = true;
        _notes.SelectionStart = 0;
        _notes.SelectionLength = 0;
        _open.Visible = _explore.Visible = _reportButton.Visible = true;
        _convert.Text = "Convert again";
        _convert.BackColor = C.BG4;
        _convert.FlatAppearance.MouseOverBackColor = C.BORDER;
        AcceptButton = _open;
        _open.Focus();
        // The next conversion goes to a fresh folder.
        _folderEdited = false;
        SuggestFolder();
        // A world that failed its check must not be missed.
        if (failed.Count > 0)
            BeginInvoke(() => Dialogs.ShowText(this, "The converted world did not pass its check", ReportText()));
    }

    /// <summary>The result summary shown under the progress bar: checks, losses, changes, rescue chests, notes.</summary>
    static string ResultText(ConversionReport report)
    {
        var lines = new List<string>();
        var failed = report.Checks.Where(c => !c.Passed).ToList();
        if (report.Checks.Count > 0)
            lines.Add(failed.Count == 0
                ? $"CHECKED  The converted world opens and passed all {report.Checks.Count} checks ({string.Join(", ", report.Checks.Select(c => c.Name.ToLowerInvariant()))})."
                : "CHECK FAILED  " + string.Join("; ", failed.Select(c => $"{c.Name}: {c.Detail}")));
        var lost = report.Losses.Where(l => l.Lost).ToList();
        if (lost.Count > 0)
        {
            lines.Add("");
            lines.Add("NOT CARRIED OVER");
            lines.AddRange(lost.Take(40).Select(l => "  ✗ " + l));
            if (lost.Count > 40) lines.Add($"  … and {lost.Count - 40} more in the full report");
        }
        if (report.RescueChests.Count > 0)
        {
            lines.Add("");
            var first = report.RescueChests[0];
            lines.Add($"RESCUE CHESTS  near the spawn at {first.X}, {first.Y}, {first.Z}: " + string.Join(", ", report.RescueChests.Select(r => r.Label).Distinct()));
        }
        var changed = report.Losses.Where(l => !l.Lost).ToList();
        if (changed.Count > 0)
        {
            lines.Add("");
            lines.Add("CHANGED TO FIT");
            lines.AddRange(changed.Take(40).Select(l => "  • " + l));
            if (changed.Count > 40) lines.Add($"  … and {changed.Count - 40} more in the full report");
        }
        if (report.Issues.Count > 0)
        {
            lines.Add("");
            lines.Add("WHAT THE CHECK SAW");
            lines.AddRange(report.Issues.Take(20).Select(i => "  • " + i));
        }
        if (report.Notes.Count > 0)
        {
            lines.Add("");
            lines.Add("NOTES");
            lines.AddRange(report.Notes.Select(n => "  • " + n));
        }
        if (report.Warnings.Count > 0) lines.Add($"  • {report.Warnings.Count} warning{(report.Warnings.Count == 1 ? "" : "s")} while reading the source — see the full report.");
        return string.Join("\r\n", lines);
    }

    string IdleHint => _source?.OpenedForRescue == true
        ? "The damaged world is never changed: what could be read is written as a new world to the folder above."
        : "Your original world is never changed: the converted copy is written to the folder above.";

    void ShowMessage(string text, Color color)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }

    static void RemoveOutput(string output, bool existed)
    {
        try
        {
            if (!Directory.Exists(output)) return;
            if (existed)
            {
                foreach (var f in Directory.EnumerateFiles(output)) File.Delete(f);
                foreach (var d in Directory.EnumerateDirectories(output)) Directory.Delete(d, true);
            }
            else Directory.Delete(output, true);
        }
        catch (Exception ex) { Log.Error("Could not remove the unfinished conversion", ex); }
    }

    string ReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Source:  {_source?.Name} — {_source?.FormatDescription}");
        sb.AppendLine($"         {_source?.Location}");
        sb.AppendLine($"Target:  {_reportTarget?.Name}");
        sb.AppendLine($"Output:  {_reportOutput}");
        if (_mcworldPath != null) sb.AppendLine($"         {_mcworldPath}");
        if (_source?.OpenedForRescue == true && _rescueFindings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("WHAT THE RESCUE FOUND IN THE DAMAGED WORLD");
            foreach (var f in _rescueFindings) sb.AppendLine("  • " + f);
        }
        if (_report != null)
        {
            sb.AppendLine($"Chunks:  {_report.Chunks:N0} converted, {_report.FailedChunks:N0} damaged in {Duration(_report.Elapsed)}");
            sb.AppendLine();
            sb.AppendLine("CHECKS OF THE CONVERTED WORLD");
            foreach (var c in _report.Checks) sb.AppendLine($"  {(c.Passed ? "✓" : "✗")} {c.Name}: {c.Detail}");
            sb.AppendLine();
            var lost = _report.Losses.Where(l => l.Lost).ToList();
            sb.AppendLine(lost.Count == 0 ? "NOTHING WAS LOST" : "NOT CARRIED OVER");
            foreach (var l in lost) sb.AppendLine($"  ✗ [{l.Kind}] {l}");
            sb.AppendLine();
            foreach (var r in _report.RescueChests) sb.AppendLine($"Rescue chest \"{r.Label}\" at {r.X}, {r.Y}, {r.Z}");
            if (_report.RescueChests.Count > 0) sb.AppendLine();
            var changed = _report.Losses.Where(l => !l.Lost).ToList();
            if (changed.Count > 0) { sb.AppendLine("CHANGED TO FIT"); foreach (var l in changed) sb.AppendLine($"  • [{l.Kind}] {l}"); sb.AppendLine(); }
            if (_report.Issues.Count > 0) { sb.AppendLine("WHAT THE CHECK SAW"); foreach (var i in _report.Issues) sb.AppendLine("  • " + i); sb.AppendLine(); }
            if (_report.Notes.Count > 0) { sb.AppendLine("NOTES"); foreach (var n in _report.Notes) sb.AppendLine("  " + n); sb.AppendLine(); }
            if (_report.Warnings.Count > 0) { sb.AppendLine("WARNINGS"); foreach (var w in _report.Warnings) sb.AppendLine("  " + w); }
        }
        return sb.ToString();
    }

    void RequestCancel()
    {
        if (_cts == null) return;
        _status.ForeColor = C.YELLOW;
        _status.Text = "Stopping…";
        _cts.Cancel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            if (Dialogs.Ask(this, "Stop the conversion? The unfinished output is removed.") == DialogResult.Yes)
            {
                _closeWhenDone = true;
                RequestCancel();
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (_running) RequestCancel(); else Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icons.Dispose();
        base.Dispose(disposing);
    }
}
