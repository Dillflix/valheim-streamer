using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Recent live footage permits continuation of the same acquired
    /// environmental plan. Queries never renew evidence; geometry is still
    /// checked independently by the game adapter at every actual camera pose.</summary>
    public sealed class ScenicReadinessPolicy
    {
        public const double MaximumGapSeconds = .75, MaximumStepMetres = 16;
        private long plan;
        private AutonomyPosition focus;
        private double presentedAt = -1;

        public bool CanContinue(long planId, AutonomyPosition point, double now) =>
            planId > 0 && planId == plan && Finite(now) && now >= presentedAt &&
            presentedAt >= 0 && now - presentedAt <= MaximumGapSeconds &&
            Valid(point) && point.Distance(focus) <= MaximumStepMetres;

        public void Presented(long planId, AutonomyPosition point, double now)
        {
            if (planId <= 0 || !Valid(point) || !Finite(now) || now < 0 || presentedAt >= 0 && now < presentedAt)
            { Reset(); throw new ArgumentException("Live scenic evidence requires valid plan, position and monotonic time."); }
            plan = planId; focus = point; presentedAt = now;
        }
        public void Reset() { plan = 0; presentedAt = -1; focus = default; }
        private static bool Valid(AutonomyPosition p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
