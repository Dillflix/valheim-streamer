using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyDepartureSpaceTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey departure explores different bodies and gaze instead of repeating the recorded terrain-blocked opening", RecordedOpening);
        yield return ("Journey opening search covers bounded elevations and bearings toward the destination", Variety);
        yield return ("Unknown departure space can mature without being remembered as blocked or busy-looped", Unknown);
        yield return ("Terrain-fitted equivalent departure openings are screened once per search", Equivalent);
        yield return ("Journey departure search has finite exhaustion and resets with scene ownership", Lifetime);
        yield return ("Continuation gaze alternatives preserve the seam and native angular rate", GazeAlternatives);
        yield return ("Journey gaze can show an observed hillside without requiring empty air through it", LandscapeSurface);
        yield return ("Journey landscape framing cannot turn an obstruction or unobserved hit into a subject", LandscapeRejects);
    }
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static AutonomyPosition P(float x, float y = 20, float z = 0) => new AutonomyPosition(x, y, z);

    private static void RecordedOpening()
    {
        var seed = new AutonomyPosition(-960.9389038f, 73.10746765f, 169.0019531f);
        var goal = new AutonomyPosition(-4380, 60, 2612);
        var search = new JourneyDepartureSpace(); var first = JourneyDepartureSpace.Propose(0, seed, goal, 12);
        Check(first.Position.Distance(seed) == 0, "The recorded starting view was not represented");
        Check(search.TryNext(0, out int initial) && initial == 0, "Search skipped its observed seed");
        var initialAim = first.AimAt(first.Position);
        Check(search.RememberMeasured(first.Position, initialAim), "First observation was already retired");
        search.Complete(initial, false); // supplied trace: terrain blocks this exact opening
        var bodies = new HashSet<string>();
        for (int i = 1; i < 12; i++)
        {
            Check(search.TryNext(i * .02, out int next) && next != 0, "A known blocked opening was retried under another route alternative");
            var proposal = JourneyDepartureSpace.Propose(next, seed, goal, 12);
            Check(proposal.Position.Distance(first.Position) >= 4, "Alternative retained the identical blocked camera origin");
            bodies.Add(proposal.Position.X + "/" + proposal.Position.Y + "/" + proposal.Position.Z);
            search.Complete(next, false);
        }
        Check(bodies.Count == 11, "Distinct labels concealed duplicate opening positions");
    }

    private static void Variety()
    {
        var seed = P(0); var goal = P(1000); var body = new HashSet<string>(); var gaze = new HashSet<string>();
        bool raisedEarly = false;
        for (int id = 0; id < JourneyDepartureSpace.CandidateCount; id++)
        {
            var p = JourneyDepartureSpace.Propose(id, seed, goal, 12);
            Check(p.Position.Distance(seed) <= JourneyDepartureSpace.MaximumOriginRelocation,
                "An opening escaped the declared local relocation budget");
            Check(p.Heading.X > .9 && Math.Abs(p.Heading.Y) < .001 && Math.Abs(p.FocusRange - 28) < .001,
                "Departure gaze no longer develops the selected destination");
            string key = p.Position.X + "/" + p.Position.Y + "/" + p.Position.Z;
            body.Add(key); gaze.Add(key + "/" + p.Heading.X + "/" + p.Heading.Z);
            if (id < 4 && p.Position.Y >= seed.Y + 8) raisedEarly = true;
        }
        Check(body.Count == 36 && gaze.Count == JourneyDepartureSpace.CandidateCount && raisedEarly,
            "Opening portfolio omits early elevation or repeats its position/gaze candidates");
    }

    private static void Unknown()
    {
        var search = new JourneyDepartureSpace(); Check(search.TryNext(1, out int first), "No initial proposal");
        search.Defer(first, 1);
        for (int i = 1; i < JourneyDepartureSpace.CandidateCount; i++)
        { Check(search.TryNext(1.1, out int next) && next != first, "Unknown proposal monopolized observation work"); search.Complete(next, false); }
        Check(!search.TryNext(1.49, out _), "Unknown observation retried without its bounded defer interval");
        Check(search.TryNext(1.5, out int resumed) && resumed == first, "Unknown space became permanently rejected");
        search.Complete(resumed, true);
        Check(search.Rejected == JourneyDepartureSpace.CandidateCount - 1 && search.Screened == JourneyDepartureSpace.CandidateCount,
            "A delayed clear observation was counted as a physical obstruction");
    }

    private static void Equivalent()
    {
        var search = new JourneyDepartureSpace();
        Check(!search.WasMeasured(P(0), P(28, 18)), "Unobserved opening was treated as a duplicate");
        Check(search.RememberMeasured(P(0), P(28, 18)), "First measured opening lost");
        Check(search.WasMeasured(P(.01f), P(28.01f, 18)), "Previously measured opening requires repeating the full physics screen");
        Check(!search.RememberMeasured(P(.01f), P(28.01f, 18)), "Terrain fitting recreated the same known opening");
        Check(search.RememberMeasured(P(0, 28), P(28, 26)), "Different elevation was incorrectly suppressed");
        Check(search.RememberMeasured(P(0), P(28, 18, 8)), "Different forward composition was incorrectly suppressed");
    }

    private static void Lifetime()
    {
        var search = new JourneyDepartureSpace(); int count = 0;
        while (search.TryNext(2, out int id)) { search.Complete(id, false); count++; Check(count <= JourneyDepartureSpace.CandidateCount, "Unbounded search"); }
        Check(count == JourneyDepartureSpace.CandidateCount, "Candidate exhaustion occurred early");
        search.Reset(); Check(search.Screened == 0 && search.Rejected == 0 && search.TryNext(0, out int reset) && reset == 0,
            "New scene inherited old blocked openings");
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false; try { search.TryNext(invalid, out _); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Nonfinite observation clock entered the scheduler");
        }
    }

    private static void GazeAlternatives()
    {
        var points = new AutonomyPosition[13]; for (int i = 0; i < points.Length; i++) points[i] = P(i * 4);
        var seam = P(28, 18);
        foreach (double speed in new[] { 2d, 6, 12 })
        foreach (double yaw in new[] { -12d, 0, 12 })
        {
            Check(JourneyGazePolicy.TryBuild(points, seam, speed, null, null, out var aims, out _, yaw, 3),
                "A bounded continuation view could not be constructed");
            Check(aims[0].Distance(seam) == 0, "Alternative gaze changed the accepted seam");
            for (int i = 1; i < points.Length; i++)
            {
                double ax = aims[i - 1].X - points[i - 1].X, ay = aims[i - 1].Y - points[i - 1].Y, az = aims[i - 1].Z - points[i - 1].Z;
                double bx = aims[i].X - points[i].X, by = aims[i].Y - points[i].Y, bz = aims[i].Z - points[i].Z;
                double cosine = (ax * bx + ay * by + az * bz) / Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, cosine))) * 180 / Math.PI;
                Check(angle <= JourneyGazePolicy.DegreesPerSecond * 4 / speed + .002,
                    "Context variation jumped the gaze instead of respecting cinematic angular motion");
            }
            Check(yaw == 0 || Math.Sign(aims[12].Z) == -Math.Sign(yaw), "Context alternative did not develop its deliberate bearing");
        }
        Check(!JourneyGazePolicy.TryBuild(points, seam, 6, null, null, out _, out _, double.NaN), "Unbounded gaze input accepted");
    }

    private static void LandscapeSurface()
    {
        // The recorded departure was blocked along this outward ray. Measured
        // terrain is legitimate subject matter, while the camera corridor still
        // needs its separate complete surface and vegetation certificate.
        var camera = new AutonomyPosition(-960.9389038f, 73.10746765f, 169.0019531f);
        var desired = new AutonomyPosition(-983.720f, 71.107f, 185.281f);
        Check(JourneyLandscapeGaze.TryTerminate(camera, desired, true, 10, .3, out var focus),
            "A measured hillside still requires visibility of fictitious air behind the ground");
        Check(Math.Abs(camera.Distance(focus) - 9.7) < .0001 && camera.Distance(focus) >= 2,
            "Observed surface framing moved into terrain or onto the near plane");
        double factor = camera.Distance(focus) / camera.Distance(desired);
        Check(Math.Abs((focus.X - camera.X) - (desired.X - camera.X) * factor) < .0001 &&
            Math.Abs((focus.Y - camera.Y) - (desired.Y - camera.Y) * factor) < .0001 &&
            Math.Abs((focus.Z - camera.Z) - (desired.Z - camera.Z) * factor) < .0001,
            "Terminating the gaze changed the authored view direction/pitch");
    }

    private static void LandscapeRejects()
    {
        var camera = P(0); var desired = P(28, 18);
        Check(!JourneyLandscapeGaze.TryTerminate(camera, desired, false, 12, .3, out var blocked) && blocked.Distance(desired) == 0,
            "A foreground tree, building or incomplete hit list became authorized landscape");
        foreach (double distance in new[] { double.NaN, double.PositiveInfinity, -1d, 0d, 1.9, 3.99, 29 })
            Check(!JourneyLandscapeGaze.TryTerminate(camera, desired, true, distance, .3, out var unchanged) && unchanged.Distance(desired) == 0,
                "Near-lens, missing or beyond-focus terrain supplied an unsafe focus");
        foreach (double clip in new[] { double.NaN, double.PositiveInfinity, -1d, 0d, 4d })
            Check(!JourneyLandscapeGaze.TryTerminate(camera, desired, true, 10, clip, out _),
                "An invalid or enclosing near clip admitted a landscape subject");
        Check(!JourneyLandscapeGaze.TryTerminate(camera, camera, true, 10, .3, out _), "A degenerate view became landscape framing");
    }
}
