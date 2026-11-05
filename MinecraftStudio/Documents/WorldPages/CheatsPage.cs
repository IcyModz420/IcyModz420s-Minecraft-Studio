// MinecraftStudio/Documents/WorldPages/CheatsPage.cs: Cheats, Creative and achievements: "Achievements Enabled" (read
// from the world like the other settings, for every edition: ticking it clears the marks, unticking marks the world),
// "Restore Hardcore world from death" (the dead player back in Survival on safe ground, achievements on, still hardcore),
// whether the world has been cheated in (as each edition records it), the switches that mark it, the default game
// mode, difficulty, the game rules the world keeps (keep inventory, daylight cycle … — the usual ones first, the rest on
// request) and each player's game mode.
// Owns: CheatsPage.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents.WorldPages;

/// <summary>
/// Cheats, Creative and achievements: whether the world has been cheated in (as each edition records it), the switches
/// that mark it, the default game mode, difficulty and each player's game mode. Changes are written to level.dat (and
/// the players) when saved, after a backup.
/// </summary>
sealed class CheatsPage : WorldPage
{
    const int Width0 = 760, LabelWidth = 180;
    static readonly string[] Modes = ["Survival", "Creative", "Adventure", "Spectator"];
    static readonly string[] Difficulties = ["Peaceful", "Easy", "Normal", "Hard"];

    readonly FlowLayoutPanel _column;
    readonly ToolTip _tips = new();
    readonly Dictionary<PlayerRecord, int> _playerModes = [];
    /// <summary>Dead hardcore players to bring back when the page is saved, and where they will stand.</summary>
    // Dead players to bring back on Save changes, and where each will stand.
    readonly Dictionary<PlayerRecord, HardcoreRestore.Spot> _revive = [];
    // The world's game rules, spawn, time, weather and border as shown (changed values are written on Save changes).
    List<GameRule> _rules = [];
    WorldSettings _worldSettings = null!;
    NumberField? _spawnX, _spawnY, _spawnZ;
    DarkCheckBox? _spawnSurface;
    WorldFlags _flags = null!;
    Label _status = null!, _verdict = null!, _reasons = null!;
    DarkCheckBox? _enabled;
    Button _save = null!, _discard = null!;
    bool _dirty, _loading;

    public CheatsPage(WorldDocument owner) : base(owner)
    {
        _column = Column();
        Build();
    }

    public override bool IsDirty => _dirty;

