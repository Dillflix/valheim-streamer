using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ConnectionRecoveryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Reconnect stays disabled until an authenticated session arms it", Disabled);
        yield return ("Reconnect backs off and permits only one active attempt", Backoff);
        yield return ("Reconnect times out an incomplete connection and increases delay", Timeout);
        yield return ("Repeated disconnect observations cannot postpone retry", RepeatedLoss);
        yield return ("Explicit stop clears recovery intent until rearmed", ManualStop);
        yield return ("Authorization rejection halts retries", Rejected);
        yield return ("Reconnect refuses a different world", WrongWorld);
        yield return ("Connection flapping does not reset retry pressure", Flapping);
        yield return ("Stable authorized connection resets retry pressure", Stable);
        yield return ("Long outages use a bounded burst followed by a quiet cooldown", Burst);
        yield return ("Reconnect clock rollback halts and invalid times fail", Clocks);
    }
    private static ConnectionRecoveryPolicy Armed()
    {
        var p = new ConnectionRecoveryPolicy(); p.Arm(0, 42); return p;
    }
    private static void Disabled()
    {
        var p = new ConnectionRecoveryPolicy(); p.Lost(0, "gone");
        Check(!p.BeginAttempt(1000) && !p.Armed && !p.Recovering);
    }
    private static void Backoff()
    {
        var p = Armed(); p.Lost(1, "gone");
        Check(p.NextAttemptAt == 6 && !p.BeginAttempt(5.9) && p.BeginAttempt(6));
        Check(!p.BeginAttempt(7) && p.Attempts == 1 && p.AttemptDeadline == 96);
    }
    private static void Timeout()
    {
        var p = Armed(); p.Lost(1, "gone"); p.BeginAttempt(6);
        Check(!p.TimedOut(95.9) && p.TimedOut(96));
        Check(p.NextAttemptAt == 106 && p.State == ConnectionRecoveryState.Backoff);
    }
    private static void RepeatedLoss()
    {
        var p = Armed(); p.Lost(1, "gone"); p.Lost(3, "again");
        Check(p.NextAttemptAt == 6 && p.LastFailure == "gone");
    }
    private static void ManualStop()
    {
        var p = Armed(); p.Lost(1, "gone"); p.Cancel();
        Check(!p.BeginAttempt(10) && !p.Connected(11, 42));
        p.Arm(12, 100); Check(p.WorldId == 100 && p.Attempts == 0 && p.Armed);
    }
    private static void Rejected()
    {
        var p = Armed(); p.Lost(1, "gone"); p.BeginAttempt(6); p.Halt("Denied");
        p.Lost(7, "gone"); Check(!p.BeginAttempt(10000) && !p.Armed && p.LastFailure == "Denied");
    }
    private static void WrongWorld()
    {
        var p = Armed(); p.Lost(1, "gone"); p.BeginAttempt(6);
        Check(!p.Connected(7, 43) && p.State == ConnectionRecoveryState.Halted);
    }
    private static void Flapping()
    {
        var p = Armed(); p.Lost(1, "gone"); p.BeginAttempt(6); Check(p.Connected(7, 42));
        p.Lost(8, "gone"); Check(p.NextAttemptAt == 18);
    }
    private static void Stable()
    {
        var p = Armed(); p.Lost(1, "gone"); p.BeginAttempt(6); p.Connected(7, 42);
        p.Connected(67, 42); p.Lost(68, "gone"); Check(p.NextAttemptAt == 73);
    }
    private static void Burst()
    {
        var p = Armed(); double now = 1; p.Lost(now, "gone");
        for (int i = 0; i < ConnectionRecoveryPolicy.BurstLimit; i++)
        {
            now = p.NextAttemptAt; Check(p.BeginAttempt(now));
            now += ConnectionRecoveryPolicy.AttemptSeconds; Check(p.TimedOut(now));
        }
        Check(p.Attempts == ConnectionRecoveryPolicy.BurstLimit && p.NextAttemptAt == now + 900);
        now = p.NextAttemptAt; Check(p.BeginAttempt(now));
        now += ConnectionRecoveryPolicy.AttemptSeconds; p.TimedOut(now); Check(p.NextAttemptAt == now + 10);
    }
    private static void Clocks()
    {
        var p = Armed(); p.Connected(100, 42); p.Lost(99, "gone");
        Check(p.State == ConnectionRecoveryState.Halted && !p.Armed);
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { new ConnectionRecoveryPolicy().Arm(invalid, 42); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected);
        }
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Connection recovery invariant failed"); }
}
