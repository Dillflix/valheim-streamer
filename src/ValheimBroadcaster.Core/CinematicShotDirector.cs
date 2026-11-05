using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum CinematicActivity { Idle, Travel, Combat, Work }

    /// <summary>Relative, bounded shot intent. The adapter must validate the
    /// complete trajectory and may retain its last accepted intent.</summary>
    public readonly struct CinematicShotMotion
    {
        public readonly float AzimuthOffset, RadiusScale, HeightOffset;
        public readonly string Family;
        public CinematicShotMotion(float azimuthOffset, float radiusScale, float heightOffset, string family)
        { AzimuthOffset = azimuthOffset; RadiusScale = radiusScale; HeightOffset = heightOffset; Family = family; }
    }

    /// <summary>
    /// Live directing above Cinemachine's geometric camera solver. Physical
    /// eligibility always wins. Timing advances only with observed usable
    /// footage; corrections count as camera activity and suppress decoration.
    /// The small scene idioms follow the separation used by Virtual
    /// Cinematographer, while shot history and dwell preserve editing continuity.
    /// </summary>
    public sealed class CinematicShotDirector
    {
        public const float MinimumPlannedQuality = 0.6f;
        public const double SettleSeconds = 5;
        private const double HistorySeconds = 45, CorrectionWindow = 10;
        private readonly ProceduralShotPolicy selection = new ProceduralShotPolicy();
        private readonly ShotIntentPolicy intent = new ShotIntentPolicy();
        private readonly float[] weighted = new float[ProceduralShotPolicy.MaximumCandidates];
        private readonly bool[] seedEligibility = new bool[ProceduralShotPolicy.MaximumCandidates];
        private readonly bool[] plannedEligibility = new bool[ProceduralShotPolicy.MaximumCandidates];
        private readonly float[] plannedScores = new float[ProceduralShotPolicy.MaximumCandidates];
        private readonly double[] lastShown = new double[ProceduralShotPolicy.MaximumCandidates];
        private readonly int[] familyLastUsed = new int[5];
        private readonly Queue<double> corrections = new Queue<double>();
        private readonly double[] checkpointShown = new double[ProceduralShotPolicy.MaximumCandidates];
        private readonly int[] checkpointFamilies = new int[5];
        private readonly double[] checkpointCorrections = new double[16];
        private SelectionCheckpoint checkpoint;
        private bool hasCheckpoint;
        private double lastNow = -1, settleUntil, lastCorrection = -1, motionAge, activitySince, nominalSeconds = 18;
        private float motionGain;
        private bool lastUsable, motionPaused;
        private int candidateCount, plannedIndex = -1, shotNumber, familyIndex;
        private CinematicActivity activity, pendingActivity;
        private string family = "Establish";

        private struct SelectionCheckpoint
        {
            internal double Now, ShotAge, MotionAge, SettleUntil, LastCorrection, NominalSeconds, DesiredSeconds;
            internal int Previous, ShotNumber, FamilyIndex, CorrectionCount;
            internal float MotionGain;
            internal bool MotionPaused;
            internal string Family;
        }

        public int SelectedIndex => selection.SelectedIndex;
        /// <summary>Challenger currently auditioning to replace the live shot;
        /// the adapter promotes it to per-frame evaluation while standby
        /// candidates stay on their slower cadence.</summary>
        public int PendingIndex => selection.PendingIndex;
        public double ShotAge { get; private set; }
        public double DesiredSeconds { get; private set; } = 18;
        public int RecentCorrections => corrections.Count;
        public string Reason { get; private set; } = "Awaiting camera candidates";
        public CinematicShotIntent Intent => intent.Intent;
        public string IntentReason => intent.Reason;
        public ShotTrajectoryKind TrajectoryKind => activity == CinematicActivity.Combat ? ShotTrajectoryKind.Tracking :
            activity == CinematicActivity.Work ? ShotTrajectoryKind.ActivityArea :
            activity == CinematicActivity.Travel && (familyIndex == 0 || familyIndex == 4) ? ShotTrajectoryKind.Tracking :
            familyIndex == 0 || familyIndex == 1 ? ShotTrajectoryKind.Arc :
            familyIndex == 2 ? ShotTrajectoryKind.Dolly : ShotTrajectoryKind.Establish;

        public CinematicShotDirector() { ClearHistory(); }

        /// <summary>Call once per rendered frame; scores for standby candidates
        /// may be cached by the adapter. spaceFraction measures usable local
        /// camera options, not whether the game scene is loaded.</summary>
        public int Select(double now, double deltaSeconds, IReadOnlyList<float> scores,
            IReadOnlyList<bool> eligible, CinematicActivity currentActivity, float speed,
            bool confined, float spaceFraction, bool usableFootage = true,
            IReadOnlyList<bool>? readable = null, ShotSituation? situation = null, ShotRole? roleOverride = null,
            IShotCandidateRanker? ranker = null, bool protectMoment = false,
            IReadOnlyList<int>? priorities = null, bool reconsiderPicture = false)
        {
            Validate(now, deltaSeconds, scores, eligible, speed, spaceFraction);
            if (hasCheckpoint && checkpoint.Now != now) hasCheckpoint = false;
            if (readable != null && readable.Count != scores.Count)
                throw new ArgumentException("Readable evidence must match the candidate count.", nameof(readable));
            if (candidateCount != scores.Count)
            {
                Reset();
                candidateCount = scores.Count;
            }
            int previous = selection.SelectedIndex;
            usableFootage &= previous < 0 || readable == null || readable[previous];
            bool gap = lastNow >= 0 && now - lastNow > ProceduralShotPolicy.MaximumEvidenceGapSeconds;
            double step = usableFootage && lastUsable && !gap ? Math.Min(deltaSeconds, 0.25) : 0;
            // A gap cannot prove a sustained improvement. Clear its pending
            // comparison, then evaluate newly supplied camera-quality evidence.
            if ((gap || usableFootage && !lastUsable) && previous >= 0 && eligible[previous])
                SeedCurrent(now, previous, scores, ranker);
            lastNow = now;
            lastUsable = usableFootage;
            TrimCorrections(now);
            UpdateActivity(now, currentActivity);
            if (situation.HasValue) intent.Update(now, deltaSeconds, situation.Value, usableFootage, roleOverride);
            DesiredSeconds = nominalSeconds * (confined ? 1.5 : 1);

            int best = ShotCandidateRanking.Best(scores, eligible, readable,
                previous >= 0 && eligible[previous] ? previous : -1, ranker, priorities), adequate = 0;
            for (int i = 0; i < scores.Count; i++)
            {
                weighted[i] = scores[i];
                if (!eligible[i]) continue;
                if ((readable == null || readable[i]) && scores[i] >= MinimumPlannedQuality) adequate++;
            }
            if (previous >= 0 && eligible[previous])
            {
                ShotAge += step;
                lastShown[previous] = now;
            }
            bool settling = now < settleUntil || corrections.Count >= 3;
            // An interior label alone must not prohibit direction forever. A
            // spacious hall with several readable alternatives supports slower
            // shot changes; cramped passages still retain the viable view.
            bool constrained = spaceFraction < 0.45f || adequate < 2 ||
                confined && (spaceFraction < 0.7f || adequate < 3);
            bool improving = previous >= 0 && best >= 0 && best != previous &&
                (scores[best] >= scores[previous] + (reconsiderPicture ? ProceduralShotPolicy.PoorViewImprovementMargin : ProceduralShotPolicy.ImprovementMargin) ||
                    priorities != null && priorities[best] > priorities[previous]);
            bool canPlan = usableFootage && previous >= 0 && eligible[previous] && !constrained &&
                !settling && !improving && !protectMoment && activity != CinematicActivity.Combat && ShotAge >= DesiredSeconds;
            if (!canPlan) plannedIndex = -1;
            else
            {
                if (!AdequateAlternative(plannedIndex, previous, best, scores, eligible, readable))
                    plannedIndex = ChooseAlternative(now, previous, best, scores, eligible, readable, ranker, priorities);
                if (plannedIndex >= 0)
                {
                    // Only already-readable, near-best alternatives receive an
                    // editorial preference. The existing policy still applies
                    // its persistence/dwell and unchanged physical eligibility.
                    for (int i = 0; i < scores.Count; i++) weighted[i] = scores[i] * 0.65f;
                    weighted[plannedIndex] = 0.96f;
                }
            }

            // Unusable presentation pauses editorial timing, not recovery from
            // an obscured but physically supported camera. Fresh evaluations
            // still pass through the existing improvement persistence/dwell.
            int chosen = selection.Select(now, new ArraySegment<float>(weighted, 0, scores.Count), eligible, readable, ranker, priorities, reconsiderPicture);
            if (chosen < 0)
            {
                ShotAge = motionAge = 0; motionGain = 0; plannedIndex = -1;
                Reason = "No physically eligible camera";
                return chosen;
            }
            if (chosen != previous)
            {
                // Selection is a proposal until the adapter validates the actual
                // transition. Preserve the current shot after crediting its
                // observed footage, including across same-frame reselection.
                if (previous >= 0 && !hasCheckpoint) CaptureSelection(now, previous);
                bool deliberate = chosen == plannedIndex;
                if (previous >= 0 && !deliberate) NotifyCorrection(now);
                BeginShot(chosen, now, confined);
                Reason = previous < 0 ? "Establishing readable coverage" :
                    deliberate ? "Fresh composition after usable shot duration" : "Reframing for visibility or clearance";
            }
            else Reason = !usableFootage ? "Shot pacing paused while footage is unavailable" :
                protectMoment ? "Preserving an observed action moment" : settling ? "Settling after camera corrections" : constrained ? "Holding within limited camera space" :
                plannedIndex >= 0 ? "Confirming a fresh readable composition" : "Developing " + family.ToLowerInvariant() + " shot";

            motionPaused = !usableFootage || protectMoment || now < settleUntil || corrections.Count >= 3;
            if (!motionPaused) motionAge += step;
            float freedom = Clamp01((spaceFraction - 0.25f) / 0.65f);
            if (confined || adequate < 2) freedom *= 0.12f;
            float activityScale = activity == CinematicActivity.Combat ? 0 :
                activity == CinematicActivity.Travel ? 0.85f : activity == CinematicActivity.Work ? 0.45f : 1;
            // Readable moving subjects still receive intentional development.
            // Available space/correction history control decoration; sustained
            // speed alone must not reduce travel to almost a fixed chase camera.
            float targetGain = freedom * activityScale * (situation.HasValue ? Intent.MotionScale : 1) / (1 + speed * 0.015f);
            // Motion does not jump when activity/space classification changes.
            if (!motionPaused) motionGain += (targetGain - motionGain) * (float)(1 - Math.Exp(-step * 1.5));
            return chosen;
        }

        /// <summary>Read current movement or preview a future point. The preview
        /// does not advance timers or history, and stops during recovery.</summary>
        public CinematicShotMotion Motion(float strength = 1, double previewSeconds = 0)
        {
            if (!Finite(strength) || strength < 0 || !Finite(previewSeconds) || previewSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(strength));
            if (SelectedIndex < 0) return new CinematicShotMotion(0, 1, 0, "Unavailable");
            double age = motionAge + (motionPaused ? 0 : Math.Min(previewSeconds, 3));
            float progress = (float)Math.Min(1, age / Math.Max(8, DesiredSeconds));
            // Quintic easing starts and ends at rest, without repeating an orbit.
            float eased = progress * progress * progress * (progress * (progress * 6 - 15) + 10);
            // Match Camera.ShotMotion's 0..1.5 range. Even an external caller
            // above that range cannot exceed the documented movement envelope.
            float amount = eased * motionGain * Math.Min(1.5f, strength);
            switch (familyIndex)
            {
                case 0: return new CinematicShotMotion(-14 * amount, 1 + 0.025f * amount, 0.25f * amount, family);
                case 1: return new CinematicShotMotion(14 * amount, 1 + 0.025f * amount, 0.25f * amount, family);
                case 2: return new CinematicShotMotion(3 * amount, 1 - 0.12f * amount, -0.35f * amount, family);
                case 3: return new CinematicShotMotion(-3 * amount, 1 + 0.14f * amount, 1.1f * amount, family);
                default: return new CinematicShotMotion(5 * amount, 1 + 0.06f * amount, 0.5f * amount, family);
            }
        }

        /// <summary>Report a newly obstructed trajectory or a corrective cut.
        /// Repeated reports extend settling but count at most twice per second.</summary>
        public void NotifyCorrection(double now)
        {
            if (!Finite(now) || now < 0 || lastNow >= 0 && now < lastNow)
                throw new ArgumentOutOfRangeException(nameof(now));
            settleUntil = Math.Max(settleUntil, now + SettleSeconds);
            ShotAge = 0;
            plannedIndex = -1;
            motionPaused = true;
            TrimCorrections(now);
            if (lastCorrection < 0 || now - lastCorrection >= 0.5)
            {
                if (corrections.Count == 16) corrections.Dequeue();
                corrections.Enqueue(now); lastCorrection = now;
            }
        }

        public void Reset()
        {
            selection.Reset(); intent.Reset(); corrections.Clear(); ClearHistory();
            hasCheckpoint = false;
            candidateCount = shotNumber = familyIndex = 0; plannedIndex = -1;
            lastNow = lastCorrection = -1; settleUntil = motionAge = activitySince = ShotAge = 0;
            motionGain = 0; lastUsable = motionPaused = false;
            activity = pendingActivity = CinematicActivity.Idle;
            family = "Establish"; DesiredSeconds = nominalSeconds = 18; Reason = "Awaiting camera candidates";
        }

        /// <summary>Candidate poses changed during a bounded recovery search.
        /// Preserve the displayed shot while discarding comparisons measured
        /// against the previous standby geometry.</summary>
        public void InvalidatePending()
        { selection.InvalidatePending(); plannedIndex = -1; }

        /// <summary>The adapter rejected a proposed transition and kept a fresh,
        /// validated view of the previous shot. Roll back proposal-only pacing,
        /// history and motion changes without erasing real displayed coverage.</summary>
        public bool RejectSelection(int retainedIndex)
        {
            if (!hasCheckpoint || checkpoint.Previous != retainedIndex || !selection.RejectSelection(retainedIndex)) return false;
            ShotAge = checkpoint.ShotAge; motionAge = checkpoint.MotionAge;
            settleUntil = checkpoint.SettleUntil; lastCorrection = checkpoint.LastCorrection;
            nominalSeconds = checkpoint.NominalSeconds; DesiredSeconds = checkpoint.DesiredSeconds;
            shotNumber = checkpoint.ShotNumber; familyIndex = checkpoint.FamilyIndex;
            motionGain = checkpoint.MotionGain; motionPaused = checkpoint.MotionPaused; family = checkpoint.Family;
            Array.Copy(checkpointShown, lastShown, lastShown.Length);
            Array.Copy(checkpointFamilies, familyLastUsed, familyLastUsed.Length);
            corrections.Clear();
            for (int i = 0; i < checkpoint.CorrectionCount; i++) corrections.Enqueue(checkpointCorrections[i]);
            plannedIndex = -1; hasCheckpoint = false;
            Reason = "Retaining the validated readable shot after rejecting a transition";
            return true;
        }

        /// <summary>A policy reset (for example dynamic/steady mode change) has
        /// no previous selection checkpoint. Adopt the adapter's freshly
        /// validated displayed pose instead of tracking a denied proposal.</summary>
        public bool AdoptValidatedSelection(double now, int retainedIndex)
        {
            // Only a same-tick proposal with no shown duration can have its
            // history entry removed here; established shot history is retained.
            int denied = selection.SelectedIndex;
            if (!selection.AdoptValidatedSelection(now, retainedIndex)) return false;
            if (denied >= 0 && denied != retainedIndex && ShotAge == 0 && lastShown[denied] == now)
                lastShown[denied] = -1;
            hasCheckpoint = false;
            lastNow = now; lastUsable = true;
            BeginShot(retainedIndex, now, false);
            settleUntil = Math.Max(settleUntil, now + SettleSeconds);
            motionPaused = true;
            Reason = "Adopted the freshly validated displayed shot after policy reset";
            return true;
        }

        private void CaptureSelection(double now, int previous)
        {
            checkpoint = new SelectionCheckpoint
            {
                Now = now, Previous = previous, ShotAge = ShotAge, MotionAge = motionAge,
                SettleUntil = settleUntil, LastCorrection = lastCorrection, NominalSeconds = nominalSeconds,
                DesiredSeconds = DesiredSeconds, ShotNumber = shotNumber, FamilyIndex = familyIndex,
                MotionGain = motionGain, MotionPaused = motionPaused, Family = family, CorrectionCount = corrections.Count
            };
            Array.Copy(lastShown, checkpointShown, lastShown.Length);
            Array.Copy(familyLastUsed, checkpointFamilies, familyLastUsed.Length);
            corrections.CopyTo(checkpointCorrections, 0);
            hasCheckpoint = true;
        }

        /// <summary>Steady mode retains environment and locomotion adaptation
        /// without requesting editorial angle changes. Repeated same-frame
        /// calls cannot advance the intent's evidence clock twice.</summary>
        public CinematicShotIntent UpdateIntent(double now, double dt, ShotSituation situation, bool usable, ShotRole? roleOverride = null) =>
            intent.Update(now, dt, situation, usable, roleOverride);

        private void BeginShot(int selected, double now, bool confined)
        {
            ShotAge = motionAge = 0; motionGain = 0;
            lastShown[selected] = now; plannedIndex = -1;
            // Prefer an appropriate, recently unused movement. This is a small
            // set of reusable shot idioms, not a looping sequence of camera moves.
            int preferred = (selected * 3 + shotNumber * 7 + (int)activity * 2) % familyLastUsed.Length;
            double bestRank = double.NegativeInfinity;
            for (int i = 0; i < familyLastUsed.Length; i++)
            {
                double rank = Math.Min(6, shotNumber - familyLastUsed[i]) + (i == preferred ? 0.75 : 0);
                if (activity == CinematicActivity.Work && (i == 2 || i == 4)) rank += 0.3;
                if (activity == CinematicActivity.Travel && (i == 0 || i == 1)) rank += 0.3;
                if (rank > bestRank) { bestRank = rank; familyIndex = i; }
            }
            familyLastUsed[familyIndex] = shotNumber++;
            family = familyIndex == 0 ? "Left arc" : familyIndex == 1 ? "Right arc" :
                familyIndex == 2 ? "Approach" : familyIndex == 3 ? "Reveal" : "Rise";
            double baseSeconds = activity == CinematicActivity.Travel ? 20 : activity == CinematicActivity.Work ? 19 : 16;
            nominalSeconds = baseSeconds + (shotNumber * 3 + selected) % 6;
            DesiredSeconds = nominalSeconds * (confined ? 1.5 : 1);
        }

        private void UpdateActivity(double now, CinematicActivity value)
        {
            if (value != pendingActivity) { pendingActivity = value; activitySince = now; }
            // Combat suppresses decorative movement immediately. Other activity
            // changes need persistence and do not themselves force a camera cut.
            if (value == CinematicActivity.Combat || now - activitySince >= 2) activity = value;
        }

        private int ChooseAlternative(double now, int current, int best,
            IReadOnlyList<float> scores, IReadOnlyList<bool> eligible, IReadOnlyList<bool>? readable,
            IShotCandidateRanker? ranker, IReadOnlyList<int>? priorities)
        {
            // The native ranker receives the same bounded history preference
            // as regular selection. Only already-readable near-best shots may
            // compete for a deliberate edit; history cannot admit a bad view.
            for (int i = 0; i < scores.Count; i++)
            {
                plannedEligibility[i] = AdequateAlternative(i, current, best, scores, eligible, readable);
                double away = lastShown[i] < 0 ? HistorySeconds : Math.Min(HistorySeconds, now - lastShown[i]);
                plannedScores[i] = (float)((scores[i] + away / HistorySeconds * 0.12) / 1.12);
            }
            return ShotCandidateRanking.Best(new ArraySegment<float>(plannedScores, 0, scores.Count),
                new ArraySegment<bool>(plannedEligibility, 0, scores.Count), readable, -1, ranker, priorities);
        }

        private static bool AdequateAlternative(int index, int current, int best,
            IReadOnlyList<float> scores, IReadOnlyList<bool> eligible, IReadOnlyList<bool>? readable) =>
            index >= 0 && index < scores.Count && index != current && best >= 0 && eligible[index] &&
            (readable == null || readable[index]) &&
            scores[index] >= MinimumPlannedQuality && scores[index] >= scores[best] - 0.08f;

        private void SeedCurrent(double now, int previous, IReadOnlyList<float> scores, IShotCandidateRanker? ranker)
        {
            Array.Clear(seedEligibility, 0, seedEligibility.Length);
            seedEligibility[previous] = true;
            selection.Reset();
            selection.Select(now, scores, new ArraySegment<bool>(seedEligibility, 0, scores.Count), ranker: ranker);
            plannedIndex = -1;
        }
        private void ClearHistory()
        {
            for (int i = 0; i < lastShown.Length; i++) lastShown[i] = -1;
            for (int i = 0; i < familyLastUsed.Length; i++) familyLastUsed[i] = -10;
        }
        private void TrimCorrections(double now)
        { while (corrections.Count > 0 && now - corrections.Peek() > CorrectionWindow) corrections.Dequeue(); }
        private void Validate(double now, double delta, IReadOnlyList<float> scores,
            IReadOnlyList<bool> eligible, float speed, float space)
        {
            if (!Finite(now) || now < 0 || !Finite(delta) || delta < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
            if (scores == null || eligible == null || scores.Count != eligible.Count || scores.Count > ProceduralShotPolicy.MaximumCandidates)
                throw new ArgumentException("Provide bounded, equally sized camera score and eligibility arrays.");
            for (int i = 0; i < scores.Count; i++)
                if (!Finite(scores[i]) || scores[i] < 0 || scores[i] > 1) throw new ArgumentOutOfRangeException(nameof(scores));
            if (!Finite(speed) || speed < 0 || !Finite(space) || space < 0 || space > 1)
                throw new ArgumentOutOfRangeException(nameof(space));
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static float Clamp01(float value) => Math.Max(0, Math.Min(1, value));
    }
}
