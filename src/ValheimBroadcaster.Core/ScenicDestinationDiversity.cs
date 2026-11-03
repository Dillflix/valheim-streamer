using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimBroadcaster.Core;

public enum ScenicLandscape : byte { Unknown = 0, Inland = 1, Coast = 2, Highland = 3 }
public enum ScenicDestinationKind : byte { Landscape = 0, Base = 1, Landmark = 2 }

// Editorial memory records actual scenery, separately from trips that merely
// attempted acquisition. Candidate multiplicity never enters a category's share.
public sealed class ScenicDestinationDiversity
{
    public const int HistoryLimit = 24, AttemptLimit = 8;
    private readonly List<ScenicRegion> history = new List<ScenicRegion>(HistoryLimit);
    private readonly List<ScenicRegion> attempts = new List<ScenicRegion>(AttemptLimit);
    private ScenicRegion? current;
    private double footage;
    private bool credited;
    public int CompletedVisits => history.Count;
    public int RecentAttempts => attempts.Count;
    public string Reason { get; private set; } = "Awaiting eligible scenic destinations";
    public string CurrentDescription => current == null ? "unclassified local scenery" : Describe(current);
    public void Reset() { history.Clear(); attempts.Clear(); StopVisit(); Reason = "Awaiting eligible scenic destinations"; }
    public void StopVisit() { current = null; footage = 0; credited = false; }
    public void Begin(ScenicRegion region) { current = region ?? throw new ArgumentNullException(nameof(region)); footage = 0; credited = false; }
    public void Attempt(ScenicRegion region)
    {
        if (region == null) throw new ArgumentNullException(nameof(region));
        if (attempts.Count == AttemptLimit) attempts.RemoveAt(0);
        attempts.Add(region); StopVisit();
    }
    public void Observe(double dt, bool actualScenicPicture)
    {
        AutonomousDirector.Time(dt, nameof(dt));
        if (current == null || credited || !actualScenicPicture) return;
        footage += Math.Min(1, dt);
        if (footage < 15) return;
        credited = true;
        if (history.Count == HistoryLimit) history.RemoveAt(0);
        history.Add(current);
        int attempt = attempts.FindLastIndex(r => r.Id == current.Id);
        if (attempt >= 0) attempts.RemoveAt(attempt);
    }
    public ScenicRegion? Choose(IReadOnlyList<ScenicRegion> eligible, AutonomyPosition position, Func<string, double> visitRank)
    {
        if (eligible == null || visitRank == null) throw new ArgumentNullException();
        if (eligible.Count > 1024) throw new ArgumentException("Scenic candidate bound exceeded.");
        if (eligible.Count == 0) { Reason = "No eligible destination after distance, freshness and cooldown rules"; return null; }
        var kinds = new HashSet<ScenicDestinationKind>(eligible.Select(r => r.Kind));
        double sourceWeight = kinds.Sum(k => Weight(k));
        double total = history.Count + attempts.Count * .25;
        var sourceScores = kinds.ToDictionary(k => k, k => Weight(k) / sourceWeight * (total + 1) - Count(r => r.Kind == k));
        // Initial acquisition retains quality preference. Once any trip has been
        // attempted, source shares govern and quality only breaks comparable ties.
        bool first = history.Count == 0 && attempts.Count == 0;
        ScenicDestinationKind chosenKind = eligible[0].Kind;
        if (!first)
            chosenKind = kinds.OrderByDescending(k => sourceScores[k]).ThenBy(k => k).First();
        var category = first ? eligible.ToList() : eligible.Where(r => r.Kind == chosenKind).ToList();
        var landscapes = new HashSet<ScenicLandscape>(category.Where(r => r.Kind == ScenicDestinationKind.Landscape).Select(r => r.Landscape));
        var landscapeScores = new Dictionary<ScenicLandscape, double>();
        double landscapeWeight = landscapes.Sum(l => Weight(l));
        double naturalTotal = Count(r => r.Kind == ScenicDestinationKind.Landscape);
        foreach (var landscape in landscapes)
            landscapeScores[landscape] = Weight(landscape) / landscapeWeight * (naturalTotal + 1) - Count(r => r.Kind == ScenicDestinationKind.Landscape && r.Landscape == landscape);
        if (!first && chosenKind == ScenicDestinationKind.Landscape && landscapes.Count > 1)
        {
            ScenicLandscape chosenLandscape = landscapes.OrderByDescending(l => landscapeScores[l]).ThenBy(l => l).First();
            category = category.Where(r => r.Landscape == chosenLandscape).ToList();
        }
        ScenicRegion? best = null; double bestBiome = 0, bestFamily = 0, bestVisit = 0, bestDistance = 0;
        foreach (ScenicRegion r in category)
        {
            // Unclassified destinations share one bucket; repeatedly using
            // unknown metadata cannot masquerade as discovering a fresh biome.
            double biome = -Count(h => h.Biome == r.Biome);
            double family = -Count(h => h.FamilyId == r.FamilyId);
            double visit = visitRank(r.Id), distance = position.HorizontalDistance(r.Position);
            bool better = best == null || (!first && (biome > bestBiome || biome == bestBiome && family > bestFamily)) ||
                (first || biome == bestBiome && family == bestFamily) &&
                (r.Preference > best!.Preference || r.Preference == best.Preference &&
                (visit < bestVisit || visit == bestVisit && (distance < bestDistance || distance == bestDistance && StringComparer.Ordinal.Compare(r.Id, best.Id) < 0)));
            if (better) { best = r; bestBiome = biome; bestFamily = family; bestVisit = visit; bestDistance = distance; }
        }
        Reason = (first ? "Initial destination" : "Balancing recent scenic coverage") + ": " + Describe(best!) +
            "; " + history.Count + " sustained visits, " + attempts.Count + " acquisition attempts; " + kinds.Count + " eligible source types";
        return best;
    }
    private double Count(Func<ScenicRegion, bool> predicate) => history.Count(predicate) + .25 * attempts.Count(predicate);
    private static double Weight(ScenicDestinationKind kind) => kind == ScenicDestinationKind.Landscape ? 3 : 1;
    private static double Weight(ScenicLandscape landscape) => landscape == ScenicLandscape.Unknown ? .25 : landscape == ScenicLandscape.Highland ? .5 : 1;
    public static string Describe(ScenicRegion r) => r.Kind + " / " + (r.Biome.Length == 0 ? "biome unknown" : r.Biome) + " / " + r.Landscape;
    public static string BiomeName(string? value)
    {
        switch ((value ?? "").Replace(" ", "").ToLowerInvariant())
        {
            case "meadows": return "Meadows";
            case "blackforest": return "BlackForest";
            case "swamp": return "Swamp";
            case "mountain": return "Mountain";
            case "plains": return "Plains";
            case "mistlands": return "Mistlands";
            case "ashlands": return "AshLands";
            case "deepnorth": return "DeepNorth";
            case "ocean": return "Ocean";
            default: return "";
        }
    }
}
