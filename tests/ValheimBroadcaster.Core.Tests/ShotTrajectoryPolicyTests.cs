using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ShotTrajectoryPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("World trajectory does not copy a player's movement onset", MovementOnset);
        yield return ("World trajectory families keep bounded lag during three minutes of sprinting", SustainedSprint);
        yield return ("World trajectory reversal and stopping preserve bounded acceleration", ReverseAndStop);
        yield return ("Confined framing pressure changes continuously and settles after stopping", ConfinedPressure);
        yield return ("Activity-area trajectory keeps a stable world origin during local work", ActivityArea);
        yield return ("Group trajectory follows common motion instead of one member's local movement", GroupMotion);
        yield return ("Shot and group changes preserve trajectory position and velocity continuity", ModeAndCenterChanges);
        yield return ("Standby and variable frame cadence preserve world trajectory timing", Cadence);
        yield return ("World trajectory preview is read-only and rejected paths cannot advance secretly", PreviewAndConstraint);
        yield return ("World trajectory resets only for explicit lifecycle and discontinuity boundaries", ResetBoundaries);
    }

    private static AutonomyPosition P(double x = 0, double y = 0, double z = 0) =>
        new AutonomyPosition((float)x, (float)y, (float)z);
    private static double Length(AutonomyPosition value) => value.Distance(P());
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static ShotTrajectoryState Tick(ShotTrajectoryPolicy policy, double now, double x, double speed,
        ShotTrajectoryKind kind = ShotTrajectoryKind.Tracking, CinematicActivity activity = CinematicActivity.Travel,
        long shot = 0, float lag = 8, double dt = 1d / 60, float pressure = 0, float freedom = 1) =>
        policy.Step(now, dt, P(x), P(x), P(speed), activity, kind, shot, lag, pressure, freedom);

    private static void MovementOnset()
    {
        var policy = new ShotTrajectoryPolicy();
        Tick(policy, 0, 0, 0);
        ShotTrajectoryState state = Tick(policy, 1d / 60, 1, 8);
        Check(state.Anchor.X > 0 && state.Anchor.X < 0.01,
            "A one-metre actor movement was copied directly to the camera body.");
        Check(state.Velocity.X <= ShotTrajectoryPolicy.MaximumAcceleration / 60 + 0.001,
            "Movement onset bypassed the acceleration bound.");
        var establish = new ShotTrajectoryPolicy();
        Tick(establish, 0, 0, 0, ShotTrajectoryKind.Establish);
        ShotTrajectoryState anchored = Tick(establish, 1d / 60, 1, 8, ShotTrajectoryKind.Establish);
        Check(anchored.Velocity.X < state.Velocity.X * 0.2,
            "An establishing shot behaves identically to tracking.");
    }

    private static void SustainedSprint()
    {
        foreach (ShotTrajectoryKind kind in new[] { ShotTrajectoryKind.Establish, ShotTrajectoryKind.Arc,
            ShotTrajectoryKind.Dolly, ShotTrajectoryKind.Tracking, ShotTrajectoryKind.ActivityArea })
        {
            var policy = new ShotTrajectoryPolicy();
            ShotTrajectoryState state = Tick(policy, 0, 0, 8, kind);
            double maximumLag = 0;
            for (int i = 1; i <= 10800; i++)
            {
                state = Tick(policy, i / 60d, i * 8d / 60, 8, kind);
                maximumLag = Math.Max(maximumLag, state.Lag);
                Check(!state.WasReset && Length(state.Velocity) <= ShotTrajectoryPolicy.MaximumSpeed + 0.001,
                    "Sustained travel reset or exceeded the velocity limit.");
            }
            Check(maximumLag < 12, kind + " allowed sprinting subjects to outrun coverage: " + maximumLag);
            Check(Math.Abs(state.Velocity.X - 8) < 0.15,
                kind + " failed to match sustained average speed.");
        }
    }

    private static void ReverseAndStop()
    {
        var policy = new ShotTrajectoryPolicy();
        ShotTrajectoryState previous = Tick(policy, 0, 0, 8);
        double position = 0;
        double firstReverseVelocity = 0;
        for (int i = 1; i <= 1800; i++)
        {
            double speed = i <= 300 ? 8 : i <= 600 ? -8 : 0;
            position += speed / 60;
            ShotTrajectoryState current = Tick(policy, i / 60d, position, speed);
            Check(current.Velocity.Distance(previous.Velocity) <= ShotTrajectoryPolicy.MaximumAcceleration / 60 + 0.0001,
                "Stop/reversal changed body velocity instantaneously.");
            Check(current.Anchor.Distance(previous.Anchor) <= ShotTrajectoryPolicy.MaximumSpeed / 60 + 0.001,
                "Stop/reversal changed body position instantaneously.");
            if (i == 301) firstReverseVelocity = current.Velocity.X;
            previous = current;
        }
        Check(firstReverseVelocity > 0, "A target reversal was copied without trajectory inertia.");
        Check(Math.Abs(previous.Anchor.X - position) < 0.05 && Length(previous.Velocity) < 0.05,
            "Tracking body failed to settle after the subject stopped.");
    }

    private static void ActivityArea()
    {
        var policy = new ShotTrajectoryPolicy();
        policy.Step(0, 1d / 60, P(), P(), P(), CinematicActivity.Work,
            ShotTrajectoryKind.ActivityArea, 0, 8, 0, 1);
        double maximumDisplacement = 0;
        for (int i = 1; i <= 3600; i++)
        {
            double now = i / 60d;
            double x = 2 * Math.Sin(now * 0.5), z = 2 * (Math.Cos(now * 0.5) - 1);
            ShotTrajectoryState state = policy.Step(now, 1d / 60, P(x, 0, z), P(x, 0, z),
                P(Math.Cos(now * 0.5), 0, -Math.Sin(now * 0.5)), CinematicActivity.Work,
                ShotTrajectoryKind.ActivityArea, 0, 10, 0, 1);
            maximumDisplacement = Math.Max(maximumDisplacement, Length(state.Anchor));
        }
        Check(maximumDisplacement < 0.65,
            "Small work-area movements dragged the camera body's world origin around: " + maximumDisplacement);
    }

    private static void ConfinedPressure()
    {
        var policy = new ShotTrajectoryPolicy();
        ShotTrajectoryState previous = Tick(policy, 0, 0, 4, ShotTrajectoryKind.Dolly, lag: 1.5f, freedom: 0.2f);
        double position = 0, maximumLag = 0;
        double latestMinimum = double.PositiveInfinity, latestMaximum = double.NegativeInfinity;
        for (int i = 1; i <= 2400; i++)
        {
            double now = i / 60d;
            double speed = now < 20 ? 4 : 0;
            position += speed / 60;
            // Pressure rises and falls during movement, then remains high
            // after stopping: it must not create a sign-flipping limit cycle.
            float pressure = now < 5 ? 0 : now < 7 ? (float)((now - 5) / 2) :
                now < 10 ? 1 : now < 12 ? (float)((12 - now) / 2) : now < 18 ? 0 : 1;
            ShotTrajectoryState current = Tick(policy, now, position, speed,
                ShotTrajectoryKind.Dolly, lag: 1.5f, pressure: pressure, freedom: 0.2f);
            maximumLag = Math.Max(maximumLag, current.Lag);
            Check(current.Velocity.Distance(previous.Velocity) <= ShotTrajectoryPolicy.MaximumAcceleration / 60 + 0.0001,
                "Changing framing pressure bypassed bounded acceleration.");
            if (now > 35)
            {
                latestMinimum = Math.Min(latestMinimum, current.Anchor.X);
                latestMaximum = Math.Max(latestMaximum, current.Anchor.X);
                Check(Length(current.Velocity) < 0.02, "Persistent pressure caused camera jitter after stopping.");
            }
            previous = current;
        }
        Check(maximumLag < 3.5, "A cramped moving shot diverged from its subject: " + maximumLag);
        Check(latestMaximum - latestMinimum < 0.01,
            "A stationary subject under framing pressure caused an oscillating body path.");
    }

    private static void GroupMotion()
    {
        var group = new ShotTrajectoryPolicy();
        var reference = new ShotTrajectoryPolicy();
        for (int i = 0; i <= 2400; i++)
        {
            double now = i / 60d;
            AutonomyPosition center = P(now * 5);
            ShotTrajectoryState actual = group.Step(now, 1d / 60, P(now * 5, 0, 3 * Math.Sin(now)),
                center, P(5), CinematicActivity.Travel, ShotTrajectoryKind.Tracking, 0, 8, 0, 1);
            ShotTrajectoryState expected = reference.Step(now, 1d / 60, center, center, P(5),
                CinematicActivity.Travel, ShotTrajectoryKind.Tracking, 0, 8, 0, 1);
            Check(actual.Anchor.Distance(expected.Anchor) < 0.0001,
                "Group framing copied one member's local movement instead of common motion.");
        }
    }

    private static void ModeAndCenterChanges()
    {
        var policy = new ShotTrajectoryPolicy();
        ShotTrajectoryState previous = Tick(policy, 0, 0, 5);
        for (int i = 1; i <= 720; i++)
        {
            double now = i / 60d;
            bool changed = i >= 300;
            // The actor remains continuous while a companion changes the scene
            // center and the director begins a different camera treatment.
            ShotTrajectoryState current = policy.Step(now, 1d / 60, P(now * 5),
                P(now * 5, 0, changed ? 5 : 0), P(5), CinematicActivity.Travel,
                changed ? ShotTrajectoryKind.Dolly : ShotTrajectoryKind.Tracking,
                changed ? 1 : 0, 8, 0, 1);
            Check(!current.WasReset, "A group-center or shot change was mistaken for a teleport.");
            Check(current.Anchor.Distance(previous.Anchor) <= ShotTrajectoryPolicy.MaximumSpeed / 60 + 0.0001 &&
                current.Velocity.Distance(previous.Velocity) <= ShotTrajectoryPolicy.MaximumAcceleration / 60 + 0.0001,
                "A shot/group change discarded body position or velocity continuity.");
            previous = current;
        }
    }

    private static ShotTrajectoryState RunCadence(double[] intervals, double passedDt)
    {
        var policy = new ShotTrajectoryPolicy();
        ShotTrajectoryState state = Tick(policy, 0, 0, 7, ShotTrajectoryKind.Arc);
        double now = 0;
        int index = 0;
        while (now < 20)
        {
            now = Math.Min(20, now + intervals[index++ % intervals.Length]);
            state = Tick(policy, now, now * 7, 7, ShotTrajectoryKind.Arc, dt: passedDt);
        }
        return state;
    }
    private static void Cadence()
    {
        ShotTrajectoryState regular = RunCadence(new[] { 1d / 60 }, 1d / 60);
        ShotTrajectoryState variable = RunCadence(new[] { 0.008, 0.03, 0.016, 0.041 }, 1d / 60);
        ShotTrajectoryState standby = RunCadence(new[] { 0.2 }, 1d / 60);
        ShotTrajectoryState highFps = RunCadence(new[] { 1d / 240 }, 1d / 240);
        foreach (ShotTrajectoryState state in new[] { variable, standby, highFps })
            Check(state.Anchor.Distance(regular.Anchor) < 0.04 && state.Velocity.Distance(regular.Velocity) < 0.04,
                "Standby/frame cadence changed the planned world trajectory: position " +
                state.Anchor.Distance(regular.Anchor) + ", velocity " + state.Velocity.Distance(regular.Velocity));
    }

    private static void PreviewAndConstraint()
    {
        var policy = new ShotTrajectoryPolicy();
        ShotTrajectoryState actual = Tick(policy, 0, 0, 6);
        for (int i = 1; i <= 120; i++) actual = Tick(policy, i / 60d, i * 6d / 60, 6);
        ShotTrajectoryState predicted = policy.Preview(0.5);
        Check(predicted.Anchor.X > actual.Anchor.X, "Preview failed to develop the independent trajectory.");
        ShotTrajectoryState unchanged = Tick(policy, 2, 12, 6);
        Check(unchanged.Anchor.Distance(actual.Anchor) < 0.0001,
            "Preview secretly advanced live body state.");
        AutonomyPosition accepted = P(actual.Anchor.X - 0.5);
        policy.Synchronize(accepted, true);
        Check(policy.Preview(0.5).Anchor.Distance(accepted) < 0.0001,
            "A rejected path continued advancing behind the retained camera.");
        ShotTrajectoryState resumed = Tick(policy, 2 + 1d / 60, 12.1, 6);
        Check(resumed.Anchor.Distance(accepted) < 0.01 && Length(resumed.Velocity) < 0.5,
            "Motion resumed from hidden rejected state instead of the accepted anchor.");
    }

    private static void ResetBoundaries()
    {
        var policy = new ShotTrajectoryPolicy();
        Tick(policy, 0, 0, 0);
        ShotTrajectoryState gap = Tick(policy, 2, 8, 8);
        Check(gap.WasReset && gap.Anchor.Distance(P(8)) == 0 && Length(gap.Velocity) == 0,
            "A long observation gap extrapolated stale body motion.");
        ShotTrajectoryState teleport = Tick(policy, 2.1, 100, 8);
        Check(teleport.WasReset && teleport.Anchor.Distance(P(100)) == 0 && Length(teleport.Velocity) == 0,
            "A teleport was interpreted as extreme camera velocity.");
        ShotTrajectoryState paused = Tick(policy, 2.2, 101, 8, dt: 0);
        Check(paused.Anchor.Distance(teleport.Anchor) == 0, "Paused camera state advanced.");
        policy.Reset();
        ShotTrajectoryState restarted = Tick(policy, 0, -20, 0);
        Check(restarted.WasReset && restarted.Anchor.X == -20,
            "A new binding inherited previous-world trajectory state.");
    }
}
