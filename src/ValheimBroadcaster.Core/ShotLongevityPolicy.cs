using System;

namespace ValheimBroadcaster.Core
{
    public enum PredictedVisibility { Unknown, Readable, Obstructed }

    /// <summary>A short geometric forecast, never proof of rendered pixels or
    /// permission to move the observer. ReadableSeconds is a sampled lower
    /// bound, not a promise that a player will maintain their current course.</summary>
    public readonly struct ShotLongevityEvidence
    {
        public readonly bool Available, Blocked;
        public readonly float ReadableSeconds, Confidence;
        public readonly string Reason;
        internal ShotLongevityEvidence(bool available, bool blocked, float seconds, float confidence, string reason)
        { Available = available; Blocked = blocked; ReadableSeconds = seconds; Confidence = confidence; Reason = reason; }
    }

    /// <summary>Ranks the likely useful lifetime of views which are already
    /// readable now. Unknown future support/foliage is neutral; evidence can
    /// only lower an editorial score, never change physical eligibility,
    /// present readability, shot progress, or travel authorization.</summary>
    public sealed class ShotLongevityPolicy
    {
        public const double NearSeconds = .5, MiddleSeconds = 1, FarSeconds = 1.5;
        public const double MaximumEvidenceAge = .65, MaximumObservationGap = .5;
        public const float MaximumPenalty = .36f;
        private readonly Forecast[] forecasts;
        private double lastNow = -1, confidence;
        private AutonomyPosition lastFocus, lastVelocity;
        private bool authored;

        private struct Forecast
        {
            internal bool Recorded, Available, Blocked;
            internal double At;
            internal float ReadableSeconds, Confidence;
        }

        public ShotLongevityPolicy(int candidateCount)
        {
            if (candidateCount < 1 || candidateCount > ProceduralShotPolicy.MaximumCandidates)
                throw new ArgumentOutOfRangeException(nameof(candidateCount));
            forecasts = new Forecast[candidateCount];
        }

        public float MotionConfidence => (float)confidence;

