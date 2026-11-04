using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyArrivalGeometryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Arrival approaches a separated destination viewpoint without passing its subject", StandOff);
        yield return ("Turning arrival preserves its exact incoming seam and ends inside the destination", Turning);
        yield return ("Arrival rejects backwards travel and an already passed stand-off", RejectReversal);
        yield return ("Arrival rejects coincident, rearward or remote subjects", RejectSubject);
        yield return ("Arrival respects the complete local route length rather than endpoint displacement alone", Horizon);
    }
    private static AutonomyPosition P(double x, double z = 0, double y = 12) => new AutonomyPosition((float)x, (float)y, (float)z);
    private static AutonomyPosition D(double x = 1, double z = 0, double y = 0) => P(x, z, y);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void StandOff()
    {
        foreach (double radius in new[] { 12d, 16d, 24d })
        {
            var target = P(48);
            Check(JourneyArrivalGeometry.TryBuild(P(0), D(), target, radius, target, 48, 0, out var body, out var reason), reason);
            var end = body[body.Length - 1];
            Check(end.X >= 36 && end.X <= 40 && end.HorizontalDistance(target) <= radius && end.Distance(target) >= 8,
                "Arrival passed or collided with its destination subject");
            Check(end.Y == 12 && body[0].Distance(P(0)) == 0 && body[1].Distance(P(2)) == 0,
                "Arrival moved its accepted seam, altitude or incoming tangent");
            float previous = -1;
            foreach (var point in body) { Check(point.X > previous && point.X <= end.X, "Approach overshot then returned"); previous = point.X; }
        }
    }
    private static void Turning()
    {
        var start = P(-1620, 602, 42); var target = P(-1580, 622, 42);
        foreach (double bias in new[] { -1d, 0d, 1d })
        {
            Check(JourneyArrivalGeometry.TryBuild(start, D(), target, 16, target, 48, bias, out var body, out var reason), reason);
            Check(body[0].Distance(start) == 0 && body[1].Distance(P(-1618, 602, 42)) == 0,
                "Bending arrival changed the predecessor tangent");
            Check(body[body.Length - 1].HorizontalDistance(target) <= 16, "Bending arrival missed the destination region");
            double previous = start.HorizontalDistance(target);
            foreach (var point in body) { double next = point.HorizontalDistance(target); Check(next <= previous + .001, "Bending arrival receded from destination"); previous = next; }
        }
    }
    private static void RejectReversal()
    {
        Check(!JourneyArrivalGeometry.TryBuild(P(0), D(-1), P(40), 16, P(40), 48, 0, out _, out _), "Backward incoming tangent was spun toward destination");
        Check(!JourneyArrivalGeometry.TryBuild(P(35), D(), P(40), 16, P(40), 48, 0, out _, out _), "Camera beyond stand-off reversed to it");
    }
    private static void RejectSubject()
    {
        foreach (var subject in new[] { P(30.4), P(0), P(120) })
            Check(!JourneyArrivalGeometry.TryBuild(P(0), D(), P(40), 16, subject, 48, 0, out _, out _),
                "Coincident, behind or remote subject became a valid arrival focus");
    }
    private static void Horizon()
    {
        Check(!JourneyArrivalGeometry.TryBuild(P(0), D(), P(80), 16, P(80), 48, 0, out _, out _), "Distant approach exceeded its 48m planning horizon");
        var target = P(40, 20); double direct = P(0).HorizontalDistance(target) - 9.6;
        Check(!JourneyArrivalGeometry.TryBuild(P(0), D(), target, 16, target, direct + .01, 1, out _, out _),
            "Curved route was admitted using only its shorter endpoint displacement");
    }
}
