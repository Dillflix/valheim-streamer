using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyRoutePlaybackTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey execution previews cannot advance until the matching first live frame", FirstPresentation);
        yield return ("Journey execution commits only matching fresh proposed captured progress", CaptureIdentity);
        yield return ("Missing or unreadable Journey output pauses the same route without a short-shot deadline", RecoverableOutputLoss);
        yield return ("Physical Journey blockage remains resumable without Scenic replacement ownership", PhysicalRecovery);
        yield return ("Journey holds discard elapsed time and resumed movement cannot catch up", HeldClock);
        yield return ("Journey subsecond sections and finite ending pans use their explicit duration exactly once", ExplicitTiming);
        yield return ("A certified stationary Journey arrival earns live dwell without inventing movement or catch-up time", StationaryArrival);
        yield return ("Journey route acceptance isolates callbacks and motion state across consecutive sections", NewSection);
        yield return ("Journey playback rejects ordinary scenery and invalid clock values", InvalidInputs);
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
