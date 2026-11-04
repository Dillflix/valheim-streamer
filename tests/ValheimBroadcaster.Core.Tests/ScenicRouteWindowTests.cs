using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicRouteWindowTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic window retains a readable suffix after an obstructed start", Suffix);
        yield return ("Scenic window finds an interior run bounded by rejected endpoints", Interior);
        yield return ("Scenic window never bridges a wall between readable endpoints", BlockedCorridor);
        yield return ("Scenic window never bridges an unknown intermediate pose", UnknownPose);
        yield return ("Scenic window treats an unknown corridor as a new run", UnknownCorridor);
        yield return ("Scenic window cannot promote isolated readable points into a moving shot", IsolatedPoints);
        yield return ("Scenic window preserves a complete readable prefix", Prefix);
        yield return ("Scenic window ranks measured travel rather than progress span", LongestTravel);
        yield return ("Equal scenic windows preserve first occurrence deterministically", StableTie);
        yield return ("Scenic window permits rotation samples without inventing camera travel", StationarySamples);
        yield return ("Invalid scenic route observations preserve accumulated valid evidence", InvalidObservations);
        yield return ("Scenic window resets all history between independently generated routes", Reset);
        yield return ("Recorded coast-prefix salvage retains the rejection beyond its endpoint", RecordedCoastPrefix);
        yield return ("Unknown scenery evidence does not become a known blocked departure", UnknownDeparture);
        yield return ("Selected scenic window owns only the rejection adjoining its own boundary", SelectedBoundary);
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Window(ScenicRouteWindow route, double start, double end, double travel)
    {
        Check(route.HasWindow && Math.Abs(route.Start - start) < 1e-9 && Math.Abs(route.End - end) < 1e-9 &&
            Math.Abs(route.TravelMetres - travel) < 1e-9, "Unexpected readable route interval.");
    }

    private static void Suffix()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, false, false); route.Observe(.2, 4, false, false);
        route.Observe(.4, 8, true, false); route.Observe(.7, 14, true, true); route.Observe(1, 20, true, true);
        Window(route, .4, 1, 12);
    }

    private static void Interior()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, false, false); route.Observe(.2, 4, true, false);
        route.Observe(.4, 8, true, true); route.Observe(.6, 12, true, true);
        route.Observe(.8, 16, false, true); route.Observe(1, 20, false, false);
        Window(route, .2, .6, 8);
    }

    private static void BlockedCorridor()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.2, 4, true, true);
        // Both viewpoints are readable, but a wall crosses the connecting move.
        route.Observe(.4, 8, true, false); route.Observe(.7, 14, true, true); route.Observe(1, 20, true, true);
        Window(route, .4, 1, 12);
    }

    private static void UnknownPose()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.25, 5, true, true);
        route.Observe(.5, 10, false, true); // Geometry/visibility evidence unavailable.
        route.Observe(.75, 15, true, true); route.Observe(1, 21, true, true);
        Window(route, .75, 1, 6);
    }

    private static void UnknownCorridor()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.25, 5, true, false);
        route.Observe(.5, 10, true, true); route.Observe(.75, 15, true, true); route.Observe(1, 20, true, false);
        Window(route, .25, .75, 10);
    }

    private static void IsolatedPoints()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, false, false); route.Observe(.25, 10, true, false);
        route.Observe(.5, 20, false, false); route.Observe(.75, 30, true, false);
        route.Observe(1, 40, true, false);
        Check(!route.HasWindow && route.TravelMetres == 0, "Disconnected safe points created a fictitious camera path.");
    }

    private static void Prefix()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.2, 6, true, true); route.Observe(.4, 12, true, true);
        route.Observe(.6, 18, false, false); route.Observe(.8, 24, false, false); route.Observe(1, 30, false, false);
        Window(route, 0, .4, 12);
    }

    private static void LongestTravel()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 100, true, false); route.Observe(.4, 104, true, true);
        route.Observe(.5, 105, false, false); route.Observe(.6, 106, true, false);
        route.Observe(.8, 118, true, true); route.Observe(1, 120, false, true);
        Window(route, .6, .8, 12);
    }

    private static void StableTie()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.2, 8, true, true);
        route.Observe(.4, 10, false, false); route.Observe(.6, 12, true, false);
        route.Observe(1, 20, true, true);
        Window(route, 0, .2, 8);
    }

    private static void StationarySamples()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.2, 0, true, true);
        Check(!route.HasWindow, "Camera rotation alone invented travel distance.");
        route.Observe(.4, 3, true, true); route.Observe(.6, 3, true, true); route.Observe(1, 6, true, true);
        Window(route, 0, 1, 6);
    }

    private static void InvalidObservations()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.25, 5, true, true);
        foreach ((double progress, double distance) in new[] {
            (double.NaN, 6d), (double.PositiveInfinity, 6d), (-.1, 6d), (1.1, 6d),
            (.25, 6d), (.2, 6d), (.5, double.NaN), (.5, double.PositiveInfinity), (.5, -1d), (.5, 4d) })
        {
            bool rejected = false;
            try { route.Observe(progress, distance, false, false); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Malformed or out-of-order path evidence was accepted.");
            Window(route, 0, .25, 5);
        }
        route.Observe(.5, 10, true, true);
        Window(route, 0, .5, 10);
    }

    private static void Reset()
    {
        var route = new ScenicRouteWindow();
        route.Observe(.3, 10, true, false); route.Observe(.8, 20, true, true); route.Reset();
        Check(!route.HasWindow && route.TravelMetres == 0 && route.Start == 0 && route.End == 0,
            "Previous route evidence survived reset.");
        route.Observe(0, 0, true, false); route.Observe(.2, 6, true, true);
        Window(route, 0, .2, 6);
    }

    private static void RecordedCoastPrefix()
    {
        // 0.8.39 capture: query70 was clear through progress.5625 (z670.538),
        // then its focus ray failed at.59375 (z671.049). The last tail became
        // readable again but was shorter than the selected8.54m prefix.
        const string rejection = "Scenic feature is outside the frame or hidden";
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false);
        route.Observe(.53125, 8.028, true, true);
        route.Observe(.5625, 8.54, true, true);
        route.Observe(.59375, 9.052, false, false, rejection);
        route.Observe(.78125, 11.91, false, false, rejection);
        route.Observe(.8125, 12.42, true, false);
        route.Observe(1, 14.95, true, true);
        Window(route, 0, .5625, 8.54);
        Check(route.HasRejectedContinuation && route.ContinuationRejection == rejection,
            "Readable prefix discarded the known failure that terminated it");
        route.Reset();
        Check(!route.HasRejectedContinuation && route.ContinuationRejection.Length == 0,
            "A new route inherited another route's rejected continuation");
    }

    private static void UnknownDeparture()
    {
        foreach (bool readableEndpoint in new[] { false, true })
        {
            var route = new ScenicRouteWindow();
            route.Observe(0, 0, true, false); route.Observe(.5, 10, true, true);
            route.Observe(.75, 15, readableEndpoint, false); // Loading/unknown pose or corridor.
            Check(!route.HasRejectedContinuation, "Missing geometry was represented as an observed obstruction");
        }
    }

    private static void SelectedBoundary()
    {
        var route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.2, 8, true, true);
        route.Observe(.3, 10, false, false, "First boundary is hidden");
        route.Observe(.4, 11, true, false); route.Observe(.5, 12, true, true);
        route.Observe(.6, 13, true, false, "Later short corridor crosses a wall");
        Check(route.ContinuationRejection == "First boundary is hidden", "An unrelated later failure replaced the selected boundary");
        route.Observe(1, 24, true, true);
        Window(route, .6, 1, 11);
        Check(!route.HasRejectedContinuation, "A longer clear suffix inherited the previous selected run's rejection");

        route = new ScenicRouteWindow();
        route.Observe(0, 0, true, false); route.Observe(.5, 8, true, true);
        route.Observe(.6, 8, true, true); // Aim-only change at the same terminal camera position.
        route.Observe(.7, 9, true, false, "Wall at the outgoing corridor");
        Check(route.HasRejectedContinuation, "A stationary boundary sample hid the outgoing corridor failure");
    }
}
