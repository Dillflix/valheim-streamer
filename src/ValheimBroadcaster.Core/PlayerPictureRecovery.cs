using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Picture-only fallback after the camera has had time to search.
    /// Callers may choose only other locally ready subjects, never travel.</summary>
    public sealed class PlayerPictureRecovery
    {
        public const double SearchSeconds = 12, FailedViewCooldownSeconds = 30;
        private readonly Dictionary<string, double> failedUntil = new Dictionary<string, double>(StringComparer.Ordinal);
        private string? current;
        private double lostSince = -1, lastAt = -1;
        public void Observe(string? id, double now, bool presented, bool readable)
        {
            AutonomousDirector.Time(now, nameof(now));
            bool gap = lastAt >= 0 && (now < lastAt || now - lastAt > 3);
            if (id != current || gap) lostSince = -1;
            current = id; lastAt = now;
            if (id == null || !presented || readable) lostSince = -1;
            else if (lostSince < 0) lostSince = now;
        }
        public bool ShouldTryAlternative(double now) => lostSince >= 0 && now - lostSince >= SearchSeconds;
        public bool OnCooldown(string id, double now) => failedUntil.TryGetValue(id, out double until) && now < until;
        public void LeftUnusable(string id, double now)
        {
            if (!failedUntil.ContainsKey(id) && failedUntil.Count >= 128)
            {
                string? oldest = null; double earliest = double.PositiveInfinity;
                foreach (var pair in failedUntil) if (pair.Value < earliest) { earliest = pair.Value; oldest = pair.Key; }
                if (oldest != null) failedUntil.Remove(oldest);
            }
            failedUntil[id] = now + FailedViewCooldownSeconds;
            ResetCurrent();
        }
        public void ResetCurrent() { current = null; lostSince = lastAt = -1; }
        public void Reset() { ResetCurrent(); failedUntil.Clear(); }
    }
}
