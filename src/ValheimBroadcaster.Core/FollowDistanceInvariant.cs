using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Outcome of one invariant observation. When engaged, the
    /// displacement is the world-space corrective step the presenter must
    /// apply to the presented camera this frame.</summary>
    public readonly struct FollowInvariantDecision
    {
        public readonly bool Engaged;
        public readonly AutonomyPosition Displacement;
        public readonly string Reason;
        internal FollowInvariantDecision(bool engaged, AutonomyPosition displacement, string reason)
        { Engaged = engaged; Displacement = displacement; Reason = reason; }
    }

    /// <summary>
    /// Last-line distance guarantee for the live follow camera: if the
    /// presented camera has effectively sat still while its subject receded
    /// beyond the shot's nominal distance band, the camera MUST move toward
    /// the subject. No validation failure, hold branch, trajectory state or
    /// recentering interaction may park the presented camera while the actor
    /// walks away; validation may shape the corrective motion (terrain and
    /// in-geometry floors belong to the adapter), never veto it. The class is
    /// pure and explicit-clock: it observes presented camera and subject
    /// positions and returns a bounded corrective displacement.
    /// </summary>
    public sealed class FollowDistanceInvariant
    {
        /// <summary>Distance band: the invariant arms beyond
        /// max(nominal x BandScale, nominal + BandSlackMeters).</summary>
        public const float BandScale = 1.6f;
        public const float BandSlackMeters = 4f;
        /// <summary>Stillness window: engage only when the camera moved less
        /// than StillCameraMeters while the subject moved more than
        /// RecedingSubjectMeters over the last WindowSeconds. An active blend
        /// or tracking solve moves the camera and therefore never trips this;
        /// only a genuinely parked presentation does.</summary>
        public const double WindowSeconds = 1.0;
        public const float StillCameraMeters = 0.5f;
        public const float RecedingSubjectMeters = 3f;
        /// <summary>Pursuit speed limits. The ceiling outruns a sprinting
        /// subject; the floor keeps the correction visibly under way.</summary>
        public const float MaximumPursuitSpeed = 14f;
        public const float MinimumPursuitSpeed = 4f;
        /// <summary>Once engaged, pursuit continues until the camera is back
        /// inside nominal x DisengageScale, giving hysteresis so the handoff
        /// back to the ordinary solver does not flicker.</summary>
        public const float DisengageScale = 1.2f;
        /// <summary>A subject displacement beyond this between observations is
        /// a teleport/binding change; the window restarts rather than chasing
        /// across a discontinuity the editorial layer owns.</summary>
        public const float SubjectDiscontinuityMeters = 20f;

        private readonly struct Sample
        {
            internal readonly double Time;
            internal readonly AutonomyPosition Camera, Subject;
            internal Sample(double time, AutonomyPosition camera, AutonomyPosition subject)
            { Time = time; Camera = camera; Subject = subject; }
        }

        private readonly List<Sample> window = new List<Sample>(64);
        private double lastNow = -1;
        private bool engaged;

        public bool Engaged => engaged;

        /// <summary>Observe the presented pose for this frame and decide. now
        /// is a monotonic clock; dt is the presentation step used to scale the
        /// corrective displacement. A backwards clock or a subject
        /// discontinuity restarts the window (world reload / teleport).</summary>
        public FollowInvariantDecision Observe(double now, AutonomyPosition camera,
            AutonomyPosition subject, float nominalDistance, double dt)
        {
            if (!Finite(now) || now < 0 || !Finite(dt) || dt < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!Finite(nominalDistance) || nominalDistance <= 0 || nominalDistance > 500)
                throw new ArgumentOutOfRangeException(nameof(nominalDistance));
            if (lastNow >= 0 && now < lastNow) Reset();
            if (window.Count > 0 && Distance(subject, window[window.Count - 1].Subject) > SubjectDiscontinuityMeters)
                Reset();
            lastNow = now;
            window.Add(new Sample(now, camera, subject));
            while (window.Count > 2 && now - window[1].Time >= WindowSeconds) window.RemoveAt(0);
            if (window.Count > 256) window.RemoveAt(0);

            float distance = Distance(camera, subject);
            float band = Math.Max(nominalDistance * BandScale, nominalDistance + BandSlackMeters);
            if (!engaged)
            {
                Sample oldest = window[0];
                bool fullWindow = now - oldest.Time >= WindowSeconds * 0.9;
                engaged = fullWindow && distance > band &&
                    Distance(camera, oldest.Camera) < StillCameraMeters &&
                    Distance(subject, oldest.Subject) > RecedingSubjectMeters;
            }
            else if (distance <= nominalDistance * DisengageScale)
                engaged = false;
            if (!engaged || dt <= 0 || distance <= 0.001f)
                return new FollowInvariantDecision(engaged, default,
                    engaged ? "distance invariant engaged" : "within follow band");
            // Step toward the subject along the line of sight, never past the
            // nominal following distance. Faster the further behind it fell.
            float speed = Clamp((distance - nominalDistance) * 1.5f, MinimumPursuitSpeed, MaximumPursuitSpeed);
            float magnitude = Math.Min((float)(speed * dt), Math.Max(0, distance - nominalDistance));
            float scale = magnitude / distance;
            return new FollowInvariantDecision(true, new AutonomyPosition(
                (subject.X - camera.X) * scale, (subject.Y - camera.Y) * scale, (subject.Z - camera.Z) * scale),
                "distance invariant engaged");
        }

        public void Reset() { window.Clear(); engaged = false; lastNow = -1; }

        private static float Distance(AutonomyPosition a, AutonomyPosition b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
        private static float Clamp(float value, float low, float high) =>
            value < low ? low : value > high ? high : value;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
