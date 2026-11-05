// MinecraftStudio.Core/Models/AnimationProject.cs: one mob being designed in the Animation Studio — its model and its
// animations, as edited copies of the game's (or a pack's), with the undo history and what changed.
// Owns: AnimationProject — the editable model and animations (the catalog's own are never changed), the names the entity
// gives its animations, new animations, every change as one undo step (a snapshot of what it touched), and which model
// and animations changed (only those go into the saved pack).
using System.Text.Json.Nodes;

namespace MinecraftStudio.Core.Models;

public sealed class AnimationProject
{
    sealed record Snapshot(string What, string? Model, Dictionary<string, string> Animations, List<(string Name, string Id)> Names, HashSet<string> Changed, bool ModelChanged);

    const int HistoryLimit = 200;
    readonly List<Snapshot> _undo = [], _redo = [];
    readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    int _state, _saved, _next;

    /// <param name="model">The mob's model (copied; null when it has none).</param>
    /// <param name="animations">Every animation the project can draw on, by id (copied when edited).</param>
    /// <param name="names">The entity's own names for its animations ("walk" → id).</param>
    public AnimationProject(string mob, EntityModel? model, IReadOnlyDictionary<string, EntityAnimation> animations, IEnumerable<(string Name, string Id)> names)
    {
        Mob = mob;
        Model = model == null ? null : EntityModels.Clone(model);
        foreach (var (name, id) in names)
            if (animations.TryGetValue(id, out var a)) { Animations[id] = a.Clone(); Names.Add((name, id)); }
    }

    public string Mob { get; }
    public EntityModel? Model { get; private set; }
    /// <summary>The project's animations (edited copies), by id.</summary>
    public Dictionary<string, EntityAnimation> Animations { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The names the entity gives its animations, and the project's new ones.</summary>
    public List<(string Name, string Id)> Names { get; private set; } = [];
    public bool ModelChanged { get; private set; }
    public IReadOnlyCollection<string> ChangedAnimations => _changed;
    /// <summary>Animations the project made itself (not the game's).</summary>
    public HashSet<string> NewIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsDirty => _state != _saved;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].What : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].What : null;

    /// <summary>Something about the project changed (redraw, refill the lists).</summary>
    public event Action? Changed;

    public void MarkSaved() { _saved = _state; Changed?.Invoke(); }

    Snapshot Take(string what) => new(what, Model == null ? null : EntityModels.ToJson(Model).ToJsonString(),
        Animations.ToDictionary(a => a.Key, a => EntityAnimations.ToJson(a.Value).ToJsonString(), StringComparer.OrdinalIgnoreCase),
        [.. Names], new HashSet<string>(_changed, StringComparer.OrdinalIgnoreCase), ModelChanged);

    void Restore(Snapshot s)
    {
        if (s.Model != null)
        {
            var read = EntityModels.Read([("undo", System.Text.Encoding.UTF8.GetBytes(new JsonObject { ["format_version"] = "1.12.0", ["minecraft:geometry"] = new JsonArray(JsonNode.Parse(s.Model)) }.ToJsonString()))]);
            var restored = read.Values.FirstOrDefault();
            // Bind poses the game adds itself are not written into the model's file; they come back from the current model.
            if (restored != null && Model != null)
                foreach (var b in restored.Bones)
                    if (Model.Bones.FirstOrDefault(x => x.Name == b.Name) is { BindPoseInherited: true } had && b.BindPose == System.Numerics.Vector3.Zero)
                    { b.BindPose = had.BindPose; b.BindPoseInherited = true; }
            Model = restored ?? Model;
        }
        var anims = new Dictionary<string, EntityAnimation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, json) in s.Animations)
        {
            var file = new JsonObject { ["format_version"] = "1.8.0", ["animations"] = new JsonObject { [id] = JsonNode.Parse(json) } };
            if (EntityAnimations.Read([("undo", System.Text.Encoding.UTF8.GetBytes(file.ToJsonString()))]).TryGetValue(id, out var a)) anims[id] = a;
        }
        Animations = anims;
        Names = [.. s.Names];
        _changed.Clear();
        _changed.UnionWith(s.Changed);
        ModelChanged = s.ModelChanged;
    }

    /// <summary>
    /// Makes a change as one undo step: <paramref name="change"/> edits the model and/or animations; the animation ids it
    /// touches (and whether it touches the model) are marked as changed, so the saved pack carries them.
    /// </summary>
    public void Change(string what, Action change, bool model = false, params string[] animations)
    {
        _undo.Add(Take(what));
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
        change();
        if (model) ModelChanged = true;
        foreach (var a in animations) _changed.Add(a);
        _state = ++_next;
        Changed?.Invoke();
    }

    public void Undo() => Travel(_undo, _redo);
    public void Redo() => Travel(_redo, _undo);

    void Travel(List<Snapshot> from, List<Snapshot> to)
    {
        if (from.Count == 0) return;
        var s = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(Take(s.What));
        Restore(s);
        _state = ++_next;
        Changed?.Invoke();
    }

    /// <summary>Marks animations (and the model) as changed without a step: they came from a pack and are saved with it again.</summary>
    public void MarkFromPack(IEnumerable<string> animations, bool model)
    {
        foreach (var a in animations) if (Animations.ContainsKey(a)) _changed.Add(a);
        if (model) ModelChanged = true;
    }

    /// <summary>A new, empty animation for this mob (named like the game's own: animation.&lt;mob&gt;.&lt;name&gt;).</summary>
    public string NewAnimation(string name, EntityAnimation? copyOf = null)
    {
        string mob = new string(Mob.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()).ToLowerInvariant();
        string baseName = new string(name.Trim().Replace(' ', '_').Where(c => char.IsLetterOrDigit(c) || c is '_' or '.').ToArray()).ToLowerInvariant();
        if (baseName.Length == 0) baseName = "new";
        string id = $"animation.{(mob.Length > 0 ? mob : "mob")}.{baseName}";
        for (int n = 2; Animations.ContainsKey(id); n++) id = $"animation.{(mob.Length > 0 ? mob : "mob")}.{baseName}_{n}";
        string shortName = baseName;
        for (int n = 2; Names.Any(x => x.Name == shortName); n++) shortName = $"{baseName}_{n}";
        Change("New animation " + shortName, () =>
        {
            var a = copyOf?.Clone() ?? new EntityAnimation { Id = id, Loop = "true", Length = 1 };
            a.Id = id;
            Animations[id] = a;
            Names.Add((shortName, id));
            NewIds.Add(id);
        }, animations: id);
        return id;
    }
}
