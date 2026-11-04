using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Server;

public sealed partial class ObserverServerPlugin
{
    private ConfigEntry<bool> environmentEnabled = null!;
    private readonly HashSet<ZNetPeer> environmentSubscribers = new HashSet<ZNetPeer>();
    private readonly Dictionary<ZNetPeer, double> environmentNextRequest = new Dictionary<ZNetPeer, double>();
    private long environmentSequence;
    private int lastEnvironmentConnections = -1;
    private bool lastEnvironmentEnabled;
    private double nextEnvironmentPoll;

    private void InitializeEnvironment() => environmentEnabled = Config.Bind("Observer", "CinematicEnvironmentEnabled", true,
        "Permit an authenticated read-only observer to change its own visual time/weather only while no ordinary connections (including joining players) exist. The client feature defaults off. Does not change shared world time, simulation speed, weather for ordinary players, or observer admission. Requires the optional Environment1 client capability.");

    private int OrdinaryEnvironmentConnections() => Math.Min(ObserverEnvironmentProtocol.MaxConnections,
        ZNet.instance.GetPeers().Count(peer => !Protected(peer)));

    private void EnvironmentRequest(ZRpc rpc, ZPackage package)
    {
        if (!Serving || Find(rpc) is not ZNetPeer peer || !peer.IsReady() || !Authorized(peer) ||
            !leases.TryGetValue(peer, out ObserverLease lease) || !lease.Confirmed) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (environmentNextRequest.TryGetValue(peer, out double next) && now < next) return;
        environmentNextRequest[peer] = now + .5;
        try
        {
            if (package.Size() != ObserverEnvironmentProtocol.RequestBytes) return;
            long requestId = ObserverEnvironmentProtocol.DecodeRequest(package.GetArray());
            environmentSubscribers.Add(peer);
            var snapshot = new ObserverEnvironmentSnapshot(++environmentSequence, ZNet.instance.GetWorldUID(), requestId,
                allow.Value && environmentEnabled.Value, OrdinaryEnvironmentConnections());
            rpc.Invoke(ObserverEnvironmentProtocol.Reply, new ZPackage(ObserverEnvironmentProtocol.Encode(snapshot)));
        }
        catch (Exception error)
        {
            // A failed optional permission never changes observer admission.
            Logger.LogWarning("Optional cinematic environment request unavailable after " + error.GetType().Name + ".");
        }
    }

    private void TickEnvironment()
    {
        if (Time.realtimeSinceStartupAsDouble < nextEnvironmentPoll) return;
        nextEnvironmentPoll = Time.realtimeSinceStartupAsDouble + .25;
        int ordinary = OrdinaryEnvironmentConnections();
        bool allowedNow = allow.Value && environmentEnabled.Value;
        if ((ordinary != lastEnvironmentConnections || allowedNow != lastEnvironmentEnabled) && (!allowedNow || ordinary > 0))
            RevokeEnvironment(ordinary, !allowedNow);
        lastEnvironmentConnections = ordinary; lastEnvironmentEnabled = allowedNow;
        foreach (ZNetPeer peer in environmentSubscribers.Where(peer => !ZNet.instance.GetPeers().Contains(peer)).ToArray())
        { environmentSubscribers.Remove(peer); environmentNextRequest.Remove(peer); }
        // Malformed authenticated requests are throttled before parsing and may
        // never become subscribers. Their disconnected peers still need pruning.
        foreach (ZNetPeer peer in environmentNextRequest.Keys.Where(peer => !ZNet.instance.GetPeers().Contains(peer)).ToArray())
            environmentNextRequest.Remove(peer);
    }

    private void RevokeEnvironment(int minimumOrdinary = 0, bool disabled = false)
    {
        if (!ZNet.instance || environmentSubscribers.Count == 0) return;
        int ordinary = Math.Max(minimumOrdinary, OrdinaryEnvironmentConnections());
        bool enabledNow = !disabled && allow.Value && environmentEnabled.Value;
        if (enabledNow && ordinary == 0) return; // Grants require a fresh, bounded client request.
        var snapshot = new ObserverEnvironmentSnapshot(++environmentSequence, ZNet.instance.GetWorldUID(), 0, enabledNow, ordinary);
        byte[] bytes = ObserverEnvironmentProtocol.Encode(snapshot);
        foreach (ZNetPeer peer in environmentSubscribers)
        {
            if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
            try { peer.m_rpc.Invoke(ObserverEnvironmentProtocol.Reply, new ZPackage(bytes)); }
            catch (Exception) { /* Disconnected clients also expire their request-bound permission. */ }
        }
    }

    private void ResetEnvironment()
    {
        environmentSubscribers.Clear(); environmentNextRequest.Clear(); environmentSequence = 0;
        lastEnvironmentConnections = -1; lastEnvironmentEnabled = false; nextEnvironmentPoll = 0;
    }
}
