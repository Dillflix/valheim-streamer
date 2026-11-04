using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicPacingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Ordinary Scenic role pacing is independent of spatial versus authored route representation", Representation);
        yield return ("Short detail and broader Scenic roles retain deliberate minimum viewing time", RoleTiming);
        yield return ("Measured Scenic pacing is monotonic with continuous velocity and bounded endpoints", Mapping);
        yield return ("Explicit fixture durations and journey cruise retain their own timing authority", ExplicitTiming);
        yield return ("Pacing snapshots cannot be changed through caller arrays and reject malformed timing", Validation);
    }
    private static AutonomyPosition P(float x, float y = 2, float z = -8) => new AutonomyPosition(x, y, z);
    private static ScenicFeature Feature() => new ScenicFeature("detail", ScenicFeatureKind.Structure, P(0, 0, 0), P(1, 0, 0), 4, 5, .9f);
    private static ScenicShotPlan Plan(ScenicShotRole role = ScenicShotRole.Detail) => ScenicShotPlan.Directed(1, Feature(), role,
        P(-5), P(5), P(0, 0, 0), P(0, 0, 0), 53);
    private static ScenicPacingProfile Profile() => ScenicPacingProfile.Restore(12, new[] { 0d, .2, .6, 1 }, new[] { 0d, .4, .65, 1 });
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Representation()
    {
        foreach (ScenicShotRole role in Enum.GetValues(typeof(ScenicShotRole)))
        {
            var authored = Plan(role);
            var spatial = ScenicShotPlan.Spatial(2, Feature(), role,
                new[] { P(-5), P(0), P(5) }, new[] { P(0, 0, 0), P(0, 0, 0), P(0, 0, 0) }, 53);
            Check(Math.Abs(authored.DurationSeconds - spatial.DurationSeconds) < .05,
                "The same measured route acquired a different shot duration through its storage representation.");
            Check(spatial.DurationSeconds <= 20, "A ten-metre ordinary route still defaults to a long generic shot.");
        }
    }
    private static void RoleTiming()
    {
        Check(Plan().DurationSeconds >= 8 && Plan().DurationSeconds <= 14, "Detail pacing lost its bounded information beat.");
        Check(Plan(ScenicShotRole.Establish).DurationSeconds >= 12, "Establishing context was reduced to a fast insert.");
        var shortPlan = ScenicShotPlan.Directed(2, Feature(), ScenicShotRole.Detail, P(0), P(1), P(0, 0, 0), P(0, 0, 0), 53);
        Check(shortPlan.DurationSeconds >= 8, "A small motion corridor created subsecond shot churn.");
        Check(ScenicPacingPolicy.TravelMetresPerSecond(ScenicShotRole.Detail, true) <
            ScenicPacingPolicy.TravelMetresPerSecond(ScenicShotRole.Detail, false), "Confined subject motion was accelerated indiscriminately.");
    }
    private static void Mapping()
    {
        var profile = Profile(); double previous = 0;
        for (int i = 0; i <= 10000; i++)
        {
            double p = profile.DistanceAt(i / 10000d);
            Check(p >= previous && p >= 0 && p <= 1, "Measured pacing reversed or escaped the validated geometry.");
            previous = p;
        }
        foreach (double t in new[] { .2, .6 })
        {
            double before = (profile.DistanceAt(t) - profile.DistanceAt(t - .000001)) / .000001;
            double after = (profile.DistanceAt(t + .000001) - profile.DistanceAt(t)) / .000001;
            Check(Math.Abs(before - after) < .0001, "A pacing-table boundary introduces a velocity step.");
        }
        var plan = Plan().WithPacingProfile(profile);
        Check(plan.MotionFraction(.0001) < .000001 && 1 - plan.MotionFraction(.9999) < .000001,
            "Paced shots lost their smooth endpoint ramps.");
    }
    private static void ExplicitTiming()
    {
        var automatic = Plan().WithPacingProfile(Profile());
        var fixture = automatic.WithDuration(3600);
        Check(fixture.HasExplicitDuration && fixture.DurationSeconds == 3600 &&
            ReferenceEquals(fixture.PacingProfile, automatic.PacingProfile), "Authored duration discarded its relative speed profile.");
        Check(fixture.RaiseCamera(3).DurationSeconds == 3600 &&
            fixture.RaiseCamera(3).MotionFraction(.3) == fixture.MotionFraction(.3), "Fixture fit changed authored timing.");
        Check(automatic.RaiseCamera(3).PacingProfile == null && !automatic.RaiseCamera(3).HasExplicitDuration,
            "Changed automatic view geometry retained its stale angle-derived timing.");
        var spatial = ScenicShotPlan.Spatial(2, Feature(), ScenicShotRole.Traverse,
            new[] { P(-5), P(5) }, new[] { P(0, 0, 0), P(0, 0, 0) }, 53).WithPacingProfile(Profile());
        var journey = spatial.AsJourney(9, 1, 2);
        Check(journey.DurationSeconds == 2 && Math.Abs(journey.MotionFraction(.3) - .3) < .00001,
            "Ordinary perceptual pacing changed independently commanded journey cruise.");
    }
    private static void Validation()
    {
        var t = new[] { 0d, .3, 1 }; var d = new[] { 0d, .6, 1 };
        var profile = ScenicPacingProfile.Restore(10, t, d); t[1] = .99; d[1] = .01;
        Check(Math.Abs(profile.DistanceAt(.3) - .6) < .000001, "Caller arrays mutated an admitted pacing profile.");
        Reject(() => ScenicPacingProfile.Restore(10, new[] { 0d, .6, .4, 1 }, new[] { 0d, .3, .7, 1 }));
        Reject(() => ScenicPacingProfile.Restore(double.NaN, new[] { 0d, 1 }, new[] { 0d, 1 }));
        Reject(() => ScenicPacingProfile.Restore(10, new[] { 0d, 1 }, new[] { 0d, .9 }));
        Reject(() => ScenicPacingProfile.Restore(10, new[] { 0d, double.Epsilon, 1 }, new[] { 0d, .5, 1 }));
        Reject(() => ScenicPacingProfile.Restore(10, new[] { 0d, .5, 1 }, new[] { 0d, double.Epsilon, 1 }));
        Reject(() => profile.DistanceAt(double.PositiveInfinity));
    }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Malformed pacing accepted."); }
}
