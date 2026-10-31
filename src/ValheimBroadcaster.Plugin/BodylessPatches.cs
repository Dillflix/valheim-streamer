using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

public sealed partial class BodylessPlugin
{
    private static FieldInfo SceneInstances = null!, InitZdo = null!;
    private static MethodInfo IsGenerated = null!;
    private static FieldInfo SyncPeer = null!;
    private static FieldInfo SyncRevisions = null!;
    private static MethodInfo FindSector = null!, GetSyncPeer = null!;
    private static FieldInfo[] SpawnFlags = null!;

    private static FieldInfo RequiredField(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
    private void Patch(Type type, string method, Type[] args, string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        MethodInfo target = AccessTools.DeclaredMethod(type, method, args) ?? throw new MissingMethodException(type.FullName, method);
        harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(BodylessPlugin), prefix),
            postfix == null ? null : new HarmonyMethod(typeof(BodylessPlugin), postfix), null,
            finalizer == null ? null : new HarmonyMethod(typeof(BodylessPlugin), finalizer), null);
    }

    private void InstallPatches()
    {
        // These barriers are deliberately first and remain installed on initialization failure.
        if (client)
        {
            Patch(typeof(Game), "SpawnPlayer", new[] { typeof(Vector3), typeof(bool) }, nameof(NoSpawn));
            Patch(typeof(Game), "UpdateRespawn", new[] { typeof(float) }, nameof(NoRespawn));
            Patch(typeof(Game), "RequestRespawn", new[] { typeof(float), typeof(bool) }, nameof(NoRespawn));
            Patch(typeof(Game), "SavePlayerProfile", new[] { typeof(bool), typeof(bool) }, nameof(NoRespawn));
            Patch(typeof(ZNet), "SaveWorld", new[] { typeof(bool) }, nameof(NoRespawn));
            Listener = RequiredField(typeof(GameCamera), "m_listner");
            Sky = RequiredField(typeof(GameCamera), "m_skyCamera");
            LoadingScreen = RequiredField(typeof(Hud), "m_loadingScreen");
            SceneInstances = RequiredField(typeof(ZNetScene), "m_instances");
            InitZdo = RequiredField(typeof(ZNetView), "m_initZDO");
            SpawnFlags = new[] { "m_requestRespawn", "m_queuedIntro", "m_inIntro" }.Select(n => RequiredField(typeof(Game), n)).ToArray();
            Patch(typeof(Game), "Start", Type.EmptyTypes, postfix: nameof(AfterGameStart));
            Patch(typeof(Game), "WaitingForRespawn", Type.EmptyTypes, nameof(NotWaiting));
            Patch(typeof(ZNet), "SetReferencePosition", new[] { typeof(Vector3) }, nameof(ClientReference));
            Patch(typeof(ZNet), "SendServerSyncPlayerData", new[] { typeof(ZNetPeer) }, nameof(NoRespawn));
            Patch(typeof(ZoneSystem), "Update", Type.EmptyTypes, nameof(ClientSceneGate));
            Patch(typeof(ZNetScene), "CreateDestroyObjects", Type.EmptyTypes, nameof(ClientSceneGate));
            Patch(typeof(ZNetView), "ClaimOwnership", Type.EmptyTypes, nameof(NoClaim));
            Patch(typeof(ZDO), "IsOwner", Type.EmptyTypes, nameof(NotOwner));
            Patch(typeof(ZDO), "get_Owner", Type.EmptyTypes, nameof(NotOwner));
            Patch(typeof(ZNetView), "Awake", Type.EmptyTypes, nameof(ViewAwake));
            Patch(typeof(ZRoutedRpc), "InvokeRoutedRPC", new[] { typeof(long), typeof(ZDOID), typeof(string), typeof(object[]) }, nameof(NoRouted));
            Patch(typeof(ZRpc), "Invoke", new[] { typeof(string), typeof(object[]) }, nameof(OutgoingRpc));
            Patch(typeof(ZDOMan), "RPC_ZDOData", new[] { typeof(ZRpc), typeof(ZPackage) }, postfix: nameof(ReceivedData));
            Patch(typeof(GameCamera), "UpdateCamera", new[] { typeof(float) }, nameof(CameraUpdate));
            Patch(typeof(GameCamera), "UpdateListner", Type.EmptyTypes, postfix: nameof(ListenerUpdate));
            Patch(typeof(GameCamera), "UpdateMouseCapture", Type.EmptyTypes, nameof(MouseCapture));
            Patch(typeof(Hud), "UpdateBlackScreen", new[] { typeof(Player), typeof(float) }, nameof(BlackScreen));
        }
        Patch(typeof(ZDO), "SetOwner", new[] { typeof(long) }, nameof(AssignOwner));
        Patch(typeof(ZDO), "SetOwnerInternal", new[] { typeof(long) }, nameof(AssignOwner));
        if (server)
        {
            Patch(typeof(ZRpc), "HandlePackage", new[] { typeof(ZPackage) }, nameof(IncomingRpc));
            Patch(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) }, postfix: nameof(PeerReady));
            Patch(typeof(ZNet), "UpdatePlayerList", Type.EmptyTypes, postfix: nameof(FilterPlayers));
            Patch(typeof(ZDOMan), "IsInPeerActiveArea", new[] { typeof(Vector3), typeof(long) }, nameof(NotActivePeer));
            Patch(typeof(ZoneSystem), "CreateGhostZones", new[] { typeof(Vector3) }, nameof(NoQuarantineGeneration));
            Type syncType = AccessTools.Inner(typeof(ZDOMan), "ZDOPeer") ?? throw new MissingMemberException("ZDOMan.ZDOPeer");
            SyncPeer = RequiredField(syncType, "m_peer");
            SyncRevisions = RequiredField(syncType, "m_zdos");
            FindSector = AccessTools.Method(typeof(ZDOMan), "FindSectorObjects", new[] { typeof(Vector2s), typeof(SimulationDistance), typeof(List<ZDO>), typeof(List<ZDO>) }) ?? throw new MissingMethodException("FindSectorObjects");
            GetSyncPeer = AccessTools.Method(typeof(ZDOMan), "GetPeer", new[] { typeof(long) }) ?? throw new MissingMethodException("ZDOMan.GetPeer");
            Patch(typeof(ZDOMan), "CreateSyncList", new[] { syncType, typeof(List<ZDO>) }, nameof(BeginSync), finalizer: nameof(EndSync));
        }
        IsGenerated = AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated", new[] { typeof(Vector2s) }) ?? throw new MissingMethodException("ZoneSystem.IsZoneGenerated");
    }

    private static bool NoRespawn() => !ClientMode;
    private static bool ClientSceneGate() => instance?.ClientWorld != true ||
        (instance.haveView && instance.viewAccepted && !instance.quitting && instance.session.Phase != SpectatorPhase.Failed);
    private static bool NoSpawn(ref Player __result)
    {
        if (!ClientMode) return true;
        instance!.spawnAttempts++; instance.Fault("Game attempted to spawn a player despite the respawn barrier");
        __result = null!; return false;
    }
    private static bool NotWaiting(ref bool __result) { if (!ClientMode) return true; __result = false; return false; }
    private static void AfterGameStart(Game __instance)
    {
        if (!ClientMode) return;
        foreach (FieldInfo flag in SpawnFlags) flag.SetValue(__instance, false);
        __instance.CancelInvoke("_RequestRespawn");
    }
    private static void ClientReference(ref Vector3 __0)
    {
        if (ClientMode) __0 = instance!.haveView && instance.viewAccepted ? instance.viewPosition : Quarantine;
    }
    private static bool NoClaim()
    {
        if (instance?.ClientWorld != true) return true;
        instance.blockedOwnership++; return false;
    }
    private static bool NotOwner(ref bool __result)
    {
        if (instance?.ClientWorld != true) return true;
        __result = false; return false;
    }
    private static bool AssignOwner(long __0)
    {
        BodylessPlugin? p = instance;
        if (p == null || __0 == 0) return true;
        if (p.ClientWorld && __0 == ZDOMan.GetSessionID())
        { p.blockedOwnership++; p.Fault("Assignment of simulation ownership to spectator was blocked"); return false; }
        if (p.ReservedUid(__0))
        {
            p.blockedOwnership++;
            if (p.blockedOwnership <= 5) p.Logger.LogWarning("BODYLESS server blocked ownership assignment to a reserved spectator.");
            return false;
        }
        return true;
    }
    private static bool ViewAwake(ZNetView __instance)
    {
        if (instance?.ClientWorld != true || InitZdo.GetValue(null) != null) return true;
        // Received replicas have an init ZDO; a newly authored object does not.
        instance.blockedCreation++;
        instance.Fault("A prefab attempted to create a new network object: " + __instance.gameObject.name);
        Destroy(__instance.gameObject); return false;
    }
    private static bool NoRouted()
    {
        if (instance?.ClientWorld != true) return true;
        instance.blockedRpc++; return false;
    }
    private static bool OutgoingRpc(ZRpc __instance, string __0)
    {
        BodylessPlugin? p = instance;
        if (p?.ClientWorld != true) return true;
        ZNetPeer? peer = FindPeer(__instance);
        // Authentication is vanilla. Restrict the peer as soon as it becomes ready;
        // also block gameplay channels during handshake (defence in depth).
        if ((peer == null || !peer.IsReady()) && __0 != "ZDOData" && __0 != "RoutedRPC" && __0 != "CharacterID") return true;
        if (SpectatorSession.AllowsRpc(__0)) return true;
        p.blockedRpc++;
        if (p.blockedRpc <= 5) p.Logger.LogWarning("BODYLESS blocked outbound RPC: " + __0);
        return false;
    }
    private static bool IncomingRpc(ZRpc __instance, ZPackage __0)
    {
        BodylessPlugin? p = instance;
        if (p == null || !p.ReservedRpc(__instance)) return true;
        ZNetPeer peer = FindPeer(__instance)!;
        if (!peer.IsReady()) return true; // successful vanilla authentication is mandatory
        int position = __0.GetPos();
        try
        {
            int method = __0.ReadInt();
            if (p.allowedRpcHashes.Contains(method)) return true;
            p.blockedRpc++;
            if (p.blockedRpc <= 5) p.Logger.LogWarning($"BODYLESS server rejected gameplay RPC hash {method} from reserved spectator peer.");
            return false;
        }
        catch { p.blockedRpc++; return false; }
        finally { __0.SetPos(position); }
    }
    private static void PeerReady(ZNet __instance, ZRpc __0)
    {
        ZNetPeer? peer = FindPeer(__0);
        if (instance == null || !instance.Reserved(peer) || !peer!.IsReady()) return;
        peer.m_refPos = Quarantine; peer.m_publicRefPos = false;
        instance.Logger.LogInfo("BODYLESS reserved authenticated Steam peer; no gameplay interest or ownership permitted.");
    }
    private static void FilterPlayers(ZNet __instance)
    {
        BodylessPlugin? p = instance;
        if (p == null || !p.server || !__instance.IsDedicated()) return;
        var ids = new HashSet<string>(__instance.GetPeers().Where(p.Reserved).Select(peer => peer.m_socket.GetHostName()), StringComparer.Ordinal);
        __instance.GetPlayerList().RemoveAll(info => ids.Contains(info.m_userInfo.m_id.m_userID));
    }
    private static bool NotActivePeer(long __1, ref bool __result)
    {
        if (instance?.ReservedUid(__1) != true) return true;
        __result = false; return false;
    }
    private static bool NoQuarantineGeneration(Vector3 __0, ref bool __result)
    {
        if (instance == null || !instance.server || !ZNet.instance || !ZNet.instance.IsDedicated() || __0 != Quarantine) return true;
        __result = false; return false;
    }
    private sealed class SyncRestore
    {
        internal readonly ZNetPeer Peer; internal readonly Vector3 Position;
        internal SyncRestore(ZNetPeer peer) { Peer = peer; Position = peer.m_refPos; }
    }
    private static bool BeginSync(object __0, List<ZDO> __1, ref SyncRestore? __state)
    {
        BodylessPlugin? p = instance;
        ZNetPeer peer = (ZNetPeer)SyncPeer.GetValue(__0);
        if (p == null || !p.Reserved(peer)) return true;
        if (!p.views.TryGetValue(peer, out Vector3 view)) { __1.Clear(); return false; }
        __state = new SyncRestore(peer); peer.m_refPos = view;
        return true;
    }
    private static Exception? EndSync(Exception? __exception, SyncRestore? __state)
    {
        // Also restores on exceptions. Only synchronous selection of outbound data sees the camera reference.
        if (__state != null) __state.Peer.m_refPos = __state.Position;
        return __exception;
    }
    private static void ReceivedData() { if (instance?.ClientWorld == true) instance.receivedBatches++; }
    private static bool CameraUpdate(GameCamera __instance)
    {
        BodylessPlugin? p = instance;
        if (p?.ClientWorld != true) return true;
        if (p.quitting) return false;
        try
        {
            Camera camera = __instance.GetComponent<Camera>() ?? __instance.GetComponentInChildren<Camera>();
            if (!camera) throw new InvalidOperationException("GameCamera has no Camera");
            camera.transform.SetPositionAndRotation(p.viewPosition, Quaternion.Euler(p.pitch, p.yaw, 0));
            camera.fieldOfView = 75;
            p.cameraUpdates++;
            if (Sky.GetValue(__instance) is Camera sky) sky.fieldOfView = 75;
            p.hud.HideHud(true);
        }
        catch (Exception error) { p.Fault("Camera update: " + error.Message); }
        return false;
    }
    private static void ListenerUpdate(GameCamera __instance)
    {
        BodylessPlugin? p = instance;
        if (p?.ClientWorld != true) return;
        if (Listener.GetValue(__instance) is AudioListener listener)
            listener.transform.SetPositionAndRotation(p.viewPosition, Quaternion.Euler(p.pitch, p.yaw, 0));
    }
    private static bool MouseCapture()
    {
        if (instance?.ClientWorld != true) return true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true; return false;
    }
    private static bool BlackScreen(Hud __instance)
    {
        if (instance?.ClientWorld != true || instance.quitting) return true;
        if (LoadingScreen.GetValue(__instance) is CanvasGroup screen)
        { screen.alpha = 0; screen.gameObject.SetActive(false); }
        return false; // Our explicit loading/failure slate replaces the player-dependent vanilla screen.
    }
}
