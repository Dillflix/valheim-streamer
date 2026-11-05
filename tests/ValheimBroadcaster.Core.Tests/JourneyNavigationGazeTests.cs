using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyNavigationGazeTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Connected Journey gaze translates along a straight route without an artificial fixed subject", Straight);
        yield return ("An acute connected turn needs a slower planned cruise instead of backwards footage", SlowerTurn);
        yield return ("Alternating body turns retain independently rate-bounded forward gaze", Alternating);
        yield return ("Journey navigation rejects backwards exact seams and invalid rate inputs", Invalid);
    }
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static AutonomyPosition P(double x, double z, double y = 20) => new AutonomyPosition((float)x, (float)y, (float)z);
    private static void Straight()
    {
        var prior = new JourneyNavigationPose(P(0, 0), P(28, 0));
        for (int i = 1; i <= 12; i++)
        {
            var next = P(i * 4, 0);
            Check(JourneyNavigationGazePolicy.TryAim(prior, next, P(1000, 0), 12, 0, out var aim), "Straight route rejected");
            Check(Math.Abs(aim.X - next.X - 28) < .001 && Math.Abs(aim.Z) < .001, "Gaze remained attached to an old world point");
            prior = new JourneyNavigationPose(next, aim);
        }
    }
    private static void SlowerTurn()
    {
        var prior = new JourneyNavigationPose(P(0, 0), P(28, 0));
        var next = P(4 * Math.Cos(75 * Math.PI / 180), 4 * Math.Sin(75 * Math.PI / 180));
        Check(!JourneyNavigationGazePolicy.TryAim(prior, next, P(1000, 0), 12, 0, out _), "High cruise accepted a view almost perpendicular to travel");
        Check(JourneyNavigationGazePolicy.TryAim(prior, next, P(1000, 0), 2, 0, out var aim), "A genuinely slower bounded turn was not available");
        Check(JourneyNavigationGazePolicy.ForwardCompatible(prior.Position, next, aim), "Slower turn still looks backwards");
        Check(Angle(prior, new JourneyNavigationPose(next, aim)) <= 12.001, "Slower tier bypassed angular rate");
        Check(JourneyNavigationGazePolicy.SlowerTier(12) == 6 && JourneyNavigationGazePolicy.SlowerTier(6) == 2 && JourneyNavigationGazePolicy.SlowerTier(2) == 2,
            "Speed fallback is unbounded or exceeds requested cruise");
    }
    private static void Alternating()
    {
        var prior = new JourneyNavigationPose(P(0, 0), P(28, 0)); double x = 0, z = 0;
        foreach (double degrees in new[] { 30d, 0d, -30d, 0d, 30d, 0d })
        {
            x += 4 * Math.Cos(degrees * Math.PI / 180); z += 4 * Math.Sin(degrees * Math.PI / 180);
            var next = P(x, z);
            Check(JourneyNavigationGazePolicy.TryAim(prior, next, P(1000, 0), 6, 0, out var aim), "Composable alternating turn rejected");
            var current = new JourneyNavigationPose(next, aim);
            Check(Angle(prior, current) <= 4.001 && Math.Abs(next.Distance(aim) - 28) < .001, "Gaze snaps or changes subject range through a bend");
            Check(JourneyNavigationGazePolicy.ForwardCompatible(prior.Position, next, aim), "Body moves behind the viewing direction");
            prior = current;
        }
    }
    private static void Invalid()
    {
        var prior = new JourneyNavigationPose(P(0, 0), P(28, 0));
        Check(!JourneyNavigationGazePolicy.ForwardCompatible(P(0, 0), P(-4, 0), P(24, 0)), "Reverse travel retained forward-looking fixed view");
        Check(!JourneyNavigationGazePolicy.TryAim(prior, P(4, 0), P(100, 0), double.NaN, 0, out _), "NaN speed accepted");
        Check(!JourneyNavigationGazePolicy.TryAim(prior, P(4, 0), P(100, 0), 1, 0, out _), "Unbounded slow drift accepted");
        Check(!JourneyNavigationGazePolicy.TryAim(prior, P(4, 0), P(100, 0), 6, 3, out _), "Unexpected option bypassed bounded gaze portfolio");
    }
    private static double Angle(JourneyNavigationPose a, JourneyNavigationPose b)
    {
        double ax = a.Aim.X - a.Position.X, ay = a.Aim.Y - a.Position.Y, az = a.Aim.Z - a.Position.Z;
        double bx = b.Aim.X - b.Position.X, by = b.Aim.Y - b.Position.Y, bz = b.Aim.Z - b.Position.Z;
        return Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) /
            Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz))))) * 180 / Math.PI;
    }
}
