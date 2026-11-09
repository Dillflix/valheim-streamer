using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ValheimBroadcaster.Core;

// One area per zone, newest first across the world. The handle stays with
// the server so it can revalidate the exact object before advertising it.
public sealed class RecentGravestones<T>
{
    public const int Limit = 16;
    public sealed class Marker
    {
        public readonly string Id;
        public readonly ObserverPoint Position;
        public readonly long DeathTicks;
        public readonly T Handle;
        internal Marker(string id, ObserverPoint position, long ticks, T handle)
        { Id = id; Position = position; DeathTicks = ticks; Handle = handle; }
    }
    private readonly Dictionary<string, Marker> markers = new Dictionary<string, Marker>(StringComparer.Ordinal);
    public IEnumerable<Marker> Markers => markers.Values;
    public Marker? Find(string id) => markers.TryGetValue(id, out var marker) ? marker : null;
    public int Count => markers.Count;
    public void Clear() => markers.Clear();

    public static double Remaining(long worldTicks, long deathTicks, int minutes)
    {
        if (minutes <= 0 || deathTicks <= 0 || worldTicks < deathTicks) return 0;
        double age = (worldTicks - deathTicks) / (double)TimeSpan.TicksPerSecond;
        return Math.Max(0, minutes * 60.0 - age);
    }

    public void Observe(int zoneX, int zoneZ, ObserverPoint position, long deathTicks, T handle)
    {
        string id = "grave:" + zoneX.ToString(CultureInfo.InvariantCulture) + ":" + zoneZ.ToString(CultureInfo.InvariantCulture);
        if (markers.TryGetValue(id, out var prev))
        {
            if (deathTicks <= prev.DeathTicks) return;
        }
        else if (markers.Count == Limit)
        {
            Marker oldest = markers.Values.OrderBy(m => m.DeathTicks).ThenByDescending(m => m.Id, StringComparer.Ordinal).First();
            if (deathTicks < oldest.DeathTicks || deathTicks == oldest.DeathTicks && StringComparer.Ordinal.Compare(id, oldest.Id) >= 0) return;
            markers.Remove(oldest.Id);
        }
        markers[id] = new Marker(id, position, deathTicks, handle);
    }
}
