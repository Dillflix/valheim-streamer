using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicIntegrationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Dedicated scenic editing does not inherit coverage's blanket shot timer", IndependentScenicClock);
        yield return ("Dedicated scenic shots preserve wildlife opportunities across internal edits", WildlifeOpportunity);
        yield return ("Dedicated scenic operator hold pauses wildlife opportunity time", HeldOpportunity);
        yield return ("Unreadable dedicated scenery can still yield to available wildlife", UnreadableWildlife);
        yield return ("Dedicated scenic scheduling retains ordinary player priority", PlayerPriority);
        yield return ("Legacy scenic coverage still requests a new shot on dwell", LegacyScheduling);
        yield return ("Measured scenic features produce distinct purposeful framing scales", DistinctFactoryRoles);
        yield return ("Generated scenic roles have meaningful finite routes and visible development", MeaningfulFactoryMovement);
        yield return ("Scenic viewpoint alternatives change geography without modifying a live route", DistinctFactoryViews);
        yield return ("Scenic factory generates deterministic routes from measured feature identity", DeterministicFactory);
        yield return ("Dedicated scenic route search is not preempted by the generic three-second timeout", SearchCompletesBeforeFallback);
        yield return ("Exhausted dedicated scenic recovery can yield to available wildlife", ExhaustedSearchFallback);
        yield return ("Pending scenic prefetch does not postpone ordinary wildlife opportunities", PendingSearchWildlifeOpportunity);
    }
    private static readonly string[] None = Array.Empty<string>();
    private static readonly string[] Deer = { "deer" };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static AutonomyPosition P(float x = 0, float y = 20, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicFeature Feature(ScenicFeatureKind kind, float radius = 10, float height = 8) =>
        new ScenicFeature(kind.ToString(), kind, P(), P(1, 0, 0), radius, height, .8f);
    private static CoverageDirector Coverage(bool dedicated = true)
    {
        var coverage = new CoverageDirector { DedicatedScenicShots = dedicated, ScenicSeconds = 12, CreatureSeconds = 8 };
        coverage.Tick(0, null, 0, true, None);
        return coverage;
    }
    private static void IndependentScenicClock()
    {
        var c = Coverage(); long revision = c.Revision;
        for (int second = 0; second < 60; second++) c.Tick(1, null, 0, true, None);
        Check(c.Mode == CoverageMode.Scenic && c.Revision == revision,
            "The broad coverage clock kept replacing internally directed scenic shots.");
    }
    private static void WildlifeOpportunity()
    {
        var c = Coverage(); long revision = c.Revision;
        // Multiple short environmental edits need no change to this independent
        // mode clock. Its opportunity can arrive while the current shot runs.
        c.Tick(5, null, 0, true, None);
        c.Tick(5, null, 0, true, None);
        c.Tick(2, null, 0, true, Deer);
        Check(c.Mode == CoverageMode.Creature && c.SubjectId == "deer" && c.Revision > revision,
            "The dedicated scenic mode starved the existing wildlife opportunity.");
    }
    private static void HeldOpportunity()
    {
        var c = Coverage(); c.Tick(10, null, 0, true, None);
        for (int i = 0; i < 60; i++) c.Tick(1, null, 0, true, Deer, holding: true);
        Check(c.Mode == CoverageMode.Scenic && Math.Abs(c.ShotElapsed - 10) < .001,
            "Operator hold advanced the wildlife deadline.");
        c.Tick(2, null, 0, true, Deer);
        Check(c.Mode == CoverageMode.Creature, "Releasing a held scenic view lost its pending wildlife opportunity.");
    }
    private static void UnreadableWildlife()
    {
        var c = Coverage();
        c.Tick(1, null, 0, true, Deer, usableFootage: false);
        c.Tick(1, null, 0, true, Deer, usableFootage: false);
        c.Tick(1, null, 0, true, Deer, usableFootage: false);
        Check(c.Mode == CoverageMode.Creature && c.SubjectId == "deer",
            "An unusable scenic picture blocked the existing loaded-creature fallback.");
    }
    private static void PlayerPriority()
    {
        var c = Coverage(); c.Tick(8, null, 0, true, None);
        c.Tick(1, "player", 1, true, Deer);
        Check(c.Mode == CoverageMode.Player && c.SubjectId == "player",
            "Dedicated scenic editing changed player coverage priority.");
    }
    private static void LegacyScheduling()
    {
        var c = Coverage(false); long revision = c.Revision;
        c.Tick(12, null, 0, true, None);
        Check(c.Mode == CoverageMode.Scenic && c.Revision > revision,
            "The dedicated opt-in disabled legacy shot scheduling.");
    }
    private static void DistinctFactoryRoles()
    {
        var feature = Feature(ScenicFeatureKind.Structure);
        var establish = ScenicShotFactory.Create(1, feature, ScenicShotRole.Establish, 0);
        var detail = ScenicShotFactory.Create(2, feature, ScenicShotRole.Detail, 0);
        var wide = establish.Sample(.5); var tight = detail.Sample(.5);
        double wideScale = feature.Radius / wide.CameraPosition.Distance(wide.AimPosition) /
            Math.Tan(establish.FieldOfView * Math.PI / 360);
        double tightScale = feature.Radius / tight.CameraPosition.Distance(tight.AimPosition) /
            Math.Tan(detail.FieldOfView * Math.PI / 360);
        Check(tightScale > wideScale * 2 && detail.FieldOfView < establish.FieldOfView,
            "Establishing and detail shots do not offer visibly different measured subject scales.");
        var reveal = ScenicShotFactory.Create(3, feature, ScenicShotRole.Reveal, 0);
        Check(reveal.Sample(1).CameraPosition.Y - reveal.Sample(0).CameraPosition.Y >= 5,
            "A reveal has no deliberate height development.");
    }
    private static void MeaningfulFactoryMovement()
    {
        long id = 0;
        foreach (ScenicFeatureKind kind in Enum.GetValues(typeof(ScenicFeatureKind)))
        foreach (ScenicShotRole role in Enum.GetValues(typeof(ScenicShotRole)))
        {
            var plan = ScenicShotFactory.Create(++id, Feature(kind), role, (int)role);
            Check(plan.IsDirected && plan.TravelMetres >= 5 && plan.VisualChange >= .12 &&
                plan.DurationSeconds >= 8 && plan.DurationSeconds <= 20,
                kind + "/" + role + " produced a negligible move or legacy-duration shot.");
            double firstMovement = plan.Sample(2 / plan.DurationSeconds).CameraPosition.Distance(plan.Sample(0).CameraPosition);
            double openingVisualChange = firstMovement / plan.Sample(0).CameraPosition.Distance(plan.Sample(0).AimPosition) /
                Math.Tan(plan.FieldOfView * Math.PI / 360);
            Check(kind == ScenicFeatureKind.Clearing ? openingVisualChange > .05 : firstMovement > .65,
                kind + "/" + role + " has an excessively static opening at its measured subject distance and lens.");
            var before = plan.Sample(.4, .1f); var after = plan.Sample(.4, 1.5f);
            Check(before.CameraPosition.Distance(after.CameraPosition) < .001,
                "Live motion-strength change modified a generated route's geometry.");
        }
    }
    private static void DistinctFactoryViews()
    {
        var feature = Feature(ScenicFeatureKind.Coastline);
        var first = ScenicShotFactory.Create(1, feature, ScenicShotRole.Traverse, 0);
        var opposite = ScenicShotFactory.Create(2, feature, ScenicShotRole.Traverse, 4);
        Check(first.Sample(.5).CameraPosition.Distance(opposite.Sample(.5).CameraPosition) > 30,
            "Opposite environmental viewpoints collapsed to an equivalent nearby composition.");
        Check(first.Sample(.2).CameraPosition.Distance(ScenicShotFactory.Create(3, feature,
            ScenicShotRole.Traverse, 0).Sample(.2).CameraPosition) < .001,
            "Preparing another viewpoint modified the original route.");
    }
    private static void DeterministicFactory()
    {
        var feature = Feature(ScenicFeatureKind.Rock, 6, 12);
        var first = ScenicShotFactory.Create(1, feature, ScenicShotRole.Detail, 3);
        var same = ScenicShotFactory.Create(2, feature, ScenicShotRole.Detail, 3);
        for (int i = 0; i <= 20; i++)
        {
            double p = i / 20d;
            Check(first.Sample(p).CameraPosition.Distance(same.Sample(p).CameraPosition) < .001 &&
                first.Sample(p).AimPosition.Distance(same.Sample(p).AimPosition) < .001,
                "An immutable measured feature generated nondeterministic route geometry.");
        }
        Check(first.FieldOfView == same.FieldOfView && first.DurationSeconds == same.DurationSeconds,
            "Staging a new shot identity changed the same planned lens or duration.");
    }

    private static void SearchCompletesBeforeFallback()
    {
        var c = Coverage(); long revision = c.Revision;
        for (int i = 0; i < 100; i++)
            c.Tick(.1, null, 0, true, Deer, usableFootage: false, scenicSearchExhausted: false);
        Check(c.Mode == CoverageMode.Scenic && c.Revision == revision && c.ShotElapsed == 0,
            "Wildlife fallback preempted bounded scenic route discovery or credited unseen scenery.");
        // Legacy coverage has no route-search completion signal to wait for.
        var legacy = Coverage(false);
        legacy.Tick(3, null, 0, true, Deer, usableFootage: false, scenicSearchExhausted: false);
        Check(legacy.Mode == CoverageMode.Creature, "Dedicated search gating disabled legacy wildlife fallback.");
    }

    private static void ExhaustedSearchFallback()
    {
        var c = Coverage();
        c.Tick(4, null, 0, true, Deer, usableFootage: false, scenicSearchExhausted: false);
        Check(c.Mode == CoverageMode.Scenic, "An incomplete search was abandoned.");
        c.Tick(.1, null, 0, true, Deer, usableFootage: false, scenicSearchExhausted: true);
        Check(c.Mode == CoverageMode.Creature && c.SubjectId == "deer",
            "A completed unsuccessful scenic search could not try its available wildlife alternative.");
        c.Tick(3, null, 0, true, Deer, usableFootage: false, scenicSearchExhausted: true);
        Check(c.Mode == CoverageMode.Scenic, "An unusable creature did not return to scenic recovery.");
        c.Tick(4, null, 0, true, Deer, usableFootage: false, scenicSearchExhausted: true);
        Check(c.Mode == CoverageMode.Scenic,
            "The same failed creature immediately restarted the three-second coverage cycle.");
    }

    private static void PendingSearchWildlifeOpportunity()
    {
        var c = Coverage();
        c.Tick(12, null, 0, true, Deer, usableFootage: true, scenicSearchExhausted: false);
        Check(c.Mode == CoverageMode.Creature && c.SubjectId == "deer",
            "Preparing the next scenic route suppressed a normal wildlife opportunity after actual scenic footage.");
    }
}
