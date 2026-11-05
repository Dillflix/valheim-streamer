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
        yield return ("Independent departure can start a measured climb without inheriting the old shot's flat tangent", IndependentClimb);
        yield return ("Explicit Journey cruise altitude retains elevated viewpoints instead of descending toward floor plus capped clearance", CruiseAltitude);
        yield return ("Cruise altitude remains stable through horizontal zigzags and varying lower terrain", ZigzagAltitude);
        yield return ("Cruise altitude preserves exact continuation geometry and still yields to measured higher surfaces", CruiseSeamAndTerrain);
        yield return ("Cruise altitude intent accepts signed world heights and rejects nonfinite or out of world values", CruiseAltitudeBounds);
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
    private static void IndependentClimb()
    {
        var knots = Knots(); var floors = new[] { 2f, 5.5f, 6, 6, 6, 6 };
        Check(!JourneyTerrainProfile.TryFit(knots, floors, 1, 0, out _, out _), "A connected seam unexpectedly changed its tangent");
        Check(JourneyTerrainProfile.TryFit(knots, floors, 1, 0, out var fitted, out string reason, preserveIncomingTangent: false), reason);
        Check(fitted[0] == knots[0] && fitted[1].y >= 5.5f && fitted[1].y <= 5.8001f,
            "Independent fitting either moved the observed opening or failed its bounded initial climb");
        for (int i = 1; i < fitted.Length; i++)
            Check(fitted[i].y >= floors[i] && Mathf.Abs(fitted[i].y - fitted[i - 1].y) <=
                Vector3.ProjectOnPlane(fitted[i] - fitted[i - 1], Vector3.up).magnitude * JourneyTerrainProfile.MaximumGrade + .0001f,
                "Independent departure bypassed its measured surface or slope bounds");
    }
    private static void CruiseAltitude()
    {
        // A mathematical lower-surface fixture. These values are not presented
        // as terrain measurements from the supplied runtime recording.
        var knots = Knots(110.25464f); var floors = new[] { 70f, 70, 70, 70, 70, 70 };
        Check(JourneyTerrainProfile.TryFit(knots, floors, 18, 0, out var old, out var reason,
            preserveIncomingTangent: false), reason);
        Check(old[old.Length - 1].y < 100, "Default terrain following did not represent the forced-descent defect");
        Check(JourneyTerrainProfile.TryFit(knots, floors, 18, 0, out var held, out reason,
            preserveIncomingTangent: false, cruiseAltitude: knots[0].y), reason);
        foreach (Vector3 point in held) Check(point.y == knots[0].y, "An elevated cruise was pulled toward floor plus18m");
        Check(JourneyTerrainProfile.TryFit(knots, floors, 18, 8, out var lofted, out reason,
            preserveIncomingTangent: false, cruiseAltitude: knots[0].y), reason);
        Check(lofted[lofted.Length - 1].y > knots[0].y + 7.9f,
            "A deliberate loft stopped working above an elevated cruise datum");
    }
    private static void ZigzagAltitude()
    {
        var knots = new Vector3[10]; var floors = new float[10];
        for (int i = 0; i < knots.Length; i++)
        {
            knots[i] = new Vector3(i * 6, 112, i % 2 == 0 ? -3 : 3);
            floors[i] = i % 3 == 0 ? 60 : i % 3 == 1 ? 72 : 65;
        }
        Check(JourneyTerrainProfile.TryFit(knots, floors, 18, 0, out var fitted, out var reason,
            cruiseAltitude: 112), reason);
        for (int i = 0; i < fitted.Length; i++)
            Check(fitted[i] == knots[i], "Changing horizontal heading or lower terrain changed an explicit level cruise");
    }
    private static void CruiseSeamAndTerrain()
    {
        var knots = Knots(110); knots[1].y = 109.6f;
        var low = new[] { 70f, 70, 70, 70, 70, 70 };
        Check(JourneyTerrainProfile.TryFit(knots, low, 18, 0, out var fitted, out var reason,
            cruiseAltitude: 120), reason);
        Check(fitted[0] == knots[0] && fitted[1] == knots[1], "Altitude intent rewrote the accepted seam or incoming tangent");
        for (int i = 2; i < fitted.Length; i++)
            Check(Mathf.Abs(fitted[i].y - fitted[i - 1].y) <=
                Vector3.ProjectOnPlane(knots[i] - knots[i - 1], Vector3.up).magnitude * JourneyTerrainProfile.MaximumGrade + .0001f,
                "Returning toward cruise intent bypassed the cinematic slope limit");
        knots = Knots(110); var hill = new[] { 95f, 95, 95, 95, 116, 119 };
        Check(JourneyTerrainProfile.TryFit(knots, hill, 3, 0, out fitted, out reason, cruiseAltitude: 110), reason);
        Check(fitted[fitted.Length - 1].y >= 119, "Altitude intent overrode a higher measured surface");
        for (int i = 0; i < fitted.Length; i++) Check(fitted[i].y >= hill[i], "Cruise crossed a measured floor");
        Check(!JourneyTerrainProfile.TryFit(knots, new[] { 95f, 112, 120, 120, 120, 120 }, 3, 0,
            out _, out _, cruiseAltitude: 130), "An elevated target excused an unsafe preserved tangent");
    }
    private static void CruiseAltitudeBounds()
    {
        var knots = Knots(-24); var floors = new[] { -50f, -50, -50, -50, -50, -50 };
        Check(JourneyTerrainProfile.TryFit(knots, floors, 3, 0, out var fitted, out var reason, cruiseAltitude: -24), reason);
        foreach (Vector3 point in fitted) Check(point.y == -24, "A valid signed world altitude was lost");
        foreach (float altitude in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -20001f, 20001f })
            Check(!JourneyTerrainProfile.TryFit(knots, floors, 3, 0, out _, out _, cruiseAltitude: altitude),
                "Invalid or unknown cruise altitude entered route geometry");
    }
}
