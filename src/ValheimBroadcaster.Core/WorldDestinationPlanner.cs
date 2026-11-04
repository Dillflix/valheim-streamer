using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimBroadcaster.Core;

public static class WorldDestinationPlanner
{
    // Preserve representative categories before ranking: a dense catalogue of
    // ruins must not evict every coastline or less numerous biome at the cap.
    // Server metadata guides transport, never certifies a renderable local shot.
    public static IReadOnlyList<ScenicRegion> Merge(IReadOnlyList<ScenicRegion> generated,
        IReadOnlyList<WorldInterestSite> sites, DestinationFeedbackLedger local, double now)
    {
        AutonomousDirector.Time(now, nameof(now));
        if (generated == null || sites == null || local == null) throw new ArgumentNullException();
        if (generated.Count > 1024 || sites.Count > WorldInterestProtocol.MaxSites) throw new ArgumentException("Destination input exceeds limit.");
        var distinct = new Dictionary<string, ScenicRegion>(StringComparer.Ordinal);
        foreach (ScenicRegion region in generated) distinct[region.Id] = region;
        foreach (WorldInterestSite s in sites)
        {
            if (s.Age >= WorldInterestProtocol.SiteSeconds) continue;
            ScenicDestinationKind kind = s.Kind == WorldInterestKind.Natural ? ScenicDestinationKind.Landscape :
                s.Kind == WorldInterestKind.Landmark ? ScenicDestinationKind.Landmark : ScenicDestinationKind.Base;
            // Natural entries deliberately share generated-region IDs. The
            // classified approach replaces, rather than duplicates, that row.
            distinct[s.Id] = new ScenicRegion(s.Id, s.Name, new AutonomyPosition(s.Arrival.X, s.Arrival.Y, s.Arrival.Z),
                s.Priority, now + Math.Max(s.RetryAfter, local.RetryAfter(s.Id, now)), s.Biome, s.Landscape, kind,
                s.ParentId.Length == 0 ? s.Id : s.ParentId);
        }
        var sources = distinct.Values.GroupBy(r => r.Kind).OrderBy(g => g.Key).Select(g => new SourceBuckets(g)).ToArray();
        var rows = new List<ScenicRegion>(1024);
        bool progress = true;
        while (rows.Count < 1024 && progress)
        {
            progress = false;
            foreach (SourceBuckets source in sources)
            {
                ScenicRegion? region = source.Take();
                if (region == null) continue;
                rows.Add(region); progress = true;
                if (rows.Count == 1024) break;
            }
        }
        return rows;
    }
    private sealed class SourceBuckets
    {
        private readonly Queue<ScenicRegion>[] buckets;
        private int cursor;
        internal SourceBuckets(IEnumerable<ScenicRegion> source)
        {
            buckets = source.GroupBy(r => r.Biome + ":" + (int)r.Landscape).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Queue<ScenicRegion>(g.OrderByDescending(r => r.Preference).ThenBy(r => r.Id, StringComparer.Ordinal))).ToArray();
        }
        internal ScenicRegion? Take()
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                Queue<ScenicRegion> bucket = buckets[cursor]; cursor = (cursor + 1) % buckets.Length;
                if (bucket.Count != 0) return bucket.Dequeue();
            }
            return null;
        }
    }
}
