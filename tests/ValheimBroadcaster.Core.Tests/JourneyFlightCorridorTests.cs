using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyFlightCorridorTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey climbs ahead of a forest stand and descends smoothly into open land after crossing", ForestCrest);
    }

    // Two close ranks of thick trunks flank a wooded ridge. Each successive row
    // gets taller. The middle corridor is closed; the sides are much longer.
    private sealed class Stand : IJourneyGuideWorld, IJourneyFlightCorridor
    {
        internal bool LookAhead;
        private static double Height(int row) => 18 + row * 1.2;
        private static double RowX(int row) => 70 + 7 * row;
        internal bool Clear(AutonomyPosition p)
        {
            if (p.Y < .5) return false;
            if (Math.Abs(p.Z) > 24) return true;
            for (int i = 0; i < 9; i++)
                if (Math.Abs(p.X - RowX(i)) < 1.6 && p.Y < Height(i)) return false;
            return true;
        }
        public bool TryGetSurface(AutonomyPosition at, out double value) { value = 0; return true; }
        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            if (!Clear(from)) return 0;
            double length = from.Distance(to);
            int steps = Math.Max(1, (int)Math.Ceiling(length / .25));
            for (int i = 1; i <= steps; i++)
            {
                var p = Lerp(from, to, (double)i / steps);
                if (!Clear(p)) return length * (i - 1) / steps;
            }
            return length;
        }
        public bool TryGetClimbSlope(AutonomyPosition from, AutonomyPosition direction, double range, out double slope)
        {
            slope = double.NegativeInfinity;
            if (!LookAhead || direction.X < .5) return false;
            for (int i = 0; i < 9; i++)
            {
                double along = (RowX(i) - from.X) / direction.X;
                if (along + 2 < 0 || along - 2 > range || Math.Abs(from.Z + along * direction.Z) > 25) continue;
                double gap = Height(i) + 2 - from.Y;
                slope = Math.Max(slope, gap / (gap > 0 ? Math.Max(2, along - 4) : Math.Max(2, along + 5)));
            }
            return !double.IsInfinity(slope);
        }
        private static AutonomyPosition Lerp(AutonomyPosition from, AutonomyPosition to, double t) =>
            new((float)(from.X + (to.X - from.X) * t), (float)(from.Y + (to.Y - from.Y) * t),
                (float)(from.Z + (to.Z - from.Z) * t));
    }

    private static void ForestCrest()
    {
        (double seconds, double preRise, double maxDrift, double endY) Fly(bool anticipation)
        {
            var world = new Stand { LookAhead = anticipation };
            var goal = new AutonomyPosition(380, 0, 0);
            var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), goal, 12, 8);
            double preRise = 0, maxDrift = 0, elapsed = 0;
            while (elapsed < 140 && !guide.Arrived)
            {
                guide.Step(.05, goal, world);
                if (!world.Clear(guide.Position)) throw new Exception("Entered forest obstacle");
                if (guide.Position.X < 51) preRise = Math.Max(preRise, guide.Position.Y);
                maxDrift = Math.Max(maxDrift, Math.Abs(guide.Position.Z));
                elapsed += .05;
            }
            return (elapsed, preRise, maxDrift, guide.Position.Y);
        }
        var baseline = Fly(false);
        var elevated = Fly(true);
        Console.WriteLine($"Forest stand: early approach height {baseline.preRise:F1}m / {elevated.preRise:F1}m with lookahead, " +
            $"elapsed {baseline.seconds:F1}s / {elevated.seconds:F1}s, lateral detour {baseline.maxDrift:F1}m / {elevated.maxDrift:F1}m");
        if (elevated.preRise < 10 || elevated.preRise < baseline.preRise + 2 ||
            elevated.seconds > 40 || elevated.maxDrift > 3 || elevated.endY > 8)
            throw new Exception("Forest corridor was not anticipated or open-ground cruise was not resumed");
    }
}
