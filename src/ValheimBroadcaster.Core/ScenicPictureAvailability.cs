using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Owns independently admitted scenic pictures while cinematic
    /// route planning continues. Admission requires fresh adapter evidence;
    /// only matching live presentation earns dwell. A failed route does not
    /// invalidate its readable viewpoint, and a picture is never a route.</summary>
    public sealed class ScenicPictureAvailability
    {
        public const double MinimumPictureSeconds = 4;
        public const double PreferredPictureSeconds = 8;
        public const double ExhaustedPictureSeconds = 24;
        public const double PendingRouteGraceSeconds = 2;
        private readonly List<ScenicShotPlan> shown = new List<ScenicShotPlan>(16);
        private double acceptedAt, lastObservation = -1, lastReadableAt = -1;
        private double pendingRouteAt = -1;
        private bool previousReadable, previousSettled, remembered, routeSearchFailed;
        public ScenicShotPlan? Current { get; private set; }
        public double ShownSeconds { get; private set; }
        public double TotalPictureSeconds { get; private set; }
        public bool ContentExhausted => routeSearchFailed && TotalPictureSeconds >= ExhaustedPictureSeconds &&
            (pendingRouteAt < 0 || lastObservation - pendingRouteAt >= PendingRouteGraceSeconds);
        public bool WantsCandidate(bool currentPictureUsable) =>
            !currentPictureUsable || Current != null && ShownSeconds >= PreferredPictureSeconds;

        public bool TryOffer(ScenicShotPlan candidate, long observedEpoch, long currentEpoch,
            double observedAt, double now, bool currentPictureUsable)
        {
            Time(now); Time(observedAt);
            if (candidate == null || !candidate.IsPictureOnly)
                throw new ArgumentException("Independent availability accepts only a measured single-picture proposal.", nameof(candidate));
            if (observedEpoch != currentEpoch || now < observedAt || now - observedAt > .5) return false;
            // Never interrupt a valid cinematic shot, nor flick through clear
            // graph nodes as the background planner discovers them.
            if (!WantsCandidate(currentPictureUsable)) return false;
            foreach (ScenicShotPlan previous in shown)
                if (Equivalent(previous, candidate)) return false;
            Current = candidate; ShownSeconds = 0; acceptedAt = now;
            lastObservation = lastReadableAt = -1; previousReadable = previousSettled = remembered = false;
            return true;
        }

        public void Observe(long planId, double now, bool usableLive, bool held, bool retainedRoutePicture = false)
        {
            Time(now);
            if (now <= lastObservation) return;
            bool readable = Current != null && Current.Id == planId && usableLive && !held;
            bool settled = readable || retainedRoutePicture && usableLive && !held;
            double delta = lastObservation >= 0 ? now - lastObservation : 0;
            if (settled && previousSettled && delta <= .25) TotalPictureSeconds += delta;
            if (readable && previousReadable && delta <= .25)
            {
                ShownSeconds += delta;
                if (!remembered && ShownSeconds >= 2)
                {
                    shown.Add(Current!); remembered = true;
                    if (shown.Count > 16) shown.RemoveAt(0);
                }
            }
            if (readable) lastReadableAt = now;
            previousReadable = readable; previousSettled = settled; lastObservation = now;
        }

        public bool CanYield(double now)
        {
            Time(now);
            return Current == null || ShownSeconds >= MinimumPictureSeconds ||
                now - Math.Max(acceptedAt, lastReadableAt) > .5;
        }

        public void SearchFinished(bool hasMovingRoute)
        {
            if (!hasMovingRoute) RouteUnavailable();
            // A newly staged route gets one brief chance to render and start
            // moving. Repeated stages/failed cuts cannot restart this grace or
            // clear exhaustion while the old settled picture remains on air.
            else if (pendingRouteAt < 0) pendingRouteAt = Math.Max(acceptedAt, lastObservation);
        }

        public void RouteUnavailable() => routeSearchFailed = true;

        // A staged/accepted route does not clear content exhaustion. Its actual
        // developed live footage must arrive before the independent fallback
        // budget starts afresh.
        public void MotionPresented()
        { TotalPictureSeconds = 0; routeSearchFailed = false; pendingRouteAt = -1; }

        public void ClearCurrent()
        {
            Current = null; ShownSeconds = 0; acceptedAt = 0;
            lastObservation = lastReadableAt = -1; previousReadable = previousSettled = remembered = false;
        }

        public void Reset()
        { ClearCurrent(); shown.Clear(); TotalPictureSeconds = 0; routeSearchFailed = false; pendingRouteAt = -1; }

        private static bool Equivalent(ScenicShotPlan a, ScenicShotPlan b)
        {
            var pa = a.Sample(0); var pb = b.Sample(0);
            if (a.FeatureId != b.FeatureId)
                return pa.CameraPosition.Distance(pb.CameraPosition) < 3 && pa.AimPosition.Distance(pb.AimPosition) < 3;
            double ax = pa.CameraPosition.X - pa.AimPosition.X, az = pa.CameraPosition.Z - pa.AimPosition.Z;
            double bx = pb.CameraPosition.X - pb.AimPosition.X, bz = pb.CameraPosition.Z - pb.AimPosition.Z;
            double lengths = Math.Sqrt((ax * ax + az * az) * (bx * bx + bz * bz));
            double angle = lengths > .001 ? Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + az * bz) / lengths))) * 180 / Math.PI : 180;
            double sa = pa.CameraPosition.Distance(pa.AimPosition) * Math.Tan(a.FieldOfView * Math.PI / 360);
            double sb = pb.CameraPosition.Distance(pb.AimPosition) * Math.Tan(b.FieldOfView * Math.PI / 360);
            double ratio = Math.Max(sa, sb) / Math.Max(.001, Math.Min(sa, sb));
            return angle < 25 && ratio < 1.35;
        }

        private static void Time(double value)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
}
