using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Select a stable viewpoint from normalized picture-quality scores.
    /// Eligibility belongs to physical/scene safety, never foliage quality.</summary>
    public sealed class ProceduralShotPolicy
    {
        public const int MaximumCandidates = 32;
        public const float ImprovementMargin = 0.15f;
        public const float HealthyScore = 0.35f;
        public const double ImprovementSeconds = 0.5;
        public const double HealthyDwellSeconds = 2;
        public const double MaximumEvidenceGapSeconds = 1;

        private int candidateCount;
        private double lastTime = -1, selectedAt, pendingSince;
        public int SelectedIndex { get; private set; } = -1;
        public int PendingIndex { get; private set; } = -1;

        /// <summary>Returns -1 only when no candidate is eligible. An occluded but
        /// physically valid picture remains a candidate while alternatives are sought.
        /// Candidate indices must retain their meaning until Reset is called.</summary>
        public int Select(double now, IReadOnlyList<float> scores, IReadOnlyList<bool> eligible)
        {
            if (!Finite(now) || now < 0 || lastTime >= 0 && now < lastTime)
            {
                Reset();
                throw new ArgumentOutOfRangeException(nameof(now), "Shot selection requires finite monotonic time.");
            }
            if (scores == null || eligible == null || scores.Count != eligible.Count || scores.Count > MaximumCandidates)
                throw new ArgumentException("Provide equally sized candidate scores and eligibility, up to 32 entries.");
            for (int i = 0; i < scores.Count; i++)
                if (!Finite(scores[i]) || scores[i] < 0 || scores[i] > 1)
                    throw new ArgumentOutOfRangeException(nameof(scores), "Picture-quality scores must be finite values from zero to one.");

            if (candidateCount != scores.Count)
            {
                Reset();
                candidateCount = scores.Count;
            }
            if (lastTime >= 0 && now - lastTime > MaximumEvidenceGapSeconds) PendingIndex = -1;
            lastTime = now;

            bool currentEligible = SelectedIndex >= 0 && eligible[SelectedIndex];
            int best = currentEligible ? SelectedIndex : -1;
            for (int i = 0; i < scores.Count; i++)
                if (eligible[i] && (best < 0 || scores[i] > scores[best])) best = i;

            if (best < 0)
            {
                SelectedIndex = PendingIndex = -1;
                return -1;
            }
            // Collision, missing geometry or an unreachable carrier can never be
            // held in place by dwell/hysteresis. Initial selection uses this too.
            if (!currentEligible) return Switch(best, now);

            if (best == SelectedIndex || scores[best] < scores[SelectedIndex] + ImprovementMargin)
            {
                PendingIndex = -1;
                return SelectedIndex;
            }
            if (PendingIndex != best)
            {
                PendingIndex = best;
                pendingSince = now;
            }
            if (now - pendingSince < ImprovementSeconds) return SelectedIndex;
            // Dwell protects a healthy composition, not footage buried in foliage.
            if (scores[SelectedIndex] >= HealthyScore && now - selectedAt < HealthyDwellSeconds) return SelectedIndex;
            return Switch(best, now);
        }

        public void Reset()
        {
            SelectedIndex = PendingIndex = -1;
            candidateCount = 0;
            lastTime = -1;
            selectedAt = pendingSince = 0;
        }

        private int Switch(int index, double now)
        {
            SelectedIndex = index;
            PendingIndex = -1;
            selectedAt = now;
            return index;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
