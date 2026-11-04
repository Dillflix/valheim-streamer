using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class NativeJourneySamplingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Native journey keeps its intended view direction across short-focus knots", ConstantAim);
        yield return ("Native journey full preflight remains within its bounded sampling effort", SamplingBound);
        yield return ("Native measured slices retain only the rejected endpoint they still reach", RejectedEndpoint);
    }

    private static void ConstantAim()
    {
        foreach (float turn in new[] { 0f, 70f })
        foreach (float grade in new[] { 0f, .4f })
        {
            ScenicShotPlan plan = Plan(64, turn, grade, 2, true);
            using var path = new ScenicSplinePath();
            Check(path.Prepare(plan, 1), "Native journey did not prepare");
            Vector3 offset = V(plan.AimWaypoint(0)) - V(plan.CameraWaypoint(0));
            for (int i = 0; i <= 1024; i++)
            {
                ScenicShotSample actual = path.Sample(i / 1024f);
                Check(Vector3.Distance(V(actual.AimPosition) - V(actual.CameraPosition), offset) < .0001f,
                    "A paired journey aim drifted or stopped at a native knot");
            }
            Check(path.Sample(0).AimPosition.Distance(plan.AimWaypoint(0)) < .0001,
                "Journey interpolation moved its admitted starting aim");
            Check(path.Sample(1).AimPosition.Distance(plan.AimWaypoint(plan.WaypointCount - 1)) < .0001,
                "Journey interpolation moved its terminal aim");
        }
    }

    private static void RejectedEndpoint()
    {
        var source = Plan(48, 40, .1f, 20, true).AsStandalone()
            .WithRejectedContinuation("Observed focus obstruction after this endpoint");
        using var path = new ScenicSplinePath();
        Check(path.Prepare(source, 1), "Source native route did not prepare");
        var suffix = path.ProposeMeasuredSlice(.2f, 1);
        Check(suffix.HasRejectedContinuation && suffix.ContinuationRejection == source.ContinuationRejection,
            "Native reconstruction erased the rejected tail adjoining its retained endpoint");
        var prefix = path.ProposeMeasuredSlice(0, .6f);
        Check(!prefix.HasRejectedContinuation && ScenicJourneyPolicy.Eligible(prefix),
            "Native earlier endpoint retained unrelated rejection evidence");
    }

    private static void SamplingBound()
    {
        foreach (float length in new[] { 32f, 48f, 64f })
        foreach (float turn in new[] { 0f, 20f, 40f, 70f })
        foreach (float grade in new[] { 0f, .4f })
        foreach (float aimDistance in new[] { 2f, 3f, 10f, 20f })
        foreach (bool entry in new[] { false, true })
        {
            ScenicShotPlan plan = Plan(length, turn, grade, aimDistance, entry);
            using var path = new ScenicSplinePath();
            Check(path.Prepare(plan, 1), "Native journey did not prepare");
            int count = PreflightSamples(plan, path);
            Check(count <= 512 && count * 2 <= 1024,
                $"{length}m journey at {turn} degrees / grade {grade} / aim {aimDistance} needs {count} samples per pass");
        }
    }

    // Execute the evaluator's actual adaptive spatial/angular criteria against
    // production native spline output. Both accepted samples and subdivision
    // attempts consume its budget; terrain fitting and validation each repeat
    // the traversal. No world-readiness or physics result is simulated here.
    private static int PreflightSamples(ScenicShotPlan plan, ScenicSplinePath path)
    {
        var boundaries = new List<float>();
        for (int i = 0; i <= 16; i++) boundaries.Add(i / 16f);
        for (int i = 0; i < plan.WaypointCount; i++) boundaries.Add(path.WaypointProgress(i));
        boundaries.Sort();
        var pending = new Stack<float>();
        float last = float.NaN;
        for (int i = boundaries.Count - 1; i >= 0; i--)
            if (boundaries[i] != last) { pending.Push(boundaries[i]); last = boundaries[i]; }
        int count = 0;
        bool hasPrevious = false;
        ScenicShotSample previous = default;
        float previousProgress = 0;
        while (pending.Count > 0)
        {
            Check(++count <= 2048, "Native adaptive sampling failed to converge");
            float progress = pending.Pop();
            ScenicShotSample sample = path.Sample(progress);
            if (hasPrevious && (sample.CameraPosition.Distance(previous.CameraPosition) > .75 ||
                path.SpatialTravelBetween(previousProgress, progress) > .75 ||
                sample.AimPosition.Distance(previous.AimPosition) > 1 || Angle(previous, sample) > 3))
            {
                Check(progress - previousProgress >= .0001f, "Native journey contains a pose discontinuity");
                pending.Push(progress); pending.Push((progress + previousProgress) * .5f); continue;
            }
            previous = sample; previousProgress = progress; hasPrevious = true;
        }
        return count;
    }

    private static double Angle(ScenicShotSample before, ScenicShotSample after)
    {
        // Compose applies the same constant screen-offset rotation to each
        // pose. That preserves the relative quaternion angle measured here.
        quaternion a = quaternion.LookRotationSafe((float3)(V(before.AimPosition) - V(before.CameraPosition)), new float3(0, 1, 0));
        quaternion b = quaternion.LookRotationSafe((float3)(V(after.AimPosition) - V(after.CameraPosition)), new float3(0, 1, 0));
        return 2 * Math.Acos(Math.Min(1, Math.Abs(math.dot(a.value, b.value)))) * 180 / Math.PI;
    }

    private static ScenicShotPlan Plan(float length, float turn, float grade, float aimDistance, bool entry)
    {
        // Keep the legacy authored route's interpolated aim beyond its 2m
        // validity threshold even on the tightest curved test. The horizontal
        // look distance still exercises a close 2m subject.
        Vector3 position = new Vector3(0, 12, 0), offset = new Vector3(0, -aimDistance * .8f, aimDistance);
        var body = new List<AutonomyPosition> { P(position) };
        var aim = new List<AutonomyPosition> { P(position + offset) };
        float travelled = 0, angle = 0, stepLength = Math.Max(4, (length - 2) / 13);
        while (travelled < length - .01f && body.Count < 16)
        {
            float step = Math.Min(length - travelled, body.Count == 1 ? 2 : stepLength);
            if (body.Count > 1) angle = Math.Min(turn, angle + step * 3);
            position += new Vector3((float)Math.Cos(angle * Math.PI / 180), 0,
                -(float)Math.Sin(angle * Math.PI / 180)) * step;
            if (body.Count > 1) position.y += grade * step;
            travelled += step; body.Add(P(position)); aim.Add(P(position + offset));
        }
        var feature = new ScenicFeature("journey-native", ScenicFeatureKind.Coastline,
            new AutonomyPosition(0, 0, 0), new AutonomyPosition(1, 0, 0), 12, 5, 1);
        return ScenicShotPlan.Spatial(2, feature, ScenicShotRole.Traverse, body, aim, 65)
            .AsJourney(1, 1, length / 6).WithJourneyRamps(entry, false);
    }

    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static Vector3 V(AutonomyPosition point) => new Vector3(point.X, point.Y, point.Z);
    private static AutonomyPosition P(Vector3 point) => new AutonomyPosition(point.x, point.y, point.z);
}
