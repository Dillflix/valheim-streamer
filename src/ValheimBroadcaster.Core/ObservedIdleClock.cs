using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Counts only a continuous series of known quiet observations.
    /// Passing time or receiving another transport reply cannot manufacture idle evidence.</summary>
    public sealed class ObservedIdleClock
    {
        public const double MaximumSampleGap = 3;
        private double sampleAt = -1, quietSince, lastTime = -1;
        public double ObservedIdleSeconds { get; private set; }
        public bool Known { get; private set; }

        public void Observe(double now, bool known, bool activity)
        {
            Time(now);
            if (!known)
            { Known = false; sampleAt = -1; quietSince = now; ObservedIdleSeconds = 0; return; }
            if (!Known || sampleAt < 0 || now - sampleAt > MaximumSampleGap || activity) quietSince = now;
            Known = true; sampleAt = now;
            ObservedIdleSeconds = Math.Min(86400, Math.Max(0, now - quietSince));
        }

        // An action arriving between sampling ticks must immediately invalidate
        // the old duration, without itself establishing a fresh character sample.
        public void Activity(double now)
        { Time(now); quietSince = now; ObservedIdleSeconds = 0; }

        public void Reset()
        { sampleAt = lastTime = -1; quietSince = ObservedIdleSeconds = 0; Known = false; }

        private void Time(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < lastTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            lastTime = now;
        }
    }
}
