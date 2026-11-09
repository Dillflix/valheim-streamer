using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class FrameFitPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A symmetric centred span solves a minimal fov that just fits", SymmetricCentred);
        yield return ("The 0.05 aim offset biases headroom and tightens feet room", OffsetAimPin);
        yield return ("The deep feet span at 75mm distances dominates the lens target", DeepFeetSpan);
        yield return ("A degenerate tiny span returns a tiny fov the caller floors", TinySpan);
        yield return ("The enemy angle overload fits tall-beside-small spans", EnemyAngleOverload);
        yield return ("The metre overload delegates to the angle overload", MetreDelegatesToAngle);
        yield return ("The policy rejects non-finite and out-of-range inputs", ContractValidation);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float expected, float actual, string message, float tol = 1e-2f)
    { if (Math.Abs(expected - actual) > tol) throw new Exception($"{message}: expected {expected}, got {actual}."); }

    // Forward projection: for a solved vFov, the screen fraction of a point sitting
    // angleFromForward off the camera forward axis is tan(angle)/(2*tan(vFov/2)).
    // Both extremes must land inside [-0.5,0.5] (plus epsilon), and dropping the fov
    // by a hair must push one extreme out of frame (minimality).
    private static void AssertFits(float aimScreenY, float angleBelowDeg, float angleAboveDeg, float vFov, string label)
    {
        double half = vFov * 0.5 * Math.PI / 180.0;
        double phi = Math.Atan(2.0 * aimScreenY * Math.Tan(half));
        double feet = phi + angleBelowDeg * Math.PI / 180.0;   // below forward axis
        double head = angleAboveDeg * Math.PI / 180.0 - phi;   // above forward axis
        double feetScreen = Math.Tan(feet) / (2.0 * Math.Tan(half));
        double headScreen = Math.Tan(head) / (2.0 * Math.Tan(half));
        Check(Math.Abs(feetScreen) <= 0.5 + 2e-3, $"{label}: feet out of frame at {feetScreen}.");
        Check(Math.Abs(headScreen) <= 0.5 + 2e-3, $"{label}: head out of frame at {headScreen}.");
    }

    private static void AssertMinimal(float aimScreenY, float angleBelowDeg, float angleAboveDeg, float vFov, string label)
    {
        // The returned fov must satisfy the fit; a narrower fov must fail it. The
        // probe gap (0.3 deg) is wide enough that the binding extreme clears the
        // 2e-3 screen slack, yet small enough to prove the solution sits at the edge.
        AssertFits(aimScreenY, angleBelowDeg, angleAboveDeg, vFov, label);
        float narrower = vFov - 0.3f;
        double half = narrower * 0.5 * Math.PI / 180.0;
        double phi = Math.Atan(2.0 * aimScreenY * Math.Tan(half));
        double feet = phi + angleBelowDeg * Math.PI / 180.0;
        double head = angleAboveDeg * Math.PI / 180.0 - phi;
        double feetScreen = Math.Abs(Math.Tan(feet) / (2.0 * Math.Tan(half)));
        double headScreen = Math.Abs(Math.Tan(head) / (2.0 * Math.Tan(half)));
        Check(feetScreen > 0.5 + 2e-3 || headScreen > 0.5 + 2e-3,
            $"{label}: fov {vFov} was not minimal - a narrower fov still fit ({feetScreen},{headScreen}).");
    }

    private static void SymmetricCentred()
    {
        // Centred aim (screenY 0) and an equal span each way: the half-fov must
        // reach exactly the span angle, i.e. vFov = 2*span.
        float vFov = FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0f, 15f, 15f);
        Near(30f, vFov, "symmetric 15/15 -> 30");
        AssertMinimal(0f, 15f, 15f, vFov, "symmetric");
    }

    private static void OffsetAimPin()
    {
        // With the aim pinned 0.05 below centre the aim sits below the forward axis,
        // so the feet (already below) need MORE bottom room; the solved fov exceeds
        // the centred solve and the feet extreme is the binding one.
        float centred = FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0f, 20f, 20f);
        float offset = FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, 20f, 20f);
        Check(offset > centred, "The below-centre aim pin did not widen the required fov.");
        AssertMinimal(0.05f, 20f, 20f, offset, "offset");
    }

    private static void DeepFeetSpan()
    {
        // The locked player sanity case: h=1.9, aim=1.52, d=6. belowAim=1.77,
        // aboveAim=0.73. angle_below=atan(1.77/6)=16.43 deg already exceeds half of
        // the 75mm 18.18 deg (=9.09), so the floor lands well above the lens target.
        float below = (float)(Math.Atan(1.77 / 6.0) * 180.0 / Math.PI);
        float above = (float)(Math.Atan(0.73 / 6.0) * 180.0 / Math.PI);
        float vFov = FrameFitPolicy.RequiredVerticalFovDegrees(0.05f, 1.77f, 0.73f, 6f);
        Check(vFov > 18.1805f, "The deep feet span did not exceed the 75mm lens target.");
        Check(vFov > 30f, "The feet floor should land in the high-30s for the sanity case.");
        AssertMinimal(0.05f, below, above, vFov, "deep feet");
    }

    private static void TinySpan()
    {
        // A near-zero span returns near the solver floor; the caller maxes it with
        // the lens target, so a tiny value here is correct.
        float vFov = FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0f, 0.01f, 0.01f);
        Check(vFov < 1.5f, "A degenerate span returned a non-tiny fov.");
    }

    private static void EnemyAngleOverload()
    {
        // A tall enemy beside a small player: the enemy's head is far above the aim
        // direction (50 deg) and its base a little below (10 deg). The fit must
        // contain the full span; the head is the binding extreme here.
        float vFov = FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, 10f, 50f);
        AssertMinimal(0.05f, 10f, 50f, vFov, "tall enemy");
        // The head (above) drives it: dropping the above angle shrinks the fov.
        float shorter = FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, 10f, 30f);
        Check(shorter < vFov, "A shorter enemy did not reduce the required fov.");
    }

    private static void MetreDelegatesToAngle()
    {
        // The metre overload must equal the angle overload after atan-ing the spans.
        float below = (float)(Math.Atan(1.5 / 8.0) * 180.0 / Math.PI);
        float above = (float)(Math.Atan(0.9 / 8.0) * 180.0 / Math.PI);
        float viaMetres = FrameFitPolicy.RequiredVerticalFovDegrees(0.05f, 1.5f, 0.9f, 8f);
        float viaAngles = FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, below, above);
        Near(viaAngles, viaMetres, "metre overload delegates to angle overload");
    }

    private static void ContractValidation()
    {
        // Angle overload contracts.
        Throws(() => FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(float.NaN, 10f, 10f));
        Throws(() => FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.5f, 10f, 10f));   // aimScreenY > 0.45
        Throws(() => FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(-0.5f, 10f, 10f));  // aimScreenY < -0.45
        Throws(() => FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, -1f, 10f));  // below < 0
        Throws(() => FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, 90f, 10f));  // below >= 90
        Throws(() => FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, 10f, 90f));  // above >= 90
        Throws(() => FrameFitPolicy.RequiredVerticalFovFromAnglesDegrees(0.05f, float.PositiveInfinity, 10f));
        // Metre overload contracts.
        Throws(() => FrameFitPolicy.RequiredVerticalFovDegrees(0.05f, -1f, 1f, 6f));         // below metres < 0
        Throws(() => FrameFitPolicy.RequiredVerticalFovDegrees(0.05f, 1f, -1f, 6f));         // above metres < 0
        Throws(() => FrameFitPolicy.RequiredVerticalFovDegrees(0.05f, 1f, 1f, 0f));          // distance <= 0
        Throws(() => FrameFitPolicy.RequiredVerticalFovDegrees(0.05f, 1f, 1f, -6f));         // distance < 0
        Throws(() => FrameFitPolicy.RequiredVerticalFovDegrees(0.05f, 1f, 1f, float.NaN));
        Throws(() => FrameFitPolicy.RequiredVerticalFovDegrees(float.NaN, 1f, 1f, 6f));      // aimScreenY non-finite (via overload)
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Expected ArgumentOutOfRangeException.");
    }
}
