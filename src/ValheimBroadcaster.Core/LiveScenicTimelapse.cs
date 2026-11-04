using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Live programme ownership. Route progress drives the lighting
    /// passage; preparation, unrelated frames and holds never advance it.</summary>
    public sealed class LiveScenicTimelapse
    {
        public long PlanId { get; private set; }
        public bool Active => PlanId > 0;
        public bool OnAir { get; private set; }
        public double Progress { get; private set; }
        public bool Requested { get; private set; }
        public string Reason { get; private set; } = "Waiting for varied outdoor coverage";
        private double previous = -1, readySeconds, began, missingSince = -1, nextAt;
        public void Reset(double now) { End(now, "Waiting for varied outdoor coverage"); nextAt = now; previous = -1; }
        public void Request() { Requested = true; Reason = "Requested; awaiting an eligible outdoor view and empty-server permission"; }
        public bool Opportunity(double now, bool eligible)
        {
            double dt = previous < 0 ? 0 : Math.Max(0, Math.Min(.1, now - previous)); previous = now;
            if (!eligible) { readySeconds = 0; return false; }
            if (!Active) readySeconds += dt;
            return !Active && (Requested || now >= nextAt && readySeconds >= 45);
        }
        public void Begin(long planId, double now)
        {
            if (planId <= 0) throw new ArgumentOutOfRangeException(nameof(planId));
            PlanId = planId; began = now; Progress = 0; OnAir = false; missingSince = -1;
            Requested = false; readySeconds = 0; Reason = "Preparing a slow daylight-passage shot";
        }
        public void Observe(double now, bool permitted, long ownerId, long presentedId, double progress, bool live, bool held)
        {
            if (!Active) return;
            if (!permitted || ownerId != PlanId) { End(now, "Interrupted; natural lighting restored"); return; }
            if (!OnAir && now - began > 25) { End(now, "Timelapse preparation expired"); return; }
            if (live && presentedId == PlanId && !held && !double.IsNaN(progress) && !double.IsInfinity(progress) && progress >= 0 && progress <= 1)
            {
                OnAir = true; missingSince = -1; Progress = Math.Max(Progress, progress);
                Reason = "Live daylight passage " + Progress.ToString("P0") + "; slow camera movement";
                // Allow one frame at the natural endpoint before ending ownership.
                if (Progress >= .999999) End(now, "Daylight passage completed; ordinary coverage resumes");
            }
            else if (held && OnAir) missingSince = -1;
            else if (!held && OnAir)
            {
                if (missingSince < 0) missingSince = now;
                if (now - missingSince >= 3) End(now, "Live timelapse picture lost; ordinary recovery resumes");
            }
        }
        public void End(double now, string reason)
        {
            PlanId = 0; OnAir = Requested = false; Progress = 0; missingSince = -1;
            readySeconds = 0; nextAt = now + 300; Reason = reason;
        }
        public static bool Suitable(ScenicShotPlan? plan) => plan != null && plan.IsDirected && !plan.IsPictureOnly && !plan.IsJourney &&
            plan.Feature != null && plan.Feature.SubjectKind == ScenicSubjectKind.Environment && plan.Feature.MeasuredBounds == null &&
            (plan.FeatureKind == ScenicFeatureKind.Coastline || plan.FeatureKind == ScenicFeatureKind.Ridge || plan.FeatureKind == ScenicFeatureKind.Clearing) &&
            plan.TravelMetres <= 24;
        /// <summary>One forward visual day, eased at both ends, added to current
        /// natural time. The endpoint therefore rejoins the moving world clock.</summary>
        public static double Phase(double natural, double progress, double initialOffset = 0)
        {
            if (double.IsNaN(natural) || double.IsInfinity(natural) || double.IsNaN(progress) || double.IsInfinity(progress) ||
                progress < 0 || progress > 1 || double.IsNaN(initialOffset) || double.IsInfinity(initialOffset))
                throw new ArgumentOutOfRangeException(nameof(progress));
            double smooth = progress * progress * (3 - 2 * progress);
            double value = natural + smooth + initialOffset * (1 - smooth);
            return value - Math.Floor(value);
        }
    }
}
