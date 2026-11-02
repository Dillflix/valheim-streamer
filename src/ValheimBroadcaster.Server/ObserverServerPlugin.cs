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

[BepInPlugin("com.dillflix.valheimbroadcaster.server", "Valheim Broadcaster Server", "0.8.14")]
[BepInDependency(Jotunn.Main.ModGuid, "2.30.2")]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public sealed class ObserverServerPlugin : BaseUnityPlugin
{
    private static ObserverServerPlugin? instance;
    private Harmony? harmony;
    private ConfigEntry<bool> allow = null!;
    private ConfigEntry<string> identities = null!;
    private ConfigEntry<bool> discoveryEnabled = null!, sharePrivatePositions = null!;
    private ObserverDiscoveryService discovery = null!;
    private bool discoveryFailed, contextFailed;
    private ConfigEntry<bool> contextEnabled = null!;
    private readonly ObserverContextService context = new ObserverContextService();
    private HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<ZNetPeer> reserved = new HashSet<ZNetPeer>();
    private readonly Dictionary<ZNetPeer, ObserverAdmission> admissions = new Dictionary<ZNetPeer, ObserverAdmission>();
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
        identities = Config.Bind("Observer", "SteamIds", "", "Comma-separated Steam64 identities permitted to request observer mode before joining. Vanilla clients and clients with ObserverConnection=false play normally, including these identities. An accepted observer role remains read-only until disconnect.");
        discoveryEnabled = Config.Bind("Observer", "DiscoveryEnabled", true, "Supply connected-player and already-generated scenic destinations only to authenticated, allowlisted observer connections. Requires Enabled.");
        sharePrivatePositions = Config.Bind("Observer", "SharePrivatePlayerPositions", true, "Allow authorized observers to acquire player characters even when their map position is private. False limits destination coordinates to public-map players; presence remains visible. Requires Enabled and DiscoveryEnabled.");
        contextEnabled = Config.Bind("Observer", "ContextEnabled", true, "Optional timestamped character observations and replicated health-loss hints for authorized broadcasters. Does not change observer admission, activate regions, or require mods on ordinary players.");
        instance = this;
        try
        {
            ReloadAllowlist();
            discovery = new ObserverDiscoveryService();
            harmony = new Harmony("com.dillflix.valheimbroadcaster.server");
            Patch(typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }, postfix: nameof(NewConnection));
            Patch(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) }, prefix: nameof(BeginAdmission), postfix: nameof(PeerReady), transpiler: nameof(PreserveAdmissionCount));
            Patch(typeof(ZNet), "RPC_ServerSyncedPlayerData", new[] { typeof(ZRpc), typeof(ZPackage) }, prefix: nameof(ObserverPosition), postfix: nameof(PlayerMetadataReceived));
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
            Logger.LogInfo("Observer server 0.8.14 ready; protocol 3, explicit pre-admission request required. Allowlisted vanilla clients play normally. Discovery enabled=" + discoveryEnabled.Value + "; authorized private positions=" + sharePrivatePositions.Value + ".");
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
        if (world != ZNet.instance) { reserved.Clear(); admissions.Clear(); leases.Clear(); nextReply.Clear(); discovery.Reset(); context.Reset(); discoveryFailed = contextFailed = false; world = ZNet.instance; }
    }
    private void Bind(ZNetPeer peer)
    {
        EnsureWorld();
        if (!peer.IsReady() || !Protected(peer) || leases.ContainsKey(peer)) return;
        // Native PeerInfo verifies the Steam ticket before publishing m_uid/IsReady.
        // Pre-admission intent is never authority to receive discovery or approval.
        if (!Authorized(peer)) { ZNet.instance.Disconnect(peer); return; }
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
        // Membership grants permission, not a role. Vanilla peers send no request.
        plugin.admissions[__0] = new ObserverAdmission();
        __0.m_rpc.Register<int>(ObserverProtocol.Request, plugin.Request);
        __0.m_rpc.Register<int>(ObserverProtocol.Hello, plugin.Hello);
        __0.m_rpc.Register<int>(ObserverDiscoveryProtocol.Request, plugin.DiscoveryRequest);
        __0.m_rpc.Register<int>(ObserverContextProtocol.Request, plugin.ContextRequest);
    }
    private void Request(ZRpc rpc, int version)
    {
        if (!Serving || Find(rpc) is not ZNetPeer peer) return;
        if (!allow.Value || peer.m_socket is not ZSteamSocket ||
            !admissions.TryGetValue(peer, out ObserverAdmission admission) ||
            (peer.IsReady() && !Protected(peer)) || !admission.Request(version))
        {
            Logger.LogWarning("Observer request rejected: unsupported protocol/backend, disabled service, or ordinary admission already started.");
            ZNet.instance.Disconnect(peer);
            return;
        }
        // The request arrives on the same reliable Steam stream before PeerInfo.
        // Restrict this connection immediately; authorize its real identity only
        // after the game's authentication. Never release these guards in-place.
        reserved.Add(peer);
    }
    private static void BeginAdmission(ZRpc __0)
    {
        if (!Serving || Find(__0) is not ZNetPeer peer) return;
        if (!instance!.admissions.TryGetValue(peer, out ObserverAdmission admission))
            instance.admissions[peer] = admission = new ObserverAdmission();
        admission.BeginAdmission();
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
    private void DiscoveryRequest(ZRpc rpc, int version)
    {
        if (!Serving || Find(rpc) is not ZNetPeer peer || !peer.IsReady() || !Authorized(peer) ||
            !leases.TryGetValue(peer, out ObserverLease lease) || !lease.Confirmed || !discovery.MayReply(peer, Time.realtimeSinceStartup)) return;
        if (version != ObserverDiscoveryProtocol.Version) { ZNet.instance.Disconnect(peer); return; }
        try
        {
            ObserverDiscoverySnapshot snapshot = discovery.Build(peer, ZNet.instance.GetPeers(), Protected,
                ZNet.instance.GetWorldUID(), Time.realtimeSinceStartup, discoveryEnabled.Value && !discoveryFailed, sharePrivatePositions.Value);
            rpc.Invoke(ObserverDiscoveryProtocol.Reply, new ZPackage(ObserverDiscoveryProtocol.Encode(snapshot)));
        }
        catch (Exception error)
        {
            Logger.LogError("Observer discovery could not produce a bounded snapshot: " + error.GetType().Name);
            ZNet.instance.Disconnect(peer);
        }
    }
    private void ContextRequest(ZRpc rpc, int version)
    {
        if (!Serving || version != ObserverContextProtocol.Version || Find(rpc) is not ZNetPeer peer ||
            !peer.IsReady() || !Authorized(peer) || !leases.TryGetValue(peer, out ObserverLease lease) ||
            !lease.Confirmed || !context.MayReply(peer, Time.realtimeSinceStartup)) return;
        try
        {
            ObserverContextSnapshot snapshot = context.Build(ZNet.instance.GetPeers(), Protected, ZNet.instance.GetWorldUID(),
                Time.realtimeSinceStartup, contextEnabled.Value && discoveryEnabled.Value && !contextFailed, sharePrivatePositions.Value);
            rpc.Invoke(ObserverContextProtocol.Reply, new ZPackage(ObserverContextProtocol.Encode(snapshot)));
        }
        catch (Exception error)
        {
            // Context is an optional camera hint. Failure must not affect a healthy
            // observer lease, discovery stream, or rendered footage.
            contextFailed = true; context.Suspend();
            Logger.LogWarning("Optional observer context suspended after " + error.GetType().Name + "; ordinary discovery and read-only guards remain active.");
        }
    }
    private static void PlayerMetadataReceived(ZRpc __0)
    {
        if (Serving && Find(__0) is ZNetPeer peer && peer.IsReady() && !instance!.Protected(peer))
        {
            instance.discovery.ReceivedMetadata(peer, Time.realtimeSinceStartup);
            if (!instance.contextFailed && instance.contextEnabled.Value)
                instance.context.ReceivedMetadata(peer, Time.realtimeSinceStartup);
        }
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
        foreach (ZNetPeer gone in admissions.Keys.Where(peer => !ZNet.instance.GetPeers().Contains(peer)).ToArray()) admissions.Remove(gone);
        try
        {
            // Prune disconnected requesters even after optional sampling fails.
            context.Tick(ZNet.instance.GetPeers(), Protected, Time.realtimeSinceStartup,
                !contextFailed && allow.Value && discoveryEnabled.Value && contextEnabled.Value && leases.Values.Any(lease => lease.Confirmed));
        }
        catch (Exception error)
        {
            if (!contextFailed)
                Logger.LogWarning("Optional observer context suspended after " + error.GetType().Name + "; discovery remains available.");
            contextFailed = true; context.Suspend();
        }
        if (!discoveryFailed)
        {
            try
            {
                discovery.Tick(ZNet.instance.GetPeers(), Time.realtimeSinceStartup,
                    allow.Value && discoveryEnabled.Value && leases.Values.Any(lease => lease.Confirmed));
            }
            catch (Exception error)
            {
                discoveryFailed = true;
                Logger.LogError("Observer discovery suspended after " + error.GetType().Name + "; read-only guards remain active. Restart this server session after investigation.");
            }
        }
    }
    private void OnDisable()
    {
        if (ZNet.instance && ZNet.instance.IsServer() && ZNet.instance.IsDedicated()) foreach (ZNetPeer peer in ZNet.instance.GetPeers().Where(Protected).ToArray()) ZNet.instance.Disconnect(peer);
        Logger.LogInfo($"Observer server stopped; rejected {blockedWrites} world-write packets and {blockedRpcs} gameplay routed calls.");
    }
    private void OnDestroy() { OnDisable(); harmony?.UnpatchSelf(); if (instance == this) instance = null; }
}
