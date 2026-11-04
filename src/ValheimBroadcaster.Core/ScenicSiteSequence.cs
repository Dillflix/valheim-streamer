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
    private sealed class Coverage { internal double Context, Subject, Last; internal readonly HashSet<string> Details = new HashSet<string>(StringComparer.Ordinal); internal readonly Dictionary<string, double> DetailSeconds = new Dictionary<string, double>(StringComparer.Ordinal); }

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
        double gap = previous < 0 ? 0 : at - previous;
        double dt = gap <= 1 ? Math.Min(.25, gap) : 0;
        previous = now = at;
        if (!usable || held || feature == null || dt <= 0) return;
        Coverage own = GetOrAdd(feature.Id);
        own.Last = at;
        bool portrait = feature.SubjectKind == ScenicSubjectKind.Npc || feature.ScopeLabel == "Opening";
        if (!portrait && role == ScenicShotRole.Establish) own.Context += dt;
        else if (!portrait && own.Context >= ContextSeconds || portrait && ContextShown(feature)) own.Subject += dt;
        // Showing a building establishes that building. It does NOT prove a
        // whole district/settlement was shown. Parent member credit is earned
        // separately by sustained actual child coverage.
        if (feature.ParentId != null)
        {
            Coverage? parent = Find(feature.ParentId);
            if (parent != null && parent.Context >= ContextSeconds)
            {
                if (!parent.DetailSeconds.TryGetValue(feature.Id, out double seconds)) seconds = 0;
                if (parent.DetailSeconds.Count < 16 || parent.DetailSeconds.ContainsKey(feature.Id))
                {
                    seconds += dt; parent.DetailSeconds[feature.Id] = seconds;
                    if (seconds >= SubjectSeconds) parent.Details.Add(feature.Id);
                }
                parent.Last = at;
            }
        }
    }

    public bool ContextShown(ScenicFeature feature) =>
        Find(feature.SubjectKind == ScenicSubjectKind.Npc || feature.ScopeLabel == "Opening"
            ? feature.ParentId ?? feature.Id : feature.Id)?.Context >= ContextSeconds;
    public bool CanDevelop(ScenicFeature feature) =>
        feature.SubjectKind != ScenicSubjectKind.Npc && feature.ScopeLabel != "Opening" || ContextShown(feature);
    public double Priority(ScenicFeature feature)
    {
        Coverage? own = Find(feature.Id);
        Coverage? parent = feature.ParentId != null ? Find(feature.ParentId) : null;
        bool context = own != null && own.Context >= ContextSeconds;
        if (feature.SubjectKind == ScenicSubjectKind.Npc || feature.ScopeLabel == "Opening")
            return !ContextShown(feature) ? -.8 : own == null || own.Subject < SubjectSeconds ? .55 : -.2;
        if (feature.SubjectKind == ScenicSubjectKind.SiteContext) return !context ? .4 : -.1;
        if (parent != null && parent.Context >= ContextSeconds)
            return parent.Details.Contains(feature.Id) ? own != null && own.Subject < SubjectSeconds ? .08 : -.18 : .32;
        if (feature.ScopeLabel == "Settlement" || feature.ScopeLabel == "District") return context ? -.15 : .2;
        if (feature.ScopeLabel == "Building" && context && own!.Subject < SubjectSeconds) return .12;
        return 0;
    }
    private Coverage GetOrAdd(string id)
    {
        Coverage? value = Find(id);
        if (value != null) return value;
        if (sites.Count >= Capacity)
        {
            string? oldest = null; double at = double.PositiveInfinity;
            foreach (var pair in sites) if (pair.Value.Last < at) { oldest = pair.Key; at = pair.Value.Last; }
            if (oldest != null) sites.Remove(oldest);
        }
        value = new Coverage(); sites[id] = value; return value;
    }
    private Coverage? Find(string id)
    {
        if (!sites.TryGetValue(id, out Coverage? value) || now < value.Last || now - value.Last > 300) return null;
        return value;
    }
    public void Reset() { sites.Clear(); previous = -1; now = 0; }
}
