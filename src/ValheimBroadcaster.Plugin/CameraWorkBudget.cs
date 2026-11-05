using System;
using System.Diagnostics;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>One measured scheduler for all camera rigs and scene observers.
/// Nested scopes charge exclusive time, not the same work twice. The budget is
/// cooperative: each indivisible Unity query can overrun and is measured.</summary>
internal sealed class CameraWorkBudget
{
    internal static readonly CameraWorkBudget Shared = new CameraWorkBudget();
    private readonly CameraWorkSchedule schedule = new CameraWorkSchedule();
    private readonly CameraWorkKind[] kinds = new CameraWorkKind[16];
    private readonly int[] tokens = new int[16];
    private int depth, sequence, frame = -1;
    private long sampled;
    internal string Diagnostics => $"Camera CPU: live {schedule.LiveMilliseconds:F2}ms; background {schedule.BackgroundSpent:F2}/{CameraWorkSchedule.BackgroundMilliseconds:F1}ms; observations {schedule.ObservationMilliseconds:F2}ms; deferred {schedule.Deferred}; largest slice {schedule.LargestOperation:F2}ms";
    internal bool WithinLive
    {
        get { Synchronize(); for (int i = 0; i < depth; i++) if (kinds[i] == CameraWorkKind.Live) return true; return false; }
    }

    internal readonly struct Lease : IDisposable
    {
        private readonly CameraWorkBudget? owner;
        private readonly int token;
        internal Lease(CameraWorkBudget owner, int token) { this.owner = owner; this.token = token; }
        public void Dispose() => owner?.End(token);
    }

    internal bool TryBegin(CameraWorkKind kind, out Lease lease)
    {
        Synchronize(); Account(); lease = default;
        if (depth == kinds.Length || !schedule.TryStart(kind)) return false;
        int token = ++sequence;
        kinds[depth] = kind; tokens[depth++] = token;
        sampled = Stopwatch.GetTimestamp(); lease = new Lease(this, token); return true;
    }

    internal bool CanContinue(CameraWorkKind kind)
    { Synchronize(); Account(); return schedule.CanContinue(kind); }

    private void End(int token)
    {
        if (depth == 0 || tokens[depth - 1] != token) return;
        Account(); depth--; sampled = Stopwatch.GetTimestamp();
    }
    private void Synchronize()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount; depth = 0; sampled = Stopwatch.GetTimestamp(); schedule.BeginFrame(frame);
    }
    private void Account()
    {
        long now = Stopwatch.GetTimestamp();
        if (depth > 0) schedule.Charge(kinds[depth - 1], (now - sampled) * 1000.0 / Stopwatch.Frequency);
        sampled = now;
    }
}
