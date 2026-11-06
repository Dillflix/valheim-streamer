using System;
using ValheimBroadcaster.Core;

internal static class JourneyRetreatTests
{
    // A covered passage forces the guide to withdraw through its actual entrance.
    // A ray toward the destination beyond the blind end is physically blocked.
    private sealed class CoveredPassage : IJourneyGuideWorld
    {
        internal bool Through;
        internal double ReturnGate = double.NegativeInfinity;
        public bool TryGetSurface(AutonomyPosition at, out double height) { height = 0; return true; }
        internal bool Clear(AutonomyPosition point)
        {
            if (point.Y < .5) return false;
            if (point.X >= ReturnGate - 1 && point.X <= ReturnGate) return false;
            if (point.X < 12 || point.X > 68 || Math.Abs(point.Z) > 6 || point.Y > 15) return true;
            return (Through || point.X < 63) && Math.Abs(point.Z) < 2 && point.Y < 9;
        }
        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            if (!Clear(from)) return 0;
            int steps = Math.Max(1, (int)Math.Ceiling(from.Distance(to) / .1));
            for (int i = 1; i <= steps; i++)
            {
                double f = (double)i / steps;
                var p = new AutonomyPosition((float)(from.X + (to.X - from.X) * f),
                    (float)(from.Y + (to.Y - from.Y) * f), (float)(from.Z + (to.Z - from.Z) * f));
                if (!Clear(p)) return from.Distance(to) * (i - 1) / steps;
            }
            return from.Distance(to);
        }
    }

    internal static void CoveredPassageWithdrawal()
    {
        var world = new CoveredPassage();
        var destination = new AutonomyPosition(230, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), destination, 12, 5);
        bool entered = false, withdrew = false;
        const double dt = .05;
        double elapsed = 0;
        for (int frame = 0; frame < 2400 && !guide.Arrived; frame++)
        {
            var from = guide.Position;
            guide.Step(dt, destination, world);
            elapsed += dt;
            var to = guide.Position;
            if (from.Distance(to) > 12 * dt + .001 || world.Clearance(from, to) + .001 < from.Distance(to))
                throw new Exception("Discontinuous or unverified retreat");
            entered |= to.X > 48 && to.X < 63 && Math.Abs(to.Z) < 2 && to.Y < 9;
            withdrew |= entered && to.X < 12;
        }
        if (!entered || !withdrew || !guide.Arrived)
            throw new Exception($"Covered passage stalled at ({guide.Position.X:F1}, {guide.Position.Y:F1}, {guide.Position.Z:F1}); entered {entered}, withdrew {withdrew}, {guide.Status}");
        Console.WriteLine($"Covered passage: entered blind corridor, withdrew and arrived; {guide.Travelled:F1}m in {elapsed:F1}s");
    }
    internal static void ReopenedPassage()
    {
        var world = new CoveredPassage();
        var destination = new AutonomyPosition(230, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), destination, 12, 5);
        bool returnBlocked = false;
        double openAt = double.PositiveInfinity;
        for (int frame = 0; frame < 1400 && !guide.Arrived; frame++)
        {
            if (!double.IsFinite(world.ReturnGate) && guide.Status.StartsWith("Retracing") && guide.Position.X < 60)
                world.ReturnGate = guide.Position.X - .03;
            if (!returnBlocked && guide.Status.StartsWith("Waiting for clearance on the return path"))
            {
                returnBlocked = true;
                openAt = frame + 30;
            }
            if (frame >= openAt) world.Through = true;
            var from = guide.Position;
            guide.Step(.05, destination, world);
            if (world.Clearance(from, guide.Position) + .001 < from.Distance(guide.Position))
                throw new Exception("Guide crossed a closed gate");
        }
        if (!returnBlocked || !guide.Arrived)
            throw new Exception($"Guide failed to recognize reopened exit: {guide.Status}; x={guide.Position.X:F2}");
    }

    internal static void DepartureFacingBlindEnd()
    {
        var world = new CoveredPassage();
        var destination = new AutonomyPosition(230, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(62.999f, 8.99f, 0), destination, 12, 5);
        bool withdrew = false;
        for (int frame = 0; frame < 1800 && !guide.Arrived; frame++)
        {
            var from = guide.Position;
            guide.Step(.05, destination, world);
            withdrew |= guide.Position.X < 12;
            if (world.Clearance(from, guide.Position) + .001 < from.Distance(guide.Position))
                throw new Exception("Unverified rear probe");
        }
        if (!withdrew || !guide.Arrived)
            throw new Exception($"Guide stuck without a departure history at x={guide.Position.X:F2}: {guide.Status}");
    }

}
