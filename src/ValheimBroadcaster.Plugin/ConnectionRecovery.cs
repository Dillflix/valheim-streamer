using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Retains one explicitly authorized connection in memory. Never persists a password,
// selects a different character, or treats ordinary Steam login as observer approval.
internal sealed class ConnectionRecovery
{
    private static ConnectionRecovery? instance;
    private readonly Func<bool> configured, observerConfigured;
    private readonly Action<string> log, reject;
    private readonly Action beforeReconnect;
    private readonly BroadcastSettings settings;
    private readonly StartupJoinPolicy startup;
    private bool startupPrepared;
    private bool supervisorShutdownFailed;
    private static readonly FieldInfo? PendingPasswordRpc = AccessTools.Field(typeof(ZNet), "m_tempPasswordRPC");
    private static readonly FieldInfo? Profiles = AccessTools.Field(typeof(FejdStartup), "m_profiles");
    private static readonly FieldInfo? CinematicInstance = AccessTools.Field(typeof(CinematicsManager), "s_instance");
    private string failureCategory = "none";
    internal bool StoppedByOperator => (manualStopped || startup.OperatorStopped) && policy.State != ConnectionRecoveryState.Halted && startup.State != StartupJoinState.Blocked;
    internal bool Blocked => policy.State == ConnectionRecoveryState.Halted || startup.State == StartupJoinState.Blocked;
    internal string FailureCategory => failureCategory;
    internal bool Starting => startup.Pending;
    internal string StartupPhase => startup.State == StartupJoinState.Complete ? "authorized" :
        startup.State == StartupJoinState.Connecting ? (passwordSubmittedTo ? "password_submitted" : "joining_server") :
        startup.Pending ? (readyMenu ? "selecting_character" : "waiting_for_menu") : "";

    internal string StartupStatus => "Startup: " + startup.State + "; attempts " + startup.Attempts + "; reason " + startup.Failure;
    private readonly ConnectionRecoveryPolicy policy = new ConnectionRecoveryPolicy();
    private static readonly MethodInfo? PasswordEntered = AccessTools.Method(typeof(ZNet), "OnPasswordEntered", new[] { typeof(string) });
    private static readonly MethodInfo? ContinueLogout = AccessTools.Method(typeof(Game), "ContinueLogout", new[] { typeof(bool), typeof(bool), typeof(bool) });
    private ServerJoinData destination;
    private string? password, profile;
    private FileHelpers.FileSource profileSource;
    private ZNet? owner, failedExitWorld;
    private FejdStartup? readyMenu, attemptMenu;
    private double menuReadyAt, nextProfileCheck, profileDeadline;
    private ZNet? passwordSubmittedTo;
    private bool haveDestination, manualStopped, internalJoin, internalExit, installed, wasConfigured, passwordRequired, cancelPendingJoin;
    private string readiness = "Enable unattended recovery before manually joining a server";
    internal bool Armed => policy.Armed;
    internal bool Recovering => policy.Recovering;
    internal bool AllowsObserverActivation => !manualStopped && !cancelPendingJoin;
    internal string Status => "Connection: " + policy.State +
        (policy.State == ConnectionRecoveryState.Backoff ? $"; attempt {policy.Attempts}; retry in {Math.Max(0, policy.NextAttemptAt - Time.realtimeSinceStartup):F0}s" : "") +
        (policy.State == ConnectionRecoveryState.Connecting ? $"; attempt {policy.Attempts}; deadline in {Math.Max(0, policy.AttemptDeadline - Time.realtimeSinceStartup):F0}s" : "") +
        (policy.LastFailure == "none" ? "" : "; " + policy.LastFailure) +
        (!Armed && !manualStopped ? "; " + readiness : "");
    internal event Action? ManualStopped;

