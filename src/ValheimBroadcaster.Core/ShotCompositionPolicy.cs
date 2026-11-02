using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Measurements in an anchor-centred camera projection. Coordinates
    /// are fractions of viewport width/height, with positive Y upwards. Extents
    /// bound the complete subject/group around that anchor. Motion is per second.
    /// Facing is the signed screen-right dot of observed body facing, not eye gaze.</summary>
    public struct ShotCompositionObservation
    {
        public float MotionX, MotionY, FacingX, FacingConfidence;
        public float InterestX, InterestY, InterestConfidence;
        public float LeftExtent, RightExtent, TopExtent, BottomExtent;
        public bool Grouped;
        public float PrimaryOffsetX, PrimaryOffsetY;
    }

    public readonly struct ShotComposition
    {
        public readonly float ScreenX, ScreenY, DesiredX, DesiredY;
        public readonly bool FitsEnvelope;
        public readonly string Reason;
        public ShotComposition(float x, float y, float desiredX, float desiredY, bool fits, string reason)
        { ScreenX = x; ScreenY = y; DesiredX = desiredX; DesiredY = desiredY; FitsEnvelope = fits; Reason = reason; }
    }

    /// <summary>
    /// Deliberate shot placement, separate from Cinemachine damping and geometry.
    /// Motion/attention cues choose lead room, persistent evidence avoids side
    /// flips, and projected extents constrain the requested composition. No
    /// elapsed-time oscillation or positional drift is generated here.
    /// </summary>
    public sealed class ShotCompositionPolicy
    {
        public const float MaximumOffset = 0.22f;
        public const double DirectionSeconds = 1, CueGraceSeconds = 2.5, MaximumEvidenceGap = 1;
        private const float MotionThreshold = 0.025f, EdgeMargin = 0.06f, TopMargin = 0.09f;
        private double lastNow = -1, footageClock, cueSeenAt, pendingSeconds;
        private int side = -1, cuePriority, pendingSide, pendingPriority;
        private long shot;
        private bool initialized, lastUsable;
        public ShotComposition Current { get; private set; } = new ShotComposition(0, 0, 0, 0, true, "Awaiting composition");

        /// <summary>
        /// Update each candidate when it is evaluated. Actual observation cadence
        /// determines elapsed time, so cached standby cameras need no artificial
        /// frame-rate multiplier. Changing enabled/shot does not reset damping.
        /// </summary>
        public ShotComposition Update(double now, double deltaSeconds, long shotId,
            CinematicActivity activity, ShotCompositionObservation observation, bool confined,
            float spaceFraction, int recentCorrections, bool usableFootage = true, bool compositionEnabled = true)
        {
            Validate(now, deltaSeconds, observation, spaceFraction, recentCorrections);
            bool gap = lastNow >= 0 && now - lastNow > MaximumEvidenceGap;
            double step = lastNow >= 0 && !gap && lastUsable && usableFootage && deltaSeconds > 0
                ? Math.Min(0.25, now - lastNow) : 0;
            bool first = !initialized;
            if (gap || !usableFootage || !lastUsable) ClearPending();
            bool shotChanged = first || shot != shotId;
            shot = shotId;
            lastNow = now;
            lastUsable = usableFootage;
            footageClock += step;

            Cue(observation, activity, out int observedSide, out int priority);
            if (first)
            {
                side = observedSide == 0 ? PreferredSide(shotId) : observedSide;
                cuePriority = priority; cueSeenAt = footageClock;
            }
            else
            {
                if (shotChanged)
                {
                    ClearPending();
                    if (observedSide == 0 && cuePriority == 0) side = PreferredSide(shotId);
                }
                if (usableFootage) ObserveSide(observedSide, priority, step);
            }

            float freedom = Smooth01((spaceFraction - 0.2f) / 0.65f);
            float spaceScale = freedom * (confined ? 0.45f : 1);
            float correctionScale = recentCorrections >= 3 ? 0.2f : recentCorrections > 0 ? 0.55f : 1;
            float scale = spaceScale * correctionScale;
            bool centeredStyle = activity == CinematicActivity.Idle && !observation.Grouped &&
                cuePriority <= 1 && (shotId & 3) == 3;
            float placement = activity == CinematicActivity.Travel ? 0.16f :
                activity == CinematicActivity.Combat ? 0.09f : activity == CinematicActivity.Work ? 0.13f : 0.14f;
            if (observation.Grouped) placement *= 0.4f;
            float x = centeredStyle ? 0 : side * placement * scale;
            // With a head/chest aim anchor, modest positive placement reserves
            // lower-frame room for the body while the top constraint keeps headroom.
            float y = (observation.Grouped ? 0.035f : 0.08f) * scale;
            if (Math.Abs(observation.MotionY) > MotionThreshold)
                y -= Clamp(observation.MotionY * 0.75f, -0.04f, 0.04f) * scale;
            if ((activity == CinematicActivity.Work || activity == CinematicActivity.Combat) && observation.InterestConfidence >= 0.55f)
                y -= Clamp(observation.InterestY, -1, 1) * observation.InterestConfidence * 0.05f * scale;
            if (observation.Grouped)
            {
                // Cinemachine frames the shared anchor. Counter some of the
                // primary's displacement so companions do not marginalize them.
                x -= observation.PrimaryOffsetX * 0.65f;
                y -= observation.PrimaryOffsetY * 0.65f;
            }
            if (!compositionEnabled) x = y = 0;

            float minX = Math.Max(-MaximumOffset, -0.5f + EdgeMargin + observation.LeftExtent);
            float maxX = Math.Min(MaximumOffset, 0.5f - EdgeMargin - observation.RightExtent);
            float minY = Math.Max(-MaximumOffset, -0.5f + EdgeMargin + observation.BottomExtent);
            float maxY = Math.Min(MaximumOffset, 0.5f - TopMargin - observation.TopExtent);
            if (observation.Grouped)
            {
                minX = Math.Max(minX, -0.28f - observation.PrimaryOffsetX);
                maxX = Math.Min(maxX, 0.28f - observation.PrimaryOffsetX);
                minY = Math.Max(minY, -0.28f - observation.PrimaryOffsetY);
                maxY = Math.Min(maxY, 0.28f - observation.PrimaryOffsetY);
            }
            bool fits = minX <= maxX && minY <= maxY;
            string reason = !fits ? "Subject envelope needs wider framing" :
                !compositionEnabled ? "Easing to neutral composition" :
                recentCorrections > 0 ? "Restrained placement after camera corrections" :
                confined || freedom < 0.6f ? "Placement restrained by available space" :
                observation.Grouped ? "Group composition preserves the primary player" :
                cuePriority == 3 ? "Room for observed activity" : cuePriority == 2 ? "Room ahead of sustained movement" :
                centeredStyle ? "Centred establishing composition" : cuePriority == 1 ? "Room in the observed facing direction" :
                "Stable thirds and headroom";
            if (!fits)
            {
                // No offset can fit the requested envelope. Request neutral
                // framing (or primary-centred group framing) and explicitly let
                // the adapter choose a wider camera or a coherent subgroup.
                x = observation.Grouped && compositionEnabled ? Clamp(-observation.PrimaryOffsetX, -MaximumOffset, MaximumOffset) : 0;
                y = observation.Grouped && compositionEnabled ? Clamp(-observation.PrimaryOffsetY, -MaximumOffset, MaximumOffset) : 0;
            }
            else
            {
                x = Clamp(x, minX, maxX);
                y = Clamp(y, minY, maxY);
            }
            float nextX = Current.ScreenX, nextY = Current.ScreenY;
            if (first) { nextX = x; nextY = y; }
            else if (step > 0)
            {
                float blend = (float)(1 - Math.Exp(-step / 1.2));
                nextX += (x - nextX) * blend;
                nextY += (y - nextY) * blend;
            }
            // An envelope shrinking around the subject takes precedence over
            // aesthetic easing: never knowingly carry placement beyond the fit.
            if (fits)
            {
                nextX = Clamp(nextX, minX, maxX);
                nextY = Clamp(nextY, minY, maxY);
            }
            else if (observation.LeftExtent + observation.RightExtent >= 0.88f ||
                observation.TopExtent + observation.BottomExtent >= 0.85f)
            { nextX = x; nextY = y; }
            initialized = true;
            Current = new ShotComposition(nextX, nextY, x, y, fits, reason);
            return Current;
        }

        public void Reset()
        {
            lastNow = -1; footageClock = cueSeenAt = 0; shot = 0;
            side = -1; cuePriority = 0; initialized = lastUsable = false;
            ClearPending();
            Current = new ShotComposition(0, 0, 0, 0, true, "Awaiting composition");
        }

        private void ObserveSide(int observed, int priority, double step)
        {
            if (observed == 0)
            {
                ClearPending();
                if (footageClock - cueSeenAt > CueGraceSeconds) cuePriority = 0;
                return; // Stopping preserves the established side of the shot.
            }
            if (priority < cuePriority && footageClock - cueSeenAt <= CueGraceSeconds)
            { ClearPending(); return; }
            if (observed == side)
            {
                cuePriority = priority; cueSeenAt = footageClock; ClearPending(); return;
            }
            if (pendingSide != observed || pendingPriority != priority)
            { pendingSide = observed; pendingPriority = priority; pendingSeconds = 0; }
            pendingSeconds += step;
            if (pendingSeconds >= DirectionSeconds)
            {
                side = observed; cuePriority = priority; cueSeenAt = footageClock;
                ClearPending();
            }
        }

        private static void Cue(ShotCompositionObservation value, CinematicActivity activity, out int side, out int priority)
        {
            side = priority = 0;
            if ((activity == CinematicActivity.Combat || activity == CinematicActivity.Work) &&
                value.InterestConfidence >= 0.55f && Math.Abs(value.InterestX) >= 0.035f)
            { side = value.InterestX > 0 ? -1 : 1; priority = 3; }
            else if (Math.Abs(value.MotionX) >= MotionThreshold)
            { side = value.MotionX > 0 ? -1 : 1; priority = 2; }
            else if (value.FacingConfidence >= 0.55f && Math.Abs(value.FacingX) >= 0.2f)
            { side = value.FacingX > 0 ? -1 : 1; priority = 1; }
        }
        private void ClearPending() { pendingSide = pendingPriority = 0; pendingSeconds = 0; }
        private static int PreferredSide(long shotId) => ((shotId ^ (shotId >> 3)) & 1) == 0 ? -1 : 1;
        private static float Smooth01(float value) { value = Clamp(value, 0, 1); return value * value * (3 - 2 * value); }
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private void Validate(double now, double dt, ShotCompositionObservation value, float space, int corrections)
        {
            if (!Finite(now) || now < 0 || !Finite(dt) || dt < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
            if (!Finite(value.MotionX) || !Finite(value.MotionY) || !Finite(value.FacingX) ||
                !Finite(value.FacingConfidence) || !Finite(value.InterestX) || !Finite(value.InterestY) ||
                !Finite(value.InterestConfidence) || !Finite(value.LeftExtent) || !Finite(value.RightExtent) ||
                !Finite(value.TopExtent) || !Finite(value.BottomExtent) || !Finite(value.PrimaryOffsetX) ||
                !Finite(value.PrimaryOffsetY) || !Finite(space) || space < 0 || space > 1 || corrections < 0 ||
                value.FacingConfidence < 0 || value.FacingConfidence > 1 || value.InterestConfidence < 0 ||
                value.InterestConfidence > 1 || value.LeftExtent < 0 || value.RightExtent < 0 ||
                value.TopExtent < 0 || value.BottomExtent < 0)
                throw new ArgumentException("Composition requires finite normalized evidence and nonnegative subject extents.");
        }
    }
}
