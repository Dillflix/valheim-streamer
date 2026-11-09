using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Local, session-bound control for the owning supervisor. No remote listener and
// no endpoint/password/stream URL enters this protocol. Files live in its private
// directory (a Sandboxie guest path on Windows, mapped by the host supervisor).
internal sealed class SupervisorBridge
{
    private readonly string directory, session, pluginHash;
    private readonly int pid;
    private readonly Action<string> log;
    private float nextTick;
    private long sequence;
    private bool loggedFailure;
    internal bool ShutdownRequested { get; private set; }

    private SupervisorBridge(string directory, string session, Action<string> log)
    {
        this.directory = directory; this.session = session; this.log = log;
        pid = Process.GetCurrentProcess().Id;
        using var algorithm = SHA256.Create();
        using var input = File.OpenRead(Assembly.GetExecutingAssembly().Location);
        pluginHash = BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    internal static SupervisorBridge? Create(Action<string> log)
    {
        string? session = Environment.GetEnvironmentVariable("VB_SESSION_ID");
        string? directory = Environment.GetEnvironmentVariable("VB_CONTROL_DIR");
        string[] args = Environment.GetCommandLineArgs();
        string? argSession = null, argDirectory = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] != "--vb-session" && args[i] != "--vb-control-dir") continue;
            bool isSession = args[i] == "--vb-session";
            if (++i == args.Length || (isSession ? argSession != null : argDirectory != null))
                throw new ArgumentException("Ambiguous supervisor startup arguments");
            if (isSession) argSession = args[i]; else argDirectory = args[i];
        }
        if (argSession != null || argDirectory != null)
        {
            if (argSession == null || argDirectory == null) throw new ArgumentException("Incomplete supervisor startup arguments");
            // Exact paired command arguments take precedence over inherited Steam
            // environment values, which may refer to a preceding launch.
            session = argSession; directory = argDirectory;
        }
        if (string.IsNullOrEmpty(session) && string.IsNullOrEmpty(directory)) return null;
        if (!ValidSession(session) || string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory))
            throw new ArgumentException("Invalid supervisor session or control directory");
        directory = Path.GetFullPath(directory);
        // Sandboxie creates this private guest directory on demand. The host maps
        // precisely this path; never select a shared fallback directory.
        Directory.CreateDirectory(directory);
        CheckDirectory(directory);
        return new SupervisorBridge(directory, session!, log);
    }

    private static bool ValidSession(string? value)
    {
        if (value == null || value.Length < 32 || value.Length > 128) return false;
        foreach (char c in value) if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-') return false;
        return true;
    }
    private static void CheckDirectory(string path)
    {
        for (DirectoryInfo? current = new DirectoryInfo(path); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Supervisor directory is a link");
    }
    private static void CheckFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Supervisor file is a link");
    }

    internal void Tick(string state, bool authorized, bool active, bool publishing, string failureCategory, string reason, int encoderPid = 0, bool pictureLive = false, int[]? encoderPids = null, string startupPhase = "")
    {
        if (Time.realtimeSinceStartup < nextTick) return;
        nextTick = Time.realtimeSinceStartup + 1;
        try
        {
            CheckDirectory(directory);
            string commandPath = Path.Combine(directory, "command.json");
            CheckFile(commandPath);
            if (!ShutdownRequested && File.Exists(commandPath) && new FileInfo(commandPath).Length <= 4096)
            {
                // A partial/malformed/stale request cannot stop heartbeat output or
                // become evidence that the game hung. The supervisor writes atomically.
                try
                {
                    Command? command = JsonUtility.FromJson<Command>(File.ReadAllText(commandPath));
                    if (command != null && command.schema == 1 && command.session_id == session && command.command == "shutdown")
                        ShutdownRequested = true;
                }
                catch (ArgumentException) { }
            }
            Publish(state, authorized, active, publishing, failureCategory, reason, encoderPid, pictureLive, encoderPids, startupPhase);
        }
        catch (Exception error)
        {
            if (!loggedFailure) { loggedFailure = true; log("Supervisor control unavailable: " + error.GetType().Name); }
        }
    }

    internal void Publish(string state, bool authorized, bool active, bool publishing, string failureCategory, string reason, int encoderPid = 0, bool pictureLive = false, int[]? encoderPids = null, string startupPhase = "")
    {
        // A requested update stop is not a new operator cancellation, including
        // the intermediate frames before native logout and encoder drain finish.
        if (ShutdownRequested) reason = failureCategory == "none" ? "supervisor_shutdown" : failureCategory;
        var status = new Status
        {
            startup_phase = startupPhase, session_id = session, pid = pid, version = BroadcasterPlugin.PluginVersion,
            plugin_sha256 = pluginHash, source_sha = BroadcasterPlugin.SourceRevision, sequence = ++sequence, encoder_pid = encoderPid,
            encoder_pids = encoderPids ?? Array.Empty<int>(),
            state = state, observer_authorized = authorized, observer_active = active,
            publishing = publishing, picture_live = pictureLive, failure_category = failureCategory, reason = reason
        };
        string target = Path.Combine(directory, "status.json"), temporary = Path.Combine(directory, "status.json.tmp");
        CheckFile(target); CheckFile(temporary);
        byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(status));
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(data, 0, data.Length); file.Flush(true); }
        if (File.Exists(target)) File.Replace(temporary, target, null);
        else File.Move(temporary, target);
    }

    internal void Exiting()
    {
        try { Publish("stopped", false, false, false, "none", ShutdownRequested ? "supervisor_shutdown" : "operator_exit"); }
        catch { /* Process exit must not be prevented by unavailable status storage. */ }
    }

    [Serializable]
    private sealed class Command
    {
        public int schema = 0;
        public string session_id = "", command = "";
    }
    [Serializable]
    private sealed class Status
    {
        public string startup_phase = "";
        public int schema = 1, supervisor_protocol = SupervisorIdentity.Protocol, pid, encoder_pid;
        public int[] encoder_pids = Array.Empty<int>();
        public long sequence;
        public string session_id = "", version = "", plugin_sha256 = "", source_sha = "", state = "", failure_category = "", reason = "";
        public bool observer_authorized, observer_active, publishing, picture_live;
    }
}
