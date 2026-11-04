using System;

namespace ValheimBroadcaster.Core
{
    public enum ShotTransitionMode { Hold, Cut, Blend }
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
            if (!pathClear) return Cut("Transition path is obstructed");
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
        private static ShotTransitionDecision Cut(string reason) => new ShotTransitionDecision(ShotTransitionMode.Cut, 0, reason);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
