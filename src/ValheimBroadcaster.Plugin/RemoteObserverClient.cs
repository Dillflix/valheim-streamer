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
    private float readySince = -1, nextHello;
    internal bool Isolated => requested && world && world == ZNet.instance;
    internal bool Approved { get; private set; }
    internal string Status { get; private set; } = "Observer connection disabled";

    internal RemoteObserverClient(Func<bool> configured, Action<string> log) { this.configured = configured; this.log = log; instance = this; }
    internal void Install(Harmony harmony)
    {
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
        client.requested = client.configured(); client.Approved = client.failed = client.spawned = false;
        client.readySince = -1; client.nextHello = 0;
        client.Status = client.requested ? "Waiting for read-only observer authorization" : "Observer connection disabled";
        if (client.requested) __0.m_rpc.Register<int, int>(ObserverProtocol.Reply, client.Reply);
    }
    private void Reply(ZRpc rpc, int version, int accepted)
    {
        if (!Isolated || failed || server == null || !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        if (version != ObserverProtocol.Version || accepted != 1) { Fail("Server denied the observer role or protocol version"); return; }
        if (!Approved) { Approved = true; Status = "Read-only observer authorized; waiting for the character scene"; log(Status); }
    }
    internal void Tick()
    {
        if (!Isolated) return;
        if (server == null || !world!.GetPeers().Contains(server)) { Fail("Observer connection ended; leave this world"); return; }
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
    }
    internal void Spawned() { spawned = true; Status = "Read-only observer connection active"; }
    internal void Fail(string reason)
    {
        if (failed) return;
        failed = true; Approved = false; Status = reason; log(reason);
        if (world && world == ZNet.instance && server != null && world.GetPeers().Contains(server)) world.Disconnect(server);
        // Do not unpin input/network restrictions inside a still-loaded world.
    }
    internal void Close()
    {
        if (Isolated) Fail("Observer plugin stopped; disconnecting read-only session");
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
