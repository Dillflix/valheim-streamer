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
        private readonly float[] weighted = new float[ProceduralShotPolicy.MaximumCandidates];
        private readonly bool[] seedEligibility = new bool[ProceduralShotPolicy.MaximumCandidates];
        private readonly double[] lastShown = new double[ProceduralShotPolicy.MaximumCandidates];
        private readonly int[] familyLastUsed = new int[5];
        private readonly Queue<double> corrections = new Queue<double>();
        private double lastNow = -1, settleUntil, lastCorrection = -1, motionAge, activitySince, nominalSeconds = 18;
        private float motionGain;
        private bool lastUsable, motionPaused;
        private int candidateCount, plannedIndex = -1, shotNumber, familyIndex;
        private CinematicActivity activity, pendingActivity;
        private string family = "Establish";

        public int SelectedIndex => selection.SelectedIndex;
        public double ShotAge { get; private set; }
        public double DesiredSeconds { get; private set; } = 18;
        public int RecentCorrections => corrections.Count;
        public string Reason { get; private set; } = "Awaiting camera candidates";
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
            bool confined, float spaceFraction, bool usableFootage = true)
        {
            Validate(now, deltaSeconds, scores, eligible, speed, spaceFraction);
            if (candidateCount != scores.Count)
            {
                Reset();
                candidateCount = scores.Count;
            }
            int previous = selection.SelectedIndex;
            bool gap = lastNow >= 0 && now - lastNow > ProceduralShotPolicy.MaximumEvidenceGapSeconds;
            double step = usableFootage && lastUsable && !gap ? Math.Min(deltaSeconds, 0.25) : 0;
            // Stale or unseen frames cannot prove a candidate improved. Retain
            // the current eligible camera while clearing its pending comparison.
            if ((gap || usableFootage && !lastUsable) && previous >= 0 && eligible[previous])
                SeedCurrent(now, previous, scores);
            lastNow = now;
            lastUsable = usableFootage;
            TrimCorrections(now);
            UpdateActivity(now, currentActivity);
            DesiredSeconds = nominalSeconds * (confined ? 1.5 : 1);

            int best = -1, adequate = 0;
            for (int i = 0; i < scores.Count; i++)
            {
                weighted[i] = scores[i];
                if (!eligible[i]) continue;
                if (best < 0 || scores[i] > scores[best]) best = i;
                if (scores[i] >= MinimumPlannedQuality) adequate++;
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
                scores[best] >= scores[previous] + ProceduralShotPolicy.ImprovementMargin;
            bool canPlan = usableFootage && previous >= 0 && eligible[previous] && !constrained &&
                !settling && !improving && activity != CinematicActivity.Combat && ShotAge >= DesiredSeconds;
            if (!canPlan) plannedIndex = -1;
            else
            {
                if (!AdequateAlternative(plannedIndex, previous, best, scores, eligible))
                    plannedIndex = ChooseAlternative(now, previous, best, scores, eligible);
                if (plannedIndex >= 0)
                {
                    // Only already-readable, near-best alternatives receive an
                    // editorial preference. The existing policy still applies
                    // its persistence/dwell and unchanged physical eligibility.
                    for (int i = 0; i < scores.Count; i++) weighted[i] = scores[i] * 0.65f;
                    weighted[plannedIndex] = 0.96f;
                }
            }

            int chosen;
            if (!usableFootage && previous >= 0 && eligible[previous]) chosen = previous;
            else chosen = selection.Select(now, new ArraySegment<float>(weighted, 0, scores.Count), eligible);
            if (chosen < 0)
            {
                ShotAge = motionAge = 0; motionGain = 0; plannedIndex = -1;
                Reason = "No physically eligible camera";
                return chosen;
            }
            if (chosen != previous)
            {
                bool deliberate = chosen == plannedIndex;
                if (previous >= 0 && !deliberate) NotifyCorrection(now);
                BeginShot(chosen, now, confined);
                Reason = previous < 0 ? "Establishing readable coverage" :
                    deliberate ? "Fresh composition after usable shot duration" : "Reframing for visibility or clearance";
            }
            else Reason = !usableFootage ? "Shot pacing paused while footage is unavailable" :
                settling ? "Settling after camera corrections" : constrained ? "Holding within limited camera space" :
                plannedIndex >= 0 ? "Confirming a fresh readable composition" : "Developing " + family.ToLowerInvariant() + " shot";

            motionPaused = !usableFootage || now < settleUntil || corrections.Count >= 3;
            if (!motionPaused) motionAge += step;
            float freedom = Clamp01((spaceFraction - 0.25f) / 0.65f);
            if (confined || adequate < 2) freedom *= 0.12f;
            float activityScale = activity == CinematicActivity.Combat ? 0 :
                activity == CinematicActivity.Travel ? 0.85f : activity == CinematicActivity.Work ? 0.45f : 1;
            // Readable moving subjects still receive intentional development.
            // Available space/correction history control decoration; sustained
            // speed alone must not reduce travel to almost a fixed chase camera.
            float targetGain = freedom * activityScale / (1 + speed * 0.015f);
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
            selection.Reset(); corrections.Clear(); ClearHistory();
            candidateCount = shotNumber = familyIndex = 0; plannedIndex = -1;
            lastNow = lastCorrection = -1; settleUntil = motionAge = activitySince = ShotAge = 0;
            motionGain = 0; lastUsable = motionPaused = false;
            activity = pendingActivity = CinematicActivity.Idle;
            family = "Establish"; DesiredSeconds = nominalSeconds = 18; Reason = "Awaiting camera candidates";
        }

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
            IReadOnlyList<float> scores, IReadOnlyList<bool> eligible)
        {
            int chosen = -1;
            double bestRank = double.NegativeInfinity;
            for (int i = 0; i < scores.Count; i++)
            {
                if (!AdequateAlternative(i, current, best, scores, eligible)) continue;
                double away = lastShown[i] < 0 ? HistorySeconds : Math.Min(HistorySeconds, now - lastShown[i]);
                double rank = scores[i] + away / HistorySeconds * 0.12;
                if (rank > bestRank) { chosen = i; bestRank = rank; }
            }
            return chosen;
        }

        private static bool AdequateAlternative(int index, int current, int best,
            IReadOnlyList<float> scores, IReadOnlyList<bool> eligible) =>
            index >= 0 && index < scores.Count && index != current && best >= 0 && eligible[index] &&
            scores[index] >= MinimumPlannedQuality && scores[index] >= scores[best] - 0.08f;

        private void SeedCurrent(double now, int previous, IReadOnlyList<float> scores)
        {
            Array.Clear(seedEligibility, 0, seedEligibility.Length);
            seedEligibility[previous] = true;
            selection.Reset();
            selection.Select(now, scores, new ArraySegment<bool>(seedEligibility, 0, scores.Count));
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
