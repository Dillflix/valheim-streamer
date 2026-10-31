using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin.Streaming;

namespace ValheimBroadcaster.Plugin;

// A separate, opt-in startup role in the same payload. Never switch an already spawned
// character into this mode. Ordinary broadcaster/client behavior remains independent.
[BepInPlugin(Guid, "Valheim Bodyless Experiment", BroadcasterPlugin.PluginVersion)]
[BepInDependency(BroadcasterPlugin.PluginGuid)]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public sealed partial class BodylessPlugin : BaseUnityPlugin
{
    public const string Guid = "com.dillflix.valheimbroadcaster.bodyless";
    private static BodylessPlugin? instance;
    internal static bool ClientMode => instance != null && instance.client;
    private bool client, server, hooksReady, quitting;
    private Harmony harmony = null!;
    private readonly SpectatorSession session = new SpectatorSession();
    private readonly HashSet<string> allowedIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<ZNetPeer, Vector3> views = new Dictionary<ZNetPeer, Vector3>();
    private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
    private readonly HashSet<int> allowedRpcHashes = new HashSet<int>();
    private ZNet? world;
    private ZRpc? connection;
    private StreamCapture? stream;
    private BroadcastSettings streamSettings = null!;
    private ConfigEntry<Vector3> pointA = null!, pointB = null!;
    private ConfigEntry<string> worldA = null!, worldB = null!;
    private Vector3 viewPosition = Quarantine;
    private ZDOID evidenceObject;
    private float yaw = 45, pitch = 25, nextHello, nextAudit;
    private bool haveView, viewAccepted, ready;
    private int blockedRpc, blockedOwnership, blockedCreation, receivedBatches, ownedObjects, loadedObjects, spawnAttempts, cameraUpdates, viewBatchBaseline;
    private string lastReport = "";
    private readonly CameraRig hud = new CameraRig();
    private static readonly Vector3 Quarantine = new Vector3(1000000, 0, 1000000);
    private static FieldInfo Listener = null!, Sky = null!, LoadingScreen = null!;

    private void Awake()
    {
        instance = this;
        client = Config.Bind("Experiment", "ClientEnabled", false, "Startup-only bodyless spectator. Requires the companion on a disposable Steam dedicated server. Restart to change roles.").Value && !GUIManager.IsHeadless();
        server = Config.Bind("Experiment", "ServerEnabled", false, "Enable companion on a disposable dedicated server. Reserve spectator Steam IDs below. No effect on ordinary peers.").Value;
        string ids = Config.Bind("Experiment", "AllowedSteamIds", "", "Comma-separated SteamID64 values. These identities are reserved for spectators and cannot play normally while this is enabled.").Value;
        foreach (string id in ids.Split(',').Select(s => s.Trim()).Where(s => s.Length != 0))
        {
            if (id.Length != 17 || !id.All(char.IsDigit)) { Logger.LogError("Invalid AllowedSteamIds entry; server experiment disabled."); server = false; continue; }
            allowedIds.Add(id);
        }
        pointA = Config.Bind("Points", "A", Vector3.zero, "Camera position saved by spectator mark a.");
        pointB = Config.Bind("Points", "B", Vector3.zero, "Camera position saved by spectator mark b.");
        worldA = Config.Bind("Points", "WorldA", "", "World UID for point A; populated when marking.");
        worldB = Config.Bind("Points", "WorldB", "", "World UID for point B; populated when marking.");
        if (!GUIManager.IsHeadless()) CommandManager.Instance.AddConsoleCommand(new SpectatorCommand());
        if (!client && !server) return;
        harmony = new Harmony(Guid);
        foreach (string name in SpectatorSession.AllowedRpcNames) allowedRpcHashes.Add(name.GetStableHashCode());
        allowedRpcHashes.Add(0); // transport ping, not an RPC
        try
        {
            InstallPatches();
            if (global::Version.CurrentVersion.ToString() != "1.0.21") throw new InvalidOperationException("Bodyless experiment requires Valheim 1.0.21");
            if (client)
            {
                var normal = BepInEx.Bootstrap.Chainloader.PluginInfos[BroadcasterPlugin.PluginGuid].Instance;
                streamSettings = new BroadcastSettings(normal.Config);
                stream = gameObject.AddComponent<StreamCapture>();
            }
            hooksReady = true;
            Logger.LogWarning($"BODYLESS EXPERIMENT armed: client={client}, server={server}. Runtime validation pending; no player will spawn in client mode.");
        }
        catch (Exception error)
        {
            // Keep the spawn barrier installed. Never silently fall back to a normal player.
            session.Fail("Required hook unavailable; restart with ClientEnabled=false. " + error.Message);
            Logger.LogError(error);
            if (client) Application.Quit(); // A missing spawn barrier must never admit normal gameplay.
        }
    }

    private void Update()
    {
        if (!client && !server) return;
        try
        {
            if (world != ZNet.instance)
            {
                stream?.StopOutput(); hud.Restore();
                views.Clear(); registered.Clear(); connection = null; world = ZNet.instance;
                haveView = viewAccepted = ready = quitting = false;
                session.Disconnect();
                blockedRpc = blockedOwnership = blockedCreation = receivedBatches = ownedObjects = loadedObjects = spawnAttempts = cameraUpdates = 0;
                if (world && client) { session.Connect(); nextHello = 0; nextAudit = 0; }
            }
            if (!world) return;
            if (client && (!hooksReady || world.IsServer()))
            { Fault(!hooksReady ? "Required hook installation failed" : "Bodyless mode requires a remote dedicated server; local/hosted worlds are rejected"); }
            foreach (ZNetPeer peer in world.GetPeers())
            {
                if (!registered.Add(peer.m_rpc)) continue;
                peer.m_rpc.Register<ZPackage>("DillflixBodylessHello", Hello);
                peer.m_rpc.Register<ZPackage>("DillflixBodylessView", View);
                peer.m_rpc.Register<ZPackage>("DillflixBodylessAck", Ack);
            }
            foreach (ZNetPeer peer in views.Keys.Where(p => !world.GetPeers().Contains(p)).ToArray()) views.Remove(peer);
            registered.RemoveWhere(rpc => !world.GetPeers().Any(p => p.m_rpc == rpc));
            if (!client) return;
            // A bodyless client may never execute vanilla's player-driven reference setter.
            // Drive it explicitly as well as intercepting any later vanilla assignments.
            world.SetReferencePosition(haveView && viewAccepted ? viewPosition : Quarantine);
            ZNetPeer? remote = world.GetServerPeer();
            if (connection != null && (remote == null || !connection.IsConnected())) Fault("Server connection lost");
            if (remote != null && remote.IsReady())
            {
                connection = remote.m_rpc;
                if (remote.m_socket.GetType().Name != "ZSteamSocket") Fault("Experiment currently supports the Steam backend only");
                if (session.Phase == SpectatorPhase.Handshake && Time.unscaledTime >= nextHello)
                {
                    var packet = new ZPackage(); packet.Write(SpectatorSession.Protocol);
                    connection.Invoke("DillflixBodylessHello", packet); nextHello = Time.unscaledTime + 2;
                }
            }
            if (Time.unscaledTime >= nextAudit)
            {
                Audit(); nextAudit = Time.unscaledTime + 2;
                string report = Report();
                if (report != lastReport) { Logger.LogInfo(report); lastReport = report; }
            }
            bool sceneReady = haveView && viewAccepted && receivedBatches > viewBatchBaseline && loadedObjects > 0 &&
                ZNetScene.instance && !evidenceObject.IsNone() && ZNetScene.instance.FindInstance(evidenceObject) && IdleShots.Ready(viewPosition);
            // Allow vanilla authentication/loading to finish before timing the companion handshake.
            if (connection != null) session.Tick(Time.unscaledDeltaTime, sceneReady, Player.m_localPlayer, ownedObjects > 0);
            if (Player.m_localPlayer) Fault("Local player unexpectedly exists");
            ready = session.Phase == SpectatorPhase.Viewing && sceneReady;
            if (session.Phase == SpectatorPhase.Failed && !quitting) ExitWorld();
            if (session.Phase != SpectatorPhase.Failed && !Menu.IsVisible() && !global::Console.IsVisible() && !(Chat.instance && Chat.instance.HasFocus()))
            {
                if (Input.GetKeyDown(KeyCode.F8)) ExitWorld();
                if (Input.GetKeyDown(KeyCode.F6)) GoSaved(true);
                if (Input.GetKeyDown(KeyCode.F7)) GoSaved(false);
                if (Input.GetKeyDown(KeyCode.F11)) ToggleStream();
            }
        }
        catch (Exception error) { Fault("Adapter exception: " + error.Message); Logger.LogError(error); if (world && client && !quitting) ExitWorld(); }
    }

    private bool Reserved(ZNetPeer? peer) => hooksReady && server && ZNet.instance && ZNet.instance.IsDedicated() &&
        peer != null && peer.m_socket.GetType().Name == "ZSteamSocket" && allowedIds.Contains(peer.m_socket.GetHostName());
    private static ZNetPeer? FindPeer(ZRpc rpc) => ZNet.instance ? ZNet.instance.GetPeers().FirstOrDefault(p => p.m_rpc == rpc) : null;
    private bool ReservedRpc(ZRpc rpc) => ZNet.instance && Reserved(FindPeer(rpc)!);
    private bool ReservedUid(long uid) => uid != 0 && ZNet.instance && Reserved(ZNet.instance.GetPeer(uid));
    private bool ClientWorld => client && Game.instance && ZNet.instance;
    private static bool Valid(Vector3 p) => SpectatorSession.ValidPosition(p.x, p.y, p.z);

    private void Hello(ZRpc rpc, ZPackage packet)
    {
        if (!ReservedRpc(rpc) || !FindPeer(rpc)!.IsReady() || packet.Size() != 4) return;
        if (packet.ReadInt() != SpectatorSession.Protocol) return;
        ZNetPeer peer = FindPeer(rpc)!;
        peer.m_refPos = Quarantine; peer.m_publicRefPos = false;
        var ack = new ZPackage(); ack.Write(SpectatorSession.Protocol); ack.Write(0); ack.Write(true);
        ack.Write(ZDOID.None);
        rpc.Invoke("DillflixBodylessAck", ack);
    }

    private void View(ZRpc rpc, ZPackage packet)
    {
        if (!ReservedRpc(rpc) || !FindPeer(rpc)!.IsReady() || packet.Size() != 16) return;
        int request = packet.ReadInt(); Vector3 position = packet.ReadVector3();
        ZNetPeer peer = FindPeer(rpc)!;
        bool permitted = request > 0 && Valid(position) && ZoneSystem.instance &&
            (bool)IsGenerated.Invoke(ZoneSystem.instance, new object[] { ZoneSystem.GetZone(position) });
        ZDOID evidence = ZDOID.None;
        if (permitted)
        {
            var near = new List<ZDO>(); var far = new List<ZDO>();
            FindSector.Invoke(ZDOMan.instance, new object[] { ZoneSystem.GetZone(position), peer.m_simulationDistance, near, far });
            ZDO? sample = near.Where(z => z.GetPrefab() != 0).OrderBy(z => (z.GetPosition() - position).sqrMagnitude).FirstOrDefault();
            if (sample == null) permitted = false;
            else
            {
                evidence = sample.m_uid;
                // Re-send one existing destination object even on a return to cached scenery.
                // This changes only this peer's delivery bookkeeping, never the world ZDO.
                object sync = GetSyncPeer.Invoke(ZDOMan.instance, new object[] { peer.m_uid });
                ((System.Collections.IDictionary)SyncRevisions.GetValue(sync)).Remove(evidence);
                ZDOMan.instance.ForceSendZDO(peer.m_uid, evidence);
            }
        }
        if (permitted) views[peer] = position; else views.Remove(peer);
        var ack = new ZPackage(); ack.Write(SpectatorSession.Protocol); ack.Write(request); ack.Write(permitted);
        ack.Write(evidence);
        rpc.Invoke("DillflixBodylessAck", ack);
        Logger.LogInfo($"BODYLESS view request={request}, allowed={permitted}, camera={position}; gameplay reference remains quarantined.");
    }

    private void Ack(ZRpc rpc, ZPackage packet)
    {
        if (!ClientWorld || connection != rpc || packet.Size() != 21) return;
        int protocol = packet.ReadInt(), request = packet.ReadInt(); bool permitted = packet.ReadBool();
        ZDOID evidence = packet.ReadZDOID();
        if (request == 0)
        {
            if (permitted && session.AcceptServer(protocol)) GoSaved(true);
        }
        else if (protocol == SpectatorSession.Protocol && session.AcceptView(request, permitted))
        { viewAccepted = true; evidenceObject = evidence; }
    }

    private void GoSaved(bool a)
    {
        if (!ClientWorld || connection == null || (session.Phase != SpectatorPhase.Loading && session.Phase != SpectatorPhase.Viewing)) return;
        string savedWorld = a ? worldA.Value : worldB.Value;
        if (savedWorld != ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture))
        { Logger.LogWarning("Mark point " + (a ? "A" : "B") + " in this world first, or use spectator goto x y z."); return; }
        Go(a ? pointA.Value : pointB.Value);
    }

    private void Go(Vector3 position)
    {
        if (!ClientWorld || connection == null || !Valid(position)) throw new InvalidOperationException("A connected bodyless session and finite in-world coordinates are required.");
        int request = session.BeginView();
        haveView = true; viewAccepted = ready = false; viewPosition = position; evidenceObject = ZDOID.None;
        viewBatchBaseline = receivedBatches;
        var packet = new ZPackage(); packet.Write(request); packet.Write(position);
        connection.Invoke("DillflixBodylessView", packet);
        Logger.LogInfo($"BODYLESS requesting view {request} at {position}");
    }

    private void Audit()
    {
        ownedObjects = loadedObjects = 0;
        if (!ZNetScene.instance) return;
        var instances = (System.Collections.IDictionary)SceneInstances.GetValue(ZNetScene.instance);
        loadedObjects = instances.Count;
        foreach (object key in instances.Keys)
            if (key is ZDO zdo && zdo.GetOwner() == ZDOMan.GetSessionID()) ownedObjects++;
    }

    private void Fault(string reason)
    {
        if (session.Phase == SpectatorPhase.Failed) return;
        session.Fail(reason); ready = false; Logger.LogError("BODYLESS FAILED: " + reason);
    }

    private void ExitWorld()
    {
        Logger.LogWarning("BODYLESS disconnecting. " + Report());
        quitting = true; ready = false; stream?.StopOutput(); hud.Restore();
        if (Game.instance) Game.instance.Logout();
        else connection?.GetSocket().Close();
        // Spawn and write guards remain installed until process shutdown/restart.
    }

    private string Report() => $"BODYLESS {session.Phase}: {session.Failure} | view={session.Request}, readyViews={session.CompletedViews}, player={(Player.m_localPlayer ? "PRESENT" : "none")}, objects={loadedObjects}, owned={ownedObjects}, receivedBatches={receivedBatches}, cameraUpdates={cameraUpdates}, blockedRpc={blockedRpc}, ownershipAttempts={blockedOwnership}, creationAttempts={blockedCreation}, spawnAttempts={spawnAttempts} | RTMP={stream?.Status ?? "Stopped"}";

    private void ToggleStream()
    {
        if (stream == null) return;
        if (stream.Active) { stream.StopOutput(); return; }
        if (!ready) { Logger.LogWarning("Wait for a ready spectator view before starting RTMP."); return; }
        GameCamera camera = FindFirstObjectByType<GameCamera>();
        AudioListener? listener = camera ? Listener.GetValue(camera) as AudioListener : null;
        if (!listener) throw new InvalidOperationException("No game audio listener");
        stream.Begin(new StreamOptions {
            Executable = streamSettings.FFmpegPath.Value, Url = streamSettings.RtmpUrl.Value,
            Width = streamSettings.StreamWidth.Value, Height = streamSettings.StreamHeight.Value,
            Fps = streamSettings.StreamFps.Value, BitrateKbps = streamSettings.StreamBitrate.Value,
            SampleRate = AudioSettings.outputSampleRate, FlipVertical = streamSettings.FlipVertical.Value
        }, listener);
    }

    private void OnGUI()
    {
        if (!ClientWorld) return;
        GUI.depth = -1000;
        if (!ready) { GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = Color.white; }
        GUI.Box(new Rect(10, 10, Screen.width - 20, 128), Report());
        if (GUI.Button(new Rect(20, 70, 110, 28), "Point A [F6]")) GoSaved(true);
        if (GUI.Button(new Rect(140, 70, 110, 28), "Point B [F7]")) GoSaved(false);
        if (GUI.Button(new Rect(260, 70, 110, 28), "Stream [F11]")) { try { ToggleStream(); } catch (Exception e) { Logger.LogWarning(e.Message); } }
        if (GUI.Button(new Rect(380, 70, 130, 28), "Disconnect [F8]")) ExitWorld();
        if (GUI.Button(new Rect(520, 70, 70, 28), "Left")) yaw -= 30;
        if (GUI.Button(new Rect(600, 70, 70, 28), "Right")) yaw += 30;
        if (GUI.Button(new Rect(680, 70, 70, 28), "Up")) pitch = Mathf.Max(-70, pitch - 10);
        if (GUI.Button(new Rect(760, 70, 70, 28), "Down")) pitch = Mathf.Min(70, pitch + 10);
    }

    private void OnDestroy()
    {
        stream?.StopOutput(); hud.Restore();
        if (client && ZNet.instance) connection?.GetSocket().Close();
        harmony?.UnpatchSelf(); if (instance == this) instance = null;
    }

    private void OnDisable()
    {
        if (client && world && !quitting) ExitWorld();
        if (server && ZNet.instance)
            foreach (ZNetPeer peer in ZNet.instance.GetPeers().Where(Reserved).ToArray()) peer.m_socket.Close();
    }

    private sealed class SpectatorCommand : ConsoleCommand
    {
        public override string Name => "spectator";
        public override string Help => "spectator mark a|b / a / b / goto x y z / status / stream / stop. Enable startup role in bodyless config after marking two points.";
        public override void Run(string[] args, Terminal context)
        {
            BodylessPlugin? p = instance; if (p == null) return;
            try
            {
                if (args.Length == 2 && args[0] == "mark" && (args[1] == "a" || args[1] == "b"))
                {
                    if (p.client || !Player.m_localPlayer || !ZNet.instance) throw new InvalidOperationException("Mark in ordinary mode with a spawned player; then restart with ClientEnabled=true.");
                    bool a = args[1] == "a";
                    (a ? p.pointA : p.pointB).Value = Player.m_localPlayer.transform.position + Vector3.up * 12;
                    (a ? p.worldA : p.worldB).Value = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
                    context.AddString("Saved camera point " + args[1].ToUpperInvariant() + " for this world."); return;
                }
                if (args.Length == 4 && args[0] == "goto") p.Go(new Vector3(Parse(args[1]), Parse(args[2]), Parse(args[3])));
                else if (args.Length == 1 && args[0] == "a") p.GoSaved(true);
                else if (args.Length == 1 && args[0] == "b") p.GoSaved(false);
                else if (args.Length == 1 && args[0] == "stop") { if (p.client) p.ExitWorld(); }
                else if (args.Length == 1 && args[0] == "stream") p.ToggleStream();
                else if (args.Length > 1 || (args.Length == 1 && args[0] != "status")) context.AddString(Help);
                context.AddString(p.Report());
            }
            catch (Exception e) { context.AddString(e.Message); }
        }
        private static float Parse(string text) => float.Parse(text, CultureInfo.InvariantCulture);
    }
}
