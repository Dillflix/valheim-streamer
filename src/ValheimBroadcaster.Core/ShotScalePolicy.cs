using System;

namespace ValheimBroadcaster.Core
{
    public enum ShotScale { Wide, Full, Medium }

    /// <summary>An explicit subject envelope, not permission to forgive accidental
    /// clipping. Medium preserves the head and torso while allowing the legs below
    /// the waist to leave the frame. Companions retain their own full envelopes.</summary>
    public readonly struct ShotScaleProfile
    {
        public readonly ShotScale Scale;
        public readonly float LowerBodyFraction, TargetHeight, MinimumReadableHeight,
            AimHeightFraction, VerticalFov;
        internal ShotScaleProfile(ShotScale scale, float lower, float target, float minimum, float lens)
        {
            Scale = scale; LowerBodyFraction = lower; TargetHeight = target;
            MinimumReadableHeight = minimum; AimHeightFraction = (1 + lower) * .5f; VerticalFov = lens;
        }

        /// <summary>Distance to the envelope center at the profile's preferred lens.
        /// Account for the actor's depth and width as well as its standing height;
        /// actual pose, lens and clearance must still be evaluated by the adapter.</summary>
        public float CameraDistance(float height, float radius, float aspect)
        {
            if (!Finite(height) || height <= 0 || height > 32 || !Finite(radius) || radius <= 0 || radius > 8 ||
                !Finite(aspect) || aspect <= 0 || aspect > 10) throw new ArgumentOutOfRangeException(nameof(height));
            double tangent = Math.Tan(VerticalFov * Math.PI / 360);
            double vertical = height * (1 - LowerBodyFraction) / (2 * tangent * TargetHeight);
            double horizontal = radius / (tangent * aspect * .78);
            return (float)Math.Max(radius + .45, radius + Math.Max(vertical, horizontal));
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static class ShotScalePolicy
    {
        public static ShotScale ForSubject(ShotScale requested, bool primary)
        {
            Profile(requested);
            return primary ? requested : ShotScale.Full;
        }

        public static ShotScaleProfile Profile(ShotScale scale)
        {
            switch (scale)
            {
                case ShotScale.Wide: return new ShotScaleProfile(scale, 0, .22f, .045f, 65);
                case ShotScale.Full: return new ShotScaleProfile(scale, 0, .48f, .08f, 55);
                case ShotScale.Medium: return new ShotScaleProfile(scale, .42f, .68f, .20f, 45);
                default: throw new ArgumentOutOfRangeException(nameof(scale));
            }
        }

        /// <summary>Only supported, quiet human activity may deliberately omit legs.
        /// Unknown evidence and movement release the request to whole-body coverage.
        /// This is independent of physical readiness and cannot authorize travel.</summary>
        public static ShotScale Resolve(ShotScale requested, ShotSituation scene)
        {
            Profile(requested); // Reject invalid enum values before branching.
            if (scene.Kind == ShotSceneKind.Scenic) return ShotScale.Wide;
            if (requested != ShotScale.Medium) return requested;
            if (scene.Kind != ShotSceneKind.Player || scene.Encounter != EncounterKind.None && scene.EncounterConfidence >= .6f || scene.Confidence < .6f || scene.Opponents != 0 ||
                scene.Locomotion != ShotLocomotion.Stationary || scene.Environment == ShotEnvironment.Doorway ||
                scene.Activity == CinematicActivity.Combat || scene.Activity == CinematicActivity.Travel ||
                scene.Activity == CinematicActivity.Work && !scene.HasInterest)
                return ShotScale.Full;
            return requested;
        }

        /// <summary>Fraction of the profile's target height below which a
        /// visible subject no longer reads as the intended shot. This is the
        /// recovery backstop for a subject receding in depth: physical pursuit
        /// is the primary mechanism, but when the camera is blocked the
        /// shrinking subject must eventually count as unreadable so the
        /// occlusion-gated recovery pipeline can fire.</summary>
        public const float MinimumTargetHeightFraction = .35f;

        public static bool EnvelopeReadable(ShotProjection.Envelope envelope, ShotScale scale)
        {
            ShotScaleProfile profile = Profile(scale);
            return envelope.InFront && envelope.VisibleFraction >= .98 && envelope.SafeFraction >= .85 &&
                envelope.Headroom >= .025 && envelope.RelativeHeight >= profile.MinimumReadableHeight * .8 &&
                envelope.RelativeHeight >= profile.TargetHeight * MinimumTargetHeightFraction;
        }

        /// <summary>Editorial credit uses the actual projected required envelope,
        /// not the requested camera label. These bands are not physical validity
        /// gates: a readable fallback can stay live without earning a close beat.</summary>
        public static bool MatchesScale(float relativeHeight, ShotScale scale)
        {
            Profile(scale);
            if (float.IsNaN(relativeHeight) || float.IsInfinity(relativeHeight)) return false;
            return scale == ShotScale.Wide ? relativeHeight >= .06f && relativeHeight <= .34f :
                scale == ShotScale.Full ? relativeHeight >= .30f && relativeHeight <= .82f :
                relativeHeight >= .44f && relativeHeight <= .95f;
        }

        /// <summary>Preference for a deliberate projected size; never overrides
        /// visibility. A large cropped envelope cannot masquerade as a closer shot.</summary>
        public static float SizeQuality(ShotProjection.Envelope envelope, ShotScale scale)
        {
            ShotScaleProfile profile = Profile(scale);
            if (!envelope.InFront || envelope.RelativeHeight <= 0) return 0;
            double ratio = envelope.RelativeHeight / profile.TargetHeight;
            return (float)Math.Max(0, Math.Min(1, Math.Exp(-Math.Abs(Math.Log(ratio)) * .65)));
        }
    }
}
