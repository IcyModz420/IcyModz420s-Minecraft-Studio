// MinecraftStudio/Documents/PrunerDialog.cs: Chooses chunks to delete: rarely visited, far away, outside/inside
// a selection, or unfinished.
// Owns: PrunerDialog.
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>Chooses chunks to delete: rarely visited, far away, outside/inside a selection, or unfinished.</summary>
sealed class PrunerDialog : DarkDialog
{
    readonly WorldDimension _dim;
    readonly (int X, int Z)? _spawn;
    readonly List<(double X, double Z)> _players;
    readonly HashSet<ChunkPos> _selection;
    readonly RadioButton _byTime, _byDistance, _outsideSel, _insideSel, _proto;
    readonly NumberField _minutes, _radius, _keepSpawn, _keepPlayers;
    readonly DarkCheckBox _keepSpawnOn, _keepPlayersOn, _keepSelOn;
    readonly DarkComboBox _shape;
    readonly Label _result;
    readonly SlimProgress _progress;
    readonly Button _preview, _delete;
    CancellationTokenSource? _cts;

    public HashSet<ChunkPos> Result { get; private set; } = [];
    public bool PreviewOnly { get; private set; }

    public PrunerDialog(WorldDimension dim, (int X, int Z)? spawn, IEnumerable<(double, double)> players, HashSet<ChunkPos> selection, bool supportsInhabited)
        : base("Prune chunks — " + dim.DisplayName, 560)
    {
        _dim = dim;
        _spawn = spawn;
        _players = players.ToList();
        _selection = selection;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        panel.Controls.Add(Theme.Label($"{dim.ChunkPositions.Count:N0} chunks in {dim.DisplayName}. Choose what to delete:", C.FG));

        _byTime = Radio("Chunks where players spent less than", supportsInhabited);
        _minutes = new NumberField { Minimum = 0, Maximum = 100000, Value = 2, Size = new Size(this.Px(70), this.Px(26)), AccessibleName = "Minutes" };
        panel.Controls.Add(Row(_byTime, _minutes, Theme.Label("minutes (InhabitedTime)", C.FG_DIM)));
        if (!supportsInhabited) panel.Controls.Add(Theme.Label("   (this edition does not record time spent in chunks)", C.FG_FAINT));

        _byDistance = Radio("Chunks farther than", true);
        _radius = new NumberField { Minimum = 1, Maximum = 100000, Value = 64, Size = new Size(this.Px(70), this.Px(26)), AccessibleName = "Distance in chunks" };
        _shape = new DarkComboBox { Width = this.Px(96), AccessibleName = "Distance shape" };
        _shape.Items.AddRange([new ComboItem("square"), new ComboItem("circle")]);
        _shape.SelectedIndex = 0;
        panel.Controls.Add(Row(_byDistance, _radius, Theme.Label("chunks (", C.FG_DIM), _shape, Theme.Label(") from spawn and every player", C.FG_DIM)));

        _outsideSel = Radio($"Everything outside the map selection ({selection.Count:N0} chunks selected)", selection.Count > 0);
        _insideSel = Radio("The selected chunks", selection.Count > 0);
        _proto = Radio("Unfinished chunks (partly generated, Java)", dim.ChunkPositions.Count > 0);
        panel.Controls.Add(_outsideSel);
        panel.Controls.Add(_insideSel);
        panel.Controls.Add(_proto);
        (supportsInhabited ? _byTime : _byDistance).Checked = true;

        panel.Controls.Add(Theme.Label(" ", C.FG));
        panel.Controls.Add(Theme.Label("Always keep:", C.FG));
        _keepSpawnOn = new DarkCheckBox("Chunks within", spawn != null) { Enabled = spawn != null };
        _keepSpawn = new NumberField { Minimum = 0, Maximum = 1000, Value = 8, Size = new Size(this.Px(60), this.Px(26)), AccessibleName = "Chunks around spawn to keep" };
        panel.Controls.Add(Row(_keepSpawnOn, _keepSpawn, Theme.Label("chunks of spawn", C.FG_DIM)));
        _keepPlayersOn = new DarkCheckBox("Chunks within", _players.Count > 0) { Enabled = _players.Count > 0 };
        _keepPlayers = new NumberField { Minimum = 0, Maximum = 1000, Value = 8, Size = new Size(this.Px(60), this.Px(26)), AccessibleName = "Chunks around players to keep" };
        panel.Controls.Add(Row(_keepPlayersOn, _keepPlayers, Theme.Label("chunks of a player", C.FG_DIM)));
        _keepSelOn = new DarkCheckBox("Selected chunks (when not pruning the selection itself)", selection.Count > 0) { Enabled = selection.Count > 0 };
        panel.Controls.Add(_keepSelOn);

        _progress = new SlimProgress { Width = this.Px(520), Height = this.Px(6), Visible = false };
        _result = Theme.Label("Press Preview to see which chunks would be deleted.", C.YELLOW);
        _result.MaximumSize = new Size(this.Px(520), 0);
        panel.Controls.Add(Theme.Label(" ", C.FG));
        panel.Controls.Add(_progress);
        panel.Controls.Add(_result);

        var buttons = AddButtons(("Cancel", DialogResult.Cancel, false));
        _delete = Theme.Button("Delete chunks", true);
        _delete.Enabled = false;
        _delete.MinimumSize = new Size(this.Px(110), this.Px(30));
        _delete.Click += (_, _) => { PreviewOnly = false; DialogResult = DialogResult.OK; };
        _preview = Theme.Button("Preview on map");
        _preview.MinimumSize = new Size(this.Px(110), this.Px(30));
        _preview.Click += async (_, _) => await Compute();
        var showOnly = Theme.Button("Select on map only");
        showOnly.MinimumSize = new Size(this.Px(110), this.Px(30));
        showOnly.Click += (_, _) => { PreviewOnly = true; DialogResult = DialogResult.OK; };
        buttons.Controls.Add(_delete);
        buttons.Controls.Add(showOnly);
        buttons.Controls.Add(_preview);
        Controls.Add(panel);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormClosing += (_, _) => _cts?.Cancel();

        // Any change of the criteria invalidates the last preview.
        void Reset(object? s, EventArgs e)
        {
            _computed = false;
            _delete.Enabled = false;
            _result.Text = "Press Preview to see which chunks would be deleted.";
        }
        foreach (var r in new[] { _byTime, _byDistance, _outsideSel, _insideSel, _proto }) r.CheckedChanged += Reset;
        foreach (var n in new[] { _minutes, _radius, _keepSpawn, _keepPlayers }) n.ValueChanged += Reset;
        foreach (var c in new[] { _keepSpawnOn, _keepPlayersOn, _keepSelOn }) c.CheckedChanged += Reset;
        _shape.SelectedIndexChanged += Reset;
    }

