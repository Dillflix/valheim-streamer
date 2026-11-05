using System;

namespace ValheimBroadcaster.Core;

/// <summary>A measured readable prefix is a new proposal, never a reusable
/// certificate. Its complete regenerated native geometry must be checked again.</summary>
public static class JourneyReadablePrefix
{
    public static bool TryRestore(ScenicShotPlan original, ScenicShotPlan sliced,
        double observedStart, double observedMetres, out ScenicShotPlan? proposal)
    {
        proposal = null;
        if (original == null || sliced == null || !original.IsJourney || !original.IsSpatial || !sliced.IsSpatial ||
            double.IsNaN(observedStart) || Math.Abs(observedStart) > .000001 ||
            double.IsNaN(observedMetres) || double.IsInfinity(observedMetres) || observedMetres < ScenicJourneyPolicy.MinimumWindowMetres ||
            sliced.TravelMetres < ScenicJourneyPolicy.MinimumWindowMetres || sliced.TravelMetres >= original.TravelMetres - .05 ||
            sliced.WaypointCount < 3 || sliced.FieldOfView != original.FieldOfView) return false;
        var before = original.Sample(0); var after = sliced.Sample(0);
        if (before.CameraPosition.Distance(after.CameraPosition) > .005 || before.AimPosition.Distance(after.AimPosition) > .005 ||
            before.ScreenX != after.ScreenX || before.ScreenY != after.ScreenY ||
            original.CameraWaypoint(1).Distance(sliced.CameraWaypoint(1)) > .005 ||
            original.AimWaypoint(1).Distance(sliced.AimWaypoint(1)) > .005) return false;
        double duration = Math.Max(.1, original.DurationSeconds * sliced.TravelMetres / original.TravelMetres);
        proposal = (original.ContinuationOf == 0 ? sliced.AsJourneyDeparture(original.JourneyId, duration) :
            sliced.AsJourney(original.JourneyId, original.ContinuationOf, duration))
            .WithJourneyRamps(original.JourneyEaseIn, original.JourneyEaseOut).WithJourneyView(original.UsesJourneyView);
        if (original.JourneyCruiseSpeed > 0) proposal = proposal.WithJourneyCruiseSpeed(original.JourneyCruiseSpeed);
        return true;
    }
}
