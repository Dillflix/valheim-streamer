using System;
using ValheimBroadcaster.Core;

internal static class SpectatorTests
{
    internal static void Run()
    {
        var s = new SpectatorSession();
        Expect(s.Phase == SpectatorPhase.Offline, "starts offline");
        bool refused = false;
        try { s.BeginView(); } catch (InvalidOperationException) { refused = true; }
        Expect(refused, "view before handshake rejected");
        s.Connect(); s.Tick(14.9, false, false, false);
        Expect(s.Phase == SpectatorPhase.Handshake, "bounded wait");
        s.Tick(.2, false, false, false);
        Expect(s.Phase == SpectatorPhase.Failed, "missing companion fails");
        s.Connect(); Expect(!s.AcceptServer(999) && s.Phase == SpectatorPhase.Failed, "protocol mismatch fails");
        s.Connect(); Expect(s.AcceptServer(SpectatorSession.Protocol), "compatible server");
        int a = s.BeginView();
        s.Tick(3, true, false, false);
        Expect(s.Phase == SpectatorPhase.Loading, "local readiness cannot bypass server acknowledgement");
        Expect(!s.AcceptView(a + 1, true), "future acknowledgement ignored");
        Expect(s.AcceptView(a, true), "matching acknowledgement accepted");
        s.Tick(1, true, false, false); s.Tick(.1, false, false, false); s.Tick(1.9, true, false, false);
        Expect(s.Phase == SpectatorPhase.Loading, "readiness must be continuous");
        s.Tick(.2, true, false, false);
        Expect(s.Phase == SpectatorPhase.Viewing && s.CompletedViews == 1, "first scene ready");
        int b = s.BeginView();
        Expect(!s.AcceptView(a, true), "old area acknowledgement cannot reveal new area");
        s.AcceptView(b, true); s.Tick(2, true, false, false);
        Expect(s.Phase == SpectatorPhase.Viewing && s.CompletedViews == 2, "second distant scene ready");
        s.Tick(.1, false, false, false);
        Expect(s.Phase == SpectatorPhase.Loading, "lost scene hides footage");
        s.Tick(46, false, false, false);
        Expect(s.Phase == SpectatorPhase.Failed, "recovery timeout bounded");
        s.Connect(); s.AcceptServer(1); int c = s.BeginView(); s.AcceptView(c, false);
        Expect(s.Phase == SpectatorPhase.Failed, "ungenerated destination fails");
        s.Fail("later failure"); Expect(!s.Failure.Contains("later"), "first failure preserved");
        s.Connect(); s.Tick(.1, false, true, false);
        Expect(s.Phase == SpectatorPhase.Failed, "unexpected local character fails");
        s.Connect(); s.Tick(.1, false, false, true);
        Expect(s.Phase == SpectatorPhase.Failed, "simulation ownership fails");
        s.Disconnect(); Expect(s.Phase == SpectatorPhase.Offline, "disconnect clears active state");
        s.Connect(); Expect(s.Request == 0 && s.CompletedViews == 0 && s.Failure == "", "new world resets evidence");
    }

    internal static void Boundaries()
    {
        Expect(SpectatorSession.ValidPosition(100, 40, -100), "normal coordinate");
        foreach (float x in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1000000f })
            Expect(!SpectatorSession.ValidPosition(x, 0, 0), "invalid x rejected");
        Expect(!SpectatorSession.ValidPosition(0, float.NaN, 0), "invalid height rejected");
        Expect(!SpectatorSession.ValidPosition(0, 0, float.NaN), "invalid z rejected");
        Expect(!SpectatorSession.ValidPosition(0, 6000, 0), "unbounded altitude rejected");
        foreach (string name in new[] { "ZDOData", "RoutedRPC", "CharacterID", "ServerSyncedPlayerData", "RemoteCommand", "Kick", "PeerInfo", "UnknownModRPC" })
            Expect(!SpectatorSession.AllowsRpc(name), "post-handshake gameplay traffic rejected: " + name);
        foreach (string name in new[] { "Disconnect", "DillflixBodylessHello", "DillflixBodylessView" })
            Expect(SpectatorSession.AllowsRpc(name), "required control allowed: " + name);
        var s = new SpectatorSession(); s.Connect();
        foreach (double dt in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            bool threw = false; try { s.Tick(dt, false, false, false); } catch (ArgumentOutOfRangeException) { threw = true; }
            Expect(threw, "invalid clock rejected");
        }
    }

    private static void Expect(bool value, string description)
    { if (!value) throw new Exception(description); }
}
