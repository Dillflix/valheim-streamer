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
        public const double HealthyDwellSeconds = 5;
        public const double MaximumEvidenceGapSeconds = 1;
        public const double ReadableRecoverySeconds = .35;
        public const double PoorViewImprovementSeconds = .75;
        public const float PoorViewImprovementMargin = .05f;
        public const double SustainedClearSeconds = 0.75;
        public const double RepairWindowSeconds = 1.5;
        public const double MinimumShotSeconds = 2.0;

        private int candidateCount;
        private double lastTime = -1, selectedAt, pendingSince;
        private bool pendingReadable;
        private int rollbackIndex = -1;
        private double rollbackSelectedAt;
        private bool canRollback;
        // Per-candidate start of the current continuously clear observation
        // streak; -1 while unobserved, unreadable or physically ineligible.
        private readonly double[] clearSince = new double[MaximumCandidates];
        // When the displayed selection was last observed losing readability.
        // The adapter repairs the live camera in place during this window.
        private double readableLostAt = -1;
        public int SelectedIndex { get; private set; } = -1;
        public int PendingIndex { get; private set; } = -1;
        /// <summary>Time the current selection was adopted; meaningful only
        /// while SelectedIndex is non-negative.</summary>
        public double SelectedSince => selectedAt;

        /// <summary>Returns -1 only when no candidate is eligible. An occluded but
        /// physically valid picture remains a candidate while alternatives are sought.
        /// Candidate indices must retain their meaning until Reset is called.</summary>
        public int Select(double now, IReadOnlyList<float> scores, IReadOnlyList<bool> eligible,
            IReadOnlyList<bool>? readable = null, IShotCandidateRanker? ranker = null,
            IReadOnlyList<int>? priorities = null, bool reconsiderPicture = false)
        {
            if (!Finite(now) || now < 0 || lastTime >= 0 && now < lastTime)
            {
                Reset();
                throw new ArgumentOutOfRangeException(nameof(now), "Shot selection requires finite monotonic time.");
            }
            if (scores == null || eligible == null || scores.Count != eligible.Count || scores.Count > MaximumCandidates ||
                readable != null && readable.Count != scores.Count)
                throw new ArgumentException("Provide equally sized candidate scores and eligibility, up to 32 entries.");
            for (int i = 0; i < scores.Count; i++)
                if (!Finite(scores[i]) || scores[i] < 0 || scores[i] > 1)
                    throw new ArgumentOutOfRangeException(nameof(scores), "Picture-quality scores must be finite values from zero to one.");

            if (now != lastTime) canRollback = false;

            if (candidateCount != scores.Count)
            {
                Reset();
                candidateCount = scores.Count;
            }
            if (lastTime >= 0 && now - lastTime > MaximumEvidenceGapSeconds)
            {
                // An unobserved stall proves nothing: stale pending comparisons
                // and stale clear streaks cannot establish sustained evidence.
                PendingIndex = -1;
                ClearStreaks();
                readableLostAt = -1;
            }
            lastTime = now;
            UpdateStreaks(now, eligible, readable);
            // Stamp the start of the displayed selection's unreadable spell so
            // the in-place repair window measures from the first observation.
            if (SelectedIndex >= 0 && readable != null)
            {
                if (readable[SelectedIndex]) readableLostAt = -1;
                else if (readableLostAt < 0) readableLostAt = now;
            }

            bool currentEligible = SelectedIndex >= 0 && eligible[SelectedIndex];
            int best = ShotCandidateRanking.Best(scores, eligible, readable,
                currentEligible ? SelectedIndex : -1, ranker, priorities);

            if (best < 0)
            {
                SelectedIndex = PendingIndex = -1;
                return -1;
            }
            // Collision, missing geometry or an unreachable carrier can never be
            // held in place by dwell/hysteresis. Initial selection uses this too.
            if (!currentEligible) return Switch(best, now);

            bool recovery = readable != null && !readable[SelectedIndex];
            bool restoresReadable = recovery && readable![best];
            // An editorial purpose promotion may bypass the score margin only
            // once the current shot has a minimum age; a freshly adopted
            // composition keeps the full margin against competing purposes.
            bool purposePromotion = priorities != null && (readable == null || readable[best]) &&
                priorities[best] > priorities[SelectedIndex] && now - selectedAt >= MinimumShotSeconds;
            float margin = recovery || reconsiderPicture ? PoorViewImprovementMargin : ImprovementMargin;
            if (best == SelectedIndex || !restoresReadable && !purposePromotion && scores[best] < scores[SelectedIndex] + margin)
            {
                PendingIndex = -1;
                return SelectedIndex;
            }
            bool candidateReadable = readable != null && readable[best];
            if (PendingIndex != best || pendingReadable != candidateReadable)
            {
                PendingIndex = best;
                pendingSince = now;
                pendingReadable = candidateReadable;
            }
            double persistence = restoresReadable ? ReadableRecoverySeconds : recovery ? PoorViewImprovementSeconds : ImprovementSeconds;
            if (now - pendingSince < persistence) return SelectedIndex;
            // Destination confidence: replacing a displayed shot requires the
            // challenger to have been continuously clear, not momentarily best.
            // Without readability evidence persistence remains the only proof.
            if (readable != null)
            {
                double requiredStreak = !recovery ? SustainedClearSeconds :
                    restoresReadable ? ReadableRecoverySeconds : 0;
                if (requiredStreak > 0 && (clearSince[best] < 0 || now - clearSince[best] < requiredStreak))
                    return SelectedIndex;
            }
            // Dwell protects a healthy composition, not footage buried in foliage.
            if (recovery)
            {
                // The adapter receives a protected window to repair the live
                // camera in place (for example arcing around an occluder)
                // before an eligible but unreadable selection is abandoned.
                if (readableLostAt >= 0 && now - readableLostAt < RepairWindowSeconds) return SelectedIndex;
                // An all-bad search can make measured progress, but cannot
                // oscillate among nearly equivalent obstructed candidates.
                if (!restoresReadable && now - selectedAt < PoorViewImprovementSeconds) return SelectedIndex;
            }
            else if ((readable != null ? readable[SelectedIndex] : scores[SelectedIndex] >= HealthyScore) &&
                now - selectedAt < HealthyDwellSeconds) return SelectedIndex;
            return Switch(best, now);
        }

        /// <summary>Standby geometry changed: its previous comparison cannot
        /// establish a sustained improvement at the new pose.</summary>
        public void InvalidatePending()
        {
            PendingIndex = -1;
            pendingSince = 0;
            pendingReadable = false;
        }

        /// <summary>Reject this clock tick's proposed replacement after the
        /// adapter revalidates and retains its previously presented live view.
        /// Restores the original dwell, not a fictional newly displayed shot.</summary>
        public bool RejectSelection(int retainedIndex)
        {
            if (!canRollback || retainedIndex < 0 || retainedIndex >= candidateCount || retainedIndex != rollbackIndex)
                return false;
            SelectedIndex = retainedIndex;
            selectedAt = rollbackSelectedAt;
            // The failed transition proves nothing about the retained camera's
            // occlusion repair; restarting the window from the next unreadable
            // observation errs toward in-place repair over an immediate swap.
            readableLostAt = -1;
            canRollback = false;
            InvalidatePending();
            return true;
        }

        /// <summary>Adopt an actually displayed source revalidated by the
        /// adapter after a mode reset left no rollback checkpoint. This is not
        /// a substitute for endpoint validation; no geometry is tested here.</summary>
        public bool AdoptValidatedSelection(double now, int index)
        {
            if (!Finite(now) || now < 0 || now < lastTime || index < 0 || index >= candidateCount) return false;
            SelectedIndex = index;
            selectedAt = lastTime = now;
            readableLostAt = -1;
            canRollback = false;
            InvalidatePending();
            return true;
        }

        public void Reset()
        {
            SelectedIndex = PendingIndex = -1;
            candidateCount = 0;
            lastTime = -1;
            selectedAt = pendingSince = 0;
            pendingReadable = false;
            rollbackIndex = -1; rollbackSelectedAt = 0; canRollback = false;
            ClearStreaks();
            readableLostAt = -1;
        }

        private int Switch(int index, double now)
        {
            // Multiple same-tick re-evaluations still refer to one displayed
            // source; preserve that earliest source through the proposal chain.
            if (!canRollback)
            {
                rollbackIndex = SelectedIndex; rollbackSelectedAt = selectedAt;
                canRollback = rollbackIndex >= 0 && rollbackIndex != index;
            }
            SelectedIndex = index;
            PendingIndex = -1;
            selectedAt = now;
            // The new selection starts a fresh unreadable spell; it is stamped
            // on the next observation that actually sees it unreadable.
            readableLostAt = -1;
            return index;
        }
        /// <summary>A clear streak proves a candidate stayed readable and
        /// eligible across consecutive observations. Readability flicker or
        /// physical ineligibility restarts the proof from scratch.</summary>
        private void UpdateStreaks(double now, IReadOnlyList<bool> eligible, IReadOnlyList<bool>? readable)
        {
            if (readable == null) return;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!eligible[i] || !readable[i]) clearSince[i] = -1;
                else if (clearSince[i] < 0) clearSince[i] = now;
            }
        }
        private void ClearStreaks()
        { for (int i = 0; i < clearSince.Length; i++) clearSince[i] = -1; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
