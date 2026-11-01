using System;
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
            camera.UpdateCameraState(worldUp, deltaTime);
        }
        finally { CameraUpdateManager.s_CurrentUpdateFilter = previous; }
    }
}
