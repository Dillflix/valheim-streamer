using System;
using System.Collections.Generic;
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
        System.Console.WriteLine($"{passed}/{total} native spline math cases passed. No Unity scene, camera component or graphics backend is executed.");
        return passed == total ? 0 : 1;
    }

    private static IEnumerable<(string Name, Action Run)> Cases()
    {
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
        yield return ("Large world coordinates retain finite bounded spline positions", WorldBounds);
        yield return ("Malformed spline progress and shape strength are rejected", InvalidInputs);
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
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentOutOfRangeException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid input was accepted.");
    }
}
