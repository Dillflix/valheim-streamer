using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyMountainApproachTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey gains altitude before the crest between two lower terrain samples", NarrowCrest);
        yield return ("Journey crosses a loaded mountain range and resumes after loading the next tiles", MountainRange);
        yield return ("Journey takes a lateral route past a towering terrain reading instead of climbing in place", SkywardRidge);
        yield return ("Journey gives a confirmed terrain contact a swept vertical exit above centre-floor clearance", VerticalContact);
    }

    // Height observations are deterministic, while actual displacement must be
    // rechecked against the intervening terrain and the current loaded prefix.
    private sealed class Range : IJourneyGuideWorld, IJourneyTerrainContact
    {
        internal bool Long, Contact, ReverseOnly;
        internal double LoadedThrough = double.PositiveInfinity;
        internal int ContactFrames, SurfaceQueries;
        internal double Terrain(double x)
        {
            if (!Long) return Math.Max(0, 29 - Math.Abs(x - 100) * 2.5);
            // Multiple sustained ascending faces, narrow crests and open valleys.
            return Math.Max(0, Math.Max(Math.Min(130, (x - 180) * 1.8) - Math.Max(0, x - 261),
                Math.Max(115 - Math.Abs(x - 638) * 2.0, Math.Max(270 - Math.Abs(x - 1300) * 1.7,
                    140 - Math.Abs(x - 1810) * 2.4))));
        }
        public bool TryGetSurface(AutonomyPosition p, out double height)
        { SurfaceQueries++; height = Terrain(p.X); return p.X <= LoadedThrough; }
        private double TerrainEnvelope(double x) =>
            Math.Max(Terrain(x), Math.Max(Terrain(x - .9), Terrain(x + .9))) + .9;

        public bool NeedsVerticalEscape(AutonomyPosition p) =>
            (Contact && p.Y < 14) || p.Y < TerrainEnvelope(p.X);
        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            double length = from.Distance(to);
            if (ReverseOnly && (to.X >= from.X || Math.Abs(to.Y - from.Y) > .001 ||
                Math.Abs(to.Z - from.Z) > .001)) return 0;
            if (NeedsVerticalEscape(from))
            {
                ContactFrames++;
                if (to.Y <= from.Y || Math.Abs(to.X - from.X) > .00001 || Math.Abs(to.Z - from.Z) > .00001) return 0;
            }
            int n = Math.Max(1, (int)Math.Ceiling(length / .16));
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                double x = from.X + (to.X - from.X) * t;
                double y = from.Y + (to.Y - from.Y) * t;
                // Enclose the camera sphere over this heightfield: include the
                // terrain at both lateral ends of a 0.9m lens footprint.
                if (x > LoadedThrough || y < TerrainEnvelope(x)) return length * (i - 1) / n;
            }
            return length;
        }
    }

    private static (double elapsed, double earlyY, double maximumHeight, double gap) Fly(Range range, double end, double limit)
    {
        var goal = new AutonomyPosition((float)end, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), goal, 12, 10);
        double time = 0, earlyY = 0, minClear = double.PositiveInfinity, maxY = 0;
        while (time < limit && !guide.Arrived)
        {
            if (range.Long)
            {
                // The discontinuity represents arrival of a requested tile
                // at the loading boundary. Later tiles become available in time.
                range.LoadedThrough = Math.Min(2200, 64 * (Math.Floor(guide.Position.X / 64) + 2));
                if (guide.Position.X < 300 && time < 35) range.LoadedThrough = Math.Min(255, range.LoadedThrough);
            }
            var previous = guide.Position;
            range.SurfaceQueries = 0;
            guide.Step(.05, goal, range);
            double moved = previous.Distance(guide.Position);
            if (range.Clearance(previous, guide.Position) + .001 < moved || moved > .601)
                throw new Exception("Ridge displacement exceeded verified space/speed");
            if (range.SurfaceQueries > 100) throw new Exception("Unbounded ridge sampling");
            if (guide.Position.X <= 74) earlyY = Math.Max(earlyY, guide.Position.Y);
            maxY = Math.Max(maxY, guide.Position.Y);
            minClear = Math.Min(minClear, guide.Position.Y - range.Terrain(guide.Position.X));
            time += .05;
        }
        Console.WriteLine($"Mountain course {end:F0}m: arrival {guide.Arrived}, travel {guide.Travelled:F1}m, " +
            $"elapsed {time:F1}s, early height {earlyY:F1}m, peak altitude {maxY:F1}m, minimum ground gap {minClear:F2}m");
        if (range.ContactFrames > 0) throw new Exception("Terrain course required contact recovery");
        if (!guide.Arrived) throw new Exception($"Range stalled at {guide.Position.X},{guide.Position.Y}: {guide.Status}");
        return (time, earlyY, maxY, minClear);
    }

    private sealed class HighFace : IJourneyGuideWorld
    {
        public bool TryGetSurface(AutonomyPosition at, out double height)
        {
            // Reconstructed obstacle; the trace captured rejected sweeps but
            // did not record the height of the surface ahead.
            height = at.X >= 160 && at.X <= 210 && Math.Abs(at.Z) < 23 ? 1600 : 80;
            return true;
        }
        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            double length = from.Distance(to);
            int samples = Math.Max(1, (int)Math.Ceiling(length / .15));
            for (int i = 1; i <= samples; i++)
            {
                float f = (float)i / samples;
                var p = new AutonomyPosition(from.X + (to.X - from.X) * f,
                    from.Y + (to.Y - from.Y) * f, from.Z + (to.Z - from.Z) * f);
                TryGetSurface(p, out double floor);
                if (p.Y <= floor + .82) return length * (i - 1) / samples;
            }
            return length;
        }
    }

    private static void SkywardRidge()
    {
        var world = new HighFace();
        var start = new AutonomyPosition(0, 90, 0);
        var goal = new AutonomyPosition(5100, 80, 0);
        foreach (double speed in new double[] { 12, 24 })
        {
            var guide = new JourneyGuide(start, goal, speed, 16);
            double time = 0, peak = start.Y;
            while (!guide.Arrived && time < 600)
            {
                var before = guide.Position;
                guide.Step(.05, goal, world);
                if (world.Clearance(before, guide.Position) + .001 < before.Distance(guide.Position))
                    throw new Exception("Unsafe movement across the high face");
                peak = Math.Max(peak, guide.Position.Y);
                time += .05;
            }
            if (!guide.Arrived || peak > start.Y + 50)
                throw new Exception($"Failed ridge bypass at {speed} m/s: peak {peak:F1}, x {guide.Position.X:F1}");
            Console.WriteLine($"High face at {speed} m/s: arrived in {time:F1}s, peak Y {peak:F1}");
        }
    }

    private static void NarrowCrest()
    {
        var flight = Fly(new Range(), 240, 75);
        if (flight.earlyY < 13 || flight.gap < 2.0) throw new Exception("Guide waited for contact with the crest");
    }

    private static void MountainRange()
    {
        var flight = Fly(new Range { Long = true }, 2030, 400);
        if (flight.maximumHeight < 271 || flight.gap < 1.8)
            throw new Exception("Guide did not maintain clearance over the full range");
    }

    private static void VerticalContact()
    {
        var world = new Range();
        var goal = new AutonomyPosition(240, 0, 0);
        var guide = new JourneyGuide(new AutonomyPosition(0, 6, 0), goal, 12, 10);
        for (int i = 0; i < 300 && guide.Position.X < 40; i++) guide.Step(.05, goal, world);
        if (guide.Position.X < 40) throw new Exception("Guide never reached the contact fixture");
        // A dynamically blocked approach sends the guide back along its route.
        // The newly loaded side face then contacts a sphere more than two metres
        // above its centre height sample. The existing trail is now obstructed.
        world.ReverseOnly = true;
        for (int i = 0; i < 240 && !guide.Status.StartsWith("Retracing", StringComparison.Ordinal); i++)
            guide.Step(.05, goal, world);
        if (!guide.Status.StartsWith("Retracing", StringComparison.Ordinal)) throw new Exception("Expected withdrawal");
        world.Contact = true;
        world.ReverseOnly = false;
        double escapeX = guide.Position.X;
        for (int i = 0; i < 240 && guide.Position.Y < 14; i++)
        {
            guide.Step(.05, goal, world);
            if (Math.Abs(guide.Position.X - escapeX) > .001) throw new Exception("Translated through slope contact");
        }
        if (guide.Position.Y < 14) throw new Exception("Failed to escape steep terrain contact");
    }
}
