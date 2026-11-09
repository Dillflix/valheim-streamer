using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyForestStressTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        foreach (int seed in new[] { 11, 27, 39, 54, 73, 92, 103, 124 })
        {
            int route = seed;
            yield return ($"Journey reaches the destination through forest course {seed}", () => Run(route));
        }
    }

    private sealed class Forest : IJourneyGuideWorld, IJourneyFlightCorridor
    {
        private const double CameraRadius = .65;
        private readonly double startX, startZ, axis, lateral, phase;
        private readonly List<Box>[] bins = new List<Box>[96];
        private readonly struct Box
        {
            public readonly double X, Z, Low, High, HalfX, HalfZ;
            public Box(double x, double z, double low, double high, double halfX, double halfZ)
            { X = x; Z = z; Low = low; High = high; HalfX = halfX; HalfZ = halfZ; }
        }

        public Forest(int seed)
        {
            var random = new Random(seed);
            startX = seed * 21 - 1760; startZ = seed * -13 + 851;
            double angle = seed * .579;
            axis = Math.Cos(angle); lateral = Math.Sin(angle);
            phase = random.NextDouble() * Math.PI;
            for (int i = 0; i < bins.Length; i++) bins[i] = new List<Box>();
            // Seeded clusters of trunks, steep outcrops, overhead roof slabs, and
            // clearings. Every course allows lateral exits and an unobstructed overhead route.
            for (int cluster = 0; cluster < 44; cluster++)
            {
                double x = 36 + cluster * 45, z = (random.NextDouble() - .5) * 26;
                for (int tree = 0; tree < 10; tree++)
                {
                    double tx = x + (random.NextDouble() - .5) * 29;
                    double tz = z + (random.NextDouble() - .5) * 38;
                    double ground = Terrain(tx, tz), width = .35 + random.NextDouble() * 1.2;
                    Add(new Box(tx, tz, ground - 1, ground + 11 + random.NextDouble() * 16, width, width));
                }
                if (cluster % 4 == seed % 4)
                {
                    double ground = Terrain(x, z);
                    Add(new Box(x, z, ground - 1, ground + 12, 3, 5));
                    Add(new Box(x + 9, z, ground + 14, ground + 16, 9, 7));
                }
            }
            // Two partially roofed, dead-ended passages cut into forest clearings.
            // Travel can overfly the site or leave through the open approach.
            foreach (double cx in new[] { 42.0, 1046.0 })
            {
                double h = Terrain(cx, 0);
                Add(new Box(cx + 13, -6, h - 3, h + 24, 13, 1.2));
                Add(new Box(cx + 13, 6, h - 3, h + 24, 13, 1.2));
                Add(new Box(cx + 25, 0, h - 3, h + 24, 1, 7));
                Add(new Box(cx + 18, 0, h + 17, h + 20, 8, 7));
            }
        }

        private void Add(Box b)
        {
            // Bin by centre; query expands the range by our largest half-width.
            bins[Bin(b.X)].Add(b);
        }
        private static int Bin(double x) => Math.Max(0, Math.Min(95, (int)Math.Floor(x / 24)));
        private void Local(AutonomyPosition p, out double x, out double z)
        {
            double dx = p.X - startX, dz = p.Z - startZ;
            x = dx * axis + dz * lateral;
            z = -dx * lateral + dz * axis;
        }
        private double Terrain(double x, double z) => Math.Max(0, 10 + 10 * Math.Sin(x / 78 + phase) +
            8 * Math.Cos((x + z * .31) / 163) + 4 * Math.Sin(z / 37));
        public AutonomyPosition World(double x, double z, double altitude) =>
            new AutonomyPosition((float)(startX + x * axis - z * lateral), (float)(Terrain(x, z) + altitude),
                (float)(startZ + x * lateral + z * axis));
        public bool TryGetSurface(AutonomyPosition at, out double height)
        { Local(at, out double x, out double z); height = Terrain(x, z); return true; }

        // First contact along a segment against boxes inflated by the camera envelope.
        private static bool Clip(double start, double delta, double lo, double hi, ref double enter, ref double exit)
        {
            if (Math.Abs(delta) < .0000001) return start >= lo && start <= hi;
            double a = (lo - start) / delta, b = (hi - start) / delta;
            enter = Math.Max(enter, Math.Min(a, b)); exit = Math.Min(exit, Math.Max(a, b));
            return enter <= exit;
        }
        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            Local(from, out double x, out double z); Local(to, out double xx, out double zz);
            double distance = from.Distance(to), clear = distance;
            for (int i = Bin(Math.Min(x, xx) - 15); i <= Bin(Math.Max(x, xx) + 15); i++)
            foreach (Box b in bins[i])
            {
                double enter = 0, exit = 1;
                if (Clip(x, xx - x, b.X - b.HalfX - CameraRadius, b.X + b.HalfX + CameraRadius, ref enter, ref exit) &&
                    Clip(z, zz - z, b.Z - b.HalfZ - CameraRadius, b.Z + b.HalfZ + CameraRadius, ref enter, ref exit) &&
                    Clip(from.Y, to.Y - from.Y, b.Low - CameraRadius, b.High + CameraRadius, ref enter, ref exit))
                    clear = Math.Min(clear, Math.Max(0, enter * distance - .06));
            }
            int count = Math.Max(1, (int)Math.Ceiling(distance));
            for (int i = 0; i <= count; i++)
            {
                double t = i / (double)count;
                if (from.Y + (to.Y - from.Y) * t < Terrain(x + (xx - x) * t, z + (zz - z) * t) + CameraRadius)
                    return Math.Min(clear, distance * Math.Max(0, i - 1) / count);
            }
            return clear;
        }

        public bool TryGetClimbSlope(AutonomyPosition from, AutonomyPosition direction, double range, out double slope)
        {
            Local(from, out double x, out double z);
            double dx = direction.X * axis + direction.Z * lateral;
            double dz = -direction.X * lateral + direction.Z * axis;
            slope = double.NegativeInfinity;
            for (int i = Bin(Math.Min(x, x + dx * range) - 15); i <= Bin(Math.Max(x, x + dx * range) + 15); i++)
            foreach (Box b in bins[i])
            {
                double along = (b.X - x) * dx + (b.Z - z) * dz;
                double span = Math.Abs(dx) * b.HalfX + Math.Abs(dz) * b.HalfZ;
                double side = Math.Abs((b.X - x) * dz - (b.Z - z) * dx);
                double low = along - span, high = along + span;
                if (high < 0 || low > range || side > CameraRadius + 1 + Math.Abs(dz) * b.HalfX + Math.Abs(dx) * b.HalfZ ||
                    b.Low > from.Y + CameraRadius) continue;
                double rise = b.High + CameraRadius + 2 - from.Y;
                slope = Math.Max(slope, rise / Math.Max(2, rise >= 0 ? low - 2 : high + 3));
            }
            return !double.IsNegativeInfinity(slope);
        }
    }

    private static void Run(int seed)
    {
        var forest = new Forest(seed);
        var destination = forest.World(2030, 0, 0);
        double speed = 6 + (seed % 4) * 2;
        var guide = new JourneyGuide(forest.World(0, 0, 8), destination, speed, 10);
        double time = 0, idle = 0, longest = 0;
        const double dt = 1.0 / 30;
        // A stationary route breaks early, regardless of whether guide still reports a live view.
        while (!guide.Arrived && time < 2030 / speed * 1.8 + 30 && idle < 28)
        {
            var from = guide.Position;
            guide.Step(dt, destination, forest);
            double moved = from.Distance(guide.Position);
            if (moved > speed * dt + .005 || moved > forest.Clearance(from, guide.Position) + .005)
                throw new Exception($"Forest {seed}: unsafe displacement at {time:F2}s");
            idle = moved < .002 ? idle + dt : 0;
            longest = Math.Max(longest, idle); time += dt;
        }
        string result = $"Forest {seed}: speed {speed}, arrived {guide.Arrived}, elapsed {time:F1}s, " +
            $"travel {guide.Travelled:F1}m, remaining {guide.Position.HorizontalDistance(destination):F1}m, longest idle {longest:F1}s, {guide.Status}";
        Console.WriteLine(result);
        if (!guide.Arrived || guide.Travelled < 2000 || guide.Position.HorizontalDistance(destination) > 10.01)
            throw new Exception(result);
    }
}
