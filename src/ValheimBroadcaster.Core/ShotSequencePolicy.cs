using System;

namespace ValheimBroadcaster.Core
{
    public enum ShotSequenceBeat { Establish, Develop, Detail, Aftermath, Reestablish, Settle }

    /// <summary>A proposed editorial beat, never permission to show an unreadable
    /// view. Scale is resolved against the actual scene by ShotScalePolicy.</summary>
    public readonly struct ShotSequenceCue
    {
        public readonly ShotSequenceBeat Beat;
        public readonly ShotRole Role;
        public readonly ShotScale Scale;
        public readonly long Revision;
        public readonly float MotionMultiplier;
        public readonly string Reason;
        internal ShotSequenceCue(ShotSequenceBeat beat, ShotRole role, ShotScale scale,
            long revision, float motion, string reason)
        { Beat = beat; Role = role; Scale = scale; Revision = revision; MotionMultiplier = motion; Reason = reason; }
    }

    /// <summary>Bounded, interruptible scene idioms. Evidence selects a sequence;
    /// only actually displayed readable footage fulfills its beats. A sequence
    /// finishes in a quiet sustaining beat instead of repeating a fixed cycle.</summary>
    public sealed class ShotSequencePolicy
    {
        private enum Episode { Unknown, Idle, Interaction, Travel, Work, Combat, Scenic }
        private const double EvidenceSeconds = 1.5, MaximumGap = 1, MissingPictureResetSeconds = 3;
        private double lastNow = -1, pendingAge, unusableAge, attemptedAge;
        private bool initialized, lastUsable, wasEnabled, interruptionReset;
        private Episode episode, pending;
        private ShotSceneKind kind;
        private long revision;
        public double BeatAge { get; private set; }
        public double SequenceAge { get; private set; }
        public long Revision => revision;
        public ShotSequenceCue Cue { get; private set; } = new ShotSequenceCue(
            ShotSequenceBeat.Establish, ShotRole.Establish, ShotScale.Wide, 0, .55f, "Awaiting an observed scene");

