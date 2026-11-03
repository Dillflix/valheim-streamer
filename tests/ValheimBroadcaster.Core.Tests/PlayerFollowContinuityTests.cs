using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class PlayerFollowContinuityTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Player continuation belongs to the presented character binding", Binding);
        yield return ("Sustained walking and sprinting renew continuation over kilometres", SustainedMovement);
        yield return ("Continuation rejects horizontal and vertical relocation", Discontinuity);
        yield return ("Readiness probes cannot keep unpresented footage alive", PresentationExpiry);
        yield return ("Invalid presentation evidence clears continuation", InvalidEvidence);
        yield return ("Related views retain only a bounded already-presented player binding", RelatedView);
    }

    private static void RelatedView()
    {
        var c = new PlayerFollowContinuity();
        Check(!c.ValidatePreparedReturn("player", "character:1", Start, 10));
        c.Presented("player", "character:1", Start, 10);
        for (int i = 1; i <= 8; i++)
        {
            double now = 10 + i * .5;
            Check(c.ValidatePreparedReturn("player", "character:1", Start, now));
            Check(c.CanContinue("player", "character:1", Start, now));
        }
        Check(!c.ValidatePreparedReturn("player", "character:1", Start, 14.01));
        Check(!c.CanContinue("player", "character:1", Start, 14.01));
        c.Presented("player", "character:1", Start, 15);
        Check(!c.ValidatePreparedReturn("player", "respawn", Start, 15.1));
        Check(!c.ValidatePreparedReturn("player", "character:1", new AutonomyPosition(1000, 30, 40), 15.1));
        Check(!c.ValidatePreparedReturn("player", "character:1", Start, 16.01));
        c.Presented("player", "character:1", Start, 20);
        Check(c.ValidatePreparedReturn("player", "character:1", Start, 20.5));
        c.Presented("player", "character:1", Start, 21);
        Check(c.CanContinue("player", "character:1", Start, 21.5));
    }

    private static readonly AutonomyPosition Start = new AutonomyPosition(20, 30, 40);
    private static void Check(bool condition) { if (!condition) throw new Exception("Player follow continuity assertion failed."); }

    private static void Binding()
    {
        var c = new PlayerFollowContinuity();
        Check(!c.CanContinue("player", "character:1", Start, 10));
        c.Presented("player", "character:1", Start, 10);
        Check(c.CanContinue("player", "character:1", Start, 10.1));
        Check(!c.CanContinue("other-player", "character:1", Start, 10.2));
        Check(!c.CanContinue("player", "respawn:2", Start, 10.2));
        Check(c.CanContinue("player", "character:1", Start, 10.3));
        c.Reset();
        Check(!c.CanContinue("player", "character:1", Start, 10.4));
    }

    private static void SustainedMovement()
    {
        var c = new PlayerFollowContinuity();
        c.Presented("runner", "character:1", Start, 0);
        float x = Start.X;
        double now = 0;
        for (int tick = 0; tick < 3000; tick++)
        {
            // Alternate walking/sprinting and cross many 64-metre zone boundaries.
            x += tick % 100 < 50 ? .6f : 2f;
            now += .2;
            var next = new AutonomyPosition(x, 30 + (float)Math.Sin(tick * .02), 40);
            Check(c.CanContinue("runner", "character:1", next, now));
            c.Presented("runner", "character:1", next, now);
        }
        Check(x - Start.X > 1000);
        var stopped = new AutonomyPosition(x, 30, 40);
        for (int tick = 0; tick < 100; tick++)
        {
            now += .2;
            Check(c.CanContinue("runner", "character:1", stopped, now));
            c.Presented("runner", "character:1", stopped, now);
        }
    }

    private static void Discontinuity()
    {
        var c = new PlayerFollowContinuity();
        c.Presented("player", "character:1", Start, 10);
        Check(c.CanContinue("player", "character:1", new AutonomyPosition(36, 30, 40), 10.1));
        Check(!c.CanContinue("player", "character:1", new AutonomyPosition(37, 30, 40), 10.1));
        Check(!c.CanContinue("player", "character:1", new AutonomyPosition(20, 47, 40), 10.1));
        Check(!c.CanContinue("player", "character:1", new AutonomyPosition(32, 42, 40), 10.1));
        var dungeon = new AutonomyPosition(20, 3500, 40);
        Check(!c.CanContinue("player", "character:1", dungeon, 10.1));
        // Full reacquisition may establish the same character at a new location.
        c.Presented("player", "character:1", dungeon, 11);
        Check(c.CanContinue("player", "character:1", dungeon, 11.1));
    }

    private static void PresentationExpiry()
    {
        var c = new PlayerFollowContinuity();
        c.Presented("player", "character:1", Start, 10);
        for (int tick = 1; tick <= 10; tick++)
            Check(c.CanContinue("player", "character:1", Start, 10 + tick * .1));
        Check(!c.CanContinue("player", "character:1", Start, 11.001));
        Check(!c.CanContinue("player", "character:1", Start, 9.99));
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1 })
            Check(!c.CanContinue("player", "character:1", Start, bad));
    }

    private static void InvalidEvidence()
    {
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1, 9.99 })
        {
            var c = new PlayerFollowContinuity();
            c.Presented("player", "character:1", Start, 10);
            bool rejected = false;
            try { c.Presented("player", "character:1", Start, bad); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && !c.CanContinue("player", "character:1", Start, 10.1));
        }
        var invalidId = new PlayerFollowContinuity();
        invalidId.Presented("player", "character:1", Start, 10);
        bool failed = false;
        try { invalidId.Presented("player", "", Start, 10.1); }
        catch (ArgumentException) { failed = true; }
        Check(failed && !invalidId.CanContinue("player", "character:1", Start, 10.2));
    }
}
