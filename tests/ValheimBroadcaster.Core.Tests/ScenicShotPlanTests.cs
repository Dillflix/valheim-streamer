using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicShotPlanTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Environmental shot families develop independent world camera and aim paths", IndependentPaths);
        yield return ("Environmental movement starts and ends gently without looping", ContinuousFiniteMovement);
        yield return ("Scenic strength and static controls retain predictable bounded paths", StrengthAndStaticControls);
        yield return ("Scenic previews and paused footage do not mutate shot progress", ReadOnlySampling);
        yield return ("Environmental path inputs reject invalid geometry and bound world outputs", Validation);
        yield return ("Terrain fitting raises the whole scenic route without moving its subject or changing the lens", RaisedRoutePreservesComposition);
        yield return ("Raised curved scenic routes retain their authored shape after trimming", RaisedCurveTrim);
        yield return ("Interior scenic slices preserve the entire authored quadratic and aim subcurve", InteriorCurveSlice);
        yield return ("Interior scenic slices preserve straight routes and composition identity", InteriorLineSlice);
        yield return ("Scenic slice intervals reject malformed, reversed and degenerate input", SliceValidation);
        yield return ("Scenic prefix slicing and immutable elevation fitting commute", SliceCompatibility);
    }

    private static AutonomyPosition P(float x = 0, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicShotPlan Plan(ScenicShotStyle style, long id = 8) =>
        new ScenicShotPlan(id, style, P(100, 40, -50), P(112, 42, -50), P(0, 12, -20), 0.1f, 0.08f);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void IndependentPaths()
    {
        var ends = new List<AutonomyPosition>();
        foreach (ScenicShotStyle style in Enum.GetValues(typeof(ScenicShotStyle)))
        {
            var plan = Plan(style);
            ScenicShotSample start = plan.Sample(0), end = plan.Sample(1);
            Check(start.AimPosition.Distance(P(100, 40, -50)) < 0.001 &&
                start.CameraPosition.Distance(P(100, 52, -70)) < 0.001,
                "The plan did not begin at the adapter's measured world viewpoint.");
            Check(end.CameraPosition.Distance(start.CameraPosition) > 2,
                style + " failed to develop an environmental viewpoint.");
            double x = end.CameraPosition.X - start.CameraPosition.X - (end.AimPosition.X - start.AimPosition.X);
            double y = end.CameraPosition.Y - start.CameraPosition.Y - (end.AimPosition.Y - start.AimPosition.Y);
            double z = end.CameraPosition.Z - start.CameraPosition.Z - (end.AimPosition.Z - start.AimPosition.Z);
            Check(Math.Sqrt(x * x + y * y + z * z) > 2,
                style + " merely copied aim translation with a fixed camera offset.");
            foreach (AutonomyPosition previous in ends)
                Check(previous.Distance(end.CameraPosition) > 1, "Different shot families produced the same endpoint.");
            ends.Add(end.CameraPosition);
            Check(Math.Abs(end.ScreenX) <= 0.2 && Math.Abs(end.ScreenY) <= 0.2,
                "Environmental framing requested an excessive screen displacement.");
        }
    }

    private static void ContinuousFiniteMovement()
    {
        foreach (ScenicShotStyle style in Enum.GetValues(typeof(ScenicShotStyle)))
        {
            ScenicShotPlan plan = Plan(style);
            ScenicShotSample initial = plan.Sample(0), previous = initial;
            double largestStep = 0;
            for (int frame = 1; frame <= 2700; frame++)
            {
                ScenicShotSample current = plan.Sample(frame / 2700d, 1.5f);
                largestStep = Math.Max(largestStep, current.CameraPosition.Distance(previous.CameraPosition));
                Check(current.CameraPosition.Distance(initial.CameraPosition) < 25,
                    "The bounded local environmental shot escaped its intended region.");
                Check(current.CameraPosition.Distance(current.AimPosition) > 8,
                    "The planned camera collapsed into its environmental focus.");
                previous = current;
            }
            Check(largestStep < 0.025, "Environmental motion jumped during a 45-second shot.");
            double beginning = plan.Sample(0.01).CameraPosition.Distance(initial.CameraPosition);
            double middle = plan.Sample(0.51).CameraPosition.Distance(plan.Sample(0.5).CameraPosition);
            double ending = plan.Sample(1).CameraPosition.Distance(plan.Sample(0.99).CameraPosition);
            Check(beginning < middle * 0.03 && ending < middle * 0.03,
                "An environmental shot starts or ends with abrupt motion.");
            Check(plan.Sample(1).CameraPosition.Distance(plan.Sample(10).CameraPosition) < 0.001,
                "A completed environmental path loops or continues drifting.");
        }
    }

    private static void StrengthAndStaticControls()
    {
        foreach (ScenicShotStyle style in Enum.GetValues(typeof(ScenicShotStyle)))
        {
            ScenicShotPlan plan = Plan(style);
            ScenicShotSample initial = plan.Sample(0);
            Check(initial.CameraPosition.Distance(plan.Sample(1, 0).CameraPosition) < 0.001 &&
                initial.AimPosition.Distance(plan.Sample(1, 0).AimPosition) < 0.001,
                "Zero strength did not preserve the starting environmental composition.");
            Check(initial.CameraPosition.Distance(plan.Sample(1, 1.5f, false).CameraPosition) < 0.001,
                "Static comparison mode still advanced the environmental camera.");
            Check(plan.Sample(1, 1.5f).CameraPosition.Distance(initial.CameraPosition) >
                plan.Sample(1).CameraPosition.Distance(initial.CameraPosition) * 1.25,
                "The upper motion strength range is silently ignored.");
            Check(plan.Sample(1, 1.5f).CameraPosition.Distance(plan.Sample(1, 100).CameraPosition) < 0.001,
                "An external strength exceeded the bounded motion envelope.");
        }
    }

    private static void ReadOnlySampling()
    {
        ScenicShotPlan plan = Plan(ScenicShotStyle.Arc);
        ScenicShotSample current = plan.Sample(0.4);
        for (int i = 0; i < 100; i++) plan.Sample(0.9, 1.5f);
        ScenicShotSample resumed = plan.Sample(0.4);
        Check(current.CameraPosition.Distance(resumed.CameraPosition) == 0 &&
            current.AimPosition.Distance(resumed.AimPosition) == 0,
            "Preview evaluation or paused footage advanced the hidden path.");
        Check(Plan(ScenicShotStyle.Arc, 9).Sample(1).CameraPosition.Distance(plan.Sample(1).CameraPosition) > 10,
            "Distinct arc directions did not produce alternative environmental views.");
    }

    private static void Validation()
    {
        Reject(() => Plan(ScenicShotStyle.Arc).Sample(double.NaN));
        Reject(() => Plan(ScenicShotStyle.Arc).Sample(0.5, -1));
        Reject(() => new ScenicShotPlan(1, (ScenicShotStyle)99, P(), P(1), P(0, 10, 20)));
        Reject(() => new ScenicShotPlan(1, ScenicShotStyle.Arc, P(), P(100), P(0, 10, 20)));
        Reject(() => new ScenicShotPlan(1, ScenicShotStyle.Arc, P(), P(1), P()));
        var boundary = new ScenicShotPlan(1, ScenicShotStyle.Reveal, P(19999, 19999, 19999),
            P(20000, 20000, 20000), P(20, 10, 20));
        ScenicShotSample sample = boundary.Sample(1, 1.5f);
        Check(sample.CameraPosition.X <= 20000 && sample.CameraPosition.Y <= 20000 && sample.CameraPosition.Z <= 20000,
            "Environmental sampling exceeded supported finite world coordinates.");
    }

    private static ScenicShotPlan DirectedReveal() => ScenicShotPlan.Directed(41,
        new ScenicFeature("rock", ScenicFeatureKind.Rock, P(100, 20, 100), P(1), 8, 12, .8f),
        ScenicShotRole.Reveal, P(85, 16, 85), P(100, 23, 85), P(100, 22, 100), P(102, 23, 100),
        58, .08f, -.04f, P(92, 18, 82), false);

    private static void RaisedRoutePreservesComposition()
    {
        ScenicShotPlan original = DirectedReveal(), raised = original.RaiseCamera(14);
        Check(!ReferenceEquals(original, raised) && raised.Id == original.Id &&
            raised.Feature == original.Feature && raised.Role == original.Role &&
            raised.FieldOfView == original.FieldOfView && raised.UseNativeSpline == original.UseNativeSpline,
            "Terrain fitting changed the subject/lens/engine identity or mutated the existing plan.");
        for (int i = 0; i <= 100; i++)
        {
            ScenicShotSample a = original.Sample(i / 100d), b = raised.Sample(i / 100d);
            Check(Math.Abs(b.CameraPosition.Y - a.CameraPosition.Y - 14) < .0001 &&
                b.CameraPosition.X == a.CameraPosition.X && b.CameraPosition.Z == a.CameraPosition.Z &&
                b.AimPosition.Distance(a.AimPosition) < .0001 && b.ScreenX == a.ScreenX && b.ScreenY == a.ScreenY,
                "Surface fit bobbed/clamped the live path or displaced the measured subject.");
        }
        Check(Math.Abs(original.TravelMetres - raised.TravelMetres) < .0001,
            "A rigid vertical fit changed the route's travel length.");
        Check(ReferenceEquals(original, original.RaiseCamera(0)), "A no-op fit changed path identity.");
        Reject(() => original.RaiseCamera(float.NaN));
        Reject(() => original.RaiseCamera(-1));
        Reject(() => original.RaiseCamera(65));
    }

    private static void RaisedCurveTrim()
    {
        ScenicShotPlan original = DirectedReveal();
        ScenicShotPlan fitThenTrim = original.RaiseCamera(8).Trim(.7);
        ScenicShotPlan trimThenFit = original.Trim(.7).RaiseCamera(8);
        for (int i = 0; i <= 50; i++)
        {
            ScenicShotSample a = fitThenTrim.Sample(i / 50d), b = trimThenFit.Sample(i / 50d);
            Check(a.CameraPosition.Distance(b.CameraPosition) < .0001 && a.AimPosition.Distance(b.AimPosition) < .0001,
                "Terrain fit/trim changed the curved reveal rather than preserving its useful prefix.");
        }
    }

    private static void InteriorCurveSlice()
    {
        ScenicShotPlan original = DirectedReveal();
        foreach (var interval in new[] { (Start: .15, End: .85), (Start: 0d, End: .63), (Start: .2, End: 1d) })
        {
            ScenicShotPlan slice = original.Slice(interval.Start, interval.End);
            AssertSubcurve(original, slice, interval.Start, interval.End);
            Check(slice.TravelMetres < original.TravelMetres && slice.TravelMetres >= 1 && slice.DurationSeconds >= 8,
                "Interior segment lost meaningful bounded movement or a developed shot duration.");
        }
    }

    private static void InteriorLineSlice()
    {
        ScenicShotPlan original = ScenicShotPlan.Directed(57, DirectedReveal().Feature!, ScenicShotRole.Traverse,
            P(80, 30, 80), P(112, 35, 80), P(98, 23, 100), P(103, 24, 100), 61, -.12f, -.07f, null, true);
        ScenicShotPlan slice = original.Slice(.2, .8);
        Check(!ReferenceEquals(original, slice) && original.Id == slice.Id && original.Feature == slice.Feature &&
            original.FeatureId == slice.FeatureId && original.FeatureKind == slice.FeatureKind && original.Role == slice.Role &&
            original.FieldOfView == slice.FieldOfView && original.UseNativeSpline == slice.UseNativeSpline,
            "Slicing mutated the source or changed its authored subject/role/lens/engine identity.");
        AssertSubcurve(original, slice, .2, .8);
    }

    private static void AssertSubcurve(ScenicShotPlan original, ScenicShotPlan slice, double fromFraction, double toFraction)
    {
        double from = ScenicMotionProfile.Progress(fromFraction), to = ScenicMotionProfile.Progress(toFraction);
        for (int i = 0; i <= 100; i++)
        {
            double p = i / 100d;
            double sourceProgress = InverseProfile(from + (to - from) * ScenicMotionProfile.Progress(p));
            ScenicShotSample expected = original.Sample(sourceProgress), actual = slice.Sample(p);
            Check(expected.CameraPosition.Distance(actual.CameraPosition) < .0001 &&
                expected.AimPosition.Distance(actual.AimPosition) < .0001 &&
                actual.ScreenX == expected.ScreenX && actual.ScreenY == expected.ScreenY,
                "A sliced route deviated from its original interior geometry or composition.");
        }
    }

    private static double InverseProfile(double value)
    {
        double low = 0, high = 1;
        for (int i = 0; i < 60; i++)
        {
            double mid = (low + high) * .5;
            if (ScenicMotionProfile.Progress(mid) < value) low = mid; else high = mid;
        }
        return (low + high) * .5;
    }

    private static void SliceValidation()
    {
        ScenicShotPlan source = DirectedReveal();
        Reject(() => source.Slice(double.NaN, .8));
        Reject(() => source.Slice(.2, double.PositiveInfinity));
        Reject(() => source.Slice(-.1, .8));
        Reject(() => source.Slice(.2, 1.1));
        Reject(() => source.Slice(.7, .3));
        Reject(() => source.Slice(.5, .5));
        Reject(() => source.Slice(1, 1));
        Reject(() => source.Slice(.5, .50001));
        try { Plan(ScenicShotStyle.Arc).Slice(.2, .8); }
        catch (InvalidOperationException) { return; }
        throw new Exception("A legacy environmental route accepted directed slicing.");
    }

    private static void SliceCompatibility()
    {
        ScenicShotPlan original = DirectedReveal();
        ScenicShotPlan prefix = original.Trim(.7), explicitPrefix = original.Slice(0, .7);
        ScenicShotPlan fitThenSlice = original.RaiseCamera(8).Slice(.2, .85);
        ScenicShotPlan sliceThenFit = original.Slice(.2, .85).RaiseCamera(8);
        ScenicShotPlan whole = original.Slice(0, 1);
        for (int i = 0; i <= 100; i++)
        {
            double p = i / 100d;
            Check(prefix.Sample(p).CameraPosition.Distance(explicitPrefix.Sample(p).CameraPosition) < .0001,
                "Trim no longer delegates equivalent prefix geometry.");
            Check(fitThenSlice.Sample(p).CameraPosition.Distance(sliceThenFit.Sample(p).CameraPosition) < .0001 &&
                fitThenSlice.Sample(p).AimPosition.Distance(sliceThenFit.Sample(p).AimPosition) < .0001,
                "Slice and rigid elevation fitting changed the selected subcurve.");
            Check(whole.Sample(p).CameraPosition.Distance(original.Sample(p).CameraPosition) < .0001 &&
                whole.Sample(p).AimPosition.Distance(original.Sample(p).AimPosition) < .0001,
                "A full interval changed the original route.");
        }
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Invalid environmental input was accepted.");
    }
}
