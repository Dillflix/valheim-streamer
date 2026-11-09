using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Where the recovery state machine currently stands. Watching
    /// means occlusion is at the limit but the engagement conditions (sustain
    /// and the deoccluder's first crack) are still being confirmed; Steering
    /// means the camera must slew its orbit toward the target azimuth.</summary>
    public enum OcclusionRecoveryPhase { Idle, Watching, Steering }

    /// <summary>One caller-probed orbit vantage: an absolute azimuth on the
    /// current orbit and the fraction of the subject's body rays that are
    /// unobstructed by solid geometry from that probe point.</summary>
    public readonly struct OcclusionRecoveryCandidate
    {
        public readonly float AzimuthDegrees;
        public readonly float ClearFraction;
        public OcclusionRecoveryCandidate(float azimuthDegrees, float clearFraction)
        { AzimuthDegrees = azimuthDegrees; ClearFraction = clearFraction; }
    }

    /// <summary>Outcome of one observation. NeedsCandidates asks the caller to
    /// probe azimuth clearances before the next observation; TargetAzimuthDegrees
    /// is only meaningful while Steering.</summary>
    public readonly struct OcclusionRecoveryDecision
    {
        public readonly OcclusionRecoveryPhase Phase;
        public readonly bool NeedsCandidates;
        public readonly float TargetAzimuthDegrees;
        public readonly string Reason;
        internal OcclusionRecoveryDecision(OcclusionRecoveryPhase phase, bool needsCandidates,
            float targetAzimuthDegrees, string reason)
        { Phase = phase; NeedsCandidates = needsCandidates; TargetAzimuthDegrees = targetAzimuthDegrees; Reason = reason; }
    }

    /// <summary>
    /// The ONLY authorized vantage-change trigger for the simple follow
    /// camera: severe solid-geometry occlusion of the player. When solid
    /// geometry hides most of the subject and the native deoccluder cannot
    /// resolve it, the camera's orbit azimuth is steered - continuously, never
    /// cut - toward the nearest caller-verified clear side, then control is
    /// released back to the native behind-the-player recentering. Foliage
    /// never counts as occlusion; the caller measures solid geometry only.
    /// Pure and explicit-clock: all raycasts belong to the adapter.
    /// </summary>
    public sealed class OcclusionRecoveryPolicy
    {
        /// <summary>Occlusion fraction at or above which the subject counts as
        /// blocked (the user's &gt;80% rule). Below this, nothing ever engages.</summary>
        public const float EngageOcclusion = 0.8f;
        /// <summary>Occlusion must hold at the limit this long before recovery
        /// may engage, so door frames and passing posts never trigger an arc.</summary>
        public const double EngageSustainSeconds = 0.4;
        /// <summary>After the sustain is met, if the native deoccluder is
        /// actively displacing the camera AND occlusion is falling relative to
        /// the worst occlusion seen this episode, engagement waits up to this
        /// much longer - the deoccluder gets first crack at the shot.</summary>
        public const double DeoccluderGraceSeconds = 0.4;
        /// <summary>A candidate azimuth qualifies as an escape only when at
        /// least this fraction of the subject's body rays are clear from it.</summary>
        public const float MinimumCandidateClear = 0.7f;
        /// <summary>While steering, if the chosen azimuth's refreshed clearance
        /// falls below this, the target has collapsed and is re-selected.</summary>
        public const float CollapsedTargetClear = 0.5f;
        /// <summary>Occlusion at or below this counts as recovered.</summary>
        public const float ReleaseOcclusion = 0.3f;
        /// <summary>Recovered occlusion must hold this long before control is
        /// released back to native recentering (hysteresis against flicker).</summary>
        public const double ReleaseSustainSeconds = 0.5;
        /// <summary>Minimum time between a release and the next engagement, so
        /// a marginal vantage cannot thrash the orbit back and forth.</summary>
        public const double CooldownSeconds = 2.0;

        private OcclusionRecoveryPhase phase;
        private double lastNow = -1;
        private double highSince = -1;
        private double lowSince = -1;
        private double graceUntil = -1;
        private float watchPeakOcclusion = -1;
        private double cooldownUntil;
        private float targetAzimuth;

        public OcclusionRecoveryPhase Phase => phase;

        /// <summary>Observe one frame. now is a monotonic clock (a rollback is
        /// a world reload and resets the machine); occlusion is the fraction
        /// [0..1] of the subject blocked by SOLID geometry from the presented
        /// camera; deoccluderDisplacing reports whether the native deoccluder
        /// is actively pushing the camera; currentAzimuthDegrees is the orbit's
        /// horizontal axis value; candidates are the caller's latest azimuth
        /// clearance probes (may be null or empty when none were requested).</summary>
        public OcclusionRecoveryDecision Observe(double now, float occlusion, bool deoccluderDisplacing,
            float currentAzimuthDegrees, IReadOnlyList<OcclusionRecoveryCandidate>? candidates)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!Finite(occlusion) || occlusion < 0 || occlusion > 1) throw new ArgumentOutOfRangeException(nameof(occlusion));
            if (!Finite(currentAzimuthDegrees)) throw new ArgumentOutOfRangeException(nameof(currentAzimuthDegrees));
            for (int i = 0; candidates != null && i < candidates.Count; i++)
            {
                OcclusionRecoveryCandidate candidate = candidates[i];
                if (!Finite(candidate.AzimuthDegrees) || !Finite(candidate.ClearFraction) ||
                    candidate.ClearFraction < 0 || candidate.ClearFraction > 1)
                    throw new ArgumentOutOfRangeException(nameof(candidates));
            }
            if (lastNow >= 0 && now < lastNow) Reset();
            lastNow = now;
            switch (phase)
            {
                case OcclusionRecoveryPhase.Idle:
                    if (occlusion < EngageOcclusion)
                        return new OcclusionRecoveryDecision(phase, false, 0, "subject visible");
                    if (now < cooldownUntil)
                        return new OcclusionRecoveryDecision(phase, false, 0, "recovery cooling down");
                    phase = OcclusionRecoveryPhase.Watching;
                    highSince = now; graceUntil = -1; watchPeakOcclusion = occlusion;
                    return new OcclusionRecoveryDecision(phase, true, 0, "confirming sustained occlusion");
                case OcclusionRecoveryPhase.Watching:
                    if (occlusion < EngageOcclusion)
                    {
                        phase = OcclusionRecoveryPhase.Idle; highSince = -1;
                        return new OcclusionRecoveryDecision(phase, false, 0, "occlusion receded");
                    }
                    if (occlusion > watchPeakOcclusion) watchPeakOcclusion = occlusion;
                    if (now - highSince < EngageSustainSeconds)
                        return new OcclusionRecoveryDecision(phase, true, 0, "confirming sustained occlusion");
                    // The deoccluder's first crack: while it is actively
                    // displacing and the picture is improving against the
                    // worst occlusion of this episode, hold off a bounded
                    // while longer before taking over.
                    if (graceUntil < 0) graceUntil = now + DeoccluderGraceSeconds;
                    if (now < graceUntil && deoccluderDisplacing && occlusion < watchPeakOcclusion)
                        return new OcclusionRecoveryDecision(phase, true, 0, "deoccluder resolving");
                    if (!SelectTarget(currentAzimuthDegrees, candidates, out targetAzimuth))
                        return new OcclusionRecoveryDecision(phase, true, 0, "no clear azimuth probed yet");
                    phase = OcclusionRecoveryPhase.Steering; lowSince = -1;
                    return new OcclusionRecoveryDecision(phase, true, targetAzimuth, "arc recovery engaged");
                default:
                    if (occlusion <= ReleaseOcclusion)
                    {
                        if (lowSince < 0) lowSince = now;
                        if (now - lowSince >= ReleaseSustainSeconds)
                        {
                            phase = OcclusionRecoveryPhase.Idle;
                            highSince = lowSince = graceUntil = -1; watchPeakOcclusion = -1;
                            cooldownUntil = now + CooldownSeconds;
                            return new OcclusionRecoveryDecision(phase, false, 0, "occlusion resolved; recentering resumes");
                        }
                    }
                    else lowSince = -1;
                    // Re-target when the chosen azimuth's clearance collapsed.
                    // With no qualifying escape left, steer to the clearest
                    // probe available - continued motion beats a blocked park.
                    if (TryFindClearance(candidates, targetAzimuth, out float targetClear) &&
                        targetClear < CollapsedTargetClear &&
                        !SelectTarget(currentAzimuthDegrees, candidates, out targetAzimuth) &&
                        !SelectClearest(candidates, out targetAzimuth))
                        targetAzimuth = currentAzimuthDegrees;
                    return new OcclusionRecoveryDecision(phase, true, targetAzimuth, "arc recovery steering");
            }
        }

        public void Reset()
        {
            phase = OcclusionRecoveryPhase.Idle;
            lastNow = highSince = lowSince = graceUntil = -1;
            watchPeakOcclusion = -1; cooldownUntil = 0; targetAzimuth = 0;
        }

        /// <summary>Clearest-nearest selection: among candidates meeting the
        /// clearance floor, prefer the smallest shortest-arc turn from the
        /// current azimuth; equal turns prefer the clearer side.</summary>
        private static bool SelectTarget(float currentAzimuth,
            IReadOnlyList<OcclusionRecoveryCandidate>? candidates, out float target)
        {
            target = 0;
            bool found = false;
            float bestDelta = float.MaxValue, bestClear = 0;
            for (int i = 0; candidates != null && i < candidates.Count; i++)
            {
                OcclusionRecoveryCandidate candidate = candidates[i];
                if (candidate.ClearFraction < MinimumCandidateClear) continue;
                float delta = Math.Abs(DeltaAngle(currentAzimuth, candidate.AzimuthDegrees));
                if (found && (delta > bestDelta + 0.01f ||
                    Math.Abs(delta - bestDelta) <= 0.01f && candidate.ClearFraction <= bestClear)) continue;
                target = candidate.AzimuthDegrees; bestDelta = delta; bestClear = candidate.ClearFraction; found = true;
            }
            return found;
        }

        private static bool SelectClearest(IReadOnlyList<OcclusionRecoveryCandidate>? candidates, out float target)
        {
            target = 0;
            float bestClear = -1;
            for (int i = 0; candidates != null && i < candidates.Count; i++)
                if (candidates[i].ClearFraction > bestClear)
                { bestClear = candidates[i].ClearFraction; target = candidates[i].AzimuthDegrees; }
            return bestClear > 0;
        }

        private static bool TryFindClearance(IReadOnlyList<OcclusionRecoveryCandidate>? candidates,
            float azimuth, out float clear)
        {
            clear = 1;
            bool found = false;
            float bestDelta = 5f; // Only a probe of (nearly) the same azimuth refreshes the target.
            for (int i = 0; candidates != null && i < candidates.Count; i++)
            {
                float delta = Math.Abs(DeltaAngle(azimuth, candidates[i].AzimuthDegrees));
                if (delta > bestDelta) continue;
                bestDelta = delta; clear = candidates[i].ClearFraction; found = true;
            }
            return found;
        }

        /// <summary>Shortest signed arc from one azimuth to another, degrees.</summary>
        public static float DeltaAngle(float fromDegrees, float toDegrees)
        {
            float delta = (toDegrees - fromDegrees) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