    internal ConnectionRecovery(Func<bool> configured, Func<bool> observerConfigured, Action<string> log,
        Action<string> reject, Action beforeReconnect, BroadcastSettings settings)
    {
        this.settings = settings; startup = new StartupJoinPolicy(settings.StartupEnabled.Value);
        this.configured = configured; this.observerConfigured = observerConfigured;
        this.log = log; this.reject = reject; this.beforeReconnect = beforeReconnect;
        instance = this;
    }

    internal void Install(Harmony harmony)
    {
        if (PasswordEntered == null || PendingPasswordRpc?.FieldType != typeof(ZRpc) || ContinueLogout == null || Profiles == null) throw new MissingMethodException("Native observer reconnect lifecycle unavailable");
        Hook(harmony, typeof(FejdStartup), "JoinServer", nameof(BeforeJoin));
        Hook(harmony, typeof(FejdStartup), "Start", nameof(MenuReady), true);
        Hook(harmony, typeof(FejdStartup), "OnStartGame", nameof(MenuInteraction));
        Hook(harmony, typeof(FejdStartup), "OnCharacterStart", nameof(MenuInteraction));
        harmony.Patch(AccessTools.Method(typeof(FejdStartup), nameof(FejdStartup.SetServerToJoin), new[] { typeof(ServerJoinData) }),
            prefix: new HarmonyMethod(typeof(ConnectionRecovery), nameof(ServerSelected)));
        Hook(harmony, typeof(Menu), "Logout", nameof(OperatorExit));
        Hook(harmony, typeof(Menu), "QuitGame", nameof(OperatorExit));
        harmony.Patch(AccessTools.Method(typeof(Game), nameof(Game.Logout), new[] { typeof(bool), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(ConnectionRecovery), nameof(OperatorExit)));
        harmony.Patch(PasswordEntered, prefix: new HarmonyMethod(typeof(ConnectionRecovery), nameof(PasswordSubmitted)));
        harmony.Patch(AccessTools.Method(typeof(ZNet), "RPC_ClientHandshake", new[] { typeof(ZRpc), typeof(bool), typeof(string) }),
            postfix: new HarmonyMethod(typeof(ConnectionRecovery), nameof(PasswordRequested)));
        installed = true;
    }
    private static void Hook(Harmony harmony, Type type, string original, string hook, bool postfix = false)
    {
        MethodInfo method = AccessTools.Method(type, original, Type.EmptyTypes) ?? throw new MissingMethodException(type.Name, original);
        var patch = new HarmonyMethod(typeof(ConnectionRecovery), hook);
        harmony.Patch(method, postfix ? null : patch, postfix ? patch : null);
    }

    private static bool BeforeJoin(FejdStartup __instance)
    {
        ConnectionRecovery? recovery = instance;
        if (recovery == null || recovery.internalJoin) return true;
        ServerJoinData join = __instance.GetServerToJoin();
        if ((recovery.startup.Pending || recovery.Recovering || recovery.cancelPendingJoin) && __instance != recovery.readyMenu) return false;
        if (recovery.startup.Pending && recovery.haveDestination && join == recovery.destination &&
            recovery.startup.State != StartupJoinState.Connecting) return false;
        if (recovery.cancelPendingJoin && join == recovery.destination) return false;
        // The native privilege check can resume JoinServer asynchronously.
        if ((recovery.policy.State == ConnectionRecoveryState.Connecting || recovery.startup.State == StartupJoinState.Connecting) && join == recovery.destination) return true;
        recovery.OnManualStop(true);
        recovery.manualStopped = recovery.cancelPendingJoin = recovery.passwordRequired = false;
        // Capture only non-secret destination metadata before opt-in; this permits
        // arming an already connected passwordless observer without another join.
        if (join.IsValid && (join.m_type == ServerJoinDataType.Dedicated || join.m_type == ServerJoinDataType.SteamUser))
        { recovery.destination = join; recovery.haveDestination = true; }
        return true;
    }
    private static void MenuReady(FejdStartup __instance)
    {
        if (instance == null) return;
        instance.readyMenu = __instance; instance.menuReadyAt = Time.realtimeSinceStartup + 2;
        instance.profileDeadline = Time.realtimeSinceStartup + 45; instance.nextProfileCheck = 0;
    }
    private static void MenuInteraction()
    {
        if (instance == null || instance.internalJoin) return;
        if (instance.Recovering || instance.startup.Pending || instance.cancelPendingJoin) instance.OnManualStop(true);
        instance.cancelPendingJoin = false;
    }
    private static void ServerSelected()
    {
        // Native UI selection is a new explicit intent; async JoinServer privilege
        // callbacks do not call this setter and remain cancelled after operator stop.
        if (instance != null && !instance.internalJoin)
        { if (instance.startup.Pending) instance.OnManualStop(true); instance.cancelPendingJoin = false; }
    }
    private static void OperatorExit()
    {
        if (instance != null && !instance.internalExit) instance.OnManualStop(true);
    }
    private static void PasswordSubmitted(string __0)
    {
        ConnectionRecovery? recovery = instance;
        if (recovery != null && recovery.configured() && recovery.observerConfigured() && recovery.haveDestination && !recovery.manualStopped)
            recovery.password = __0;
    }
    private static void PasswordRequested(ZNet __instance, ZRpc __0, bool __1)
    {
        ConnectionRecovery? recovery = instance;
        if (recovery == null || !recovery.haveDestination || recovery.manualStopped || __instance != ZNet.instance || __instance.IsServer()) return;
        recovery.passwordRequired = __1;
        if (!__1 || (!recovery.Recovering && recovery.startup.State != StartupJoinState.Connecting)) return;
        // During the challenge GetServerRPC() cannot return an admitted peer.
        // Use the same pending RPC the native dialog will authenticate. It is
        // cleared if Valheim already submitted its own password in the original.
        if (!__instance.InPasswordDialog() || !ReferenceEquals(PendingPasswordRpc!.GetValue(__instance), __0) ||
            !__0.IsConnected() || recovery.passwordSubmittedTo == __instance) return;
        if (string.IsNullOrEmpty(recovery.password))
        { recovery.Reject("Server requested a password but none is available", "configuration"); recovery.reject("Server password is unavailable"); return; }
        try
        {
            recovery.passwordSubmittedTo = __instance;
            PasswordEntered!.Invoke(__instance, new object[] { recovery.password! });
            recovery.log("Submitted the configured observer password to the native server challenge; awaiting admission.");
        }
        catch { recovery.Reject("Saved connection password could not be submitted"); }
    }

    internal void OnManualStart() { manualStopped = cancelPendingJoin = false; startup.ResumeOperatorIntent(); }
    internal void OnManualStop(bool forgetConnection = false)
    {
        if (policy.State == ConnectionRecoveryState.Connecting || startup.State == StartupJoinState.Connecting)
        {
            cancelPendingJoin = true;
            if (readyMenu) readyMenu.CancelInvoke("LoadMainSceneIfBackendSelected");
        }
        startup.Cancel(); policy.Cancel(); manualStopped = true; owner = null;
        if (forgetConnection) { haveDestination = false; password = profile = null; }
        ManualStopped?.Invoke();
    }
    internal void Reject(string reason, string category = "observer_authorization")
    {
        // A privilege callback or scene load may outlive a rejected attempt. Keep
        // its destination cancelled until an explicit new menu selection.
        if (policy.State == ConnectionRecoveryState.Connecting ||
            (haveDestination && startup.Attempts > 0 && startup.State != StartupJoinState.Complete && startup.State != StartupJoinState.Cancelled))
        {
            cancelPendingJoin = true;
            if (readyMenu) readyMenu.CancelInvoke("LoadMainSceneIfBackendSelected");
        }
        failureCategory = category; if (startup.Pending) startup.Block(category);
        policy.Halt(reason); manualStopped = true; password = profile = null; haveDestination = false;
        log("Reconnect halted: " + reason);
    }

    internal void Tick(bool authorized, bool observerActive)
    {
        if (cancelPendingJoin)
        {
            // A scene load already underway cannot be cancelled by Unity. Keep
            // observer restrictions and close that load as soon as Game exists.
            try { ReturnToMenu(); }
            catch (Exception error) { log("Cancelled reconnect cleanup failed: " + error.GetType().Name); cancelPendingJoin = false; }
            return;
        }
        bool enabled = configured() && observerConfigured();
        if (startup.Pending && (!enabled || !settings.StartupEnabled.Value))
        { Reject("Startup requires enabled observer connection and unattended recovery", "configuration"); return; }
        if (!enabled)
        {
            if (wasConfigured || Armed) OnManualStop(true);
            wasConfigured = false; return;
        }
        wasConfigured = true;
        if (!installed || manualStopped) return;
        double now = Time.realtimeSinceStartup;
        try
        {
            ZNet? net = ZNet.instance;
            ZNetPeer? server = net && !net.IsServer() ? net.GetServerPeer() : null;
            bool transportReady = server != null && server.IsReady() && server.m_rpc.IsConnected();
            if (Armed && authorized && net && transportReady && net.GetWorldUID() != policy.WorldId)
            {
                const string reason = "Connected world differs from the authorized session";
                Reject(reason); reject(reason); return;
            }
            if (authorized && observerActive && net && transportReady)
            {
                if (!Armed)
                {
                    if (!haveDestination || !Game.instance || !net.IsCurrentServerDedicated() || server!.m_socket is not ZSteamSocket)
                    { readiness = "Rejoin a dedicated Steam server with unattended recovery enabled to capture the destination"; return; }
                    if (passwordRequired && string.IsNullOrEmpty(password))
                    { readiness = "Rejoin with unattended recovery enabled so the server password can be retained in memory"; return; }
                    PlayerProfile selected = Game.instance.GetPlayerProfile();
                    if (startup.State == StartupJoinState.Connecting &&
                        (selected.GetFilename() != profile || selected.m_fileSource != profileSource))
                    { Reject("Startup character changed before authorization", "configuration"); reject("Startup character mismatch"); return; }
                    if (startup.State == StartupJoinState.Connecting) startup.Complete();
                    profile = selected.GetFilename(); profileSource = selected.m_fileSource;
                    if (string.IsNullOrEmpty(profile)) return;
                    policy.Arm(now, net.GetWorldUID()); owner = net;
                    log("Unattended connection recovery armed for this observer session.");
                }
                else if (!policy.Connected(now, net.GetWorldUID()))
                {
                    string reason = policy.LastFailure; Reject(reason); reject(reason); return;
                }
                else owner = net;
                return;
            }
            if (startup.Pending) { TickStartup(now); return; }
            if (!Armed) return;
            ZNet.ConnectionStatus status = ZNet.GetConnectionStatus();
            if (Terminal(status)) { Reject("Native connection rejected: " + status, "authentication"); return; }
            if (policy.State == ConnectionRecoveryState.Monitoring && (!net || net != owner || !transportReady))
                policy.Lost(now, "Observer connection lost");
            if (policy.TimedOut(now)) log("Observer reconnect timed out; backing off.");
            if (!Recovering) return;
            if (policy.State == ConnectionRecoveryState.Backoff)
            {
                ReturnToMenu();
                if (Game.instance || ZNet.instance || !readyMenu || readyMenu != FejdStartup.instance || now < menuReadyAt) return;
                if (!policy.BeginAttempt(now)) return;
                BeginJoin();
            }
        }
        catch (Exception error)
        {
            // Never repeat a broken Unity/reflection adapter indefinitely.
            string reason = "Reconnect adapter failed: " + error.GetType().Name;
            Reject(reason, "adapter"); reject(reason);
        }
    }

    internal IEnumerable<string> StartupProfiles()
    {
        if (!readyMenu || readyMenu != FejdStartup.instance || Game.instance)
            return new[] { "Character listing is available at the main menu after startup." };
        var list = Profiles?.GetValue(readyMenu) as List<PlayerProfile>;
        return list == null ? new[] { "Character list is not ready." } :
            list.Select(p => p.GetFilename() + " | " + p.m_fileSource).ToArray();
    }

    private void TickStartup(double now)
    {
        if (startup.State == StartupJoinState.Connecting && Terminal(ZNet.GetConnectionStatus()))
        { Reject("Startup connection was rejected", "authentication"); CloseFailedWorld(); return; }
        if (startup.TimedOut(now))
        {
            if (readyMenu) readyMenu.CancelInvoke("LoadMainSceneIfBackendSelected");
            // Native privilege callbacks have no cancellation token. If the first
            // scene never opened, do not reuse that menu for another attempt and
            // let a delayed callback impersonate the new attempt.
            if (!Game.instance && readyMenu && readyMenu == attemptMenu)
            { Reject("Native startup did not leave the menu before its deadline", "connection"); return; }
            ReturnToMenu();
            if (startup.State == StartupJoinState.Blocked)
            { Reject("Startup connection attempt budget exhausted", "connection"); return; }
        }
        if (startup.State == StartupJoinState.Connecting) return;
        if (Game.instance || ZNet.instance) { ReturnToMenu(); return; }
        if (!readyMenu || readyMenu != FejdStartup.instance || now < menuReadyAt || now < startup.NextAttemptAt) return;
        if (!startupPrepared)
        {
            if (!StartupJoinPolicy.ValidProfile(settings.StartupCharacter.Value) ||
                !StartupJoinPolicy.TryAddress(settings.StartupServer.Value, out string host, out ushort port) ||
                (settings.StartupCharacterSource.Value != "Local" && settings.StartupCharacterSource.Value != "Cloud"))
            { Reject("Startup character/source or dedicated server address is invalid", "configuration"); return; }
            profile = settings.StartupCharacter.Value;
            profileSource = settings.StartupCharacterSource.Value == "Local" ? FileHelpers.FileSource.Local : FileHelpers.FileSource.Cloud;
            destination = new ServerJoinData(new ServerJoinDataDedicated(host, port));
            if (!destination.IsValid) { Reject("Native startup server address is invalid", "configuration"); return; }
            haveDestination = true;
            try { password = ReadPassword(settings.StartupPasswordFile.Value); }
            catch { Reject("Startup password file is missing, unreadable or invalid", "configuration"); return; }
            startupPrepared = true;
        }
        // The native menu has already loaded profiles. Never create a character or
        // substitute a display-name match, a different save source, or the last profile.
        if (now < nextProfileCheck) return;
        // UpdateCharacterList redraws m_profiles once populated and can keep an
        // early empty/local-only list while Steam Cloud mounts. Read the native
        // save catalogue on retries without changing the user's menu selection.
        var profiles = Profiles!.GetValue(readyMenu) as List<PlayerProfile>;
        if (nextProfileCheck != 0)
        {
            SaveSystem.InvalidateCache(SaveDataType.Character);
            profiles = SaveSystem.GetAllPlayerProfiles();
        }
        PlayerProfile[] matches = profiles == null ? Array.Empty<PlayerProfile>() : profiles.Where(p =>
            p.GetFilename() == profile && p.m_fileSource == profileSource).ToArray();
        if (matches.Length == 0 && now < profileDeadline)
        { nextProfileCheck = now + 2; return; }
        if (matches.Length != 1 || !matches[0].Load() || matches[0].m_fileSource != profileSource || matches[0].HaveIncompatiblPlayerData())
        { Reject("Configured startup character is missing, ambiguous, unreadable or incompatible", "configuration"); return; }
        if (CinematicInstance?.GetValue(null) is CinematicsManager cinematic && cinematic && CinematicsManager.IsPlaying())
            CinematicsManager.Stop();
        if (startup.Begin(now)) BeginJoin();
    }

    private static string? ReadPassword(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (!Path.IsPathRooted(path) || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 1024) throw new IOException("Invalid password file");
        string value = File.ReadAllText(path).TrimEnd('\r', '\n');
        if (value.Length == 0 || value.Length > 256 || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            throw new IOException("Invalid password content");
        return value;
    }

    // Used only for this process's session-bound supervisor command. The caller
    // stops output first; no native profile save persists the observer's camera carrier.
    internal bool SupervisorShutdown()
    {
        if (supervisorShutdownFailed) return false;
        OnManualStop(true);
        if (ZNet.instance && ZNet.instance.IsServer()) return false;
        if (Game.instance)
        {
            try { ReturnToMenu(); }
            catch (Exception error)
            {
                // Keep the control heartbeat alive for the host's bounded
                // exact-process shutdown. Never repeat broken native cleanup
                // every frame, save the temporary carrier, or claim success.
                supervisorShutdownFailed = true;
                Reject("Supervisor no-save cleanup failed: " + error.GetType().Name, "adapter");
            }
            return false;
        }
        return !ZNet.instance;
    }

    internal bool RequestRecovery(string reason)
    {
        if (!Armed || manualStopped || !configured()) return false;
        policy.Lost(Time.realtimeSinceStartup, reason);
        try { ReturnToMenu(); }
        catch (Exception error) { Reject("Reconnect cleanup failed: " + error.GetType().Name); return false; }
        return true;
    }
    // Failure cleanup is mandatory even before recovery is armed, or when the
    // operator disabled unattended reconnect. It never grants retry permission.
    internal void CloseFailedWorld()
    {
        ZNet? net = ZNet.instance;
        if (!net || net.IsServer() || !Game.instance || Game.instance.IsShuttingDown() || failedExitWorld == net) return;
        failedExitWorld = net; // One attempt per world, including a failing native adapter.
        try { ReturnToMenu(); }
        catch (Exception error) { Reject("Failed observer world could not close: " + error.GetType().Name + "; exit the game manually"); }
    }

    private void ReturnToMenu()
    {
        if (!Game.instance || Game.instance.IsShuttingDown()) return;
        if (ZNet.instance && ZNet.instance.IsServer())
        { cancelPendingJoin = false; Reject("A local world replaced the observer connection; recovery stopped"); return; }
        internalExit = true;
        try
        {
            beforeReconnect();
            // This is the native shutdown + start-scene transition, explicitly
            // without persisting the carrier's temporary airborne travel position.
            ContinueLogout!.Invoke(Game.instance, new object[] { false, true, true });
        }
        finally { internalExit = false; }
    }
    private void BeginJoin()
    {
        if (!haveDestination || string.IsNullOrEmpty(profile)) { Reject("Original observer destination or character unavailable"); return; }
        internalJoin = true; passwordSubmittedTo = null; attemptMenu = readyMenu;
        try
        {
            readyMenu!.OnConnectionFailedOk();
            Game.SetProfile(profile!, profileSource);
            readyMenu.SetServerToJoin(destination);
            readyMenu.JoinServer();
            log("Joining observer through the native join flow; attempt " + (startup.Pending ? startup.Attempts : policy.Attempts) + ".");
        }
        finally { internalJoin = false; }
    }
    private static bool Terminal(ZNet.ConnectionStatus status) => status == ZNet.ConnectionStatus.ErrorVersion ||
        status == ZNet.ConnectionStatus.ErrorPassword || status == ZNet.ConnectionStatus.ErrorBanned ||
        status == ZNet.ConnectionStatus.ErrorKicked || status == ZNet.ConnectionStatus.ErrorPlatformExcluded ||
        status == ZNet.ConnectionStatus.ErrorCrossplayPrivilege || status == ZNet.ConnectionStatus.ErrorAlreadyConnected;
    internal void Dispose() { OnManualStop(true); if (instance == this) instance = null; }
}
