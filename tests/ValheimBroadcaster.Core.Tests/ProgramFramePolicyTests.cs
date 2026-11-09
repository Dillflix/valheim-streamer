using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ProgramFramePolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Startup observes the last completed live render before the next camera pass", StartupEvidence);
        yield return ("Clean program refuses a previous-frame picture when camera rendering stops", MissingCamera);
        yield return ("Clean program uses only the compositor-selected bounded held picture", Hold);
        yield return ("Clean program never exposes live footage while inactive or travelling", Boundaries);
        yield return ("Clean program readback cannot resurrect a picture after reset or resize", Generation);
        yield return ("Clean program rejects stalled GPU readbacks and clock reversal", ReadbackAge);
        yield return ("Clean recovery slate remains publishable while the scene is unavailable", Slate);
        yield return ("Clean program clock or frame reversal retires pending picture identity", ClockReset);
        yield return ("Clean program requires available texture and fresh render evidence", Availability);
    }

    private static void Check(bool condition) { if (!condition) throw new Exception("Clean program policy assertion failed."); }
    private static ProgramFramePolicy Live(double now = 1, int frame = 10)
    { var value = new ProgramFramePolicy(); value.Rendered(now, frame, FramePresentation.Live); return value; }

    private static void MissingCamera()
    {
        var value = Live();
        Check(value.Select(1, 10, true, false, true).Presentation == FramePresentation.Live);
        Check(value.Select(1.01, 11, true, false, true).Presentation == FramePresentation.Slate);
        Check(value.Select(1.1, 15, true, false, true).Presentation == FramePresentation.Slate);
    }
    private static void StartupEvidence()
    {
        var policy = new ProgramFramePolicy();
        for (int frame = 0; frame < 1800; frame++)
        {
            double now = frame / 60.0;
            Check(policy.HasCompletedLiveRender(now, frame) == (frame > 0));
            Check(policy.Select(now, frame, true, false, true).Presentation == FramePresentation.Slate);
            policy.Rendered(now, frame, FramePresentation.Live);
            Check(policy.Select(now, frame, true, false, true).Presentation == FramePresentation.Live);
        }
        Check(!policy.HasCompletedLiveRender(32, 1800));
        Check(!policy.HasCompletedLiveRender(30, 1810));
        policy.Rendered(32, 1810, FramePresentation.Hold);
        Check(!policy.HasCompletedLiveRender(32, 1811));
        policy.Rendered(33, 1811, FramePresentation.Slate);
        Check(!policy.HasCompletedLiveRender(33, 1812));
        policy.Rendered(34, 1812, FramePresentation.Live);
        policy.Invalidate();
        Check(!policy.HasCompletedLiveRender(34, 1813));
    }

    private static void Hold()
    {
        var value = Live();
        value.Rendered(1.2, 12, FramePresentation.Hold);
        ProgramFrameStamp held = value.Select(1.2, 12, true, false, true);
        Check(held.Presentation == FramePresentation.Hold && value.AcceptReadback(held, 1.25, true, false));
        // Selection does not turn a missing render into an indefinite held frame.
        Check(value.Select(1.21, 13, true, false, true).Presentation == FramePresentation.Slate);
        value.Rendered(1.8, 18, FramePresentation.Slate);
        Check(value.Select(1.8, 18, true, false, true).Presentation == FramePresentation.Slate);
    }
    private static void Boundaries()
    {
        var value = Live();
        ProgramFrameStamp live = value.Select(1, 10, true, false, true);
        Check(value.Select(1, 10, false, false, true).Presentation == FramePresentation.Slate);
        Check(value.Select(1, 10, true, true, true).Presentation == FramePresentation.Slate);
        Check(!value.AcceptReadback(live, 1.1, true, true));
        Check(!value.AcceptReadback(live, 1.1, false, false));
    }
    private static void Generation()
    {
        var value = Live();
        ProgramFrameStamp old = value.Select(1, 10, true, false, true);
        value.Invalidate();
        value.Rendered(1.1, 11, FramePresentation.Live);
        Check(!value.AcceptReadback(old, 1.15, true, false));
        Check(value.AcceptReadback(value.Select(1.1, 11, true, false, true), 1.15, true, false));
        Check(value.Generation != old.Generation);
    }
    private static void ReadbackAge()
    {
        var value = Live();
        ProgramFrameStamp stamp = value.Select(1, 10, true, false, true);
        Check(value.AcceptReadback(stamp, 1.49, true, false));
        Check(!value.AcceptReadback(stamp, 1.51, true, false));
        Check(!value.AcceptReadback(stamp, .99, true, false));
        Check(!value.AcceptReadback(stamp, double.NaN, true, false));
        Check(!value.AcceptReadback(default, 0, true, false));
    }
    private static void Slate()
    {
        var value = new ProgramFramePolicy();
        ProgramFrameStamp stamp = value.Select(2, 20, true, true, false);
        Check(stamp.Presentation == FramePresentation.Slate);
        Check(value.AcceptReadback(stamp, 2.1, true, true));
    }
    private static void ClockReset()
    {
        var value = Live(3, 30);
        ProgramFrameStamp old = value.Select(3, 30, true, false, true);
        value.Rendered(2, 31, FramePresentation.Live);
        Check(value.Generation != old.Generation && !value.AcceptReadback(old, 3.1, true, false));
        long generation = value.Generation;
        value.Rendered(2.1, 1, FramePresentation.Live);
        Check(value.Generation != generation);
    }
    private static void Availability()
    {
        var value = Live();
        Check(value.Select(1, 10, true, false, false).Presentation == FramePresentation.Slate);
        Check(value.Select(1.26, 10, true, false, true).Presentation == FramePresentation.Slate);
        Check(value.Select(.9, 10, true, false, true).Presentation == FramePresentation.Slate);
    }
}
