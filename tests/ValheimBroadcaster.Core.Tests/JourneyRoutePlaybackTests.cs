using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyRoutePlaybackTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Pending mission observation retries the original uncommitted pose through the real authority transition", MissionObservationRetry);
        yield return ("Journey execution previews cannot advance until the matching first live frame", FirstPresentation);
        yield return ("Journey execution commits only matching fresh proposed captured progress", CaptureIdentity);
        yield return ("Missing or unreadable Journey output pauses the same route without a short-shot deadline", RecoverableOutputLoss);
        yield return ("Physical Journey blockage remains resumable without Scenic replacement ownership", PhysicalRecovery);
        yield return ("Journey holds discard elapsed time and resumed movement cannot catch up", HeldClock);
        yield return ("Journey subsecond sections and finite ending pans use their explicit duration exactly once", ExplicitTiming);
        yield return ("A certified stationary Journey arrival earns live dwell without inventing movement or catch-up time", StationaryArrival);
        yield return ("Journey route acceptance isolates callbacks and motion state across consecutive sections", NewSection);
        yield return ("Journey playback rejects ordinary scenery and invalid clock values", InvalidInputs);
        yield return ("Retimed Journey bends travel at their selected cruise without a second mission-speed slowdown", SelectedCruiseClock);
        yield return ("Journey cruise metadata survives route transformations and stationary endings", CruiseTransforms);
        yield return ("Journey endpoint completion cannot turn a sub-five-centimetre remainder into a large position jump", TinyEndpointCompletion);
        yield return ("Journey pending geometry keeps one exact proposal across variable frame times and accepted fallback frames", PendingGeometryPose);
        yield return ("Journey pending proposals are cancelled by holds physical pauses and replacement ownership", CancelPendingPose);
    }

    private static AutonomyPosition P(float x, float z = 0) => new AutonomyPosition(x, 30, z);
    private static ScenicShotPlan Section(long id = 20, double duration = 8)
    {
        var feature = new ScenicFeature("coast", ScenicFeatureKind.Coastline, P(40, 20), P(1), 20, 5, .9f);
        return ScenicShotPlan.Spatial(id, feature, ScenicShotRole.Traverse,
            new[] { P(0), P(24), P(48) }, new[] { P(20), P(44), P(68) }, 65)
            .AsJourneyDeparture(7, duration).WithJourneyRamps(false, false);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double value, double expected) => Check(Math.Abs(value - expected) < .000001, $"Expected {expected}, got {value}");
    private static JourneyRoutePlayback Start(ScenicShotPlan? section = null, double at = 0)
    {
        section ??= Section();
        var playback = new JourneyRoutePlayback(); playback.Accept(section, at);
        playback.Observe(section.Id, at, 0, true, true, false);
        return playback;
    }

    private static void MissionObservationRetry()
    {
        var playback = Start();
        var mission = new JourneyTravelController();
        mission.Begin(7, 1, 5, 0, 90, 900);
        mission.Observe(new JourneyTravelObservation(0, 1, 5));
        var route = new JourneyRouteEvidence(playback.Current!.Id, 5, 48);
        Check(mission.TryAcceptRoute(route, 0, out _), "Route was not admitted");
        double wanted = playback.Propose(.02);
        for (int frame = 1; frame <= 12; frame++)
        {
            double at = frame * .02;
            mission.Observe(new JourneyTravelObservation(at, 1, 5,
                navigation: JourneyTravelNavigation.WaitingForObservation, route: route));
            playback.Observe(route.RouteId, at, 0, true, true, false);
            Check(!mission.CanAdvance && playback.TryRetryObservation(mission, 5, false, out double retry)
                && retry == wanted && playback.Progress == 0, "Rechecking the old pose erased pending movement");
        }
        Check(!playback.TryRetryObservation(mission, 6, false, out _) &&
            !playback.TryRetryObservation(mission, 5, true, out _), "Changed scene or operator hold reused pending movement");
        playback.Observe(route.RouteId, .26, wanted, true, true, false);
        Near(playback.Progress, wanted);
        Check(!playback.TryRetryObservation(mission, 5, false, out _), "Consumed preview was issued again");
    }

    private static void SelectedCruiseClock()
    {
        foreach (double selected in new[] { 2d, 6d, 12d })
        foreach (double actual in new[] { selected, selected * .5 })
        {
            var feature = new ScenicFeature("bend", ScenicFeatureKind.Landscape, P(30, 20), P(1), 20, 5, .9f);
            ScenicShotPlan shape = ScenicShotPlan.Spatial(30, feature, ScenicShotRole.Traverse,
                new[] { P(0), P(12), P(24, 6), P(36, 12) },
                new[] { P(20), P(32), P(44, 6), P(56, 12) }, 65, useNativeSpline: false);
            ScenicShotPlan plan = shape.AsJourneyDeparture(7, shape.TravelMetres / selected)
                .WithJourneyCruiseSpeed(selected).WithJourneyRamps(false, false);
            var playback = Start(plan);
            double distance = 0; AutonomyPosition last = plan.Sample(0).CameraPosition;
            for (int frame = 1; frame <= 120; frame++)
            {
                double progress = playback.ProposeAtCruise(1d / 60, actual, 12);
                playback.Observe(plan.Id, frame / 60d, progress, true, true, false);
                AutonomyPosition next = plan.Sample(playback.Progress).CameraPosition;
                distance += last.Distance(next); last = next;
            }
            Check(Math.Abs(distance - actual * 2) < .02, "Selected bend speed was divided by the mission request a second time");
            Near(playback.Progress, actual * 2 / shape.TravelMetres);
            Near(playback.ProposeAtCruise(120, actual, 12, false), playback.Progress);
        }
        // Old fixtures carry zero metadata and preserve their original divisor.
        var legacy = Start(Section(duration: 4));
        Near(legacy.ProposeAtCruise(.1, 6, 12), .0125);
        var slow = Start(Section(duration: 24).WithJourneyCruiseSpeed(2));
        bool rejected = false;
        try { slow.ProposeAtCruise(.1, 3, 12); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Playback silently exceeded its certified angular speed");
    }

    private static void CruiseTransforms()
    {
        ScenicShotPlan plan = Section().WithJourneyCruiseSpeed(6);
        Check(plan.AsStandalone().JourneyCruiseSpeed == 6 && plan.WithIdentity(31).JourneyCruiseSpeed == 6 &&
            plan.WithDuration(10).JourneyCruiseSpeed == 6 && plan.RaiseCamera(1).JourneyCruiseSpeed == 6 &&
            ScenicJourneyEnding.Refuge(plan, 41)!.JourneyCruiseSpeed == 6 &&
            ScenicJourneyEnding.Propose(plan, 42, 1)!.JourneyCruiseSpeed == 6,
            "An immutable route transformation discarded its authored cruise");
        foreach (double invalid in new[] { 0d, 1.9, 12.1, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { plan.WithJourneyCruiseSpeed(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid authored cruise accepted");
        }
    }

    private static void TinyEndpointCompletion()
    {
        foreach (double selected in new[] { 2d, 6d, 12d })
        {
            ScenicShotPlan plan = Section(duration: 48 / selected).WithJourneyCruiseSpeed(selected);
            var playback = Start(plan); double at = 0;
            while (48 * (1 - playback.Progress) > .05)
            {
                at += 1d / 60;
                double proposal = playback.ProposeAtCruise(1d / 60, selected, 12);
                playback.Observe(plan.Id, at, proposal, true, true, false);
            }
            AutonomyPosition before = plan.Sample(playback.Progress).CameraPosition;
            // Mirrors the final endpoint-only completion, which remains
            // capped by Propose's .1s authored-clock limit. It never consumes
            // the remaining duration of a larger route or missing picture.
            double final = playback.Propose(plan.DurationSeconds * (1 - playback.Progress));
            Check(plan.Sample(final).CameraPosition.Distance(before) <= .05001,
                "Endpoint completion moved farther than the small measured remainder");
            playback.Observe(plan.Id, at + 1d / 60, final, true, true, false);
            Near(playback.Progress, 1);
        }
    }

    private static void PendingGeometryPose()
    {
        ScenicShotPlan plan = Section(duration: 8).WithJourneyCruiseSpeed(6);
        var playback = Start(plan);
        double requested = playback.ProposeAtCruise(1d / 60, 6, 12), at = 0;
        AutonomyPosition wanted = plan.Sample(requested).CameraPosition;
        for (int frame = 1; frame <= 240; frame++)
        {
            double dt = frame % 3 == 0 ? .04 : frame % 3 == 1 ? .011 : .025;
            at += dt;
            // Exact geometry is still pending: the preceding accepted pose
            // remains readable and may legitimately render for many frames.
            playback.Observe(plan.Id, at, 0, true, true, false);
            double retry = playback.ProposeAtCruise(dt, 6, 12);
            Near(retry, requested);
            Check(plan.Sample(retry).CameraPosition.Distance(wanted) == 0,
                "Frame-time variation restarted the pending camera query");
        }
        Near(playback.Progress, 0);
        playback.Observe(plan.Id, at + .02, requested, true, true, false);
        Near(playback.Progress, requested);
        double next = playback.ProposeAtCruise(.02, 6, 12);
        Near(next - requested, .02 / plan.DurationSeconds);
        Check(plan.Sample(next).CameraPosition.Distance(wanted) <= .12001,
            "Completing pending work consumed the elapsed wait as catch-up travel");
    }

    private static void CancelPendingPose()
    {
        var playback = Start(); ScenicShotPlan first = playback.Current!;
        double pending = playback.Propose(.1);
        Check(pending > 0, "Fixture did not create a pending proposal");
        Near(playback.Propose(.1, false), 0);
        Near(playback.Propose(.01), .01 / first.DurationSeconds);
        playback.Pause(); playback.Resume();
        Near(playback.Propose(.02), .02 / first.DurationSeconds);
        playback.Observe(first.Id, .1, 0, true, true, false, held: true);
        Near(playback.Propose(.03), .03 / first.DurationSeconds);
        var replacement = Section(21, 4);
        playback.Accept(replacement, 1);
        playback.Observe(first.Id, 1.01, pending, true, true, false);
        Near(playback.Propose(.1), 0);
        playback.Observe(replacement.Id, 1.02, 0, true, true, false);
        Near(playback.Propose(.01), .01 / replacement.DurationSeconds);
        // Zero cruise is an explicit stop; a zero-time observation retry at
        // nonzero cruise is not. Only the former withdraws pending movement.
        Near(playback.ProposeAtCruise(.1, 0, 6), 0);
        double resumed = playback.ProposeAtCruise(.02, 6, 6);
        Near(resumed, .02 / replacement.DurationSeconds);
        Near(playback.ProposeAtCruise(0, 6, 6), resumed);
    }

    private static void FirstPresentation()
    {
        var playback = new JourneyRoutePlayback(); var section = Section(); playback.Accept(section, 10);
        Near(playback.Propose(.1), 0);
        playback.Observe(section.Id + 1, 10, 0, true, true, false);
        Check(!playback.HasPresented, "An unrelated camera frame launched the route");
        playback.Observe(section.Id, 10, 0, true, true, false);
        double proposal = playback.Propose(.1);
        Check(proposal > 0, "Matching live first frame did not release the route clock");
        for (int i = 0; i < 100; i++) Near(playback.Propose(.1), proposal);
        Near(playback.Progress, 0); Near(playback.ElapsedSeconds, 0);
        playback.Observe(section.Id, 10.1, proposal, true, true, false);
        Near(playback.Progress, proposal); Near(playback.ElapsedSeconds, .1);
    }

    private static void CaptureIdentity()
    {
        var playback = Start(); long id = playback.Current!.Id;
        double proposed = playback.Propose(.1);
        playback.Observe(id, .1, .9, true, true, false);
        Near(playback.Progress, 0);
        playback.Observe(id + 1, .1, proposed, true, true, false);
        Near(playback.Progress, 0);
        playback.Observe(id, .1, proposed, true, true, false);
        Near(playback.Progress, proposed);
        double next = playback.Propose(.1);
        playback.Observe(id, .1, next, true, true, false);
        playback.Observe(id, .05, next, true, true, false);
        Near(playback.Progress, proposed);
        playback.Observe(id, .2, 0, true, true, false);
        Near(playback.Progress, proposed);
        playback.Observe(id, .2, next, true, true, false);
        Near(playback.Progress, next);
    }

    private static void RecoverableOutputLoss()
    {
        foreach (bool pictureExists in new[] { false, true })
        {
            var playback = Start(); ScenicShotPlan plan = playback.Current!;
            double at = .1, proposed = playback.Propose(.1);
            playback.Observe(plan.Id, at, proposed, true, true, false);
            double preserved = playback.Progress, shown = playback.ElapsedSeconds;
            for (int i = 0; i < 100; i++)
            {
                at += .1; playback.Observe(plan.Id, at, preserved, pictureExists, false, false);
                Near(playback.Propose(.1), preserved);
                Check(ReferenceEquals(plan, playback.Current) && !playback.PhysicalBlocked,
                    "Output loss replaced a route or became a physical obstruction");
            }
            Near(playback.Progress, preserved); Near(playback.ElapsedSeconds, shown);
            at += .1; playback.Observe(plan.Id, at, preserved, true, true, false);
            Near(playback.ElapsedSeconds, shown);
            Check(playback.PictureAvailable && playback.HasPresented && playback.Propose(.1) > preserved,
                "Fresh live output could not resume the existing Journey section");
        }
    }

    private static void PhysicalRecovery()
    {
        var playback = Start(); ScenicShotPlan plan = playback.Current!;
        for (int i = 1; i <= 50; i++) playback.Observe(plan.Id, i * .1, 0, true, true, true);
        Check(playback.PhysicalBlocked && playback.BlockedSeconds > 4 && ReferenceEquals(playback.Current, plan),
            "Physical blockage lost its resumable owner after the ordinary two-second deadline");
        Near(playback.Propose(.1), 0);
        playback.Observe(plan.Id, 5.1, 0, false, false, false);
        Check(playback.PhysicalBlocked, "Missing evidence cleared physical blockage");
        playback.Resume();
        Check(!playback.PhysicalBlocked, "Explicit fresh-validation resume did not release physical pause");
        Near(playback.BlockedSeconds, 0);
        Near(playback.Propose(.1), 0); // Current picture still needs reacquisition.
        playback.Observe(plan.Id, 5.2, 0, true, true, false);
        Check(playback.Propose(.1) > 0, "Recovered section never resumed travel");
        playback.Pause();
        Check(playback.PhysicalBlocked, "Explicit validation pause was ignored");
        playback.Observe(plan.Id, 5.3, 0, true, true, false);
        Check(!playback.PhysicalBlocked && playback.Propose(.1) > 0,
            "A fresh readable unblocked observation could not recover physical pause");
    }

    private static void HeldClock()
    {
        var playback = Start(); long id = playback.Current!.Id;
        double preview = playback.Propose(.1);
        playback.Observe(id, .1, preview, true, true, false, held: true);
        Near(playback.Progress, 0); Near(playback.ElapsedSeconds, 0);
        Near(playback.Propose(1000, canMove: false), 0);
        playback.Observe(id, 1000, 0, true, true, false, held: true);
        playback.Observe(id, 1000.1, 0, true, true, false);
        Near(playback.ElapsedSeconds, 0);
        Near(playback.Propose(1000), .1 / playback.Current.DurationSeconds);
    }

    private static void ExplicitTiming()
    {
        foreach (int fps in new[] { 30, 60 })
        {
            var shortSection = Section(duration: .4); var playback = Start(shortSection);
            double at = 0;
            for (int i = 0; i < fps * .4; i++)
            {
                at += 1d / fps;
                playback.Observe(shortSection.Id, at, playback.Propose(1d / fps), true, true, false);
            }
            Near(playback.Progress, 1);
            var ending = ScenicJourneyEnding.Propose(shortSection, 21, 1)!;
            playback.Accept(ending, at); playback.Observe(ending.Id, at, 0, true, true, false);
            for (int i = 0; i < fps * ending.DurationSeconds; i++)
            {
                at += 1d / fps;
                playback.Observe(ending.Id, at, playback.Propose(1d / fps), true, true, false);
            }
            Near(playback.Progress, 1);
            Check(ReferenceEquals(playback.Current, ending) && !playback.PhysicalBlocked,
                "The execution clock started an editorial replacement at the ending");
            Near(playback.Propose(.1), 1); // Controller owns the immediate handoff.
        }
    }

    private static void NewSection()
    {
        var playback = Start(); ScenicShotPlan old = playback.Current!;
        playback.Pause();
        var next = Section(21); playback.Accept(next, 10);
        playback.Observe(old.Id, 11, 0, true, true, false);
        playback.Observe(next.Id, 9, 0, true, true, false);
        Check(!playback.HasPresented && !playback.PhysicalBlocked && !playback.PictureAvailable,
            "A new section inherited old presentation, old blockage or an earlier callback");
        Near(playback.Progress, 0); Near(playback.BlockedSeconds, 0); Near(playback.ElapsedSeconds, 0);
        playback.Observe(next.Id, 10, 0, true, true, false);
        Check(playback.HasPresented, "The current first frame was rejected after old callbacks");
        playback.Reset();
        Check(playback.Current == null && !playback.HasPresented && !playback.PictureAvailable && !playback.PhysicalBlocked,
            "Reset retained Journey ownership");
    }

    private static void StationaryArrival()
    {
        foreach (int fps in new[] { 30, 60 })
        {
            ScenicShotPlan section = Section(); ScenicShotSample end = section.Sample(1);
            ScenicShotPlan arrival = ScenicShotPlan.Picture(30, section.Feature!, ScenicShotRole.Establish,
                end.CameraPosition, end.AimPosition, section.FieldOfView, end.ScreenX, end.ScreenY).WithDuration(6);
            var playback = Start(arrival, 10);
            double at = 10;
            for (int i = 0; i < fps * .6; i++)
            {
                Near(playback.Propose(1d / fps), 0);
                at += 1d / fps;
                playback.Observe(arrival.Id, at, 0, true, true, false);
            }
            Near(playback.ElapsedSeconds, .6); Near(playback.Progress, 0);
            playback.Observe(arrival.Id, at + .1, .5, true, true, false);
            Near(playback.ElapsedSeconds, .6); Near(playback.Progress, 0);
            playback.Observe(arrival.Id, at + .1, 0, true, true, false, held: true);
            playback.Observe(arrival.Id, 1000, 0, false, false, false);
            Near(playback.Propose(1000), 0);
            playback.Observe(arrival.Id, at = 1000.1, 0, true, true, false);
            Near(playback.ElapsedSeconds, .6);
            for (int i = 0; i < fps * .2; i++)
            {
                at += 1d / fps;
                playback.Observe(arrival.Id, at, playback.Propose(1d / fps), true, true, false);
            }
            Near(playback.ElapsedSeconds, .8); Near(playback.Progress, 0);
            Near(arrival.TravelMetres, 0);
            Near(arrival.Sample(playback.Progress).CameraPosition.Distance(end.CameraPosition), 0);
            Near(arrival.Sample(playback.Progress).AimPosition.Distance(end.AimPosition), 0);
            Check(playback.HasPresented && playback.PictureAvailable && !playback.PhysicalBlocked &&
                ReferenceEquals(playback.Current, arrival), "Stationary arrival lost its validated picture owner");
        }
    }

    private static void InvalidInputs()
    {
        var playback = new JourneyRoutePlayback();
        bool rejected = false;
        try { playback.Accept(Section().AsStandalone(), 0); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Ordinary Scenic travel accidentally entered the Journey executor");
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            rejected = false;
            try { playback.Propose(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid clock entered Journey playback");
        }
        rejected = false;
        try { playback.Observe(1, 0, 1.01, true, true, false); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid captured progress was accepted");
    }
}
