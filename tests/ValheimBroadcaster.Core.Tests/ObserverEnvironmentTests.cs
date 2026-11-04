using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ObserverEnvironmentTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Environment wire preserves world/request identity and pending ordinary peers", RoundTrip);
        yield return ("Environment protocol rejects malformed envelopes and unsolicited grants", Malformed);
        yield return ("Environment leases expire from send time, not delayed response arrival", SendTimeExpiry);
        yield return ("Environment server push revokes immediately and stale grants cannot revive it", Revocation);
        yield return ("Environment permissions reject wrong worlds and unmatched requests", Identity);
        yield return ("Environment permissions fail closed on clock reversal and reset", ClockReset);
        yield return ("Environment cannot grant when server disabled or an ordinary player is joining", Blocking);
        yield return ("Environment timed-out request cannot become a fresh permit", DelayedReply);
        yield return ("Visual environment cycle begins at current phase and wraps smoothly", Phase);
        yield return ("Visual environment pause/stall never stores catch-up movement", Pause);
        yield return ("Visual environment settings reject invalid or nonfinite cycles", InvalidCycle);
    }

    private static void Check(bool value) { if (!value) throw new Exception("Environment assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid environment input was accepted.");
    }
    private static ObserverEnvironmentSnapshot Reply(long sequence, long request, bool enabled = true, int ordinary = 0, long world = 42) =>
        new ObserverEnvironmentSnapshot(sequence, world, request, enabled, ordinary);

    private static void RoundTrip()
    {
        byte[] request = ObserverEnvironmentProtocol.EncodeRequest(71);
        Check(request.Length == ObserverEnvironmentProtocol.RequestBytes && ObserverEnvironmentProtocol.DecodeRequest(request) == 71);
        byte[] bytes = ObserverEnvironmentProtocol.Encode(Reply(29, 71, true, 2));
        Check(bytes.Length == ObserverEnvironmentProtocol.ReplyBytes);
        ObserverEnvironmentSnapshot copy = ObserverEnvironmentProtocol.Decode(bytes);
        Check(copy.Sequence == 29 && copy.WorldId == 42 && copy.RequestId == 71 && copy.Enabled && copy.OrdinaryConnections == 2 && !copy.Granted);
        Check(ObserverEnvironmentProtocol.Decode(ObserverEnvironmentProtocol.Encode(Reply(30, 71))).Granted);
    }

    private static void Malformed()
    {
        byte[] bytes = ObserverEnvironmentProtocol.Encode(Reply(1, 1));
        for (int length = 0; length < bytes.Length; length++)
        { var copy = new byte[length]; Array.Copy(bytes, copy, length); Reject(() => ObserverEnvironmentProtocol.Decode(copy)); }
        Reject(() => ObserverEnvironmentProtocol.Decode(new byte[bytes.Length + 1]));
        foreach (int offset in new[] { 0, 4, 32 })
        { byte[] copy = (byte[])bytes.Clone(); copy[offset] = 77; Reject(() => ObserverEnvironmentProtocol.Decode(copy)); }
        Reject(() => new ObserverEnvironmentSnapshot(1, 42, 0, true, 0));
        Reject(() => Reply(0, 1)); Reject(() => Reply(1, -1)); Reject(() => Reply(1, 1, ordinary: -1));
        Reject(() => Reply(1, 1, ordinary: ObserverEnvironmentProtocol.MaxConnections + 1));
        Reject(() => ObserverEnvironmentProtocol.EncodeRequest(0));
        Reject(() => ObserverEnvironmentProtocol.DecodeRequest(new byte[16]));
        Reject(() => ObserverEnvironmentProtocol.Decode(null!));
    }

    private static void SendTimeExpiry()
    {
        var permit = new ObserverEnvironmentPermit();
        long request = permit.Request(10);
        Check(permit.Accept(Reply(1, request), 42, 12));
        Check(permit.Granted(12.49));
        Check(!permit.Granted(12.5)); // Not 14.5 seconds based on receipt.
    }

    private static void Revocation()
    {
        var permit = new ObserverEnvironmentPermit();
        long request = permit.Request(1);
        Check(permit.Accept(Reply(1, request), 42, 1.1) && permit.Granted(1.2));
        Check(permit.Accept(Reply(3, 0, ordinary: 1), 42, 1.3));
        Check(!permit.Granted(1.3));
        Check(!permit.Accept(Reply(2, request), 42, 1.4) && !permit.Granted(1.4));
        long next = permit.Request(2);
        Check(permit.Accept(Reply(4, next), 42, 2.1) && permit.Granted(2.1));
    }

    private static void Identity()
    {
        var permit = new ObserverEnvironmentPermit();
        long request = permit.Request(0);
        Reject(() => permit.Accept(Reply(1, request, world: 43), 42, .1));
        Check(!permit.Accept(Reply(2, request + 1), 42, .2) && !permit.Granted(.2));
        Check(permit.Snapshot == null);
        Check(permit.Accept(Reply(1, request), 42, .3)); // Unmatched higher sequence did not poison this response.
        Check(!permit.Accept(Reply(1, request), 42, .4));
        Check(!permit.Granted(2.5));
    }

    private static void ClockReset()
    {
        var permit = new ObserverEnvironmentPermit(); long request = permit.Request(10);
        permit.Accept(Reply(1, request), 42, 10.1);
        Check(!permit.Granted(9)); Check(!permit.Granted(10.2));
        permit.Reset(); Check(permit.Snapshot == null && !permit.Granted(20));
        Reject(() => permit.Request(double.NaN)); Reject(() => permit.Granted(-1));
    }

    private static void Blocking()
    {
        var permit = new ObserverEnvironmentPermit(); long request = permit.Request(0);
        permit.Accept(Reply(1, request, enabled: false), 42, .1);
        Check(!permit.Granted(.1));
        request = permit.Request(1); permit.Accept(Reply(2, request, ordinary: 1), 42, 1.1);
        Check(!permit.Granted(1.1));
        request = permit.Request(2); permit.Accept(Reply(3, request), 42, 2.1);
        Check(permit.Granted(2.1));
    }

    private static void DelayedReply()
    {
        var permit = new ObserverEnvironmentPermit(); long request = permit.Request(0);
        Check(!permit.Accept(Reply(1, request), 42, 8) && !permit.Granted(8));
        long fresh = permit.Request(8.1); Check(permit.Accept(Reply(2, fresh), 42, 8.2));
        Check(permit.Granted(8.2));
        Check(!permit.Accept(Reply(1, request), 42, 8.3));
        Check(!permit.Granted(10.6));
    }

    private static void Phase()
    {
        var clock = new ScenicEnvironmentClock(); clock.Reset(.98, 0);
        Check(Math.Abs(clock.Advance(0, 30, true) - .98) < 1e-9);
        double next = clock.Advance(.9, 30, true);
        Check(Math.Abs(next - .01) < 1e-9);
        Check(Math.Abs(clock.Advance(1.8, 60, true) - .025) < 1e-9); // Retiming does not jump.
    }

    private static void Pause()
    {
        var clock = new ScenicEnvironmentClock(); clock.Reset(.25, 0);
        Check(clock.Advance(.5, 30, false) == .25);
        Check(clock.Advance(10, 30, true) == .25);
        Check(Math.Abs(clock.Advance(10.5, 30, true) - (.25 + .5 / 30)) < 1e-9);
        double before = clock.Fraction; Check(clock.Advance(11, 0, true) == before);
    }

    private static void InvalidCycle()
    {
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, -1, 1, 29.99, 86401 })
            Reject(() => ScenicEnvironmentClock.Cycle(value));
        ScenicEnvironmentClock.Cycle(0); ScenicEnvironmentClock.Cycle(30); ScenicEnvironmentClock.Cycle(86400);
        var clock = new ScenicEnvironmentClock(); Reject(() => clock.Reset(double.NaN, 0)); Reject(() => clock.Reset(.5, -1));
    }
}