        /// <summary>Supply a current loaded subject/group observation. An
        /// authored scenic path may bypass motion inference, but its geometry
        /// must still be sampled. Caller resets on scene/plan/world changes.</summary>
        public void ObserveMotion(double now, AutonomyPosition focus, AutonomyPosition velocity, bool authoredPath = false)
        {
            if (!Finite(now) || now < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
            if (lastNow == now) return;
            double elapsed = lastNow < 0 ? 0 : now - lastNow;
            bool reset = lastNow < 0 || elapsed > MaximumObservationGap || authored != authoredPath ||
                focus.Distance(lastFocus) > ShotTrajectoryPolicy.DiscontinuityDistance;
            if (reset)
            {
                Array.Clear(forecasts, 0, forecasts.Length);
                confidence = authoredPath ? 1 : 0;
            }
            else if (authoredPath) confidence = 1;
            else
            {
                double speed = Length(velocity), oldSpeed = Length(lastVelocity);
                double change = velocity.Distance(lastVelocity);
                double dot = speed > .75 && oldSpeed > .75 ?
                    (velocity.X * (double)lastVelocity.X + velocity.Y * (double)lastVelocity.Y +
                     velocity.Z * (double)lastVelocity.Z) / (speed * oldSpeed) : 1;
                double dx = focus.X - lastFocus.X - lastVelocity.X * elapsed;
                double dy = focus.Y - lastFocus.Y - lastVelocity.Y * elapsed;
                double dz = focus.Z - lastFocus.Z - lastVelocity.Z * elapsed;
                double unexplained = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                bool abrupt = change > Math.Max(1.5, 12 * elapsed) || dot < .75 ||
                    unexplained > Math.Max(2, 8 * elapsed) || speed > 20 || oldSpeed > 20;
                if (abrupt)
                {
                    confidence = 0;
                    Array.Clear(forecasts, 0, forecasts.Length);
                }
                else
                {
                    // Confidence takes one second of coherent observations to
                    // recover. Measured acceleration reduces it immediately.
                    double stability = Math.Max(0, 1 - change / Math.Max(.001, elapsed) / 24);
                    confidence = Math.Min(stability, Math.Min(1, confidence + elapsed));
                }
            }
            lastNow = now; lastFocus = focus; lastVelocity = velocity; authored = authoredPath;
        }

        /// <summary>Samples are at .5, 1 and 1.5 seconds along the same forecast.
        /// Only a consecutive known prefix can justify a longevity preference.
        /// An unknown interval cannot certify later obstruction or visibility.</summary>
        public void Record(int candidate, double now, PredictedVisibility halfSecond,
            PredictedVisibility oneSecond, PredictedVisibility oneAndHalfSeconds)
        {
            Index(candidate); ValidateTime(now);
            Validate(halfSecond); Validate(oneSecond); Validate(oneAndHalfSeconds);
            Forecast value = new Forecast { Recorded = true, At = now, Confidence = (float)confidence };
            if (lastNow < 0 || now - lastNow > MaximumObservationGap) { forecasts[candidate] = value; return; }
            if (Sample(ref value, halfSecond, (float)NearSeconds) &&
                Sample(ref value, oneSecond, (float)MiddleSeconds))
                Sample(ref value, oneAndHalfSeconds, (float)FarSeconds);
            forecasts[candidate] = value;
        }

        private static bool Sample(ref Forecast value, PredictedVisibility visibility, float seconds)
        {
            if (visibility == PredictedVisibility.Unknown) return false;
            value.Available = true;
            if (visibility == PredictedVisibility.Obstructed) { value.Blocked = true; return false; }
            value.ReadableSeconds = seconds;
            return true;
        }

        public ShotLongevityEvidence Evidence(int candidate, double now)
        {
            Index(candidate); ValidateTime(now);
            Forecast value = forecasts[candidate];
            if (!value.Recorded || !value.Available)
                return Empty("Future viewpoint is not fully surveyed");
            if (lastNow < 0 || now - lastNow > MaximumObservationGap || now - value.At > MaximumEvidenceAge)
                return Empty("Viewpoint forecast is stale");
            double certainty = Math.Min(confidence, value.Confidence);
            if (certainty < .4)
                return Empty("Waiting for a coherent subject course");
            float freshness = (float)Math.Max(0, Math.Min(1, (MaximumEvidenceAge - (now - value.At)) / .25));
            // Later stable observations cannot retroactively strengthen a
            // sample made while the subject's course was still uncertain.
            float weight = (float)certainty * freshness;
            return new ShotLongevityEvidence(true, value.Blocked, value.ReadableSeconds, weight,
                value.Blocked ? "Predicted obstruction along the current course" :
                value.ReadableSeconds >= FarSeconds ? "Readable through the forecast horizon" : "Partial readable forecast; farther geometry unknown");
        }

        public float AdjustScore(int candidate, double now, float currentScore, bool currentlyReadable)
        {
            if (!Finite(currentScore) || currentScore < 0 || currentScore > 1)
                throw new ArgumentOutOfRangeException(nameof(currentScore));
            ShotLongevityEvidence evidence = Evidence(candidate, now);
            if (!currentlyReadable || !evidence.Available || !evidence.Blocked) return currentScore;
            float penalty = MaximumPenalty * (1 - evidence.ReadableSeconds / (float)FarSeconds) * evidence.Confidence;
            return Math.Max(0, currentScore - penalty);
        }

        public void Invalidate(int candidate) { Index(candidate); forecasts[candidate] = default; }
        public void Reset()
        {
            Array.Clear(forecasts, 0, forecasts.Length);
            lastNow = -1; confidence = 0; lastFocus = lastVelocity = default; authored = false;
        }
        private static ShotLongevityEvidence Empty(string reason) => new ShotLongevityEvidence(false, false, 0, 0, reason);
        private void Index(int candidate)
        { if (candidate < 0 || candidate >= forecasts.Length) throw new ArgumentOutOfRangeException(nameof(candidate)); }
        private void ValidateTime(double now)
        { if (!Finite(now) || now < 0 || lastNow >= 0 && now < lastNow) throw new ArgumentOutOfRangeException(nameof(now)); }
        private static void Validate(PredictedVisibility value)
        { if ((int)value < 0 || (int)value > (int)PredictedVisibility.Obstructed) throw new ArgumentOutOfRangeException(nameof(value)); }
        private static double Length(AutonomyPosition value) => Math.Sqrt(value.X * (double)value.X + value.Y * (double)value.Y + value.Z * (double)value.Z);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