    void Build()
    {
        _loading = true;
        _column.SuspendLayout();
        foreach (var c in _column.Controls.Cast<Control>().ToList()) c.Dispose();
        _flags = WorldFlags.Read(World);
        _playerModes.Clear();

        _column.Controls.Add(Note("Cheats & game mode", Width0, C.FG, 14f, FontStyle.Bold));
        _column.Controls.Add(Note(Intro(), Width0));

        // ---- achievements: shown for every edition, read from the world like the rest
        _column.Controls.Add(Header("ACHIEVEMENTS", Width0));
        _enabled = new DarkCheckBox("Achievements Enabled", _flags.LocksAchievements ? _flags.AchievementBlockers().Count == 0 : World is JavaWorld)
        {
            AutoSize = true, Margin = new Padding(0, this.Px(4), 0, this.Px(2)), Font = Theme.Ui(10.5f, FontStyle.Bold), AccessibleName = "Achievements Enabled",
        };
        _enabled.CheckedChanged += (_, _) => { if (!_loading) SetAchievements(_enabled.Checked); };
        _column.Controls.Add(_enabled);
        if (_flags.LocksAchievements)
        {
            _verdict = Note("", Width0, C.FG, 11f, FontStyle.Bold);
            _reasons = Note("", Width0);
            _column.Controls.AddRange([_verdict, _reasons]);
        }
        else
        {
            // Editions that never lock achievements: the box only tells how it is.
            _enabled.Enabled = false;
            _column.Controls.Add(Note(World is JavaWorld
                ? "Java Edition never locks advancements: they can be earned whatever the world's settings (cheats included)."
                : World is PocketLegacyWorld ? "Pocket Edition worlds from before 1.0 have no achievements." : "This kind of world has no achievements.", Width0, C.FG_FAINT, 9f));
        }

        // ---- hardcore: bring a dead player back (still hardcore)
        _revive.Clear();
        if (HardcoreRestore.HasHardcore(World))
        {
            _column.Controls.Add(Header("HARDCORE", Width0));
            bool hardcore = HardcoreRestore.IsHardcore(World);
            var dead = hardcore ? HardcoreRestore.Dead(World) : [];
            _column.Controls.Add(Note(!hardcore ? "This is not a hardcore world." : dead.Count == 0 ? "The player is alive: nothing to restore." :
                $"{string.Join(", ", dead.Select(PlayerLabel))} died: the world only lets {(dead.Count == 1 ? "them" : "them")} spectate.", Width0, dead.Count > 0 ? C.ORANGE : C.FG_DIM));
            var restore = Theme.Button("Restore Hardcore world from death", true);
            restore.MinimumSize = S(280, 32);
            restore.Margin = new Padding(0, this.Px(2), 0, this.Px(2));
            restore.Enabled = dead.Count > 0 && !World.OpenedForRescue;
            restore.Click += (_, _) => Restore(dead);
            _column.Controls.Add(restore);
            _column.Controls.Add(Note("Brings the player back in Survival with full health, standing on safe ground beside their bed or respawn point (else near the world spawn), and turns achievements on. The world stays hardcore; what was dropped at death stays where it fell. Saved with Save changes (after a backup).", Width0, C.FG_FAINT, 9f));
        }

        // ---- the world's own switches
        _column.Controls.Add(Header("WORLD", Width0));
        if (_flags.GameMode is int mode)
            _column.Controls.Add(Row("Default game mode", Combo(Modes, mode, v => { _flags.GameMode = v; Edited(); }), "The game mode new players and the host start in."));
        if (_flags.Difficulty is int difficulty && difficulty is >= 0 and <= 3)
            _column.Controls.Add(Row("Difficulty", Combo(Difficulties, difficulty, v => { _flags.Difficulty = v; Edited(); }), null));
        Check(_flags.Cheats, World is ConsoleWorld ? "Host privileges and commands allowed" : "Cheats are allowed (commands)", v => _flags.Cheats = v,
            World is JavaWorld ? "Lets players use commands. Java Edition keeps no record of this once it is off." : "Bedrock and console worlds switch achievements off while this is on.");
        Check(_flags.PlayedInCreative, "Has been played in Creative", v => _flags.PlayedInCreative = v,
            "The mark the game sets the first time the world is played in Creative; it keeps achievements off for good.");
        Check(_flags.ExperimentsUsed, "Experiments have been used", v => _flags.ExperimentsUsed = v,
            _flags.ExperimentsOn.Count > 0 ? $"Still switched on: {string.Join(", ", _flags.ExperimentsOn)}. The game marks the world again when it loads, so turn them off in the game too (that can break what they added)." : "The mark left by experimental gameplay.");
        Check(_flags.AchievementsDisabled, "Achievements are switched off", v => _flags.AchievementsDisabled = v, "Older versions keep this switch in the world.");
        Check(_flags.Hardcore, "Hardcore (one life)", v => _flags.Hardcore = v, null);
        Check(_flags.ForceGameMode, "Keep every player in the world's game mode", v => _flags.ForceGameMode = v, null);
        if (_flags.AddOns)
            _column.Controls.Add(Note("Add-ons (behaviour packs) are applied to this world: achievements stay off while they are. Remove them in the game's world settings if you want achievements.", Width0, C.YELLOW));

        // ---- game rules: only those the world keeps, the usual ones first, each written back in its own form
        _rules = GameRules.Read(World);
        if (_rules.Count > 0)
        {
            _column.Controls.Add(Header("GAME RULES", Width0));
            var more = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Visible = false };
            foreach (var rule in _rules)
            {
                Control editor;
                if (rule.IsBool)
                {
                    var box = new DarkCheckBox(rule.Label, rule.On) { AutoSize = true, Margin = new Padding(0, this.Px(4), 0, 0) };
                    box.CheckedChanged += (_, _) => { if (_loading) return; rule.Value = box.Checked ? "true" : "false"; Edited(); };
                    _tips.SetToolTip(box, $"Game rule {rule.Key}");
                    editor = box;
                }
                else
                {
                    var field = new NumberField { Minimum = 0, Maximum = int.MaxValue, Value = long.TryParse(rule.Value, out long v) ? Math.Clamp(v, 0, int.MaxValue) : 0, Height = this.Px(28), AccessibleName = rule.Label };
                    field.ValueChanged += (_, _) => { if (_loading) return; rule.Value = ((long)field.Value).ToString(); Edited(); };
                    _tips.SetToolTip(field, $"Game rule {rule.Key}");
                    editor = Row(rule.Label, field, null);
                }
                (rule.Common ? _column : more).Controls.Add(editor);
            }
            int extra = _rules.Count(r => !r.Common);
            if (extra > 0)
            {
                var toggle = Theme.Button($"Show every game rule ({extra} more)");
                toggle.AutoSize = true;
                toggle.Margin = new Padding(0, this.Px(6), 0, this.Px(2));
                toggle.Click += (_, _) => { more.Visible = !more.Visible; toggle.Text = more.Visible ? "Show only the usual game rules" : $"Show every game rule ({extra} more)"; };
                _column.Controls.Add(toggle);
                _column.Controls.Add(more);
            }
            _column.Controls.Add(Note("Only the rules this world keeps are shown (the game adds the rest as it needs them); each is written back the way the world stores it.", Width0, C.FG_FAINT, 9f));
        }

