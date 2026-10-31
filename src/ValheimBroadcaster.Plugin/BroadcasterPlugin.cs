using System;
using System.Linq;
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
    public const string PluginVersion = "0.3.0";
    private static BroadcasterPlugin? instance;
    private Harmony? harmony;
    private StreamCapture? stream;
    private bool listenerPatchAvailable;
    private static readonly FieldInfo? ListenerField = AccessTools.Field(typeof(GameCamera), "m_listner");
    private BroadcastSettings settings = null!;
    private readonly BroadcastDirector director = new BroadcastDirector();
    private readonly TargetRoster roster = new TargetRoster();
    private readonly CameraRig rig = new CameraRig();
    private float nextRosterRefresh;
    private bool remotePatchAvailable;
    private bool cameraReady;
    private bool selfTest;
    private Vector3? remoteReference;
    private Vector3? normalReference;
    private ZNet? referenceOwner;
    private long lastRevision = -1;
    private string lastStatus = "Disabled";

    private void Awake()
    {
        if (GUIManager.IsHeadless()) { enabled = false; return; }
        settings = new BroadcastSettings(Config);
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
            harmony.Patch(cameraMethod, postfix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(AfterCameraUpdate)));
            stream = gameObject.AddComponent<StreamCapture>();
            MethodInfo? listenerMethod = AccessTools.Method(typeof(GameCamera), "UpdateListner", Type.EmptyTypes);
            if (listenerMethod != null && ListenerField != null)
            {
                harmony.Patch(listenerMethod, postfix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(AfterListenerUpdate)));
                listenerPatchAvailable = true;
            }
            MethodInfo? referenceMethod = AccessTools.Method(typeof(ZNet), "SetReferencePosition", new[] { typeof(Vector3) });
            if (referenceMethod != null)
            {
                harmony.Patch(referenceMethod, prefix: new HarmonyMethod(typeof(BroadcasterPlugin), nameof(BeforeReferencePosition)));
                remotePatchAvailable = true;
            }
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
        if (!Player.m_localPlayer || Player.m_localPlayer.IsDead() || !ZNet.instance || !ZNetScene.instance)
        {
            if (director.IsEnabled) Stop();
            return;
        }
        // Hotkeys must not fire while the player types into chat/console or navigates a menu.
        if (!IsTextOrMenuOpen())
        {
            if (settings.Toggle.Value.IsDown()) { if (director.IsEnabled) Stop(); else StartBroadcast(); }
            else if (settings.SelfTest.Value.IsDown()) StartBroadcast(true);
            if (director.IsEnabled)
            {
                if (settings.StreamToggle.Value.IsDown()) ToggleStream();
                if (settings.Next.Value.IsDown()) director.Next();
                if (settings.Previous.Value.IsDown()) director.Previous();
                if (settings.Hold.Value.IsDown()) director.SetHolding(!director.IsHolding);
            }
        }
        if (!director.IsEnabled) return;
        try
        {
            director.DwellSeconds = settings.Dwell.Value;
            director.TargetTimeoutSeconds = settings.Timeout.Value;
            if (Time.unscaledTime >= nextRosterRefresh)
            {
                RefreshRoster();
                nextRosterRefresh = Time.unscaledTime + 0.5f;
            }
            TargetRoster.Target? current = roster.Find(director.CurrentTarget?.Id);
            director.Tick(Time.unscaledDeltaTime, IsReady(current));
            current = roster.Find(director.CurrentTarget?.Id); // Tick may change the selection.
            if (director.SelectionRevision != lastRevision)
            {
                lastRevision = director.SelectionRevision;
                rig.ResetPose();
                cameraReady = false;
            }
            UpdateReference(current);
            string status = Status();
            if (status != lastStatus) { Logger.LogInfo(status); lastStatus = status; }
        }
        catch (Exception error) { FailClosed(error); }
    }

    private static bool IsTextOrMenuOpen() =>
        (global::Console.instance && global::Console.IsVisible()) ||
        (Chat.instance && Chat.instance.HasFocus()) || Menu.IsVisible() ||
        (InventoryGui.instance && InventoryGui.IsVisible()) || TextInput.IsVisible();

    private void RefreshRoster()
    {
        roster.Refresh(settings.RemoteTargets.Value && remotePatchAvailable, selfTest);
        if (selfTest && roster.Targets.Count == 1)
            director.SetSelfTarget(roster.Identities().Single());
        else
            director.SetRoster(roster.Identities(), Player.m_localPlayer ? Player.m_localPlayer.GetZDOID().ToString() : null);
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
        Stop(); // Restore any previous camera/HUD/reference before switching modes.
        selfTest = testSelf;
        RefreshRoster();
        director.SetEnabled(true);
        director.SetHolding(false);
        cameraReady = false;
        Logger.LogWarning("Broadcast camera enabled. Observer remains an ordinary player: park safely. No god mode, teleport, or gameplay isolation is applied.");
        if (selfTest)
            Logger.LogInfo("Self-follow test: only your character is targeted; remote reference overrides are disabled.");
        else if (settings.RemoteTargets.Value)
            Logger.LogWarning(remotePatchAvailable ? "Experimental distant streaming enabled; multiplayer side effects are unverified." : "Remote streaming unavailable in this game build; using loaded targets only.");
        return true;
    }

    private void Stop()
    {
        stream?.StopOutput();
        director.SetEnabled(false);
        selfTest = false;
        cameraReady = false;
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
        if (referenceOwner != ZNet.instance)
        {
            RestoreReference();
            referenceOwner = ZNet.instance;
            normalReference = referenceOwner.GetReferencePosition();
        }
        remoteReference = position;
        // The setter patch also covers subsequent normal game updates. Do not change send timers.
        referenceOwner.SetReferencePosition(position.Value);
    }

    private void RestoreReference()
    {
        remoteReference = null; // Remove override before calling the original setter.
        if (referenceOwner && normalReference.HasValue) referenceOwner.SetReferencePosition(normalReference.Value);
        referenceOwner = null;
        normalReference = null;
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
        if (plugin == null || !plugin.director.IsEnabled) return;
        try
        {
            TargetRoster.Target? target = plugin.roster.Find(plugin.director.CurrentTarget?.Id);
            plugin.cameraReady = IsReady(target);
            if (plugin.cameraReady)
                plugin.rig.Apply(__instance, target!.LoadedPlayer!.transform, plugin.settings, plugin.director.SelectionRevision, __0);
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
        if (stream != null && stream.Active) { stream.StopOutput(); return; }
        StartStream();
    }

    private void StartStream()
    {
        if (!director.IsEnabled || stream == null) { Logger.LogWarning("Start the broadcast camera first (F8 or F10)."); return; }
        if (!listenerPatchAvailable) { Logger.LogError("Streaming unavailable: this game build's audio listener hook was not found."); return; }
        try
        {
            GameCamera camera = FindFirstObjectByType<GameCamera>();
            AudioListener? listener = camera ? ListenerField?.GetValue(camera) as AudioListener : null;
            if (!listener) { Logger.LogWarning("Game audio listener unavailable; enter a world first."); return; }
            stream.Begin(new StreamOptions
            {
                Executable = settings.FFmpegPath.Value, Url = settings.RtmpUrl.Value,
                Width = settings.StreamWidth.Value, Height = settings.StreamHeight.Value,
                Fps = settings.StreamFps.Value, BitrateKbps = settings.StreamBitrate.Value,
                SampleRate = AudioSettings.outputSampleRate, FlipVertical = settings.FlipVertical.Value
            }, listener);
            Logger.LogInfo("Starting RTMP with H.264 video and stereo AAC game audio. F11 stops output.");
        }
        catch (ArgumentException error) { Logger.LogWarning(error.Message); }
        catch (InvalidOperationException error) { Logger.LogWarning(error.Message); }
        catch { Logger.LogError("Streaming initialization failed. Check FFmpeg and audio/GPU availability."); }
    }

    private void OnGUI()
    {
        if (!director.IsEnabled || settings == null) return;
        Color old = GUI.color;
        if (!cameraReady)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(24, 24, Screen.width - 48, 60), "Valheim Broadcaster — waiting for a loaded target\nF8: stop · PageUp/PageDown: cycle · F9: hold/resume");
        }
        else if (settings.ShowStatus.Value || selfTest)
        {
            GUI.color = Color.white;
            GUI.Box(new Rect(12, 12, 800, 34), Status());
        }
        GUI.color = old;
    }

    private string Status() => $"{(selfTest ? "SELF-FOLLOW TEST | " : "")}{director.State} | {director.CurrentTarget?.Name ?? "No targets"} | {(director.IsHolding ? "Hold" : "Auto")} | {director.Roster.Count} target(s) | RTMP: {stream?.Status ?? "Stopped"}";

    private void FailClosed(Exception error)
    {
        Logger.LogError($"Broadcaster stopped after an error: {error}");
        Stop();
    }

    private void OnDisable() { Stop(); }
    private void OnDestroy()
    {
        Stop();
        harmony?.UnpatchSelf();
        if (instance == this) instance = null;
    }

    private sealed class BroadcastCommand : ConsoleCommand
    {
        public override string Name => "broadcast";
        public override string Help => "broadcast self|on|off|next|previous|hold|resume|list|status|follow <id>|stream on|off|status";
        public override void Run(string[] args, Terminal context)
        {
            BroadcasterPlugin? plugin = instance;
            if (plugin == null || !plugin.enabled) { context?.AddString("Broadcaster is unavailable on this client."); return; }
            string action = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
            try
            {
                switch (action)
                {
                    case "stream":
                        if (args.Length != 2) { context?.AddString("broadcast stream on|off|status"); return; }
                        switch (args[1].ToLowerInvariant())
                        {
                            case "on": plugin.StartStream(); break;
                            case "off": plugin.stream?.StopOutput(); break;
                            case "status": break;
                            default: context?.AddString("broadcast stream on|off|status"); return;
                        }
                        context?.AddString("RTMP: " + (plugin.stream?.Status ?? "Stopped"));
                        return;
                    case "on":
                    case "self":
                        if (!plugin.StartBroadcast(action == "self")) { context?.AddString("Join a world with a living, spawned observer first."); return; }
                        break;
                    case "off": plugin.Stop(); break;
                    case "next": plugin.director.Next(); break;
                    case "previous": plugin.director.Previous(); break;
                    case "hold": plugin.director.SetHolding(true); break;
                    case "resume": plugin.director.SetHolding(false); break;
                    case "follow":
                        if (args.Length != 2 || !plugin.director.Select(args[1])) { context?.AddString("Unknown target. Use broadcast list and follow its exact ID."); return; }
                        plugin.director.SetHolding(true);
                        break;
                    case "list":
                        plugin.RefreshRoster();
                        foreach (PlayerTarget target in plugin.director.Roster) context?.AddString($"{target.Id}  {target.Name}");
                        break;
                    case "status": break;
                    default: context?.AddString(Help); return;
                }
                if (action != "status" && action != "list") plugin.cameraReady = false;
                context?.AddString(plugin.Status());
            }
            catch (Exception error) { plugin.FailClosed(error); context?.AddString("Stopped after an error; see BepInEx log."); }
        }
    }
}
