using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// The role is selected BEFORE joining and stays pinned until the world connection ends.
internal sealed partial class RemoteObserverClient
{
    private static RemoteObserverClient? instance;
    private readonly Func<bool> configured;
    private readonly Action<string> log;
    private ZNet? world;
    private ZNetPeer? server;
    private bool requested, requestSent, failed, spawned;
    private float readySince = -1, nextHello, nextDiscovery;
    private readonly ObserverDiscoveryCache discovery = new ObserverDiscoveryCache();
    private readonly ObserverContextCache context = new ObserverContextCache();
    private float contextProbeAt = -1, nextContext;
    private bool contextSupported, contextUnavailable, contextRejected;
    internal ObserverContextSnapshot? Context => Isolated && Approved ? context.Snapshot : null;
    internal double ContextAge => context.Age(Time.realtimeSinceStartup);
    internal string ContextStatus => contextUnavailable ? "Context: local observations (optional server context unavailable)" :
        !contextSupported ? "Context: probing optional server support" : Context?.Enabled != true ? "Context: server telemetry disabled" :
        ContextAge >= ObserverContextProtocol.FreshSeconds ? "Context: server observations stale; using local scene" :
        "Context: server observations available; age " + ContextAge.ToString("F1") + "s";
    internal ObserverPlayerContext? GetPlayerContext(string id, string characterId)
    {
        if (!Isolated || !Approved || contextUnavailable) return null;
        ObserverPlayerContext? item = context.Find(id, characterId, Time.realtimeSinceStartup);
        if (item == null) return null;
        float elapsed = (float)ContextAge;
        return new ObserverPlayerContext(item.Id, item.CharacterId, item.Position, item.Velocity,
            item.StateAgeSeconds + elapsed, item.MetadataAgeSeconds + elapsed,
            item.HealthLossAgeSeconds.HasValue ? (float?)Math.Min(ObserverContextProtocol.MaximumAgeSeconds, item.HealthLossAgeSeconds.Value + elapsed) : null);
    }
    private readonly ObserverActivityCache activity = new ObserverActivityCache();
    private float activityProbeAt = -1, nextActivity;
    private bool activitySupported, activityUnavailable, activityRejected;
    internal ObserverActivitySnapshot? Activity => Isolated && Approved ? activity.Snapshot : null;
    internal double ActivityAge => activity.Age(Time.realtimeSinceStartup);
    internal bool ActivityFresh => Isolated && Approved && !activityUnavailable && activity.Fresh(Time.realtimeSinceStartup);
    internal string ActivityStatus
    {
        get
        {
            if (!Isolated || !Approved) return "Activity: awaiting authorized observer connection";
            if (activityUnavailable) return "Activity: optional server observations unavailable; connected-player wildlife cutaways disabled";
            if (!activitySupported) return "Activity: probing optional server support";
            ObserverActivitySnapshot? snapshot = Activity;
            if (snapshot?.Enabled != true) return "Activity: server observations disabled";
            if (!snapshot.Complete) return "Activity: incomplete ordinary-player cohort";
            double age = ActivityAge;
            if (age >= ObserverActivityProtocol.FreshSeconds) return "Activity: server observations stale";
            int fresh = 0;
            foreach (ObserverPlayerActivity player in snapshot.Players) if (player.Fresh(age)) fresh++;
            return "Activity: " + fresh + "/" + snapshot.Players.Count + " players with fresh observations; age " + age.ToString("F1") + "s";
        }
    }
    // The caller must also match the complete discovery cohort before making an
    // all-player decision. This accessor never extrapolates observed idle time.
    internal ObserverPlayerActivity? GetPlayerActivity(string id, string characterId) =>
        Isolated && Approved && !activityUnavailable ? activity.Find(id, characterId, Time.realtimeSinceStartup) : null;
    private readonly ObserverEventCache events = new ObserverEventCache();
    private float eventProbeAt = -1, nextEvents;
    private bool eventsSupported, eventsUnavailable, eventsRejected;
    internal ObserverEventSnapshot? Events => Isolated && Approved ? events.Snapshot : null;
    internal double EventAge => events.Age(Time.realtimeSinceStartup);
    internal string EventStatus => !Isolated || !Approved ? "Events: awaiting authorized observer connection" :
        eventsUnavailable ? "Events: optional server evidence unavailable; using local activity" :
        !eventsSupported ? "Events: probing optional server support" : Events?.Enabled != true ? "Events: server evidence disabled" :
        !events.Fresh(Time.realtimeSinceStartup) ? "Events: server evidence stale; using local activity" :
        "Events: server evidence available; age " + EventAge.ToString("F1") + "s";
    // Returned source and event ages already include time since receipt. Generic
    // Action and HealthLoss are deliberately not labelled as combat or exact intent.
    internal ObserverPlayerEvents? GetPlayerEvents(string id, string characterId)
    {
        if (!Isolated || !Approved || eventsUnavailable) return null;
        ObserverPlayerEvents? item = events.Find(id, characterId, Time.realtimeSinceStartup);
        return item?.Elapsed((float)EventAge);
    }
    internal bool Isolated => requested && world && world == ZNet.instance;
    internal bool Approved { get; private set; }
    internal bool Failed => failed;
    internal bool TerminalFailure { get; private set; }
    internal ObserverDiscoverySnapshot? Discovery => Isolated && Approved ? discovery.Snapshot : null;
    internal bool DiscoveryFresh => Isolated && Approved && discovery.Fresh(Time.realtimeSinceStartup);
    internal double DiscoveryAge => discovery.Age(Time.realtimeSinceStartup);
    internal string DiscoveryStatus => Discovery == null ? "Waiting for server discovery" :
        !DiscoveryFresh ? "Server discovery stale (" + DiscoveryAge.ToString("F1") + "s)" :
        !Discovery.DiscoveryEnabled ? "Server discovery disabled" :
        "Discovery: " + Discovery.Players.Count + " players, " + Discovery.Regions.Count + " generated scenic regions; age " + DiscoveryAge.ToString("F1") + "s" +
        (Discovery.SharePrivatePositions ? "" : "; private player coordinates withheld");
    internal string Status { get; private set; } = "Observer connection disabled";

