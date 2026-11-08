using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Return: the adapter aborts an in-progress move and glides back
    /// to its validated source instead of cutting forward.</summary>
    public enum ShotTransitionMode { Hold, Cut, Blend, Return }
    public readonly struct ShotTransitionDecision
    {
        public readonly ShotTransitionMode Mode;
        public readonly double Seconds;
        public readonly string Reason;
        internal ShotTransitionDecision(ShotTransitionMode mode, double seconds, string reason)
        { Mode = mode; Seconds = seconds; Reason = reason; }
    }

    /// <summary>Editorial transition intent after endpoint evaluation. The
    /// adapter must re-evaluate every interpolated frame; a failed moving path
    /// prefers returning home over cutting forward.</summary>
    public static class ShotTransitionPolicy
    {
        public const double ReturnSeconds = 0.5;
        /// <summary>No-cut mode pending validated single-camera follow: the
        /// only remaining cut is a scene discontinuity (teleport or binding
        /// change), which is physically unbridgeable and keeps its
        /// establishing cut. Severe hard occlusion — the live subject more
        /// than 80 percent blocked by solid geometry, i.e. hard-geometry
        /// visibility below this threshold — now travels to its validated
        /// replacement as a live-validated blend instead of cutting.
        /// Editorial changes (beats, combat angles, large viewpoint moves)
        /// travel as validated blends, obstructed paths hold for in-place
        /// repair, and foreground foliage never motivates a transition.</summary>
        public const float SevereOcclusionVisibility = 0.2f;
        // Visual-distinctness thresholds. No longer cut criteria: the adapter
        // uses them to prefer grammatically distinct replacement candidates.
        public const float DistinctAngleDegrees = 30;
        public const float DistinctFovDegrees = 12;
        public const float DistinctScreenDisplacement = .25f;

        public static ShotTransitionDecision Decide(bool sameScene, bool sourceReadable, float sourceVisibility,
            bool destinationReadable, bool pathClear, float angleDegrees, float distance, float screenDisplacement,
            float fovDifference, ShotRole fromRole, ShotRole toRole, ShotEnvironment environment,
            CinematicActivity activity, bool recovering = false)
        {
            if (!Finite(sourceVisibility) || sourceVisibility < 0 || sourceVisibility > 1 ||
                !Finite(angleDegrees) || angleDegrees < 0 || angleDegrees > 180 ||
                !Finite(distance) || distance < 0 || !Finite(screenDisplacement) || screenDisplacement < 0 ||
                !Finite(fovDifference) || fovDifference < 0 ||
                (int)fromRole < 0 || (int)fromRole > (int)ShotRole.Landscape ||
                (int)toRole < 0 || (int)toRole > (int)ShotRole.Landscape ||
                (int)environment < 0 || (int)environment > (int)ShotEnvironment.Dungeon ||
                (int)activity < 0 || (int)activity > (int)CinematicActivity.Work)
                throw new ArgumentOutOfRangeException(nameof(angleDegrees));
            if (!destinationReadable && sourceReadable)
                return new ShotTransitionDecision(ShotTransitionMode.Hold, 0, "Retaining the readable source while seeking a clear destination");
            if (!sameScene) return Cut("New scene receives an establishing cut");
            if (!sourceReadable || recovering)
            {
                // No-cut mode: even a subject lost behind solid cover travels
                // to its validated replacement. The blend is live-validated
                // every frame and returns home if its path fails mid-move.
                if (sourceVisibility < SevereOcclusionVisibility)
                    return Blend(angleDegrees, distance, environment, activity,
                        "Traveling to a clear view of a subject behind solid cover");
                return Blend(angleDegrees, distance, environment, activity,
                    "Moving to a clear view of a partially covered subject");
            }
            if (!pathClear)
                return new ShotTransitionDecision(ShotTransitionMode.Hold, 0,
                    "Obstructed transition path waits for in-place repair");
            string reason = angleDegrees > 80 || distance > 18 || screenDisplacement > .45f
                    ? "Large viewpoint change carried by a moving blend"
                : activity == CinematicActivity.Combat && (angleDegrees > 25 || distance > 4)
                    ? "Action angle reached with a swift blend"
                : fromRole != toRole && (angleDegrees > 35 || fovDifference > 10)
                    ? "Purposeful shot change carried by a moving blend"
                : environment == ShotEnvironment.Doorway ? "Brief validated doorway handoff"
                : "Continuous adjustment within the same scene";
            return Blend(angleDegrees, distance, environment, activity, reason);
        }
        /// <summary>Mid-move validation failed. No-cut mode pending validated
        /// single-camera follow: the camera always glides back toward the
        /// source — even one momentarily unreadable or behind solid cover —
        /// and lets in-place repair work; the rig's mid-blend fall-through
        /// re-decides every frame toward the best available view. A
        /// physically unsupported blended pose may not linger through a
        /// glide, so the return happens immediately (zero seconds) instead
        /// of over ReturnSeconds.</summary>
        public static ShotTransitionDecision DecideObstructedBlend(bool sourceReadable, float sourceVisibility,
            bool blendedSupported)
        {
            if (!Finite(sourceVisibility) || sourceVisibility < 0 || sourceVisibility > 1)
                throw new ArgumentOutOfRangeException(nameof(sourceVisibility));
            if (sourceVisibility >= SevereOcclusionVisibility)
                return new ShotTransitionDecision(ShotTransitionMode.Return, blendedSupported ? ReturnSeconds : 0,
                    sourceReadable ? "Returning to the validated source after an obstructed move"
                        : "Returning to a partially covered source for in-place repair");
            return new ShotTransitionDecision(ShotTransitionMode.Return, blendedSupported ? ReturnSeconds : 0,
                "Returning toward the covered source; repair re-decides toward the best available view");
        }
        private static ShotTransitionDecision Blend(float angleDegrees, float distance,
            ShotEnvironment environment, CinematicActivity activity, string reason)
        {
            double duration = .45 + Math.Min(.8, distance * .045) + Math.Min(.45, angleDegrees * .0075);
            if (environment == ShotEnvironment.Doorway) duration = Math.Min(.65, duration);
            else if (activity == CinematicActivity.Combat) duration = Math.Min(.7, duration);
            else if (environment == ShotEnvironment.Room || environment == ShotEnvironment.Dungeon)
                duration = Math.Min(1.0, duration);
            return new ShotTransitionDecision(ShotTransitionMode.Blend, duration, reason);
        }
        private static ShotTransitionDecision Cut(string reason) => new ShotTransitionDecision(ShotTransitionMode.Cut, 0, reason);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
