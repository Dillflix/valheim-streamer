using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ShotScaleTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Shot scales have distinct lenses distances and required envelopes", DistinctScales);
        yield return ("Intentional medium framing may omit legs while full framing may not", IntentionalCrop);
        yield return ("Medium framing never forgives a cropped head or torso", RequiredBody);
        yield return ("Scale framing rejects near-plane crossings and unsafe edges", NearPlaneAndEdges);
        yield return ("A medium shot must be meaningfully larger than a wide shot", ReadableSize);
        yield return ("Medium work framing requires a supported interaction point", SupportedWork);
        yield return ("Motion action uncertainty and doorways retain whole-body framing", RestrictedMedium);
        yield return ("A medium primary cannot award partial companion coverage", CompanionEnvelope);
        yield return ("Scenery and creatures retain their appropriate geometric envelopes", SceneKinds);
        yield return ("Distance calculation respects wide actors and narrow aspect ratios", WidthAndAspect);
        yield return ("Scale quality rewards intended size without rewarding clipping", SizePreference);
        yield return ("Editorial scale credit requires an actually matching projected size", ActualScaleCredit);
        yield return ("Shot scale input validation rejects impossible dimensions", InvalidEvidence);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double a, double b) { Check(Math.Abs(a - b) < .0001, $"Expected {a}, got {b}"); }
    private static ShotSituation Scene(CinematicActivity activity = CinematicActivity.Idle,
        ShotLocomotion movement = ShotLocomotion.Stationary, float confidence = .9f,
        bool interest = false, int opponents = 0, ShotEnvironment environment = ShotEnvironment.Open,
        ShotSceneKind kind = ShotSceneKind.Player) =>
        new ShotSituation(kind, activity, confidence, 0, opponents, interest, environment, movement);

    private static ShotProjection.Envelope Box(double left, double bottom, double right, double top,
        ShotScale scale = ShotScale.Full)
    {
        var points = new ShotProjection.Point[8];
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        for (int x = 0; x < 2; x++)
            points[y * 4 + z * 2 + x] = new ShotProjection.Point(
                ((x == 0 ? left : right) - .5) * 20, ((y == 0 ? bottom : top) - .5) * 20, 10);
        return ShotProjection.MeasureEnvelope(points, 90, 1, .3, ShotScalePolicy.Profile(scale).MinimumReadableHeight);
    }

    private static void DistinctScales()
    {
        var wide = ShotScalePolicy.Profile(ShotScale.Wide);
        var full = ShotScalePolicy.Profile(ShotScale.Full);
        var medium = ShotScalePolicy.Profile(ShotScale.Medium);
        Check(wide.CameraDistance(1.85f, .6f, 16f / 9) > full.CameraDistance(1.85f, .6f, 16f / 9) &&
            full.CameraDistance(1.85f, .6f, 16f / 9) > medium.CameraDistance(1.85f, .6f, 16f / 9), "Scales did not change camera distance.");
        Check(wide.VerticalFov > full.VerticalFov && full.VerticalFov > medium.VerticalFov, "Scales share the same lens.");
        Near(0, wide.LowerBodyFraction); Near(0, full.LowerBodyFraction); Near(.42, medium.LowerBodyFraction);
        Near(.71, medium.AimHeightFraction);
    }

    private static void IntentionalCrop()
    {
        // One physical actor extends below the frame. The medium's required
        // envelope starts at its explicit waist fraction, not a guessed crop.
        const double feet = -.30, head = .90;
        var whole = Box(.30, feet, .70, head);
        var medium = Box(.30, feet + (head - feet) * ShotScalePolicy.Profile(ShotScale.Medium).LowerBodyFraction,
            .70, head, ShotScale.Medium);
        Check(!ShotScalePolicy.EnvelopeReadable(whole, ShotScale.Full), "Accidental cropped feet counted as full coverage.");
        Check(ShotScalePolicy.EnvelopeReadable(medium, ShotScale.Medium), "Explicit waist-up shot rejected its intentional leg crop.");
    }

    private static void RequiredBody()
    {
        Check(!ShotScalePolicy.EnvelopeReadable(Box(.25, .30, .75, 1.10, ShotScale.Medium), ShotScale.Medium), "Cropped head accepted.");
        Check(!ShotScalePolicy.EnvelopeReadable(Box(.25, -.20, .75, .90, ShotScale.Medium), ShotScale.Medium), "Required torso crop accepted.");
        Check(!ShotScalePolicy.EnvelopeReadable(Box(-.10, .25, .50, .90, ShotScale.Medium), ShotScale.Medium), "Cropped shoulders accepted.");
    }

    private static void NearPlaneAndEdges()
    {
        Check(!ShotScalePolicy.EnvelopeReadable(default, ShotScale.Medium), "Absent/near-plane geometry was readable.");
        Check(!ShotScalePolicy.EnvelopeReadable(Box(.25, .25, .75, .99, ShotScale.Medium), ShotScale.Medium), "Missing headroom accepted.");
        Check(!ShotScalePolicy.EnvelopeReadable(Box(.001, .25, .08, .90), ShotScale.Full), "Unsafe edge body accepted.");
    }

    private static void ReadableSize()
    {
        Check(ShotScalePolicy.EnvelopeReadable(Box(.45, .4, .55, .46, ShotScale.Wide), ShotScale.Wide), "Wide readable subject rejected.");
        Check(!ShotScalePolicy.EnvelopeReadable(Box(.45, .4, .55, .46, ShotScale.Medium), ShotScale.Medium), "Tiny waist-up actor called a readable medium.");
        Check(ShotScalePolicy.EnvelopeReadable(Box(.3, .2, .7, .88, ShotScale.Medium), ShotScale.Medium), "Proper medium rejected.");
    }

    private static void SupportedWork()
    {
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(CinematicActivity.Work, interest: true)) == ShotScale.Medium, "Supported work lost medium framing.");
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(CinematicActivity.Work)) == ShotScale.Full, "Work point was invented.");
    }

    private static void RestrictedMedium()
    {
        foreach (ShotLocomotion movement in new[] { ShotLocomotion.Walk, ShotLocomotion.Run, ShotLocomotion.Vertical, ShotLocomotion.Swim, ShotLocomotion.Boat })
            Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(movement: movement)) == ShotScale.Full, "Moving legs omitted.");
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(CinematicActivity.Combat)) == ShotScale.Full, "Combat accepted medium.");
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(confidence: .59f)) == ShotScale.Full, "Uncertain activity accepted medium.");
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(opponents: 1)) == ShotScale.Full, "Nearby relevant opponent lost whole-body framing.");
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(environment: ShotEnvironment.Doorway)) == ShotScale.Full, "Doorway accepted medium.");
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(environment: ShotEnvironment.Room)) == ShotScale.Medium, "Quiet room arbitrarily prohibited medium.");
    }

    private static void CompanionEnvelope()
    {
        Check(ShotScalePolicy.ForSubject(ShotScale.Medium, true) == ShotScale.Medium, "Primary scale lost.");
        Check(ShotScalePolicy.ForSubject(ShotScale.Medium, false) == ShotScale.Full, "Companion received partial-body credit.");
    }

    private static void SceneKinds()
    {
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(kind: ShotSceneKind.Creature)) == ShotScale.Full, "Human waist crop applied to wildlife.");
        Check(ShotScalePolicy.Resolve(ShotScale.Medium, Scene(kind: ShotSceneKind.Scenic)) == ShotScale.Wide, "Landscape received human waist crop.");
        Check(ShotScalePolicy.Resolve(ShotScale.Wide, Scene(kind: ShotSceneKind.Creature)) == ShotScale.Wide, "Creature context cannot be wide.");
    }

    private static void WidthAndAspect()
    {
        var profile = ShotScalePolicy.Profile(ShotScale.Medium);
        Check(profile.CameraDistance(2, 2, 1) > profile.CameraDistance(2, .3f, 1), "Wide body ignored.");
        Check(profile.CameraDistance(2, 1, .6f) > profile.CameraDistance(2, 1, 2), "Portrait/narrow framing ignored.");
    }

    private static void SizePreference()
    {
        var preferred = Box(.3, .15, .7, .83, ShotScale.Medium);
        var tiny = Box(.4, .4, .6, .55, ShotScale.Medium);
        Check(ShotScalePolicy.SizeQuality(preferred, ShotScale.Medium) > ShotScalePolicy.SizeQuality(tiny, ShotScale.Medium), "Medium has no size preference.");
        Check(!ShotScalePolicy.EnvelopeReadable(Box(.3, -.1, .7, .58, ShotScale.Medium), ShotScale.Medium), "Perfect size forgave cropping.");
    }

    private static void InvalidEvidence()
    {
        foreach (float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 33f })
        {
            bool threw = false;
            try { ShotScalePolicy.Profile(ShotScale.Full).CameraDistance(value, .6f, 1); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "Invalid height accepted.");
        }
        bool invalidScale = false;
        try { ShotScalePolicy.Profile((ShotScale)100); } catch (ArgumentOutOfRangeException) { invalidScale = true; }
        Check(invalidScale, "Invalid scale accepted.");
    }

    private static void ActualScaleCredit()
    {
        Check(ShotScalePolicy.MatchesScale(.22f, ShotScale.Wide), "A genuine wide view lost wide credit.");
        Check(!ShotScalePolicy.MatchesScale(.22f, ShotScale.Full), "Requested Full labeled a wide view as full.");
        Check(ShotScalePolicy.MatchesScale(.48f, ShotScale.Full), "A full view lost full credit.");
        Check(!ShotScalePolicy.MatchesScale(.48f, ShotScale.Wide), "Large full actor earned wide credit.");
        Check(ShotScalePolicy.MatchesScale(.68f, ShotScale.Medium), "A genuine medium lost medium credit.");
        Check(!ShotScalePolicy.MatchesScale(.2f, ShotScale.Medium), "Tiny waist-up envelope earned medium credit.");
        Check(!ShotScalePolicy.MatchesScale(.96f, ShotScale.Medium), "Frame-filling medium omitted safety slack.");
        Check(!ShotScalePolicy.MatchesScale(float.NaN, ShotScale.Full) &&
            !ShotScalePolicy.MatchesScale(float.PositiveInfinity, ShotScale.Full), "Invalid size earned credit.");
    }
}
