using System;
using System.Runtime.InteropServices;
using ValheimBroadcaster.Core;

namespace Journey.NativeTerrain;

internal sealed class NativeLand : IJourneyGuideWorld, IJourneyTerrainContact
{
    internal const double Radius = .82;
    internal const double GridStep = 2;
    private readonly bool legacyTileVeto;
    private readonly float[] vertices = new float[9];
    internal float? RoofHeight;
    internal int QueryCount { get; private set; }

    internal NativeLand(bool legacyTileVeto) => this.legacyTileVeto = legacyTileVeto;

    [DllImport("journeyterrain", EntryPoint="terrain_height")]
    private static extern float Ground(float x, float z);
    [DllImport("journeyterrain", EntryPoint="terrain_overlap")]
    private static extern int Overlap(float x, float y, float z);
    [DllImport("journeyterrain", EntryPoint="terrain_clearance")]
    private static extern float Sweep(float x, float y, float z, float endX, float endY, float endZ,
        int verticalRelease, int roof, float roofHeight);
    [DllImport("journeyterrain", EntryPoint="terrain_triangle")]
    private static extern void Triangle(int x, int z, int face, [Out] float[] coords);

    internal static bool TouchesTerrain(AutonomyPosition p) => Overlap(p.X, p.Y, p.Z) != 0;

    public bool TryGetSurface(AutonomyPosition p, out double height)
    {
        height = Ground(p.X, p.Z);
        return true;
    }

    private double SupportedHeight(AutonomyPosition p)
    {
        double required = double.NegativeInfinity;
        for (int i = (int)Math.Floor((p.X - Radius) / GridStep); i <= Math.Floor((p.X + Radius) / GridStep); i++)
            for (int j = (int)Math.Floor((p.Z - Radius) / GridStep); j <= Math.Floor((p.Z + Radius) / GridStep); j++)
                for (int face = 0; face < 2; face++)
                {
                    Triangle(i, j, face, vertices);
                    if (!JourneyTerrainContactPolicy.TryRequiredHeight(p, Radius, Corner(0), Corner(3),
                            Corner(6), out double height))
                        throw new InvalidOperationException("Invalid heightfield face");
                    required = Math.Max(required, height);
                }
        return required;
    }

    private AutonomyPosition Corner(int i) => new(vertices[i], vertices[i+1], vertices[i+2]);

    public bool NeedsVerticalEscape(AutonomyPosition p) =>
        !legacyTileVeto && p.Y <= SupportedHeight(p) + .06;

    public double Clearance(AutonomyPosition a, AutonomyPosition b)
    {
        double requested = a.Distance(b);
        if (requested < .000001) return 0;
        QueryCount++;
        bool release = false;
        if (TouchesTerrain(a))
        {
            if (legacyTileVeto)
            {
                // Exact bounds from the trace. The baseline case starts in this tile.
                var relativeCenter = new AutonomyPosition(-1728f - a.X, 141.97f - a.Y, -768f - a.Z);
                var movement = new AutonomyPosition(b.X-a.X, b.Y-a.Y, b.Z-a.Z);
                release = JourneyEgressPolicy.BoundsBehindMovement(relativeCenter,
                    new AutonomyPosition(32f, 24.60f, 32f), movement);
            }
            else
                release = JourneyTerrainContactPolicy.CanRise(a, b, Radius, Ground(a.X, a.Z), SupportedHeight(a));
            if (!release) return 0;
        }
        double measured = Sweep(a.X,a.Y,a.Z,b.X,b.Y,b.Z,release ? 1 : 0,
            RoofHeight.HasValue ? 1 : 0, RoofHeight.GetValueOrDefault());
        return requested - measured < .00001 ? requested : measured;
    }
}

internal static class Program
{
    private static readonly AutonomyPosition Recorded = new(-1730.37f,139.73f,-792.59f);

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    private static void TestContact(AutonomyPosition from)
    {
        var old = new NativeLand(true);
        var current = new NativeLand(false);
        var up = new AutonomyPosition(from.X,from.Y+4,from.Z);
        Check(NativeLand.TouchesTerrain(from), "Native PhysX should overlap this reconstructed slope");
        Check(old.Clearance(from,up) == 0 && current.Clearance(from,up) == 4,
            "Expected a 4 m vertical release previously vetoed by the tile box");
        Check(!NativeLand.TouchesTerrain(up), "Lift endpoint still overlaps terrain");
        Check(current.Clearance(from,new AutonomyPosition(up.X+.1f, up.Y, up.Z)) == 0,
            "Diagonal escape through the starting terrain was admitted");
        current.RoofHeight = from.Y + 1.50f;
        double gap = current.Clearance(from, up);
        Check(gap > .62 && gap < .66, "Overhead swept obstacle must independently limit the lift");
        Console.WriteLine($"Contact at {from.X},{from.Y},{from.Z}: overlap, 4 m upward clearance, {gap:F3} m with roof");
    }

    private static void Run(bool legacy)
    {
        var world = new NativeLand(legacy);
        var goal = new AutonomyPosition(Recorded.X,0,Recorded.Z+2045);
        var guide = new JourneyGuide(Recorded,goal,12,16);
        double time=0, clearedAt=-1;
        const double dt=1.0/60;
        while (time < (legacy ? 34 : 300) && !guide.Arrived)
        {
            var from=guide.Position;
            guide.Step(dt,goal,world);
            time += dt;
            var to=guide.Position;
            if (NativeLand.TouchesTerrain(to))
                Check(clearedAt < 0 && to.X == Recorded.X && to.Z == Recorded.Z && to.Y >= from.Y,
                    "Movement entered terrain outside strictly upward initial egress");
            else if (clearedAt < 0) clearedAt = time;
            double distance = from.Distance(to);
            Check(distance <= 12 * dt + .001 && world.Clearance(from,to)+.001 >= distance,
                "Unsafe guide step");
        }
        Console.WriteLine($"legacy tile rule={legacy}, arrived={guide.Arrived}, travel={guide.Travelled:F2} m," +
            $" elapsed={time:F2} s, contact cleared at {clearedAt:F3} s, sweeps/checks={world.QueryCount}");
        Check(legacy ? guide.Travelled == 0 && !guide.Arrived :
            guide.Arrived && guide.Travelled >= 2000 && clearedAt >= 0 &&
            guide.Position.HorizontalDistance(goal)<=16,"Journey acceptance failed");
    }

    public static void Main()
    {
        TestContact(Recorded);
        var world = new NativeLand(false);
        // The 0.82 m footprint spans z=-800, the edge of the logged 64 m tile.
        foreach (float z in new[] {-799.75f,-800.25f})
        {
            var p = new AutonomyPosition(Recorded.X,0,z);
            world.TryGetSurface(p, out double ground);
            TestContact(new AutonomyPosition(p.X,(float)(ground+1.13),p.Z));
        }
        Run(legacy:true);
        Run(legacy:false);
    }
}
