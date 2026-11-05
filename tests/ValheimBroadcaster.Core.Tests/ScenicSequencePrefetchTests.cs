using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class ScenicSequencePrefetchTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic successor preparation predicts development without inventing shown context", PredictedContext);
        yield return ("Interrupted establishing coverage does not block recovery admission", RecoveryContext);
        yield return ("Early scenic prefetch includes alternate roles while urgent search stays subject-fair", EarlyAcceptance);
        yield return ("Interleaved scenic search visits each applicable role once for bounded mixed subjects", MixedCoverage);
        yield return ("Scenic programme repetition affects ranking across different feature identities", ProgrammeRoles);
        yield return ("Measured destination clusters reuse building identity without merging unrelated hierarchy", MeasuredIdentity);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicFeature Feature(string id, string? scope = null, float x = 0, string? parent = null,
        ScenicFeatureKind kind = ScenicFeatureKind.Structure, PlaceBounds? bounds = null) =>
        new ScenicFeature(id, kind, new AutonomyPosition(x, 20, 0), new AutonomyPosition(1, 0, 0),
            8, 8, .8f, scopeLabel: scope, parentId: parent, measuredBounds: bounds);
    private static ScenicShotPlan Plan(long id, ScenicFeature f, ScenicShotRole role = ScenicShotRole.Establish) =>
        ScenicShotFactory.Create(id, f, role, 0);
    private static void Feed(ScenicDirector d, long id, double start, int frames, bool live = true, bool held = false)
    { for (int i = 0; i <= frames; i++) d.Observe(id, start + i * .1, .2, live, live, false, held); }
    private static void PredictedContext()
    {
        var d = new ScenicDirector(); var f = Feature("building", "Building"); var first = Plan(1, f);
        d.Accept(first, 0);
        Check(d.PreferredNextRole(f) == ScenicShotRole.Establish, "An unshown establishing shot predicted development.");
        Feed(d, 1, 0, 22);
        Check(d.PreferredRole(f) == ScenicShotRole.Establish && d.PreferredNextRole(f) == ScenicShotRole.Detail,
            "Two-second preparation did not distinguish future intent from six-second shown context.");
        var detail = Plan(2, f, d.PreferredNextRole(f)); d.Stage(detail);
        Check(!d.SuccessorContextReady(detail), "Prepared detail bypassed establishing context.");
        Feed(d, 1, 2.3, 30, held: true);
        Check(!d.SuccessorContextReady(detail), "Operator hold invented site context.");
        Feed(d, 1, 5.4, 42);
        Check(d.SuccessorContextReady(detail) && d.PreferredRole(f) == ScenicShotRole.Detail,
            "Presented context failed to release the already prepared successor.");
    }
    private static void RecoveryContext()
    {
        var d = new ScenicDirector(); var f = Feature("building", "Building"); d.Accept(Plan(1, f), 0);
        Feed(d, 1, 0, 22); var detail = Plan(2, f, ScenicShotRole.Detail);
        Check(!d.SuccessorContextReady(detail), "Incomplete context did not guard ordinary preparation.");
        Feed(d, 1, 2.3, 10, live: false);
        Check(d.BlocksMovement && d.SuccessorContextReady(detail), "A failed outgoing picture trapped a validated recovery route.");
    }
    private static void EarlyAcceptance()
    {
        var subjects = Enumerable.Range(0, 32).Select(i => Feature("feature" + i, x: i * 30)).ToArray();
        var prefix = Enumerable.Range(0, 4).Select(i => ScenicCandidateSchedule.SelectSpatial(subjects, 5, 0, i, true)).ToArray();
        Check(prefix.Select(c => c.RoleIndex).Distinct().Count() == 2 && prefix.Count(c => c.FeatureIndex == 5) == 2,
            "The first four scheduled candidates saw only one role per subject.");
        var d = new ScenicDirector(); d.Accept(Plan(1, subjects[5]), 0); Feed(d, 1, 0, 22);
        var actualPrefixRoles = prefix.Select(c => ScenicCandidateSchedule.Role(subjects[c.FeatureIndex], d.PreferredNextRole(subjects[c.FeatureIndex]), c.RoleIndex)).ToArray();
        Check(actualPrefixRoles.Contains(ScenicShotRole.Traverse) && actualPrefixRoles.Contains(ScenicShotRole.Reveal),
            "Early prefetch did not contain development roles in its early scheduled candidates.");
        var absentCurrent = Enumerable.Range(0, 4).Select(i => ScenicCandidateSchedule.SelectSpatial(subjects, -1, 0, i, true)).ToArray();
        Check(absentCurrent.Select(c => c.RoleIndex).Distinct().Count() == 2,
            "A refreshed survey that omitted the current feature lost alternate-role prefetch.");
        var urgent = Enumerable.Range(0, 4).Select(i => ScenicCandidateSchedule.SelectSpatial(subjects, 5, 0, i)).ToArray();
        Check(urgent.All(c => c.RoleIndex == 0) && urgent.Select(c => c.FeatureIndex).Distinct().Count() == 4,
            "First-picture recovery lost its broad subject-first schedule.");
    }
    private static void MixedCoverage()
    {
        for (int count = 1; count <= 32; count++)
        for (int pinned = -1; pinned < count; pinned++)
        {
            var subjects = Enumerable.Range(0, count).Select(i => i % 4 == 0
                ? new ScenicFeature("npc" + i, ScenicFeatureKind.Structure, new AutonomyPosition(i * 5, 20, 0), new AutonomyPosition(1, 0, 0), 1, 2, .8f,
                    subjectKind: ScenicSubjectKind.Npc, subjectIdentity: "$npc_haldor")
                : Feature("subject" + i, kind: i % 2 == 0 ? ScenicFeatureKind.Landscape : ScenicFeatureKind.Structure)).ToArray();
            for (int round = 0; round < 2; round++)
            {
                int limit = ScenicCandidateSchedule.SpatialAttemptLimit(subjects, pinned, round);
                var expected = Enumerable.Range(0, limit).Select(i => ScenicCandidateSchedule.SelectSpatial(subjects, pinned, round, i)).Select(c => (c.FeatureIndex, c.RoleIndex)).ToHashSet();
                var found = Enumerable.Range(0, limit).Select(i => ScenicCandidateSchedule.SelectSpatial(subjects, pinned, round, i, true)).Select(c => (c.FeatureIndex, c.RoleIndex)).ToArray();
                Check(found.Distinct().Count() == limit && expected.SetEquals(found), "Prefetch duplicated or omitted an applicable bounded candidate.");
            }
        }
    }
    private static void ProgrammeRoles()
    {
        var d = new ScenicDirector();
        for (int i = 0; i < 3; i++) { d.Accept(Plan(i + 1, Feature("old" + i, x: i * 100)), i * 10); Feed(d, i + 1, i * 10, 22); }
        // Different subjects with otherwise equal geometric quality should not
        // erase an established run of wide shots from programme-level memory.
        var f = Feature("new", x: 1000);
        Check(d.Rank(Plan(10, f, ScenicShotRole.Traverse), .8) > d.Rank(Plan(11, f), .8),
            "Changing feature identity reset the programme's repeated-role preference.");
        Check(d.CanOffer(Plan(12, f)), "Soft role preference incorrectly became an availability veto.");
    }
    private static void MeasuredIdentity()
    {
        // The capture supplies the 0.46m focus-centre offset, not the full
        // source bounds. Representative overlapping bounds exercise the merge
        // rule without pretending to replay unavailable renderer geometry.
        var bounds = new PlaceBounds(new AutonomyPosition(-5, 16, -5), new AutonomyPosition(5, 24, 5));
        var shifted = new PlaceBounds(new AutonomyPosition(-4.54f, 16, -5), new AutonomyPosition(5.46f, 24, 5));
        var building = Feature("building:299", "Building", bounds: bounds);
        var cluster = Feature("structure:130:483", x: .46f, bounds: shifted);
        Check(ScenicFeatureIdentity.SameMeasuredStructure(building, cluster), "Overlapping 0.46m-offset cluster failed to reuse the measured building.");
        Check(!ScenicFeatureIdentity.SameMeasuredStructure(building, Feature("another", "Building", bounds: shifted)), "Sibling persistent buildings merged.");
        Check(!ScenicFeatureIdentity.SameMeasuredStructure(building, Feature("district", "District", bounds: bounds)), "Hierarchy levels merged.");
        Check(!ScenicFeatureIdentity.SameMeasuredStructure(building, Feature("child", parent: building.Id, bounds: bounds)), "Child subject merged.");
        Check(!ScenicFeatureIdentity.SameMeasuredStructure(building, Feature("unknown")), "Unmeasured evidence claimed identity.");
        var far = new PlaceBounds(new AutonomyPosition(20, 16, -5), new AutonomyPosition(30, 24, 5));
        Check(!ScenicFeatureIdentity.SameMeasuredStructure(building, Feature("landmark", x: 25, bounds: far)), "Unrelated real landmark was discarded.");
    }
}
