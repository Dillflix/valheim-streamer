using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>
    /// Bounded viewing credit for players actually visible in useful footage.
    /// Group membership alone never earns credit. The adapter supplies clear,
    /// readable player IDs, or an empty set for held/slate/unusable footage.
    /// </summary>
    public sealed class PlayerCoverageHistory
    {
        public const int Capacity = 128;
        public const double MinimumFootageSeconds = 6;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private double lastRecord = -1;
        private long sequence;

        private sealed class Entry
        {
            internal double LastSeen, Continuous, CreditedAt = double.NegativeInfinity;
            internal long Sequence;
        }

        public int Count => entries.Count;

        public void Record(double now, double dt, IReadOnlyList<string> visibleIds)
        {
            ValidateTime(now, nameof(now)); ValidateTime(dt, nameof(dt));
            if (lastRecord >= 0 && now < lastRecord) throw new ArgumentOutOfRangeException(nameof(now));
            if (visibleIds == null) throw new ArgumentNullException(nameof(visibleIds));
            if (visibleIds.Count > Capacity) throw new ArgumentException("Coverage exceeds the bounded player capacity.", nameof(visibleIds));
            seen.Clear();
            foreach (string id in visibleIds)
            {
                if (string.IsNullOrWhiteSpace(id) || id.Length > 128) throw new ArgumentException("Expected a bounded player ID.", nameof(visibleIds));
                seen.Add(id);
            }
            // Repeated callbacks for the same frame cannot manufacture dwell.
            if (lastRecord == now) return;
            double elapsed = lastRecord < 0 ? dt : Math.Min(dt, now - lastRecord);
            bool continuous = lastRecord < 0 || now - lastRecord <= 1.5;
            double footage = Math.Min(1, elapsed);
            lastRecord = now; sequence++;
            foreach (string id in seen)
            {
                if (!entries.TryGetValue(id, out Entry entry))
                {
                    if (entries.Count == Capacity) RemoveOldest();
                    entry = new Entry(); entries.Add(id, entry);
                }
                if (!continuous || entry.Sequence != sequence - 1) entry.Continuous = 0;
                entry.Continuous = Math.Min(MinimumFootageSeconds, entry.Continuous + footage);
                entry.LastSeen = now; entry.Sequence = sequence;
                if (entry.Continuous >= MinimumFootageSeconds) entry.CreditedAt = now;
            }
        }

        public bool RecentlyCovered(string id, double now, double withinSeconds)
        {
            ValidateTime(now, nameof(now)); ValidateTime(withinSeconds, nameof(withinSeconds));
            return entries.TryGetValue(id, out Entry entry) && now >= entry.CreditedAt &&
                now - entry.CreditedAt < withinSeconds;
        }

        public void Reset() { entries.Clear(); seen.Clear(); lastRecord = -1; sequence = 0; }

        private void RemoveOldest()
        {
            string? oldest = null; double at = double.PositiveInfinity;
            foreach (var pair in entries)
                if (pair.Value.LastSeen < at || pair.Value.LastSeen == at && StringComparer.Ordinal.Compare(pair.Key, oldest) < 0)
                { oldest = pair.Key; at = pair.Value.LastSeen; }
            if (oldest != null) entries.Remove(oldest);
        }

        private static void ValidateTime(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
    }
}
