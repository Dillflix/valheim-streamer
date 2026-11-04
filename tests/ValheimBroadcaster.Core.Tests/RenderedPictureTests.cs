using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class RenderedPictureTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Rendered measurement recognizes near-black and clipped output", Exposure);
        yield return ("Rendered measurement allows textured night scenes and partial bright sky", NightAndSky);
        yield return ("Scenic flat composition is not declared unreadable from absent subject data", NoPrimary);
        yield return ("Rendered subject contrast detects indistinct region without claiming occlusion", LowContrast);
        yield return ("Rendered subject contrast preserves distinctive color and visible detail", Distinctive);
        yield return ("Unverified GPU row orientation cannot penalize a clear mirrored subject", Mirrored);
        yield return ("Invalid tiny or unsupported ROI keeps global measurements only", Regions);
        yield return ("Rendered measurement rejects malformed and unbounded buffers", Buffers);
        yield return ("Rendered pixel penalty requires sustained fresh samples", Persistence);
        yield return ("A transient exposure flash and readable recovery reset pixel concern", Flash);
        yield return ("Missing rendered samples cannot manufacture persistence", SampleGap);
        yield return ("Rendered feedback expires and never transfers to unseen candidate or scene", FreshnessAndIdentity);
        yield return ("Delayed and out-of-order GPU results cannot overwrite current evidence", Delayed);
        yield return ("Pixel-driven correction settles before another shown shot is penalized", Cooldown);
        yield return ("Normal blend suspension discards pixels but preserves a nonrenewing correction cooldown", BlendSuspension);
        yield return ("A new scene clears pixel correction cooldown after suspension", SceneAfterSuspension);
        yield return ("Rendered evidence reset discards old world and correction state", Reset);
        yield return ("Rendered pixel penalty is bounded and remains a soft score", BoundedPenalty);
    }

    private const int W = 48, H = 32;
    private static readonly PictureRegion Roi = new PictureRegion(.35f, .2f, .65f, .6f);
    private static readonly RenderedPictureKey Key = new RenderedPictureKey(1, 1, 2);
    private static void Check(bool v) { if (!v) throw new Exception("Rendered picture assertion failed."); }
    private static byte[] Solid(byte r, byte g, byte b)
    {
        byte[] data = new byte[W * H * 4];
        for (int i = 0; i < data.Length; i += 4)
        { data[i] = r; data[i + 1] = g; data[i + 2] = b; data[i + 3] = 255; }
        return data;
    }
    private static void Fill(byte[] data, PictureRegion region, byte r, byte g, byte b)
    {
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            if ((x + .5f) / W >= region.XMin && (x + .5f) / W <= region.XMax &&
                (y + .5f) / H >= region.YMin && (y + .5f) / H <= region.YMax)
            {
                int i = (y * W + x) * 4; data[i] = r; data[i + 1] = g; data[i + 2] = b;
            }
    }
    private static RenderedPictureMeasurement Measure(byte[] data, bool subject = true) => RenderedPictureQuality.Measure(data, W, H, Roi, subject);
    private static RenderedPictureMeasurement Dark => Measure(Solid(1, 1, 1));
    private static void Feed(RenderedPicturePolicy policy, RenderedPictureKey key, double start, int count, RenderedPictureMeasurement value)
    { for (int i = 0; i < count; i++) Check(policy.Observe(key, start + i * .5, start + i * .5 + .05, value)); }

    private static void Exposure()
    {
        var dark = Dark; Check(dark.Valid && dark.DarkFraction == 1 && dark.Concern > .8f);
        var white = Measure(Solid(255, 255, 255)); Check(white.ClippedFraction == 1 && white.Concern > .8f);
    }
    private static void NightAndSky()
    {
        byte[] night = Solid(20, 24, 30);
        Fill(night, Roi, 40, 48, 64);
        Check(Measure(night).Concern == 0);
        byte[] sky = Solid(64, 80, 60);
        Fill(sky, new PictureRegion(0, .5f, 1, 1), 255, 255, 255);
        var measured = Measure(sky, false);
        Check(measured.ClippedFraction > .45f && measured.ClippedFraction < .55f && measured.Concern == 0);
    }
    private static void NoPrimary()
    {
        var result = Measure(Solid(100, 100, 100), false);
        Check(result.Valid && !result.HasSubjectContrast && result.Concern == 0);
    }
    private static void LowContrast()
    {
        var result = Measure(Solid(100, 100, 100));
        Check(result.HasSubjectContrast && result.Concern > 0 && result.Concern < .5f);
        Check(!result.Reason.Contains("occlud") && !result.Reason.Contains("foliage"));
    }
    private static void Distinctive()
    {
        byte[] colors = Solid(128, 0, 0); Fill(colors, Roi, 0, 38, 0);
        Check(Measure(colors).Concern == 0); // Near-equal luminance still has color separation.
        byte[] detailed = Solid(100, 100, 100);
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int i = (y * W + x) * 4; byte c = (byte)((x + y) % 2 == 0 ? 60 : 140);
            detailed[i] = detailed[i + 1] = detailed[i + 2] = c;
        }
        Check(Measure(detailed).Concern == 0);
    }
    private static void Mirrored()
    {
        var asymmetric = new PictureRegion(.3f, .1f, .7f, .3f);
        byte[] data = Solid(100, 100, 100);
        Fill(data, new PictureRegion(.3f, .7f, .7f, .9f), 200, 180, 80);
        var result = RenderedPictureQuality.Measure(data, W, H, asymmetric, true);
        Check(result.HasSubjectContrast && result.Concern == 0);
    }
    private static void Regions()
    {
        byte[] data = Solid(100, 100, 100);
        var invalid = RenderedPictureQuality.Measure(data, W, H, new PictureRegion(-1, 0, 1, 1), true);
        var tiny = RenderedPictureQuality.Measure(data, W, H, new PictureRegion(.49f, .49f, .5f, .5f), true);
        var noRing = RenderedPictureQuality.Measure(data, W, H, new PictureRegion(0, 0, 1, 1), true);
        Check(invalid.Valid && !invalid.HasSubjectContrast && invalid.Concern == 0);
        Check(!tiny.HasSubjectContrast && !noRing.HasSubjectContrast);
    }
    private static void Buffers()
    {
        Check(!RenderedPictureQuality.Measure(new byte[3], W, H, Roi, true).Valid);
        Check(!RenderedPictureQuality.Measure(new byte[4], 1, 1, Roi, true).Valid);
        Check(!RenderedPictureQuality.Measure(Array.Empty<byte>(), 999999, 999999, Roi, true).Valid);
    }
    private static void Persistence()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 4, Dark);
        Check(policy.Feedback(Key, 1.7).IsFresh && policy.Feedback(Key, 1.7).Penalty == 0);
        Check(policy.Observe(Key, 2, 2.05, Dark)); Check(policy.Feedback(Key, 2.1).Penalty > 0);
    }
    private static void Flash()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 3, Dark);
        Check(policy.Observe(Key, 1.5, 1.55, Measure(Solid(100, 100, 100), false)));
        Feed(policy, Key, 2, 4, Dark); Check(policy.Feedback(Key, 3.6).Penalty == 0);
        Check(policy.Observe(Key, 4, 4.05, Dark)); Check(policy.Feedback(Key, 4.1).Penalty > 0);
        Check(policy.Observe(Key, 4.5, 4.55, Measure(Solid(100, 100, 100), false)));
        Check(policy.Feedback(Key, 4.6).Penalty == 0);
    }
    private static void SampleGap()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 4, Dark);
        Feed(policy, Key, 5, 4, Dark); Check(policy.Feedback(Key, 6.6).Penalty == 0);
    }
    private static void FreshnessAndIdentity()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 5, Dark);
        Check(policy.Feedback(Key, 2.1).Penalty > 0);
        Check(!policy.Feedback(Key, 4).IsFresh && policy.Feedback(Key, 4).Penalty == 0);
        Check(!policy.Feedback(new RenderedPictureKey(1, 1, 3), 2.1).IsFresh);
        Check(!policy.Feedback(new RenderedPictureKey(1, 2, 2), 2.1).IsFresh);
        Check(!policy.Feedback(new RenderedPictureKey(2, 1, 2), 2.1).IsFresh);
        Check(!policy.Feedback(Key, 1).IsFresh);
    }
    private static void Delayed()
    {
        var policy = new RenderedPicturePolicy();
        Check(!policy.Observe(Key, 0, 2, Dark)); Check(!policy.Feedback(Key, 2).IsFresh);
        Check(policy.Observe(Key, 5, 5.1, Dark));
        Check(!policy.Observe(Key, 4.9, 5.1, Dark));
        Check(!policy.Observe(new RenderedPictureKey(3, 1, 1), 4.9, 5.1, Dark));
        Check(!policy.Observe(Key, 6, 5.9, Dark));
        Check(!policy.Observe(default, 6, 6.1, Dark));
    }
    private static void Cooldown()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 5, Dark);
        var next = new RenderedPictureKey(1, 2, 3); Feed(policy, next, 2.5, 5, Dark);
        Check(policy.Feedback(next, 4.6).Penalty == 0);
        Feed(policy, next, 5, 12, Dark); Check(policy.Feedback(next, 10.6).Penalty > 0);
    }
    private static void Reset()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 5, Dark); policy.Reset();
        Check(!policy.Feedback(Key, 2.1).IsFresh);
        Feed(policy, new RenderedPictureKey(2, 0, 0), 0, 5, Dark);
        Check(policy.Feedback(new RenderedPictureKey(2, 0, 0), 2.1).Penalty > 0);
    }
    private static void BlendSuspension()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 5, Dark);
        policy.Suspend(2.1);
        Check(!policy.Feedback(Key, 2.2).IsFresh);
        Check(!policy.Observe(Key, 2, 2.2, Dark)); // A delayed callback cannot repopulate suspended pixels.
        policy.Suspend(2.2); policy.Suspend(2.4); policy.Suspend(2.6);
        var next = new RenderedPictureKey(1, 2, 3);
        Feed(policy, next, 3, 5, Dark);
        Check(policy.Feedback(next, 5.1).IsFresh && policy.Feedback(next, 5.1).Penalty == 0);
        Feed(policy, next, 5.5, 10, Dark);
        Check(policy.Feedback(next, 10.2).Penalty > 0); // Deadline stayed at 10.1, not 10.6.
    }
    private static void SceneAfterSuspension()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 5, Dark);
        policy.Suspend(2.1);
        var next = new RenderedPictureKey(2, 0, 0);
        Feed(policy, next, 3, 5, Dark);
        Check(policy.Feedback(next, 5.1).Penalty > 0);
    }
    private static void BoundedPenalty()
    {
        var policy = new RenderedPicturePolicy(); Feed(policy, Key, 0, 100, Dark);
        var feedback = policy.Feedback(Key, 49.6);
        Check(feedback.Penalty > 0 && feedback.Penalty <= RenderedPicturePolicy.MaximumPenalty);
        Check(feedback.IsFresh && feedback.Measurement.Valid);
    }
}
