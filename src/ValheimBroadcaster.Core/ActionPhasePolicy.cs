using System;

namespace ValheimBroadcaster.Core
{
    public enum ObservedActionPhase { Unknown, Observe, Approach, Aim, Action, FollowThrough, Aftermath, Arrival, Work }

    /// <summary>Read-only observations, not inferred hits, victories or exact aim.
    /// Health loss and attack animation establish only a moment worth preserving.</summary>
    public readonly struct ActionPhaseEvidence
    {
        public readonly long SceneId;
        public readonly bool Fresh, Drawing, Attacking, HealthLost, Moving, Working, Doorway;
        public readonly EncounterKind Encounter;
        public readonly float Confidence;
        public ActionPhaseEvidence(long sceneId, bool fresh, bool drawing, bool attacking, bool healthLost,
            bool moving, bool working, bool doorway, EncounterKind encounter, float confidence)
        {
            SceneId = sceneId; Fresh = fresh; Drawing = drawing; Attacking = attacking; HealthLost = healthLost;
            Moving = moving; Working = working; Doorway = doorway; Encounter = encounter; Confidence = confidence;
        }
    }

    public readonly struct ActionPhaseCue
    {
        public readonly ObservedActionPhase Phase;
        public readonly long Revision;
        public readonly bool ProtectMoment, ComplementaryWindow;
        public readonly string Reason;
        internal ActionPhaseCue(ObservedActionPhase phase, long revision, bool protect, bool complementary, string reason)
        { Phase = phase; Revision = revision; ProtectMoment = protect; ComplementaryWindow = complementary; Reason = reason; }
    }

    /// <summary>Observed phase boundaries complement the shown-footage sequence.
    /// Evidence loss resets rather than manufacturing an ending. A draw ending
    /// receives follow-through without claiming that a projectile was released.</summary>
    public sealed class ActionPhasePolicy
    {
        private double last = -1, protectedUntil, quietSince = -1, movementSince = -1, transitionUntil;
        private long scene, revision;
        private bool drawing, attacking, moving, hadEngagement;
        private ObservedActionPhase phase;
        public ActionPhaseCue Cue { get; private set; }

        public ActionPhaseCue Observe(double now, ActionPhaseEvidence evidence)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (last >= 0 && (now < last || now - last > 1) || scene != evidence.SceneId) Reset();
            if (!evidence.Fresh || evidence.SceneId <= 0 || float.IsNaN(evidence.Confidence) || float.IsInfinity(evidence.Confidence) || evidence.Confidence < .6f || evidence.Confidence > 1)
            { Reset(); last = now; return Set(ObservedActionPhase.Unknown, false, false, "Action phase evidence unavailable"); }
            if (now == last) return Cue;
            scene = evidence.SceneId;
            if (evidence.Drawing || evidence.Attacking && !attacking || evidence.HealthLost)
                protectedUntil = Math.Max(protectedUntil, now + 1.25);
            if (drawing && !evidence.Drawing)
            { transitionUntil = now + 1.5; protectedUntil = Math.Max(protectedUntil, transitionUntil); }
            if (evidence.Moving)
            { if (!moving) movementSince = now; }
            else if (moving && movementSince >= 0 && now - movementSince >= 4)
                transitionUntil = Math.Max(transitionUntil, now + 2);
            bool engagement = evidence.Drawing || evidence.Attacking || evidence.Encounter == EncounterKind.Combat;
            if (engagement) { hadEngagement = true; quietSince = -1; }
            else if (hadEngagement && quietSince < 0) quietSince = now;
            ObservedActionPhase next;
            string reason;
            if (evidence.Drawing) { next = ObservedActionPhase.Aim; reason = "Observed ranged draw; preserving aim and immediate follow-through"; }
            else if (now < transitionUntil && (drawing || phase == ObservedActionPhase.FollowThrough))
            { next = ObservedActionPhase.FollowThrough; reason = "Draw ended; release and outcome remain unconfirmed"; }
            else if (evidence.Attacking || evidence.HealthLost || evidence.Encounter == EncounterKind.Combat)
            { next = ObservedActionPhase.Action; reason = "Observed engagement/action; exact victim and outcome unknown"; }
            else if (hadEngagement && quietSince >= 0 && now - quietSince >= 2 && now - quietSince < 7)
            { next = ObservedActionPhase.Aftermath; reason = "Sustained observed quiet after engagement; no outcome inferred"; }
            else if (now < transitionUntil && !evidence.Moving)
            { next = ObservedActionPhase.Arrival; reason = "Sustained movement stopped; establishing the new surroundings"; }
            else if (evidence.Working) { next = ObservedActionPhase.Work; reason = "Observed work with local context"; }
            else if (evidence.Moving) { next = ObservedActionPhase.Approach; reason = "Observed movement; destination and intent unknown"; }
            else { next = ObservedActionPhase.Observe; reason = "Observing current relationships and surroundings"; }
            if (quietSince >= 0 && now - quietSince >= 7) hadEngagement = false;
            bool protect = evidence.Drawing || evidence.Attacking || evidence.Doorway || now < protectedUntil;
            bool complementary = !protect && (next == ObservedActionPhase.Observe || next == ObservedActionPhase.Aftermath || next == ObservedActionPhase.Action);
            drawing = evidence.Drawing; attacking = evidence.Attacking; moving = evidence.Moving; last = now;
            return Set(next, protect, complementary, reason);
        }
        private ActionPhaseCue Set(ObservedActionPhase value, bool protect, bool complementary, string reason)
        {
            if (phase != value || Cue.Reason == null) revision++;
            phase = value;
            return Cue = new ActionPhaseCue(value, revision, protect, complementary, reason);
        }
        public void Reset()
        {
            last = quietSince = movementSince = -1; protectedUntil = transitionUntil = 0;
            scene = 0; drawing = attacking = moving = hadEngagement = false; revision++;
            phase = ObservedActionPhase.Unknown;
            Cue = new ActionPhaseCue(phase, revision, false, false, "Awaiting fresh action phase evidence");
        }
    }
}
