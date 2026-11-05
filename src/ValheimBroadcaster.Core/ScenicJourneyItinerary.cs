using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ValheimBroadcaster.Core;

/// <summary>Bounded destination-selection evidence. Counts describe metadata
/// eligibility and guiding topology, not camera clearance or world loading.</summary>
public sealed class ScenicJourneySelectionEvidence
{
    public int CatalogueRegions { get; internal set; }
    public int UsableObservations { get; internal set; }
    public int NearDeparture { get; internal set; }
    public int ConnectedObservations { get; internal set; }
    public int ConnectedCandidates { get; internal set; }
    public int BearingCandidates { get; internal set; }
    public int CooldownExcluded { get; internal set; }
    public int StaleExcluded { get; internal set; }
    public int RadiusExcluded { get; internal set; }
    public int RecentExcluded { get; internal set; }
    public int HeadingExcluded { get; internal set; }
    public int ScaleExcluded { get; internal set; }
    public string Summary => $"catalogue {CatalogueRegions}; usable {UsableObservations}; near departure {NearDeparture}; connected {ConnectedObservations}; connected destinations {ConnectedCandidates}; bearing destinations {BearingCandidates}; excluded cooldown {CooldownExcluded}, stale {StaleExcluded}, radius {RadiusExcluded}, recent {RecentExcluded}, heading {HeadingExcluded}, scale {ScaleExcluded}";
}

/// <summary>A programme intention toward an actual named destination. Metadata
/// does not certify collision, visibility or loading: every local camera section
/// still needs the live spatial evaluator.</summary>
public sealed class ScenicJourneyItinerary
{
    public const int MaximumRegions = 1024, MaximumWaypoints = 128;
    public const double MaximumObservedGap = 192;
    public ScenicRegion Destination { get; }
    public WorldInterestSite? Site { get; }
    public AutonomyPosition Origin => Waypoints[0];
    public IReadOnlyList<AutonomyPosition> Waypoints { get; }
    public IReadOnlyList<string> WaypointIds { get; }
    /// <summary>True when the programme guide joins nearby metadata observations.
    /// Neither value is a clearance or loaded-scene certificate.</summary>
    public bool UsesConnectedObservations { get; }
    public double RouteMetres { get; }
    public double RequestedMetres { get; }
    public double ArrivalRadius { get; }
    public AutonomyPosition ArrivalFocus { get; }
    public string Description => Destination.Landscape == ScenicLandscape.Coast
        ? "Journey to coast at " + Destination.Name : Destination.Kind == ScenicDestinationKind.Landscape
        ? "Landscape journey to " + Destination.Name : "Approach to " + Destination.Name;
    private readonly double[] cumulative;

    private ScenicJourneyItinerary(ScenicRegion destination, WorldInterestSite? site,
        double requested, IReadOnlyList<AutonomyPosition> points, IReadOnlyList<string> ids, bool connected)
    {
        Destination = destination; Site = site; RequestedMetres = requested; UsesConnectedObservations = connected;
        var copy = new AutonomyPosition[points.Count]; var names = new string[points.Count];
        cumulative = new double[points.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = points[i]; names[i] = ids[i];
            if (i > 0) cumulative[i] = cumulative[i - 1] + copy[i - 1].HorizontalDistance(copy[i]);
        }
        Waypoints = new ReadOnlyCollection<AutonomyPosition>(copy);
        WaypointIds = new ReadOnlyCollection<string>(names);
        RouteMetres = cumulative[cumulative.Length - 1];
        ArrivalRadius = site == null ? 16 : Math.Max(12, Math.Min(24, Math.Max(site.Extents.X, site.Extents.Z) * .3));
        ArrivalFocus = site == null ? destination.Position : new AutonomyPosition(site.Center.X, site.Center.Y, site.Center.Z);
    }

    /// <summary>Select once per catalogue/request, not per rendered frame.
    /// Destination metadata is sparse, paged and selected for interest. A nearby
    /// chain supplies a useful guide when present, but its absence does not prove
    /// that travel is blocked. A bearing to a known destination is an intention;
    /// the adapter still validates every local section and ending before use.</summary>
    public static bool TrySelect(AutonomyPosition origin, AutonomyPosition incomingHeading,
        IReadOnlyList<ScenicRegion> regions, IReadOnlyDictionary<string, WorldInterestSite>? sites,
        double requestedMetres, double now, IReadOnlyCollection<string>? recentDestinations,
        out ScenicJourneyItinerary? itinerary, out string reason) =>
        TrySelect(origin, incomingHeading, regions, sites, requestedMetres, now, recentDestinations,
            out itinerary, out reason, out _);

