using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ValheimBroadcaster.Core;

// Aggregate persistent construction pivots in fixed 32m cells. These are
// approximate base/area subjects, not inferred rooms or exact building meshes.
public sealed class WorldBaseIndex
{
    public const int MaxCells = 4096;
    private sealed class Cell
    {
        internal int X, Y, Z, Count;
        internal float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        internal bool Anchor;
        internal string Key => KeyOf(X, Y, Z);
    }
    private readonly Dictionary<string, Cell> cells = new Dictionary<string, Cell>(StringComparer.Ordinal);
    public bool Truncated { get; private set; }
    public int Count => cells.Count;
    private static string KeyOf(int x, int y, int z) => x.ToString(CultureInfo.InvariantCulture) + ":" + y.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture);
    public void Clear() { cells.Clear(); Truncated = false; }
    public void Add(ObserverPoint p, bool functionalAnchor)
    {
        if (Math.Abs(p.X) > 19990 || Math.Abs(p.Z) > 19990 || p.Y > 990 || p.Y < -1000) return;
        int x = (int)Math.Floor(p.X / 32), y = (int)Math.Floor(p.Y / 64), z = (int)Math.Floor(p.Z / 32);
        string key = KeyOf(x, y, z);
        if (!cells.TryGetValue(key, out Cell? c))
        {
            if (cells.Count >= MaxCells) { Truncated = true; return; }
            cells.Add(key, c = new Cell { X = x, Y = y, Z = z, MinX = p.X, MaxX = p.X, MinY = p.Y, MaxY = p.Y, MinZ = p.Z, MaxZ = p.Z });
        }
        c.Count = Math.Min(1000000, c.Count + 1); c.Anchor |= functionalAnchor;
        c.MinX = Math.Min(c.MinX, p.X); c.MaxX = Math.Max(c.MaxX, p.X);
        c.MinY = Math.Min(c.MinY, p.Y); c.MaxY = Math.Max(c.MaxY, p.Y);
        c.MinZ = Math.Min(c.MinZ, p.Z); c.MaxZ = Math.Max(c.MaxZ, p.Z);
    }
    public IReadOnlyList<WorldInterestSite> Build()
    {
        var result = new List<WorldInterestSite>(); var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (Cell seed in cells.Values.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (used.Contains(seed.Key)) continue;
            var group = new List<Cell>(); var queue = new Queue<Cell>(); queue.Enqueue(seed); used.Add(seed.Key);
            float minX = seed.MinX, maxX = seed.MaxX, minY = seed.MinY, maxY = seed.MaxY, minZ = seed.MinZ, maxZ = seed.MaxZ;
            while (queue.Count > 0)
            {
                Cell c = queue.Dequeue(); group.Add(c);
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    string key = KeyOf(c.X + x, c.Y + y, c.Z + z);
                    if (used.Contains(key) || !cells.TryGetValue(key, out Cell? n)) continue;
                    float dx = Math.Max(0, Math.Max(c.MinX - n.MaxX, n.MinX - c.MaxX));
                    float dz = Math.Max(0, Math.Max(c.MinZ - n.MaxZ, n.MinZ - c.MaxZ));
                    if (dx * dx + dz * dz > 24 * 24 || Math.Max(c.MinY, n.MinY) - Math.Min(c.MaxY, n.MaxY) > 20 ||
                        Math.Max(maxX, n.MaxX) - Math.Min(minX, n.MinX) > 300 || Math.Max(maxZ, n.MaxZ) - Math.Min(minZ, n.MinZ) > 300 ||
                        Math.Max(maxY, n.MaxY) - Math.Min(minY, n.MinY) > 200) continue;
                    used.Add(key); queue.Enqueue(n);
                    minX = Math.Min(minX, n.MinX); maxX = Math.Max(maxX, n.MaxX); minY = Math.Min(minY, n.MinY); maxY = Math.Max(maxY, n.MaxY);
                    minZ = Math.Min(minZ, n.MinZ); maxZ = Math.Max(maxZ, n.MaxZ);
                }
            }
            int count = (int)Math.Min(10000000, group.Sum(c => (long)c.Count)); bool anchor = group.Any(c => c.Anchor);
            if (count < 4 && !anchor) continue;
            Cell focus = group.OrderByDescending(c => c.Count + (c.Anchor ? 12 : 0)).ThenBy(c => c.Key, StringComparer.Ordinal).First();
            string id = "base:" + seed.Key;
            var center = new ObserverPoint((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            var extent = new ObserverPoint((maxX - minX) / 2 + 3, (maxY - minY) / 2 + 3, (maxZ - minZ) / 2 + 3);
            var arrivalHint = new ObserverPoint((focus.MinX + focus.MaxX) / 2, focus.MaxY + 4, (focus.MinZ + focus.MaxZ) / 2);
            float priority = (float)Math.Min(82, 40 + Math.Log(1 + count) * 5 + (anchor ? 8 : 0));
            result.Add(new WorldInterestSite(id, "", group.Count > 2 ? "Player settlement" : "Player base", WorldInterestKind.Base, center, extent, arrivalHint, count, priority));
            if (Math.Max(extent.X, extent.Z) < 40) continue;
            // Large settlements provide geographically distinct area hints. The
            // client still measures buildings/rooms from the loaded scene.
            foreach (Cell area in group.Where(c => c.Count >= 4 || c.Anchor).OrderByDescending(c => c.Count).ThenBy(c => c.Key, StringComparer.Ordinal).Take(12))
                result.Add(new WorldInterestSite("area:" + area.Key, id, "Settlement area", WorldInterestKind.BaseArea,
                    new ObserverPoint((area.MinX + area.MaxX) / 2, (area.MinY + area.MaxY) / 2, (area.MinZ + area.MaxZ) / 2),
                    new ObserverPoint((area.MaxX - area.MinX) / 2 + 3, (area.MaxY - area.MinY) / 2 + 3, (area.MaxZ - area.MinZ) / 2 + 3),
                    new ObserverPoint((area.MinX + area.MaxX) / 2, area.MaxY + 4, (area.MinZ + area.MaxZ) / 2), area.Count, priority - 3));
        }
        return result;
    }
}
