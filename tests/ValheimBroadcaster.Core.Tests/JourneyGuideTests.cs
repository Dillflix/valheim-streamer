using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyGuideTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey steers two kilometres across a simulated coast, ridge and obstructed forest at travel pace", MixedTerrain);
        yield return ("Journey traverses offset stands of trees without losing progress", ForestPass);
        yield return ("Journey crosses two kilometres with tiles loading around a moving observer", RollingTiles);
        yield return ("Journey escapes a U-shaped enclosure and climbs a steep escarpment", Bluff);
        yield return ("Journey recovers when a raised water surface overtakes its current position", RaisedWater);
        yield return ("Selected two-kilometre itinerary completes only after measured travel and displayed arrival", ScheduledJourney);
        yield return ("Journey brakes at an unloaded region and resumes on the same mission", LoadedBoundary);
        yield return ("Journey stays in place when surrounded and proceeds when a corridor opens", Enclosure);
        yield return ("Journey cannot claim arrival at an intermediate steering target", IntermediateTarget);
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
    }

    private static AutonomyPosition Point(double x, double y, double z) =>
        new AutonomyPosition((float)x, (float)y, (float)z);

    // The world is independent of the guide's candidates. It contains a shoreline,
    // an ocean crossing, rolling ground and two finite obstacles on the direct course.
    private sealed class Landscape : IJourneyGuideWorld
    {
        internal bool Mixed;
        internal bool Enclosed;
        internal bool Forest, Bluff, Flood;
        internal readonly Dictionary<(int X, int Z), double> Tiles = new();
        internal bool Rolling;
        internal double Clock;
        internal int BlockedTileSamples;

        internal void StreamAround(AutonomyPosition observer)
        {
            int x = (int)Math.Floor(observer.X / 64), z = (int)Math.Floor(observer.Z / 64);
            foreach (var key in new List<(int X, int Z)>(Tiles.Keys))
                if (Math.Abs(x - key.X) > 1 || Math.Abs(z - key.Z) > 1) Tiles.Remove(key);
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
                Tiles.TryAdd((x + i, z + j), Clock == 0 ? 0 : Clock + (i % 2 == 0 ? 4 : 7));
        }

        private bool Loaded(AutonomyPosition at)
        {
            if (!Rolling) return at.X <= LoadedThrough;
            var key = ((int)Math.Floor(at.X / 64), (int)Math.Floor(at.Z / 64));
            if (Tiles.TryGetValue(key, out double ready) && ready <= Clock) return true;
            BlockedTileSamples++;
            return false;
        }

        internal double LoadedThrough = double.PositiveInfinity;
        internal int Queries;

        internal double Surface(AutonomyPosition at)
        {
            if (Flood) return 10;
            if (Bluff) return Math.Max(0, Math.Min(26, (at.X - 100) * 1.7));
            if (!Mixed) return 0;
            // The smooth island profile falls below sea level in the middle of the trip.
            return Math.Max(0, 12 + 22 * Math.Cos(at.X * Math.PI / 640.0) + 3 * Math.Sin(at.Z / 80.0));
        }

        public bool TryGetSurface(AutonomyPosition at, out double height)
        {
            Queries++;
            height = Surface(at);
            return Loaded(at);
        }

        internal bool Traversable(AutonomyPosition at)
        {
            if (!Loaded(at) || at.Y < Surface(at) + 0.5) return false;
            if (Bluff && at.Y < 18)
            {
                bool back = Math.Abs(at.X - 25) < 2 && Math.Abs(at.Z) < 12;
                bool sides = Math.Abs(Math.Abs(at.Z) - 12) < 2 && at.X > -15 && at.X < 25;
                if (back || sides) return false;
            }
            if (Forest)
            {
                // Two opposing lines with an offset gap, rising above the guide.
                // The direct path would clip trunks on both sides of the passage.
                for (int i = 0; i < 7; i++)
                {
                    if (InsideTrunk(at, 65, -3 + i * 2.5) || InsideTrunk(at, 84, 3 - i * 2.5)) return false;
                }
            }
            if (!Mixed) return true;
            bool trunk = Math.Abs(at.X - 175) < 3 && Math.Abs(at.Z) < 5 && at.Y < Surface(at) + 21;
            bool outcrop = Math.Abs(at.X - 1550) < 6 && Math.Abs(at.Z) < 12 && at.Y < Surface(at) + 24;
            return !(trunk || outcrop);
        }

        private static bool InsideTrunk(AutonomyPosition at, double x, double z)
        {
            double dx = at.X - x, dz = at.Z - z;
            return dx * dx + dz * dz < 1.75 * 1.75 && at.Y < 24;
        }

        public double Clearance(AutonomyPosition from, AutonomyPosition to)
        {
            Queries++;
            if (Flood && from.Y < Surface(from) + 0.5)
            {
                bool rising = to.Y > from.Y && to.HorizontalDistance(from) <= 0.0001;
                return rising ? from.Distance(to) : 0;
            }
            if (Enclosed || !Traversable(from)) return 0;
            double distance = from.Distance(to);
            int intervals = Math.Max(1, (int)Math.Ceiling(distance / 0.25));
            for (int i = 1; i <= intervals; i++)
            {
                double t = (double)i / intervals;
                if (!Traversable(Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t,
                        from.Z + (to.Z - from.Z) * t)))
                    return distance * (i - 1) / intervals;
            }
            return distance;
        }
    }

    private static void MixedTerrain()
    {
        var world = new Landscape { Mixed = true };
        var start = Point(0, world.Surface(Point(0, 0, 0)) + 7, 0);
        var destination = Point(2026, world.Surface(Point(2026, 0, 0)), 0);
        const double cruise = 12, radius = 10;
        var guide = new JourneyGuide(start, destination, cruise, radius);
        double elapsed = 0, fastSeconds = 0, longestIdle = 0, idle = 0, firstMetre = 0;
        bool crossedTree = false, crossedOutcrop = false, crossedSea = false;
        // Timing jitter exercises the production frame-rate-independent integration.
        for (int frame = 0; elapsed < 230 && !guide.Arrived; frame++)
        {
            double dt = frame % 5 == 0 ? 1.0 / 24 : 1.0 / 45;
            AutonomyPosition previous = guide.Position;
            world.Queries = 0;
            guide.Step(dt, destination, world);
            AutonomyPosition current = guide.Position;
            double moved = previous.Distance(current);
            Check(world.Queries < 100, "Unbounded world sampling in a guide frame");
            Check(world.Traversable(current), $"Guide moved into solid geometry at {current.X},{current.Y},{current.Z}");
            Check(moved <= cruise * dt + 0.02, $"Pose jumped {moved:F2} m in {dt:F3} s");
            elapsed += dt;
            if (firstMetre == 0 && guide.Travelled >= 1) firstMetre = elapsed;
            if (moved / dt > cruise * 0.8) fastSeconds += dt;
            idle = moved < 0.002 ? idle + dt : 0;
            longestIdle = Math.Max(longestIdle, idle);
            crossedTree |= current.X > 175;
            crossedSea |= current.X > 500 && current.X < 800 && world.Surface(current) == 0;
            crossedOutcrop |= current.X > 1550;
        }
        Check(guide.Arrived && guide.Position.HorizontalDistance(destination) <= radius + 0.05,
            $"Guide ended at x={guide.Position.X:F1}, z={guide.Position.Z:F1}, elapsed={elapsed:F1}, {guide.Status}");
        Check(crossedTree && crossedSea && crossedOutcrop && guide.Travelled >= 2000,
            "Arrival omitted part of the course");
        Check(firstMetre > 0 && firstMetre < 2 && longestIdle < 2 && fastSeconds > 130,
            $"Unacceptable travel pace: first metre {firstMetre:F2}s, idle {longestIdle:F2}s, cruising {fastSeconds:F1}s");
        Console.WriteLine($"Simulated guide: {guide.Travelled:F1} m in {elapsed:F1} s, first metre {firstMetre:F2} s, " +
            $"longest idle {longestIdle:F2} s, {fastSeconds:F1} s above 80% cruise");
        AutonomyPosition arrival = guide.Position;
        guide.Step(0.1, destination, world);
        Check(guide.Arrived && guide.Position.Distance(arrival) < 0.001, "Arrival drifted beyond the destination");
    }

    private static void ForestPass()
    {
        var world = new Landscape { Forest = true };
        var destination = Point(260, 0, 0);
        var guide = new JourneyGuide(Point(0, 7, 0), destination, 12, 8);
        const double dt = 0.05;
        for (int i = 0; i < 800 && !guide.Arrived; i++)
        {
            guide.Step(dt, destination, world);
            Check(world.Traversable(guide.Position), "Guide clipped a trunk in the offset forest passage");
        }
        Check(guide.Arrived, $"Forested travel stalled at {guide.Position.X:F1},{guide.Position.Y:F1},{guide.Position.Z:F1}: {guide.Status}");
    }

    private static void RollingTiles()
    {
        var world = new Landscape { Rolling = true, Mixed = true, Forest = true };
        var start = Point(0, world.Surface(Point(0, 0, 0)) + 7, 0);
        var destination = Point(2026, 0, 0);
        var guide = new JourneyGuide(start, destination, 12, 10);
        const double dt = 0.05;
        var observer = start;
        for (int i = 0; i < 5600 && !guide.Arrived; i++)
        {
            world.StreamAround(observer);
            guide.Step(dt, destination, world);
            Check(world.Traversable(guide.Position), $"Guide entered an unloaded tile or solid at {guide.Position.X:F1}");
            // Delayed loading interest follows the accepted position from the preceding step.
            observer = guide.Position;
            world.Clock += dt;
        }
        Check(world.BlockedTileSamples > 0, "Fixture never exercised an unloaded neighbouring tile");
        Check(guide.Arrived && guide.Travelled >= 2000 && guide.Position.HorizontalDistance(destination) <= 10.05,
            $"Guide stalled against its own moving loading frontier at {guide.Position.X:F1}: {guide.Status}");
        Console.WriteLine($"Simulated moving tile window: {guide.Travelled:F1} m in {world.Clock:F1} s");
    }

    private static void Bluff()
    {
        var world = new Landscape { Bluff = true };
        var guide = new JourneyGuide(Point(0, 6, 0), Point(260, 26, 0), 12, 8);
        for (int i = 0; i < 1500 && !guide.Arrived; i++)
        {
            guide.Step(0.05, Point(260, 26, 0), world);
            Check(world.Traversable(guide.Position), "Guide intersected the enclosure or escarpment");
        }
        Check(guide.Arrived, $"Escarpment travel stalled at {guide.Position.X:F1},{guide.Position.Y:F1},{guide.Position.Z:F1}: {guide.Status}");
    }

    private static void RaisedWater()
    {
        var world = new Landscape();
        var guide = new JourneyGuide(Point(0, 7, 0), Point(150, 0, 0), 12, 8);
        for (int i = 0; i < 50; i++) guide.Step(0.05, Point(150, 0, 0), world);
        var beforeFlood = guide.Position;
        world.Flood = true;
        for (int i = 0; i < 800 && !guide.Arrived; i++) guide.Step(0.05, Point(150, 0, 0), world);
        Check(guide.Arrived && guide.Position.Y > beforeFlood.Y,
            $"Rising surface stranded the guide at {guide.Position.X:F1},{guide.Position.Y:F1}: {guide.Status}");
    }

    private static void ScheduledJourney()
    {
        var world = new Landscape { Mixed = true, Forest = true, Rolling = true };
        var start = Point(0, world.Surface(Point(0, 0, 0)) + 7, 0);
        var regions = new List<ScenicRegion>();
        for (int i = 1; i <= 12; i++)
            regions.Add(new ScenicRegion($"tile-{i}", "Meadows", Point(i * 160, 0, Math.Sin(i * 0.7) * 24)));
        regions.Add(new ScenicRegion("original-site", "BlackForest", Point(2026, 0, 0)));
        bool selected = ScenicJourneyItinerary.TrySelect(start, Point(1, 0, 0), regions, null, 2000, 0, null,
            out ScenicJourneyItinerary? choice, out string reason);
        Check(selected && choice != null && choice.Destination.Id == "original-site" && choice.UsesConnectedObservations,
            "Selection did not reach the observed destination via the recorded catalogue: " + reason);
        var itinerary = choice!;
        var mission = new ScenicJourneyPolicy();
        mission.Start(71, itinerary, 12);
        var guide = new JourneyGuide(start, itinerary.Destination.Position, 12, itinerary.ArrivalRadius);
        const double dt = 0.05;
        double heldAt = -1, initialArrival = -1;
        for (int i = 0; i < 7000 && mission.Active; i++)
        {
            world.StreamAround(guide.Position);
            if (heldAt < 0 && guide.Position.X > 750) heldAt = world.Clock;
            bool operatorHold = heldAt >= 0 && world.Clock - heldAt < 1.5;
            double before = mission.TravelMetres;
            if (!operatorHold) guide.Step(dt, mission.SteeringGoal(guide.Position), world);
            world.Clock += dt;
            mission.ObserveGuide(world.Clock, !operatorHold, operatorHold, guide.Position);
            if (operatorHold)
                Check(mission.TravelMetres == before, "Held coverage accrued artificial distance");
            if (guide.Arrived && initialArrival < 0) initialArrival = world.Clock;
            // The first second at the destination is held output; the mission must
            // accrue three actual seconds of visible arrival after live footage resumes.
            bool arrivalHeld = guide.Arrived && world.Clock - initialArrival < 1;
            mission.ObserveArrival(world.Clock, guide.Arrived && !arrivalHeld, arrivalHeld, guide.Position, guide.Arrived);
            if (arrivalHeld) Check(mission.ArrivalShownSeconds == 0, "Held image counted as arrival dwell");
            if (mission.TargetReached) mission.Finish(ScenicJourneyOutcome.Completed, "Destination presented");
            Check(!mission.BudgetExpired, "Journey exceeded its actual programme budget");
        }
        Check(heldAt >= 0 && mission.Outcome == ScenicJourneyOutcome.Completed && mission.ArrivalShownSeconds >= 3 &&
            mission.TravelMetres >= 2000 && initialArrival > 0 && world.Clock - initialArrival >= 4,
            $"Mission {mission.Outcome} after {mission.TravelMetres:F1} m and {mission.ArrivalShownSeconds:F2} s at destination: {mission.Reason}");
        Console.WriteLine($"Itinerary guide: {mission.TravelMetres:F1} m presented, {mission.RouteProgressMetres:F1} m route progress, " +
            $"{world.Clock:F1} s including operator hold and arrival dwell");
    }

    private static void LoadedBoundary()
    {
        var world = new Landscape { LoadedThrough = 61 };
        var destination = Point(215, 0, 0);
        var guide = new JourneyGuide(Point(0, 7, 0), destination, 12, 6);
        for (int i = 0; i < 500; i++)
        {
            guide.Step(0.05, destination, world);
            Check(guide.Position.X <= world.LoadedThrough, "Guide crossed into an unobserved region");
        }
        Check(!guide.Arrived && guide.Position.X > 30, "Unloaded horizon prevented all travel or falsely credited arrival");
        world.LoadedThrough = double.PositiveInfinity;
        for (int i = 0; i < 750 && !guide.Arrived; i++) guide.Step(0.05, destination, world);
        Check(guide.Arrived && guide.Position.HorizontalDistance(destination) <= 6.05,
            $"Guide did not resume after the region loaded: {guide.Status}");
    }

    private static void Enclosure()
    {
        var world = new Landscape { Enclosed = true };
        var start = Point(9, 8, -10);
        var destination = Point(-200, 0, 110);
        var guide = new JourneyGuide(start, destination, 10, 7);
        for (int i = 0; i < 120; i++) guide.Step(0.05, destination, world);
        Check(guide.Position.Distance(start) < 0.001 && guide.Travelled == 0 && !guide.Arrived,
            "Obstructed guide moved, travelled through a wall, or claimed completion");
        world.Enclosed = false;
        for (int i = 0; i < 900 && !guide.Arrived; i++) guide.Step(0.05, destination, world);
        Check(guide.Arrived, $"Guide could not leave cleared enclosure: {guide.Status}");
    }

    private static void IntermediateTarget()
    {
        var world = new Landscape();
        var destination = Point(200, 0, 0);
        var guide = new JourneyGuide(Point(0, 7, 0), destination, 10, 6);
        var waypoint = Point(10, 7, 45);
        for (int i = 0; i < 400 && guide.Position.HorizontalDistance(waypoint) > 8; i++)
            guide.Step(0.05, waypoint, world);
        Check(guide.Position.HorizontalDistance(waypoint) <= 8 && !guide.Arrived,
            "Intermediate waypoint was missed or was counted as destination arrival");
        for (int i = 0; i < 800 && !guide.Arrived; i++) guide.Step(0.05, destination, world);
        Check(guide.Arrived, $"Guide failed to retarget after its waypoint: {guide.Status}");
    }
}
