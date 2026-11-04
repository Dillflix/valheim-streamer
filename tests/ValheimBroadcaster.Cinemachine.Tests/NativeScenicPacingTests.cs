using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class NativeScenicPacingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Native detail pacing develops short routes without a generic thirty-second duration", DetailDevelopment);
        yield return ("Native pacing moderates close-focus turns without slowing every route section", AngularDevelopment);
        yield return ("Native measured pacing preserves geometry between preparation and live reconstruction", StableReconstruction);
        yield return ("Native explicit fixtures timelapses and cruise retain authored timing", AuthoredTiming);
        yield return ("Native paced fixtures replay the same camera and aim samples after persistence", FixtureReplay);
        yield return ("Legacy constant-speed authored fixtures preserve their original native knot geometry", LegacyConstantGeometry);
    }
    private static AutonomyPosition P(float x, float z, float y = 2) => new AutonomyPosition(x, y, z);
    private static ScenicShotPlan Plan(bool native = true) => ScenicShotPlan.Spatial(20,
        new ScenicFeature("near-subject-detail", ScenicFeatureKind.Structure, P(0, 0, 0), P(1, 0, 0), 3, 4, .9f),
        ScenicShotRole.Detail, new[] { P(-10, -5), P(-5, -4), P(-2, -4) },
        new[] { P(0, 0, 0), P(0, 0, 0), P(0, 0, 0) }, 53, .08f, -.04f, native);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static double Angle(ScenicShotSample a, ScenicShotSample b)
    {
        double ax = a.AimPosition.X - a.CameraPosition.X, ay = a.AimPosition.Y - a.CameraPosition.Y, az = a.AimPosition.Z - a.CameraPosition.Z;
        double bx = b.AimPosition.X - b.CameraPosition.X, by = b.AimPosition.Y - b.CameraPosition.Y, bz = b.AimPosition.Z - b.CameraPosition.Z;
        double cosine = (ax * bx + ay * by + az * bz) / Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
        return Math.Acos(Math.Max(-1, Math.Min(1, cosine))) * 180 / Math.PI;
    }
    private static void DetailDevelopment()
    {
        foreach (bool native in new[] { true, false })
        {
            using var path = new ScenicSplinePath(); Check(path.Prepare(Plan(native), 1), "Detail path failed preparation.");
            ScenicShotPlan measured = path.PreparedPlan!;
            Check(measured.PacingProfile != null && measured.DurationSeconds >= 8 && measured.DurationSeconds < 20,
                "Compact detail still receives a long generic duration or a rapid insert.");
            Check(path.Sample((float)(2 / measured.DurationSeconds)).CameraPosition.Distance(path.Sample(0).CameraPosition) > .8,
                "The shot spends its opening nearly stationary.");
        }
    }
    private static void AngularDevelopment()
    {
        using var path = new ScenicSplinePath(); path.Prepare(Plan(), 1);
        ScenicShotPlan measured = path.PreparedPlan!; double seconds = measured.DurationSeconds;
        double maximumAngle = 0, maximumSpeed = 0, priorSpeed = 0, maximumSpeedStep = 0;
        using var uniform = new ScenicSplinePath(); uniform.Prepare(Plan().WithDuration(seconds), 1);
        double uniformPriorSpeed = 0, uniformMaximumSpeedStep = 0;
        var uniformBefore = uniform.Sample(0);
        var before = path.Sample(0);
        const int frames = 1200;
        for (int i = 1; i <= frames; i++)
        {
            var after = path.Sample(i / (float)frames); double dt = seconds / frames;
            double speed = before.CameraPosition.Distance(after.CameraPosition) / dt;
            maximumAngle = Math.Max(maximumAngle, Angle(before, after) / dt); maximumSpeed = Math.Max(maximumSpeed, speed);
            maximumSpeedStep = Math.Max(maximumSpeedStep, Math.Abs(speed - priorSpeed)); priorSpeed = speed; before = after;
            var uniformAfter = uniform.Sample(i / (float)frames);
            double uniformSpeed = uniformBefore.CameraPosition.Distance(uniformAfter.CameraPosition) / dt;
            uniformMaximumSpeedStep = Math.Max(uniformMaximumSpeedStep, Math.Abs(uniformSpeed - uniformPriorSpeed));
            uniformBefore = uniformAfter; uniformPriorSpeed = uniformSpeed;
        }
        Check(maximumAngle <= 6.5 && maximumSpeed <= 1.1, "Timing accelerated a close-focus turn or exceeded the intended travel rate.");
        // Native arc-length lookup has its own piecewise interpolation error.
        // Compare the same native curve at the same duration, rather than
        // attributing that baseline error to the smooth timing map. Exact C1
        // continuity of the timing map is checked independently in Core.
        Check(maximumSpeedStep <= uniformMaximumSpeedStep,
            $"Pacing amplified native rate discontinuity: {maximumSpeedStep:F6} versus {uniformMaximumSpeedStep:F6} m/s.");
        double early = path.Sample(.35f).CameraPosition.Distance(path.Sample(.2f).CameraPosition) / (.15 * seconds);
        double close = path.Sample(.8f).CameraPosition.Distance(path.Sample(.65f).CameraPosition) / (.15 * seconds);
        Check(early > close * 1.15, "The entire route was slowed equally instead of allocating time to the close turn.");
    }
    private static void StableReconstruction()
    {
        var source = Plan(); using var prepared = new ScenicSplinePath(); prepared.Prepare(source, 1);
        var measured = prepared.PreparedPlan!; var spline = prepared.Spline;
        Check(prepared.Prepare(source, .3f) && ReferenceEquals(spline, prepared.Spline), "Re-reading the proposal rebuilt or retimed it.");
        using var live = new ScenicSplinePath(); live.Prepare(measured, 1);
        Check(live.PreparedPlan == measured && Math.Abs(prepared.Length - live.Length) < .00001,
            "Playback measured another curve or replaced the accepted timing.");
        for (int i = 0; i <= 100; i++)
        {
            var a = prepared.Sample(i / 100f); var b = live.Sample(i / 100f);
            Check(a.CameraPosition.Distance(b.CameraPosition) < .0001 && a.AimPosition.Distance(b.AimPosition) < .0001,
                "The accepted pose differs from the reconstructed live pose.");
        }
    }
    private static void AuthoredTiming()
    {
        foreach (var source in new[] { Plan().WithDuration(3600), Plan().WithConstantSpeed(), Plan().AsJourney(2, 19, 2) })
        {
            using var path = new ScenicSplinePath(); path.Prepare(source, 1);
            Check(ReferenceEquals(path.PreparedPlan, source) && path.PreparedPlan!.PacingProfile == null &&
                path.PreparedPlan.DurationSeconds == source.DurationSeconds,
                "Automatic pacing took ownership of authored capture or journey timing.");
        }
    }
    private static void FixtureReplay()
    {
        using var before = new ScenicSplinePath(); before.Prepare(Plan(), 1);
        var fixture = new CameraFixture(17, "detail", "test", "", DateTime.UtcNow, 1, true, before.PreparedPlan!);
        var restored = CameraFixtureCodec.Decode(CameraFixtureCodec.Encode(fixture), 17);
        using var after = new ScenicSplinePath(); after.Prepare(restored.Plan, 1);
        Check(restored.Plan.DurationSeconds == fixture.Plan.DurationSeconds, "Saved shot duration changed.");
        for (int i = 0; i <= 120; i++)
        {
            var a = before.Sample(i / 120f); var b = after.Sample(i / 120f);
            Check(a.CameraPosition.Distance(b.CameraPosition) < .0001 && a.AimPosition.Distance(b.AimPosition) < .0001,
                "Saved measured pacing did not reproduce its accepted route samples.");
        }
    }
    private static void LegacyConstantGeometry()
    {
        foreach (bool entry in new[] { false, true })
        {
            var source = ScenicShotPlan.Directed(31, Plan().Feature!, ScenicShotRole.Reveal,
                P(-10, -10), P(5, -10), P(0, 0, 0), P(2, 0, 0), 60,
                cameraControl: P(-2, -15, 7)).WithConstantSpeed().WithJourneyRamps(entry, false).WithDuration(180);
            using var path = new ScenicSplinePath(); path.Prepare(source, 1);
            for (int i = 0; i < path.Spline!.Count; i++)
            {
                var knot = path.Spline[i].Position;
                var expected = source.Sample(i / (double)(path.Spline.Count - 1)).CameraPosition;
                Check(new AutonomyPosition(knot.x, knot.y, knot.z).Distance(expected) < .0001,
                    "New automatic pacing changed the old constant/ramped native fixture geometry recipe.");
            }
        }
    }
}
