using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class FoliageSpatialIndexTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Foliage index contains every intersecting stationary bound across negative cells and boundaries", Equivalence);
        yield return ("Foliage index keeps mobile unknown and oversized observations in every query", GlobalFallback);
        yield return ("Foliage publication preserves active index while the next inventory is built", Publication);
        yield return ("Foliage index bounds capacity without silently discarding retained observations", Capacity);
        yield return ("Foliage local query excludes distant stationary inventory without duplicate renderer IDs", Locality);
    }
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Equivalence()
    {
        var index = new FoliageSpatialIndex(); var bounds = new List<(double X0, double Z0, double X1, double Z1)>();
        var random = new Random(51843);
        for (int i = 0; i < 2000; i++)
        {
            double x = random.Next(-220, 220), z = random.Next(-220, 220), w = random.Next(0, 20), d = random.Next(0, 20);
            bounds.Add((x, z, x + w, z + d)); index.Add(i, x, z, x + w, z + d, true);
        }
        // Exact zero-width and cell-edge bounds must not fall through broad-phase buckets.
        bounds.Add((-16, 0, -16, 0)); index.Add(2000, -16, 0, -16, 0, true);
        var result = new List<int>();
        for (int query = 0; query < 300; query++)
        {
            double x = query == 0 ? 0 : random.Next(-240, 240), z = query == 0 ? 0 : random.Next(-240, 240);
            double radius = query % 3 == 0 ? 16 : query % 3 == 1 ? 100 : 0;
            index.Query(x, z, radius, result); var found = new HashSet<int>(result);
            Check(found.Count == result.Count, "Duplicate renderer IDs");
            for (int id = 0; id < bounds.Count; id++)
            {
                var b = bounds[id]; double dx = Math.Max(b.X0 - x, Math.Max(0, x - b.X1));
                double dz = Math.Max(b.Z0 - z, Math.Max(0, z - b.Z1));
                if (dx * dx + dz * dz <= radius * radius)
                    Check(found.Contains(id), "Broad phase omitted an intersecting bound at " + id);
            }
        }
    }
    private static void GlobalFallback()
    {
        var index = new FoliageSpatialIndex();
        index.Add(0, 800, 800, 801, 801, false); // A moving observation may arrive anywhere.
        index.Add(1, double.NaN, 0, 1, 1, true); // Unknown extent cannot certify empty space.
        index.Add(2, -1000, -1000, 1000, 1000, true); // Too many buckets remains globally visible.
        index.Add(3, 0, 0, 1, 1, true);
        var result = new List<int>(); index.Query(-800, 600, 16, result);
        Check(result.OrderBy(x => x).SequenceEqual(new[] { 0, 1, 2 }), "Dynamic/unknown/large fallback omitted");
    }
    private static void Publication()
    {
        var active = new FoliageSpatialIndex(); var next = new FoliageSpatialIndex();
        active.Add(0, -4, -4, 4, 4, true); next.Add(0, 100, 100, 110, 110, true);
        var result = new List<int>(); active.Query(0, 0, 16, result); Check(result.Count == 1, "Unpublished inventory changed active lookup");
        var old = active; active = next; next = old; next.Reset();
        active.Query(0, 0, 16, result); Check(result.Count == 0, "New inventory retained stale cell membership");
        active.Query(100, 100, 16, result); Check(result.SequenceEqual(new[] { 0 }), "Recycling previous inventory changed published lookup");
        next.Add(0, -100, -100, -90, -90, true);
        active.Query(100, 100, 16, result); Check(result.SequenceEqual(new[] { 0 }), "Background rebuild mutated current IDs");
    }
    private static void Capacity()
    {
        var index = new FoliageSpatialIndex(); var result = new List<int>();
        for (int id = 0; id < FoliageSpatialIndex.Capacity; id++) index.Add(id, 0, 0, 1, 1, false);
        bool rejected = false;
        try { index.Add(FoliageSpatialIndex.Capacity, 0, 0, 1, 1, true); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Overflow silently discarded"); index.Query(100, 100, 1, result);
        Check(result.Count == FoliageSpatialIndex.Capacity, "Overflow damaged retained inventory");
        index.Reset(); index.Add(0, 0, 0, 1, 1, true); index.Query(0, 0, 1, result);
        Check(result.SequenceEqual(new[] { 0 }), "Reset retained stale visit state");
    }
    private static void Locality()
    {
        var index = new FoliageSpatialIndex(); var result = new List<int>();
        for (int id = 0; id < 2048; id++) index.Add(id, (id % 64) * 16, (id / 64) * 16, (id % 64) * 16 + 4, (id / 64) * 16 + 4, true);
        index.Query(0, 0, 16, result);
        Check(result.Count <= 4 && result.Contains(0), "Local projection still enumerates the whole stationary inventory");
    }
}
