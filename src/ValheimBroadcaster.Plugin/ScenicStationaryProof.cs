using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>One full physical and picture check of an immutable stationary
/// composition. Callers own a shared Scenic work-budget lease. No spline,
/// fitting, angular sweep or repeated constant-path samples are involved.</summary>
internal static class ScenicStationaryProof
{
    internal static bool Check(ScenicShotPlan picture, ScenicVisibility visibility,
        float aspect, float nearClip, bool moveCarrier, out ScenicRejectionKind kind, out string reason)
    {
        if (picture == null || !picture.IsPictureOnly)
            throw new ArgumentException("A stationary composition is required.", nameof(picture));
        ScenicShotSample sample = picture.Sample(0);
        Vector3 position = Vector(sample.CameraPosition), focus = Vector(sample.AimPosition);
        var pose = new ScenicPose(position, focus,
            ScenicVisibility.Compose(position, focus, sample.ScreenX, sample.ScreenY, picture.FieldOfView, aspect),
            picture.FieldOfView, 0);
        return ScenicPoseValidation.Check(pose, picture, visibility, aspect, nearClip, false, moveCarrier,
            out _, out kind, out reason);
    }

    private static Vector3 Vector(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
