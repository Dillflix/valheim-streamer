using System;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Bounded spatial uncertainty, not a single radius around the region origin.
// Omitted/unknown objects invalidate the cells they touch, including all their
// extent. Rasterization expands uncertainty; it never clips it to a centre point.
// This class uses managed math only and is exercised by the native math suite.
internal sealed class FoliageCoverageMap
{
    internal const float Radius = 220, CellSize = 16;
    private const int Half = 14, Side = Half * 2;
    private readonly bool[] unknown = new bool[Side * Side * Side];
    private Vector3 origin;
    internal bool Unbounded { get; private set; }
    internal int UnknownCells { get; private set; }

    internal void Reset(Vector3 centre)
    { origin = centre; Array.Clear(unknown, 0, unknown.Length); Unbounded = false; UnknownCells = 0; }

    internal void Invalidate() => Unbounded = true;

    // Cancelling a scan cannot erase newly discovered uncertainty that has
    // already invalidated its published predecessor. Both maps use the same
    // published origin; an unexpected mismatch remains explicitly unknown.
    internal void RetainUnknownFrom(FoliageCoverageMap source)
    {
        if (origin != source.origin || source.Unbounded) { Invalidate(); return; }
        for (int i = 0; i < unknown.Length; i++)
            if (source.unknown[i] && !unknown[i]) { unknown[i] = true; UnknownCells++; }
    }

    internal void AddUnknown(Bounds bounds)
    {
        if (!FoliageGeometryMath.Valid(bounds)) { Invalidate(); return; }
        bounds.Expand(.4f);
        Vector3 min = bounds.min - origin, max = bounds.max - origin;
        // Guard before integer conversion; absurd but finite extents must not
        // overflow an index and disappear from the map.
        for (int x = Lower(min.x); x <= Upper(max.x); x++)
        for (int y = Lower(min.y); y <= Upper(max.y); y++)
        for (int z = Lower(min.z); z <= Upper(max.z); z++)
        {
            int index = (x * Side + y) * Side + z;
            if (!unknown[index]) { unknown[index] = true; UnknownCells++; }
        }
    }

    internal float CoverageAt(Vector3 point, float maximum)
    {
        float available = Math.Min(maximum, Radius - (point - origin).magnitude);
        if (Unbounded || !Finite(available) || available <= 0) return 0;
        Vector3 relative = point - origin;
        for (int x = Lower(relative.x - available); x <= Upper(relative.x + available); x++)
        for (int y = Lower(relative.y - available); y <= Upper(relative.y + available); y++)
        for (int z = Lower(relative.z - available); z <= Upper(relative.z + available); z++)
        {
            if (!unknown[(x * Side + y) * Side + z]) continue;
            float dx = Gap(relative.x, x), dy = Gap(relative.y, y), dz = Gap(relative.z, z);
            available = Math.Min(available, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }
        return available;
    }
    private static int Lower(float coordinate) => coordinate < -Half * CellSize ? 0 :
        coordinate >= Half * CellSize ? Side : (int)Math.Floor(coordinate / CellSize) + Half;
    private static int Upper(float coordinate) => coordinate < -Half * CellSize ? -1 :
        coordinate >= Half * CellSize ? Side - 1 : (int)Math.Floor(coordinate / CellSize) + Half;
    private static float Gap(float value, int cell) => Math.Max(0,
        Math.Max((cell - Half) * CellSize - value, value - (cell - Half + 1) * CellSize));
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
