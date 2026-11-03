using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimBroadcaster.Core;

// Retain representation before quality ranking. Hundreds of common landmarks
// cannot consume every catalogue slot ahead of a scarce biome or coastline.
public static class WorldInterestDistribution
{
    public static IReadOnlyList<WorldInterestSite> Take(IEnumerable<WorldInterestSite> sites, int limit)
    {
        if (sites == null || limit < 0 || limit > WorldInterestProtocol.MaxSites) throw new ArgumentException("Invalid catalogue bound.");
        var groups = sites.GroupBy(s => s.Kind + ":" + s.Biome + ":" + s.Landscape, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Queue<WorldInterestSite>(g.OrderByDescending(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal))).ToArray();
        var result = new List<WorldInterestSite>(limit);
        while (result.Count < limit)
        {
            bool any = false;
            foreach (var group in groups)
            {
                if (group.Count == 0) continue;
                result.Add(group.Dequeue()); any = true;
                if (result.Count == limit) break;
            }
            if (!any) break;
        }
        return result;
    }
}
