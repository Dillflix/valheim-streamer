using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class NativeJourneyDistanceTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Native journey execution traverses a measured kilometre at 30fps without decorative-strength scaling", () => Traverse(30));
        yield return ("Native journey execution traverses a measured kilometre at 60fps without decorative-strength scaling", () => Traverse(60));
    }
    private static AutonomyPosition P(double x, float y = 12, float z = -20) => new AutonomyPosition((float)x, y, z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Traverse(int fps)
    {
        double? previousDuration = null;
        foreach (float decorativeStrength in new[] { .25f, 1.5f })
        {
            var journey = new ScenicJourneyPolicy(); journey.Start(1, 1000, 6, P(0), new AutonomyPosition(1, 0, 0));
            double at = 0, location = 0; long id = 1;
            AutonomyPosition? previousEndpoint = null;
            while (journey.CanContinue)
            {
                double length = journey.NextWindowMetres();
                Check(length >= 8 && length <= 64, "Local journey window lost its geometry bounds");
                bool first = id == 1, last = length >= journey.RemainingMetres - .5;
                var feature = new ScenicFeature("native-coast", ScenicFeatureKind.Coastline, P(location, 2, 0),
                    new AutonomyPosition(1, 0, 0), 12, 5, 1);
                var plan = ScenicShotPlan.Spatial(++id, feature, ScenicShotRole.Traverse,
                    new[] { P(location), P(location + length / 2), P(location + length) },
                    new[] { P(location, 2, 0), P(location + length / 2, 2, 0), P(location + length, 2, 0) }, 65)
                    .AsJourney(1, id - 1, length / journey.CruiseSpeed / ScenicJourneyMotion.CruiseFraction(first, last))
                    .WithJourneyRamps(first, last);
                using var native = new ScenicSplinePath();
                Check(native.Prepare(plan, decorativeStrength), "Production native path did not prepare");
                var director = new ScenicDirector(); director.Accept(plan, at);
                Check(director.Propose(1d / fps) == 0, "New native section skipped exact starting-pose admission");
                AutonomyPosition start = native.Sample(0).CameraPosition;
                if (previousEndpoint.HasValue)
                    Check(start.Distance(previousEndpoint.Value) <= .005, "Native sections did not join at the same world position");
                director.Observe(plan.Id, at, 0, true, true, false);
                journey.Observe(plan, at, 0, true, false, start);
                AutonomyPosition before = start;
                while (director.Progress < .999999)
                {
                    double next = director.Propose(ScenicJourneyPolicy.SafeDeltaSeconds(1d / fps, journey.CruiseSpeed));
                    AutonomyPosition actual = native.Sample((float)next).CameraPosition;
                    Check(actual.Distance(before) <= ScenicJourneyPolicy.MaximumStepMetres + .005,
                        "Native traversal exceeded the live corridor sample limit");
                    at += 1d / fps;
                    director.Observe(plan.Id, at, next, true, true, false);
                    journey.Observe(plan, at, next, true, false, actual);
                    Check(journey.Active, "Native motion caused a position discontinuity");
                    before = actual;
                }
                previousEndpoint = before; location += length;
                Check(at < 180, "Native kilometre traversal exceeded its intended travel duration");
            }
            Check(journey.TargetReached && journey.TravelMetres >= 999.5 && journey.DisplacementMetres >= 999.5,
                "Nominal path completion did not correspond to a measured kilometre through native execution");
            Check(journey.AchievedMetresPerSecond > 5.9 && journey.AchievedMetresPerSecond <= 6.01,
                "Native executed speed did not match the journey cruise speed");
            if (previousDuration.HasValue)
                Check(Math.Abs(at - previousDuration.Value) < .0001, "Decorative strength retimed native journey traversal");
            previousDuration = at;
        }
    }
}
