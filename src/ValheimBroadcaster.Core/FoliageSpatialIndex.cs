using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>Conservative broad phase for one immutable renderer inventory.
/// Mobile, unknown or excessively large observations are returned for every
/// query. The adapter must still read current bounds and validate coverage.</summary>
public sealed class FoliageSpatialIndex
{
    public const int Capacity = 8192;
    private const double CellSize = 16;
    private const int MaximumCellsPerEntry = 16;
    private readonly Dictionary<(int X, int Z), List<int>> cells = new Dictionary<(int X, int Z), List<int>>();
    private readonly List<int> global = new List<int>();
    private readonly int[] visited = new int[Capacity];
    private int stamp, count;
    public int Count => count;
    public int GlobalCount => global.Count;

    public void Reset()
    { cells.Clear(); global.Clear(); count = 0; stamp = 0; Array.Clear(visited, 0, visited.Length); }

    /// <summary>IDs are contiguous inventory offsets; no geometry may be
    /// silently omitted when its bounds or stationary classification is absent.</summary>
    public void Add(int id, double minX, double minZ, double maxX, double maxZ, bool stationary)
    {
        if (id != count || id < 0 || id >= Capacity) throw new ArgumentOutOfRangeException(nameof(id));
        count++;
        if (!stationary || !Range(minX, minZ, maxX, maxZ, out int x0, out int z0, out int x1, out int z1) ||
            (long)(x1 - x0 + 1) * (z1 - z0 + 1) > MaximumCellsPerEntry)
        { global.Add(id); return; }
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            if (!cells.TryGetValue((x, z), out List<int>? entries)) cells.Add((x, z), entries = new List<int>());
            entries.Add(id);
        }
    }

    /// <summary>Returns a conservative box around the query sphere, without a
    /// complete-inventory scan. IDs are unique and always refer to this index.</summary>
    public void Query(double x, double z, double radius, List<int> result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (!Finite(x) || !Finite(z) || !Finite(radius) || radius < 0 || radius > 220)
            throw new ArgumentOutOfRangeException(nameof(radius));
        result.Clear();
        if (++stamp == int.MaxValue) { Array.Clear(visited, 0, visited.Length); stamp = 1; }
        foreach (int id in global) Include(id, result);
        if (!Range(x - radius, z - radius, x + radius, z + radius, out int x0, out int z0, out int x1, out int z1))
        { for (int id = 0; id < count; id++) Include(id, result); return; }
        for (int cx = x0; cx <= x1; cx++)
        for (int cz = z0; cz <= z1; cz++)
            if (cells.TryGetValue((cx, cz), out List<int>? entries))
                foreach (int id in entries) Include(id, result);
    }

    private void Include(int id, List<int> result)
    { if (visited[id] != stamp) { visited[id] = stamp; result.Add(id); } }
    private static bool Range(double minX, double minZ, double maxX, double maxZ,
        out int x0, out int z0, out int x1, out int z1)
    {
        x0 = z0 = x1 = z1 = 0;
        if (!Finite(minX) || !Finite(minZ) || !Finite(maxX) || !Finite(maxZ) || minX > maxX || minZ > maxZ ||
            Math.Abs(minX) > 1000000 || Math.Abs(minZ) > 1000000 || Math.Abs(maxX) > 1000000 || Math.Abs(maxZ) > 1000000) return false;
        x0 = (int)Math.Floor(minX / CellSize); x1 = (int)Math.Floor(maxX / CellSize);
        z0 = (int)Math.Floor(minZ / CellSize); z1 = (int)Math.Floor(maxZ / CellSize);
        return true;
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
