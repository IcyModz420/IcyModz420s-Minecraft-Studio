// MinecraftStudio/Documents/RescueDialog.Repair.cs: repair for RescueDialog.
// Part of RescueDialog (main part in RescueDialog.cs).
using System.Diagnostics;
using System.IO.Compression;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

partial class RescueDialog
{
    // ------------------------------------------------------------------ writing the repaired copy

    async Task RepairAsync()
    {
        if (_world == null || _running) return;
        string output = _folder.Text.Trim();
        if (Validate(output) is string problem) { ShowMessage(problem, C.ORANGE); return; }
        output = Path.GetFullPath(output);
        string name = _worldName.Text.Trim().Length > 0 ? _worldName.Text.Trim() : _world.Name;
        bool makeMcworld = _world is BedrockWorld && _mcworld.Checked;
        bool existed = Directory.Exists(output);
        var world = _world;

        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _report = null;
        _mcworldPath = null;
        _open.Visible = _explore.Visible = _reportButton.Visible = false;
        _bar.Visible = true;
        _bar.BarColor = C.ACCENT2;
        _bar.Value = 0;
        ShowMessage("Writing the repaired copy…", C.FG_DIM);
        UpdateState();
        var progress = new Progress<ConversionProgress>(ShowProgress);
        try
        {
            var report = await Task.Run(() =>
            {
                var result = WorldRepair.Write(world, output, name, progress, token);
                if (makeMcworld)
                {
                    ((IProgress<ConversionProgress>)progress).Report(new ConversionProgress { Stage = "Packing the .mcworld file", Done = 1, Total = 1 });
                    Directory.CreateDirectory(RescuedFolder);
                    string file = Path.Combine(RescuedFolder, ConvertDialog.SafeName(name) + ".mcworld");
                    for (int i = 2; File.Exists(file); i++) file = Path.Combine(RescuedFolder, $"{ConvertDialog.SafeName(name)} ({i}).mcworld");
                    ZipFile.CreateFromDirectory(output, file, CompressionLevel.Optimal, includeBaseDirectory: false);
                    _mcworldPath = file;
                    result.Notes.Add("Saved " + file);
                }
                return result;
            }, token);
            _report = report;
            _output = output;
            ShowResult(report);
        }
        catch (OperationCanceledException)
        {
            RemoveOutput(output, existed);
            _bar.Visible = false;
            ShowMessage("Stopped. The unfinished copy was removed.", C.YELLOW);
        }
        catch (Exception ex)
        {
            RemoveOutput(output, existed);
            _bar.BarColor = C.RED;
            ShowMessage("The repaired copy could not be written: " + ex.Message, C.RED);
            Log.Error("Repaired copy failed", ex);
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

    void ShowProgress(ConversionProgress p)
    {
        if (!_running) return;
        _bar.Marquee = p.Total <= 1 && p.Stage.StartsWith("Checking", StringComparison.Ordinal);
        _bar.Value = p.Total > 0 ? p.Done / (double)p.Total : 0;
        _status.ForeColor = C.FG_DIM;
        _status.Text = p.Total > 1 ? $"{p.Stage}  ·  {p.Done:N0} of {p.Total:N0}" : p.Stage;
    }

    void ShowResult(ConversionReport report)
    {
        _bar.Marquee = false;
        _bar.Value = 1;
        var failed = report.Checks.Where(c => !c.Passed).ToList();
        var lost = report.Losses.Where(l => l.Lost).ToList();
        string done = $"{report.Chunks:N0} chunks in {ConvertDialog.Duration(report.Elapsed)}";
        if (failed.Count > 0)
        {
            _bar.BarColor = C.RED;
            ShowMessage($"✗  The repaired copy was written ({done}), but its check found a problem: {failed[0].Name}.", C.RED);
        }
        else if (lost.Count > 0)
        {
            _bar.BarColor = C.ORANGE;
            ShowMessage($"⚠  Repaired copy written and checked: {done}. Some damaged parts could not be saved; they are listed above.", C.ORANGE);
        }
        else
        {
            _bar.BarColor = C.ACCENT2;
            ShowMessage($"✓  Repaired copy written and checked: {done}, byte for byte.", C.GREEN);
        }
        _foundHeader.Text = "RESULT";
        _foundHeader.Invalidate();
        _text.Text = ResultText(report) + "\r\n\r\nWHAT THE RESCUE FOUND\r\n" + FindingsText();
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;
        _open.Visible = _explore.Visible = _reportButton.Visible = true;
        _repair.Text = "Save another copy";
        AcceptButton = _open;
        _open.Focus();
        // Another copy goes to a fresh folder.
        _folderEdited = false;
        SuggestFolder();
        // A copy that failed its check must not be missed.
        if (failed.Count > 0) BeginInvoke(() => Dialogs.ShowText(this, "The repaired copy did not pass its check", ReportText()));
    }

    static string ResultText(ConversionReport report)
    {
        var lines = new List<string>();
        var failed = report.Checks.Where(c => !c.Passed).ToList();
        lines.Add(failed.Count == 0
            ? $"CHECKED  The repaired copy opens and passed all {report.Checks.Count} checks ({string.Join(", ", report.Checks.Select(c => c.Name.ToLowerInvariant()))})."
            : "CHECK FAILED  " + string.Join("; ", failed.Select(c => $"{c.Name}: {c.Detail}")));
        var lost = report.Losses.Where(l => l.Lost).ToList();
        if (lost.Count > 0)
        {
            lines.Add("");
            lines.Add("COULD NOT BE SAVED");
            lines.AddRange(lost.Take(40).Select(l => "  ✗ " + l));
            if (lost.Count > 40) lines.Add($"  … and {lost.Count - 40} more in the full report");
        }
        if (report.Notes.Count > 0)
        {
            lines.Add("");
            lines.Add("NOTES");
            lines.AddRange(report.Notes.Select(n => "  • " + n));
        }
        if (report.Warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("WARNINGS");
            lines.AddRange(report.Warnings.Take(20).Select(w => "  • " + w));
        }
        return string.Join("\r\n", lines);
    }

    string ReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Damaged world:  {_world?.Name} — {_world?.FormatDescription}");
        sb.AppendLine($"                {_world?.Location ?? _path}");
        sb.AppendLine($"Repaired copy:  {_output}");
        if (_mcworldPath != null) sb.AppendLine($"                {_mcworldPath}");
        if (_report != null) sb.AppendLine($"Chunks:  {_report.Chunks:N0} written, {_report.FailedChunks:N0} could not be saved, in {ConvertDialog.Duration(_report.Elapsed)}");
        sb.AppendLine();
        sb.AppendLine("WHAT THE RESCUE FOUND");
        foreach (var f in _findings) sb.AppendLine("  • " + f);
        if (_findings.Count == 0) sb.AppendLine("  Nothing damaged.");
        if (_report != null)
        {
            sb.AppendLine();
            sb.AppendLine("CHECKS OF THE REPAIRED COPY");
            foreach (var c in _report.Checks) sb.AppendLine($"  {(c.Passed ? "✓" : "✗")} {c.Name}: {c.Detail}");
            sb.AppendLine();
            var lost = _report.Losses.Where(l => l.Lost).ToList();
            sb.AppendLine(lost.Count == 0 ? "NOTHING ELSE WAS LOST" : "COULD NOT BE SAVED");
            foreach (var l in lost) sb.AppendLine($"  ✗ [{l.Kind}] {l}");
            if (_report.Notes.Count > 0) { sb.AppendLine(); sb.AppendLine("NOTES"); foreach (var n in _report.Notes) sb.AppendLine("  " + n); }
            if (_report.Warnings.Count > 0) { sb.AppendLine(); sb.AppendLine("WARNINGS"); foreach (var w in _report.Warnings) sb.AppendLine("  " + w); }
        }
        return sb.ToString();
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
        catch (Exception ex) { Log.Error("Could not remove the unfinished repaired copy", ex); }
    }
}
