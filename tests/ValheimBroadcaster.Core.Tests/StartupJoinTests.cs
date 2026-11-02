using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class StartupJoinTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Startup cannot join without its explicit opt-in", Disabled);
        yield return ("Startup accepts only one in-flight native join", OneAttempt);
        yield return ("Startup timeouts respect exponential retry deadlines", Backoff);
        yield return ("Startup exhausts a finite attempt budget and stays blocked", Bounded);
        yield return ("Manual startup cancellation cannot be revived by a timer", Cancelled);
        yield return ("Terminal startup rejection cannot restart itself", Rejected);
        yield return ("Only an active startup attempt can receive authorization", Authorized);
        yield return ("Startup timing rejects clock regression and invalid time", Clocks);
        yield return ("Startup profile identity rejects paths and preserves exact filenames", Profiles);
        yield return ("Startup requires an explicit valid dedicated Steam host and port", Addresses);
    }

    private static void Disabled()
    {
        var p = new StartupJoinPolicy(false);
        Check(!p.Pending && !p.Begin(0) && !p.TimedOut(10000) && p.State == StartupJoinState.Disabled);
    }
    private static void OneAttempt()
    {
        var p = new StartupJoinPolicy(true);
        Check(p.Pending && p.Begin(0) && !p.Begin(1) && p.Attempts == 1);
        Check(!p.TimedOut(89.99) && p.Deadline == 90);
    }
    private static void Backoff()
    {
        var p = new StartupJoinPolicy(true);
        p.Begin(0); Check(p.TimedOut(90) && p.NextAttemptAt == 95);
        Check(!p.TimedOut(91) && !p.Begin(94.99) && p.Begin(95));
        Check(p.TimedOut(185) && p.NextAttemptAt == 195);
        Check(!p.Begin(194.99) && p.Begin(195));
        Check(p.TimedOut(285) && p.NextAttemptAt == 305);
    }
    private static void Bounded()
    {
        var p = new StartupJoinPolicy(true);
        double now = 0;
        for (int i = 0; i < StartupJoinPolicy.MaximumAttempts; i++)
        {
            Check(p.Begin(now));
            now += StartupJoinPolicy.AttemptSeconds;
            Check(p.TimedOut(now));
            if (p.Pending) now = p.NextAttemptAt;
        }
        Check(p.Attempts == StartupJoinPolicy.MaximumAttempts && p.State == StartupJoinState.Blocked);
        Check(!p.Pending && !p.Begin(now + 10000) && p.Failure == "attempts_exhausted");
    }
    private static void Cancelled()
    {
        foreach (bool connecting in new[] { false, true })
        {
            var p = new StartupJoinPolicy(true);
            if (connecting) p.Begin(0);
            p.Cancel(); p.Cancel();
            Check(p.State == StartupJoinState.Cancelled && !p.Begin(10000) && !p.TimedOut(10001));
        }
    }
    private static void Rejected()
    {
        var p = new StartupJoinPolicy(true); p.Begin(0); p.Block("authentication");
        Check(!p.Begin(10000) && !p.TimedOut(10001) && p.Failure == "authentication");
    }
    private static void Authorized()
    {
        var p = new StartupJoinPolicy(true);
        bool failed = false;
        try { p.Complete(); } catch (InvalidOperationException) { failed = true; }
        Check(failed);
        p.Begin(0); p.Complete();
        Check(!p.Pending && !p.Begin(100) && !p.TimedOut(10000) && p.State == StartupJoinState.Complete);
    }
    private static void Clocks()
    {
        foreach (double time in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            bool failed = false;
            try { new StartupJoinPolicy(true).Begin(time); } catch (ArgumentOutOfRangeException) { failed = true; }
            Check(failed);
        }
        var p = new StartupJoinPolicy(true); p.Begin(10);
        bool regressed = false;
        try { p.TimedOut(9); } catch (ArgumentOutOfRangeException) { regressed = true; }
        Check(regressed);
    }
    private static void Profiles()
    {
        foreach (string value in new[] { "", " ", ".", "..", "../observer", "C:\\observer", "folder/observer", "observer.fch", "observer.FCH", "observer\0", "observer\r", "observer\n", "observer ", " observer", new string('x', 129) })
            Check(!StartupJoinPolicy.ValidProfile(value));
        Check(StartupJoinPolicy.ValidProfile("Observer") && StartupJoinPolicy.ValidProfile("Observer Two") && StartupJoinPolicy.ValidProfile("Ørn"));
    }
    private static void Addresses()
    {
        Check(StartupJoinPolicy.TryAddress("server.example:2456", out string host, out ushort port) && host == "server.example" && port == 2456);
        Check(StartupJoinPolicy.TryAddress("192.168.1.2:65535", out _, out port) && port == 65535);
        foreach (string value in new[] { "", "server.example", "server.example:0", "server.example:65536", "server.example:-1", "server.example: 2456", " server.example:2456", "server.example:2456 ", "https://server.example:2456", "[::1]:2456", "123456", "server.example:2456\n" })
            Check(!StartupJoinPolicy.TryAddress(value, out _, out _));
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Startup join invariant failed"); }
}
