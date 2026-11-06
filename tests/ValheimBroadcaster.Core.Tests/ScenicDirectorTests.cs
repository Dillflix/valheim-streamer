using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicDirectorTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Directed scenic paths keep validated geometry and lens immutable", ImmutableRoute);
        yield return ("Directed scenic duration follows route and visual development", UsefulDuration);
        yield return ("Directed scenic curves retain independently authored aim", CurvedAim);
        yield return ("Trimming scenery restricts its route to the useful prefix", TrimPrefix);
        yield return ("Directed scene input validates finite meaningful measured subjects", Validation);
        yield return ("Scenic proposals never advance accepted progress", ProposalIsReadOnly);
        yield return ("Only matching live readable scenery commits presented progress", PresentationCommit);
        yield return ("Scenic output gaps and duplicate observations earn no time", ObservationGaps);
        yield return ("Rejected scenic movement leads to bounded local replacement", BlockedReplacement);
        yield return ("Manual scenic hold pauses both novelty and blockage timers", ManualHold);
        yield return ("Scenic still comparison retains normal finite shot lifetime", StaticComparison);
        yield return ("Scenic prefetch prepares a replacement without activating it", PreparedShot);
        yield return ("Scenic shot progression never repays an interruption", NoCatchUp);
        yield return ("Actual scenic footage earns sequence beats, candidate search does not", SequenceEvidence);
        yield return ("Scenic ranking rewards distinct views and retires exhausted subjects", ViewMemory);
        yield return ("Scenic fallback ranks a different rock setting above another nearby establishing view", RockSetting);
        yield return ("Scenic memory remains bounded and resets across worlds", BoundedHistory);
        yield return ("Missing or unreadable scenic output requests bounded replacement", PictureFailure);
        yield return ("Scenic rejected progress never revokes already accepted progress", MonotonicProgress);
        yield return ("Low frame rate cannot suspend scenic recovery deadlines", LowRateRecovery);
        yield return ("Long observation gaps reset uncertain scenic failure persistence", RecoveryGap);
        yield return ("Completed scenic episodes cannot win again through score fallback", EpisodeAdmission);
        yield return ("Equivalent scenic views cannot be relabeled as fresh sequence beats", EquivalentAdmission);
        yield return ("Measured repeated scenic arcs cannot bypass novelty through a new survey identity", CrossIdentityArcAdmission);
        yield return ("World-composition memory preserves different places scales and camera paths", DistinctWorldComposition);
        yield return ("World-composition memory requires shown footage and never vetoes recovery pictures", WorldCompositionEvidence);
        yield return ("A blocked scenic route can admit a materially different recovery path", RecoveryAdmission);
        yield return ("Scenic episode memory survives unrelated short-history turnover", EpisodeRetention);
        yield return ("Recovered matching scenery cancels obsolete missing-output replacement", RecoveredMissingPicture);
        yield return ("Recovered readable scenery resumes instead of cutting to a prepared replacement", RecoveredUnreadablePicture);
        yield return ("Picture recovery preserves blocked completed and operator-requested edits", DurableReplacementRequests);
        yield return ("Editorial scenic edits retain validated movement and the prepared successor", EditorialMovement);
        yield return ("Editorial scenic edits still escalate blocked and completed routes", EditorialMotionFailure);
        yield return ("Editorial intent survives transient picture recovery without restarting failed motion", EditorialPictureRecovery);
    }

    private static AutonomyPosition P(float x = 0, float y = 20, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicFeature Feature(string id = "ridge") => new ScenicFeature(id, ScenicFeatureKind.Ridge,
        P(), P(1, 0, 0), 10, 8, .8f);
    private static ScenicShotPlan Plan(long id = 1, string feature = "ridge", ScenicShotRole role = ScenicShotRole.Establish,
        float distance = 24, bool curve = false) => ScenicShotPlan.Directed(id, Feature(feature), role,
            P(-8, 26, -distance), P(8, 26, -distance), P(), P(2), 55, -.1f, -.08f,
            curve ? P(0, 30, -distance - 6) : (AutonomyPosition?)null);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double a, double b, string message) => Check(Math.Abs(a - b) < .0001, message);
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid input was accepted."); }
    private static void Observe(ScenicDirector d, int first, int last, bool live = true, bool readable = true,
        bool blocked = false, bool held = false, bool movement = true)
    { for (int i = first; i <= last; i++) d.Observe(d.Current!.Id, i / 10d, d.Progress, live, readable, blocked, held, movement); }

    private static void ImmutableRoute()
    {
        var plan = Plan(curve: true);
        Check(plan.IsDirected && plan.FeatureId == "ridge" && plan.Feature != null && plan.Feature.Kind == ScenicFeatureKind.Ridge && plan.FieldOfView == 55,
            "Directed metadata or lens was not retained.");
        for (int i = 0; i <= 20; i++)
        {
            double p = i / 20d;
            Check(plan.Sample(p, .2f).CameraPosition.Distance(plan.Sample(p, 1.5f).CameraPosition) < .001,
                "Motion strength reshaped an already validated directed route.");
            Check(plan.Sample(p, .2f).AimPosition.Distance(plan.Sample(p, 1.5f).AimPosition) < .001,
                "Motion strength reshaped the environmental aim route.");
        }
        Check(plan.Sample(1, 0).CameraPosition.Distance(plan.Sample(0).CameraPosition) < .001,
            "Explicit zero-motion sampling comparison did not retain the initial pose.");
    }
    private static void UsefulDuration()
    {
        var plan = Plan();
        Check(plan.DurationSeconds >= 8 && plan.DurationSeconds <= 20 && plan.VisualChange > .25,
            "A useful route was stretched to the legacy blanket dwell or has no planned screen development.");
        Check(plan.Sample(2 / plan.DurationSeconds).CameraPosition.Distance(plan.Sample(0).CameraPosition) > 1,
            "The directed move has an excessively static opening.");
        var shortPlan = ScenicShotPlan.Directed(2, Feature(), ScenicShotRole.Traverse,
            P(-2, 26, -24), P(2, 26, -24), P(), P(), 55);
        Check(shortPlan.DurationSeconds < plan.DurationSeconds,
            "Shorter routes did not receive shorter shots.");
        var near = ScenicShotPlan.Directed(3, Feature(), ScenicShotRole.Traverse,
            P(-8, 24, -12), P(8, 24, -12), P(), P(), 55);
        Check(near.VisualChange > plan.VisualChange,
            "Measured visual development ignores the subject's viewing distance.");
    }
    private static void CurvedAim()
    {
        var plan = Plan(curve: true);
        var middle = plan.Sample(.5);
        Check(middle.CameraPosition.Y > 27 && middle.CameraPosition.Z < -26,
            "Authored camera control point was ignored.");
        Check(middle.AimPosition.Distance(P(1)) < .001,
            "The scenic aim copied the body curve instead of its authored subject trajectory.");
        double largest = 0;
        var previous = plan.Sample(0);
        for (int i = 1; i <= 1200; i++)
        {
            var sample = plan.Sample(i / 1200d);
            largest = Math.Max(largest, previous.CameraPosition.Distance(sample.CameraPosition)); previous = sample;
        }
        Check(largest < .04, "Directed curve has a position discontinuity.");
    }
    private static void TrimPrefix()
    {
        var plan = Plan(curve: true);
        var prefix = plan.Trim(.65);
        Check(prefix.Id == plan.Id && prefix.FeatureId == plan.FeatureId && prefix.FieldOfView == plan.FieldOfView,
            "Prefix changed shot identity or lens.");
        Check(prefix.Sample(0).CameraPosition.Distance(plan.Sample(0).CameraPosition) < .001 &&
            prefix.Sample(1).CameraPosition.Distance(plan.Sample(.65).CameraPosition) < .001 &&
            prefix.Sample(1).AimPosition.Distance(plan.Sample(.65).AimPosition) < .001,
            "Prefix did not preserve original route endpoints.");
        Check(prefix.TravelMetres < plan.TravelMetres && prefix.DurationSeconds <= plan.DurationSeconds,
            "A shorter useful route retained the original route length or expanded duration.");
    }
    private static void Validation()
    {
        Reject(() => new ScenicFeature("", ScenicFeatureKind.Ridge, P(), P(1, 0, 0), 10, 2, .8f));
        Reject(() => new ScenicFeature("x", ScenicFeatureKind.Ridge, P(), P(0, 1, 0), 10, 2, .8f));
        Reject(() => new ScenicFeature("x", ScenicFeatureKind.Ridge, P(), P(1, 0, 0), float.NaN, 2, .8f));
        Reject(() => ScenicShotPlan.Directed(1, Feature(), ScenicShotRole.Detail, P(0, 20, -10), P(0, 20, -10), P(), P(), 55));
        Reject(() => ScenicShotPlan.Directed(1, Feature(), ScenicShotRole.Detail, P(-4, 20, 0), P(4, 20, 0), P(), P(), 55));
        Reject(() => Plan().Trim(0));
        Reject(() => Plan().Trim(double.NaN));
        var d = new ScenicDirector(); Reject(() => d.Propose(-1)); Reject(() => d.Propose(.1, true, float.PositiveInfinity));
    }
    private static void ProposalIsReadOnly()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0);
        double proposal = d.Propose(.1);
        for (int i = 0; i < 50; i++) Near(d.Propose(.1), proposal, "Repeated preview changed proposed progress.");
        Near(d.Progress, 0, "A preview committed unrendered camera progress.");
        Near(d.Propose(.1, false), 0, "Paused scenic move proposed advancement.");
    }
    private static void PresentationCommit()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0);
        d.Observe(2, 0, .1, true, true, false); Near(d.Progress, 0, "Another plan's frame advanced scenery.");
        d.Observe(1, 0, .1, false, true, false); Near(d.Progress, 0, "Held texture advanced scenery.");
        d.Observe(1, .1, .1, true, false, false); Near(d.Progress, 0, "Unreadable output advanced scenery.");
        d.Observe(1, .2, .1, true, true, false); Near(d.Progress, .1, "Actual useful frame did not commit.");
    }
    private static void ObservationGaps()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); d.Observe(1, 0, 0, true, true, false);
        d.Observe(1, .1, .01, true, true, false); Near(d.ElapsedSeconds, .1, "Continuous output lost time.");
        d.Observe(1, .1, .02, true, true, false); Near(d.Progress, .01, "Duplicate timestamp committed another proposal.");
        d.Observe(1, 20, .01, true, true, false); Near(d.ElapsedSeconds, .1, "Missing observation interval earned screen time.");
        Check(!d.NeedsReplacement && d.RememberedShots == 0, "Observation gap advanced editorial lifetime.");
    }
    private static void BlockedReplacement()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); Observe(d, 0, 19, blocked: true);
        Check(!d.NeedsReplacement, "A temporary pause ended a useful shot too quickly.");
        Observe(d, 20, 21, blocked: true);
        Check(d.NeedsReplacement && d.Reason.IndexOf("blocked", StringComparison.Ordinal) >= 0,
            "Permanently rejected progress trapped the shot until a long fixed dwell.");
        Near(d.Progress, 0, "Rejected next position became accepted progress.");
    }
    private static void ManualHold()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); Observe(d, 0, 400, blocked: true, held: true);
        Check(!d.NeedsReplacement && d.ElapsedSeconds == 0 && d.BlockedSeconds == 0,
            "Operator hold was treated as failed camera movement or completed footage.");
    }
    private static void StaticComparison()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); Observe(d, 0, 300, movement: false);
        Check(d.NeedsReplacement && d.ElapsedSeconds >= d.Current!.DurationSeconds,
            "A steady comparison disabled normal shot variety indefinitely.");
    }
    private static void PreparedShot()
    {
        var d = new ScenicDirector(); var current = Plan(); var next = Plan(2, "shore", ScenicShotRole.Traverse);
        d.Accept(current, 0); d.Stage(next);
        Check(d.Current == current && d.Prepared == next && d.Progress == 0 && d.RememberedShots == 0,
            "Prefetch changed the on-air scene or viewing history.");
        Check(d.TakePrepared() == next && d.Prepared == null && d.Current == current,
            "Taking staged geometry implicitly activated it.");
        d.Accept(next, 1); Check(d.Current == next && d.Progress == 0, "Prepared activation retained previous progress.");
        d.Observe(2, 1, .7, true, true, false); Check(d.ShouldPrefetch, "Developed scenic shot did not request a successor in advance.");
    }
    private static void NoCatchUp()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0);
        d.Observe(1, 0, .2, true, true, false);
        double before = d.Propose(.016);
        for (int i = 1; i <= 100; i++) d.Propose(.016, false);
        Near(d.Propose(.016), before, "Resumption repaid paused proposal time.");
        Check(d.Propose(20) - d.Progress <= .1 / d.Current!.DurationSeconds + .00001,
            "Long frame stall created a camera leap.");
    }
    private static void SequenceEvidence()
    {
        var d = new ScenicDirector(); var feature = Feature(); d.Accept(Plan(), 0);
        Check(d.PreferredRole(feature) == ScenicShotRole.Establish, "Unshown plan consumed the establishing beat.");
        d.Stage(Plan(2)); Check(d.PreferredRole(feature) == ScenicShotRole.Establish, "Staging consumed a sequence beat.");
        Observe(d, 0, 21); Check(d.PreferredRole(feature) == ScenicShotRole.Establish && d.RememberedShots == 1,
            "Brief footage confused view memory with completed establishing context.");
        Observe(d, 22, 61); Check(d.PreferredRole(feature) == ScenicShotRole.Reveal,
            "Sustained actual establishing footage did not advance the ridge sequence.");
    }
    private static void ViewMemory()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); Observe(d, 0, 21);
        var repeated = Plan(2);
        var fresh = ScenicShotPlan.Directed(3, Feature(), ScenicShotRole.Reveal,
            P(24, 26, -8), P(24, 30, 8), P(), P(2), 55);
        Check(d.Rank(fresh, .9) > d.Rank(repeated, .9) + .2,
            "Equivalent nearby views competed equally with a distinct next sequence shot.");
        d.Accept(Plan(4, role: ScenicShotRole.Reveal), 3); Observe(d, 30, 51);
        d.Accept(Plan(5, role: ScenicShotRole.Traverse), 6); Observe(d, 60, 81);
        Check(d.Rank(Plan(6, "new-ridge"), .9) > d.Rank(Plan(7), .9),
            "An exhausted feature did not yield to an unshown place.");
    }
    private static void RockSetting()
    {
        ScenicShotPlan Rock(long id, float x)
        {
            var rock = new ScenicFeature("rock-" + id, ScenicFeatureKind.Rock, P(x), P(1, 0, 0), 3, 3, .8f);
            return ScenicShotPlan.Directed(id, rock, ScenicShotRole.Establish,
                P(x - 8, 26, -30), P(x + 8, 26, -30), P(x), P(x + 2), 55);
        }
        var director = new ScenicDirector();
        director.Accept(Rock(1, 0), 0);
        Observe(director, 0, 22);
        director.RebaseInWorld();
        Check(director.Rank(Rock(3, 100), .9) > director.Rank(Rock(2, 12), .9) + .1,
            "A new rock ID made the same nearby establishing view score like a different setting");
    }
    private static void BoundedHistory()
    {
        var d = new ScenicDirector();
        for (int shot = 0; shot < 40; shot++)
        { d.Accept(Plan(shot + 1, "feature" + shot), shot * 3); Observe(d, shot * 30, shot * 30 + 22); }
        Check(d.RememberedShots == 24, "Scenic viewing memory grew without a bound.");
        d.RebaseInWorld();
        Check(d.Current == null && d.Prepared == null && d.RememberedShots == 24,
            "Local Journey repair must retire camera plans while preserving shown Scenic history");
        d.Reset(); Check(d.Current == null && d.Prepared == null && d.RememberedShots == 0 && d.Progress == 0,
            "World reset retained scenic identities or direction history.");
    }
    private static void PictureFailure()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); Observe(d, 0, 6, live: false, readable: false);
        Check(d.NeedsReplacement, "Missing live scenery never requested replacement.");
        d.Accept(Plan(2), 1); Observe(d, 10, 31, readable: false);
        Check(d.NeedsReplacement, "Persistent unreadable scenery never requested replacement.");
    }
    private static void MonotonicProgress()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); d.Observe(1, 0, .4, true, true, false);
        d.Observe(1, .1, .3, true, true, true); Near(d.Progress, .4, "Retained output rewound already accepted progress.");
        d.Accept(Plan(2), .2); Near(d.Progress, 0, "A new shot inherited previous progress.");
    }
    private static void LowRateRecovery()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0);
        d.Observe(1, 0, 0, true, true, true);
        d.Observe(1, 1, 0, true, true, true);
        d.Observe(1, 2, 0, true, true, true);
        Check(d.NeedsReplacement && d.BlockedSeconds >= 2 && d.ElapsedSeconds == 0,
            "Slow output either suspended blocked-route recovery or invented continuous footage credit.");
        d.Accept(Plan(2), 3);
        d.Observe(2, 3, 0, false, false, false);
        d.Observe(2, 4, 0, false, false, false);
        Check(d.NeedsReplacement, "Slow observations suspended missing-output recovery.");
    }
    private static void RecoveryGap()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0);
        d.Observe(1, 0, 0, true, true, true);
        d.Observe(1, 1, 0, true, true, true);
        d.Observe(1, 20, 0, true, true, true);
        Check(!d.NeedsReplacement && d.BlockedSeconds == 0,
            "Unobserved time was counted as a persistent blocked condition.");
        d.Observe(1, 21, 0, true, true, true);
        Check(!d.NeedsReplacement, "A stale failure interval leaked into renewed observations.");
    }
    private static void EpisodeAdmission()
    {
        var d = new ScenicDirector();
        for (int shot = 0; shot < 3; shot++)
        {
            d.Accept(Plan(shot + 1, role: (ScenicShotRole)shot), shot * 3);
            Observe(d, shot * 30, shot * 30 + 22);
        }
        var novelAngle = ScenicShotPlan.Directed(5, Feature(), ScenicShotRole.Detail,
            P(18, 24, -8), P(18, 24, 8), P(), P(), 45);
        Check(!d.CanOffer(novelAngle), "A fourth shot of a completed subject episode was still admissible.");
        var freshFeature = new ScenicFeature("fresh-feature", ScenicFeatureKind.Ridge, P(100), P(1, 0, 0), 10, 8, .8f);
        Check(d.CanOffer(ScenicShotPlan.Directed(6, freshFeature, ScenicShotRole.Establish,
            P(92, 26, -24), P(108, 26, -24), P(100), P(102), 55)), "A completed episode blocked unrelated scenery.");
        d.Accept(Plan(7, "clock"), 320);
        Check(d.CanOffer(novelAngle), "Expired episode memory permanently banned a feature.");
    }
    private static void EquivalentAdmission()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); Observe(d, 0, 22);
        Check(!d.CanOffer(Plan(2, role: ScenicShotRole.Reveal)),
            "Relabeling identical framing bypassed view novelty admission.");
        var scaled = Plan(3, role: ScenicShotRole.Detail, distance: 12);
        Check(d.CanOffer(scaled), "A genuinely different framing scale was mistaken for an equivalent view.");
    }
    private static ScenicFeature ArcSubject(string id, float x = 0, float radius = 8, string? parent = null, string scope = "Building") =>
        new ScenicFeature(id, ScenicFeatureKind.Structure, P(x, 0, 0), P(1, 0, 0), radius, 8, .9f,
            parentId: parent, scopeLabel: scope);

    // The observed 0.8.39 route pair 6/9, translated to a common local origin.
    // Both showed the same structure through almost identical 15m arcs. The
    // original trace did not record feature IDs, so these intentionally model
    // the two survey identities that must not bypass geometric memory.
    private static ScenicShotPlan ObservedArc(long id, ScenicFeature subject, bool second = false,
        bool reverse = false, float fov = 75, float cameraScale = 1, bool differentCurve = false)
    {
        AutonomyPosition[] points = second ? new[] {
            P(10.94456f, 6.19179f, 13.56055f), P(12.87946f, 6.19179f, 10.73682f),
            P(15.24394f, 6.19179f, 7.40625f), P(17.14985f, 6.19179f, 3.84814f), P(18.12107f, 6.19179f, .54956f)
        } : new[] {
            P(11.75531f, 6.19179f, 12.69336f), P(13.48521f, 6.19179f, 9.74487f),
            P(15.61005f, 6.19179f, 6.25830f), P(17.26539f, 6.19179f, 2.55273f), P(17.99127f, 6.19179f, -.79150f)
        };
        var aims = new AutonomyPosition[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = P(subject.Center.X + points[i].X * cameraScale,
                subject.Center.Y + points[i].Y * cameraScale + (differentCurve && i == 2 ? 6 : 0),
                subject.Center.Z + points[i].Z * cameraScale);
            aims[i] = subject.Center;
        }
        if (reverse) Array.Reverse(points);
        return ScenicShotPlan.Spatial(id, subject, ScenicShotRole.Establish, points, aims, fov);
    }
    private static void CrossIdentityArcAdmission()
    {
        var d = new ScenicDirector(); d.Accept(ObservedArc(6, ArcSubject("survey-cluster")), 0); Observe(d, 0, 22);
        Check(!d.CanOffer(ObservedArc(9, ArcSubject("measured-building"), second: true)),
            "A different survey identity admitted the near-identical observed 15m arc.");
        Check(!d.CanOffer(ObservedArc(10, ArcSubject("reverse-alias"), second: true, reverse: true)),
            "Reversing the same world-space arc bypassed composition memory.");
        Check(!d.CanOffer(ObservedArc(11, ArcSubject("changed-envelope", radius: 14), second: true)),
            "A changed subject envelope relabeled an otherwise identical actual picture.");
    }
    private static void DistinctWorldComposition()
    {
        var d = new ScenicDirector(); d.Accept(ObservedArc(6, ArcSubject("building-a")), 0); Observe(d, 0, 22);
        Check(d.CanOffer(ObservedArc(9, ArcSubject("building-b", x: 4), second: true)),
            "A physically different nearby building was treated as the previous subject.");
        Check(d.CanOffer(ObservedArc(10, ArcSubject("building-close"), second: true, cameraScale: .55f)),
            "A meaningfully closer view was rejected as the same world composition.");
        Check(d.CanOffer(ObservedArc(11, ArcSubject("lens-detail"), second: true, fov: 45)),
            "Different lens framing was mistaken for an alias of the wide shot.");
        Check(d.CanOffer(ObservedArc(12, ArcSubject("raised-route"), differentCurve: true)),
            "Matching endpoints erased a route with a different intermediate viewpoint.");
        var hierarchy = new ScenicDirector();
        hierarchy.Accept(ObservedArc(13, ArcSubject("settlement", radius: 35, scope: "Settlement"), cameraScale: 3), 0);
        Observe(hierarchy, 0, 65);
        Check(hierarchy.CanOffer(ObservedArc(14, ArcSubject("central-building", parent: "settlement"))),
            "Settlement context incorrectly prohibited a closer view of its central building.");
    }
    private static void WorldCompositionEvidence()
    {
        var d = new ScenicDirector(); var first = ObservedArc(6, ArcSubject("first"));
        var alias = ObservedArc(9, ArcSubject("alias"), second: true);
        d.Accept(first, 0); Observe(d, 0, 22, held: true);
        Check(d.CanOffer(alias), "Unshown or held route observations consumed world-composition novelty.");
        Observe(d, 23, 46); Check(!d.CanOffer(alias), "Sustained readable output did not establish world-composition memory.");
        var availability = new ScenicPictureAvailability(); var pose = alias.Sample(0);
        var picture = ScenicShotPlan.Picture(10, alias.Feature!, alias.Role, pose.CameraPosition, pose.AimPosition, alias.FieldOfView);
        Check(availability.TryOffer(picture, 1, 1, 4.6, 4.6, false),
            "Editorial composition memory vetoed an independently readable recovery picture.");
        d.Accept(Plan(11, "clock"), 320);
        Check(d.CanOffer(alias), "Expired cross-identity memory permanently prohibited a place.");
    }
    private static void RecoveryAdmission()
    {
        var d = new ScenicDirector(); var current = Plan(); d.Accept(current, 0); Observe(d, 0, 22);
        var alternative = ScenicShotPlan.Directed(2, Feature(), ScenicShotRole.Traverse,
            P(-8, 26, -24), P(14, 26, -24), P(), P(2), 55, -.1f, -.08f);
        Check(!d.CanOffer(alternative), "A small route variation was treated as fresh ordinary coverage.");
        Observe(d, 23, 45, blocked: true);
        Check(d.CanOffer(alternative) && !d.CanOffer(Plan(3)),
            "Blocked-route recovery either banned a distinct route or readmitted the same failing route.");
    }
    private static void EpisodeRetention()
    {
        var d = new ScenicDirector();
        for (int shot = 0; shot < 3; shot++)
        { d.Accept(Plan(shot + 1), shot * 3); Observe(d, shot * 30, shot * 30 + 22); }
        for (int shot = 3; shot < 30; shot++)
        { d.Accept(Plan(shot + 1, "other" + shot), shot * 3); Observe(d, shot * 30, shot * 30 + 22); }
        Check(d.RememberedShots == 24 && !d.CanOffer(ScenicShotPlan.Directed(40, Feature(), ScenicShotRole.Detail,
            P(18, 24, -8), P(18, 24, 8), P(), P(), 45)),
            "Short continuity history turnover erased the bounded episode exclusion.");
    }

    private static void RecoveredMissingPicture()
    {
        var d = new ScenicDirector(); var current = Plan(); var next = Plan(2, "shore");
        d.Accept(current, 0); d.Stage(next);
        d.Observe(1, 0, .1, true, true, false);
        Observe(d, 1, 7, live: false, readable: false);
        Check(d.NeedsReplacement, "Missing output did not request bounded recovery.");
        d.Observe(2, .8, .6, true, true, false);
        Check(d.NeedsReplacement, "Another shot's image cancelled this shot's recovery.");
        d.Observe(1, .9, .1, true, false, false);
        Check(d.NeedsReplacement, "An unreadable live image cancelled picture recovery.");
        d.Observe(1, 1, .1, true, true, false);
        Check(!d.NeedsReplacement && d.Current == current && d.Prepared == next && d.Propose(.1) > d.Progress,
            "A recovered shot stayed frozen or required an unnecessary cut to its prepared successor.");
        d.Observe(1, 1.1, .1, false, false, false);
        Check(!d.NeedsReplacement, "A new short gap inherited the previous missing-output deadline.");
    }

    private static void RecoveredUnreadablePicture()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); d.Stage(Plan(2, "shore"));
        Observe(d, 0, 21, readable: false);
        Check(d.NeedsReplacement, "Unreadable output did not request recovery.");
        d.Observe(1, 2.2, .2, true, true, false);
        Check(!d.NeedsReplacement && d.Progress == .2 && d.Propose(.1) > .2,
            "Fresh readable output did not restore the current shot's movement.");
        Observe(d, 23, 30, readable: false);
        Check(!d.NeedsReplacement, "Recovered readability retained an obsolete failure budget.");
    }

    private static void DurableReplacementRequests()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); Observe(d, 0, 21, blocked: true);
        d.Observe(1, 2.2, 0, true, true, false);
        Check(d.NeedsReplacement && d.Propose(.1) == d.Progress,
            "A readable retained pose cancelled the replacement of a persistently blocked route.");
        d.Accept(Plan(2), 3); d.Observe(2, 3, 1, true, true, false);
        d.Observe(2, 3.1, 1, true, true, false);
        Check(d.NeedsReplacement, "A completed shot was revived by its final readable frame.");
        d.Accept(Plan(3), 4); Observe(d, 40, 46, live: false, readable: false);
        d.RequestReplacement("Operator selected next scene");
        d.Observe(3, 4.7, 0, true, true, false);
        Check(d.NeedsReplacement && d.Reason == "Operator selected next scene",
            "Recovering an earlier picture failure cancelled the operator's later edit.");
    }

    private static void EditorialMovement()
    {
        var d = new ScenicDirector(); var first = Plan(); var next = Plan(2, "shore");
        d.Accept(first, 0); d.Observe(1, 0, .2, true, true, false); d.Stage(next);
        d.RequestEdit("Prefer another local composition");
        for (int i = 1; i <= 10; i++)
        {
            double nextProgress = d.Propose(.1);
            Check(nextProgress > d.Progress, "A desired edit froze the still-valid current trajectory.");
            d.Observe(1, i * .1, nextProgress, true, true, false);
        }
        Check(d.NeedsReplacement && !d.BlocksMovement && d.Current == first && d.Prepared == next &&
            d.Reason == "Prefer another local composition", "Preparation consumed the current shot or lost its editorial request.");
        Near(d.Propose(.1, false), d.Progress, "Editorial preparation overrode an operator hold.");
        d.Accept(d.TakePrepared()!, 1.1);
        Check(!d.NeedsReplacement && !d.BlocksMovement, "A fulfilled edit leaked into the new shot.");
        Reject(() => d.RequestEdit(""));
    }

    private static void EditorialMotionFailure()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); d.Observe(1, 0, .2, true, true, false);
        d.RequestEdit("Prefer another local composition"); Observe(d, 1, 22, blocked: true);
        Check(d.BlocksMovement && d.Reason.Contains("blocked"), "A pending editorial request hid persistent motion failure.");
        d.RequestEdit("New destination subject"); d.Observe(1, 2.3, .2, true, true, false);
        Near(d.Propose(.1), d.Progress, "A soft request restarted a persistently rejected movement path.");
        Check(d.Reason.Contains("blocked"), "A soft edit erased the physical failure reason.");
        d.Accept(Plan(2), 3); d.Observe(2, 3, .9, true, true, false); d.RequestEdit("New view");
        d.Observe(2, 3.1, 1, true, true, false);
        Check(d.BlocksMovement && d.Reason.Contains("completed"), "Editorial preparation prevented completed-route detection.");
        d.Reset(); Check(!d.NeedsReplacement && !d.BlocksMovement, "Reset retained pending edit state.");
    }

    private static void EditorialPictureRecovery()
    {
        var d = new ScenicDirector(); d.Accept(Plan(), 0); d.Observe(1, 0, .2, true, true, false);
        d.RequestEdit("Measured picture concern"); Observe(d, 1, 7, live: false, readable: false);
        Check(d.BlocksMovement, "Editorial preparation concealed actual output loss.");
        d.Observe(1, .8, .2, true, true, false);
        Check(d.NeedsReplacement && !d.BlocksMovement && d.Propose(.1) > d.Progress &&
            d.Reason == "Measured picture concern", "Picture recovery lost the pending composition improvement or left motion frozen.");
        Observe(d, 9, 30, readable: false);
        Check(d.BlocksMovement && d.Reason.Contains("unreadable"), "Editorial preparation concealed persistent unreadability.");
        d.RequestReplacement("Operator stopped camera"); d.RequestEdit("New preference");
        d.Observe(1, 3.1, .2, true, true, false);
        Check(d.BlocksMovement && d.Reason == "Operator stopped camera", "A soft preference defeated an explicit stop.");
    }
}
