using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimBroadcaster.Core;

public static class WorldDestinationPlanner
{
    // Preserve a useful procedural fallback even on worlds with many buildings.
    // Metadata guides transport; none of it certifies a locally renderable shot.
    public static IReadOnlyList<ScenicRegion> Merge(IReadOnlyList<ScenicRegion> generated,
        IReadOnlyList<WorldInterestSite> sites, DestinationFeedbackLedger local, double now)
    {
        AutonomousDirector.Time(now, nameof(now));
        if (generated == null || sites == null || local == null) throw new ArgumentNullException();
        if (generated.Count > 1024 || sites.Count > WorldInterestProtocol.MaxSites) throw new ArgumentException("Destination input exceeds limit.");
        var rows = new List<ScenicRegion>(1024); var ids = new HashSet<string>(StringComparer.Ordinal);
        int interestLimit = 1024 - Math.Min(128, generated.Count);
        foreach (WorldInterestSite s in sites.OrderByDescending(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            if (rows.Count == interestLimit) break;
            if (s.Age >= WorldInterestProtocol.SiteSeconds || !ids.Add(s.Id)) continue;
            rows.Add(new ScenicRegion(s.Id, s.Name, new AutonomyPosition(s.Arrival.X, s.Arrival.Y, s.Arrival.Z),
                s.Priority, now + Math.Max(s.RetryAfter, local.RetryAfter(s.Id, now))));
        }
        foreach (ScenicRegion g in generated)
        {
            if (rows.Count == 1024) break;
            if (ids.Add(g.Id)) rows.Add(g);
        }
        return rows;
    }
}
