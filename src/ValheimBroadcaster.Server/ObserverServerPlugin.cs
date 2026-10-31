using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Server;

[BepInPlugin("com.dillflix.valheimbroadcaster.server", "Valheim Broadcaster Server", "0.7.1")]
[BepInDependency(Jotunn.Main.ModGuid, "2.30.2")]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public sealed class ObserverServerPlugin : BaseUnityPlugin
{
    private static ObserverServerPlugin? instance;
    private Harmony? harmony;
    private ConfigEntry<bool> allow = null!;
    private ConfigEntry<string> identities = null!;
    private HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<ZNetPeer> reserved = new HashSet<ZNetPeer>();
    private readonly Dictionary<ZNetPeer, ObserverLease> leases = new Dictionary<ZNetPeer, ObserverLease>();
    private readonly Dictionary<ZNetPeer, float> nextReply = new Dictionary<ZNetPeer, float>();
    private ZNet? world;
    private float nextCheck;
    private string configured = "";
    private long blockedWrites, blockedRpcs;
    private static bool Serving => instance != null && ZNet.instance && ZNet.instance.IsServer() && ZNet.instance.IsDedicated();

    private void Awake()
    {
        allow = Config.Bind("Observer", "Enabled", false, "Opt-in read-only observer connections on a dedicated Steam server. Restart after changing configuration.");
        identities = Config.Bind("Observer", "SteamIds", "", "Comma-separated Steam64 identities dedicated to broadcasting. These connections cannot play normally; other players need no mod.");
        instance = this;
        try
        {
            ReloadAllowlist();
            harmony = new Harmony("com.dillflix.valheimbroadcaster.server");
            Patch(typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }, postfix: nameof(NewConnection));
            Patch(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) }, postfix: nameof(PeerReady), transpiler: nameof(PreserveAdmissionCount));
            Patch(typeof(ZNet), "RPC_ServerSyncedPlayerData", new[] { typeof(ZRpc), typeof(ZPackage) }, prefix: nameof(ObserverPosition));
            Patch(typeof(RandEventSystem), "RefreshPlayerEventData", Type.EmptyTypes, transpiler: nameof(EventPeerList));
            Patch(typeof(ZoneSystem), "Update", Type.EmptyTypes, transpiler: nameof(EventPeerList));
            Patch(typeof(ZNet), "RPC_CharacterID", new[] { typeof(ZRpc), typeof(ZDOID) }, prefix: nameof(CharacterIdentity));
            Patch(typeof(ZDOMan), "RPC_ZDOData", new[] { typeof(ZRpc), typeof(ZPackage) }, prefix: nameof(WorldWrite));
            Patch(typeof(ZRoutedRpc), "RPC_RoutedRPC", new[] { typeof(ZRpc), typeof(ZPackage) }, prefix: nameof(RoutedRequest));
            Patch(typeof(ZDOMan), "ReleaseNearbyZDOS", new[] { typeof(Vector3), typeof(long) }, prefix: nameof(AssignNearby));
            Patch(typeof(ZDO), "SetOwner", new[] { typeof(long) }, prefix: nameof(AssignOwner));
            Patch(typeof(ZNet), "UpdatePlayerList", Type.EmptyTypes, transpiler: nameof(GameplayPeerList));
            Patch(typeof(ZNet), "RPC_Save", new[] { typeof(ZRpc) }, prefix: nameof(WorldWrite));
            foreach (string command in new[] { "RPC_RemoteCommand", "RPC_Kick", "RPC_Ban", "RPC_Unban" })
                Patch(typeof(ZNet), command, new[] { typeof(ZRpc), typeof(string) }, prefix: nameof(WorldWrite));
            // The observer never receives a character ZDO on the server. Inbound object
            // writes and gameplay routed calls are refused before native handlers run.
            Logger.LogInfo("Observer server 0.7.1 ready; opt-in dedicated Steam connections only. No ordinary-client mod requirement.");
        }
        catch (Exception error)
        {
            harmony?.UnpatchSelf();
            Logger.LogError("Observer server unavailable; allowlisted observer connections will not be supported: " + error);
            instance = null; enabled = false;
        }
    }

    private void Patch(Type type, string name, Type[] args, string? prefix = null, string? postfix = null, string? transpiler = null)
    {
        MethodInfo method = AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.Name, name);
        harmony!.Patch(method,
            prefix == null ? null : new HarmonyMethod(typeof(ObserverServerPlugin), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(ObserverServerPlugin), postfix),
            transpiler == null ? null : new HarmonyMethod(typeof(ObserverServerPlugin), transpiler));
    }

    private void ReloadAllowlist()
    {
        configured = identities.Value;
        try { allowed = ObserverProtocol.SteamAllowlist(configured); }
        catch (ArgumentException error) { allowed.Clear(); Logger.LogError(error.Message + " Observer access disabled until corrected."); }
    }
    private bool Authorized(ZNetPeer peer) => allow.Value && peer.m_socket is ZSteamSocket && allowed.Contains(peer.m_socket.GetHostName());
    private bool Protected(ZNetPeer peer) => reserved.Contains(peer);
    private static ZNetPeer? Find(ZRpc rpc) => ZNet.instance ? ZNet.instance.GetPeers().FirstOrDefault(peer => ReferenceEquals(peer.m_rpc, rpc)) : null;
    private static bool ProtectedRpc(ZRpc rpc) => Serving && Find(rpc) is ZNetPeer peer && instance!.Protected(peer);
    private static bool ProtectedUid(long uid) => uid != 0 && Serving && ZNet.instance.GetPeers().Any(peer => peer.m_uid == uid && instance!.Protected(peer));

    private void EnsureWorld()
    {
        if (world != ZNet.instance) { reserved.Clear(); leases.Clear(); nextReply.Clear(); world = ZNet.instance; }
    }
    private void Bind(ZNetPeer peer)
    {
        EnsureWorld();
        if (!peer.IsReady() || !Protected(peer) || leases.ContainsKey(peer)) return;
        leases.Add(peer, new ObserverLease(Time.realtimeSinceStartup));
        peer.m_characterID = ZDOID.None;
        Logger.LogInfo("Read-only observer connection reserved; awaiting matching client protocol.");
    }
    private static void NewConnection(ZNetPeer __0)
    {
        if (!Serving) return;
        ObserverServerPlugin plugin = instance!;
        plugin.EnsureWorld();
        if (plugin.configured != plugin.identities.Value) plugin.ReloadAllowlist();
        // Pin the role before authentication completes. Later allowlist additions
        // must not convert a live ordinary player with already-published objects.
        if (plugin.Authorized(__0)) plugin.reserved.Add(__0);
        __0.m_rpc.Register<int>(ObserverProtocol.Hello, plugin.Hello);
    }
    private static void PeerReady(ZRpc __0)
    {
        if (Serving && Find(__0) is ZNetPeer peer) instance!.Bind(peer);
    }
    private void Hello(ZRpc rpc, int version)
    {
        if (!Serving || Find(rpc) is not ZNetPeer peer || !peer.IsReady()) return;
        if (nextReply.TryGetValue(peer, out float next) && Time.realtimeSinceStartup < next) return;
        nextReply[peer] = Time.realtimeSinceStartup + 1;
        Bind(peer);
        if (!Authorized(peer) || !leases.TryGetValue(peer, out ObserverLease lease))
        { rpc.Invoke(ObserverProtocol.Reply, ObserverProtocol.Version, 0); return; }
        if (!lease.Confirm(version, Time.realtimeSinceStartup))
        { ZNet.instance.Disconnect(peer); return; }
        rpc.Invoke(ObserverProtocol.Reply, ObserverProtocol.Version, 1);
    }
    private static bool ObserverPosition(ZRpc __0, ZPackage __1)
    {
        if (!ProtectedRpc(__0)) return true;
        // Preserve only interest. Do not publish observer gameplay/profile metadata
        // or trigger the vanilla random-event data refresh for this connection.
        try
        {
            if (__1.Size() > 65536) throw new ArgumentException("Oversized observer metadata");
            Vector3 position = __1.ReadVector3();
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z)) throw new ArgumentException("Invalid observer position");
            ZNetPeer? peer = Find(__0);
            if (peer != null) { peer.m_refPos = position; peer.m_publicRefPos = false; peer.m_serverSyncedPlayerData.Clear(); }
        }
        catch { if (Find(__0) is ZNetPeer peer) ZNet.instance.Disconnect(peer); }
        return false;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 20000;
    private static bool CharacterIdentity(ZRpc __0)
    {
        if (!ProtectedRpc(__0)) return true;
        ZNetPeer? peer = Find(__0);
        if (peer != null) peer.m_characterID = ZDOID.None;
        return false;
    }
    private static bool WorldWrite(ZRpc __0)
    {
        if (!ProtectedRpc(__0)) return true;
        instance!.blockedWrites++;
        return false;
    }
    private static bool RoutedRequest(ZRpc __0, ZPackage __1)
    {
        if (!ProtectedRpc(__0)) return true;
        // Only the vanilla read request is allowed. Authenticate the actual socket,
        // then require its session ID, no target object, and the server as recipient.
        int position = __1.GetPos();
        try
        {
            if (__1.Size() <= 1024)
            {
                // Parse the audited envelope without ReadPackage: a forged embedded
                // length must not cause a large allocation before it is validated.
                __1.ReadLong(); // message ID
                long sender = __1.ReadLong(), recipient = __1.ReadLong();
                ZDOID target = __1.ReadZDOID();
                int method = __1.ReadInt(), length = __1.ReadInt();
                if (length < 0 || length > 32 || length != __1.Size() - __1.GetPos()) return false;
                __1.ReadZDOID(); // the sole RequestZDO argument
                ZNetPeer? peer = Find(__0);
                if (__1.GetPos() == __1.Size() && peer != null && sender == peer.m_uid && target.IsNone() &&
                    recipient == ZDOMan.GetSessionID() && method == "RequestZDO".GetStableHashCode()) return true;
            }
        }
        catch { /* Malformed observer requests never reach the vanilla dispatcher. */ }
        finally { __1.SetPos(position); }
        instance!.blockedRpcs++;
        return false;
    }
    private static bool AssignNearby(long __1) => !ProtectedUid(__1);
    private static bool AssignOwner(long __0) => !ProtectedUid(__0);

    // Replace only the peer enumeration in this gameplay-facing list. The real
    // connection collection and its capacity/accounting remain intact.
    private static List<ZNetPeer> GameplayPeers(ZNet net) => !Serving ? net.GetPeers() : net.GetPeers().Where(peer => !instance!.Protected(peer)).ToList();
    private static IEnumerable<CodeInstruction> GameplayPeerList(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo field = AccessTools.Field(typeof(ZNet), "m_peers");
        int replacements = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
            { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ObserverServerPlugin), nameof(GameplayPeers)); replacements++; }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException("Unsupported player-list layout; observer isolation disabled.");
    }
    private static IEnumerable<CodeInstruction> EventPeerList(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo method = AccessTools.Method(typeof(ZNet), nameof(ZNet.GetPeers));
        int replacements = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(method))
            { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ObserverServerPlugin), nameof(GameplayPeers)); replacements++; }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException("Unsupported gameplay peer query; observer isolation disabled.");
    }
    private static int AdmissionCount(ZNet net) => Serving && (instance!.allow.Value || instance.reserved.Count > 0) ? net.GetPeerConnections() : net.GetNrOfPlayers();
    private static IEnumerable<CodeInstruction> PreserveAdmissionCount(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo method = AccessTools.Method(typeof(ZNet), nameof(ZNet.GetNrOfPlayers));
        int replacements = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(method))
            { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ObserverServerPlugin), nameof(AdmissionCount)); replacements++; }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException("Unsupported connection-capacity check; observer isolation disabled.");
    }

    private void Update()
    {
        if (!Serving) return;
        EnsureWorld();
        if (Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + 1;
        if (configured != identities.Value) ReloadAllowlist();
        foreach (ZNetPeer gone in nextReply.Keys.Where(peer => !ZNet.instance.GetPeers().Contains(peer)).ToArray()) nextReply.Remove(gone);
        foreach (ZNetPeer peer in ZNet.instance.GetPeers().ToArray()) Bind(peer);
        foreach (var entry in leases.ToArray())
        {
            if (!ZNet.instance.GetPeers().Contains(entry.Key)) { leases.Remove(entry.Key); nextReply.Remove(entry.Key); continue; }
            if (entry.Value.MustDisconnect(Time.realtimeSinceStartup, Authorized(entry.Key)))
            {
                Logger.LogWarning("Observer disconnected: handshake timeout or authorization revoked.");
                ZNet.instance.Disconnect(entry.Key);
                // Keep the role pinned until the peer is removed, including cleanup callbacks.
            }
        }
        reserved.RemoveWhere(peer => !ZNet.instance.GetPeers().Contains(peer));
    }
    private void OnDisable()
    {
        if (ZNet.instance && ZNet.instance.IsServer() && ZNet.instance.IsDedicated()) foreach (ZNetPeer peer in ZNet.instance.GetPeers().Where(Protected).ToArray()) ZNet.instance.Disconnect(peer);
        Logger.LogInfo($"Observer server stopped; rejected {blockedWrites} world-write packets and {blockedRpcs} gameplay routed calls.");
    }
    private void OnDestroy() { OnDisable(); harmony?.UnpatchSelf(); if (instance == this) instance = null; }
}
