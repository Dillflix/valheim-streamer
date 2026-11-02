using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicCandidateScheduleTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Dense scenic surveys attempt every shot scale within the bounded search", DenseRoleCoverage);
        yield return ("Compact scenic searches examine every azimuth without duplicate role substitutions", SmallSearchCoverage);
        yield return ("Scenic search rotates omitted features and retains current subject priority", SearchFairness);
        yield return ("Clearing routes remain inside the observed local footprint", ClearingFootprint);
    }

    private static ScenicFeature Feature(int index, ScenicFeatureKind kind = ScenicFeatureKind.Rock) =>
        new ScenicFeature("feature-" + index, kind, new AutonomyPosition(100, 30, 200),
            new AutonomyPosition(1, 0, 0), 8, 8, .8f);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static void DenseRoleCoverage()
    {
        var features = new List<ScenicFeature>();
        for (int i = 0; i < 32; i++) features.Add(Feature(i));
        int limit = ScenicCandidateSchedule.AttemptLimit(features, -1, 0);
        Check(limit == 96, "Dense search no longer has its hard effort bound.");
        var roles = new Dictionary<int, HashSet<ScenicShotRole>>();
        for (int i = 0; i < limit; i++)
        {
            var candidate = ScenicCandidateSchedule.Select(features, -1, 0, i);
            if (!roles.TryGetValue(candidate.FeatureIndex, out var found)) roles[candidate.FeatureIndex] = found = new HashSet<ScenicShotRole>();
            found.Add(ScenicCandidateSchedule.Role(features[candidate.FeatureIndex].Kind, ScenicShotRole.Establish, candidate.RoleIndex));
        }
        Check(roles.Count == 24, "Dense search did not bound its feature batch.");
        foreach (var found in roles.Values)
            Check(found.Count == 4 && found.Contains(ScenicShotRole.Detail) && found.Contains(ScenicShotRole.Reveal),
                "A feature consumed its budget on wide establishing shots without trying close or rising coverage.");
        for (int i = 0; i < 4; i++)
            Check(ScenicCandidateSchedule.Select(features, -1, 0, i).FeatureIndex == 0,
                "First subject did not receive alternative shot scales before the search moved on.");
    }

    private static void SmallSearchCoverage()
    {
        var features = new[] { Feature(0, ScenicFeatureKind.Clearing) };
        var unique = new HashSet<string>();
        int limit = ScenicCandidateSchedule.AttemptLimit(features, 0, 2);
        Check(limit == 24, "Three applicable roles over eight angles should require exactly 24 candidates.");
        for (int i = 0; i < limit; i++)
        {
            var c = ScenicCandidateSchedule.Select(features, 0, 2, i);
            var role = ScenicCandidateSchedule.Role(features[0].Kind, ScenicShotRole.Reveal, c.RoleIndex);
            Check(role != ScenicShotRole.Detail, "Unmeasured landscape detail was substituted for a proper role.");
            Check(unique.Add(role + ":" + c.Viewpoint), "A duplicate role/azimuth wasted bounded scenic search effort.");
        }
    }

    private static void SearchFairness()
    {
        var features = new List<ScenicFeature>();
        for (int i = 0; i < 32; i++) features.Add(Feature(i));
        var seen = new HashSet<int>();
        for (int round = 0; round < 8; round++)
        {
            Check(ScenicCandidateSchedule.Select(features, 9, round, 0).FeatureIndex == 9,
                "Current feature lost its first opportunity to develop the sequence.");
            for (int i = 0; i < ScenicCandidateSchedule.AttemptLimit(features, 9, round); i++)
                seen.Add(ScenicCandidateSchedule.Select(features, 9, round, i).FeatureIndex);
        }
        Check(seen.Count == 32, "The bounded feature batch permanently starved some surveyed subjects.");
        Check(ScenicCandidateSchedule.AttemptLimit(Array.Empty<ScenicFeature>(), -1, 0) == 0,
            "An empty measured survey manufactured candidate work.");
    }

    private static void ClearingFootprint()
    {
        ScenicFeature feature = Feature(1, ScenicFeatureKind.Clearing);
        // The adapter measures an 8m overlap sphere at ground+3m. Its scenic
        // feature centre is ground+1.5m; retain clearance inside that evidence.
        var queryCentre = new AutonomyPosition(feature.Center.X, feature.Center.Y + 1.5f, feature.Center.Z);
        long id = 1;
        foreach (ScenicShotRole role in new[] { ScenicShotRole.Establish, ScenicShotRole.Traverse, ScenicShotRole.Reveal })
        for (int view = 0; view < 8; view++)
        {
            var plan = ScenicShotFactory.Create(id++, feature, role, view);
            Check(plan.TravelMetres >= 5 && plan.VisualChange >= .12, "Compact route lost meaningful visual development.");
            for (int sample = 0; sample <= 64; sample++)
            {
                var point = plan.Sample(sample / 64d).CameraPosition;
                Check(point.Distance(queryCentre) < feature.Radius - 1,
                    "Clearing candidate left the observed patch and moved into the surrounding forest.");
                Check(point.Y > feature.Center.Y + 1,
                    "Compact clearing route became a ground-level camera.");
            }
        }
    }
}
