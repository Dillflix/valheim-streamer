using System;
using System.Collections.Generic;
using System.Text.Json;
using ValheimBroadcaster.Core;

internal static class JourneyTerrainContactTests
{
    // The fixture translates the obstacle centre to (0,0,0). Heights of the
    // triangle surface used in the tests are modelled, not captured.
    private static readonly JsonElement Evidence = ReadEvidence();
    private static readonly AutonomyPosition Stuck = Position(Evidence.GetProperty("stationaryCamera"));
    private static readonly AutonomyPosition TileExtents = Position(Evidence.GetProperty("tileExtents"));
    private static readonly double Radius = Evidence.GetProperty("captureRadius").GetDouble();
    private static JsonElement ReadEvidence()
    {
        using JsonDocument fixture = JourneyMissionReplayTests.Fixture("0.8.58-terrain-overlap.json");
        return fixture.RootElement.Clone();
    }
    private static AutonomyPosition Position(JsonElement values) =>
        V(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble());
    private static AutonomyPosition V(double x, double y, double z) => new AutonomyPosition((float)x, (float)y, (float)z);
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Mountain contact inside recorded tile can rise through a measured slope contact", CapturedTerrain);
        yield return ("Terrain sphere envelope covers face, seam, edge and corner contacts", TriangleContacts);
        yield return ("Only an observed swept pose can escape a newly rebuilt tile from deep burial", ClearedColumn);
        yield return ("Two kilometre mountain courses cross loaded slopes after terrain contact", MountainCourses);
    }

    private static void ClearedColumn()
    {
        var history = new JourneyTerrainClearanceHistory();
        var start = V(1, 10, 2);
        var above = V(1, 11, 2);
        history.RecordSweptEnd(7, start);
        Require(!history.CanRecoverAt(7, start), "Planning alone cannot authorize an underground rise");
        history.Presented(7, start);
        Require(history.CanRecoverAt(8, start), "Lost recently rendered above-ground origin");
        history.Presented(8, above);
        Require(history.CanRecoverAt(9, above), "Vertical escape must continue across rendered frames");
        Require(!history.CanRecoverAt(9, V(1, 9, 2)), "Cannot move down");
        history.Presented(9, V(1.1, 11, 2));
        Require(!history.CanRecoverAt(10, above), "Horizontal cut must invalidate the column");
        history.RecordSweptEnd(20, start);
        history.Presented(21, start);
        Require(!history.CanRecoverAt(22, start), "A stale sweep cannot authorize the next frame");
        history.RecordSweptEnd(22, start);
        history.Presented(22, start);
        Require(!history.CanRecoverAt(29, start), "Stale presentation cannot authorize terrain recovery");
        history.Reset();
        Require(!history.CanRecoverAt(22, start), "Cannot retain proof across journeys");
    }

    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }

    private static double Required(AutonomyPosition p, double r, AutonomyPosition a, AutonomyPosition b, AutonomyPosition c)
    {
        Require(JourneyTerrainContactPolicy.TryRequiredHeight(p, r, a, b, c, out double h), "Invalid test triangle");
        return h;
    }

    private static void CapturedTerrain()
    {
        foreach (JsonElement batch in Evidence.GetProperty("rejectedScans").EnumerateArray())
        {
            AutonomyPosition from = Position(batch.GetProperty("from"));
            var relative = V(-from.X, -from.Y, -from.Z);
            foreach (JsonElement motion in batch.GetProperty("motions").EnumerateArray())
            {
                double yaw = motion[0].GetDouble() * Math.PI / 180;
                double pitch = motion[1].GetDouble() * Math.PI / 180;
                double distance = motion[2].GetDouble();
                var delta = V(Math.Sin(yaw) * Math.Cos(pitch) * distance, Math.Sin(pitch) * distance,
                    Math.Cos(yaw) * Math.Cos(pitch) * distance);
                Require(!JourneyEgressPolicy.BoundsBehindMovement(relative, TileExtents, delta),
                    "Old tile-bounds rule unexpectedly admitted recorded direction");
                Require(motion[3].GetDouble() == 0, "Fixture should preserve zero reported clearance");
            }
        }

        var world = new Ridges(1.25, 64);
        double floor = world.Height(Stuck.Z);
        Require(Stuck.Y - floor > Radius, "Test requires camera centre above apparent flat clearance");
        double contact = world.Support(Stuck);
        Require(contact > Stuck.Y + .2, "Slope should overlap the camera envelope");
        Require(JourneyTerrainContactPolicy.CanRise(Stuck, V(Stuck.X, Stuck.Y + .04, Stuck.Z), Radius, floor, contact),
            "Supported vertical escape rejected");
        Require(!JourneyTerrainContactPolicy.CanRise(Stuck, V(Stuck.X + .0005, Stuck.Y + .1, Stuck.Z), Radius, floor, contact),
            "Terrain exemption allowed diagonal travel");
        Require(!JourneyTerrainContactPolicy.CanRise(Stuck, V(Stuck.X, Stuck.Y - .1, Stuck.Z), Radius, floor, contact),
            "Terrain exemption allowed downward travel");
        Require(!JourneyTerrainContactPolicy.CanRise(Stuck, V(Stuck.X, Stuck.Y + .1, Stuck.Z), Radius, floor, double.NaN),
            "Missing footprint was accepted");
        Require(!JourneyTerrainContactPolicy.CanRise(Stuck, V(Stuck.X, Stuck.Y + .1, Stuck.Z), Radius, Stuck.Y + 2, Stuck.Y + 3),
            "Deep burial bypassed ground geometry");
        // Terrain changed around a known clear on-air pose. Authority for that
        // pose widens only the burial-depth condition, never direction/coverage.
        double raisedGround = Stuck.Y + 6;
        var up = V(Stuck.X, Stuck.Y + .1, Stuck.Z);
        Require(JourneyTerrainContactPolicy.CanRise(Stuck, up, Radius, raisedGround, raisedGround + Radius, true),
            "Verified replaced terrain pinned its previously clear camera");
        Require(!JourneyTerrainContactPolicy.CanRise(Stuck, V(Stuck.X, Stuck.Y + 1, Stuck.Z + .001),
            Radius, raisedGround, raisedGround + Radius, true), "Clear-pose evidence allowed a diagonal");
        Require(!JourneyTerrainContactPolicy.CanRise(Stuck, up, Radius, raisedGround, double.NaN, true),
            "Clear-pose evidence waived missing current footprint");
        world.Ceiling = Stuck.Y + Radius + .02;
        Require(world.Clearance(Stuck, V(Stuck.X, Stuck.Y + .5, Stuck.Z)) == 0, "Roof ignored during rise");
    }

    private static void TriangleContacts()
    {
        var a = V(0,0,0);
        var b = V(10,0,0);
        var c = V(0,0,10);
        Require(Math.Abs(Required(V(2,0,2), 1, a,b,c) - 1) < 1e-8, "Flat face");
        Require(Math.Abs(Required(V(-.6,0,2), 1, a,b,c) - .8) < 1e-6, "Edge slice");
        Require(Math.Abs(Required(V(-.6,0,-.3), 1, a,b,c) - Math.Sqrt(.55)) < 1e-6, "Vertex contact");
        Require(double.IsNegativeInfinity(Required(V(-2,0,1), 1, a,b,c)), "Distant triangle should not veto a route");
        Require(!JourneyTerrainContactPolicy.TryRequiredHeight(a, 1, a,b,V(20,5,0), out _), "Degenerate projection accepted");
        var steep = V(0,20,10);
        double h = Required(V(2,3,2), 1, a,b,steep);
        Require(Math.Abs(h - (4 + Math.Sqrt(5))) < 1e-6, "Inclined face tangent");
        Require(Math.Abs(h - Required(V(2,3,2), 1, steep,b,a)) < 1e-8, "Mesh winding changed envelope");
        // Adjacent face across a tile seam can produce the highest contact
        // even when the camera centre projects into the flat tile.
        var edge0 = V(1,0,-5); var edge1 = V(1,0,5); var p = V(.6,0,0);
        double flat = Required(p, 1, edge0,edge1,V(-10,0,0));
        double rise = Required(p, 1, edge1,edge0,V(7,18,0));
        Require(flat == 1 && rise > flat + .9, "Missed contact across seam");
    }

    private sealed class Ridges : IJourneyGuideWorld, IJourneyTerrainContact
    {
        private readonly double slope, spacing;
        internal double Ceiling = double.PositiveInfinity;
        internal Ridges(double slope, double spacing) { this.slope = slope; this.spacing = spacing; }

        internal double Height(double z)
        {
            double phase = (z - Stuck.Z + spacing * .25) / (spacing * 2);
            phase -= Math.Floor(phase);
            double folded = phase < .5 ? 2 * phase : 2 - 2 * phase;
            return Stuck.Y - .94 + slope * spacing * (folded - .25);
        }

        public bool TryGetSurface(AutonomyPosition at, out double height)
        { height = Height(at.Z); return true; }

        // Each strip is planar and split into two faces. The footprint straddles
        // their diagonal and, at ridges, two strips with different normals.
        internal double Support(AutonomyPosition p)
        {
            double origin = Stuck.Z - spacing * .25;
            int first = (int)Math.Floor((p.Z - Radius - origin) / spacing);
            int last = (int)Math.Floor((p.Z + Radius - origin) / spacing);
            double highest = double.NegativeInfinity;
            for (int strip = first; strip <= last; strip++)
            {
                double z = origin + strip * spacing;
                var a = V(p.X - 10, Height(z), z); var b = V(p.X + 10, Height(z), z);
                var c = V(p.X - 10, Height(z + spacing), z + spacing);
                var d = V(p.X + 10, Height(z + spacing), z + spacing);
                highest = Math.Max(highest, Math.Max(Required(p, Radius, a,b,c), Required(p, Radius, d,c,b)));
            }
            return highest;
        }
        public bool NeedsVerticalEscape(AutonomyPosition at) => at.Y < Support(at);
        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            double length = from.Distance(to);
            if (Math.Max(from.Y, to.Y) + Radius >= Ceiling) return 0;
            double support = Support(from);
            if (from.Y < support)
                return JourneyTerrainContactPolicy.CanRise(from, to, Radius, Height(from.Z), support) ? length : 0;
            int steps = Math.Max(1, (int)Math.Ceiling(length * 2));
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                var at = V(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, from.Z + (to.Z - from.Z) * t);
                if (at.Y < Support(at)) return length * (i - 1) / steps;
            }
            return length;
        }
    }

    private static void MountainCourses()
    {
        foreach (var course in new[] { (1.25,64d,12d,0d), (.95,100d,9d,340d), (1.5,48d,12d,-240d) })
        {
            var world = new Ridges(course.Item1, course.Item2);
            var goal = V(Stuck.X + course.Item4, 0, Stuck.Z + 2030);
            var guide = new JourneyGuide(Stuck, goal, course.Item3, 10);
            const double dt = 1d/30;
            double clock = 0;
            for (; !guide.Arrived && clock < 420; clock += dt)
            {
                var from = guide.Position;
                guide.Step(dt, goal, world);
                double step = from.Distance(guide.Position);
                Require(step <= dt * course.Item3 + .005 &&
                    (step < .00001 || world.Clearance(from, guide.Position) >= step - .00001), "Unswept guide movement");
            }
            Require(guide.Arrived && guide.Travelled > 2000, $"Mountain route failed after {guide.Travelled:F1} m: {guide.Status}");
            Console.WriteLine($"Mountain fixture: slope {course.Item1:F2}, cruise {course.Item3}, travelled {guide.Travelled:F1} m in {clock:F1}s");
        }
    }
}
