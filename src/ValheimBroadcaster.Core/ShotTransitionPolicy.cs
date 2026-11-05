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
    /// adapter must re-evaluate every interpolated frame and may cut to the
    /// validated destination if the actual moving path becomes obstructed.</summary>
    public static class ShotTransitionPolicy
    {
        public const double ReturnSeconds = 0.5;
        // A cut must change shot grammar. Obstructed moves below every one of
        // these thresholds land on a near-identical viewpoint and would read
        // as a stutter, so they wait for in-place repair instead.
        public const float DistinctAngleDegrees = 30;
        public const float DistinctFovDegrees = 12;
        public const float DistinctScreenDisplacement = .25f;

        public static ShotTransitionDecision Decide(bool sameScene, bool sourceReadable, bool destinationReadable,
            bool pathClear, float angleDegrees, float distance, float screenDisplacement, float fovDifference,
            ShotRole fromRole, ShotRole toRole, ShotEnvironment environment, CinematicActivity activity,
            bool recovering = false)
        {
            if (!Finite(angleDegrees) || angleDegrees < 0 || angleDegrees > 180 ||
                !Finite(distance) || distance < 0 || !Finite(screenDisplacement) || screenDisplacement < 0 ||
                !Finite(fovDifference) || fovDifference < 0 ||
                (int)fromRole < 0 || (int)fromRole > (int)ShotRole.Landscape ||
                (int)toRole < 0 || (int)toRole > (int)ShotRole.Landscape ||
                (int)environment < 0 || (int)environment > (int)ShotEnvironment.Dungeon ||
                (int)activity < 0 || (int)activity > (int)CinematicActivity.Work)
                throw new ArgumentOutOfRangeException(nameof(angleDegrees));
            if (!destinationReadable && sourceReadable)
                return new ShotTransitionDecision(ShotTransitionMode.Hold, 0, "Retaining the readable source while seeking a clear destination");
            if (!sourceReadable || recovering)
                return Cut("Visibility recovery uses the validated replacement directly");
            if (!sameScene) return Cut("New scene receives an establishing cut");
            if (!pathClear)
            {
                // Jump-cut guard: an obstructed move toward a visually similar
                // viewpoint must not cut; the adapter repairs the live camera.
                if (angleDegrees < DistinctAngleDegrees && fovDifference < DistinctFovDegrees &&
                    screenDisplacement < DistinctScreenDisplacement)
                    return new ShotTransitionDecision(ShotTransitionMode.Hold, 0,
                        "Obstructed path toward a similar viewpoint waits for in-place repair");
                return Cut("Transition path is obstructed");
            }
            if (angleDegrees > 80 || distance > 18 || screenDisplacement > .45f)
                return Cut("Large viewpoint or screen-direction change");
            if (activity == CinematicActivity.Combat && (angleDegrees > 25 || distance > 4))
                return Cut("Readable action angle without a sweeping transition");
            if (fromRole != toRole && (angleDegrees > 35 || fovDifference > 10))
                return Cut("Purposeful shot or scale change");
            double duration = .45 + Math.Min(.8, distance * .045) + Math.Min(.45, angleDegrees * .0075);
            if (environment == ShotEnvironment.Doorway) duration = Math.Min(.65, duration);
            else if (activity == CinematicActivity.Combat) duration = Math.Min(.7, duration);
            else if (environment == ShotEnvironment.Room || environment == ShotEnvironment.Dungeon)
                duration = Math.Min(1.0, duration);
            return new ShotTransitionDecision(ShotTransitionMode.Blend, duration,
                environment == ShotEnvironment.Doorway ? "Brief validated doorway handoff" : "Continuous adjustment within the same scene");
        }
        /// <summary>Mid-move validation failed. Prefer gliding back to the
        /// validated source over cutting forward; cutting adopts the validated
        /// destination only when no readable return path exists. A physically
        /// unsupported blended pose may not linger through a glide, so the
        /// return happens immediately (zero seconds) instead of over
        /// ReturnSeconds.</summary>
        public static ShotTransitionDecision DecideObstructedBlend(bool sourceReadable, bool blendedSupported)
        {
            if (sourceReadable)
                return new ShotTransitionDecision(ShotTransitionMode.Return, blendedSupported ? ReturnSeconds : 0,
                    "Returning to the validated source after an obstructed move");
            return Cut("No readable return path; adopting the validated destination");
        }
        private static ShotTransitionDecision Cut(string reason) => new ShotTransitionDecision(ShotTransitionMode.Cut, 0, reason);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
