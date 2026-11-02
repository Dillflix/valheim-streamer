using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum EncounterKind { None, Hunting, Threat, Combat }

    /// <summary>Only a previously qualified target may survive the end of draw.
    /// This is recent-aim context, never proof of a released projectile or hit.</summary>
    public sealed class RangedAimContinuity
    {
        private double until = -1, lastAt = -1;
        public bool Observe(double now, bool drawing, EncounterKind selected)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentException("Ranged aim requires finite time.");
            if (lastAt >= 0 && (now < lastAt || now - lastAt > 1)) Reset();
            lastAt = now;
            if (drawing) until = now + 2.5;
            return drawing || selected == EncounterKind.Hunting && now <= until;
        }
        public void Reset() { until = lastAt = -1; }
    }

    /// <summary>Loaded, living actor evidence. ClearConnection means a structural
    /// sightline, not pixel visibility; aim alignment is explicitly inferred.</summary>
    public readonly struct EncounterObservation
    {
        public readonly string Id;
        public readonly float Distance, GroupDistance, AimAlignment, Prominence;
        public readonly bool ClearConnection, ClearAimLine, Hostile, Huntable, TargetsGroup, Attacking, Damaged, RetainedCombat;
        public EncounterObservation(string id, float distance, float groupDistance, float aimAlignment, float prominence,
            bool clearConnection = true, bool hostile = true, bool huntable = true, bool targetsGroup = false,
            bool attacking = false, bool damaged = false, bool retainedCombat = false, bool clearAimLine = true)
        {
            Id = id; Distance = distance; GroupDistance = groupDistance; AimAlignment = aimAlignment; Prominence = prominence;
            ClearConnection = clearConnection; ClearAimLine = clearAimLine; Hostile = hostile; Huntable = huntable; TargetsGroup = targetsGroup;
            Attacking = attacking; Damaged = damaged; RetainedCombat = retainedCombat;
        }
        public bool IsValid => !string.IsNullOrEmpty(Id) && Id.Length <= 128 && Finite(Distance) && Distance >= 0 && Distance <= 96 &&
            Finite(GroupDistance) && GroupDistance >= 0 && GroupDistance <= 96 && Finite(AimAlignment) && AimAlignment >= -1 &&
            AimAlignment <= 1 && Finite(Prominence) && Prominence >= 0 && Prominence <= 1;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct EncounterCandidate
    {
        public readonly string Id;
        public readonly EncounterKind Kind;
        public readonly float Score, Confidence, Distance;
        internal EncounterCandidate(string id, EncounterKind kind, float score, float confidence, float distance)
        { Id = id; Kind = kind; Score = score; Confidence = confidence; Distance = distance; }
    }

    /// <summary>Evidence-qualified encounter interest, independent of framing.
    /// Missing targets are removed immediately; quality changes require a stable
    /// challenger. No camera or simulation authority is implied by selection.</summary>
    public sealed class EncounterPolicy
    {
        public const int MaximumCandidates = 64;
        private readonly List<EncounterCandidate> ranked = new List<EncounterCandidate>(MaximumCandidates);
        private string pending = "";
        private EncounterKind pendingKind;
        private double pendingSince, lastAt = -1;
        public EncounterCandidate Selected { get; private set; }
        public IReadOnlyList<EncounterCandidate> Ranked => ranked;

        public EncounterCandidate Update(double now, bool aiming, bool groupAttack, IReadOnlyList<EncounterObservation> observations)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || observations == null || observations.Count > MaximumCandidates)
                throw new ArgumentException("Encounter survey requires finite time and bounded observations.");
            if (lastAt >= 0 && (now < lastAt || now - lastAt > 1)) Reset();
            lastAt = now; ranked.Clear();
            foreach (EncounterObservation observation in observations)
            {
                EncounterCandidate candidate = Classify(observation, aiming, groupAttack);
                if (candidate.Kind != EncounterKind.None) ranked.Add(candidate);
            }
            ranked.Sort((a, b) => { int score = b.Score.CompareTo(a.Score); return score != 0 ? score : string.CompareOrdinal(a.Id, b.Id); });
            EncounterCandidate retained = default;
            foreach (EncounterCandidate candidate in ranked) if (candidate.Id == Selected.Id) { retained = candidate; break; }
            Selected = retained;
            if (ranked.Count == 0) { pending = ""; return Selected; }
            EncounterCandidate best = ranked[0];
            // Several animals within the inferred body-facing cone do not
            // establish which one the player is aiming at. Keep an already
            // admitted interest if still credible, otherwise decline to guess.
            if (best.Kind == EncounterKind.Hunting && ranked.Count > 1 && ranked[1].Kind == EncounterKind.Hunting &&
                best.Score - ranked[1].Score < .15f)
            { pending = ""; return Selected; }
            if (best.Id == Selected.Id && best.Kind == Selected.Kind) { pending = ""; return Selected; }
            bool emergency = best.Kind == EncounterKind.Combat && Selected.Kind != EncounterKind.Combat;
            if (!emergency && Selected.Kind != EncounterKind.None && best.Score < Selected.Score + .35f)
            { pending = ""; return Selected; }
            if (pending != best.Id || pendingKind != best.Kind)
            { pending = best.Id; pendingKind = best.Kind; pendingSince = now; }
            double delay = emergency ? .25 : Selected.Kind != EncounterKind.None ? .75 : best.Kind == EncounterKind.Hunting ? .65 : .8;
            if (now - pendingSince >= delay)
            { Selected = best; pending = ""; }
            return Selected;
        }

        public static EncounterCandidate Classify(EncounterObservation value, bool aiming, bool groupAttack)
        {
            if (!value.IsValid || !value.ClearConnection) return default;
            // Remote attack flags establish context, not an exact victim. A
            // corroborated close encounter is stronger than distant proximity.
            bool combat = value.Hostile && (value.TargetsGroup && value.GroupDistance <= 40 ||
                value.GroupDistance <= 12 && (value.Attacking || groupAttack && value.Damaged) ||
                value.RetainedCombat && value.GroupDistance <= 24);
            if (combat)
                return new EncounterCandidate(value.Id, EncounterKind.Combat,
                    4 + (value.TargetsGroup ? .8f : 0) + (value.Attacking ? .4f : 0) + (value.Damaged ? .2f : 0) +
                    value.Prominence * .35f + .5f * (1 - Math.Min(1, value.GroupDistance / 24)),
                    value.TargetsGroup ? .95f : .8f, value.Distance);
            // A narrow cone plus an actually active draw state is required.
            // Looking at wildlife, or a stale draw percentage, is insufficient.
            if (aiming && value.ClearAimLine && value.Huntable && value.Distance >= 6 && value.Distance <= 96 && value.AimAlignment >= .985f)
                return new EncounterCandidate(value.Id, EncounterKind.Hunting,
                    2 + (value.AimAlignment - .985f) * 40 + (value.Damaged ? .2f : 0) + .1f * (1 - value.Distance / 96),
                    .65f, value.Distance);
            if (value.Hostile && value.Prominence >= .5f && value.GroupDistance <= 20)
                return new EncounterCandidate(value.Id, EncounterKind.Threat,
                    1 + value.Prominence * .4f + .3f * (1 - value.GroupDistance / 20), .65f, value.Distance);
            return default;
        }

        public void Reset() { ranked.Clear(); Selected = default; pending = ""; pendingKind = EncounterKind.None; pendingSince = 0; lastAt = -1; }
    }
}
