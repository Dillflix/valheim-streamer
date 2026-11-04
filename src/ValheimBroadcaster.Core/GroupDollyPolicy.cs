using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Bounds native group-framing translation. This does not infer
    /// clearance: the adapter must validate the proposed complete view/path.</summary>
    public sealed class GroupDollyPolicy
    {
        public const float MaximumDistance = 4;
        public const double RetrySeconds = 2;
        private double blockedUntil, lastNow = -1;
        public float Limit { get; private set; }
        public bool Allowed => Limit > 0;
        public string Reason { get; private set; } = "group dolly awaiting scene evidence";

        public void Configure(double now, bool enabled, bool group, bool knownOpenSpace,
            float availableSpace, float spread, float primaryDistance)
        {
            if (!Finite(now) || now < 0 || !Finite(availableSpace) || !Finite(spread) || !Finite(primaryDistance))
            { Reset(); Reason = "group dolly has no valid scene evidence"; return; }
            if (lastNow >= 0 && now < lastNow) Reset();
            lastNow = now; Limit = 0;
            if (!enabled || !group) { Reason = "group dolly not requested"; return; }
            if (!knownOpenSpace || availableSpace < .6f || spread < 0 || primaryDistance < 3)
            { Reason = "group zoom retained within constrained or uncertain space"; return; }
            if (now < blockedUntil) { Reason = "group zoom retained after a blocked dolly"; return; }
            // Small local reframing can supplement the persistent world path;
            // it cannot become a second unbounded camera trajectory.
            Limit = Math.Min(MaximumDistance, Math.Min(primaryDistance * .3f, .75f + spread * .5f));
            Reason = "native group dolly before bounded lens adjustment";
        }

        public bool WithinBudget(float x, float y, float z)
        {
            if (!Allowed || !Finite(x) || !Finite(y) || !Finite(z)) return false;
            double lengthSquared = (double)x * x + (double)y * y + (double)z * z;
            return lengthSquared <= (Limit + .001) * (Limit + .001);
        }

        public void Reject(double now)
        {
            if (!Finite(now) || now < 0) { Reset(); return; }
            blockedUntil = now + RetrySeconds;
            Limit = 0; Reason = "native group dolly rejected; recomputing zoom from supported body position";
        }

        public void Reset()
        { blockedUntil = 0; lastNow = -1; Limit = 0; Reason = "group dolly awaiting scene evidence"; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
