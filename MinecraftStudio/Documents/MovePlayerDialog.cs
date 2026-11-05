// Documents/MovePlayerDialog.cs: a stuck or lost player moved to safe ground (Java, Bedrock, consoles, Pocket 0.x, Indev).
// Owns: MovePlayerDialog — where: beside their bed or respawn point (else near the world spawn), near the world spawn, or
// near coordinates (where they are now, when that is the overworld); "Find a safe spot" shows the place found (solid,
// harmless ground with room to stand — the hardcore restore's rules — and room for what a Java player rides); "Move them"
// backs the world up and puts the player there in the overworld, still, not falling or burning (HardcoreRestore.MoveAndSave).
// Refused while the game has the world open.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class MovePlayerDialog : DarkDialog
{
    readonly SaveWorld _world;
    readonly PlayerRecord _player;
    readonly DarkRadioButton _home, _spawn, _near;
    readonly NumberField _x, _z;
    readonly Label _where, _found, _status;
    readonly Button _find, _move;
    HardcoreRestore.Spot? _spot;
    bool _busy;

    /// <summary>Whether the player was moved and saved.</summary>
    public bool Moved { get; private set; }

    /// <summary>The window for a player; <paramref name="near"/> (a block right-clicked on the map) starts it at those coordinates.</summary>
    public MovePlayerDialog(SaveWorld world, PlayerRecord player, (int X, int Z)? near = null) : base($"Move {player.Name} to safety", 560)
    {
        _world = world;
        _player = player;
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Label Note(string text, Color color) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(this.Px(520), 0), ForeColor = color, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        };
        column.Controls.Add(Note("For a player stuck in a wall, lost in the void, trapped in the Nether or the End, or somewhere they can't get out of: they are put on safe ground in the overworld — solid, harmless ground with room to stand (not water, lava, fire, a cactus, a tree top or a bed) — standing still, not falling.", C.FG_DIM));
        if (HardcoreRestore.MountName(world, player) is string mount)
            column.Controls.Add(Note($"They are riding a {mount}: it comes with them, so the place found has room for it too.", C.FG_FAINT));
        _home = new DarkRadioButton("Beside their bed or respawn point (else near the world spawn)") { Checked = true };
        _spawn = new DarkRadioButton("Near the world spawn");
        _near = new DarkRadioButton("Near these coordinates, on the surface:");
        var (startX, startZ, startWhere) = near is var (nx, nz) ? (nx, nz, "where you right-clicked") : Start(world, player);
        if (near != null) { _home.Checked = false; _near.Checked = true; }
        _x = new NumberField { Width = this.Px(100), Height = this.Px(28), Minimum = -30_000_000, Maximum = 30_000_000, Value = startX, AccessibleName = "x" };
        _z = new NumberField { Width = this.Px(100), Height = this.Px(28), Minimum = -30_000_000, Maximum = 30_000_000, Value = startZ, AccessibleName = "z" };
        _where = new Label { Text = startWhere, AutoSize = true, ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(this.Px(8), this.Px(7), 0, 0) };
        var coords = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, this.Px(8)) };
        coords.Controls.AddRange([
            new Label { Text = "x", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(24), this.Px(7), this.Px(4), 0) }, _x,
            new Label { Text = "z", AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(10), this.Px(7), this.Px(4), 0) }, _z, _where]);
        foreach (var c in new Control[] { _home, _spawn, _near, coords }) column.Controls.Add(c);
        foreach (var r in new[] { _home, _spawn, _near }) r.CheckedChanged += (_, _) => { _spot = null; ShowSpot(); };
        void Typed() { _where.Text = ""; _near.Checked = true; _spot = null; ShowSpot(); }
        _x.ValueChanged += (_, _) => Typed();
        _z.ValueChanged += (_, _) => Typed();
        _find = Theme.Button("Find a safe spot");
        _find.MinimumSize = new Size(this.Px(150), this.Px(30));
        _find.Click += async (_, _) => await FindAsync();
        _found = new Label { AutoSize = true, MaximumSize = new Size(this.Px(520), 0), ForeColor = C.FG, UseMnemonic = false, Margin = new Padding(0, this.Px(8), 0, 0) };
        column.Controls.Add(_find);
        column.Controls.Add(_found);

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(520), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _move = Theme.Button("Move them", true);
        _move.MinimumSize = new Size(this.Px(120), this.Px(30));
        _move.Click += async (_, _) => await MoveAsync();
        buttons.Controls.Add(_move);
        Controls.Add(_status);
        Controls.Add(column);
        SizeToContent();
        ShowSpot();
    }

    // Where the coordinates start: where the player is now when that is the overworld, else the world spawn.
    static (int X, int Z, string Where) Start(SaveWorld world, PlayerRecord p)
    {
        var d = p.Data;
        bool overworld = world is BedrockWorld
            ? d.GetLong("DimensionId") == 0
            : d["Dimension"] switch { NbtString s => s.Value is "minecraft:overworld" or "overworld", { IsNumeric: true } n => n.AsLong == 0, _ => true };
        if (overworld && d.GetList("Pos") is { Count: 3 } pos && double.IsFinite(pos[0].AsDouble) && double.IsFinite(pos[2].AsDouble)
            && Math.Abs(pos[0].AsDouble) < 30_000_000 && Math.Abs(pos[2].AsDouble) < 30_000_000)
            return ((int)Math.Floor(pos[0].AsDouble), (int)Math.Floor(pos[2].AsDouble), "where they are now");
        try
        {
            var info = WorldInfoReader.Read(world);
            return (info.SpawnX, info.SpawnZ, "the world spawn");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or Core.IO.SaveFormatException) { return (0, 0, ""); }
    }

    void ShowSpot()
    {
        _found.Text = _spot is { } s ? $"Safe ground at {s}." : "";
        _move.Enabled = !_busy && _spot != null;
        _find.Enabled = !_busy;
        foreach (var c in new Control[] { _home, _spawn, _near, _x, _z }) c.Enabled = !_busy;
    }

    async Task FindAsync()
    {
        _busy = true;
        ShowSpot();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Looking for safe ground…";
        bool home = _home.Checked, near = _near.Checked;
        int x = (int)_x.Value, z = (int)_z.Value;
        try
        {
            _spot = await Task.Run(() => near ? HardcoreRestore.SafeSpotNear(_world, x, z, _player) : HardcoreRestore.SafeSpot(_world, _player, besideBed: home));
            _status.ForeColor = _spot == null ? C.ORANGE : C.FG_DIM;
            _status.Text = _spot == null
                ? $"No safe ground within {HardcoreRestore.SearchReach} blocks: that part of the world isn't generated, or it is all water, lava or trees. Try other coordinates."
                : "Press Move them to put the player there.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException)
        {
            _spot = null;
            _status.ForeColor = C.ORANGE;
            _status.Text = ex.Message;
        }
        finally { _busy = false; ShowSpot(); }
    }

    async Task MoveAsync()
    {
        if (_spot is not { } at) return;
        if (!PlayerStore.CanSave(_world)) { _status.ForeColor = C.ORANGE; _status.Text = _world.OpenedForRescue ? SaveWorld.RescueReadOnly : "Players of this kind of world can't be saved."; return; }
        if (WorldUse.InUse(_world) is string busy) { _status.ForeColor = C.ORANGE; _status.Text = busy; return; }
        _busy = true;
        ShowSpot();
        try
        {
            var progress = new Progress<string>(s => _status.Text = s);
            _status.ForeColor = C.FG_DIM;
            _status.Text = "Backing up the world…";
            var done = await Task.Run(() =>
            {
                Backup.WorldBeforeSave(_world, progress);
                return HardcoreRestore.MoveAndSave(_world, _player, at);
            });
            Moved = true;
            _spot = null;
            _status.ForeColor = C.GREEN;
            _status.Text = $"✓  {_player.Name}: {string.Join(", ", done)}, in the overworld. Saved; a backup was made first.";
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)) Log.Error("Moving a player failed", ex);
            _status.ForeColor = C.ORANGE;
            _status.Text = "Not moved: " + ex.Message;
        }
        finally { _busy = false; ShowSpot(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }
}
