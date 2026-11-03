using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>Measured site identity and presentation-qualified context before detail.
/// This is editorial memory, never permission to bypass scene validation.</summary>
public sealed class ScenicSiteSequence
{
    public const double ContextSeconds = 6, SubjectSeconds = 4;
    private const int Capacity = 64;
    private readonly Dictionary<string, Coverage> sites = new Dictionary<string, Coverage>(StringComparer.Ordinal);
    private double previous = -1, now;
    private sealed class Coverage { internal double Context, Subject, Last; internal readonly HashSet<string> Details = new HashSet<string>(StringComparer.Ordinal); }

    public static bool TryTrader(string? token, out string label, out string location)
    {
        switch (token)
        {
            case "$npc_haldor": label = "Haldor"; location = "Vendor_BlackForest"; return true;
            case "$npc_hildir": label = "Hildir"; location = "Hildir_camp"; return true;
            case "$npc_bogwitch": label = "Bog Witch"; location = "BogWitch_Camp"; return true;
            default: label = location = ""; return false;
        }
    }

    public void Observe(double at, ScenicFeature? feature, ScenicShotRole role, bool usable, bool held)
    {
        if (double.IsNaN(at) || double.IsInfinity(at) || at < 0) throw new ArgumentOutOfRangeException(nameof(at));
        if (previous > at) Reset();
        double dt = previous < 0 ? 0 : Math.Min(.25, at - previous);
        previous = now = at;
        if (!usable || held || feature == null || dt <= 0) return;
        string siteId = SiteId(feature);
        if (!sites.TryGetValue(siteId, out Coverage? coverage))
        {
            if (sites.Count >= Capacity)
            {
                string? oldest = null; double time = double.PositiveInfinity;
                foreach (var pair in sites) if (pair.Value.Last < time) { oldest = pair.Key; time = pair.Value.Last; }
                if (oldest != null) sites.Remove(oldest);
            }
            sites.Add(siteId, coverage = new Coverage());
        }
        coverage.Last = at;
        if (feature.SubjectKind != ScenicSubjectKind.Npc && role == ScenicShotRole.Establish)
            coverage.Context += dt;
        else if (coverage.Context >= ContextSeconds)
        {
            coverage.Subject += dt;
            if (coverage.Details.Count < 16) coverage.Details.Add(feature.Id);
        }
        // A building also establishes its own branch, before the director
        // moves to that building's interior/details in a larger settlement.
        if (feature.ParentId != null && feature.SubjectKind != ScenicSubjectKind.Npc && role == ScenicShotRole.Establish &&
            sites.Count < Capacity)
        {
            if (!sites.TryGetValue(feature.Id, out Coverage? local)) sites.Add(feature.Id, local = new Coverage());
            local.Context += dt; local.Last = at;
        }
    }

    public bool ContextShown(ScenicFeature feature) => Get(feature)?.Context >= ContextSeconds;
    public bool CanDevelop(ScenicFeature feature) => feature.SubjectKind != ScenicSubjectKind.Npc || ContextShown(feature);
    public double Priority(ScenicFeature feature)
    {
        Coverage? coverage = Get(feature);
        if (feature.SubjectKind == ScenicSubjectKind.SiteContext)
            return coverage == null || coverage.Context < ContextSeconds ? .4 : -.1;
        if (feature.SubjectKind == ScenicSubjectKind.Npc)
            return coverage == null || coverage.Context < ContextSeconds ? -.8 : coverage.Subject < SubjectSeconds ? .55 : -.2;
        if (feature.ParentId != null && coverage != null && coverage.Context >= ContextSeconds)
            return coverage.Details.Contains(feature.Id) ? -.12 : .2;
        return 0;
    }
    public void Reset() { sites.Clear(); previous = -1; now = 0; }
    private Coverage? Get(ScenicFeature feature)
    {
        if (!sites.TryGetValue(SiteId(feature), out Coverage? value) || now - value.Last > 300) return null;
        return value;
    }
    private static string SiteId(ScenicFeature feature) => feature.SubjectKind == ScenicSubjectKind.SiteContext
        ? feature.Id : feature.ParentId ?? feature.Id;
}
