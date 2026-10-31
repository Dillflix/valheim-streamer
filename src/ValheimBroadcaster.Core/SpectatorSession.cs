using System;

namespace ValheimBroadcaster.Core;

public enum SpectatorPhase { Offline, Handshake, Loading, Viewing, Failed }

/// <summary>Fail-closed, per-connection state. Readiness is evidence, not a complete-scene verdict.</summary>
public sealed class SpectatorSession
{
    public const int Protocol = 1;
    public SpectatorPhase Phase { get; private set; }
    public string Failure { get; private set; } = "";
    public int Request { get; private set; }
    public int CompletedViews { get; private set; }
    public double Elapsed { get; private set; }
    private double stable;
    private bool acknowledged;

    public void Connect()
    {
        Phase = SpectatorPhase.Handshake; Failure = ""; Request = 0;
        CompletedViews = 0; Elapsed = 0; stable = 0; acknowledged = false;
    }

    public bool AcceptServer(int protocol)
    {
        if (Phase != SpectatorPhase.Handshake) return false;
        if (protocol != Protocol) { Fail("Companion protocol mismatch"); return false; }
        Phase = SpectatorPhase.Loading; Elapsed = 0;
        return true;
    }

    public int BeginView()
    {
        if (Phase != SpectatorPhase.Loading && Phase != SpectatorPhase.Viewing)
            throw new InvalidOperationException("A verified companion is required before requesting a view.");
        Request++; Phase = SpectatorPhase.Loading; Elapsed = 0; stable = 0; acknowledged = false;
        return Request;
    }

    public bool AcceptView(int request, bool allowed)
    {
        if (Phase != SpectatorPhase.Loading || request != Request || request <= 0 || acknowledged) return false;
        if (!allowed) { Fail("Destination was rejected: use a previously generated region"); return false; }
        acknowledged = true;
        return true;
    }

    public void Tick(double seconds, bool ready, bool localPlayerExists, bool ownsObject)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Phase == SpectatorPhase.Offline || Phase == SpectatorPhase.Failed) return;
        if (localPlayerExists || ownsObject) { Fail(localPlayerExists ? "Local player exists" : "Spectator owns a network object"); return; }
        Elapsed += seconds;
        if (Phase == SpectatorPhase.Handshake && Elapsed >= 15) Fail("No compatible server companion acknowledgement within 15 seconds");
        if (Phase == SpectatorPhase.Loading)
        {
            stable = acknowledged && ready ? stable + seconds : 0;
            if (stable >= 2) { Phase = SpectatorPhase.Viewing; CompletedViews++; }
            else if (Elapsed >= 45) Fail("Scene did not become ready within 45 seconds");
        }
        else if (Phase == SpectatorPhase.Viewing && !ready)
        { Phase = SpectatorPhase.Loading; Elapsed = 0; stable = 0; }
    }

    public void Fail(string reason)
    {
        if (Phase == SpectatorPhase.Failed) return;
        Failure = reason; Phase = SpectatorPhase.Failed;
    }

    public void Disconnect() { Phase = SpectatorPhase.Offline; acknowledged = false; stable = 0; }

    public static bool ValidPosition(float x, float y, float z) =>
        !float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z) &&
        Math.Abs(x) <= 10500 && Math.Abs(z) <= 10500 && y >= -1000 && y <= 5000;

    // After the authenticated peer becomes ready, no ordinary gameplay RPCs may leave it.
    // Ping uses hash zero and is handled separately by the transport adapter.
    public static readonly string[] AllowedRpcNames = {
        "Disconnect", "DillflixBodylessHello", "DillflixBodylessView"
    };

    public static bool AllowsRpc(string name) => Array.IndexOf(AllowedRpcNames, name) >= 0;
}