        // ---- spawn, time of day, weather and the Java world border: only what the world keeps
        _worldSettings = WorldSettings.Read(World);
        var ws = _worldSettings;
        _spawnX = _spawnY = _spawnZ = null;
        _spawnSurface = null;
        if (ws.Name != null || ws.SpawnX != null || ws.TimeOfDay != null || ws.Weather != null || ws.BorderSize != null)
        {
            _column.Controls.Add(Header(ws.Name != null ? "NAME, SPAWN, TIME AND WEATHER" : "SPAWN, TIME AND WEATHER", Width0));
            if (ws.Name is string name)
            {
                var field = new DarkTextField(name) { Width = this.Px(360), Height = this.Px(28), AccessibleName = "World name" };
                field.Box.TextChanged += (_, _) => { if (_loading) return; ws.Name = field.Text; Edited(); };
                _column.Controls.Add(Row("World name", field, "The name in the game's world list (the folder keeps its name)."));
            }
            if (ws.SpawnX is int sx && ws.SpawnZ is int sz)
            {
                bool surface = ws.IsBedrock && ws.SpawnY == WorldSettings.Surface;
                var fy = Coord(surface ? 64 : ws.SpawnY ?? 64, v => ws.SpawnY = v, -2048, 4096);
                fy.Enabled = !surface;
                var coords = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                _spawnX = Coord(sx, v => ws.SpawnX = v);
                _spawnY = fy;
                _spawnZ = Coord(sz, v => ws.SpawnZ = v);
                coords.Controls.AddRange([_spawnX, fy, _spawnZ]);
                _column.Controls.Add(Row("World spawn (x, y, z)", coords, "Or right-click a place on the map: Set the world spawn here."));
                if (ws.IsBedrock)
                {
                    var onTop = new DarkCheckBox("Spawn on the surface (the game finds the height)", surface) { AutoSize = true, Margin = new Padding(0, this.Px(2), 0, this.Px(4)) };
                    _spawnSurface = onTop;
                    onTop.CheckedChanged += (_, _) =>
                    {
                        if (_loading) return;
                        fy.Enabled = !onTop.Checked;
                        ws.SpawnY = onTop.Checked ? WorldSettings.Surface : (int)fy.Value;
                        Edited();
                    };
                    _column.Controls.Add(onTop);
                }
            }
            if (ws.TimeOfDay is int tod)
            {
                var times = new List<(string Label, int Ticks)> { ("Sunrise (6:00)", 0), ("Morning (7:00)", 1000), ("Noon (12:00)", 6000), ("Sunset (18:00)", 12000), ("Night (19:00)", 13000), ("Midnight (0:00)", 18000) };
                if (!times.Any(t => t.Ticks == tod)) times.Insert(0, ($"As it is now ({WorldSettings.Clock(tod)})", tod));
                _column.Controls.Add(Row("Time of day", Combo(times.Select(t => t.Label).ToArray(), times.FindIndex(t => t.Ticks == tod), i => { ws.TimeOfDay = times[i].Ticks; Edited(); }), "The days already played stay as they are."));
            }
            if (ws.Weather is Weather weather)
                _column.Controls.Add(Row("Weather", Combo(["Clear", "Rain (snow where it is cold)", "Thunderstorm"], (int)weather, i => { ws.Weather = (Weather)i; Edited(); }), null));
            if (ws.BorderSize is double size)
            {
                var width = new NumberField { Minimum = 1, Maximum = 59_999_968, Value = (decimal)Math.Clamp(Math.Round(size), 1, 59_999_968), Width = this.Px(110), Height = this.Px(28), AccessibleName = "World border width" };
                width.ValueChanged += (_, _) => { if (_loading) return; ws.BorderSize = (double)width.Value; Edited(); };
                _column.Controls.Add(Row("World border width (blocks)", width, size >= 59_999_968 ? "As wide as the world: no border." : null));
                var centre = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                centre.Controls.AddRange([Coord((int)Math.Round(ws.BorderCenterX ?? 0), v => ws.BorderCenterX = v), Coord((int)Math.Round(ws.BorderCenterZ ?? 0), v => ws.BorderCenterZ = v)]);
                _column.Controls.Add(Row("World border centre (x, z)", centre, null));
            }
        }