    internal RemoteObserverClient(Func<bool> configured, Action<string> log) { this.configured = configured; this.log = log; instance = this; }
    internal void Install(Harmony harmony)
    {
        // Gate allocation while authorization/transport is unavailable. Keep the
        // protocol-3 SendPeerInfo prefix below: intent must precede admission.
        Patch(harmony, typeof(Game), "UpdateRespawn", new[] { typeof(float) }, nameof(BeforeRespawn), false);
        Patch(harmony, typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }, nameof(Connection), true);
        Patch(harmony, typeof(ZNet), "SendPeerInfo", new[] { typeof(ZRpc), typeof(string) }, nameof(RequestBeforeAdmission), false);
        MethodInfo sync = AccessTools.Method(typeof(ZDOMan), "CreateSyncList") ?? throw new MissingMethodException("ZDOMan.CreateSyncList");
        harmony.Patch(sync, postfix: new HarmonyMethod(typeof(RemoteObserverClient), nameof(PublishObjects)));
        Patch(harmony, typeof(ZRoutedRpc), "InvokeRoutedRPC", new[] { typeof(long), typeof(ZDOID), typeof(string), typeof(object[]) }, nameof(SendGameplay), false);
        Patch(harmony, typeof(ZDO), "IsOwner", Type.EmptyTypes, nameof(SimulationOwner), true);
        Patch(harmony, typeof(ZNetView), "ClaimOwnership", Type.EmptyTypes, nameof(Claim), false);
    }
    private static void Patch(Harmony harmony, Type type, string method, Type[] parameters, string patch, bool postfix)
    {
        MethodInfo original = AccessTools.Method(type, method, parameters) ?? throw new MissingMethodException(type.Name, method);
        var hook = new HarmonyMethod(typeof(RemoteObserverClient), patch);
        harmony.Patch(original, postfix ? null : hook, postfix ? hook : null);
    }
    private static void Connection(ZNetPeer __0)
    {
        RemoteObserverClient? client = instance;
        if (client == null || !ZNet.instance || ZNet.instance.IsServer() || !__0.m_server) return;
        client.world = ZNet.instance; client.server = __0;
        client.requested = client.configured(); client.requestSent = client.Approved = client.failed = client.spawned = client.TerminalFailure = false;
        client.readySince = -1; client.nextHello = client.nextDiscovery = 0; client.discovery.Reset();
        client.context.Reset(); client.contextProbeAt = -1; client.nextContext = 0;
        client.contextSupported = client.contextUnavailable = client.contextRejected = false;
        client.activity.Reset(); client.activityProbeAt = -1; client.nextActivity = 0;
        client.activitySupported = client.activityUnavailable = client.activityRejected = false;
        client.events.Reset(); client.eventProbeAt = -1; client.nextEvents = 0;
        client.eventsSupported = client.eventsUnavailable = client.eventsRejected = false;
        client.ResetInterests();
        client.Status = client.requested ? "Waiting for read-only observer authorization" : "Observer connection disabled";
        if (client.requested)
        {
            __0.m_rpc.Register<int, int>(ObserverProtocol.Reply, client.Reply);
            __0.m_rpc.Register<ZPackage>(ObserverDiscoveryProtocol.Reply, client.DiscoveryReply);
            __0.m_rpc.Register<ZPackage>(ObserverContextProtocol.Reply, client.ContextReply);
            __0.m_rpc.Register<ZPackage>(ObserverActivityProtocol.Reply, client.ActivityReply);
            __0.m_rpc.Register<ZPackage>(ObserverEventProtocol.Reply, client.EventReply);
            __0.m_rpc.Register<ZPackage>(WorldInterestProtocol.Reply, client.InterestReply);
            __0.m_rpc.Register<ZPackage>(WorldInterestProtocol.Reply2, client.InterestReply2);
        }
    }
    private static bool RequestBeforeAdmission(ZRpc __0)
    {
        RemoteObserverClient? client = instance;
        if (client?.Isolated != true) return true;
        if (client.failed) return false;
        if (client.server == null || !ReferenceEquals(__0, client.server.m_rpc) || client.server.m_socket is not ZSteamSocket)
        { client.Fail("Observer mode requires the dedicated Steam server connection"); return false; }
        if (client.requestSent) return true;
        try
        {
            // Both RPCs use the same reliable FIFO transport. Put intent ahead of
            // native authentication/admission, without changing the vanilla packet.
            __0.Invoke(ObserverProtocol.Request, ObserverProtocol.Version);
            client.requestSent = true;
            client.log("Observer protocol 3 opt-in request sent before native admission.");
            return true;
        }
        catch (Exception error)
        { client.Fail("Observer request could not be sent: " + error.GetType().Name); return false; }
    }
    private void Reply(ZRpc rpc, int version, int accepted)
    {
        if (!Isolated || !requestSent || failed || server == null || !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        if (version != ObserverProtocol.Version)
        { Fail("Observer protocol mismatch: client " + ObserverProtocol.Version + ", server " + version); return; }
        if (accepted != 1)
        { Fail("Server denied observer authorization after the protocol-3 opt-in request. Check the server admission log."); return; }
        if (!Approved) { Approved = true; Status = "Read-only observer authorized; waiting for the character scene"; log(Status); }
    }
    private void DiscoveryReply(ZRpc rpc, ZPackage package)
    {
        if (!Isolated || !Approved || failed || server == null || !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        try
        {
            if (package.Size() > ObserverDiscoveryProtocol.MaxPacketBytes) throw new ArgumentException("Observer discovery packet too large.");
            ObserverDiscoverySnapshot snapshot = ObserverDiscoveryProtocol.Decode(package.GetArray());
            if (snapshot.WorldId != world!.GetWorldUID()) throw new ArgumentException("Observer discovery world mismatch.");
            discovery.Accept(snapshot, Time.realtimeSinceStartup);
        }
        catch (Exception error) { Fail("Invalid observer discovery snapshot: " + error.GetType().Name); }
    }
    private void ContextReply(ZRpc rpc, ZPackage package)
    {
        if (!Isolated || !Approved || failed || contextRejected || server == null ||
            !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        try
        {
            if (package.Size() > ObserverContextProtocol.MaxPacketBytes) throw new ArgumentException("Observer context packet too large.");
            ObserverContextSnapshot snapshot = ObserverContextProtocol.Decode(package.GetArray());
            if (snapshot.WorldId != world!.GetWorldUID()) throw new ArgumentException("Observer context world mismatch.");
            // A legitimate delayed first reply can recover optional support after
            // a loading stall, without sending repeated probes to old servers.
            context.Accept(snapshot, Time.realtimeSinceStartup); contextSupported = true; contextUnavailable = false;
        }
        catch (Exception error)
        {
            contextUnavailable = contextRejected = true; context.Reset();
            log("Optional server context rejected after " + error.GetType().Name + "; using local scene observations.");
        }
    }
    private void TickContext(float now)
    {
        if (!Approved || contextUnavailable || server == null) return;
        // Probe once. Old protocol-3 servers need no upgrade to continue camera
        // operation and are not repeatedly sent an RPC they do not implement.
        if (!contextSupported && contextProbeAt >= 0)
        {
            if (now - contextProbeAt >= ObserverContextProtocol.ProbeSeconds)
            { contextUnavailable = true; log("Optional server context unavailable; local scene observations remain active."); }
            return;
        }
        if (now < nextContext) return;
        nextContext = now + 2;
        if (contextProbeAt < 0) contextProbeAt = now;
        try { server.m_rpc.Invoke(ObserverContextProtocol.Request, ObserverContextProtocol.Version); }
        catch (Exception error)
        { contextUnavailable = true; context.Reset(); log("Optional server context request unavailable: " + error.GetType().Name); }
    }
    private void ActivityReply(ZRpc rpc, ZPackage package)
    {
        if (!Isolated || !Approved || failed || activityRejected || server == null ||
            !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        try
        {
            if (package.Size() > ObserverActivityProtocol.MaxPacketBytes) throw new ArgumentException("Observer activity packet too large.");
            ObserverActivitySnapshot snapshot = ObserverActivityProtocol.Decode(package.GetArray());
            if (snapshot.WorldId != world!.GetWorldUID()) throw new ArgumentException("Observer activity world mismatch.");
            activity.Accept(snapshot, Time.realtimeSinceStartup);
            activitySupported = true; activityUnavailable = false;
        }
        catch (Exception error)
        {
            // Optional telemetry cannot revoke a correctly admitted observer.
            activityUnavailable = activityRejected = true; activity.Reset();
            log("Optional server activity rejected after " + error.GetType().Name + "; connected-player wildlife cutaways disabled.");
        }
    }

    private void TickActivity(float now)
    {
        if (!Approved || activityUnavailable || server == null) return;
        if (!activitySupported && activityProbeAt >= 0)
        {
            if (now - activityProbeAt >= ObserverActivityProtocol.ProbeSeconds)
            {
                activityUnavailable = true;
                log("Optional server activity unavailable; existing observer camera operation remains active.");
            }
            return;
        }
        if (now < nextActivity) return;
        nextActivity = now + 2;
        if (activityProbeAt < 0) activityProbeAt = now;
        try { server.m_rpc.Invoke(ObserverActivityProtocol.Request, ObserverActivityProtocol.Version); }
        catch (Exception error)
        {
            activityUnavailable = true; activity.Reset();
            log("Optional server activity request unavailable: " + error.GetType().Name);
        }
    }
    private void EventReply(ZRpc rpc, ZPackage package)
    {
        if (!Isolated || !Approved || failed || eventsRejected || server == null ||
            !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        try
        {
            if (package.Size() > ObserverEventProtocol.MaxPacketBytes) throw new ArgumentException("Observer event packet too large.");
            ObserverEventSnapshot snapshot = ObserverEventProtocol.Decode(package.GetArray());
            if (snapshot.WorldId != world!.GetWorldUID()) throw new ArgumentException("Observer event world mismatch.");
            events.Accept(snapshot, Time.realtimeSinceStartup);
            eventsSupported = true; eventsUnavailable = false;
        }
        catch (Exception error)
        {
            eventsUnavailable = eventsRejected = true; events.Reset();
            log("Optional server event evidence rejected after " + error.GetType().Name + "; using local activity.");
        }
    }
    private void TickEvents(float now)
    {
        if (!Approved || eventsUnavailable || server == null) return;
        if (!eventsSupported && eventProbeAt >= 0)
        {
            if (now - eventProbeAt >= ObserverEventProtocol.ProbeSeconds)
            { eventsUnavailable = true; log("Optional server event evidence unavailable; using local activity."); }
            return;
        }
        if (now < nextEvents) return;
        nextEvents = now + 2;
        if (eventProbeAt < 0) eventProbeAt = now;
        try { server.m_rpc.Invoke(ObserverEventProtocol.Request, ObserverEventProtocol.Version); }
        catch (Exception error)
        { eventsUnavailable = true; events.Reset(); log("Optional server event request unavailable: " + error.GetType().Name); }
    }
    internal void Tick()
    {
        if (!Isolated) return;
        if (server == null || !world!.GetPeers().Contains(server)) { Fail("Observer connection ended", false); return; }
        if (failed) return;
        if (!server.IsReady()) return;
        if (!requestSent) { Fail("Observer intent was not sent before ordinary admission; reconnect with matching plugins"); return; }
        if (readySince < 0) readySince = Time.realtimeSinceStartup;
        if (!Approved && Time.realtimeSinceStartup - readySince >= ObserverProtocol.HandshakeSeconds)
        { Fail("No matching observer server authorization within 15 seconds"); return; }
        if (Approved && !spawned && Time.realtimeSinceStartup - readySince >= 60)
        { Fail("Observer character could not become ready within 60 seconds; use an existing character without an intro sequence"); return; }
        if (!Approved && server.IsReady() && Time.realtimeSinceStartup >= nextHello)
        {
            nextHello = Time.realtimeSinceStartup + 2;
            server.m_rpc.Invoke(ObserverProtocol.Hello, ObserverProtocol.Version);
        }
        if (Approved && Time.realtimeSinceStartup >= nextDiscovery)
        {
            nextDiscovery = Time.realtimeSinceStartup + 2;
            server.m_rpc.Invoke(ObserverDiscoveryProtocol.Request, ObserverDiscoveryProtocol.Version);
        }
        TickContext(Time.realtimeSinceStartup);
        TickActivity(Time.realtimeSinceStartup);
        TickEvents(Time.realtimeSinceStartup);
        TickInterests(Time.realtimeSinceStartup);
    }
    internal void Spawned() { spawned = true; Status = "Read-only observer connection active"; }
    internal void Fail(string reason, bool terminal = true)
    {
        if (failed) return;
        failed = true; Approved = false; TerminalFailure = terminal; discovery.Reset(); context.Reset(); contextUnavailable = true;
        activity.Reset(); activityUnavailable = true; Status = reason; log(reason);
        events.Reset(); eventsUnavailable = true;
        ResetInterests();
        if (world && world == ZNet.instance && server != null && world.GetPeers().Contains(server)) world.Disconnect(server);
        // Do not unpin input/network restrictions inside a still-loaded world.
    }
    internal void Close()
    {
        ResetInterests();
        if (Isolated) Fail("Observer plugin stopped; disconnecting read-only session");
    }
    private static bool BeforeRespawn()
    {
        RemoteObserverClient? client = instance;
        if (client?.Isolated != true) return true;
        return !client.failed && client.requestSent && client.Approved && client.server != null && client.world &&
            client.world.GetPeers().Contains(client.server) && client.server.IsReady() && client.server.m_rpc.IsConnected();
    }
    private static void PublishObjects(List<ZDO> __1)
    {
        if (instance?.Isolated == true) __1.Clear();
    }
    private static bool SendGameplay(string __2) => instance?.Isolated != true || __2 == "RequestZDO";
    private static bool Claim(ZNetView __instance) => instance?.Isolated != true || IsLocalCharacter(__instance.GetZDO());
    private static void SimulationOwner(ZDO __instance, ref bool __result)
    {
        if (__result && instance?.Isolated == true && !IsLocalCharacter(__instance)) __result = false;
    }
    private static bool IsLocalCharacter(ZDO? zdo)
    {
        if (zdo == null) return false;
        if (Player.m_localPlayer) return Player.m_localPlayer.GetComponent<ZNetView>()?.GetZDO() == zdo;
        // During spawn Player.Awake has not yet assigned m_localPlayer. Preserve
        // ownership only for the local avatar prefab; never grant ownership here.
        return Game.instance && Game.instance.m_playerPrefab && zdo.GetPrefab() == Game.instance.m_playerPrefab.name.GetStableHashCode();
    }
}
