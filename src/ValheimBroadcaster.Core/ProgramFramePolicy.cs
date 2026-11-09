using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Identity of the clean picture copied for one asynchronous readback.</summary>
    public readonly struct ProgramFrameStamp
    {
        public readonly long Generation;
        public readonly int RenderedFrame;
        public readonly double CopiedAt;
        public readonly FramePresentation Presentation;

        internal ProgramFrameStamp(long generation, int renderedFrame, double copiedAt, FramePresentation presentation)
        { Generation = generation; RenderedFrame = renderedFrame; CopiedAt = copiedAt; Presentation = presentation; }
    }

    /// <summary>Only the current compositor output may enter the program feed.
    /// A missing camera produces a slate, never a desktop/GUI capture or an
    /// unbounded repeat of the last world picture.</summary>
    public sealed class ProgramFramePolicy
    {
        public const double MaximumRenderAge = .25, MaximumReadbackAge = .5;
        private int renderedFrame = -1;
        private double renderedAt = -1;
        private FramePresentation presentation = FramePresentation.Slate;
        public long Generation { get; private set; } = 1;

        public void Rendered(double now, int frame, FramePresentation mode)
        {
            Validate(now, frame);
            if ((int)mode < 0 || (int)mode > (int)FramePresentation.Slate) throw new ArgumentOutOfRangeException(nameof(mode));
            if (renderedAt >= 0 && (now < renderedAt || frame < renderedFrame)) Invalidate();
            renderedAt = now; renderedFrame = frame; presentation = mode;
        }

        // Update can run before this frame's compositor. Health observes the last
        // completed render; texture copies still require an exact frame in Select.
        public bool HasCompletedLiveRender(double now, int frame)
        {
            Validate(now, frame);
            return presentation == FramePresentation.Live && renderedFrame >= 0 &&
                renderedFrame <= frame && renderedFrame >= frame - 1 &&
                now >= renderedAt && now - renderedAt <= MaximumRenderAge;
        }

        public ProgramFrameStamp Select(double now, int frame, bool active, bool hardFailure, bool textureAvailable)
        {
            Validate(now, frame);
            bool current = active && !hardFailure && textureAvailable && frame == renderedFrame &&
                now >= renderedAt && now - renderedAt <= MaximumRenderAge;
            return new ProgramFrameStamp(Generation, current ? renderedFrame : -1, now,
                current ? presentation : FramePresentation.Slate);
        }

        public bool AcceptReadback(ProgramFrameStamp stamp, double now, bool active, bool hardFailure) =>
            active && Finite(now) && now >= stamp.CopiedAt && now - stamp.CopiedAt <= MaximumReadbackAge &&
            stamp.Generation == Generation && (!hardFailure || stamp.Presentation == FramePresentation.Slate);

        public void Invalidate()
        {
            Generation++; renderedFrame = -1; renderedAt = -1; presentation = FramePresentation.Slate;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void Validate(double now, int frame)
        { if (!Finite(now) || now < 0 || frame < 0) throw new ArgumentOutOfRangeException(nameof(now)); }
    }
}
