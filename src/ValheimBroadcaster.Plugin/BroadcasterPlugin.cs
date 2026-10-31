using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin.Streaming;

namespace ValheimBroadcaster.Plugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid, "2.30.2")]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public sealed class BroadcasterPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.dillflix.valheimbroadcaster";
    public const string PluginName = "Valheim Broadcaster";
    public const string PluginVersion = "0.8.1";
    private static BroadcasterPlugin? instance;
    private Harmony? harmony;
    private StreamCapture? stream;
    private StreamRecovery? output;
    private ConnectionRecovery? connection;
    private AutonomousBroadcast? automatic;
    private bool? unattendedOverride;
    private bool backgroundOwned, previousBackground;
    private string? presentedPlayerId;
    private int presentedFrame = -100;
    private float nextStatusLog;
    private bool terminalReported;
    private bool listenerPatchAvailable;
    private static readonly FieldInfo? ListenerField = AccessTools.Field(typeof(GameCamera), "m_listner");
    private BroadcastSettings settings = null!;
    private readonly BroadcastDirector director = new BroadcastDirector();
    private readonly ObserverController observer = new ObserverController();
    private bool observerHooksAvailable;
    private RemoteObserverClient remote = null!;
    private float nextObserverStart;
    private readonly ObserverVisualMask observerVisuals = new ObserverVisualMask();
    private readonly SceneProbe probe = new SceneProbe();
    private Vector3? probeMarker;
    private ZNet? probeWorld;
    private Vector3 probeOrigin;
    private readonly CoverageDirector coverage = new CoverageDirector();
    private readonly BroadcastDirector acquisition = new BroadcastDirector();
    private readonly AcquisitionWindow acquisitionWindow = new AcquisitionWindow();
    private readonly IdleShots idle = new IdleShots();
    private float nextCreatureRefresh;
    private float nextScenicSearch;
    private readonly TargetRoster roster = new TargetRoster();
    private readonly CameraRig rig = new CameraRig();
    private readonly FrameHold frameHold = new FrameHold();
    private float nextRosterRefresh;
    private bool remotePatchAvailable;
    private bool cameraReady;
    private bool selfTest;
    private ZNet? activeWorld;
    private Vector3? remoteReference;
    private ZDO? retainedObserver;
    private string? acquisitionFailure;
    private Vector3? normalReference;
    private ZNet? referenceOwner;
    private long lastRevision = -1;
    private string lastStatus = "Disabled";

    private void Awake()
    {
        if (GUIManager.IsHeadless()) { enabled = false; return; }
        settings = new BroadcastSettings(Config);
        remote = new RemoteObserverClient(() => settings.ObserverConnection.Value, message => Logger.LogInfo(message));
        observer.RemoteApproved = () => remote.Isolated && remote.Approved;
        automatic = new AutonomousBroadcast(settings, remote, observer, ResetShot, message => Logger.LogInfo(message));
        connection = new ConnectionRecovery(() => WantsUnattended, () => settings.ObserverConnection.Value,
            message => Logger.LogInfo(message), reason => remote.Fail(reason), () => Stop(true));
        connection.ManualStopped += CancelOperatorIntent;
        instance = this;
        MethodInfo? cameraMethod = AccessTools.Method(typeof(GameCamera), "UpdateCamera", new[] { typeof(float) });
        if (cameraMethod == null)
        {
            Logger.LogError("Unsupported Valheim build: GameCamera.UpdateCamera(float) was not found. Broadcaster disabled.");
            enabled = false;
            instance = null;
            return;
        }
        try
        {
            harmony = new Harmony(PluginGuid);
            remote.Install(harmony);
            connection.Install(harmony);
            harmony.Patch(cameraMethod, postfix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(AfterCameraUpdate)));
            stream = gameObject.AddComponent<StreamCapture>();
            output = new StreamRecovery(stream, BuildStreamOptions, FindAudioListener, message => Logger.LogInfo(message));
            MethodInfo? listenerMethod = AccessTools.Method(typeof(GameCamera), "UpdateListner", Type.EmptyTypes);
            if (listenerMethod != null && ListenerField != null)
            {
                harmony.Patch(listenerMethod, postfix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(AfterListenerUpdate)));
                listenerPatchAvailable = true;
            }
            MethodInfo? referenceMethod = AccessTools.Method(typeof(ZNet), "SetReferencePosition", new[] { typeof(Vector3) });
            MethodInfo? removeMethod = AccessTools.Method(typeof(ZNetScene), "RemoveObjects", new[] { typeof(List<ZDO>), typeof(List<ZDO>) });
            if (referenceMethod != null && removeMethod != null)
            {
                harmony.Patch(removeMethod, prefix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(BeforeRemoveObjects)));
                harmony.Patch(referenceMethod, prefix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(BeforeReferencePosition)));
                remotePatchAvailable = true;
            }
            var observerMethods = new[] {
                AccessTools.Method(typeof(Player), "FixedUpdate"), AccessTools.Method(typeof(Player), "Update"),
                AccessTools.Method(typeof(Player), "SetControls"), AccessTools.Method(typeof(Character), "CustomFixedUpdate", new[] { typeof(float) }) };
            MethodInfo? openServer = AccessTools.Method(typeof(ZNet), "OpenServer", Type.EmptyTypes);
            if (remotePatchAvailable && openServer != null && observerMethods.All(method => method != null))
            {
                harmony.Patch(observerMethods[0], prefix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(ObserverFixedUpdate)));
                foreach (MethodInfo method in observerMethods.Skip(1))
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(ObserverInputOrPhysics)));
                harmony.Patch(openServer, prefix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(BeforeOpenServer)));
                observerHooksAvailable = true;
            }
            StartCoroutine(frameHold.Capture(() => director.IsEnabled, () => cameraReady && !IsTextOrMenuOpen(), message => Logger.LogWarning(message)));
            CommandManager.Instance.AddConsoleCommand(new BroadcastCommand());
            Logger.LogInfo($"{PluginName} {PluginVersion} ready. F8 toggles; F10 starts self-follow; broadcast help lists commands. Starts disabled.");
        }
        catch (Exception error)
        {
            Logger.LogError($"Broadcaster initialization failed: {error}");
            harmony?.UnpatchSelf();
            instance = null;
            enabled = false;
        }
    }

    private void Update()
    {
        if (settings == null) return;
        try { remote.Tick(); }
        catch (Exception error) { remote.Fail("Observer connection error: " + error.GetType().Name); }
        if (remote.TerminalFailure && !terminalReported)
        { terminalReported = true; connection?.Reject(remote.Status); CancelIntent(); }
        if (remote.Approved) terminalReported = false;
        connection?.Tick(remote.Isolated && remote.Approved, observer.Active);
        UpdateBackground();
        bool allowedOutput = director.IsEnabled && Player.m_localPlayer && ZNet.instance && activeWorld == ZNet.instance &&
            (!remote.Isolated || remote.Approved);
        if (allowedOutput && remote.Isolated && observer.Active) output?.ArmAutoStart(settings.AutoStream.Value);
        output?.Tick(allowedOutput, settings.RetryStream.Value);
        if (!ZNet.instance || probeWorld != ZNet.instance) { probeMarker = null; probeWorld = null; }
        if (!Player.m_localPlayer || Player.m_localPlayer.IsDead() || !ZNet.instance || ((director.IsEnabled || observer.Active) && activeWorld != ZNet.instance))
        {
            if (director.IsEnabled || observer.Active)
            {
                if (remoteReference.HasValue && !Player.m_localPlayer && ZNet.instance)
                {
                    acquisitionFailure = "Local player became unavailable during acquisition. Stopped; rejoin if the normal view does not return.";
                    probe.Fail(acquisitionFailure);
                    Logger.LogError(acquisitionFailure);
                }
                Stop(true);
            }
            return;
        }
        if (remote.Approved && remote.Isolated && !observer.Active && connection?.AllowsObserverActivation != false && Time.unscaledTime >= nextObserverStart)
        { nextObserverStart = Time.unscaledTime + 1; TryStartRemoteObserver(); }
        // Hotkeys must not fire while the player types into chat/console or navigates a menu.
        if (!IsTextOrMenuOpen())
        {
            if (settings.Toggle.Value.IsDown())
            { if (director.IsEnabled) RequestStop(); else { connection?.OnManualStart(); StartBroadcast(); } }
            else if (settings.SelfTest.Value.IsDown()) { connection?.OnManualStart(); StartBroadcast(true); }
            if (director.IsEnabled)
            {
                if (settings.StreamToggle.Value.IsDown()) ToggleStream();
                if (settings.Next.Value.IsDown()) MoveTarget(1);
                if (settings.Previous.Value.IsDown()) MoveTarget(-1);
                if (settings.Hold.Value.IsDown()) SetHolding(!director.IsHolding);
            }
        }
        try
        {
            ObserverPhase previousPhase = observer.Phase;
            if (observer.Tick(Time.deltaTime))
            {
                Stop(true);
                Logger.LogInfo("Observer session ended; owned state restored.");
                return;
            }
            if (Time.unscaledTime >= nextRosterRefresh)
            { RefreshRoster(); nextRosterRefresh = Time.unscaledTime + 0.5f; }
            bool recentPicture = cameraReady && Time.frameCount - presentedFrame <= 2;
            automatic?.Tick(Time.unscaledDeltaTime, roster, recentPicture ? presentedPlayerId : null,
                recentPicture && (coverage.Mode == CoverageMode.Scenic || coverage.Mode == CoverageMode.Creature));
            string? reconnect = automatic?.TakeReconnectReason();
            if (reconnect != null && connection?.RequestRecovery(reconnect) == true) return;
            if (automatic?.ControlsSelection == true && automatic.TargetId != null) director.Select(automatic.TargetId);
            if (previousPhase == ObserverPhase.Returning && observer.Phase == ObserverPhase.Parked)
            { Stop(true, true); Logger.LogInfo("Observer parked; read-only role remains until disconnect. F8 resumes coverage."); return; }
            if (!director.IsEnabled) return;
            if (previousPhase == ObserverPhase.Travelling && observer.Phase == ObserverPhase.Watching)
                ResetShot();
            if (observer.Active && observer.Phase != ObserverPhase.Watching)
            {
                cameraReady = false;
                LogStatus();
                return;
            }
            director.DwellSeconds = settings.Dwell.Value;
            director.TargetTimeoutSeconds = settings.Timeout.Value;
            TargetRoster.Target? current = CurrentTarget();
            // A lost/dead subject must not block another ready player until its dwell expires.
            if (current != null && !IsReady(current)) { RefreshRoster(); current = CurrentTarget(); }
            if (automatic?.ControlsSelection != true) director.Tick(Time.unscaledDeltaTime, IsReady(current));
            current = CurrentTarget();
            bool readyPlayer = CanShow(current);
            if (!readyPlayer && observer.RemoteSession && !idle.SceneReady && IdleShots.Ready(Player.m_localPlayer.transform.position))
                ResetShot();
            if (Time.unscaledTime >= nextCreatureRefresh)
            {
                idle.Refresh(settings.CreatureRadius.Value);
                nextCreatureRefresh = Time.unscaledTime + 1f;
            }
            coverage.ScenicSeconds = settings.ScenicDwell.Value;
            coverage.CreatureSeconds = settings.CreatureDwell.Value;
            coverage.Tick(Time.unscaledDeltaTime, readyPlayer ? current!.Identity.Id : null,
                roster.ConnectedOthers, !selfTest && idle.SceneReady, idle.ReadyIds(), director.IsHolding);
            if (coverage.Revision != lastRevision)
            {
                lastRevision = coverage.Revision;
                rig.ResetPose();
                if (coverage.Mode == CoverageMode.Scenic) idle.BeginScenicPath();
                // Presentation readiness belongs to the camera hook, not a provisional director cut.
            }
            if (coverage.Mode == CoverageMode.Scenic && !idle.HasScenicPath && Time.unscaledTime >= nextScenicSearch)
            { idle.BeginScenicPath(); nextScenicSearch = Time.unscaledTime + 5f; }
            bool wasAcquiring = acquisitionWindow.Active;
            bool acquiring = acquisitionWindow.Tick(Time.unscaledDeltaTime,
                !observer.Active && !selfTest && !readyPlayer && settings.RemoteTargets.Value && remotePatchAvailable && acquisition.Roster.Count > 0,
                settings.Timeout.Value, settings.AcquisitionCooldown.Value);
            if (wasAcquiring && !acquiring) acquisition.Next();
            bool probing = probe.Active;
            if (probing)
            {
                bool sceneReady = probeMarker.HasValue && IdleShots.Ready(probeMarker.Value) && ProbeCameraAreaReady(probeMarker.Value);
                probe.Tick(Time.unscaledDeltaTime, sceneReady);
                if (probe.Active) SetReference(probeMarker!.Value);
                else { RestoreReference(); rig.ResetPose(); }
            }
            else if (!observer.Active) UpdateReference(readyPlayer ? current : acquiring ? roster.Find(acquisition.CurrentTarget?.Id) : null);
            LogStatus();
        }
        catch (Exception error) { FailClosed(error); }
    }

    private void LogStatus()
    {
        string status = Status();
        if (status != lastStatus && Time.unscaledTime >= nextStatusLog)
        { Logger.LogInfo(status); lastStatus = status; nextStatusLog = Time.unscaledTime + 5; }
    }

    private static bool IsTextOrMenuOpen() =>
        (global::Console.instance && global::Console.IsVisible()) ||
        (Chat.instance && Chat.instance.HasFocus()) || Menu.IsVisible() ||
        (InventoryGui.instance && InventoryGui.IsVisible()) || TextInput.IsVisible();

    private void RefreshRoster()
    {
        roster.Refresh(settings.RemoteTargets.Value && remotePatchAvailable, selfTest,
            observer.RemoteSession ? remote.Discovery : null);
        if (selfTest && roster.Targets.Count == 1)
            director.SetSelfTarget(roster.Identities().Single());
        else
            director.SetRoster(roster.Targets.Values.Where(target => automatic?.ControlsSelection == true || IsReady(target)).Select(target => target.Identity),
                Player.m_localPlayer ? Player.m_localPlayer.GetZDOID().ToString() : null);
        acquisition.SetRoster(roster.Targets.Values.Where(target => target.PublicPosition.HasValue && !IsReady(target)).Select(target => target.Identity));
    }

    private TargetRoster.Target? CurrentTarget() => roster.Find(automatic?.ControlsSelection == true ? automatic.TargetId : director.CurrentTarget?.Id);
    private bool CanShow(TargetRoster.Target? target) => IsReady(target) && (!observer.RemoteSession || automatic?.CanFrame(target) == true);
    private void ResetShot()
    {
        if (Player.m_localPlayer) idle.SetRegion(Player.m_localPlayer.transform.position);
        coverage.Reset(); rig.ResetPose(); cameraReady = false; presentedPlayerId = null;
        nextCreatureRefresh = nextScenicSearch = 0;
    }
    private void MoveTarget(int direction)
    {
        if (automatic?.ControlsSelection == true) automatic.Next(direction);
        else if (direction > 0) director.Next(); else director.Previous();
    }
    private void SetHolding(bool held) { director.SetHolding(held); automatic?.SetHolding(held); }
    private bool WantsUnattended => unattendedOverride ?? settings.Unattended.Value;
    private void UpdateBackground()
    {
        bool desired = WantsUnattended && (connection?.Armed == true || remote.Isolated && remote.Approved && director.IsEnabled);
        if (desired && !backgroundOwned) { previousBackground = Application.runInBackground; backgroundOwned = true; Application.runInBackground = true; }
        else if (!desired && backgroundOwned) { Application.runInBackground = previousBackground; backgroundOwned = false; }
    }
    private void CancelIntent() { automatic?.PauseForManual(); output?.RequestStop(); }
    private void CancelOperatorIntent()
    {
        automatic?.PauseForManual();
        // Native initial join also resets recovery state. Suppress future auto-start
        // only when an operator stop is cancelling existing publishing intent.
        if (output?.Desired == true) output.RequestStop();
    }
    private void RequestStop()
    {
        CancelIntent(); connection?.OnManualStop(); Stop(); UpdateBackground();
    }

    private static bool IsReady(TargetRoster.Target? target)
    {
        if (target?.LoadedPlayer == null || !target.LoadedPlayer || target.LoadedPlayer.IsDead()) return false;
        return ZNetScene.instance && TargetRoster.Finite(target.LoadedPlayer.transform.position) &&
               ZNetScene.instance.IsAreaReady(target.LoadedPlayer.transform.position);
    }

    private bool StartBroadcast(bool testSelf = false)
    {
        if (!enabled || !Player.m_localPlayer || Player.m_localPlayer.IsDead() || !ZNet.instance || !ZNetScene.instance) return false;
        if (remote.Isolated && (testSelf || !remote.Approved)) return false;
        if (observer.Phase == ObserverPhase.Parked)
        {
            observer.Resume();
            director.SetEnabled(true); activeWorld = ZNet.instance;
            idle.SetRegion(Player.m_localPlayer.transform.position); coverage.Reset(); rig.ResetPose();
            nextRosterRefresh = nextCreatureRefresh = nextScenicSearch = 0;
            automatic?.Started(); connection?.OnManualStart(); SetHolding(false);
            return true;
        }
        if (observer.Active) return false;
        Stop(); // Restore any previous camera/HUD/reference before switching modes.
        acquisitionFailure = null;
        selfTest = testSelf;
        activeWorld = ZNet.instance;
        idle.SetRegion(Player.m_localPlayer.transform.position);
        nextRosterRefresh = nextCreatureRefresh = 0;
        RefreshRoster();
        director.SetEnabled(true);
        director.SetHolding(false);
        cameraReady = false;
        if (!remote.Isolated)
            Logger.LogWarning("Broadcast camera enabled. Observer remains an ordinary player: park safely. No god mode, teleport, or gameplay isolation is applied.");
        if (selfTest)
            Logger.LogInfo("Self-follow test: only your character is targeted; remote reference overrides are disabled.");
        else if (settings.RemoteTargets.Value)
            Logger.LogWarning(remotePatchAvailable ? "Experimental distant streaming enabled; multiplayer side effects are unverified." : "Remote streaming unavailable in this game build; using loaded targets only.");
        return true;
    }

    private void Stop(bool force = false, bool parked = false)
    {
        if (observer.Active && !force)
        {
            if (observer.RemoteSession) output?.Suspend();
            if (observer.Phase != ObserverPhase.Returning) observer.RequestReturn();
            cameraReady = false;
            return;
        }
        if (!parked) observer.Release();
        frameHold.Reset();
        output?.Suspend();
        if (!parked) automatic?.EndSession();
        probe.Stop();
        if (!parked) observerVisuals.Restore();
        director.SetEnabled(false);
        director.SetRoster(Array.Empty<PlayerTarget>());
        roster.Clear();
        coverage.Reset();
        acquisitionWindow.Reset();
        acquisition.SetRoster(Array.Empty<PlayerTarget>());
        idle.Clear();
        selfTest = false;
        activeWorld = parked ? ZNet.instance : null;
        cameraReady = false;
        presentedPlayerId = null; presentedFrame = -100;
        RestoreReference();
        rig.Restore();
    }

    private void UpdateReference(TargetRoster.Target? target)
    {
        if (selfTest || !settings.RemoteTargets.Value || !remotePatchAvailable || target == null || !ZNet.instance)
        {
            RestoreReference();
            return;
        }
        Vector3? position = target.LoadedPlayer ? target.LoadedPlayer.transform.position : target.PublicPosition;
        if (!position.HasValue || !TargetRoster.Finite(position.Value)) { RestoreReference(); return; }
        SetReference(position.Value);
    }

    private void SetReference(Vector3 position)
    {
        if (referenceOwner != ZNet.instance)
        {
            RestoreReference();
            referenceOwner = ZNet.instance;
            normalReference = referenceOwner.GetReferencePosition();
            retainedObserver = Player.m_localPlayer ? Player.m_localPlayer.GetComponent<ZNetView>()?.GetZDO() : null;
        }
        remoteReference = position;
        // The setter patch also covers subsequent normal game updates. Do not change send timers.
        referenceOwner.SetReferencePosition(position);
    }

    private void RestoreReference()
    {
        retainedObserver = null;
        remoteReference = null; // Remove override before calling the original setter.
        if (referenceOwner && normalReference.HasValue) referenceOwner.SetReferencePosition(normalReference.Value);
        referenceOwner = null;
        normalReference = null;
    }

    private static bool BeforeRemoveObjects(List<ZDO> __0, List<ZDO> __1)
    {
        BroadcasterPlugin? plugin = instance;
        if (plugin == null) return true;
        if (plugin.observer.Active)
        {
            try
            {
                if (!SceneRetention.MustAbort(true, plugin.observer.NetworkObject, __0, __1)) return true;
                plugin.SuspendObserver("Observer excluded from scene retention; travel suspended. Use broadcast observer return.");
            }
            catch (Exception error) { plugin.FailClosed(error); }
            // Never remove this player using stale interest lists. Its next fixed
            // tick re-synchronizes body/ZDO/reference before lists are recomputed.
            return false;
        }
        if (!plugin.remoteReference.HasValue) return true;
        try
        {
            if (!SceneRetention.MustAbort(true, plugin.retainedObserver, __0, __1)) return true;
            plugin.acquisitionFailure = "Acquisition aborted before scene removal: the distant reference excludes the observer. Dual-region retention is required; non-teleport travel is not safe on this path.";
            plugin.probe.Fail(plugin.acquisitionFailure);
            plugin.Logger.LogError(plugin.acquisitionFailure);
            plugin.Stop(); // Clear override, restore camera/HUD/renderers, release encoder.
        }
        catch (Exception error)
        {
            plugin.Logger.LogError("Scene retention guard failed; cancelling acquisition before removal: " + error.GetType().Name);
            plugin.Stop();
        }
        // Lists were computed for the unsafe distant reference. Do not pass them to
        // vanilla removal after restoring local interest. Next cycle recomputes them.
        return false;
    }

    private static void BeforeReferencePosition(ZNet __instance, ref Vector3 __0)
    {
        BroadcasterPlugin? plugin = instance;
        if (plugin == null || !plugin.director.IsEnabled || plugin.referenceOwner != __instance || !plugin.remoteReference.HasValue) return;
        // Calls made by us carry exactly the current override. Remember other incoming values
        // as the normal game's latest interest position for restoration.
        if (__0 != plugin.remoteReference.Value) plugin.normalReference = __0;
        __0 = plugin.remoteReference.Value;
    }

    private static void AfterCameraUpdate(GameCamera __instance, float __0)
    {
        BroadcasterPlugin? plugin = instance;
        if (plugin == null) return;
        try
        {
            if (plugin.remote.Isolated) plugin.observerVisuals.Apply(Player.m_localPlayer, true, true);
            if (!plugin.director.IsEnabled) return;
            Vector3? focus = null;
            string? presentationFailure = null;
            float azimuth = plugin.settings.Azimuth.Value;
            plugin.observerVisuals.Apply(Player.m_localPlayer, !plugin.selfTest, plugin.observer.Active);
            if (plugin.observer.Active && plugin.observer.Phase != ObserverPhase.Watching) { }
            else if (plugin.probe.Active)
            {
                if (plugin.probe.State == ProbeState.Viewing && plugin.probeMarker.HasValue)
                    focus = plugin.probeMarker.Value + Vector3.up * 1.5f;
            }
            else if (plugin.coverage.Mode == CoverageMode.Player)
            {
                TargetRoster.Target? target = plugin.roster.Find(plugin.coverage.SubjectId);
                if (plugin.CanShow(target)) focus = target!.LoadedPlayer!.transform.position + Vector3.up * 1.5f;
            }
            else if (plugin.coverage.Mode == CoverageMode.Creature)
            {
                Character? actor = plugin.idle.Find(plugin.coverage.SubjectId);
                if (actor) focus = actor.transform.position + Vector3.up;
            }
            else if (plugin.coverage.Mode == CoverageMode.Scenic && plugin.idle.SceneReady && plugin.idle.HasScenicPath)
            {
                focus = plugin.idle.ScenicFocus((float)(plugin.coverage.ShotElapsed / plugin.coverage.ScenicSeconds));
            }
            plugin.cameraReady = focus.HasValue && plugin.rig.Apply(__instance, focus.Value, azimuth,
                plugin.settings, plugin.coverage.Revision, __0);
            if (plugin.cameraReady && plugin.observer.Active)
            {
                Camera camera = __instance.GetComponent<Camera>() ?? __instance.GetComponentInChildren<Camera>();
                Vector3 carrier = camera ? camera.transform.position + Vector3.down * 2f : Vector3.zero;
                bool reachable = camera && Player.m_localPlayer && TargetRoster.Finite(carrier) &&
                    Vector3.Distance(Player.m_localPlayer.transform.position, carrier) < 95f && IdleShots.Ready(carrier);
                plugin.cameraReady = reachable && plugin.observer.FollowCamera(camera!.transform.position);
                if (!reachable)
                { plugin.rig.ResetPose(); presentationFailure = "Camera carrier destination is outside immediate range or still loading"; }
            }
            if (!plugin.cameraReady) plugin.frameHold.LastFailure = presentationFailure ?? (focus.HasValue ? plugin.rig.LastFailure : "No ready shot: " + plugin.coverage.Mode + " / observer " + plugin.observer.Phase);
            plugin.presentedPlayerId = plugin.cameraReady && plugin.coverage.Mode == CoverageMode.Player ? plugin.coverage.SubjectId : null;
            plugin.presentedFrame = Time.frameCount;
            plugin.rig.HideHud(plugin.settings.HideHud.Value);
        }
        catch (Exception error) { plugin.FailClosed(error); }
    }

    private static void AfterListenerUpdate(GameCamera __instance)
    {
        instance?.stream?.AlignListener(__instance.GetComponent<Camera>() ?? __instance.GetComponentInChildren<Camera>());
    }

    private void ToggleStream()
    {
        if (output?.Desired == true) { output.RequestStop(); return; }
        StartStream();
    }

    private void StartStream()
    {
        if (!director.IsEnabled || stream == null) { Logger.LogWarning("Start the broadcast camera first (F8 or F10)."); return; }
        if (!listenerPatchAvailable) { Logger.LogError("Streaming unavailable: this game build's audio listener hook was not found."); return; }
        output?.RequestStart();
    }

    private StreamOptions BuildStreamOptions() => new StreamOptions
    {
        Executable = settings.FFmpegPath.Value, Url = settings.RtmpUrl.Value,
        Width = settings.StreamWidth.Value, Height = settings.StreamHeight.Value,
        Fps = settings.StreamFps.Value, BitrateKbps = settings.StreamBitrate.Value,
        SampleRate = AudioSettings.outputSampleRate, FlipVertical = settings.FlipVertical.Value,
        VideoEncoder = settings.VideoEncoder.Value, NvencGpu = settings.NvencGpu.Value
    };
    private AudioListener? FindAudioListener()
    {
        if (!listenerPatchAvailable) return null;
        GameCamera camera = FindFirstObjectByType<GameCamera>();
        return camera ? ListenerField?.GetValue(camera) as AudioListener : null;
    }

    private void OnGUI()
    {
        if (settings == null) return;
        if (!director.IsEnabled)
        {
            if (connection?.Recovering == true)
            {
                GUI.Box(new Rect(12, 12, Mathf.Max(200, Screen.width - 24), 80), connection.Status);
                if (GUI.Button(new Rect(24, 55, 180, 28), "Cancel reconnection"))
                { connection.OnManualStop(true); CancelIntent(); UpdateBackground(); }
            }
            else if (remote.Isolated) GUI.Box(new Rect(12, 12, Mathf.Max(200, Screen.width - 24), 54),
                observer.Phase == ObserverPhase.Parked ? "Read-only observer parked. F8 resumes coverage; disconnect to leave the observer role." : remote.Status);
            return;
        }
        bool hardFailure = (probe.Active && probe.State != ProbeState.Viewing) || (observer.Active && observer.Phase != ObserverPhase.Watching);
        FramePresentation presentation = frameHold.Present(cameraReady, hardFailure);
        if (presentation == FramePresentation.Hold) return;
        Color old = GUI.color;
        if (presentation == FramePresentation.Slate)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(24, 24, Screen.width - 48, 110), "Valheim Broadcaster — recovery: waiting for a ready scene\n" + Status() +
                (observer.Active ? remote.Isolated
                    ? "\nF8 or broadcast observer return: return and park. Disconnect to leave the read-only role."
                    : "\nF8 or broadcast observer return: return to the starting position, then restore controls." : ""));
        }
        else if (settings.ShowStatus.Value || selfTest || probe.Active)
        {
            GUI.color = Color.white;
            GUI.Box(new Rect(12, 12, Mathf.Max(200, Screen.width - 24), observer.RemoteSession ? 76 : 48), Status());
        }
        GUI.color = old;
    }

    private string Status()
    {
        if (observer.Active) return observer.Status + " | " + coverage.Mode + " | RTMP: " + (output?.Status ?? "Stopped") +
            (observer.RemoteSession ? "\n" + automatic?.Status + " | " + remote.DiscoveryStatus : "");
        if (acquisitionFailure != null) return acquisitionFailure;
        if (probe.Active) return $"SOLO DISTANT SCENE TEST | {probe.State} | {probe.Elapsed:F0}s | RTMP: {output?.Status ?? "Stopped"}";
        string subject = coverage.Mode == CoverageMode.Player ? roster.Find(coverage.SubjectId)?.Identity.Name ?? "Player" :
            coverage.Mode == CoverageMode.Creature ? idle.Find(coverage.SubjectId)?.GetHoverName() ?? "Creature" : "Scenery";
        string exhausted = coverage.Mode == CoverageMode.Scenic && !idle.HasScenicPath ? " | No fresh scenic path in loaded region" : "";
        string pending = coverage.AwaitingPlayers ? acquisitionWindow.Active ? " | Acquiring distant player" : " | Awaiting player scene" : "";
        return $"{(selfTest ? "SELF-FOLLOW TEST | " : "")}{coverage.Mode} | {subject} | {(director.IsHolding ? "Hold" : "Auto")} | {roster.ConnectedOthers} other connected, {director.Roster.Count} ready{pending}{exhausted} | RTMP: {output?.Status ?? "Stopped"}";
    }

    private bool ProbeCameraAreaReady(Vector3 point)
    {
        float angle = settings.Azimuth.Value * Mathf.Deg2Rad;
        return IdleShots.Ready(point + Vector3.up * (1.5f + settings.Height.Value) +
            new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * settings.Distance.Value);
    }

    private void ProbeCommand(string[] args, Terminal context)
    {
        if (observer.Active) { context?.AddString("Return and stop observer mode before using the reference-only probe."); return; }
        if (args.Length != 2) { context?.AddString("broadcast probe mark|start|stop|status"); return; }
        switch (args[1].ToLowerInvariant())
        {
            case "mark":
                if (probe.Active || !Player.m_localPlayer || !ZNet.instance || !IdleShots.Ready(Player.m_localPlayer.transform.position))
                { context?.AddString("Stop the probe and stand in a loaded world to mark its destination."); return; }
                probeMarker = Player.m_localPlayer.transform.position;
                probeWorld = ZNet.instance;
                context?.AddString("Destination saved for this world session. Travel away until it unloads, then run broadcast probe start.");
                return;
            case "start":
                if (probe.Active) { context?.AddString("Probe already active."); return; }
                if (!probeMarker.HasValue || probeWorld != ZNet.instance || !Player.m_localPlayer || Player.m_localPlayer.IsDead())
                { context?.AddString("Mark a destination in this world first, with a living character."); return; }
                if (!remotePatchAvailable) { context?.AddString("Network reference hook unavailable in this build."); return; }
                float distance = Vector3.Distance(Player.m_localPlayer.transform.position, probeMarker.Value);
                if (distance < 200f || !ZoneSystem.instance || ZoneSystem.instance.IsZoneLoaded(ZoneSystem.GetZone(probeMarker.Value)))
                { context?.AddString("Destination is too close or still loaded. Move farther away (at least 200m) and try again."); return; }
                if (selfTest) { context?.AddString("Exit F10 self-follow before starting this scene test."); return; }
                if (!director.IsEnabled && !StartBroadcast()) { context?.AddString("Join a ready world first."); return; }
                RestoreReference();
                acquisitionWindow.Reset();
                probeOrigin = Player.m_localPlayer.transform.position;
                acquisitionFailure = null;
                probe.Start(); rig.ResetPose(); cameraReady = false;
                SetReference(probeMarker.Value);
                Logger.LogWarning("Solo scene probe started: changing network interest only, no teleport. 30s acquisition timeout, 120s total limit. Scene readiness is not proof of full scene delivery.");
                context?.AddString("Probe started; close the console. Use broadcast probe status for results.");
                return;
            case "stop":
                probe.Stop(); RestoreReference(); acquisitionWindow.Reset(); rig.ResetPose(); cameraReady = false;
                context?.AddString(director.IsEnabled ? "Probe stopped; normal coverage resumes without restarting RTMP." : "Probe is inactive; broadcast camera is off. " + (acquisitionFailure ?? ""));
                return;
            case "status":
                string distanceText = probeMarker.HasValue && Player.m_localPlayer ? Vector3.Distance(Player.m_localPlayer.transform.position, probeMarker.Value).ToString("F0") + "m" : "unavailable";
                string moved = Player.m_localPlayer && probe.State != ProbeState.Idle ? Vector3.Distance(Player.m_localPlayer.transform.position, probeOrigin).ToString("F1") + "m" : "unavailable";
                context?.AddString($"Probe: {probe.State}; elapsed {probe.Elapsed:F1}s; first-ready {probe.FirstReadySeconds?.ToString("F1") ?? "never"}s; destination distance {distanceText}; observer displacement {moved}. Readiness is not a complete-scene verdict. {probe.FailureReason ?? ""}");
                return;
            default: context?.AddString("broadcast probe mark|start|stop|status"); return;
        }
    }

    private static bool ObserverInputOrPhysics(Character __instance) => instance == null ||
        (!instance.observer.Owns(__instance) && !(instance.remote.Isolated && __instance == Player.m_localPlayer));

    private static bool BeforeOpenServer(ZNet __instance)
    {
        BroadcasterPlugin? plugin = instance;
        if (plugin == null || !plugin.observer.InWorld(__instance)) return true;
        const string message = "Return and stop offline observer mode before opening this world to multiplayer.";
        plugin.Logger.LogWarning(message);
        global::Console.instance?.AddString(message);
        return false;
    }

    private static bool ObserverFixedUpdate(Player __instance)
    {
        BroadcasterPlugin? plugin = instance;
        if (plugin == null) return true;
        if (!plugin.observer.Owns(__instance))
        {
            if (!plugin.remote.Isolated || __instance != Player.m_localPlayer) return true;
            try { ObserverController.AdvancePendingTeleport(__instance, Time.fixedDeltaTime); }
            catch (Exception error) { plugin.remote.Fail("Observer spawn hook failed: " + error.GetType().Name); }
            return false;
        }
        try { plugin.observer.FixedTick(Time.fixedDeltaTime); }
        catch (Exception error) { plugin.FailClosed(error); }
        return false;
    }

    private void TryStartRemoteObserver()
    {
        if (connection?.AllowsObserverActivation == false) return;
        if (!observerHooksAvailable) { remote.Fail("Observer game hooks unavailable"); return; }
        try
        {
            Player player = Player.m_localPlayer;
            if (!player || player.IsDead() || player.IsTeleporting() || player.IsAttached() || !IdleShots.Ready(player.transform.position)) return;
            if (!director.IsEnabled && !StartBroadcast()) return;
            observer.Begin(player, true); remote.Spawned(); automatic?.Started();
            Logger.LogInfo("Read-only observer camera started automatically.");
        }
        catch (InvalidOperationException) { Stop(true); } // Retry until the bounded spawn deadline.
        catch (Exception error) { remote.Fail("Observer startup error: " + error.GetType().Name); Stop(true); }
    }

    private void ObserverCommand(string[] args, Terminal context)
    {
        if (args.Length != 2 && !(args.Length == 5 && args[1].Equals("travel", StringComparison.OrdinalIgnoreCase))) { context?.AddString("broadcast observer start|mark|travel [x y z]|return|status"); return; }
        switch (args[1].ToLowerInvariant())
        {
            case "start":
                if (remote.Isolated)
                {
                    connection?.OnManualStart();
                    if (observer.Phase == ObserverPhase.Parked) StartBroadcast();
                    else if (!observer.Active && remote.Approved) TryStartRemoteObserver();
                    context?.AddString(observer.Active ? observer.Diagnostics : remote.Status);
                    return;
                }
                if (!observerHooksAvailable || !ObserverController.Offline)
                { context?.AddString("Requires supported game hooks and a CLOSED local world, not a server connection or hosted session."); return; }
                if (selfTest || probe.Active || observer.Active)
                { context?.AddString("Stop self-follow/probe/observer mode first."); return; }
                bool startedCamera = !director.IsEnabled;
                if (startedCamera && !StartBroadcast()) { context?.AddString("Enter a world with a living character first."); return; }
                RestoreReference(); acquisitionWindow.Reset();
                try { observer.Begin(Player.m_localPlayer); }
                catch { if (startedCamera) Stop(true); throw; }
                Logger.LogWarning("OFFLINE observer prototype enabled. Local input/physics and ordinary player update loops are suspended. Not a multiplayer or zero-world-impact mode. Return before exiting or disabling.");
                break;
            case "mark":
                if (!Player.m_localPlayer || !ZNet.instance) { context?.AddString("Enter a ready world first."); return; }
                probeMarker = Player.m_localPlayer.transform.position; probeWorld = ZNet.instance;
                context?.AddString("Observer travel marker saved at " + probeMarker.Value.ToString("F1"));
                return;
            case "travel":
                automatic?.PauseForManual(); SetHolding(true);
                if (args.Length == 5)
                {
                    var values = new float[3];
                    for (int i = 0; i < 3; i++)
                        if (!float.TryParse(args[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]) ||
                            float.IsNaN(values[i]) || float.IsInfinity(values[i]) || Math.Abs(values[i]) > 20000)
                        { context?.AddString("Use finite x y z coordinates within +/-20000 metres."); return; }
                    observer.TravelTo(new Vector3(values[0], values[1], values[2])); cameraReady = false; rig.ResetPose();
                    context?.AddString(observer.Diagnostics); return;
                }
                if (!observer.Active || !probeMarker.HasValue || probeWorld != ZNet.instance)
                { context?.AddString("Mark a destination with broadcast observer mark, then start observer mode first."); return; }
                observer.TravelTo(probeMarker.Value);
                cameraReady = false; rig.ResetPose();
                break;
            case "return": RequestStop(); break;
            case "status": break;
            default: context?.AddString("broadcast observer start|mark|travel [x y z]|return|status"); return;
        }
        context?.AddString(observer.Diagnostics);
    }

    private void SuspendObserver(string reason)
    {
        if (observer.Phase != ObserverPhase.Suspended) Logger.LogError(reason);
        observer.Suspend(reason);
        cameraReady = false;
    }

    private void FailClosed(Exception error)
    {
        if (observer.Active)
        {
            // Releasing physics at the airborne camera position would be unsafe.
            // Hold the body, stop travel, and keep the return command available.
            if (observer.Phase != ObserverPhase.Suspended) Logger.LogError($"Observer suspended after an error: {error}");
            SuspendObserver("Observer adapter error: " + error.GetType().Name + ". Use broadcast observer return.");
            return;
        }
        Logger.LogError($"Broadcaster stopped after an error: {error}");
        Stop();
    }

    private void OnDisable() { connection?.Dispose(); CancelIntent(); remote?.Close(); Stop(true); RestoreBackground(); }
    private void OnDestroy()
    {
        connection?.Dispose(); CancelIntent(); remote?.Close();
        Stop(true);
        harmony?.UnpatchSelf();
        RestoreBackground();
        if (instance == this) instance = null;
    }

    private void RestoreBackground()
    { if (backgroundOwned) { Application.runInBackground = previousBackground; backgroundOwned = false; } }

    private string Diagnostics() => Status() + "\n" + frameHold.Diagnostics + "\n" + observer.Diagnostics +
        "\n" + remote.DiscoveryStatus + "\n" + automatic?.Diagnostics + "\n" + connection?.Status + "\n" + output?.Diagnostics;

    private void AutomationCommand(string[] args, Terminal context)
    {
        if (args.Length != 2) { context?.AddString("broadcast auto on|off|status"); return; }
        switch (args[1].ToLowerInvariant())
        {
            case "on":
                if (!observer.RemoteSession || !remote.Approved || !director.IsEnabled)
                { context?.AddString("Start an authorized read-only observer camera first."); return; }
                automatic!.Enable(true); SetHolding(false); RefreshRoster(); break;
            case "off": automatic!.Enable(false); SetHolding(true); break;
            case "status": break;
            default: context?.AddString("broadcast auto on|off|status"); return;
        }
        context?.AddString(automatic!.Diagnostics);
    }

    private void UnattendedCommand(string[] args, Terminal context)
    {
        if (args.Length != 2) { context?.AddString("broadcast unattended on|off|status"); return; }
        switch (args[1].ToLowerInvariant())
        {
            case "on":
                unattendedOverride = true; connection?.OnManualStart();
                connection?.Tick(remote.Isolated && remote.Approved, observer.Active); break;
            case "off": unattendedOverride = false; connection?.OnManualStop(true); break;
            case "status": break;
            default: context?.AddString("broadcast unattended on|off|status"); return;
        }
        UpdateBackground(); context?.AddString(connection?.Status);
    }

    private void RouteCommand(string[] args, Terminal context)
    {
        if (args.Length < 2) { context?.AddString("broadcast route add [x y z]|start|stop|clear|status"); return; }
        switch (args[1].ToLowerInvariant())
        {
            case "add":
                if (!Player.m_localPlayer || args.Length != 2 && args.Length != 5)
                { context?.AddString("broadcast route add [x y z] requires a ready observer world."); return; }
                Vector3 position = Player.m_localPlayer.transform.position;
                if (args.Length == 5)
                {
                    var values = new float[3];
                    for (int i = 0; i < 3; i++)
                        if (!float.TryParse(args[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]) ||
                            float.IsNaN(values[i]) || float.IsInfinity(values[i]) || Math.Abs(values[i]) > 20000)
                        { context?.AddString("Use finite x y z coordinates within +/-20000 metres."); return; }
                    position = new Vector3(values[0], values[1], values[2]);
                }
                context?.AddString(automatic!.AddRoutePoint(position)); return;
            case "start": automatic!.StartRoute(); SetHolding(false); break;
            case "stop": automatic!.StopRoute(); break;
            case "clear": automatic!.ClearRoute(); break;
            case "status": break;
            default: context?.AddString("broadcast route add [x y z]|start|stop|clear|status"); return;
        }
        context?.AddString(automatic!.Diagnostics);
    }

    private sealed class BroadcastCommand : ConsoleCommand
    {
        public override string Name => "broadcast";
        public override string Help => "broadcast self|on|off|next|previous|hold|resume|list|status|diagnostics|survey|auto on|off|status|route add [x y z]|start|stop|clear|status|unattended on|off|status|observer start|mark|travel [x y z]|return|status|probe mark|start|stop|status|follow <id>|stream on|off|status|diagnostics";
        public override void Run(string[] args, Terminal context)
        {
            BroadcasterPlugin? plugin = instance;
            if (plugin == null || !plugin.enabled) { context?.AddString("Broadcaster is unavailable on this client."); return; }
            string action = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
            try
            {
                switch (action)
                {
                    case "auto": plugin.AutomationCommand(args, context); return;
                    case "route":
                        try { plugin.RouteCommand(args, context); }
                        catch (ArgumentException error) { context?.AddString(error.Message); }
                        catch (InvalidOperationException error) { context?.AddString(error.Message); }
                        return;
                    case "unattended": plugin.UnattendedCommand(args, context); return;
                    case "observer":
                        try { plugin.ObserverCommand(args, context); }
                        catch (InvalidOperationException error) { context?.AddString(error.Message); }
                        return;
                    case "probe":
                        plugin.ProbeCommand(args, context);
                        return;
                    case "stream":
                        if (args.Length != 2) { context?.AddString("broadcast stream on|off|status|diagnostics"); return; }
                        switch (args[1].ToLowerInvariant())
                        {
                            case "on": plugin.StartStream(); break;
                            case "off": plugin.output?.RequestStop(); break;
                            case "status": break;
                            case "diagnostics": context?.AddString(plugin.output?.Diagnostics); return;
                            default: context?.AddString("broadcast stream on|off|status|diagnostics"); return;
                        }
                        context?.AddString("RTMP: " + (plugin.output?.Status ?? "Stopped"));
                        return;
                    case "on":
                    case "self":
                        if (plugin.observer.Active && !(action == "on" && plugin.observer.Phase == ObserverPhase.Parked)) { context?.AddString("Return and park/stop observer mode before switching camera modes."); return; }
                        plugin.connection?.OnManualStart();
                        if (!plugin.StartBroadcast(action == "self")) { context?.AddString("Join a world with a living, spawned observer first."); return; }
                        break;
                    case "survey":
                    case "anchor":
                        if (!plugin.director.IsEnabled || plugin.selfTest || !Player.m_localPlayer)
                        { context?.AddString("Start normal broadcasting first; survey reset is not available in self-test."); return; }
                        plugin.idle.SetRegion(Player.m_localPlayer.transform.position);
                        plugin.coverage.Reset();
                        plugin.nextCreatureRefresh = 0;
                        context?.AddString("Idle survey region reset. New scenic paths will be selected around this loaded region.");
                        break;
                    case "off": plugin.RequestStop(); break;
                    case "next": plugin.MoveTarget(1); break;
                    case "previous": plugin.MoveTarget(-1); break;
                    case "hold": plugin.SetHolding(true); break;
                    case "resume": plugin.SetHolding(false); break;
                    case "follow":
                        if (args.Length != 2 || !plugin.director.Select(args[1])) { context?.AddString("Unknown target. Use broadcast list and follow its exact ID."); return; }
                        if (plugin.automatic?.ControlsSelection == true) plugin.automatic.Select(args[1]);
                        plugin.SetHolding(true);
                        break;
                    case "list":
                        plugin.RefreshRoster();
                        foreach (TargetRoster.Target target in plugin.roster.Targets.Values)
                            context?.AddString($"{target.Identity.Id}  {target.Identity.Name} | {(plugin.CanShow(target) ? "camera-ready" : target.PublicPosition.HasValue && plugin.remote.DiscoveryFresh ? "awaiting acquisition" : "waiting for scene/coordinates")}");
                        break;
                    case "diagnostics": context?.AddString(plugin.Diagnostics()); return;
                    case "status": break;
                    default: context?.AddString(Help); return;
                }
                context?.AddString(plugin.Status());
            }
            catch (Exception error)
            {
                plugin.FailClosed(error);
                context?.AddString(plugin.observer.Active ? plugin.observer.Status : "Stopped after an error; see BepInEx log.");
            }
        }
    }
}
