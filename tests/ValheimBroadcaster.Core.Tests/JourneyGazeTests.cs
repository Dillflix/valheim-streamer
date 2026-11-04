using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyGazeTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Recorded backward-facing journey departure is rejected before ownership", RecordedBackwardSeed);
        yield return ("Journey gaze follows a turning corridor while preserving its exact departure aim", TurningTravel);
        yield return ("Journey gaze angular development stays bounded at each supported cruise", BoundedDevelopment);
        yield return ("Measured coastal context influences gaze without turning travel into a side-facing shot", CoastalContext);
        yield return ("Distant destination metadata cannot masquerade as an observed arrival subject", ArrivalEvidence);
        yield return ("Arrival gaze develops toward a nearby subject instead of inheriting a fixed offset", ArrivalReveal);
        yield return ("Deliberate journey departure remains a cut through fitting, cloning and fixture capture", DepartureIdentity);
    }
    private static AutonomyPosition P(float x, float z = 0, float y = 10) => new AutonomyPosition(x, y, z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static AutonomyPosition[] Curve()
    {
        var points = new AutonomyPosition[13]; double x = 0, z = 0;
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = P((float)x, (float)z);
            double angle = i * 3 * Math.PI / 180; x += 4 * Math.Cos(angle); z += 4 * Math.Sin(angle);
        }
        return points;
    }
    private static double Angle(AutonomyPosition bodyA, AutonomyPosition aimA, AutonomyPosition bodyB, AutonomyPosition aimB)
    {
        double ax = aimA.X - bodyA.X, ay = aimA.Y - bodyA.Y, az = aimA.Z - bodyA.Z;
        double bx = aimB.X - bodyB.X, by = aimB.Y - bodyB.Y, bz = aimB.Z - bodyB.Z;
        double cosine = (ax * bx + ay * by + az * bz) / Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
        return Math.Acos(Math.Max(-1, Math.Min(1, cosine))) * 180 / Math.PI;
    }
    private static void RecordedBackwardSeed()
    {
        var body = P(-1620, 602, 42); var aim = P(-1655.099976f, 602, 39);
        Check(!JourneyGazePolicy.CanDepart(body, aim, new AutonomyPosition(1, 0, 0), out string reason) && reason.Contains("faces away"),
            "The recorded retreating seed still authorizes eastbound travel while looking west");
        Check(JourneyGazePolicy.CanDepart(body, P(-1600, 602, 40), new AutonomyPosition(1, 0, 0), out _),
            "A forward-facing replacement cannot depart");
    }
    private static void TurningTravel()
    {
        var body = Curve(); var start = P(24, 0, 8);
        Check(JourneyGazePolicy.TryBuild(body, start, 6, null, null, out var aims, out _), "Turning gaze failed");
        Check(aims[0].Distance(start) == 0, "Departure seam changed");
        Check(Angle(body[0], aims[0], body[12], aims[12]) > 20, "Moving body retained a fixed world-space angle");
        Check(aims[12].X > body[12].X && aims[12].Z > body[12].Z, "Travel gaze fell behind the corridor");
    }
    private static void BoundedDevelopment()
    {
        var body = Curve();
        foreach (double speed in new[] { 2d, 6, 12 })
        {
            Check(JourneyGazePolicy.TryBuild(body, P(25, -10, 8), speed, null, null, out var aims, out _), "Gaze failed at supported cruise");
            for (int i = 1; i < body.Length; i++)
                Check(Angle(body[i - 1], aims[i - 1], body[i], aims[i]) / (body[i].Distance(body[i - 1]) / speed) <= JourneyGazePolicy.DegreesPerSecond + .001,
                    "Gaze exceeded its angular allocation");
        }
    }
    private static void CoastalContext()
    {
        var body = new[] { P(0), P(4), P(8), P(12), P(16), P(20), P(24) };
        Check(JourneyGazePolicy.TryBuild(body, P(20, 0, 8), 6, new AutonomyPosition(0, 0, 1), null, out var coast, out _), "Coast gaze failed");
        Check(JourneyGazePolicy.TryBuild(body, P(20, 0, 8), 6, null, null, out var plain, out _), "Plain gaze failed");
        Check(coast[6].Z > plain[6].Z + 1 && coast[6].X > body[6].X + 10, "Coastal context was absent or replaced forward viewing");
    }
    private static void ArrivalEvidence()
    {
        Check(!JourneyGazePolicy.TryBuild(Curve(), P(24, 0, 8), 6, null, P(1000), out _, out string reason) && reason.Contains("locally observed"),
            "A distant coordinate became a live arrival subject");
    }
    private static void ArrivalReveal()
    {
        var body = new AutonomyPosition[13]; for (int i = 0; i < body.Length; i++) body[i] = P(i * 4);
        var subject = P(66, 10, 5);
        Check(JourneyGazePolicy.TryBuild(body, P(24, 0, 8), 6, null, subject, out var aims, out _), "Arrival gaze failed");
        Check(Angle(body[12], aims[12], body[12], subject) < 2 && aims[12].Z > 5,
            "Approach never resolves the actual observed arrival subject");
    }
    private static void DepartureIdentity()
    {
        var body = Curve();
        Check(JourneyGazePolicy.TryBuild(body, P(24, 0, 8), 6, null, null, out var aims, out _), "Departure gaze failed");
        var feature = new ScenicFeature("departure", ScenicFeatureKind.Ridge, P(24), new AutonomyPosition(1, 0, 0), 12, 5, 1);
        var plan = ScenicShotPlan.Spatial(22, feature, ScenicShotRole.Traverse, body, aims, 61, .1f, -.04f)
            .AsJourneyDeparture(19, 9).WithJourneyRamps(true, false);
        foreach (var copy in new[] { plan, plan.WithIdentity(25), plan.WithDuration(10), plan.RaiseCamera(2) })
            Check(copy.IsJourney && copy.JourneyId == 19 && copy.ContinuationOf == 0 && copy.ConstantSpeed &&
                copy.JourneyEaseIn && !copy.JourneyEaseOut && copy.FieldOfView == 61 && copy.Sample(0).ScreenX == .1f &&
                copy.Sample(0).ScreenY == -.04f, "A departure claimed a false predecessor or lost its framing/motion identity");
        var fixture = new CameraFixture(42, "departure", "0.8.42", "", DateTime.UtcNow, 1, true, plan);
        var restored = CameraFixtureCodec.Decode(CameraFixtureCodec.Encode(fixture), 42);
        Check(restored.SourceJourneyId == 19 && restored.SourcePredecessorId == 0 && !restored.Plan.IsJourney &&
            restored.Plan.Sample(0).AimPosition.Distance(plan.Sample(0).AimPosition) == 0 && restored.Plan.JourneyEaseIn,
            "Fixture capture lost departure provenance or altered its authored first view");
    }
}
