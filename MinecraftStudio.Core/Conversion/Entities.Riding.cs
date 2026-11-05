// MinecraftStudio.Core/Conversion/Entities.Riding.cs: riders across versions, for Entities.
// Part of Entities (main part in Entities.cs). Before Java 1.9 (and on Legacy Console) a rider kept what it rode inside it
// ("Riding"), one rider each; since 1.9 the one at the bottom keeps its riders ("Passengers"), canonical too.
// Owns: RidersOnTop (old stack → canonical), Turned (canonical → old stack: the top rider outermost), and the riders who
// get off where the target can't seat them.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Entities
{
    /// <summary>
    /// A pre-1.9 rider stack (each rider holding what it rides in "Riding") as since 1.9: the bottom entity holding its
    /// riders in "Passengers". Every level is a copy; the stack is followed 64 deep at most.
    /// </summary>
    static NbtCompound RidersOnTop(NbtCompound rider)
    {
        var stack = new List<NbtCompound>();
        for (var e = rider; e != null && stack.Count < 64; e = e["Riding"] as NbtCompound)
        {
            var level = new NbtCompound();
            foreach (var (key, tag) in e)
                if (key != "Riding") level[key] = tag.Clone();
            stack.Add(level);
        }
        for (int i = stack.Count - 2; i >= 0; i--)
        {
            var below = stack[i + 1];
            if (below.GetList("Passengers") is not NbtList seated) below["Passengers"] = seated = new NbtList(NbtType.Compound);
            seated.Insert(0, stack[i]);
        }
        return stack[^1];
    }

    /// <summary>
    /// For a version before 1.9: the entity (converted, its riders converted) as the old stack — its first rider outermost,
    /// holding it in "Riding". Riders past the first get off and stand where they are (into <paramref name="setDown"/>;
    /// without one they are reported lost).
    /// </summary>
    static NbtCompound Turned(NbtCompound bottom, int dv, List<NbtCompound>? setDown)
    {
        if (bottom.GetList("Passengers") is not NbtList seated) return bottom;
        bottom.Remove("Passengers");
        var riders = seated.OfType<NbtCompound>().ToList();
        if (riders.Count == 0) return bottom;
        if (riders.Count > 1)
        {
            if (setDown != null)
            {
                setDown.AddRange(riders.Skip(1));
                ConversionLog.Note(LossKind.Entity, "riders", $"{TargetName(dv)} seats one rider on a mob or vehicle: the others got off and stand beside it", riders.Count - 1);
            }
            else ConversionLog.Note(LossKind.Entity, "riders", $"{riders.Count - 1} lost: {TargetName(dv)} seats one rider on a mob or vehicle", riders.Count - 1, lost: true);
        }
        var top = riders[0];
        var under = top;
        while (under["Riding"] is NbtCompound next) under = next;
        under["Riding"] = bottom;
        return top;
    }

    /// <summary>
    /// Bedrock keeps riders as actors of their own, linked from what they ride ("LinksTag": the riders' UniqueIDs, in seat
    /// order); canonical keeps them inside it. Each actor read (<paramref name="actors"/>: as stored, and canonical) whose
    /// rider is among them gets it in its Passengers; the entities left standing on their own are returned, in order. A
    /// link to an actor that isn't here (another chunk, a player) is left, and a link that would seat an entity on itself
    /// (a damaged save) is refused.
    /// </summary>
    public static List<NbtCompound> SeatRiders(List<(NbtCompound Stored, NbtCompound Canonical)> actors)
    {
        var byId = new Dictionary<long, NbtCompound>();
        foreach (var (stored, canonical) in actors)
            if (stored.GetLongOrNull("UniqueID") is long id) byId.TryAdd(id, canonical);
        var seatedOn = new Dictionary<NbtCompound, NbtCompound>(ReferenceEqualityComparer.Instance);
        bool Under(NbtCompound e, NbtCompound other)
        {
            for (var at = e; seatedOn.TryGetValue(at, out var below);) { if (ReferenceEquals(below, other)) return true; at = below; }
            return false;
        }
        foreach (var (stored, vehicle) in actors)
        {
            if (stored.GetList("LinksTag") is not { Count: > 0 } links) continue;
            foreach (var link in links.OfType<NbtCompound>().OrderBy(l => l.GetInt("LinkID")))
            {
                if (link.GetLongOrNull("entityID") is not long riderId || !byId.TryGetValue(riderId, out var rider)) continue;
                if (ReferenceEquals(rider, vehicle) || seatedOn.ContainsKey(rider) || Under(vehicle, rider)) continue;
                if (vehicle.GetList("Passengers") is not NbtList seated) vehicle["Passengers"] = seated = new NbtList(NbtType.Compound);
                seated.Add(rider);
                seatedOn[rider] = vehicle;
            }
        }
        return actors.Select(a => a.Canonical).Where(c => !seatedOn.ContainsKey(c)).ToList();
    }

    /// <summary>
    /// An entity the version can't have (<see cref="Drop"/>). Its riders get off and stand where they are, converted on
    /// their own into <paramref name="setDown"/>; without one they are dropped with it (counted, what they carry rescued).
    /// </summary>
    static NbtCompound? Dropped(NbtCompound canonical, int dv, List<NbtCompound>? setDown, string why, bool itselfAsItem = false)
    {
        if (setDown == null || canonical.GetList("Passengers") is not { Count: > 0 } seated) return Drop(canonical, why, itselfAsItem);
        foreach (var r in seated.OfType<NbtCompound>())
            if (ToJava(r, dv, setDown) is NbtCompound j) setDown.Add(j);
        return Drop(Without(canonical, "Passengers"), why, itselfAsItem);
    }

    /// <summary>Everyone riding the entity, and riding them, all the way up.</summary>
    public static IEnumerable<NbtCompound> AllRiders(NbtCompound e)
    {
        foreach (var r in e.GetList("Passengers")?.OfType<NbtCompound>() ?? [])
        {
            yield return r;
            foreach (var above in AllRiders(r)) yield return above;
        }
    }

    /// <summary>A copy of the compound without one key (its other tags shared, not copied).</summary>
    public static NbtCompound Without(NbtCompound e, string key)
    {
        var c = new NbtCompound(e.Count);
        foreach (var (k, v) in e)
            if (k != key) c[k] = v;
        return c;
    }
}
