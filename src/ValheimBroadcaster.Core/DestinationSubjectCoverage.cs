using System;

namespace ValheimBroadcaster.Core;

/// <summary>Local evidence for a remotely nominated structural or coastal destination.
/// A coordinate is only a search hint. Only matching loaded scene features and
/// matching, actually presented readable frames can fulfil the visit.</summary>
public sealed class DestinationSubjectCoverage
{
    public const double RequiredSeconds = 15, ContextSeconds = 6, SearchSeconds = 30;
    public WorldInterestSite? Site { get; private set; }
    public double ShownSeconds { get; private set; }
    public double EstablishedSeconds { get; private set; }
    public double NpcSeconds { get; private set; }
    public bool RequiresNpc => Site != null && Site.Kind == WorldInterestKind.Landmark && TraderToken(Site.Name) != null;
    private bool TraderSite => RequiresNpc;
    public string CoverageScope { get; private set; } = "No observed subject context";
    public double SearchElapsed { get; private set; }
    public bool Shown { get; private set; }
    public bool Satisfied => Site != null && ShownSeconds >= RequiredSeconds && EstablishedSeconds >= ContextSeconds && (!TraderSite || NpcSeconds >= ScenicSiteSequence.SubjectSeconds);
    public bool Unavailable => Site != null && !Satisfied && SearchElapsed >= SearchSeconds && (!Shown || TraderSite && NpcSeconds < ScenicSiteSequence.SubjectSeconds);
    public bool Prioritize => Site != null && !Satisfied && !Unavailable;
    private double previous = -1;

    public bool Set(WorldInterestSite? site)
    {
        if (site != null && site.Kind != WorldInterestKind.Base && site.Kind != WorldInterestKind.BaseArea && site.Kind != WorldInterestKind.Landmark &&
            !(site.Kind == WorldInterestKind.Natural && site.Landscape == ScenicLandscape.Coast)) site = null;
        if (Site?.Id == site?.Id) return false;
        Site = site; ShownSeconds = EstablishedSeconds = NpcSeconds = SearchElapsed = 0; CoverageScope = "No observed subject context"; Shown = false; previous = -1; return true;
    }
    public void Reset() { Site = null; ShownSeconds = EstablishedSeconds = NpcSeconds = SearchElapsed = 0; CoverageScope = "No observed subject context"; Shown = false; previous = -1; }

    public bool Matches(ScenicFeature? feature)
    {
        if (Site == null || feature == null || feature.Confidence < .5f) return false;
        string? trader = TraderToken(Site.Name);
        if (trader != null)
        {
            if (feature.SubjectKind == ScenicSubjectKind.Environment || feature.SubjectIdentity != trader) return false;
        }
        else if (feature.SubjectKind != ScenicSubjectKind.Environment) return false;
        ScenicFeatureKind expected = Site.Kind == WorldInterestKind.Natural ? ScenicFeatureKind.Coastline : ScenicFeatureKind.Structure;
        if (feature.Kind != expected) return false;
        // Interior spaces and small props must not stand in for a building or
        // landmark establishing view. Bounds remain approximate, never a
        // semantic claim that a particular prefab name has been recognized.
        if (feature.ScopeLabel == "Room" || feature.ScopeLabel == "Opening") return false;
        double margin = Math.Min(6, feature.Radius * .25f);
        return Math.Abs(feature.Center.X - Site.Center.X) <= Site.Extents.X + margin &&
            Math.Abs(feature.Center.Z - Site.Center.Z) <= Site.Extents.Z + margin &&
            Math.Abs(feature.Center.Y - Site.Center.Y) <= Site.Extents.Y + feature.Height * .5f + 4;
    }
    public bool Meaningful(ScenicFeature? feature)
    {
        if (!Matches(feature) || feature!.SubjectKind == ScenicSubjectKind.Npc) return false;
        // A small fragment of a broad site is useful detail only after its
        // larger context has been established. It cannot fulfil the whole visit.
        double minimum = Math.Max(2, Math.Min(8, Math.Max(Site!.Extents.X, Site.Extents.Z) * .2));
        return feature!.Radius >= minimum && feature.Height >= 1;
    }
    public double Priority(ScenicFeature feature)
    {
        if (!Prioritize || !Matches(feature)) return 0;
        if (feature.SubjectKind == ScenicSubjectKind.Npc)
            return EstablishedSeconds >= ContextSeconds && NpcSeconds < ScenicSiteSequence.SubjectSeconds ? 5 : 0;
        // Select locally observed coherent structures, then their details;
        // oversized partial settlement envelopes remain behind actual buildings.
        double extent = Math.Min(feature.Radius, 30) / 30;
        return 2 + extent + (Meaningful(feature) ? 1 : 0) - (feature.IsPartial ? .25 : 0);
    }
    public void Observe(double now, bool onAir, bool usableLive, bool held, ScenicFeature? feature, ScenicShotRole role = ScenicShotRole.Establish)
    {
        if (!Enum.IsDefined(typeof(ScenicShotRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        if (previous > now) { Reset(); return; }
        double dt = previous < 0 ? 0 : Math.Min(.25, now - previous);
        previous = now;
        bool context = role == ScenicShotRole.Establish && Meaningful(feature);
        bool relatedDetail = EstablishedSeconds >= ContextSeconds && Matches(feature);
        Shown = Site != null && onAir && usableLive && !held && (context || relatedDetail);
        if (Site == null || !onAir || held) return;
        if (!Satisfied) SearchElapsed += dt;
        if (Shown)
        {
            ShownSeconds += dt;
            if (feature!.SubjectKind == ScenicSubjectKind.Npc && relatedDetail) NpcSeconds += dt;
            if (context)
            {
                EstablishedSeconds += dt;
                string scope = feature!.ScopeLabel ?? (feature.Kind == ScenicFeatureKind.Coastline ? "coastline" : "structure cluster");
                bool local = feature.Radius < Math.Max(Site.Extents.X, Site.Extents.Z) * .5;
                CoverageScope = (local ? "Local " : "Observed ") + scope + " at destination";
            }
        }
    }
    private static string? TraderToken(string name)
    {
        switch (name)
        {
            case "Vendor_BlackForest": return "$npc_haldor";
            case "Hildir_camp": return "$npc_hildir";
            case "BogWitch_Camp": return "$npc_bogwitch";
            default: return null;
        }
    }
}
