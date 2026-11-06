using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>World-space cells and headings actually travelled during this mission.
/// A return along a passage can be useful; repeating the same oriented passage
/// does not qualify as a new detour. History never certifies camera clearance.</summary>
public sealed class JourneyTrail
{
    private readonly HashSet<(int X, int Y, int Z, int Heading)> visited = new();
    private AutonomyPosition? anchor;
    public int Count => visited.Count;

    internal static (int, int, int, int) Key(AutonomyPosition point, double dx, double dz)
    {
        int heading = (int)Math.Round(Math.Atan2(dz, dx) * 4 / Math.PI);
        return ((int)Math.Floor(point.X / 4), (int)Math.Floor(point.Y / 4),
            (int)Math.Floor(point.Z / 4), (heading + 8) % 8);
    }

    public void Observe(AutonomyPosition point, bool continuous)
    {
        if (!continuous || !anchor.HasValue) { anchor = point; return; }
        AutonomyPosition from = anchor.Value;
        if (from.HorizontalDistance(point) < 2) return;
        visited.Add(Key(point, point.X - from.X, point.Z - from.Z));
        anchor = point;
    }

    public JourneyTrailSnapshot Snapshot() => new(visited);
    public void Reset() { visited.Clear(); anchor = null; }
}

public sealed class JourneyTrailSnapshot
{
    private readonly HashSet<(int X, int Y, int Z, int Heading)> visited;
    internal JourneyTrailSnapshot(IEnumerable<(int, int, int, int)> source) => visited = new(source);

    public bool Repeats(AutonomyPosition point, AutonomyPosition heading)
    {
        var (x, y, z, dir) = JourneyTrail.Key(point, heading.X, heading.Z);
        return visited.Contains((x, y, z, dir)) || visited.Contains((x, y, z, (dir + 1) % 8))
            || visited.Contains((x, y, z, (dir + 7) % 8));
    }
}
