using System;
using System.Runtime.CompilerServices;
using Unity.Cinemachine;
using UnityEngine;

namespace ValheimBroadcaster.CinemachineRuntime;

/// <summary>
/// Drives the isolated Cinemachine solver without a Brain writing Valheim's
/// rendering camera. The game adapter applies the resulting pose after checking
/// scene support and its observer carrier. Call on Unity's main thread.
/// </summary>
public static class ManualCameraUpdate
{
    private static readonly ConditionalWeakTable<CinemachineVirtualCameraBase, ManualCameraClock> clocks = new();
    public static void Update(CinemachineVirtualCameraBase camera, Vector3 worldUp, float deltaTime)
    {
        if (!camera) throw new ArgumentNullException(nameof(camera));
        // 3.1.7's Brain normally maintains this clock. Standalone virtual-camera
        // updates otherwise stop after their first frame because it remains 0.
        CinemachineCore.CurrentUpdateFrame = Time.frameCount;
        var previous = CameraUpdateManager.s_CurrentUpdateFilter;
        try
        {
            CameraUpdateManager.s_CurrentUpdateFilter = CameraUpdateManager.UpdateFilter.Late;
            float compensated = clocks.GetOrCreateValue(camera).Prepare(Time.frameCount, Time.unscaledTimeAsDouble,
                deltaTime, CinemachineCore.UnitTestMode);
            camera.UpdateCameraState(worldUp, compensated);
        }
        finally { CameraUpdateManager.s_CurrentUpdateFilter = previous; }
    }
}

/// <summary>One per-camera elapsed-time owner. The native manager multiplies an
/// input delta by skipped render frames; supply the quotient of actual elapsed
/// time so irregular standby cadence is integrated exactly once. It retains the
/// native first-update, same-frame deduplication and negative-delta reset rules.</summary>
public sealed class ManualCameraClock
{
    private int frame = -1;
    private double sampledAt;
    public float Prepare(int nextFrame, double now, float frameDelta, bool unitTestMode = false)
    {
        if (nextFrame < 0 || double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
            float.IsNaN(frameDelta) || float.IsInfinity(frameDelta)) throw new ArgumentOutOfRangeException(nameof(now));
        int skipped = nextFrame - frame;
        if (frame >= 0 && skipped == 0 && frameDelta >= 0) return frameDelta;
        float result = frameDelta;
        if (frame >= 0 && skipped > 0 && now >= sampledAt && frameDelta >= 0)
            result = (float)((now - sampledAt) / (unitTestMode ? 1 : skipped));
        frame = nextFrame; sampledAt = now;
        return result;
    }
}
