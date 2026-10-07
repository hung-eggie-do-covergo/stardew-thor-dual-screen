using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace DualScreen;

/// <summary>Today's crop counts, kept incrementally: one full walk at day start, then each event only looks
/// at the tiles it can have changed (dry tiles after watering, ready tiles after picking, a few tiles around
/// you after planting).</summary>
public class CropTracker
{
    // Per location: tiles whose crop is ready to harvest, and tiles whose crop still needs water.
    readonly Dictionary<GameLocation, HashSet<Vector2>> ready = new(), dry = new();
    public int Ready { get; private set; }
    public int Dry { get; private set; }
    public Item ReadyItem { get; private set; }
    public int FullCounts { get; private set; }

    static IEnumerable<GameLocation> Locations()
    {
        yield return Game1.getFarm();
        if (Game1.getLocationFromName("Greenhouse") is GameLocation g) yield return g;
        if (Game1.getLocationFromName("IslandWest") is GameLocation i) yield return i;
    }

    static HoeDirt DirtAt(GameLocation loc, Vector2 tile) =>
        loc.terrainFeatures.TryGetValue(tile, out var f) && f is HoeDirt d ? d
        : loc.objects.TryGetValue(tile, out var o) && o is IndoorPot p ? p.hoeDirt.Value : null;

    static bool IsReady(HoeDirt d) => d?.crop != null && !d.crop.dead.Value && d.readyForHarvest();

    static bool IsDry(GameLocation loc, HoeDirt d) =>
        d?.crop != null && !d.crop.dead.Value && !d.readyForHarvest() && d.needsWatering() && !d.isWatered()
        && !(loc.IsOutdoors && loc.IsRainingHere());

    /// <summary>Day start (or first use): walk every crop tile once.</summary>
    public void FullCount()
    {
        ready.Clear(); dry.Clear();
        FullCounts++;
        foreach (var loc in Locations())
        {
            if (loc == null) continue;
            var r = ready[loc] = new HashSet<Vector2>();
            var w = dry[loc] = new HashSet<Vector2>();
            foreach (var (tile, f) in loc.terrainFeatures.Pairs)
                if (f is HoeDirt d) Classify(loc, tile, d, r, w);
            foreach (var (tile, o) in loc.objects.Pairs)
                if (o is IndoorPot p) Classify(loc, tile, p.hoeDirt.Value, r, w);
        }
        Totals();
    }

    static void Classify(GameLocation loc, Vector2 tile, HoeDirt d, HashSet<Vector2> r, HashSet<Vector2> w)
    {
        if (IsReady(d)) r.Add(tile);
        else if (IsDry(loc, d)) w.Add(tile);
    }

    /// <summary>After watering or picking: drop tiles that are no longer dry or ready. Touches only those tiles.</summary>
    public void Recheck()
    {
        if (ready.Count == 0) { FullCount(); return; }
        foreach (var (loc, set) in dry) set.RemoveWhere(t => !IsDry(loc, DirtAt(loc, t)));
        foreach (var (loc, set) in ready) set.RemoveWhere(t => !IsReady(DirtAt(loc, t)));
        Totals();
    }

    /// <summary>After something was planted: look at the few tiles it could have gone on.</summary>
    public void CheckAround(GameLocation loc, IEnumerable<Vector2> tiles)
    {
        if (loc == null || !dry.TryGetValue(loc, out var w)) return;
        foreach (var t in tiles)
            if (IsDry(loc, DirtAt(loc, t))) w.Add(t);
        Totals();
    }

    void Totals()
    {
        Ready = ready.Values.Sum(s => s.Count);
        Dry = dry.Values.Sum(s => s.Count);
        // The crop you have most of ready, for the Today card's icon.
        var top = ready.SelectMany(kv => kv.Value.Select(t => DirtAt(kv.Key, t)?.crop?.indexOfHarvest.Value))
            .Where(id => id != null).GroupBy(id => id).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        if (top != ReadyItem?.ItemId) ReadyItem = top == null ? null : ItemRegistry.Create(top, allowNull: true);
    }
}
