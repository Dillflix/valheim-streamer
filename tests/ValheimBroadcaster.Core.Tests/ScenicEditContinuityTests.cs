using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicEditContinuityTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic local wide cuts prefer continuity over recorded 119 and 133 degree reversals", LocalReversals);
        yield return ("Scenic deliberate scale changes and new locations are not continuity failures", EditorialReset);
        yield return ("Scenic movement continuity uses screen relative travel", ScreenMovement);
        yield return ("Scenic continuity cannot block first pictures or outweigh a large readability gain", SoftPreference);
        yield return ("Scenic interruption continuity samples the actual accepted progress", Interrupted);
        yield return ("Recorded scenic rock cuts receive a bounded continuity preference despite different subject envelopes", RecordedCuts);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static AutonomyPosition P(float x, float y = 15, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicShotPlan Route(long id, double heading, float distance = 30, float center = 0, float motion = 12,
        ScenicShotRole role = ScenicShotRole.Establish)
    {
        double angle = heading * Math.PI / 180;
        float dx = (float)Math.Sin(angle), dz = (float)Math.Cos(angle);
        var subject = new ScenicFeature("rock-" + id, ScenicFeatureKind.Rock, P(center, 10), P(0, 0, 1), 8, 8, .9f);
        AutonomyPosition a = P(center - dx * distance, 15, -dz * distance);
        AutonomyPosition b = P(a.X + dz * motion, 15, a.Z - dx * motion);
        return ScenicShotPlan.Spatial(id, subject, role,
            new[] { a, b }, new[] { P(center, 10), P(center + dz * motion, 10, -dx * motion) }, 75);
    }
    private static void LocalReversals()
    {
        var previous = Route(1, 0); double aligned = ScenicEditContinuity.Score(previous, 1, Route(2, 20));
        double first = ScenicEditContinuity.Score(previous, 1, Route(3, 119));
        double second = ScenicEditContinuity.Score(previous, 1, Route(4, 133));
        Check(aligned > first && first > second && first < -.10, "Near-identical wide scale ignored large local viewing reversals.");
    }
    private static void EditorialReset()
    {
        var previous = Route(1, 0);
        var close = Route(1, 133, distance: 12);
        Check(ScenicEditContinuity.Score(previous, 1, close) == 0,
            "Substantial measured scale change inherited wide-to-wide direction cost.");
        Check(ScenicEditContinuity.Score(previous, 1, Route(3, 133, center: 500)) == 0,
            "A different landscape inherited the previous place's direction.");
    }
    private static void ScreenMovement()
    {
        var previous = Route(1, 0); var continuing = Route(2, 20); var reversing = Route(3, 20, motion: -12);
        Check(ScenicEditContinuity.Score(previous, 1, continuing) > ScenicEditContinuity.Score(previous, 1, reversing),
            "Opposite screen-relative camera travel was ranked identically.");
        // Turning the world axes does not change the perceived motion match.
        Check(Math.Abs(ScenicEditContinuity.Score(Route(4, 90), 1, Route(5, 110, motion: -12)) -
            ScenicEditContinuity.Score(previous, 1, reversing)) < .0001, "World axes changed the editorial result.");
    }
    private static void SoftPreference()
    {
        var candidate = Route(2, 180, motion: -12);
        Check(ScenicEditContinuity.Score(null, 0, candidate) == 0, "First picture acquired an imaginary continuity requirement.");
        double penalty = ScenicEditContinuity.Score(Route(1, 0), 1, candidate);
        Check(penalty >= -.28 && penalty <= 0 && 1 + penalty > .6,
            "Continuity could overpower a substantial readability gain or become an admission gate.");
    }
    private static void Interrupted()
    {
        var feature = new ScenicFeature("turn", ScenicFeatureKind.Rock, P(0, 10), P(0, 0, 1), 8, 8, .9f);
        var turn = ScenicShotPlan.Spatial(1, feature, ScenicShotRole.Reveal,
            new[] { P(0, 15, -30), P(-20, 15, -20), P(-30) }, new[] { P(0, 10), P(0, 10), P(0, 10) }, 75);
        var next = Route(2, 0, role: ScenicShotRole.Reveal);
        Check(ScenicEditContinuity.Score(turn, 0, next) > ScenicEditContinuity.Score(turn, 1, next),
            "Interrupted cut used the unshown outgoing endpoint.");
    }

    private static void RecordedCuts()
    {
        // Reduced geometry from 0.8.43 scenic admissions 350, 354, 357.
        // Original feature radii were not recorded. Exercise the valid range
        // rather than pretending that one guessed envelope is measured truth.
        var cameras = new[] {
            new[] { P(736.55298f,45.043865f,2870.2522f), P(733.65906f,45.043865f,2874.3296f), P(730.76514f,45.043865f,2878.407f), P(726.36414f,45.043865f,2880.78f) },
            new[] { P(756.231f,45.4701f,2874.639f), P(755.8474f,45.4701f,2879.6243f), P(755.4638f,45.4701f,2884.6096f), P(755.74866f,45.4701f,2889.6016f) },
            new[] { P(773.13226f,43.10327f,2894.7747f), P(775.52277f,43.10327f,2890.383f), P(777.91327f,43.10327f,2885.9915f), P(779.6972f,43.10327f,2881.3206f) }
        };
        var focus = new[] { P(725.40955f,40.785713f,2868.4746f), P(792.8839f,36.08129f,2882.474f), P(742.86383f,38.88327f,2872.6052f) };
        foreach (float radius in new[] { 1f, 8f, 30f, 120f })
        {
            var shots = new ScenicShotPlan[3];
            for (int i = 0; i < 3; i++)
            {
                var subject = new ScenicFeature("recorded-rock-" + i, ScenicFeatureKind.Rock, focus[i], P(0,0,1), radius, 8, .9f);
                shots[i] = ScenicShotPlan.Spatial(350 + i, subject, ScenicShotRole.Establish, cameras[i],
                    new[] { focus[i], focus[i], focus[i], focus[i] }, 75);
            }
            for (int i = 0; i < 2; i++)
            {
                double cost = ScenicEditContinuity.Score(shots[i], i == 0 ? .81 : 1, shots[i + 1]);
                Check(cost < -.01 && cost >= -.28 && !double.IsNaN(cost), "Recorded reverse-facing cut escaped continuity comparison.");
            }
        }
    }
}