    DarkRadioButton Radio(string text, bool enabled) => new(text) { Enabled = enabled, Margin = new Padding(0, this.Px(6), this.Px(4), 0) };

    FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        // Anchored left only, so every control is centred on the row's height.
        foreach (var c in controls) { c.Margin = new Padding(0, this.Px(4), this.Px(6), 0); c.Anchor = AnchorStyles.Left; p.Controls.Add(c); }
        return p;
    }

    async Task Compute()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _preview.Enabled = _delete.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        var all = _dim.ChunkPositions.ToList();
        bool byTime = _byTime.Checked, byDist = _byDistance.Checked, outside = _outsideSel.Checked, inside = _insideSel.Checked, proto = _proto.Checked;
        long ticks = (long)_minutes.Value * 60 * 20;
        int radius = (int)_radius.Value;
        int keepS = _keepSpawnOn.Checked ? (int)_keepSpawn.Value : -1, keepP = _keepPlayersOn.Checked ? (int)_keepPlayers.Value : -1;
        bool keepSel = _keepSelOn.Checked && !inside;
        var players = _players.Select(p => ((int)Math.Floor(p.X / 16), (int)Math.Floor(p.Z / 16))).ToList();
        var spawnChunk = _spawn is (int sx, int sz) ? ((sx >> 4), (sz >> 4)) : ((int, int)?)null;
        // Distances in chunks, as a square (like the game's view distance) or a circle.
        bool circle = _shape.SelectedIndex == 1;
        bool Within(ChunkPos pos, (int X, int Z) c, int r) => circle
            ? (long)(pos.X - c.X) * (pos.X - c.X) + (long)(pos.Z - c.Z) * (pos.Z - c.Z) <= (long)r * r
            : Math.Max(Math.Abs((long)pos.X - c.X), Math.Abs((long)pos.Z - c.Z)) <= r;
        var progress = new Progress<double>(v => _progress.Value = v);
        try
        {
            var result = await Task.Run(() =>
            {
                var set = new HashSet<ChunkPos>();
                int i = 0;
                foreach (var pos in all)
                {
                    token.ThrowIfCancellationRequested();
                    if ((++i & 255) == 0) ((IProgress<double>)progress).Report(i / (double)all.Count);
                    if (keepS >= 0 && spawnChunk is (int cx, int cz) && Within(pos, (cx, cz), keepS)) continue;
                    if (keepP >= 0 && players.Any(p => Within(pos, p, keepP))) continue;
                    if (keepSel && _selection.Contains(pos)) continue;
                    bool delete;
                    if (outside) delete = !_selection.Contains(pos);
                    else if (inside) delete = _selection.Contains(pos);
                    else if (byDist)
                    {
                        var centers = new List<(int, int)>(players);
                        if (spawnChunk is (int a, int b)) centers.Add((a, b));
                        if (centers.Count == 0) centers.Add((0, 0));
                        delete = centers.All(c => !Within(pos, c, radius));
                    }
                    else
                    {
                        ChunkInfo? info;
                        try { lock (_dim) info = _dim.GetChunkInfo(pos); }
                        catch { info = null; }
                        if (info == null) continue;
                        delete = byTime ? info.InhabitedTime >= 0 && info.InhabitedTime < ticks : proto && !info.IsFull;
                    }
                    if (delete) set.Add(pos);
                }
                return set;
            }, token);
            Result = result;
            _computed = true;
            long kept = all.Count - result.Count;
            _result.Text = result.Count == 0
                ? "No chunks match. Nothing would be deleted."
                : $"{result.Count:N0} of {all.Count:N0} chunks would be deleted ({result.Count * 100.0 / Math.Max(1, all.Count):0.#}%); {kept:N0} kept. They are highlighted in red on the map when you close this with \"Select on map only\".";
            _delete.Enabled = result.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _result.Text = "Failed: " + ex.Message; }
        finally
        {
            _progress.Visible = false;
            _preview.Enabled = true;
        }
    }

    bool _computed;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && !_computed)
        {
            _computed = true;
            // "Select on map only" or "Delete" pressed before Preview: compute synchronously first.
            e.Cancel = true;
            var ok = DialogResult;
            DialogResult = DialogResult.None;
            _ = Compute().ContinueWith(_ => BeginInvoke(() => { DialogResult = ok; Close(); }), TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }
        base.OnFormClosing(e);
    }
}
