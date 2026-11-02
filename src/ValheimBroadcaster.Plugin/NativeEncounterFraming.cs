using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Adapts observed encounter evidence to composition without a second lens or
// rotation solver. All native aim/group components retain their existing ownership.
internal sealed class NativeEncounterFraming
{
    private readonly EncounterAxisPolicy axis = new EncounterAxisPolicy();
    internal float Heading { get; private set; }
    internal void Update(CameraSceneFrame context, float now, float baseline) =>
        Heading = axis.Update(now, context.SceneId, baseline, context.Encounter,
            context.EncounterConfidence, context.EncounterDirection.x, context.EncounterDirection.z);
    internal void Reset() { axis.Reset(); Heading = 0; }
    internal static bool Active(CameraSceneFrame frame) => EncounterFramingPolicy.Meaningful(frame.Encounter, frame.EncounterConfidence) &&
        TargetRoster.Finite(frame.EncounterFocus) && TargetRoster.Finite(frame.EncounterDirection);
    internal static Vector3 Interest(CameraSceneFrame frame) => Active(frame) ? frame.EncounterFocus : frame.InterestFocus;
    internal static float Confidence(CameraSceneFrame frame) => Active(frame) ? frame.EncounterConfidence : frame.InterestConfidence;

    internal static float Penalty(CameraSceneFrame frame, Vector3 camera, Quaternion rotation,
        float fov, float aspect, float near, PlayerShotVisibility visibility,
        PlayerShotVisibility.Result view, int framedCount)
    {
        if (!Active(frame)) return 0;
        Vector3 radial = Vector3.ProjectOnPlane(camera - frame.PrimaryFocus, Vector3.up);
        Vector3 direction = Vector3.ProjectOnPlane(frame.EncounterDirection, Vector3.up);
        float dot = radial.sqrMagnitude > .01f && direction.sqrMagnitude > .01f
            ? Vector3.Dot(radial.normalized, direction.normalized) : 0;
        bool target = visibility.InterestVisible(camera, rotation, frame.EncounterFocus, fov, aspect, near);
        Vector3 local = Quaternion.Inverse(rotation) * (frame.EncounterFocus - camera);
        float size = local.z > near ? frame.EncounterTargetHeight / (2f * local.z * Mathf.Tan(fov * Mathf.Deg2Rad * .5f)) : 0;
        int opponents = 0, visible = 0;
        for (int i = 0; i < frame.Subjects.Count; i++)
            if (frame.Subjects[i].IsOpponent)
            { opponents++; if (i < framedCount && (view.VisibleMask & (1 << i)) != 0) visible++; }
        return EncounterFramingPolicy.ViewPenalty(frame.Encounter, frame.EncounterConfidence, dot, target,
            Mathf.Max(0, size), opponents, visible);
    }
}
