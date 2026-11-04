using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class RemoteObserverClient
{
    private readonly ObserverEnvironmentPermit environmentPermit = new ObserverEnvironmentPermit();
    private double nextEnvironment, environmentProbeAt = -1;
    private bool environmentSupported, environmentUnavailable, environmentRejected;
    internal bool EnvironmentRequested { get; set; }
    internal event Action? EnvironmentRevoked;

    internal bool EnvironmentAllowed(double now) => Isolated && Approved && !failed && !environmentUnavailable &&
        server != null && server.m_rpc.IsConnected() && environmentPermit.Granted(now);

    internal string EnvironmentStatus
    {
        get
        {
            if (!Isolated || !Approved) return "Environment: awaiting authorized observer connection";
            if (environmentUnavailable) return "Environment: optional Environment1 companion capability unavailable";
            if (!environmentSupported) return "Environment: awaiting empty-server permission";
            ObserverEnvironmentSnapshot? snapshot = environmentPermit.Snapshot;
            if (snapshot == null) return "Environment: awaiting a fresh request-bound server permission";
            if (!snapshot.Enabled) return "Environment: disabled by server";
            if (snapshot.OrdinaryConnections > 0) return "Environment: " + snapshot.OrdinaryConnections + " ordinary/joining connection(s); natural environment restored";
            return EnvironmentAllowed(Time.realtimeSinceStartupAsDouble) ? "Environment: empty-server permission active" :
                "Environment: permission expired; natural environment restored";
        }
    }

    private void EnvironmentReply(ZRpc rpc, ZPackage package)
    {
        if (!Isolated || !Approved || failed || environmentRejected || server == null ||
            !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        try
        {
            if (package.Size() != ObserverEnvironmentProtocol.ReplyBytes) throw new ArgumentException("Invalid environment packet length.");
            ObserverEnvironmentSnapshot snapshot = ObserverEnvironmentProtocol.Decode(package.GetArray());
            environmentPermit.Accept(snapshot, world!.GetWorldUID(), Time.realtimeSinceStartupAsDouble);
            environmentSupported = true; environmentUnavailable = false;
            if (!EnvironmentAllowed(Time.realtimeSinceStartupAsDouble)) EnvironmentRevoked?.Invoke();
        }
        catch (Exception error)
        {
            environmentUnavailable = environmentRejected = true; environmentPermit.Reset(); EnvironmentRevoked?.Invoke();
            log("Optional cinematic environment permission rejected after " + error.GetType().Name + "; natural time/weather retained.");
        }
    }

    private void TickEnvironment(double now)
    {
        if (!Approved || environmentUnavailable || server == null || !EnvironmentRequested) return;
        if (!environmentSupported && environmentProbeAt >= 0)
        {
            if (now - environmentProbeAt >= ObserverEnvironmentProtocol.ProbeSeconds)
            {
                environmentUnavailable = true; environmentPermit.Reset(); EnvironmentRevoked?.Invoke();
                log("Optional Environment1 companion unavailable; cinematic environment controls remain inactive.");
            }
            return;
        }
        if (now < nextEnvironment) return;
        nextEnvironment = now + ObserverEnvironmentProtocol.RequestInterval;
        if (environmentProbeAt < 0) environmentProbeAt = now;
        try
        {
            long request = environmentPermit.Request(now);
            server.m_rpc.Invoke(ObserverEnvironmentProtocol.Request, new ZPackage(ObserverEnvironmentProtocol.EncodeRequest(request)));
        }
        catch (Exception error)
        {
            environmentUnavailable = true; environmentPermit.Reset(); EnvironmentRevoked?.Invoke();
            log("Optional cinematic environment request unavailable after " + error.GetType().Name + ".");
        }
    }

    private void ResetEnvironment()
    {
        EnvironmentRequested = false; environmentPermit.Reset(); nextEnvironment = 0; environmentProbeAt = -1;
        environmentSupported = environmentUnavailable = environmentRejected = false;
        EnvironmentRevoked?.Invoke();
    }
}
