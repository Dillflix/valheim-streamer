using System;
using System.Reflection;
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
    private readonly ConnectionRecoveryPolicy policy = new ConnectionRecoveryPolicy();
    private static readonly MethodInfo? PasswordEntered = AccessTools.Method(typeof(ZNet), "OnPasswordEntered", new[] { typeof(string) });
    private static readonly MethodInfo? ContinueLogout = AccessTools.Method(typeof(Game), "ContinueLogout", new[] { typeof(bool), typeof(bool), typeof(bool) });
    private ServerJoinData destination;
    private string? password, profile;
    private FileHelpers.FileSource profileSource;
    private ZNet? owner, failedExitWorld;
    private FejdStartup? readyMenu;
    private double menuReadyAt;
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
        Action<string> reject, Action beforeReconnect)
    {
        this.configured = configured; this.observerConfigured = observerConfigured;
        this.log = log; this.reject = reject; this.beforeReconnect = beforeReconnect;
        instance = this;
    }

    internal void Install(Harmony harmony)
    {
        if (PasswordEntered == null || ContinueLogout == null) throw new MissingMethodException("Native observer reconnect lifecycle unavailable");
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
        if (recovery.cancelPendingJoin && join == recovery.destination) return false;
        // The native privilege check can resume JoinServer asynchronously.
        if (recovery.policy.State == ConnectionRecoveryState.Connecting && join == recovery.destination) return true;
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
    }
    private static void MenuInteraction()
    {
        if (instance == null || instance.internalJoin) return;
        if (instance.Recovering || instance.cancelPendingJoin) instance.OnManualStop(true);
        instance.cancelPendingJoin = false;
    }
    private static void ServerSelected()
    {
        // Native UI selection is a new explicit intent; async JoinServer privilege
        // callbacks do not call this setter and remain cancelled after operator stop.
        if (instance != null && !instance.internalJoin) instance.cancelPendingJoin = false;
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
        if (recovery != null && recovery.haveDestination) recovery.passwordRequired = __1;
        if (recovery == null || !recovery.Recovering || !__1 || string.IsNullOrEmpty(recovery.password) ||
            !__instance.InPasswordDialog() || !ReferenceEquals(__instance.GetServerRPC(), __0)) return;
        // The original handler displayed the normal password dialog and stored its
        // RPC. Submit through that same handler; normal password hashing/auth runs.
        try { PasswordEntered!.Invoke(__instance, new object[] { recovery.password! }); }
        catch { recovery.Reject("Saved connection password could not be submitted"); }
    }

    internal void OnManualStart() { manualStopped = cancelPendingJoin = false; }
    internal void OnManualStop(bool forgetConnection = false)
    {
        if (policy.State == ConnectionRecoveryState.Connecting)
        {
            cancelPendingJoin = true;
            if (readyMenu) readyMenu.CancelInvoke("LoadMainSceneIfBackendSelected");
        }
        policy.Cancel(); manualStopped = true; owner = null;
        if (forgetConnection) { haveDestination = false; password = profile = null; }
        ManualStopped?.Invoke();
    }
    internal void Reject(string reason)
    {
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
            if (!Armed) return;
            ZNet.ConnectionStatus status = ZNet.GetConnectionStatus();
            if (Terminal(status)) { Reject("Native connection rejected: " + status); return; }
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
            Reject(reason); reject(reason);
        }
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
        internalJoin = true;
        try
        {
            readyMenu!.OnConnectionFailedOk();
            Game.SetProfile(profile!, profileSource);
            readyMenu.SetServerToJoin(destination);
            readyMenu.JoinServer();
            log("Reconnecting observer through the native join flow; attempt " + policy.Attempts + ".");
        }
        finally { internalJoin = false; }
    }
    private static bool Terminal(ZNet.ConnectionStatus status) => status == ZNet.ConnectionStatus.ErrorVersion ||
        status == ZNet.ConnectionStatus.ErrorPassword || status == ZNet.ConnectionStatus.ErrorBanned ||
        status == ZNet.ConnectionStatus.ErrorKicked || status == ZNet.ConnectionStatus.ErrorPlatformExcluded ||
        status == ZNet.ConnectionStatus.ErrorCrossplayPrivilege || status == ZNet.ConnectionStatus.ErrorAlreadyConnected;
    internal void Dispose() { OnManualStop(true); if (instance == this) instance = null; }
}