        /// <param name="requestedIntentPresented">True only if the previous
        /// live rendered picture fulfilled the current cue's role and resolved
        /// scale. A candidate solve or rejected cut is not presentation.</param>
        public ShotSequenceCue Update(double now, double dt, ShotSituation scene,
            bool usableFootage, bool enabled, bool requestedIntentPresented = true)
        {
            if (!Finite(now) || now < 0 || !Finite(dt) || dt < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
            bool gap = lastNow >= 0 && now - lastNow > MaximumGap;
            double elapsed = lastNow >= 0 && !gap ? Math.Min(.25, Math.Min(dt, now - lastNow)) : 0;
            if (gap || initialized && kind != scene.Kind) Reset();
            lastNow = now;
            Episode observed = Classify(scene);
            if (!initialized)
            {
                initialized = true; kind = scene.Kind;
                Start(observed, scene, "Establishing observed scene context");
                lastUsable = false; wasEnabled = enabled;
            }
            if (!enabled)
            {
                lastUsable = wasEnabled = false; pendingAge = attemptedAge = 0;
                return Cue;
            }
            if (!wasEnabled && observed != episode)
                Start(observed, scene, "Re-establishing current activity after an editorial pause");
            bool credited = usableFootage && requestedIntentPresented;
            double shown = credited && lastUsable && wasEnabled ? elapsed : 0;
            lastUsable = credited; wasEnabled = true;

            if (!usableFootage)
            {
                unusableAge += elapsed;
                if (!interruptionReset && unusableAge >= MissingPictureResetSeconds)
                {
                    Start(observed, scene, "Re-establishing after unavailable footage");
                    interruptionReset = true;
                }
            }
            else { unusableAge = 0; interruptionReset = false; }

            if (observed != pending) { pending = observed; pendingAge = 0; }
            pendingAge += elapsed;
            if (observed != episode && (observed == Episode.Combat || observed == Episode.Unknown || pendingAge >= EvidenceSeconds))
            {
                bool aftermath = episode == Episode.Combat && observed == Episode.Idle;
                if (aftermath)
                {
                    episode = observed; pendingAge = 0;
                    Begin(ShotSequenceBeat.Aftermath, scene,
                        "Observed action has subsided; retaining scene context without inferring its outcome");
                }
                else Start(observed, scene, "Establishing the changed observed activity");
                shown = 0;
            }

            BeatAge += shown; SequenceAge += shown;
            attemptedAge = usableFootage && !requestedIntentPresented ? attemptedAge + elapsed : 0;
            // Constrained geometry can make a requested close view impossible.
            // Abandon it after a bounded attempt without pretending it was shown.
            if (attemptedAge >= 12 && Cue.Beat != ShotSequenceBeat.Settle)
                Begin(ShotSequenceBeat.Settle, scene, "Requested beat unavailable; sustaining a readable contextual view");
            else Advance(scene);
            return Cue;
        }

        private void Advance(ShotSituation scene)
        {
            switch (Cue.Beat)
            {
                case ShotSequenceBeat.Establish:
                    if (BeatAge >= (episode == Episode.Combat ? 3 : 6))
                        Begin(episode == Episode.Unknown ? ShotSequenceBeat.Settle : ShotSequenceBeat.Develop, scene,
                            "Context shown; developing the observed activity");
                    break;
                case ShotSequenceBeat.Develop:
                    double duration = episode == Episode.Combat ? 18 : episode == Episode.Travel ? 14 : 10;
                    if (BeatAge >= duration)
                        Begin(episode == Episode.Scenic || episode == Episode.Work && scene.HasInterest ||
                            episode == Episode.Idle || episode == Episode.Interaction
                            ? ShotSequenceBeat.Detail : episode == Episode.Travel
                            ? ShotSequenceBeat.Reestablish : ShotSequenceBeat.Settle, scene,
                            episode == Episode.Combat ? "Sustaining the live action while its outcome remains unknown" :
                            "Developed context supports a complementary composition");
                    break;
                case ShotSequenceBeat.Detail:
                    if (BeatAge >= 6) Begin(ShotSequenceBeat.Reestablish, scene, "Re-establishing geography after the closer composition");
                    break;
                case ShotSequenceBeat.Aftermath:
                    if (BeatAge >= 3) Begin(ShotSequenceBeat.Settle, scene, "Bounded aftermath shown; sustaining current context");
                    break;
                case ShotSequenceBeat.Reestablish:
                    if (BeatAge >= 5) Begin(ShotSequenceBeat.Settle, scene, "Sequence covered; retaining the scene without repeating its beats");
                    break;
            }
        }

        private void Start(Episode next, ShotSituation scene, string reason)
        {
            episode = pending = next; pendingAge = 0; SequenceAge = 0;
            Begin(ShotSequenceBeat.Establish, scene, reason);
        }

        private void Begin(ShotSequenceBeat beat, ShotSituation scene, string reason)
        {
            BeatAge = attemptedAge = 0; revision++;
            ShotRole role = episode == Episode.Scenic ? ShotRole.Landscape :
                episode == Episode.Combat ? ShotRole.Action :
                beat == ShotSequenceBeat.Establish || beat == ShotSequenceBeat.Reestablish ||
                beat == ShotSequenceBeat.Aftermath ? ShotRole.Establish :
                episode == Episode.Travel ? ShotRole.Tracking :
                episode == Episode.Work && scene.HasInterest ? ShotRole.Detail :
                episode == Episode.Interaction ? ShotRole.Interaction : ShotRole.Establish;
            ShotScale scale = beat == ShotSequenceBeat.Establish || beat == ShotSequenceBeat.Reestablish
                ? ShotScale.Wide : beat == ShotSequenceBeat.Detail ? ShotScale.Medium : ShotScale.Full;
            float motion = beat == ShotSequenceBeat.Establish ? .55f : beat == ShotSequenceBeat.Develop ? 1 :
                beat == ShotSequenceBeat.Detail ? .35f : beat == ShotSequenceBeat.Aftermath ? 0 :
                beat == ShotSequenceBeat.Reestablish ? .5f : .4f;
            Cue = new ShotSequenceCue(beat, role, scale, revision, motion, reason);
        }

        private static Episode Classify(ShotSituation scene)
        {
            if (scene.Kind == ShotSceneKind.Scenic) return Episode.Scenic;
            if (scene.Confidence < .6f) return Episode.Unknown;
            if (scene.Activity == CinematicActivity.Combat) return Episode.Combat;
            if (scene.Activity == CinematicActivity.Work) return Episode.Work;
            if (scene.Activity == CinematicActivity.Travel || scene.Locomotion == ShotLocomotion.Boat ||
                scene.Locomotion == ShotLocomotion.Swim || scene.Locomotion == ShotLocomotion.Vertical) return Episode.Travel;
            return scene.Companions > 0 || scene.Opponents > 0 ? Episode.Interaction : Episode.Idle;
        }

        public void Reset()
        {
            initialized = lastUsable = wasEnabled = interruptionReset = false;
            lastNow = -1; BeatAge = SequenceAge = pendingAge = attemptedAge = unusableAge = 0;
            episode = pending = Episode.Unknown; revision++;
            Cue = new ShotSequenceCue(ShotSequenceBeat.Establish, ShotRole.Establish, ShotScale.Wide,
                revision, .55f, "Awaiting an observed scene");
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
