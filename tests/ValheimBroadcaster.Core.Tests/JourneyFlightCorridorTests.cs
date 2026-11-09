using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyFlightCorridorTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey threads staggered forest openings at low height when overflight is possible", ForestPassages);
        yield return ("Broad collider bounds leave an observed open flight lane at cruising height", OpenUnderBounds);
        yield return ("Journey replans safely when a committed opening closes", ClosingPassage);
        yield return ("Long open journey sustains low, mid-height and high perspectives", OpenScale);
        yield return ("Journey climbs ahead of a forest stand and descends smoothly into open land after crossing", ForestCrest);
    }

    // Two close ranks of thick trunks flank a wooded ridge. Each successive row
    // gets taller. The middle corridor is closed; the sides are much longer.
    private sealed class Stand : IJourneyGuideWorld, IJourneyFlightCorridor
    {
        internal bool LookAhead, Gaps, ClearLane;
        private bool Opening(int row, double z) => Gaps && Math.Abs(z - (row < 4 ? 4 : -4)) < 2.8;
        private static double Height(int row) => 18 + row * 1.2;
        private static double RowX(int row) => 70 + 7 * row;
        internal bool Clear(AutonomyPosition p)
        {
            if (p.Y < .5) return false;
            if (ClearLane || Math.Abs(p.Z) > 24) return true;
            for (int i = 0; i < 9; i++)
                if (Math.Abs(p.X - RowX(i)) < 1.6 && p.Y < Height(i) && !Opening(i, p.Z)) return false;
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

    private static void ForestPassages()
    {
        var forest = new Stand { LookAhead = true, Gaps = true };
        var goal = new AutonomyPosition(185, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), goal, 9, 5);
        bool first = false, second = false;
        double peak = 0;
        for (int frame = 0; frame < 1500 && !guide.Arrived; frame++)
        {
            var from = guide.Position;
            // Production supplies a moving itinerary point, not a fixed heading.
            var steering = new AutonomyPosition(Math.Min(goal.X, from.X + 48), 0, 0);
            guide.Step(.05, steering, forest);
            var p = guide.Position;
            if (forest.Clearance(from, p) < from.Distance(p) - .00001) throw new Exception("Crossed a solid trunk");
            if (p.X < 135) peak = Math.Max(peak, p.Y);
            first |= p.X > 69 && p.X < 71 && p.Z > 1;
            second |= p.X > 125 && p.X < 127 && p.Z < -1;
        }
        Console.WriteLine($"Staggered stand: visited both openings {first && second}, peak {peak:F1}m");
        if (!guide.Arrived || !first || !second || peak > 10)
            throw new Exception("Journey flew over a usable forest passage");
    }

    private static void OpenUnderBounds()
    {
        var world = new Stand { LookAhead = true, ClearLane = true };
        var goal = new AutonomyPosition(135, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), goal, 10, 4);
        for (int i = 0; i < 500 && !guide.Arrived; i++)
        {
            guide.Step(.05, goal, world);
            if (guide.Position.Y > 6.1 || Math.Abs(guide.Position.Z) > .1)
                throw new Exception("Collider bounds displaced a clear direct course");
        }
        if (!guide.Arrived) throw new Exception("Did not reach the clearing");
    }

    private static void ClosingPassage()
    {
        var world = new Stand { LookAhead = true, Gaps = true };
        var goal = new AutonomyPosition(190, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), goal, 9, 4);
        bool closed = false;
        for (int i = 0; i < 1200 && !guide.Arrived; i++)
        {
            var from = guide.Position;
            if (!closed && guide.CorridorChoice == "Committed lateral passage")
            {
                world.Gaps = false;
                closed = true;
            }
            guide.Step(.05, goal, world);
            if (world.Clearance(from, guide.Position) < from.Distance(guide.Position) - .00001)
                throw new Exception("Guide relied on obsolete passage clearance");
        }
        if (!closed || !guide.Arrived) throw new Exception("Failed to replan a closed passage");
    }

    private static void OpenScale()
    {
        var world = new Stand();
        var goal = new AutonomyPosition(1300, 0, 60);
        var guide = new JourneyGuide(new AutonomyPosition(0, 20, 60), goal, 10, 5);
        double[] metres = new double[3];
        for (int frame = 0; frame < 3200 && !guide.Arrived; frame++)
        {
            var before = guide.Position;
            guide.Step(.05, goal, world);
            double height = guide.Position.Y;
            metres[height < 9 ? 0 : height < 18 ? 1 : 2] += before.HorizontalDistance(guide.Position);
        }
        if (!guide.Arrived || Array.Exists(metres, m => m < 80))
            throw new Exception("Open travel failed to sustain all three scales");
    }

    private static void ForestCrest()
    {
        (double seconds, double preRise, double maxDrift, double afterStand) Fly(bool anticipation)
        {
            var world = new Stand { LookAhead = anticipation };
            var goal = new AutonomyPosition(380, 0, 0);
            var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), goal, 12, 8);
            double preRise = 0, maxDrift = 0, elapsed = 0, afterStand = double.PositiveInfinity;
            while (elapsed < 140 && !guide.Arrived)
            {
                guide.Step(.05, goal, world);
                if (!world.Clear(guide.Position)) throw new Exception("Entered forest obstacle");
                if (guide.Position.X < 51) preRise = Math.Max(preRise, guide.Position.Y);
                if (guide.Position.X >= 155 && guide.Position.X <= 250) afterStand = Math.Min(afterStand, guide.Position.Y);
                maxDrift = Math.Max(maxDrift, Math.Abs(guide.Position.Z));
                elapsed += .05;
            }
            if (!guide.Arrived) throw new Exception("Journey failed to cross the closed stand");
            return (elapsed, preRise, maxDrift, afterStand);
        }
        var baseline = Fly(false);
        var elevated = Fly(true);
        Console.WriteLine($"Forest stand: early approach height {baseline.preRise:F1}m / {elevated.preRise:F1}m with lookahead, " +
            $"elapsed {baseline.seconds:F1}s / {elevated.seconds:F1}s, lateral detour {baseline.maxDrift:F1}m / {elevated.maxDrift:F1}m");
        if (elevated.preRise < 10 || elevated.preRise < baseline.preRise + 2 ||
            elevated.seconds > 40 || elevated.maxDrift > 3 || elevated.afterStand > 12)
            throw new Exception("Forest corridor was not anticipated or open-ground cruise was not resumed");
    }
}