        // ---- players
        var players = World.Players.Where(p => WorldFlags.PlayerGameMode(World, p.Data) != null).ToList();
        if (players.Count > 0)
        {
            _column.Controls.Add(Header("PLAYERS", Width0));
            foreach (var p in players)
            {
                var player = p;
                int current = Math.Clamp(WorldFlags.PlayerGameMode(World, p.Data) ?? 0, 0, 3);
                var combo = Combo(Modes, current, v => { _playerModes[player] = v; Edited(); });
                // Bedrock players can follow the world's default (5); show it as such.
                if (World is BedrockWorld && p.Data.GetLongOrNull("PlayerGameMode") == 5)
                {
                    combo.Items.Add(new ComboItem("The world's default"));
                    combo.SelectedIndex = combo.Items.Count - 1;
                }
                _column.Controls.Add(Row(PlayerLabel(p), combo, null));
            }
        }

        // ---- save
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, this.Px(18), 0, 0), WrapContents = false };
        _save = Theme.Button("Save changes", true);
        _save.MinimumSize = S(140, 34);
        _save.Click += async (_, _) => await SaveAsync();
        _discard = Theme.Button("Discard changes");
        _discard.MinimumSize = S(140, 34);
        _discard.Click += (_, _) => { _dirty = false; Build(); Changed(); };
        buttons.Controls.AddRange([_save, _discard]);
        _status = Note("", Width0);
        _column.Controls.AddRange([buttons, _status]);
        _column.ResumeLayout();
        _loading = false;
        UpdateVerdict();
        UpdateButtons();
    }

    /// <summary>A player's name for a row: player files named by their UUID are shown shortened.</summary>
    static string PlayerLabel(PlayerRecord p) => Guid.TryParse(p.Name, out _) ? $"Player {p.Name[..8]}…" : p.Name;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }

    string Intro() => World switch
    {
        JavaWorld => "Java Edition keeps no record of whether cheats or Creative were ever used, and never locks advancements: these are the world's current settings.",
        BedrockWorld or ThreeDsWorld => "Bedrock Edition switches achievements off for good once a world is played in Creative, with cheats on, or with experiments. The marks it keeps can be cleared here.",
        ConsoleWorld => "Legacy Console Edition marks a world once it has been played in Creative, which keeps trophies and achievements off for it.",
        PocketLegacyWorld => "Pocket Edition worlds from before 1.0 have no achievements; only the game mode can be changed.",
        _ => "This kind of world keeps no cheat or game mode settings.",
    };

    // ------------------------------------------------------------------ rows

    Control Row(string label, Control editor, string? hint)
    {
        var row = new Panel { Size = S(Width0, hint == null ? 34 : 54), Margin = Padding.Empty };
        var caption = new Label { Text = label, Location = P(0, 6), Size = S(LabelWidth - 12, 20), AutoEllipsis = true, ForeColor = C.FG_DIM, UseMnemonic = false };
        _tips.SetToolTip(caption, label);
        row.Controls.Add(caption);
        editor.Location = P(LabelWidth, 0);
        editor.Width = this.Px(260);
        row.Controls.Add(editor);
        if (hint != null) row.Controls.Add(new Label { Text = hint, Location = P(LabelWidth, 32), AutoSize = true, ForeColor = C.FG_FAINT, Font = Theme.Ui(8.5f), UseMnemonic = false });
        return row;
    }

    /// <summary>Puts the world spawn at a block (from the map's menu); written with Save changes like the rest of the page.</summary>
    public bool SetSpawn(int x, int? y, int z)
    {
        if (_spawnX == null || _spawnZ == null) return false;
        _spawnX.Value = x;
        _spawnZ.Value = z;
        if (y is int h && _spawnY != null)
        {
            if (_spawnSurface is { Checked: true }) _spawnSurface.Checked = false;
            _spawnY.Value = Math.Clamp(h, _spawnY.Minimum, _spawnY.Maximum);
        }
        Show($"The world spawn goes to {x}, {(y is int yy ? yy.ToString() : "the height it had")}, {z}. Save changes to keep it (the world is backed up first).", C.GREEN);
        return true;
    }

    // A coordinate field (whole blocks).
    NumberField Coord(int value, Action<int> changed, int min = -30_000_000, int max = 30_000_000)
    {
        var field = new NumberField { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = this.Px(84), Height = this.Px(28), Margin = new Padding(0, 0, this.Px(6), 0) };
        field.ValueChanged += (_, _) => { if (_loading) return; changed((int)field.Value); Edited(); };
        return field;
    }

    DarkComboBox Combo(string[] items, int selected, Action<int> changed)
    {
        var combo = new DarkComboBox();
        foreach (var i in items) combo.Items.Add(new ComboItem(i));
        combo.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        combo.SelectedIndexChanged += (_, _) => { if (!_loading && combo.SelectedIndex < items.Length) changed(combo.SelectedIndex); };
        return combo;
    }

    /// <summary>A switch for a flag the world has (none for a flag its version does not keep).</summary>
    void Check(bool? value, string text, Action<bool> changed, string? hint)
    {
        if (value is not bool on) return;
        var box = new DarkCheckBox(text, on) { AutoSize = true, Margin = new Padding(0, this.Px(4), 0, 0) };
        box.CheckedChanged += (_, _) => { if (_loading) return; changed(box.Checked); Edited(); };
        _column.Controls.Add(box);
        if (hint != null) _column.Controls.Add(new Label { Text = hint, AutoSize = true, MaximumSize = S(Width0 - 30, 0), ForeColor = C.FG_FAINT, Font = Theme.Ui(8.5f), Margin = new Padding(this.Px(26), 0, 0, this.Px(4)), UseMnemonic = false });
    }

    void Edited()
    {
        _dirty = true;
        UpdateVerdict();
        UpdateButtons();
        Changed();
    }

    void UpdateVerdict()
    {
        if (!_flags.LocksAchievements || _verdict == null) return;
        // The verdict as the world would be saved now (players kept in Creative count too).
        var blockers = _flags.AchievementBlockers().ToList();
        if (World is BedrockWorld && World.Players.Any(p => (_playerModes.TryGetValue(p, out int m) ? m : WorldFlags.PlayerGameMode(World, p.Data)) == WorldFlags.Creative))
            blockers.Add("a player is in Creative");
        _verdict.Text = blockers.Count == 0 ? "✓  Achievements can be earned in this world." : "✗  Achievements are off in this world.";
        _verdict.ForeColor = blockers.Count == 0 ? C.GREEN : C.ORANGE;
        _reasons.Text = blockers.Count == 0 ? (_dirty ? "Save the changes to keep it that way." : "") : "Because " + string.Join("; ", blockers) + ".";
        // The box shows the world as it would be saved now.
        if (_enabled != null && _enabled.Checked != (blockers.Count == 0))
        {
            bool was = _loading;
            _loading = true;
            _enabled.Checked = blockers.Count == 0;
            _loading = was;
        }
    }

    /// <summary>
    /// "Achievements Enabled" changed: ticked clears every mark (as the game would need it); unticked marks the world the
    /// way its edition does — its own "achievements off" switch where it has one, else cheats on.
    /// </summary>
    void SetAchievements(bool on)
    {
        if (on)
        {
            ReEnable();
            var left = _flags.AchievementBlockers();
            if (left.Count > 0)
            {
                _status.ForeColor = C.ORANGE;
                _status.Text = "The marks are cleared, but achievements stay off because " + string.Join("; ", left) + ": that can only be changed in the game.";
            }
            else
            {
                _status.ForeColor = C.FG_DIM;
                _status.Text = "Achievements are back on (the marks are cleared below). Save changes to keep it.";
            }
            return;
        }
        if (_flags.AchievementsDisabled != null) _flags.AchievementsDisabled = true;
        else if (_flags.Cheats != null) _flags.Cheats = true;
        else if (_flags.PlayedInCreative != null) _flags.PlayedInCreative = true;
        else { Show("This world keeps no switch that turns achievements off.", C.ORANGE); UpdateVerdict(); return; }
        RefreshControls();
        Edited();
        _status.ForeColor = C.FG_DIM;
        _status.Text = _flags.AchievementsDisabled == true ? "Achievements are switched off for this world. Save changes to keep it." : "Cheats are on, which switches achievements off. Save changes to keep it.";
    }

    void UpdateButtons()
    {
        bool canSave = World is not PocketLegacyWorld || ((PocketLegacyWorld)World).Level != null;
        _save.Enabled = _dirty && canSave && !World.OpenedForRescue;
        _discard.Enabled = _dirty;
    }

    /// <summary>Gets the dead players ready to come back (on Save changes), and turns achievements on with them.</summary>
    void Restore(List<PlayerRecord> dead)
    {
        var spots = new Dictionary<PlayerRecord, HardcoreRestore.Spot>();
        foreach (var p in dead)
            if (HardcoreRestore.SafeSpot(World, p) is { } s) spots[p] = s;
        if (spots.Count == 0)
        {
            Show("No safe ground was found by the player's bed or near the world spawn (the area is not generated, or is water or lava), so nothing was changed.", C.ORANGE);
            return;
        }
        _revive.Clear();
        foreach (var (p, s) in spots) _revive[p] = s;
        // The world's own game mode goes back to Survival too, if death set it to Spectator; and achievements come back on.
        if (_flags.GameMode == WorldFlags.Spectator) _flags.GameMode = WorldFlags.Survival;
        ReEnable();
        foreach (var p in spots.Keys) _playerModes[p] = WorldFlags.Survival;
        RefreshControls();
        var missed = dead.Where(p => !spots.ContainsKey(p)).Select(PlayerLabel).ToList();
        Show($"{string.Join("; ", spots.Select(s => $"{PlayerLabel(s.Key)} at {s.Value}"))}: back in Survival, and achievements are on. Save changes to do it (the world is backed up first)."
            + (missed.Count > 0 ? $" No safe ground was found for {string.Join(", ", missed)}: they stay as they are." : ""), missed.Count > 0 ? C.ORANGE : C.GREEN);
    }

    /// <summary>Clears every mark and puts Creative players and the world back in Survival; saved with Save changes.</summary>
    void ReEnable()
    {
        _flags.ClearMarks();
        foreach (var p in World.Players)
            if ((_playerModes.TryGetValue(p, out int m) ? m : WorldFlags.PlayerGameMode(World, p.Data)) == WorldFlags.Creative) _playerModes[p] = WorldFlags.Survival;
        RefreshControls();
        _dirty = true;
        UpdateVerdict();
        UpdateButtons();
        Changed();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "The marks are cleared above. Check them, then Save changes.";
    }

    /// <summary>Shows the current (unsaved) flags in the switches after a rebuild.</summary>
    void RefreshControls()
    {
        _loading = true;
        foreach (var box in _column.Controls.OfType<DarkCheckBox>())
            box.Checked = box.Text switch
            {
                var t when t.StartsWith("Cheats") || t.StartsWith("Host privileges") => _flags.Cheats == true,
                "Has been played in Creative" => _flags.PlayedInCreative == true,
                "Experiments have been used" => _flags.ExperimentsUsed == true,
                "Achievements are switched off" => _flags.AchievementsDisabled == true,
                var t when t.StartsWith("Hardcore") => _flags.Hardcore == true,
                var t when t.StartsWith("Keep every player") => _flags.ForceGameMode == true,
                _ => box.Checked,
            };
        var rows = _column.Controls.OfType<Panel>().ToList();
        foreach (var row in rows)
        {
            if (row.Controls.OfType<Label>().FirstOrDefault()?.Text is not string label || row.Controls.OfType<DarkComboBox>().FirstOrDefault() is not DarkComboBox combo) continue;
            if (label == "Default game mode" && _flags.GameMode is int gm) combo.SelectedIndex = Math.Clamp(gm, 0, 3);
            else if (World.Players.FirstOrDefault(p => PlayerLabel(p) == label) is PlayerRecord p && _playerModes.TryGetValue(p, out int m)) combo.SelectedIndex = m;
        }
        _loading = false;
    }

    public override async Task SaveAsync()
    {
        if (!_dirty) return;
        if (WorldUse.InUse(World) is string busy) { Show(busy, C.ORANGE); return; }
        var flags = _flags;
        var modes = new Dictionary<PlayerRecord, int>(_playerModes);
        var revive = new Dictionary<PlayerRecord, HardcoreRestore.Spot>(_revive);
        var rules = _rules.ToList();
        var settings = _worldSettings;
        bool ok = await Main.RunAsync("Saving " + World.Name, progress =>
        {
            Backup.WorldBeforeSave(World, progress);
            // Game rules, spawn, time and weather live in the same level.dat as the flags: set them first, saved with it.
            GameRules.Apply(World, rules);
            settings.Apply();
            flags.Save();
            // Brought back from death: Survival, full health, on safe ground (then saved with the other players).
            foreach (var (p, spot) in revive) HardcoreRestore.Restore(World, p, spot);
            foreach (var (p, m) in modes)
                if (WorldFlags.SetPlayerGameMode(World, p.Data, m) || revive.ContainsKey(p)) PlayerStore.Save(World, p);
            foreach (var p in revive.Keys.Where(p => !modes.ContainsKey(p))) PlayerStore.Save(World, p);
        });
        if (!ok) return;
        _dirty = false;
        Build();
        Changed();
        Show($"Saved. A backup was made first.", C.GREEN);
        Owner.RefreshFacts();
    }

    void Show(string text, Color color)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }
}
