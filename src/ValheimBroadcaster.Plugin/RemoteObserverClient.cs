using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// The role is selected BEFORE joining and stays pinned until the world connection ends.
internal sealed class RemoteObserverClient
{
    private static RemoteObserverClient? instance;
    private readonly Func<bool> configured;
    private readonly Action<string> log;
    private ZNet? world;
    private ZNetPeer? server;
    private bool requested, failed, spawned;
    private float readySince = -1, nextHello, nextDiscovery;
    private readonly ObserverDiscoveryCache discovery = new ObserverDiscoveryCache();
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
        // Native respawn allocates a character before indexing the server peer.
        // Gate the whole transaction while authorization/transport is unavailable.
        Patch(harmony, typeof(Game), "UpdateRespawn", new[] { typeof(float) }, nameof(BeforeRespawn), false);
        Patch(harmony, typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }, nameof(Connection), true);
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
        client.requested = client.configured(); client.Approved = client.failed = client.spawned = client.TerminalFailure = false;
        client.readySince = -1; client.nextHello = client.nextDiscovery = 0; client.discovery.Reset();
        client.Status = client.requested ? "Waiting for read-only observer authorization" : "Observer connection disabled";
        if (client.requested)
        {
            __0.m_rpc.Register<int, int>(ObserverProtocol.Reply, client.Reply);
            __0.m_rpc.Register<ZPackage>(ObserverDiscoveryProtocol.Reply, client.DiscoveryReply);
        }
    }
    private void Reply(ZRpc rpc, int version, int accepted)
    {
        if (!Isolated || failed || server == null || !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        if (version != ObserverProtocol.Version)
        { Fail("Observer protocol mismatch: client " + ObserverProtocol.Version + ", server " + version); return; }
        if (accepted != 1)
        { Fail("Server denied observer authorization (protocol " + version + " matches). Check the broadcaster SteamID allowlist, Enabled setting and connection reservation on the server."); return; }
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
    internal void Tick()
    {
        if (!Isolated) return;
        if (server == null || !world!.GetPeers().Contains(server)) { Fail("Observer connection ended", false); return; }
        if (failed) return;
        if (!server.IsReady()) return;
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
    }
    internal void Spawned() { spawned = true; Status = "Read-only observer connection active"; }
    internal void Fail(string reason, bool terminal = true)
    {
        if (failed) return;
        failed = true; Approved = false; TerminalFailure = terminal; discovery.Reset(); Status = reason; log(reason);
        if (world && world == ZNet.instance && server != null && world.GetPeers().Contains(server)) world.Disconnect(server);
        // Do not unpin input/network restrictions inside a still-loaded world.
    }
    internal void Close()
    {
        if (Isolated) Fail("Observer plugin stopped; disconnecting read-only session");
    }
    private static bool BeforeRespawn()
    {
        RemoteObserverClient? client = instance;
        if (client?.Isolated != true) return true; // Ordinary clients/offline worlds are unchanged.
        return !client.failed && client.Approved && client.server != null && client.world &&
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
