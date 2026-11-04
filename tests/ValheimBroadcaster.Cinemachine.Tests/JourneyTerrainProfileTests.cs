using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Plugin;

internal static class JourneyTerrainProfileTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey terrain anticipation climbs before a measured hill while preserving the seam", Anticipation);
        yield return ("Journey terrain profile descends gradually instead of staying above old hills", Descent);
        yield return ("Journey terrain profile rejects a rise that cannot preserve its incoming tangent", ImpossibleRise);
        yield return ("Journey elevation alternatives are gradual and retain exact first two points", Elevated);
        yield return ("Journey terrain fitting rejects unknown or malformed observations", Invalid);
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static Vector3[] Knots(float height = 5) => new[] { new Vector3(0, height, 0), new Vector3(2, height, 0),
        new Vector3(8, height, 0), new Vector3(16, height, 0), new Vector3(24, height, 0), new Vector3(32, height, 0) };
    private static void Anticipation()
    {
        var knots = Knots(); var floors = new[] { 2f, 2, 2, 2, 10, 10 };
        Check(JourneyTerrainProfile.TryFit(knots, floors, 3, 0, out var fitted, out var reason), reason);
        Check(fitted[0] == knots[0] && fitted[1] == knots[1], "Changed accepted seam or incoming tangent");
        Check(fitted[3].y > 5 && fitted[4].y >= 10, "Climb waited until the camera reached the hillside");
        Check(knots[3].y == 5 && floors[3] == 2, "Fitting mutated measured evidence");
        for (int i = 2; i < fitted.Length; i++)
            Check(Mathf.Abs(fitted[i].y - fitted[i - 1].y) <= (knots[i].x - knots[i - 1].x) * .4f + .0001f,
                "Measured hill caused an abrupt vertical step");
    }
    private static void Descent()
    {
        var knots = Knots(12); var floors = new[] { 9f, 9, 5, 2, 0, 0 };
        Check(JourneyTerrainProfile.TryFit(knots, floors, 3, 0, out var fitted, out var reason), reason);
        Check(fitted[2].y < 12 && fitted[5].y <= 3.01f, "Downhill travel retained the original terrain altitude");
        for (int i = 2; i < fitted.Length; i++) Check(fitted[i].y >= floors[i], "Descent went below a measured floor");
    }
    private static void ImpossibleRise()
    {
        var knots = Knots();
        Check(!JourneyTerrainProfile.TryFit(knots, new[] { 2f, 6, 8, 10, 10, 10 }, 3, 0, out _, out var reason) &&
            reason.Contains("tangent"), "Infeasible climb teleported the route's start");
    }
    private static void Elevated()
    {
        var knots = Knots(); var floor = new[] { 2f, 2, 2, 2, 2, 2 };
        Check(JourneyTerrainProfile.TryFit(knots, floor, 3, 16, out var fitted, out var reason), reason);
        Check(fitted[0] == knots[0] && fitted[1] == knots[1] && fitted[5].y > 12, "Elevated alternative failed to develop a continuous lift");
    }
    private static void Invalid()
    {
        var knots = Knots();
        Check(!JourneyTerrainProfile.TryFit(knots, new[] { 2f, 2, float.NaN, 2, 2, 2 }, 3, 0, out _, out _), "Unknown terrain became a usable floor");
        Check(!JourneyTerrainProfile.TryFit(knots, new[] { 2f, 2 }, 3, 0, out _, out _), "Mismatched measurements accepted");
        Check(!JourneyTerrainProfile.TryFit(knots, new[] { 2f, 2, 2, 2, 2, 2 }, 3, float.PositiveInfinity, out _, out _), "Unbounded lift accepted");
    }
}
