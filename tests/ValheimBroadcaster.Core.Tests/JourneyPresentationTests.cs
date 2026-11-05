using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyPresentationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey consumes the preceding clean render while the current output frame is not rendered", PreviousRender);
        yield return ("Journey keeps the applied scene identity when frame-local camera context expires at the next Update", FrameScopedScene);
        yield return ("Recorded 0.8.47 movement earns distance through the retained production scene identity", RecordedSceneHandoff);
        yield return ("Recorded 0.8.41 departure poses earn actual distance through the production presentation gate", RecordedDeparture);
        yield return ("Sixty-fps render then Update ordering credits travel once using the captured clock", FrameLoop);
        yield return ("A missing production capture breaks credit across even a single unreadable frame", MissingCaptureGap);
        yield return ("Held stale missing and reordered capture sources cannot earn journey progress", SourceLifetime);
        yield return ("Journey capture requires matching generation camera shot revision and scene", CaptureIdentity);
        yield return ("A late camera override or incomplete audit cannot earn validated journey distance", InvalidAudit);
        yield return ("World boundaries reject prior capture and admit a fresh generation", Boundary);
        yield return ("Invalid captured progress and clocks do not consume a subsequent valid frame", InvalidTiming);
    }

    private const int Camera = 868704;
    private const long Scene = 1000000004;
    private static CameraProjectionSnapshot Projection => new CameraProjectionSnapshot(
        .865372, 0, 0, 0, 0, 1.5398649, 0, 0, 0, 0, -1.00005, -1.000025, 0, 0, -1, 0);
    private static CameraRenderPose Pose(double x, double y = 34.5, double z = 605.4147338867188) =>
        new CameraRenderPose(x, y, z, -.01863187, -.75333315, -.03028052, .65667737, 66, Projection);
    private static JourneyPresentationSample Sample(int frame = 4446, double at = 89.13847351074219,
        double progress = .01, double x = -1614.5963134765625, long generation = 3, long shot = 20,
        long revision = 1, long scene = Scene, int camera = Camera, bool matched = true,
        double y = 34.5, double z = 605.4147338867188, int frameOffset = 0)
    {
        var pose = Pose(x, y, z);
        var audit = new CameraRenderAudit(generation, shot, revision, scene, 0, camera,
            frame + frameOffset, frame, frame, pose, pose, pose, pose, matched, generation, camera);
        return new JourneyPresentationSample(audit, at, progress);
    }
    private static bool Take(JourneyPresentationPolicy policy, JourneyPresentationSample sample,
        out JourneyPresentationSample accepted, FramePresentation mode = FramePresentation.Live,
        int frameDelta = 1, double age = 1d / 60, long generation = 3, long shot = 20,
        long revision = 1, long scene = Scene, int camera = Camera, int sourceOffset = 0, int expectedOffset = 0) =>
        policy.TryTake(sample, mode, sample.Audit.CaptureFrame, sample.Audit.CaptureFrame + sourceOffset,
            sample.Audit.CaptureFrame + frameDelta, sample.Time + age, generation,
            new JourneyPresentationIdentity(shot, revision, scene, sample.Audit.CaptureFrame + expectedOffset),
            camera, out accepted);
    private static void Check(bool condition, string reason = "Journey presentation assertion failed")
    { if (!condition) throw new Exception(reason); }
    private static void Near(double actual, double expected, double tolerance = .00001) =>
        Check(Math.Abs(actual - expected) <= tolerance, $"Expected {actual} near {expected}");
    private static AutonomyPosition P(float x) => new AutonomyPosition(x, 34.5f, 605.4147f);
    private static ScenicShotPlan Plan() => ScenicShotPlan.Spatial(20,
        new ScenicFeature("coast", ScenicFeatureKind.Coastline, P(0), new AutonomyPosition(1, 0, 0), 20, 5, 1),
        ScenicShotRole.Traverse, new[] { P(0), P(24), P(48) }, new[] { P(10), P(34), P(58) }, 66)
        .AsJourney(19, 11, 8);
    private static ScenicJourneyPolicy Journey(float start = 0)
    {
        var policy = new ScenicJourneyPolicy();
        policy.Start(19, 1000, 6, P(start), new AutonomyPosition(1, 0, 0));
        return policy;
    }

    private static void PreviousRender()
    {
        JourneyPresentationSample captured = Sample();
        var output = new ProgramFramePolicy();
        output.Rendered(captured.Time, captured.Audit.CaptureFrame, FramePresentation.Live);
        Check(output.Select(captured.Time + 1d / 60, captured.Audit.CaptureFrame + 1, true, false, true)
            .Presentation == FramePresentation.Slate, "The encoder's current-frame guard must remain strict");
        var policy = new JourneyPresentationPolicy();
        Check(Take(policy, captured, out var accepted), "Previous render was rejected during next Update");
        Near(accepted.Time, captured.Time); Near(accepted.Progress, .01); Near(accepted.Position.X, captured.Position.X);
        Check(!Take(policy, captured, out _), "Repeated Update credited the same capture twice");
    }

    private static void RecordedDeparture()
    {
        // Reduced numeric evidence from the supplied 0.8.41 Journey 19 capture.
        // The renderer ran at 60fps; diagnostic samples retained every fifth frame.
        var frames = new[] {
            Sample(4446, 89.13847351074219, .01, -1614.5963134765625),
            Sample(4451, 89.22180938720703, .02, -1614.560546875, y: 34.49989700317383, z: 605.4146728515625),
            Sample(4456, 89.3053207397461, .03, -1614.4610595703125, y: 34.498809814453125, z: 605.4146728515625),
            Sample(4461, 89.38865661621094, .04, -1614.2789306640625, y: 34.49443435668945, z: 605.4146728515625),
            Sample(4466, 89.47199249267578, .05, -1614.00537109375, y: 34.48435974121094, z: 605.4146118164062)
        };
        var policy = new JourneyPresentationPolicy(); var journey = Journey(-1614.5963f); var plan = Plan();
        double distance = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            Check(Take(policy, frames[i], out var accepted));
            journey.Observe(plan, accepted.Time, accepted.Progress, true, false, accepted.Position);
            if (i > 0) distance += frames[i - 1].Position.Distance(frames[i].Position);
        }
        Check(distance > .59 && journey.TravelMetres > .59, "Recorded visible departure still earns zero distance");
        Near(journey.TravelMetres, distance); Near(journey.ShownSeconds, frames[4].Time - frames[0].Time);
    }
    private static void FrameScopedScene()
    {
        const int rendered = 4446;
        long FrameLocalContext(int frame) => frame == rendered ? Scene : 0;
        var capture = Sample(frame: rendered);
        // This is the same immutable solve-time handoff used by the plugin and
        // FrameHold. The scene getter has expired before Update consumes render.
        var applied = new JourneyPresentationIdentity(20, 1, FrameLocalContext(rendered), rendered);
        var expired = new JourneyPresentationIdentity(20, 1, FrameLocalContext(rendered + 1), rendered);
        var policy = new JourneyPresentationPolicy();
        bool Consume(JourneyPresentationSample sample, JourneyPresentationIdentity identity) =>
            policy.TryTake(sample, FramePresentation.Live, sample.Audit.CaptureFrame,
                sample.Audit.CaptureFrame, sample.Audit.CaptureFrame + 1, sample.Time + 1d / 60,
                3, identity, Camera, out _);
        Check(!Consume(capture, expired),
            "An absent next-frame context must not be substituted for the captured scene");
        Check(!Consume(capture, default), "A reset camera solve must not authorize a capture");
        Check(Consume(capture, applied),
            "Retained render identity should admit the preceding matched capture");
        Check(!Consume(capture, applied),
            "Latching scene identity must preserve once-only capture consumption");
        var next = Sample(frame: rendered + 1, at: capture.Time + 1d / 60, scene: Scene + 1);
        Check(!Consume(next, applied), "The previous solve must not authorize a new capture");
        Check(!Consume(next, new JourneyPresentationIdentity(20, 1, Scene, rendered + 1)),
            "The previous scene must not authorize a new scene's capture");
        Check(Consume(next, new JourneyPresentationIdentity(20, 1, Scene + 1, rendered + 1)));
    }

    private static void RecordedSceneHandoff()
    {
        // Reduced poses and render times from Journey 63 in the supplied 0.8.47
        // capture. Both source audits matched, but the frame-local scene lookup
        // in the following Update returned 0 and rejected all travel credit.
        var frames = new[] {
            Sample(9052, 166.01690673828125, .01, -2410.203125, shot: 64,
                y: 36.5, z: -1671.54296875),
            Sample(9057, 166.10023498535156, .02, -2410.1416015625, shot: 64,
                y: 36.4996337890625, z: -1671.427734375)
        };
        var journey = new ScenicJourneyPolicy(); var start = frames[0].Position;
        journey.Start(63, 5000, 12, start, new AutonomyPosition(1, 0, 0));
        AutonomyPosition Offset(float x) => new AutonomyPosition(start.X + x, start.Y, start.Z);
        var plan = ScenicShotPlan.Spatial(64,
            new ScenicFeature("coast", ScenicFeatureKind.Coastline, start, new AutonomyPosition(1, 0, 0), 20, 5, 1),
            ScenicShotRole.Traverse, new[] { start, Offset(24), Offset(48) },
            new[] { Offset(10), Offset(34), Offset(58) }, 75).AsJourney(63, 11, 8);
        var gate = new JourneyPresentationPolicy();
        foreach (var frame in frames)
        {
            var applied = new JourneyPresentationIdentity(64, 1, Scene, frame.Audit.CaptureFrame);
            var expired = new JourneyPresentationIdentity(64, 1, 0, frame.Audit.CaptureFrame);
            bool Consume(JourneyPresentationIdentity identity, out JourneyPresentationSample accepted) =>
                gate.TryTake(frame, FramePresentation.Live, frame.Audit.CaptureFrame, frame.Audit.CaptureFrame,
                    frame.Audit.CaptureFrame + 1, frame.Time + 1d / 60, 3, identity, Camera, out accepted);
            Check(!Consume(expired, out _), "The original expired-context handoff unexpectedly earned credit");
            Check(Consume(applied, out var accepted), "The production identity snapshot rejected the matched render");
            journey.Observe(plan, accepted.Time, accepted.Progress, true, false, accepted.Position);
        }
        Near(journey.TravelMetres, frames[0].Position.Distance(frames[1].Position));
        Check(journey.TravelMetres > .13, "The captured moving picture still earned zero distance");
        Near(journey.ShownSeconds, frames[1].Time - frames[0].Time);
    }

    private static void FrameLoop()
    {
        var gate = new JourneyPresentationPolicy(); var journey = Journey(); var plan = Plan();
        for (int i = 0; i <= 480; i++)
        {
            var source = Sample(i + 100, 10 + i / 60d, i / 480d, i * .1);
            Check(Take(gate, source, out var accepted));
            journey.Observe(plan, accepted.Time, accepted.Progress, true, false, accepted.Position);
            Check(!Take(gate, source, out _), "Repeated consumption introduced fictional shown time");
        }
        Near(journey.TravelMetres, 48, .0001); Near(journey.ShownSeconds, 8);
        Near(journey.AchievedMetresPerSecond, 6, .0001);
    }

    private static void MissingCaptureGap()
    {
        var gate = new JourneyPresentationPolicy(); var journey = Journey(); var plan = Plan();
        bool Observe(int frame, double position, FramePresentation mode)
        {
            var source = Sample(frame, 10 + (frame - 100) / 60d, (frame - 100) / 480d, position);
            if (!Take(gate, source, out var accepted, mode))
            { journey.BreakPresentation(); return false; }
            journey.Observe(plan, accepted.Time, accepted.Progress, true, false, accepted.Position);
            return true;
        }
        Check(Observe(100, 0, FramePresentation.Live));
        Check(Observe(101, .1, FramePresentation.Live));
        Check(!Observe(102, .5, FramePresentation.Slate));
        Check(Observe(103, .6, FramePresentation.Live));
        Near(journey.TravelMetres, .1); Near(journey.ShownSeconds, 1d / 60);
        Check(Observe(104, .7, FramePresentation.Live));
        Near(journey.TravelMetres, .2); Near(journey.ShownSeconds, 2d / 60);
        Check(journey.Active, "Missing evidence fabricated a relocation or interrupted later valid travel");
    }

    private static void SourceLifetime()
    {
        var gate = new JourneyPresentationPolicy(); var source = Sample();
        foreach (var mode in new[] { FramePresentation.Hold, FramePresentation.Slate })
            Check(!Take(gate, source, out _, mode));
        Check(!Take(gate, source, out _, sourceOffset: -1), "Held source frame was reattributed to newer render");
        Check(!Take(gate, source, out _, frameDelta: 2), "Capture older than the preceding render was accepted");
        Check(!Take(gate, source, out _, frameDelta: -1), "Future render was accepted");
        Check(!Take(gate, source, out _, age: .251), "A long stalled frame earned continuous travel");
        Check(Take(gate, source, out _));
        Check(!Take(gate, Sample(frame: 4445), out _), "Reordered old render was accepted");
    }

    private static void CaptureIdentity()
    {
        var gate = new JourneyPresentationPolicy(); var source = Sample();
        Check(!Take(gate, source, out _, generation: 4));
        Check(!Take(gate, source, out _, shot: 21));
        Check(!Take(gate, source, out _, revision: 2));
        Check(!Take(gate, source, out _, scene: Scene + 1));
        Check(!Take(gate, source, out _, camera: Camera + 1));
        Check(!Take(gate, source, out _, expectedOffset: 1));
        Check(Take(gate, source, out _), "Rejected mismatched observations consumed valid evidence");
    }

    private static void InvalidAudit()
    {
        var gate = new JourneyPresentationPolicy();
        Check(!Take(gate, Sample(matched: false), out _));
        Check(!Take(gate, Sample(frameOffset: -1), out _));
        var pose = Pose(0); var moved = Pose(20);
        var changed = new CameraRenderAudit(3, 20, 1, Scene, 0, Camera, 8, 8, 8,
            pose, pose, moved, moved, true, 3, Camera);
        Check(!Take(gate, new JourneyPresentationSample(changed, 1, .2), out _), "Unvalidated late pose earned movement");
        Check(!Take(gate, Sample(x: double.NaN), out _));
        Check(!Take(gate, Sample(x: 20001), out _));
        Check(!Take(gate, default, out _));
    }

    private static void Boundary()
    {
        var gate = new JourneyPresentationPolicy(); Check(Take(gate, Sample(), out _));
        Check(!Take(gate, Sample(), out _, generation: 4));
        Check(Take(gate, Sample(generation: 4), out _, generation: 4), "Fresh boundary capture was suppressed by old frame identity");
        Check(!Take(gate, Sample(), out _, generation: 4));
        gate.Reset(); Check(Take(gate, Sample(generation: 5), out _, generation: 5));
    }

    private static void InvalidTiming()
    {
        var gate = new JourneyPresentationPolicy();
        foreach (double value in new[] { -.1, 1.1, double.NaN, double.PositiveInfinity })
            Check(!Take(gate, Sample(progress: value), out _));
        Check(!Take(gate, Sample(), out _, age: -.01));
        Check(!Take(gate, Sample(at: double.NaN), out _));
        Check(!Take(gate, Sample(at: -1), out _));
        Check(Take(gate, Sample(), out _));
    }
}
