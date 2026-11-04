using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Soft editorial preferences only. Visibility, framing and physical
    /// validation remain independent hard requirements in the camera adapter.</summary>
    public static class EncounterFramingPolicy
    {
        public static bool Meaningful(EncounterKind kind, float confidence) =>
            (int)kind > (int)EncounterKind.None && (int)kind <= (int)EncounterKind.Combat &&
            confidence >= .6f && confidence <= 1;

        public static float ViewPenalty(EncounterKind kind, float confidence, float axisDot,
            bool targetVisible, float targetFrameHeight, int opponents, int visibleOpponents)
        {
            if (!Meaningful(kind, confidence)) return 0;
            if (!Finite(axisDot) || !Finite(targetFrameHeight) || targetFrameHeight < 0 ||
                opponents < 0 || visibleOpponents < 0 || visibleOpponents > opponents)
                throw new ArgumentOutOfRangeException(nameof(axisDot));
            float dot = Clamp(axisDot, -1, 1);
            // Hunters benefit from an oblique rear view along the aiming line;
            // melee/threat relationships are more legible from the side.
            float angle = kind == EncounterKind.Hunting ? Math.Abs(dot + .75f) * .09f : Math.Abs(dot) * .1f;
            float missing = !targetVisible ? .18f : targetFrameHeight < .008f ? .08f : 0;
            if (kind == EncounterKind.Combat && opponents > 0)
                missing = Math.Max(missing, .24f * (1f - (float)visibleOpponents / opponents));
            return Math.Min(.36f, angle + missing) * confidence;
        }

        // Far targets supply an axis and lead room, never a giant compulsory
        // framing envelope. Lens ownership stays with native group framing.
        public static bool IncludeInterestEnvelope(float distance, float x, float y, bool inFront) =>
            Finite(distance) && distance >= 0 && distance <= 12 && Finite(x) && Finite(y) &&
            inFront && Math.Abs(x) <= .36f && Math.Abs(y) <= .32f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }

    /// <summary>A slowly developing encounter axis. Target changes do not flip
    /// the rig, and the original heading is recovered smoothly after evidence ends.</summary>
    public sealed class EncounterAxisPolicy
    {
        private double lastNow = -1;
        private long scene;
        public float Heading { get; private set; }
        public float Update(double now, long sceneId, float baseline, EncounterKind kind,
            float confidence, float directionX, float directionZ)
        {
            if (!Finite(now) || now < 0 || !Finite(baseline) || !Finite(directionX) || !Finite(directionZ))
                throw new ArgumentOutOfRangeException(nameof(now));
            bool reset = lastNow < 0 || now < lastNow || now - lastNow > 1 || scene != sceneId;
            double elapsed = reset ? 0 : Math.Min(.25, now - lastNow);
            if (reset) Heading = baseline;
            lastNow = now; scene = sceneId;
            bool encounter = EncounterFramingPolicy.Meaningful(kind, confidence) &&
                directionX * directionX + directionZ * directionZ > .001f;
            double desired = encounter ? Math.Atan2(directionX, directionZ) * 180 / Math.PI +
                (kind == EncounterKind.Hunting ? 180 : 90) : baseline;
            double delta = (desired - Heading) % 360;
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            double maximum = elapsed * (encounter ? kind == EncounterKind.Combat ? 12 : 8 : 16);
            Heading = (float)((Heading + Math.Max(-maximum, Math.Min(maximum, delta))) % 360);
            return Heading;
        }
        public void Reset() { lastNow = -1; scene = 0; Heading = 0; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
