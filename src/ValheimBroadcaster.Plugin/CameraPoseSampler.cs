using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal static class CameraPoseSampler
{
    internal static CameraRenderPose Sample(Camera camera)
    {
        try { return Sample(camera, camera.transform.position, camera.transform.rotation, camera.fieldOfView); }
        catch { return default; } // Diagnostic failure cannot take down the program picture.
    }

    internal static CameraRenderPose Sample(Camera camera, Vector3 position, Quaternion rotation, float fov)
    {
        try
        {
            return new CameraRenderPose(position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w, fov,
                Projection(camera.projectionMatrix), Projection(camera.nonJitteredProjectionMatrix));
        }
        catch { return default; }
    }
    private static CameraProjectionSnapshot Projection(Matrix4x4 m) =>
        new CameraProjectionSnapshot(m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33);
}
