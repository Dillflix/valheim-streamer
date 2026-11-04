using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Identity of the picture actually displayed. A sample from another
    /// scene, shot or candidate cannot say anything about an unrendered view.</summary>
    public readonly struct RenderedPictureKey : IEquatable<RenderedPictureKey>
    {
        public readonly long SceneEpoch, ShotId;
        public readonly int Candidate;
        public RenderedPictureKey(long sceneEpoch, long shotId, int candidate)
        { SceneEpoch = sceneEpoch; ShotId = shotId; Candidate = candidate; }
        public bool IsValid => SceneEpoch > 0 && ShotId >= 0 && Candidate >= 0;
        public bool Equals(RenderedPictureKey other) => SceneEpoch == other.SceneEpoch && ShotId == other.ShotId && Candidate == other.Candidate;
        public override bool Equals(object? obj) => obj is RenderedPictureKey other && Equals(other);
        public override int GetHashCode() => unchecked((SceneEpoch.GetHashCode() * 397 ^ ShotId.GetHashCode()) * 397 ^ Candidate);
    }

    /// <summary>Normalized viewport rectangle, bottom-left origin. It describes
    /// a projected subject envelope, not segmentation of its rendered pixels.</summary>
    public readonly struct PictureRegion
    {
        public readonly float XMin, YMin, XMax, YMax;
        public PictureRegion(float xMin, float yMin, float xMax, float yMax)
        { XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax; }
        public bool IsValid => Unit(XMin) && Unit(YMin) && Unit(XMax) && Unit(YMax) && XMax > XMin && YMax > YMin;
        internal PictureRegion MirrorY => new PictureRegion(XMin, 1 - YMax, XMax, 1 - YMin);
        private static bool Unit(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0 && v <= 1;
    }

    public readonly struct RenderedPictureMeasurement
    {
        public readonly bool Valid, HasSubjectContrast;
        public readonly float MeanLuma, LumaDeviation, DarkFraction, ClippedFraction, SubjectSeparation, SubjectDetail, Concern;
        public readonly string Reason;
        internal RenderedPictureMeasurement(float mean, float deviation, float dark, float clipped,
            bool subject, float separation, float detail, float concern, string reason)
        {
            Valid = true; MeanLuma = mean; LumaDeviation = deviation; DarkFraction = dark; ClippedFraction = clipped;
            HasSubjectContrast = subject; SubjectSeparation = separation; SubjectDetail = detail; Concern = concern; Reason = reason;
        }
    }

    /// <summary>Small RGBA image measurements supplement geometric visibility.
    /// Darkness/contrast are clues, never proof of foliage or missing terrain.</summary>
    public static class RenderedPictureQuality
    {
        public static RenderedPictureMeasurement Measure(byte[] rgba, int width, int height,
            PictureRegion primary, bool hasPrimary, ProgramRowOrder rowOrder = ProgramRowOrder.Unknown)
        {
            if (rgba == null || width < 8 || height < 8 || width > 512 || height > 512 ||
                rgba.Length != width * height * 4) return default;
            var whole = new Statistics();
            int dark = 0, clipped = 0;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                double r = rgba[i] / 255.0, g = rgba[i + 1] / 255.0, b = rgba[i + 2] / 255.0;
                double y = whole.Add(r, g, b);
                if (y < .065) dark++;
                if (r > .98 && g > .98 && b > .98) clipped++;
            }
            float darkFraction = (float)dark / whole.Count, clipFraction = (float)clipped / whole.Count;
            float separation = 0, detail = 0;
            if (rowOrder == ProgramRowOrder.TopFirst) primary = primary.MirrorY;
            bool subject = hasPrimary && primary.IsValid && Contrast(rgba, width, height, primary, out separation, out detail);
            if (subject && rowOrder == ProgramRowOrder.Unknown)
            {
                // Async GPU row order is backend/path dependent. No platform-wide
                // flip is assumed. Accept the more readable of the projected ROI
                // and its vertical mirror, so an unverified row origin cannot
                // manufacture a subject-contrast penalty. Global measures are
                // orientation invariant. This deliberately sacrifices sensitivity.
                bool mirror = Contrast(rgba, width, height, primary.MirrorY, out float mirrorSeparation, out float mirrorDetail);
                if (!mirror) subject = false;
                else
                { separation = Math.Max(separation, mirrorSeparation); detail = Math.Max(detail, mirrorDetail); }
            }
            float concern = 0;
            string reason = "No persistent exposure concern measured";
            // Ordinary night scenes, silhouettes, fog and bright sky are valid.
            // Only overwhelming loss of the image receives a strong soft penalty.
            if (whole.MeanLuma < .035 && darkFraction > .94f)
            { concern = .85f; reason = "Rendered picture is overwhelmingly dark"; }
            else if (whole.MeanLuma > .965 && clipFraction > .90f)
            { concern = .85f; reason = "Rendered picture is overwhelmingly clipped"; }
            else if (subject && separation < .025f && detail < .025f)
            { concern = .4f; reason = "Projected subject region has little measured detail or background separation"; }
            return new RenderedPictureMeasurement((float)whole.MeanLuma, (float)whole.LumaDeviation,
                darkFraction, clipFraction, subject, separation, detail, concern, reason);
        }

        private static bool Contrast(byte[] rgba, int width, int height, PictureRegion roi, out float separation, out float detail)
        {
            separation = detail = 0;
            var inner = new Statistics(); var ring = new Statistics();
            float padX = Math.Max(.04f, (roi.XMax - roi.XMin) * .6f), padY = Math.Max(.04f, (roi.YMax - roi.YMin) * .3f);
            for (int y = 0; y < height; y++)
            {
                float v = (y + .5f) / height;
                if (v < roi.YMin - padY || v > roi.YMax + padY) continue;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width;
                    if (u < roi.XMin - padX || u > roi.XMax + padX) continue;
                    int i = (y * width + x) * 4;
                    double r = rgba[i] / 255.0, g = rgba[i + 1] / 255.0, b = rgba[i + 2] / 255.0;
                    if (u >= roi.XMin && u <= roi.XMax && v >= roi.YMin && v <= roi.YMax) inner.Add(r, g, b);
                    else ring.Add(r, g, b);
                }
            }
            if (inner.Count < 12 || ring.Count < 12) return false;
            double dr = inner.MeanR - ring.MeanR, dg = inner.MeanG - ring.MeanG, db = inner.MeanB - ring.MeanB;
            separation = (float)Math.Sqrt((dr * dr + dg * dg + db * db) / 3);
            detail = (float)inner.ColorDeviation;
            return true;
        }

        private struct Statistics
        {
            internal int Count;
            private double r, g, b, rr, gg, bb, luma, lumaSquared;
            internal double MeanR => r / Count;
            internal double MeanG => g / Count;
            internal double MeanB => b / Count;
            internal double MeanLuma => luma / Count;
            internal double LumaDeviation => Math.Sqrt(Math.Max(0, lumaSquared / Count - MeanLuma * MeanLuma));
            internal double ColorDeviation => Math.Sqrt(Math.Max(0, (rr / Count - MeanR * MeanR +
                gg / Count - MeanG * MeanG + bb / Count - MeanB * MeanB) / 3));
            internal double Add(double red, double green, double blue)
            {
                double y = .2126 * red + .7152 * green + .0722 * blue;
                Count++; r += red; g += green; b += blue; rr += red * red; gg += green * green; bb += blue * blue;
                luma += y; lumaSquared += y * y; return y;
            }
        }
    }

    public readonly struct RenderedPictureFeedback
    {
        public readonly bool IsFresh, ShouldReconsider;
        public readonly float Penalty;
        public readonly RenderedPictureMeasurement Measurement;
        public readonly string Reason;
        internal RenderedPictureFeedback(bool fresh, float penalty, RenderedPictureMeasurement measurement, string reason, bool reconsider = false)
        { IsFresh = fresh; Penalty = penalty; Measurement = measurement; Reason = reason; ShouldReconsider = reconsider; }
        public static RenderedPictureFeedback Unavailable(string reason) => new RenderedPictureFeedback(false, 0, default, reason);
    }

    /// <summary>Fresh, persistent evidence can modestly reduce the shown shot's
    /// editorial score. It never changes physical eligibility or readable masks.</summary>
    public sealed class RenderedPicturePolicy
    {
        public const double PersistenceSeconds = 2, FreshSeconds = 1.5, MaximumSampleGap = 1, CorrectionCooldownSeconds = 8;
        public const float MaximumPenalty = .22f;
        private RenderedPictureKey key;
        private RenderedPictureMeasurement measurement;
        private double sampledAt = -1, concernSince = -1, invalidBefore = -1;
        private double cooldownUntil;
        private int concerns;

        public bool Observe(RenderedPictureKey identity, double capturedAt, double receivedAt, RenderedPictureMeasurement sample)
        {
            if (!identity.IsValid || !sample.Valid || !Time(capturedAt) || !Time(receivedAt) ||
                receivedAt < capturedAt || receivedAt - capturedAt > FreshSeconds) return false;
            if (capturedAt <= invalidBefore || sampledAt >= 0 && capturedAt <= sampledAt) return false;
            if (!key.Equals(identity))
            {
                // A move away from a penalized view must settle before pixels
                // can encourage another correction. This bounds night/sky
                // cycling even when every available view has similar exposure.
                if (key.IsValid && key.SceneEpoch != identity.SceneEpoch) cooldownUntil = 0;
                else BeginSettling(receivedAt);
                ResetEvidence();
            }
            if (sampledAt < 0 || capturedAt - sampledAt > MaximumSampleGap) { concernSince = -1; concerns = 0; }
            key = identity; sampledAt = capturedAt; measurement = sample;
            if (sample.Concern > 0)
            {
                if (concernSince < 0) concernSince = capturedAt;
                concerns++;
            }
            else { concernSince = -1; concerns = 0; }
            return true;
        }

        public RenderedPictureFeedback Feedback(RenderedPictureKey identity, double now)
        {
            if (!Time(now) || sampledAt < 0 || now < sampledAt || now - sampledAt > FreshSeconds || !key.Equals(identity))
                return RenderedPictureFeedback.Unavailable("No fresh rendered-picture sample for this shot");
            bool persistent = Persistent;
            float penalty = persistent && now >= cooldownUntil ? Math.Min(MaximumPenalty, MaximumPenalty * measurement.Concern) : 0;
            return new RenderedPictureFeedback(true, penalty, measurement,
                now < cooldownUntil ? "Rendered-picture correction settling" : persistent ? measurement.Reason :
                measurement.Concern > 0 ? "Waiting for persistent rendered-picture evidence" : measurement.Reason,
                persistent && now >= cooldownUntil && measurement.Concern > 0);
        }

        /// <summary>Consume only after an actually accepted editorial replacement.
        /// This evidence authorizes one reconsideration, never a physical failure.</summary>
        public bool MarkReconsidered(RenderedPictureKey identity, double now)
        {
            if (!Feedback(identity, now).ShouldReconsider) return false;
            cooldownUntil = now + CorrectionCooldownSeconds;
            invalidBefore = Math.Max(invalidBefore, now);
            ResetEvidence();
            return true;
        }

        /// <summary>A normal blend/held frame invalidates pixel evidence at once,
        /// but must not erase the settling period after leaving a poor picture.
        /// Repeated suspended frames never move the deadline forward.</summary>
        public void Suspend(double now)
        {
            if (!Time(now)) { Reset(); return; }
            BeginSettling(now);
            invalidBefore = Math.Max(invalidBefore, now);
            ResetEvidence();
        }

        private void BeginSettling(double now)
        {
            if (Persistent && now >= cooldownUntil && now >= sampledAt && now - sampledAt <= FreshSeconds)
                cooldownUntil = now + CorrectionCooldownSeconds;
        }
        private bool Persistent => concernSince >= 0 && sampledAt - concernSince >= PersistenceSeconds && concerns >= 4;
        // Retain identity only to distinguish same-scene resumption from a new
        // scene; sampledAt=-1 unconditionally prevents old feedback/readability.
        private void ResetEvidence() { measurement = default; sampledAt = concernSince = -1; concerns = 0; }
        public void Reset() { ResetEvidence(); key = default; cooldownUntil = 0; invalidBefore = -1; }
        private static bool Time(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0;
    }
}
