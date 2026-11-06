using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Splines;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class Program
{
    private static int Main()
    {
        int passed = 0, total = 0;
        foreach (var test in Cases())
        {
            total++;
            try { test.Run(); passed++; System.Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { System.Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        System.Console.WriteLine($"{passed}/{total} native camera math cases passed. No Unity scene, camera-component lifecycle, physics or graphics backend is executed.");
        return passed == total ? 0 : 1;
    }

    private static IEnumerable<(string Name, Action Run)> Cases()
    {
        foreach (var test in JourneyTerrainProfileTests.Cases()) yield return test;
        foreach (var test in FoliageTriangleGeometryTests.Cases()) yield return test;
        foreach (var test in NativeJourneyDistanceTests.Cases()) yield return test;
        foreach (var test in NativeJourneySamplingTests.Cases()) yield return test;
        foreach (var test in NativeJourneyGazeTests.Cases()) yield return test;
        foreach (var test in NativeNavigationTests.Cases()) yield return test;
        foreach (var test in NativeScenicPacingTests.Cases()) yield return test;
        foreach (var test in ShotRepairMathTests.Cases()) yield return test;
        foreach (ScenicShotStyle style in Enum.GetValues(typeof(ScenicShotStyle)))
        {
            ScenicShotStyle captured = style;
            yield return ($"{style} path matches native distance evaluation and authored endpoints", () => NativeDistance(captured));
        }
        yield return ("Spline sample and future preview never advance the path", PreviewDoesNotAdvance);
        yield return ("Live motion-strength edits preserve the prepared path shape", ImmutableShape);
        yield return ("A new scenic plan replaces all prior path geometry", NewPlan);
        yield return ("Zero-length paths retain a safe stationary fallback", ZeroLength);
        yield return ("Distance progress is bounded, monotonic and starts/ends at rest", RestToRest);
        yield return ("Long scenic splines establish movement promptly and cruise without a midpoint surge", ScenicCruise);
        yield return ("Large world coordinates retain finite bounded spline positions", WorldBounds);
        yield return ("Malformed spline progress and shape strength are rejected", InvalidInputs);
        yield return ("Native FreeLook lens interpolation preserves camera mode and clip planes", LensModifierContract);
        yield return ("Native FreeLook lens endpoints and center retain deliberate scale", LensModifierEndpoints);
        yield return ("Fresh camera solves do not accumulate FreeLook lens adjustment", LensModifierNoAccumulation);
        yield return ("Observational profiles produce nested native dead and hard guides", ObservationalRectangles);
        yield return ("Observational placement converts vertical guide coordinates once", ObservationalVerticalPlacement);
        yield return ("FreeLook composition preserves native image limits through interpolation", ProfileLimits);
        yield return ("FreeLook composition respects saturated native limit offsets", SaturatedProfileLimits);
        yield return ("FreeLook composition uses effective disabled dead-zone size", DisabledDeadZoneLimits);
        foreach (ScenicShotRole role in Enum.GetValues(typeof(ScenicShotRole)))
        {
            ScenicShotRole captured = role;
            yield return ($"Directed {role} route matches native distance evaluation and fixed endpoints", () => DirectedNativeDistance(captured));
        }
        yield return ("Directed route shape is independent of decorative strength", DirectedStrength);
        yield return ("A trimmed directed route builds the intended prefix endpoints", DirectedPrefix);
        yield return ("Directed camera routes begin developing within their own shot duration", DirectedEarlyMovement);
        yield return ("Directed dolly-off samples follow the authored route at every tested point", DirectedAuthoredPath);
        yield return ("Trimming an authored scenic route preserves dolly-off and fixed lens", DirectedAuthoredPrefix);
        yield return ("Terrain-fitted scenic routes retain native distance parity without moving their subjects", RaisedNativeRoutes);
        yield return ("Interior scenic slices rebuild native distance paths with selected authored endpoints", DirectedInteriorNativeSlice);
        yield return ("Interior authored scenic slices retain body ownership and fixed composition", DirectedInteriorAuthoredSlice);
        yield return ("Measured native scenic sections preserve validated body endpoints and original endpoint aims", MeasuredNativeSection);
        yield return ("Measured scenic section proposals preserve authored ownership and reject invalid ranges", MeasuredSectionValidation);
        foreach (var test in FoliageGeometryTests.Cases()) yield return test;
        foreach (var test in FoliageRetargetTests.Cases()) yield return test;
        foreach (var test in ManualCameraClockTests.Cases()) yield return test;
        foreach (var test in SpatialTrajectoryMathTests.Cases()) yield return test;
    }

    private static ScenicShotPlan Plan(ScenicShotStyle style = ScenicShotStyle.Arc, long id = 4,
        float x = 100, float y = 30, float z = -40) => new ScenicShotPlan(id, style,
            P(x, y, z), P(x + 18, y + 2, z + 7), P(12, 8, -18), .12f, .08f);
    private static AutonomyPosition P(float x = 0, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(AutonomyPosition a, AutonomyPosition b, double tolerance = .003) =>
        Check(a.Distance(b) <= tolerance, "Positions disagree by " + a.Distance(b));
    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    private static void NativeDistance(ScenicShotStyle style)
    {
        var plan = Plan(style); using var path = new ScenicSplinePath();
        Check(path.Prepare(plan, 1.2f) && path.Ready && path.Length > 0, "Path was not prepared.");
        Near(path.Sample(0).CameraPosition, plan.Sample(0, 1.2f).CameraPosition);
        Near(path.Sample(1).CameraPosition, plan.Sample(1, 1.2f).CameraPosition);
        for (int i = 0; i <= 100; i++)
        {
            float progress = i / 100f;
            // This is the pinned native Dolly distance-to-normalized conversion
            // and upstream spline evaluator, not a synthetic engine replacement.
            float normalized = path.Spline!.ConvertIndexUnit(path.Distance(progress), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            Vector3 native = path.Spline.EvaluatePosition(normalized);
            ScenicShotSample sample = path.Sample(progress);
            Near(sample.CameraPosition, P(native.x, native.y, native.z));
            Near(sample.AimPosition, plan.Sample(progress, 1.2f).AimPosition);
            Check(sample.ScreenX == .12f && sample.ScreenY == .08f, "Native motion replaced authored composition.");
        }
    }
    private static void PreviewDoesNotAdvance()
    {
        using var path = new ScenicSplinePath(); path.Prepare(Plan(), 1);
        var before = path.Sample(.35f);
        for (int i = 0; i <= 100; i++) path.Sample(i / 100f);
        Near(before.CameraPosition, path.Sample(.35f).CameraPosition, 0);
        Near(path.Sample(-1).CameraPosition, path.Sample(0).CameraPosition, 0);
        Near(path.Sample(4).CameraPosition, path.Sample(1).CameraPosition, 0);
    }
    private static void ImmutableShape()
    {
        using var path = new ScenicSplinePath(); var plan = Plan(); path.Prepare(plan, .8f);
        var spline = path.Spline; var sample = path.Sample(.6f); float length = path.Length;
        path.Prepare(plan, 1.5f);
        Check(ReferenceEquals(spline, path.Spline) && path.Length == length, "Live strength edit rebuilt the displayed path.");
        Near(sample.CameraPosition, path.Sample(.6f).CameraPosition, 0);
    }
    private static void NewPlan()
    {
        using var path = new ScenicSplinePath(); path.Prepare(Plan(), 1); var old = path.Spline;
        var next = Plan(ScenicShotStyle.Lift, 7, 900, 70, 300); path.Prepare(next, .5f);
        Check(!ReferenceEquals(old, path.Spline), "New plan kept old spline.");
        Near(path.Sample(0).CameraPosition, next.Sample(0, .5f).CameraPosition);
        path.Reset(); Check(!path.Ready && path.Length == 0 && path.Spline == null, "Reset retained path geometry.");
    }
    private static void ZeroLength()
    {
        using var path = new ScenicSplinePath();
        var stationary = new ScenicShotPlan(10, ScenicShotStyle.Lateral, P(), P(), P(8, 4, 12));
        Check(!path.Prepare(stationary, 1) && !path.Ready, "Degenerate path manufactured travel.");
        Check(path.Prepare(Plan(), 1), "A degenerate path poisoned the next plan.");
    }
    private static void RestToRest()
    {
        using var path = new ScenicSplinePath(); path.Prepare(Plan(), 1);
        float last = -1;
        for (int i = 0; i <= 1000; i++)
        {
            float distance = path.Distance(i / 1000f);
            Check(distance >= last && distance <= path.Length + .0001f, "Distance reversed or exceeded the path."); last = distance;
        }
        Check(path.Distance(.01f) < path.Distance(.51f) - path.Distance(.5f), "Start does not ease from rest.");
        Check(path.Length - path.Distance(.99f) < path.Distance(.51f) - path.Distance(.5f), "End does not settle.");
        Check(path.Distance(-4) == 0 && path.Distance(4) == path.Length, "Distance did not clamp to endpoints.");
    }
    private static void ScenicCruise()
    {
        using var path = new ScenicSplinePath(); path.Prepare(Plan(ScenicShotStyle.Lateral), 1);
        Check(path.Distance(3f / 45) > path.Length * .025f, "Long path still spends its opening nearly motionless.");
        float cruise = path.Distance(.3f) - path.Distance(.2f);
        foreach (float p in new[] { .3f, .4f, .5f, .6f, .7f, .8f })
            Check(Math.Abs((path.Distance(p + .1f) - path.Distance(p)) - cruise) < .0001f,
                "Native distance pacing surged or stalled during the cruise.");
    }
    private static void WorldBounds()
    {
        using var path = new ScenicSplinePath(); path.Prepare(Plan(ScenicShotStyle.Reveal, 8, 19000, 100, -19000), 1.5f);
        for (int i = 0; i <= 100; i++)
        {
            var p = path.Sample(i / 100f).CameraPosition;
            Check(Finite(p.X) && Finite(p.Y) && Finite(p.Z) && p.Distance(P(19000, 100, -19000)) < 100,
                "Spline escaped its scenic region or became non-finite.");
        }
    }
    private static void InvalidInputs()
    {
        using var path = new ScenicSplinePath();
        Throws(() => path.Sample(0));
        Throws(() => path.Prepare(Plan(), float.NaN)); Throws(() => path.Prepare(Plan(), -1));
        path.Prepare(Plan(), 1);
        Throws(() => path.Sample(float.NaN)); Throws(() => path.Distance(float.PositiveInfinity));
    }
    private static ScenicShotPlan Directed(ScenicShotRole role = ScenicShotRole.Traverse, bool useNativeSpline = true)
    {
        var feature = new ScenicFeature("ridge:example", ScenicFeatureKind.Ridge,
            P(100, 30, -40), P(1, 0, 0), 14, 8, .85f);
        return ScenicShotPlan.Directed(40, feature, role, P(78, 39, -61), P(102, 43, -58),
            P(97, 33, -40), P(106, 35, -39), 58, -.1f, -.06f, P(88, 44, -70), useNativeSpline);
    }
    private static void DirectedNativeDistance(ScenicShotRole role)
    {
        var plan = Directed(role);
        using var path = new ScenicSplinePath();
        Check(path.Prepare(plan, 1) && path.Ready, "Directed native path was not prepared.");
        plan = path.PreparedPlan!;
        Near(path.Sample(0).CameraPosition, plan.Sample(0).CameraPosition);
        Near(path.Sample(1).CameraPosition, plan.Sample(1).CameraPosition);
        for (int i = 0; i <= 100; i++)
        {
            float progress = i / 100f;
            float normalized = path.Spline!.ConvertIndexUnit(path.Distance(progress), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            Vector3 native = path.Spline.EvaluatePosition(normalized);
            var sample = path.Sample(progress);
            Near(sample.CameraPosition, P(native.x, native.y, native.z));
            Near(sample.AimPosition, plan.Sample(progress).AimPosition);
            Check(sample.ScreenX == -.1f && sample.ScreenY == -.06f, "Native Dolly replaced directed composition.");
        }
    }
    private static void DirectedStrength()
    {
        var plan = Directed();
        using var low = new ScenicSplinePath(); using var high = new ScenicSplinePath();
        Check(low.Prepare(plan, .25f) && high.Prepare(plan, 1.5f), "Directed route preparation failed.");
        for (int i = 0; i <= 100; i++)
        {
            Near(low.Sample(i / 100f).CameraPosition, high.Sample(i / 100f).CameraPosition, .0001);
            Near(low.Sample(i / 100f).AimPosition, high.Sample(i / 100f).AimPosition, .0001);
        }
        Check(Math.Abs(low.Length - high.Length) < .0001f, "Motion strength reshaped a screened directed route.");
    }
    private static void DirectedPrefix()
    {
        var plan = Directed(ScenicShotRole.Reveal); var prefix = plan.Trim(.6);
        using var path = new ScenicSplinePath();
        Check(path.Prepare(prefix, 1), "Directed prefix was not prepared.");
        Near(path.Sample(0).CameraPosition, plan.Sample(0).CameraPosition);
        Near(path.Sample(1).CameraPosition, plan.Sample(.6).CameraPosition);
        Near(path.Sample(1).AimPosition, plan.Sample(.6).AimPosition);
        Check(prefix.FieldOfView == plan.FieldOfView, "Trimming changed the fixed shot lens.");
        // A regenerated spline must still receive route validation; this proves
        // endpoint/prefix intent, not collision equivalence to the source spline.
    }
    private static void DirectedEarlyMovement()
    {
        var plan = Directed(); plan = plan.WithDuration(plan.DurationSeconds);
        using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
        float early = (float)Math.Min(1, 2 / plan.DurationSeconds);
        Check(path.Distance(early) > path.Length * .04f, "Directed shot spends its opening effectively still.");
        float cruise = path.Distance(.35f) - path.Distance(.25f);
        Check(Math.Abs((path.Distance(.65f) - path.Distance(.55f)) - cruise) < .0001f,
            "Native directed route contains an unplanned middle speed surge.");
    }
    private static void DirectedAuthoredPath()
    {
        var plan = Directed(ScenicShotRole.Reveal, false);
        using var path = new ScenicSplinePath();
        Check(path.Prepare(plan, 1), "Authored scenic route could not be prepared.");
        plan = path.PreparedPlan!;
        foreach (float progress in new[] { 0f, .015f, .08f, .17f, .35f, .52f, .73f, .92f, .99f, 1f })
        {
            ScenicShotSample authored = plan.Sample(progress), output = path.Sample(progress);
            Near(output.CameraPosition, authored.CameraPosition, 0);
            Near(output.AimPosition, authored.AimPosition, 0);
            Check(output.ScreenX == authored.ScreenX && output.ScreenY == authored.ScreenY,
                "Dolly-off changed the authored composition.");
        }
    }
    private static void DirectedAuthoredPrefix()
    {
        var original = Directed(ScenicShotRole.Reveal, false);
        var prefix = original.Trim(.63);
        Check(!prefix.UseNativeSpline && prefix.FieldOfView == original.FieldOfView,
            "Trimming reset the authored-body selection or fixed lens.");
        using var path = new ScenicSplinePath();
        Check(path.Prepare(prefix, 1), "Trimmed authored route could not be prepared.");
        Near(path.Sample(0).CameraPosition, original.Sample(0).CameraPosition, 0);
        Near(path.Sample(1).CameraPosition, original.Sample(.63).CameraPosition, .0001);
        Near(path.Sample(1).AimPosition, original.Sample(.63).AimPosition, .0001);
        Near(path.Sample(.37f).CameraPosition, path.PreparedPlan!.Sample(.37f).CameraPosition, 0);
    }

    private static void RaisedNativeRoutes()
    {
        foreach (ScenicShotRole role in Enum.GetValues(typeof(ScenicShotRole)))
        {
            ScenicShotPlan source = Directed(role);
            // This case tests rigid geometry under the same authored clock.
            source = source.WithDuration(source.DurationSeconds);
            ScenicShotPlan fitted = source.RaiseCamera(23);
            using var original = new ScenicSplinePath();
            using var raised = new ScenicSplinePath();
            Check(original.Prepare(source, 1) && raised.Prepare(fitted, 1), "Raised native route did not prepare.");
            Check(Math.Abs(original.Length - raised.Length) < .003, "Whole-route lift changed native path length.");
            for (int i = 0; i <= 100; i++)
            {
                float progress = i / 100f;
                ScenicShotSample a = original.Sample(progress), b = raised.Sample(progress);
                Near(P(a.CameraPosition.X, a.CameraPosition.Y + 23, a.CameraPosition.Z), b.CameraPosition);
                Near(a.AimPosition, b.AimPosition, 0);
                float normalized = raised.Spline!.ConvertIndexUnit(raised.Distance(progress), PathIndexUnit.Distance, PathIndexUnit.Normalized);
                Vector3 native = raised.Spline.EvaluatePosition(normalized);
                Near(P(native.x, native.y, native.z), b.CameraPosition);
            }
            var oldSpline = original.Spline;
            Check(original.Prepare(fitted, 1) && !ReferenceEquals(oldSpline, original.Spline),
                "The same-ID fitted route reused its below-surface spline cache.");
            Near(original.Sample(.4f).CameraPosition, raised.Sample(.4f).CameraPosition);
        }
    }

    private static void DirectedInteriorNativeSlice()
    {
        ScenicShotPlan source = Directed(ScenicShotRole.Reveal), slice = source.Slice(.2, .82);
        using var path = new ScenicSplinePath();
        Check(path.Prepare(source, 1), "Source path was not prepared.");
        var originalSpline = path.Spline;
        Check(path.Prepare(slice, 1) && !ReferenceEquals(originalSpline, path.Spline),
            "An interior same-ID slice reused the original complete spline.");
        Near(path.Sample(0).CameraPosition, source.Sample(.2).CameraPosition, .0001);
        Near(path.Sample(1).CameraPosition, source.Sample(.82).CameraPosition, .0001);
        Near(path.Sample(0).AimPosition, source.Sample(.2).AimPosition, .0001);
        Near(path.Sample(1).AimPosition, source.Sample(.82).AimPosition, .0001);
        for (int i = 0; i <= 100; i++)
        {
            float progress = i / 100f;
            float normalized = path.Spline!.ConvertIndexUnit(path.Distance(progress), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            Vector3 native = path.Spline.EvaluatePosition(normalized);
            ScenicShotSample actual = path.Sample(progress), authored = path.PreparedPlan!.Sample(progress);
            Near(actual.CameraPosition, P(native.x, native.y, native.z));
            Near(actual.AimPosition, authored.AimPosition, .0001);
            Check(actual.ScreenX == authored.ScreenX && actual.ScreenY == authored.ScreenY,
                "A sliced native path replaced deliberate composition.");
        }
        // This is a regenerated spline over the exact authored subcurve. It
        // must be revalidated; original native-distance samples do not prove
        // collision equivalence for the newly generated knots/parameterization.
    }

    private static void DirectedInteriorAuthoredSlice()
    {
        ScenicShotPlan source = Directed(ScenicShotRole.Reveal, false), slice = source.Slice(.2, .82);
        using var path = new ScenicSplinePath();
        Check(path.Prepare(slice, 1) && !slice.UseNativeSpline && slice.FieldOfView == source.FieldOfView,
            "Slicing changed authored-body ownership or the fixed lens.");
        for (int i = 0; i <= 100; i++)
        {
            float p = i / 100f;
            Near(path.Sample(p).CameraPosition, path.PreparedPlan!.Sample(p).CameraPosition, 0);
            Near(path.Sample(p).AimPosition, path.PreparedPlan!.Sample(p).AimPosition, 0);
        }
    }

    private static void MeasuredNativeSection()
    {
        ScenicShotPlan source = Directed(ScenicShotRole.Reveal);
        using var original = new ScenicSplinePath();
        Check(original.Prepare(source, 1), "Original native route was unavailable.");
        const float firstProgress = .13f, lastProgress = .79f;
        ScenicShotSample first = original.Sample(firstProgress), last = original.Sample(lastProgress);
        ScenicShotPlan section = original.ProposeMeasuredSlice(firstProgress, lastProgress);
        Check(source.Sample(firstProgress).CameraPosition.Distance(first.CameraPosition) > .01,
            "Fixture does not expose the native-distance versus authored-parameter mismatch.");
        Near(section.Sample(0).CameraPosition, first.CameraPosition, 0);
        Near(section.Sample(1).CameraPosition, last.CameraPosition, .0001);
        Near(section.Sample(0).AimPosition, first.AimPosition, 0);
        Near(section.Sample(1).AimPosition, last.AimPosition, .0001);
        Vector3 middle = original.Spline!.EvaluatePosition((float)((original.PreparedPlan!.MotionFraction(firstProgress) +
            original.PreparedPlan.MotionFraction(lastProgress)) * .5));
        Near(section.Sample(.5).CameraPosition, P(middle.x, middle.y, middle.z), .0001);
        Check(section.Id == source.Id && section.Role == source.Role && section.Feature == source.Feature &&
            section.FieldOfView == source.FieldOfView && section.UseNativeSpline == source.UseNativeSpline &&
            section.Sample(0).ScreenX == first.ScreenX && section.Sample(1).ScreenY == last.ScreenY,
            "Measured section changed its subject, lens, engine or composition identity.");
        using var generated = new ScenicSplinePath();
        Check(generated.Prepare(section, 1), "Measured section failed native preparation.");
        Near(generated.Sample(0).CameraPosition, first.CameraPosition, 0);
        Near(generated.Sample(1).CameraPosition, last.CameraPosition, .0001);
        for (int i = 0; i <= 100; i++)
        {
            float progress = i / 100f;
            float normalized = generated.Spline!.ConvertIndexUnit(generated.Distance(progress), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            Vector3 actual = generated.Spline.EvaluatePosition(normalized);
            Near(generated.Sample(progress).CameraPosition, P(actual.x, actual.y, actual.z));
        }
    }

    private static void MeasuredSectionValidation()
    {
        using var path = new ScenicSplinePath();
        Throws(() => path.ProposeMeasuredSlice(.2f, .8f));
        var source = Directed(ScenicShotRole.Reveal, false);
        path.Prepare(source, 1);
        var proposal = path.ProposeMeasuredSlice(.2f, .8f);
        var exact = path.PreparedPlan!.Slice(.2f, .8f);
        Check(!proposal.UseNativeSpline, "An authored section silently enabled native Dolly.");
        for (int i = 0; i <= 100; i++)
        {
            Near(proposal.Sample(i / 100d).CameraPosition, exact.Sample(i / 100d).CameraPosition, 0);
            Near(proposal.Sample(i / 100d).AimPosition, exact.Sample(i / 100d).AimPosition, 0);
        }
        Throws(() => path.ProposeMeasuredSlice(float.NaN, .8f));
        Throws(() => path.ProposeMeasuredSlice(.2f, float.PositiveInfinity));
        Throws(() => path.ProposeMeasuredSlice(-.1f, .8f));
        Throws(() => path.ProposeMeasuredSlice(.2f, 1.1f));
        Throws(() => path.ProposeMeasuredSlice(.8f, .2f));
        Throws(() => path.ProposeMeasuredSlice(.2f, .2f));
        path.Prepare(Plan(), 1);
        Throws(() => path.ProposeMeasuredSlice(.2f, .8f));
    }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentOutOfRangeException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid input was accepted.");
    }

    // These are real upstream non-component modifier objects and CameraState
    // values. The unused vcam parameter is null; no GameObject or engine API is
    // substituted. The complete extension's easing/cache/callback lifecycle
    // still requires a Unity runtime.
    private static LensSettings BaseLens()
    {
        LensSettings lens = LensSettings.Default;
        lens.FieldOfView = 55;
        lens.NearClipPlane = .25f; lens.FarClipPlane = 2200;
        lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        lens.Dutch = 0;
        return lens;
    }
    private static CinemachineFreeLookModifier.LensModifier LensModifier()
    {
        var modifier = new CinemachineFreeLookModifier.LensModifier { Top = BaseLens(), Bottom = BaseLens() };
        ElevationProfile profile = ElevationProfilePolicy.Resolve(true, true, false, false, false, ShotScale.Full, false, false, 55, true);
        modifier.Top.FieldOfView = profile.TopFov; modifier.Bottom.FieldOfView = profile.BottomFov;
        // The native modifier must inherit the current camera's perspective
        // mode even if the endpoint was authored in another projection mode.
        modifier.Top.ModeOverride = modifier.Bottom.ModeOverride = LensSettings.OverrideModes.Orthographic;
        return modifier;
    }
    private static CameraState ApplyLens(CinemachineFreeLookModifier.LensModifier modifier, float amount)
    {
        CameraState state = CameraState.Default;
        state.Lens = BaseLens(); state.RawPosition = new Vector3(10, 20, 30);
        modifier.BeforePipeline(null!, ref state, .02f, amount);
        return state;
    }
    private static void LensModifierContract()
    {
        CameraState state = ApplyLens(LensModifier(), .5f);
        Check(Math.Abs(state.Lens.FieldOfView - 57) < .0001f, "Native upper profile interpolation changed.");
        Check(state.Lens.ModeOverride == LensSettings.OverrideModes.Perspective && !state.Lens.Orthographic,
            "Lens profile changed the physical camera projection mode.");
        Check(state.Lens.NearClipPlane == .25f && state.Lens.FarClipPlane == 2200 && state.Lens.Dutch == 0,
            "Lens profile unexpectedly changed clipping or introduced roll.");
        Check(state.RawPosition == new Vector3(10, 20, 30), "Lens profile displaced the camera.");
    }
    private static void LensModifierEndpoints()
    {
        var modifier = LensModifier();
        Check(ApplyLens(modifier, -1).Lens.FieldOfView == 53 && ApplyLens(modifier, 0).Lens.FieldOfView == 55 &&
            ApplyLens(modifier, 1).Lens.FieldOfView == 59, "Native lens profile did not preserve lower/center/upper values.");
        Check(ApplyLens(modifier, -.5f).Lens.FieldOfView == 54, "Lower profile interpolated in the wrong direction.");
    }
    private static void LensModifierNoAccumulation()
    {
        var modifier = LensModifier();
        for (int i = 0; i < 100; i++)
            Check(ApplyLens(modifier, .5f).Lens.FieldOfView == 57, "Repeated fresh solves compounded lens modification.");
        Check(ApplyLens(modifier, 0).Lens.FieldOfView == 55, "Returning to neutral failed to restore the base lens.");
    }

    private static void ObservationalRectangles()
    {
        foreach (ShotScale scale in new[] { ShotScale.Wide, ShotScale.Full })
            foreach (float x in new[] { -1f, -.12f, 0, .12f, 1f })
                foreach (float y in new[] { -.4f, 0, .4f })
                {
                    var profile = ObservationalShotPolicy.Describe(scale, 10, 1, x, y);
                    ScreenComposerSettings settings = NativeObservationalCamera.Composition(profile);
                    Rect dead = settings.DeadZoneRect, hard = settings.HardLimitsRect;
                    Check(dead.xMin > hard.xMin && dead.xMax < hard.xMax && dead.yMin > hard.yMin && dead.yMax < hard.yMax,
                        "Actual native dead zone escapes the hard limit.");
                    Check(hard.xMin >= .05f && hard.xMax <= .95f && hard.yMin >= .05f && hard.yMax <= .95f,
                        "Native observational hard guide exceeds the image safe area.");
                }
    }
    private static void ObservationalVerticalPlacement()
    {
        var profile = ObservationalShotPolicy.Describe(ShotScale.Full, 10, 1, .1f, .05f);
        ScreenComposerSettings settings = NativeObservationalCamera.Composition(profile);
        Check(Math.Abs(settings.DeadZoneRect.center.x - .6f) < .0001f && Math.Abs(settings.DeadZoneRect.center.y - .45f) < .0001f,
            "Core Y-up placement was not converted exactly once to Cinemachine Y-down.");
    }
    private static ScreenComposerSettings ProfileBaseline()
    {
        ScreenComposerSettings settings = ScreenComposerSettings.Default;
        settings.ScreenPosition = new Vector2(.08f, -.08f);
        settings.DeadZone.Enabled = settings.HardLimits.Enabled = true;
        settings.DeadZone.Size = new Vector2(.12f, .1f);
        settings.HardLimits.Size = new Vector2(.8f, .72f);
        settings.HardLimits.Offset = new Vector2(-2 * .08f / (.8f - .12f), 2 * .08f / (.72f - .1f));
        return settings;
    }
    private static void RectNear(Rect a, Rect b) => Check(Math.Abs(a.xMin - b.xMin) < .0001f && Math.Abs(a.xMax - b.xMax) < .0001f &&
        Math.Abs(a.yMin - b.yMin) < .0001f && Math.Abs(a.yMax - b.yMax) < .0001f, "A native profile moved the actual image hard limits.");
    private static void ProfileLimits()
    {
        var baseline = ProfileBaseline();
        var profile = ElevationProfilePolicy.Resolve(true, true, false, false, false, ShotScale.Full, false, false, 55, true);
        var top = NativeFreeLookProfiles.ComposeEndpoint(baseline, profile.TopScreenY);
        var bottom = NativeFreeLookProfiles.ComposeEndpoint(baseline, profile.BottomScreenY);
        Check(top.ScreenPosition.y > baseline.ScreenPosition.y && bottom.ScreenPosition.y < baseline.ScreenPosition.y,
            "Elevation profiles failed to vary vertical composition.");
        for (int i = 0; i <= 20; i++)
        {
            RectNear(baseline.HardLimitsRect, ScreenComposerSettings.Lerp(baseline, top, i / 20f).HardLimitsRect);
            RectNear(baseline.HardLimitsRect, ScreenComposerSettings.Lerp(bottom, baseline, i / 20f).HardLimitsRect);
        }
    }
    private static void SaturatedProfileLimits()
    {
        foreach (float offset in new[] { -.98f, .98f })
        {
            var baseline = ProfileBaseline(); baseline.HardLimits.Offset.y = offset;
            RectNear(baseline.HardLimitsRect, NativeFreeLookProfiles.ComposeEndpoint(baseline, .1f).HardLimitsRect);
            RectNear(baseline.HardLimitsRect, NativeFreeLookProfiles.ComposeEndpoint(baseline, -.1f).HardLimitsRect);
        }
    }
    private static void DisabledDeadZoneLimits()
    {
        var baseline = ProfileBaseline(); baseline.DeadZone.Enabled = false;
        RectNear(baseline.HardLimitsRect, NativeFreeLookProfiles.ComposeEndpoint(baseline, .04f).HardLimitsRect);
        RectNear(baseline.HardLimitsRect, NativeFreeLookProfiles.ComposeEndpoint(baseline, -.04f).HardLimitsRect);
    }
}
