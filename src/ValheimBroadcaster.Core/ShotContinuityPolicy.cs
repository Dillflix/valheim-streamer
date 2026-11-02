using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Persistent screen geography above candidate geometry. The
    /// penalties are preferences among readable views, never eligibility or
    /// permission to keep an obscured view. Presentation commits the history.</summary>
    public sealed class ShotContinuityPolicy
    {
        private double lastNow = -1, pendingAge, lastEvidence = -1, lastPresented = -1, wideAge;
        private AutonomyPosition axis, pendingAxis, shownOffset;
        private int shownSide;
        private bool hasShown, lastWide;
        private ShotRole shownRole;
        private ShotScale shownScale;
        public bool HasAxis { get; private set; }
        public AutonomyPosition Axis => axis;
        public bool NeedsReestablish { get; private set; }
        public string Reason { get; private set; } = "Awaiting an observed action axis";

        public void Update(double now, AutonomyPosition direction, float confidence, bool usable, bool enabled)
        {
            Time(now);
            if (!Finite(confidence) || confidence < 0 || confidence > 1)
                throw new ArgumentOutOfRangeException(nameof(confidence));
            bool gap = lastNow >= 0 && now - lastNow > 1;
            double elapsed = lastNow >= 0 && !gap ? Math.Min(.25, now - lastNow) : 0;
            if (gap) Reset();
            lastNow = now;
            if (!enabled) { pendingAge = 0; return; }
            double length = Math.Sqrt(direction.X * (double)direction.X + direction.Z * (double)direction.Z);
            if (confidence < .6f || length < .05 || !usable)
            {
                pendingAge = 0;
                if (lastEvidence >= 0 && now - lastEvidence > 3)
                { HasAxis = NeedsReestablish = false; shownSide = 0; Reason = "Action-axis evidence expired"; }
                return;
            }
            lastEvidence = now;
            var proposed = new AutonomyPosition((float)(direction.X / length), 0, (float)(direction.Z / length));
            if (!HasAxis)
            {
                axis = pendingAxis = proposed; HasAxis = true; pendingAge = 0;
                shownSide = hasShown ? Side(shownOffset) : 0;
                Reason = "Preserving the observed action axis";
                return;
            }
            double dot = axis.X * proposed.X + axis.Z * proposed.Z;
            // Small turns do not rotate the established line with every body
            // twitch. Large sustained course/interaction changes re-establish it.
            if (dot >= .35) { pendingAge = 0; return; }
            if (pendingAxis.X * proposed.X + pendingAxis.Z * proposed.Z < .85)
            { pendingAxis = proposed; pendingAge = 0; }
            pendingAge += elapsed;
            if (pendingAge < 1.5) return;
            axis = pendingAxis = proposed; pendingAge = 0; shownSide = 0;
            NeedsReestablish = true; wideAge = 0; lastWide = false;
            Reason = "Action relationships changed; prefer a readable wider re-establishment";
        }

        public float Penalty(AutonomyPosition cameraOffset, ShotRole role, ShotScale scale,
            bool readable, bool current = false)
        {
            Validate(role, scale);
            if (!readable) return 0;
            float penalty = NeedsReestablish && scale != ShotScale.Wide ? .09f : 0;
            int side = Side(cameraOffset);
            if (HasAxis && shownSide != 0 && side != 0 && side != shownSide) penalty += .12f;
            if (!current && hasShown)
            {
                double lengths = Length(cameraOffset) * Length(shownOffset);
                double cosine = lengths > .01 ? (cameraOffset.X * shownOffset.X + cameraOffset.Z * shownOffset.Z) / lengths : -1;
                if (cosine > .95 && role == shownRole && scale == shownScale) penalty += .07f;
                if (Math.Abs((int)scale - (int)shownScale) > 1 && role != ShotRole.Establish && !NeedsReestablish)
                    penalty += .05f;
            }
            return Math.Min(.2f, penalty);
        }

        /// <summary>Commit only the camera that actually appeared in readable
        /// live footage. Same-frame calls, holds and rejected candidates earn no
        /// wide-shot completion and cannot overwrite established geography.</summary>
        public void Presented(double now, AutonomyPosition cameraOffset, ShotRole role, ShotScale scale, bool usable)
        {
            if (!Finite(now) || now < 0 || lastPresented >= 0 && now < lastPresented)
                throw new ArgumentOutOfRangeException(nameof(now));
            Validate(role, scale);
            if (now == lastPresented) return;
            bool gap = lastPresented >= 0 && now - lastPresented > 1;
            double elapsed = lastPresented >= 0 && !gap ? Math.Min(.25, now - lastPresented) : 0;
            if (gap) { wideAge = 0; lastWide = false; }
            lastPresented = now;
            if (!usable) { wideAge = 0; lastWide = false; return; }
            bool wide = scale == ShotScale.Wide;
            wideAge = wide && lastWide ? wideAge + elapsed : 0;
            lastWide = wide;
            shownOffset = cameraOffset; shownRole = role; shownScale = scale; hasShown = true;
            int side = Side(cameraOffset);
            if (!NeedsReestablish && side != 0) shownSide = side;
            if (NeedsReestablish && wideAge >= .75)
            {
                NeedsReestablish = false; shownSide = side;
                Reason = "Wider live footage re-established the changed action axis";
            }
        }

        private int Side(AutonomyPosition offset)
        {
            if (!HasAxis) return 0;
            double length = Length(offset);
            double cross = length > .01 ? (axis.X * offset.Z - axis.Z * offset.X) / length : 0;
            return Math.Abs(cross) < .2 ? 0 : cross < 0 ? -1 : 1;
        }
        private static double Length(AutonomyPosition value) => Math.Sqrt(value.X * (double)value.X + value.Z * (double)value.Z);
        private void Time(double now)
        {
            if (!Finite(now) || now < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
        }
        private static void Validate(ShotRole role, ShotScale scale)
        {
            if ((int)role < 0 || (int)role > (int)ShotRole.Landscape || (int)scale < 0 || (int)scale > (int)ShotScale.Medium)
                throw new ArgumentOutOfRangeException(nameof(role));
        }
        public void Reset()
        {
            lastNow = lastEvidence = lastPresented = -1; pendingAge = wideAge = 0;
            axis = pendingAxis = shownOffset = default; shownSide = 0;
            HasAxis = NeedsReestablish = hasShown = lastWide = false;
            Reason = "Awaiting an observed action axis";
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
