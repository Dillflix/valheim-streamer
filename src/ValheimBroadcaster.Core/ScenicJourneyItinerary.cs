using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ValheimBroadcaster.Core;

/// <summary>A programme intention joining known generated regions to an actual
/// named destination. These observations do not certify collision, visibility or
/// loading: every local camera section still needs the live spatial evaluator.</summary>
public sealed class ScenicJourneyItinerary
{
    public const int MaximumRegions = 1024, MaximumWaypoints = 128;
    public const double MaximumObservedGap = 192;
    public ScenicRegion Destination { get; }
    public WorldInterestSite? Site { get; }
    public AutonomyPosition Origin => Waypoints[0];
    public IReadOnlyList<AutonomyPosition> Waypoints { get; }
    public IReadOnlyList<string> WaypointIds { get; }
    public double RouteMetres { get; }
    public double RequestedMetres { get; }
    public double ArrivalRadius { get; }
    public AutonomyPosition ArrivalFocus { get; }
    public string Description => Destination.Landscape == ScenicLandscape.Coast
        ? "Journey to coast at " + Destination.Name : Destination.Kind == ScenicDestinationKind.Landscape
        ? "Landscape journey to " + Destination.Name : "Approach to " + Destination.Name;
    private readonly double[] cumulative;

    private ScenicJourneyItinerary(ScenicRegion destination, WorldInterestSite? site,
        double requested, IReadOnlyList<AutonomyPosition> points, IReadOnlyList<string> ids)
    {
        Destination = destination; Site = site; RequestedMetres = requested;
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
    /// The bounded graph is deliberately conservative about missing metadata:
    /// disconnected observations cannot invent an unobserved long-distance route.</summary>
    public static bool TrySelect(AutonomyPosition origin, AutonomyPosition incomingHeading,
        IReadOnlyList<ScenicRegion> regions, IReadOnlyDictionary<string, WorldInterestSite>? sites,
        double requestedMetres, double now, IReadOnlyCollection<string>? recentDestinations,
        out ScenicJourneyItinerary? itinerary, out string reason)
    {
        itinerary = null;
        if (regions == null || regions.Count > MaximumRegions) throw new ArgumentException("Journey catalogue exceeds its bounded region inventory.", nameof(regions));
        if (!Finite(requestedMetres) || requestedMetres < 256 || requestedMetres > 5000 || !Finite(now) || now < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedMetres));
        double headingLength = Math.Sqrt((double)incomingHeading.X * incomingHeading.X + (double)incomingHeading.Z * incomingHeading.Z);
        if (headingLength < .001) { reason = "Journey departure has no measured horizontal heading"; return false; }
        var nodes = new List<ScenicRegion>(regions.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var recent = new HashSet<string>(recentDestinations ?? Array.Empty<string>(), StringComparer.Ordinal);
        double maximumLength = requestedMetres * 1.35;
        foreach (ScenicRegion region in regions)
        {
            if (region == null || !ids.Add(region.Id) || region.AvailableAfter > now ||
                origin.HorizontalDistance(region.Position) > maximumLength ||
                sites != null && sites.TryGetValue(region.Id, out WorldInterestSite? site) &&
                (site.Age >= WorldInterestProtocol.SiteSeconds || site.RetryAfter > 0)) continue;
            nodes.Add(region);
        }
        nodes.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        int count = nodes.Count;
        var distance = new double[count]; var predecessor = new int[count]; var visited = new bool[count];
        for (int i = 0; i < count; i++)
        {
            double initial = origin.HorizontalDistance(nodes[i].Position);
            distance[i] = initial <= MaximumObservedGap ? initial : double.PositiveInfinity;
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
            visited[node] = true;
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
        int best = -1; double bestScore = double.NegativeInfinity;
        double minimumLength = Math.Max(256, requestedMetres * .75);
        for (int i = 0; i < count; i++)
        {
            ScenicRegion candidate = nodes[i];
            double displacement = origin.HorizontalDistance(candidate.Position);
            if (!visited[i] || distance[i] < minimumLength || distance[i] > maximumLength ||
                displacement < requestedMetres * .6 || recent.Contains(candidate.Id) || recent.Contains(candidate.FamilyId)) continue;
            double forward = ((candidate.Position.X - origin.X) * incomingHeading.X +
                (candidate.Position.Z - origin.Z) * incomingHeading.Z) / Math.Max(.001, displacement * headingLength);
            if (forward < -.25) continue;
            // Category multiplicity cannot overwhelm distance/continuity. A
            // coast gets a modest landscape preference; structures are useful
            // destinations but never receive an unconditional source priority.
            double category = candidate.Kind == ScenicDestinationKind.Landscape ? .25 : 0;
            if (candidate.Landscape == ScenicLandscape.Coast) category += .15;
            double score = -Math.Abs(distance[i] - requestedMetres) / requestedMetres * 2 +
                forward * .35 + category + Math.Min(candidate.Preference, 10) * .005;
            if (score > bestScore) { best = i; bestScore = score; }
        }
        if (best < 0)
        { reason = "No fresh, connected known destination at the requested journey scale; ordinary scenery continues"; return false; }
        var route = new List<int>();
        for (int current = best; current >= 0; current = predecessor[current])
        {
            route.Add(current);
            if (route.Count >= MaximumWaypoints)
            { reason = "Known destination requires more itinerary observations than the bounded route supports"; return false; }
        }
        route.Reverse();
        var points = new List<AutonomyPosition>(route.Count + 1) { origin };
        var labels = new List<string>(route.Count + 1) { "departure" };
        foreach (int index in route)
            if (points[points.Count - 1].HorizontalDistance(nodes[index].Position) > .01)
            { points.Add(nodes[index].Position); labels.Add(nodes[index].Id); }
        WorldInterestSite? targetSite = null;
        if (sites != null) sites.TryGetValue(nodes[best].Id, out targetSite);
        itinerary = new ScenicJourneyItinerary(nodes[best], targetSite, requestedMetres, points, labels);
        reason = itinerary.Description + "; " + itinerary.RouteMetres.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) +
            "m through " + (points.Count - 1) + " known observations; local corridors still require validation";
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
