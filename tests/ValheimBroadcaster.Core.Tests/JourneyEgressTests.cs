using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyEgressTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey travels out of a camera-envelope overlap acquired after 66 metres", NewlyOccupiedPose);
        yield return ("Egress requires strict separation from every touching body and still sweeps new obstructions", MultipleBodies);
        yield return ("Every corner of an unknown collider's bounds must recede along travel", WholeBounds);
        yield return ("Egress rejects missing supporting planes and contact with the optical centre", InvalidProof);
    }

    private static AutonomyPosition V(double x, double y = 0, double z = 0) =>
        new AutonomyPosition((float)x, (float)y, (float)z);

    private static void Require(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }

    // Analytic convex spheres let the fixture independently measure the camera
    // envelope during sub-frame sweeps. Only the separation decision is shared
    // with the Unity sensor. This does not emulate Unity collider registration.
    private sealed class World : IJourneyGuideWorld
    {
        internal const double CameraRadius = .4, SolidRadius = .75;
        internal readonly List<AutonomyPosition> Centres = new List<AutonomyPosition>();
        public bool TryGetSurface(AutonomyPosition at, out double height) { height = 0; return true; }

        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            double length = from.Distance(to);
            if (length < .000001) return 0;
            var direction = V((to.X - from.X) / length, (to.Y - from.Y) / length, (to.Z - from.Z) / length);
            double clear = length, envelope = CameraRadius + SolidRadius;
            foreach (var centre in Centres)
            {
                var offset = V(from.X - centre.X, from.Y - centre.Y, from.Z - centre.Z);
                double separation = from.Distance(centre);
                if (separation <= envelope)
                {
                    if (!JourneyEgressPolicy.MovesOut(direction, offset, envelope - separation, CameraRadius))
                        return 0;
                    continue;
                }
                double projection = offset.X * direction.X + offset.Y * direction.Y + offset.Z * direction.Z;
                double discriminant = projection * projection - separation * separation + envelope * envelope;
                if (projection < 0 && discriminant >= 0)
                    clear = Math.Min(clear, Math.Max(0, -projection - Math.Sqrt(discriminant)));
            }
            return clear;
        }
    }

    private static void NewlyOccupiedPose()
    {
        var world = new World();
        var destination = V(528, 8);
        var guide = new JourneyGuide(V(0, 8), destination, 12, 4);
        for (int frame = 0; guide.Position.X < 66 && frame < 1000; frame++)
        {
            guide.Step(1.0 / 30, destination, world);
            Require(guide.Speed > 0, "Open ground unexpectedly stopped the guide");
        }
        Require(guide.Position.X >= 66, "Open-ground setup did not reach 66 m");
        var trap = guide.Position;
        // Two solids load partway into the camera envelope behind the guide.
        // The optical centre stays outside both; forward is unobstructed.
        world.Centres.Add(V(trap.X - .81, trap.Y, trap.Z - .46));
        world.Centres.Add(V(trap.X - .81, trap.Y, trap.Z + .46));
        double initial = trap.Distance(world.Centres[0]);
        Require(initial > World.SolidRadius && initial < World.CameraRadius + World.SolidRadius, "Missing initial penetration");
        for (int frame = 0; frame < 1000 && !guide.Arrived; frame++)
        {
            guide.Step(1.0 / 30, destination, world);
            foreach (var centre in world.Centres)
                Require(guide.Position.Distance(centre) >= initial - .0001, "Travel increased penetration");
            if (frame == 59) Require(guide.Position.X > trap.X + 2, "Guide stayed pinned despite a safe forward corridor");
        }
        // Retain the destination across the transient occupancy.
        for (int frame = 0; frame < 1500 && !guide.Arrived; frame++) guide.Step(1.0 / 30, destination, world);
        Require(guide.Arrived, "Failed to reach original destination after contact escape");
    }

    private static void MultipleBodies()
    {
        var world = new World();
        var from = V(0, 8);
        world.Centres.Add(V(-.81, 8, -.46));
        world.Centres.Add(V(-.81, 8, .46));
        Require(world.Clearance(from, V(0, 8, 3)) == 0, "Direction exits one contact but penetrates the other");
        Require(world.Clearance(from, V(-1, 8)) == 0, "Cannot move into touching bodies");
        world.Centres.Add(V(3, 8));
        double clear = world.Clearance(from, V(10, 8));
        Require(Math.Abs(clear - (3 - World.CameraRadius - World.SolidRadius)) < .00001,
            "Releasing initial contacts hid a new obstruction ahead");
    }

    private static void WholeBounds()
    {
        // A diagonal travel direction may be separating even if the bounds
        // extends forward along an individual world coordinate.
        var direction = V(1, 0, -1);
        Require(JourneyEgressPolicy.BoundsBehindMovement(V(-.6, 0, .5), V(.7, 3, .2), direction),
            "Whole collider clearly behind oblique motion rejected");
        Require(!JourneyEgressPolicy.BoundsBehindMovement(V(-.6, 0, .5), V(.7, 3, .5), direction),
            "Only the centre of an arbitrary collider was behind the camera");
        Require(!JourneyEgressPolicy.BoundsBehindMovement(V(-1), V(1), V(1)), "Touching support plane must fail");
        Require(!JourneyEgressPolicy.BoundsBehindMovement(V(-1.0003), V(1), V(10)), "Tiny separation lacks safety margin");
        Require(!JourneyEgressPolicy.BoundsBehindMovement(V(-1), V(0), V(0)), "Invalid travel axis");
        Require(!JourneyEgressPolicy.BoundsBehindMovement(V(-1), V(-.1), V(1)), "Invalid bounds extents");
    }

    private static void InvalidProof()
    {
        var away = V(1);
        Require(JourneyEgressPolicy.MovesOut(away, away, 0, .4), "Exact exterior contact should allow outward motion");
        Require(!JourneyEgressPolicy.MovesOut(V(0, 1), away, .1, .4), "Tangent movement must not claim separation");
        Require(!JourneyEgressPolicy.MovesOut(V(.001, 1), away, .1, .4), "Grazing contact lacks positive clearance margin");
        Require(!JourneyEgressPolicy.MovesOut(away, V(0), .1, .4), "Missing plane normal");
        Require(!JourneyEgressPolicy.MovesOut(away, away, .4, .4), "Centre at solid boundary");
        Require(!JourneyEgressPolicy.MovesOut(away, away, .5, .4), "Centre inside solid");
        Require(!JourneyEgressPolicy.MovesOut(away, away, double.NaN, .4), "Unmeasured depth");
    }
}
