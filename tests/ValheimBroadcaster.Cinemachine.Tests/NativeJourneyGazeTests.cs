using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class NativeJourneyGazeTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Native journey gaze develops through a bending corridor at the selected cruise", BendingCorridor);
        yield return ("Native gaze rejects a fast turn even when its endpoint aims are valid", RejectFastTurn);
        yield return ("Native journey arrival framing resolves a nearby observed subject", Arrival);
        yield return ("Native evolving journey windows retain exact gaze, position and composition at their seam", Seam);
        yield return ("Native terminal approach reaches a stand-off view without passing through its destination", StandOffApproach);
        yield return ("Incremental native gaze verification preserves rejection and acceptance across work slices", IncrementalVerification);
        yield return ("Incremental native gaze verification cannot reuse a replaced or reset path", InvalidatedVerification);
        yield return ("Incremental native gaze verification rejects invalid timing without accepting an incomplete path", InvalidTiming);
        yield return ("Native journey gaze preserves a smooth outward view when observed terrain terminates its focus rays", ObservedTerrainFocus);
        yield return ("Recorded elevated Journey opening retains its altitude through native travel instead of repeating the observed descent", RecordedElevatedOpening);
        yield return ("Recorded authored Journey geometry can regenerate a measured prefix without losing its exact incoming seam", RecordedPrefixRegeneration);
    }
    private static AutonomyPosition P(double x, double y = 12, double z = 0) => new AutonomyPosition((float)x, (float)y, (float)z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicFeature Feature() => new ScenicFeature("gaze-native", ScenicFeatureKind.Coastline, P(20, 2), P(1, 0), 12, 5, 1);
    private static AutonomyPosition[] Curve(AutonomyPosition start, double initialAngle = 0)
    {
        var body = new AutonomyPosition[13]; double x = start.X, z = start.Z;
        for (int i = 0; i < body.Length; i++)
        {
            body[i] = P(x, start.Y, z);
            double angle = (initialAngle + i * 3) * Math.PI / 180;
            x += 4 * Math.Cos(angle); z += 4 * Math.Sin(angle);
        }
        return body;
    }
    private static ScenicShotPlan Build(long id, AutonomyPosition[] body, AutonomyPosition firstAim,
        double speed, bool entry, AutonomyPosition? subject = null)
    {
        Check(JourneyGazePolicy.TryBuild(body, firstAim, speed, null, subject, out var aims, out var reason), reason);
        return ScenicShotPlan.Spatial(id, Feature(), ScenicShotRole.Traverse, body, aims, 61, .1f, -.04f)
            .AsJourney(19, id - 1, 48 / speed / ScenicJourneyMotion.CruiseFraction(entry, false))
            .WithJourneyRamps(entry, false);
    }
    private static double Angle(ScenicShotSample before, ScenicShotSample after)
    {
        double ax = before.AimPosition.X - before.CameraPosition.X, ay = before.AimPosition.Y - before.CameraPosition.Y,
            az = before.AimPosition.Z - before.CameraPosition.Z;
        double bx = after.AimPosition.X - after.CameraPosition.X, by = after.AimPosition.Y - after.CameraPosition.Y,
            bz = after.AimPosition.Z - after.CameraPosition.Z;
        return Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) /
            Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz))))) * 180 / Math.PI;
    }
    private static void BendingCorridor()
    {
        foreach (double speed in new[] { 2d, 6d, 12d })
        {
            var body = Curve(P(0)); var plan = Build(22, body, P(24, 10), speed, true);
            using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Native curve failed");
            double duration = native.Length / speed / ScenicJourneyMotion.CruiseFraction(true, false);
            Check(NativeJourneyGaze.WithinRate(native, duration), "Developing native gaze exceeded its angular rate at " + speed + "m/s");
            Check(Angle(native.Sample(0), native.Sample(1)) > 15, "Native execution restored constant inherited gaze");
            Check(native.Sample(0).AimPosition.Distance(plan.Sample(0).AimPosition) < .0001, "Native departure moved its first aim");
            for (int i = 0; i <= 256; i++)
            {
                var frame = native.Sample(i / 256f);
                Check(frame.AimPosition.X > frame.CameraPosition.X && frame.AimPosition.Z >= frame.CameraPosition.Z - .001,
                    "Developing gaze looked away from the forward corridor");
                Check(frame.ScreenX == .1f && frame.ScreenY == -.04f && native.PreparedPlan!.FieldOfView == 61,
                    "Independent gaze changed the admitted lens/composition");
            }
        }
    }
    private static void RejectFastTurn()
    {
        var body = new[] { P(0), P(4), P(8), P(12) };
        var aims = new[] { P(20, 10), P(24, 10), P(8, 10, 20), P(12, 10, 20) };
        var plan = ScenicShotPlan.Spatial(22, Feature(), ScenicShotRole.Traverse, body, aims, 61).AsJourney(19, 21, 2);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Fast-turn test route failed");
        Check(!NativeJourneyGaze.WithinRate(native, 2), "Native interpolation admitted a 90-degree snap at cruise");
    }
    private static void Arrival()
    {
        var body = new AutonomyPosition[13]; for (int i = 0; i < body.Length; i++) body[i] = P(i * 4);
        var subject = P(66, 7, 10); var plan = Build(22, body, P(24, 10), 6, false, subject);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Arrival route failed");
        Check(NativeJourneyGaze.WithinRate(native, native.Length / 6), "Native reveal exceeds its bounded angular rate");
        var end = native.Sample(1);
        Check(Angle(end, end.WithAimPosition(subject)) < 2, "Native arrival retained inherited landscape gaze instead of the observed subject");
    }
    private static void Seam()
    {
        var first = Build(22, Curve(P(-1620, 42, 602)), P(-1596, 40, 602), 6, true);
        using var a = new ScenicSplinePath(); Check(a.Prepare(first, 1), "First native section failed");
        var end = a.Sample(1);
        var next = Build(23, Curve(end.CameraPosition, 33), end.AimPosition, 6, false);
        using var b = new ScenicSplinePath(); Check(b.Prepare(next, 1), "Second native section failed");
        var start = b.Sample(0);
        Check(start.CameraPosition.Distance(end.CameraPosition) < .0001 && start.AimPosition.Distance(end.AimPosition) < .0001 &&
            start.ScreenX == end.ScreenX && start.ScreenY == end.ScreenY && next.FieldOfView == first.FieldOfView,
            "Continuation changed the admitted full endpoint view");
        Check(NativeJourneyGaze.WithinRate(b, b.Length / 6), "Continuation gaze cannot develop at cruise");
        Check(Angle(end, b.Sample(.01f)) < 1, "A section boundary introduces an immediate viewing jump");
    }
    private static void StandOffApproach()
    {
        var destination = P(52, 2, 4); var subject = P(52, 6, 4);
        Check(JourneyArrivalGeometry.TryBuild(P(0), P(1, 0), destination, 16, subject, 64, 0,
            out var body, out var reason), reason);
        var plan = Build(22, body, P(24, 10), 6, false, subject);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Native arrival approach failed");
        Check(NativeJourneyGaze.WithinRate(native, native.Length / 6), "Stand-off approach cannot develop its gaze at cruise");
        var last = native.Sample(1);
        Check(last.CameraPosition.HorizontalDistance(destination) >= 8 && last.CameraPosition.HorizontalDistance(destination) <= 16 &&
            last.CameraPosition.Distance(body[body.Length - 1]) < .001 && Angle(last, last.WithAimPosition(subject)) < 1,
            "Native approach lost its authored arrival stand-off or subject");
        double previous = 1000;
        for (int i = 0; i <= 256; i++)
        {
            double distance = native.Sample(i / 256f).CameraPosition.HorizontalDistance(destination);
            Check(distance <= previous + .001 && distance >= 7.99, "Native approach passed its destination or retreated from it");
            previous = distance;
        }
    }

    private static void IncrementalVerification()
    {
        var plan = Build(22, Curve(P(0)), P(24, 10), 6, true);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Incremental test route failed");
        foreach (double duration in new[] { native.Length / 6 / ScenicJourneyMotion.CruiseFraction(true, false), .1 })
        {
            bool expected = NativeJourneyGaze.WithinRate(native, duration);
            foreach (int slice in new[] { 1, 7, 32, 64 })
            {
                var evaluation = new NativeJourneyGaze.Evaluation(); evaluation.Begin(native, duration);
                Check(!evaluation.Complete && !evaluation.Accepted, "Beginning a check admitted an unchecked native path");
                while (!evaluation.Complete)
                {
                    int previous = evaluation.CheckedIntervals;
                    evaluation.Step(slice);
                    Check(evaluation.CheckedIntervals > previous && evaluation.CheckedIntervals - previous <= slice,
                        "A native gaze work slice did not respect its requested sample bound");
                    Check(evaluation.Complete || !evaluation.Accepted, "Partially checked native gaze became usable");
                }
                Check(evaluation.Accepted == expected && evaluation.CheckedIntervals <= 256,
                    "Yielding changed the angular-rate verdict");
                if (expected) Check(evaluation.CheckedIntervals == 256, "Accepted gaze skipped part of its native path");
                int checkedAtEnd = evaluation.CheckedIntervals;
                evaluation.Step(slice);
                Check(evaluation.CheckedIntervals == checkedAtEnd && evaluation.Accepted == expected,
                    "Completed gaze verification consumed work or changed verdict on another update");
            }
        }
    }

    private static void InvalidatedVerification()
    {
        var first = Build(22, Curve(P(0)), P(24, 10), 6, true);
        var replacement = Build(23, Curve(P(100)), P(124, 10), 6, true);
        using var native = new ScenicSplinePath(); Check(native.Prepare(first, 1), "Invalidation test route failed");
        var evaluation = new NativeJourneyGaze.Evaluation(); evaluation.Begin(native, 20); evaluation.Step(32);
        Check(native.Prepare(replacement, 1), "Replacement native route failed");
        evaluation.Step(32);
        Check(evaluation.Complete && !evaluation.Accepted && evaluation.CheckedIntervals == 32,
            "Verification silently continued across a changed native path");
        evaluation.Begin(native, 20); evaluation.Step(32); native.Reset(); evaluation.Step(32);
        Check(evaluation.Complete && !evaluation.Accepted, "Reset native path retained partial verification");
        Check(native.Prepare(first, 1), "Restart native route failed");
        evaluation.Begin(native, 20);
        Check(evaluation.CheckedIntervals == 0 && !evaluation.Complete && !evaluation.Accepted, "New route retained old verdict or progress");
        while (!evaluation.Complete) evaluation.Step(64);
        Check(evaluation.Accepted, "Restarted valid native route could not finish verification");
    }

    private static void InvalidTiming()
    {
        using var native = new ScenicSplinePath();
        var evaluation = new NativeJourneyGaze.Evaluation(); evaluation.Begin(native, 1);
        Check(evaluation.Complete && !evaluation.Accepted, "An unprepared native path was accepted");
        Check(native.Prepare(Build(22, Curve(P(0)), P(24, 10), 6, true), 1), "Timing test route failed");
        foreach (double duration in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            evaluation.Begin(native, duration);
            Check(evaluation.Complete && !evaluation.Accepted && evaluation.CheckedIntervals == 0,
                "Invalid duration entered or passed native gaze verification");
        }
    }

    private static void ObservedTerrainFocus()
    {
        var body = new AutonomyPosition[13]; for (int i = 0; i < body.Length; i++) body[i] = P(i * 4);
        Check(JourneyGazePolicy.TryBuild(body, P(24, 10), 6, null, null, out var aims, out var reason), reason);
        for (int i = 0; i < aims.Length; i++)
        {
            Check(JourneyLandscapeGaze.TryTerminate(body[i], aims[i], true, 14 + Math.Sin(i * Math.PI / 12) * 3,
                .3, out var observed), "Observed hill did not terminate its travel focus ray");
            aims[i] = observed;
        }
        var plan = ScenicShotPlan.Spatial(22, Feature(), ScenicShotRole.Traverse, body, aims, 61).AsJourney(19, 21, 8);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Native observed-landscape route failed");
        Check(NativeJourneyGaze.WithinRate(native, native.Length / 6), "Terrain termination introduced a fast native gaze turn");
        for (int i = 0; i <= 256; i++)
        {
            var frame = native.Sample(i / 256f);
            Check(frame.AimPosition.Distance(frame.CameraPosition) > 4 && frame.AimPosition.X > frame.CameraPosition.X,
                "Interpolated observed landscape gaze collapsed onto the camera or turned backwards");
            Check(Angle(frame, frame.WithAimPosition(P(frame.CameraPosition.X + 24, frame.CameraPosition.Y - 2))) < 2,
                "Native focus-range variation changed the purposeful outward view");
        }
    }

    private static void RecordedElevatedOpening()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RecordedFixture()));
        Check(document.RootElement.GetProperty("sourceVersion").GetString() == "0.8.49", "Recorded geometry fixture changed identity");
        JsonElement query = default;
        foreach (JsonElement item in document.RootElement.GetProperty("candidateQueries").EnumerateArray())
            if (item.GetProperty("query").GetInt32() == 27) query = item;
        Check(query.ValueKind == JsonValueKind.Object, "Elevated candidate27 is missing from the recorded failure");
        var samples = new List<JsonElement>(query.GetProperty("samples").EnumerateArray());
        Vector3 first = Vector(samples[0].GetProperty("camera"));
        Vector3 last = Vector(samples[samples.Count - 1].GetProperty("camera"));
        Check(samples[0].GetProperty("readable").GetBoolean() && !samples[samples.Count - 1].GetProperty("readable").GetBoolean() &&
            first.y - last.y > 9, "Fixture lost the readable opening followed by the measured downhill foliage rejection");

        // These are measured preflight poses, not the original authored knots.
        // Thin their observed horizontal footprint into a *new proposal* at
        // the readable opening's altitude. The trace has no terrain heights:
        // the constant lower floor below is a constructed math counterfactual,
        // never a claim that the recorded Unity scene is clear or traversable.
        var knots = new List<Vector3>(); var floors = new List<float>();
        for (int i = 0; i < samples.Count; i += 4)
        {
            Vector3 point = Vector(samples[i].GetProperty("camera")); point.y = first.y;
            knots.Add(point); floors.Add(first.y - 40);
        }
        if ((samples.Count - 1) % 4 != 0)
        { last.y = first.y; knots.Add(last); floors.Add(first.y - 40); }
        Check(JourneyTerrainProfile.TryFit(knots, floors, 18, 0, out var old, out string reason,
            preserveIncomingTangent: false), reason);
        Check(old[old.Length - 1].y < first.y - 9, "The constructed lower-surface fixture no longer reproduces floor-following descent");
        Check(JourneyTerrainProfile.TryFit(knots, floors, 18, 0, out var fitted, out reason,
            preserveIncomingTangent: false, cruiseAltitude: first.y), reason);
        var body = new AutonomyPosition[fitted.Length];
        for (int i = 0; i < body.Length; i++) body[i] = P(fitted[i].x, fitted[i].y, fitted[i].z);
        Vector3 recordedAim = Vector(samples[0].GetProperty("aim"));
        Check(JourneyGazePolicy.TryBuild(body, P(recordedAim.x, recordedAim.y, recordedAim.z), 12,
            null, null, out var aims, out reason), reason);
        var plan = ScenicShotPlan.Spatial(22, Feature(), ScenicShotRole.Traverse, body, aims,
            query.GetProperty("fov").GetSingle()).AsJourneyDeparture(19, 3);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Native elevated re-proposal failed");
        Check(NativeJourneyGaze.WithinRate(native, native.Length / 12 / ScenicJourneyMotion.CruiseFraction(true, false)),
            "Retaining the elevated datum introduced an excessive native gaze turn");
        for (int i = 0; i <= 256; i++)
            Check(Math.Abs(native.Sample(i / 256f).CameraPosition.Y - first.y) < .001,
                "Native execution reintroduced the recorded unwanted downhill movement");
    }

    private static Vector3 Vector(JsonElement value) => new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    private static void RecordedPrefixRegeneration()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RecordedFixture()));
        JsonElement recorded = document.RootElement.GetProperty("admittedRoute");
        JsonElement cameras = recorded.GetProperty("camera"), recordedAims = recorded.GetProperty("aim");
        var body = new AutonomyPosition[cameras.GetArrayLength()]; var aims = new AutonomyPosition[body.Length];
        for (int i = 0; i < body.Length; i++)
        {
            Vector3 camera = Vector(cameras[i]), aim = Vector(recordedAims[i]);
            body[i] = P(camera.x, camera.y, camera.z); aims[i] = P(aim.x, aim.y, aim.z);
        }
        // Unlike query34's evaluated samples, these are captured authored
        // body/aim knots. They can exercise the actual native slice machinery.
        // Subject visibility and terrain of this recorded world remain unknown.
        var original = ScenicShotPlan.Spatial(recorded.GetProperty("plan").GetInt64(), Feature(), ScenicShotRole.Traverse,
            body, aims, recorded.GetProperty("fov").GetSingle()).AsJourneyDeparture(recorded.GetProperty("journey").GetInt64(),
                recorded.GetProperty("duration").GetDouble());
        using var source = new ScenicSplinePath(); Check(source.Prepare(original, 1), "Recorded native route could not prepare");
        ScenicShotPlan disconnected = source.ProposeMeasuredSlice(.1f, .8f);
        Check(!JourneyReadablePrefix.TryRestore(original, disconnected, 0, source.SpatialTravelBetween(.1f, .8f), out _),
            "A disconnected native interval was admitted by relabeling its observed start as zero");
        ScenicShotPlan tooSmall = source.ProposeMeasuredSlice(0, .15f);
        double shortDistance = source.SpatialTravelBetween(0, .15f);
        Check(shortDistance < ScenicJourneyPolicy.MinimumWindowMetres, "Undersized fixture is not below the useful window bound");
        Check(shortDistance < ScenicJourneyPolicy.MinimumWindowMetres &&
            !JourneyReadablePrefix.TryRestore(original, tooSmall, 0, shortDistance, out _),
            "A native prefix shorter than the useful physical window was admitted");
        const float end = .8f;
        ScenicShotPlan slice = source.ProposeMeasuredSlice(0, end);
        Check(!JourneyReadablePrefix.TryRestore(original, slice, .1, source.SpatialTravelBetween(0, end), out _),
            "Nonzero observed start lost its disconnection evidence");
        Check(JourneyReadablePrefix.TryRestore(original, slice, 0, source.SpatialTravelBetween(0, end), out var proposal) && proposal != null,
            "Actual native prefix could not retain Journey ownership and its preserved incoming geometry");
        Check(proposal!.JourneyId == original.JourneyId && proposal.ContinuationOf == original.ContinuationOf &&
            proposal.CameraWaypoint(1).Distance(original.CameraWaypoint(1)) < .005 &&
            proposal.AimWaypoint(1).Distance(original.AimWaypoint(1)) < .005,
            "Prefix regeneration changed its exact incoming tangent or Journey identity");
        using var rebuilt = new ScenicSplinePath(); Check(rebuilt.Prepare(proposal, 1), "New prefix native geometry could not prepare");
        ScenicShotSample before = source.Sample(0), after = rebuilt.Sample(0), observedEnd = source.Sample(end), rebuiltEnd = rebuilt.Sample(1);
        Check(before.CameraPosition.Distance(after.CameraPosition) < .005 && before.AimPosition.Distance(after.AimPosition) < .005 &&
            before.ScreenX == after.ScreenX && before.ScreenY == after.ScreenY && proposal.FieldOfView == original.FieldOfView,
            "Regenerated prefix changed its exact entry composition");
        Check(observedEnd.CameraPosition.Distance(rebuiltEnd.CameraPosition) < .005 &&
            observedEnd.AimPosition.Distance(rebuiltEnd.AimPosition) < .005 && rebuilt.Length < source.Length,
            "Regeneration used an authored parameter in place of the measured native endpoint");
        // Begin a fresh native angular verification; successful proposal
        // construction alone is explicitly not a completed trajectory proof.
        var verification = new NativeJourneyGaze.Evaluation(); verification.Begin(rebuilt, proposal.DurationSeconds);
        Check(!verification.Complete && !verification.Accepted, "A regenerated prefix inherited an earlier native certificate");
        while (!verification.Complete) verification.Step(32);
        Check(verification.CheckedIntervals > 0, "The new trajectory bypassed native angular verification");
    }
    private static string RecordedFixture()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (DirectoryInfo? directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, "tests", "fixtures", "journey-mission", "0.8.49-planning-stall.json");
                if (File.Exists(path)) return path;
            }
        throw new FileNotFoundException("The sanitized0.8.49 Journey failure fixture was not found.");
    }
}