    public static bool TrySelect(AutonomyPosition origin, AutonomyPosition incomingHeading,
        IReadOnlyList<ScenicRegion> regions, IReadOnlyDictionary<string, WorldInterestSite>? sites,
        double requestedMetres, double now, IReadOnlyCollection<string>? recentDestinations,
        out ScenicJourneyItinerary? itinerary, out string reason, out ScenicJourneySelectionEvidence evidence,
        bool allowReverseDeparture = false)
    {
        itinerary = null; evidence = new ScenicJourneySelectionEvidence();
        if (regions == null || regions.Count > MaximumRegions) throw new ArgumentException("Journey catalogue exceeds its bounded region inventory.", nameof(regions));
        if (!Finite(requestedMetres) || requestedMetres < 256 || requestedMetres > 5000 || !Finite(now) || now < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedMetres));
        evidence.CatalogueRegions = regions.Count;
        double headingLength = Math.Sqrt((double)incomingHeading.X * incomingHeading.X + (double)incomingHeading.Z * incomingHeading.Z);
        if (headingLength < .001) { reason = "Journey departure has no measured horizontal heading"; return false; }
        var nodes = new List<ScenicRegion>(regions.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var recent = new HashSet<string>(recentDestinations ?? Array.Empty<string>(), StringComparer.Ordinal);
        double maximumLength = requestedMetres * 1.35;
        foreach (ScenicRegion region in regions)
        {
            if (region == null || !ids.Add(region.Id)) continue;
            WorldInterestSite? site = null;
            if (sites != null) sites.TryGetValue(region.Id, out site);
            if (site != null && site.Age >= WorldInterestProtocol.SiteSeconds)
            { evidence.StaleExcluded++; continue; }
            if (region.AvailableAfter > now || site != null && site.RetryAfter > 0)
            { evidence.CooldownExcluded++; continue; }
            if (origin.HorizontalDistance(region.Position) > maximumLength)
            { evidence.RadiusExcluded++; continue; }
            nodes.Add(region);
        }
        nodes.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        int count = nodes.Count; evidence.UsableObservations = count;
        var distance = new double[count]; var predecessor = new int[count]; var visited = new bool[count];
        for (int i = 0; i < count; i++)
        {
            double initial = origin.HorizontalDistance(nodes[i].Position);
            distance[i] = initial <= MaximumObservedGap ? initial : double.PositiveInfinity;
            if (initial <= MaximumObservedGap) evidence.NearDeparture++;
            predecessor[i] = -1;
        }
        // Dijkstra is bounded to <=1024 observations. Edges are generated in
        // place, avoiding an unbounded retained graph or Unity scene searches.
        for (int iteration = 0; iteration < count; iteration++)
        {
            int node = -1; double minimum = double.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!visited[i] && distance[i] < minimum) { minimum = distance[i]; node = i; }
            if (node < 0 || minimum > maximumLength) break;
            visited[node] = true; evidence.ConnectedObservations++;
            for (int i = 0; i < count; i++)
            {
                if (visited[i]) continue;
                double edge = nodes[node].Position.HorizontalDistance(nodes[i].Position);
                if (edge < .01 || edge > MaximumObservedGap) continue;
                double proposed = minimum + edge;
                if (proposed < distance[i] && proposed <= maximumLength)
                { distance[i] = proposed; predecessor[i] = node; }
            }
        }
        int best = -1, bearingBest = -1;
        double bestScore = double.NegativeInfinity, bearingScore = double.NegativeInfinity;
        double minimumLength = Math.Max(256, requestedMetres * .75);
        for (int i = 0; i < count; i++)
        {
            ScenicRegion candidate = nodes[i];
            double displacement = origin.HorizontalDistance(candidate.Position);
            if (recent.Contains(candidate.Id) || recent.Contains(candidate.FamilyId))
            { evidence.RecentExcluded++; continue; }
            double forward = ((candidate.Position.X - origin.X) * incomingHeading.X +
                (candidate.Position.Z - origin.Z) * incomingHeading.Z) / Math.Max(.001, displacement * headingLength);
            // A separately framed departure cut may turn toward a known place
            // behind the incidental current view. Continuous-seam callers keep
            // the conservative heading gate; both retain a forward preference.
            if (!allowReverseDeparture && forward < -.25) { evidence.HeadingExcluded++; continue; }
            bool connected = visited[i] && distance[i] >= minimumLength && distance[i] <= maximumLength &&
                displacement >= requestedMetres * .6;
            bool bearing = displacement >= minimumLength && displacement <= maximumLength;
            if (!connected && !bearing) { evidence.ScaleExcluded++; continue; }
            // Category multiplicity cannot overwhelm distance/continuity. A
            // coast gets a modest landscape preference; structures are useful
            // destinations but never receive an unconditional source priority.
            double category = candidate.Kind == ScenicDestinationKind.Landscape ? .25 : 0;
            if (candidate.Landscape == ScenicLandscape.Coast) category += .15;
            double preference = forward * .35 + category + Math.Min(candidate.Preference, 10) * .005;
            if (connected)
            {
                evidence.ConnectedCandidates++;
                double score = -Math.Abs(distance[i] - requestedMetres) / requestedMetres * 2 + preference;
                if (score > bestScore) { best = i; bestScore = score; }
            }
            if (bearing)
            {
                evidence.BearingCandidates++;
                double score = -Math.Abs(displacement - requestedMetres) / requestedMetres * 2 + preference;
                if (score > bearingScore) { bearingBest = i; bearingScore = score; }
            }
        }
        bool connectedGuide = best >= 0;
        if (!connectedGuide) best = bearingBest;
        if (best < 0)
        { reason = "No eligible known destination at the requested journey scale; " + evidence.Summary; return false; }
        var route = new List<int>();
        if (connectedGuide)
        {
            for (int current = best; current >= 0; current = predecessor[current])
            {
                route.Add(current);
                if (route.Count >= MaximumWaypoints)
                { reason = "Known destination requires more itinerary observations than the bounded route supports"; return false; }
            }
            route.Reverse();
        }
        else route.Add(best);
        var points = new List<AutonomyPosition>(route.Count + 1) { origin };
        var labels = new List<string>(route.Count + 1) { "departure" };
        foreach (int index in route)
            if (points[points.Count - 1].HorizontalDistance(nodes[index].Position) > .01)
            { points.Add(nodes[index].Position); labels.Add(nodes[index].Id); }
        WorldInterestSite? targetSite = null;
        if (sites != null) sites.TryGetValue(nodes[best].Id, out targetSite);
        itinerary = new ScenicJourneyItinerary(nodes[best], targetSite, requestedMetres, points, labels, connectedGuide);
        reason = itinerary.Description + "; " + itinerary.RouteMetres.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) +
            "m; " + (connectedGuide ? "guide through " + (points.Count - 1) + " connected observations" :
            "bearing to a known destination; intermediate observations are sparse") +
            "; every local corridor and ending still require validation; " + evidence.Summary;
        return true;
    }

    public AutonomyPosition SteeringGoal(AutonomyPosition from, double lookaheadMetres = 48)
    {
        if (!Finite(lookaheadMetres) || lookaheadMetres <= 0 || lookaheadMetres > 192) throw new ArgumentOutOfRangeException(nameof(lookaheadMetres));
        return PositionAt(Math.Min(RouteMetres, ProjectProgress(from) + lookaheadMetres));
    }

    public AutonomyPosition PositionAt(double distance)
    {
        if (!Finite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        distance = Math.Max(0, Math.Min(RouteMetres, distance));
        for (int i = 1; i < Waypoints.Count; i++)
        {
            if (cumulative[i] < distance) continue;
            double fraction = (distance - cumulative[i - 1]) / Math.Max(.001, cumulative[i] - cumulative[i - 1]);
            AutonomyPosition a = Waypoints[i - 1], b = Waypoints[i];
            return new AutonomyPosition((float)(a.X + (b.X - a.X) * fraction),
                (float)(a.Y + (b.Y - a.Y) * fraction), (float)(a.Z + (b.Z - a.Z) * fraction));
        }
        return Destination.Position;
    }

    public double ProjectProgress(AutonomyPosition position)
    {
        double nearest = double.PositiveInfinity, progress = 0;
        for (int i = 1; i < Waypoints.Count; i++)
        {
            AutonomyPosition a = Waypoints[i - 1], b = Waypoints[i];
            double x = b.X - a.X, z = b.Z - a.Z, squared = x * x + z * z;
            double t = Math.Max(0, Math.Min(1, ((position.X - a.X) * x + (position.Z - a.Z) * z) / Math.Max(.0001, squared)));
            double dx = position.X - a.X - x * t, dz = position.Z - a.Z - z * t;
            double error = dx * dx + dz * dz;
            if (error < nearest) { nearest = error; progress = cumulative[i - 1] + Math.Sqrt(squared) * t; }
        }
        return progress;
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
