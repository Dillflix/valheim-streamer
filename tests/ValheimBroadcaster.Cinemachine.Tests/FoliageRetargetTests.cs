using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class FoliageRetargetTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Captured relocation restarts an obsolete foliage scan without transferring its coverage", CapturedRelocations);
        yield return ("Retarget preserves unpublished additions as unknown at the previous catalogue origin", PendingAdditions);
        yield return ("Retarget inside the old catalogue radius cannot revive clearance around newly discovered foliage", OverlappingRetarget);
        yield return ("Nearby carrier movement keeps the shared catalogue anchor and active traversal", NearbyMovement);
        yield return ("Sustained fast travel finishes still-useful foliage sweeps instead of repeatedly restarting them", SustainedMotion);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static FoliageCatalogueAnchorAction Decide(bool anchored, double displacement, bool scanning) =>
        FoliageCatalogueAnchorPolicy.Decide(anchored, displacement, scanning, FoliageCoverageMap.Radius, ScenicObservationPolicy.DetailRadius);

    private static void CapturedRelocations()
    {
        // The uploaded .42 sequence translated so its first region is zero.
        // At 60.083 the old traversal was still running; the obsolete snapshot
        // was published at 65.448 and new-region coverage only at 71.168.
        var first = Vector3.zero;
        var second = new Vector3(-31.11865234375f, 17.770755767822266f, 254.50048828125f);
        var third = new Vector3(544.88134765625f, 40.54652786254883f, 318.50048828125f);
        var published = new FoliageCoverageMap(); published.Reset(first);
        foreach (var relocation in new[] { (From: first, To: second), (From: second, To: third) })
        {
            double distance = Vector3.Distance(relocation.From, relocation.To);
            Check(Decide(true, distance, true) == FoliageCatalogueAnchorAction.RestartScan,
                "The recorded authoritative relocation still waits for the obsolete traversal to finish");
            Check(published.CoverageAt(relocation.From, 16) == 16 && published.CoverageAt(relocation.To, 16) == 0,
                "Cancelling the old scan silently recentered its published coverage");
            Check(Decide(true, 0, false) == FoliageCatalogueAnchorAction.Keep,
                "Repeated requests for the destination restart its new traversal");
            // Only completion of a new catalogue can replace the published
            // coverage map. Merely requesting/restarting it cannot do so.
            published.Reset(relocation.To);
            Check(published.CoverageAt(relocation.To, 16) == 16, "A completed destination catalogue did not supply local coverage");
        }
    }

    private static void PendingAdditions()
    {
        var published = new FoliageCoverageMap(); published.Reset(Vector3.zero);
        var pending = new FoliageCoverageMap(); pending.Reset(Vector3.zero);
        pending.AddUnknown(new Bounds(new Vector3(2, 2, 2), Vector3.one));
        published.RetainUnknownFrom(pending);
        pending.Reset(new Vector3(300, 0, 0));
        Check(published.CoverageAt(new Vector3(2, 2, 2), 16) == 0,
            "Restarting discovery erased newly discovered but unpublished geometry");
        Check(published.CoverageAt(new Vector3(-40, 0, 0), 16) == 16 && published.CoverageAt(new Vector3(300, 0, 0), 16) == 0,
            "Local uncertainty became a global blackout or destination clearance");
        var unknown = new FoliageCoverageMap(); unknown.Reset(Vector3.zero); unknown.Invalidate();
        published.RetainUnknownFrom(unknown);
        Check(published.CoverageAt(new Vector3(-40, 0, 0), 16) == 0, "Unlocatable pending geometry became known during cancellation");
    }

    private static void NearbyMovement()
    {
        foreach (double distance in new[] { 0d, 2, 24, 95.9 })
            Check(Decide(true, distance, true) == FoliageCatalogueAnchorAction.Keep,
                "Ordinary carrier/camera following restarted a valid active catalogue");
        Check(Decide(true, 96, true) == FoliageCatalogueAnchorAction.Keep &&
            Decide(true, FoliageCoverageMap.Radius - ScenicObservationPolicy.DetailRadius, true) == FoliageCatalogueAnchorAction.RestartScan &&
            Decide(true, 96, false) == FoliageCatalogueAnchorAction.Reanchor &&
            Decide(false, 0, false) == FoliageCatalogueAnchorAction.Reanchor,
            "Initial, running and completed catalogue anchors do not use one boundary");
    }

    private static void SustainedMotion()
    {
        // This capture's longest completed sweep took 8.49256897s. At the
        // supported 12m/s cruise, a blanket 96m restart would interrupt every
        // sweep at eight seconds despite its coverage still being useful.
        const double observedSweepSeconds = 8.492568969726562, speed = 12;
        double origin = 0; int completed = 0;
        for (int scan = 0; scan < 12; scan++)
        {
            for (double time = 0; time < observedSweepSeconds; time += 1d / 60)
                Check(Decide(true, time * speed, true) == FoliageCatalogueAnchorAction.Keep,
                    "Continuous movement restarted a useful sweep before its measured completion");
            origin += observedSweepSeconds * speed; completed++;
            Check(Decide(true, observedSweepSeconds * speed, false) == FoliageCatalogueAnchorAction.Reanchor,
                "Completed useful sweep failed to schedule the next moved anchor");
        }
        Check(completed == 12 && origin > 1000, "Long travel lost catalogue publication progress");
    }

    private static void OverlappingRetarget()
    {
        var published = new FoliageCoverageMap(); published.Reset(Vector3.zero);
        var pending = new FoliageCoverageMap(); pending.Reset(Vector3.zero);
        var nextRegion = new Vector3(210, 0, 0);
        Check(Decide(true, nextRegion.magnitude, true) == FoliageCatalogueAnchorAction.RestartScan &&
            published.CoverageAt(nextRegion, 16) > ScenicObservationPolicy.ForegroundRange, "Fixture does not overlap a previously covered destination");
        pending.AddUnknown(new Bounds(nextRegion + Vector3.one, Vector3.one));
        published.RetainUnknownFrom(pending);
        pending.Reset(Vector3.zero);
        Check(published.CoverageAt(nextRegion, 16) == 0 && published.CoverageAt(new Vector3(-40, 0, 0), 16) == 16,
            "Aborted overlapping refresh revived the missing tree or invalidated unrelated completed observations");
    }
}
