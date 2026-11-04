using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Adapts observed encounter evidence to composition without a second lens or
// rotation solver. All native aim/group components retain their existing ownership.
internal sealed class NativeEncounterFraming
{
    private readonly EncounterAxisPolicy axis = new EncounterAxisPolicy();
    internal float Heading { get; private set; }
    internal void Update(CameraSceneFrame context, float now, float baseline, bool prioritizeEncounter = true) =>
        Heading = axis.Update(now, context.SceneId, baseline, prioritizeEncounter ? context.Encounter : EncounterKind.None,
            context.EncounterConfidence, context.EncounterDirection.x, context.EncounterDirection.z);
    internal void Reset() { axis.Reset(); Heading = 0; }
    internal static bool Active(CameraSceneFrame frame) => EncounterFramingPolicy.Meaningful(frame.Encounter, frame.EncounterConfidence) &&
        TargetRoster.Finite(frame.EncounterFocus) && TargetRoster.Finite(frame.EncounterDirection);
    internal static Vector3 Interest(CameraSceneFrame frame, bool prioritizeEncounter = true) => prioritizeEncounter && Active(frame) ? frame.EncounterFocus : frame.InterestFocus;
    internal static float Confidence(CameraSceneFrame frame, bool prioritizeEncounter = true) => prioritizeEncounter && Active(frame) ? frame.EncounterConfidence : frame.InterestConfidence;

    internal static float Penalty(CameraSceneFrame frame, Vector3 camera, Quaternion rotation,
        float fov, float aspect, float near, PlayerShotVisibility visibility,
        PlayerShotVisibility.Result view, IReadOnlyList<PlayerShotVisibility.Subject> framed)
    {
        if (!Active(frame)) return 0;
        Vector3 radial = Vector3.ProjectOnPlane(camera - frame.PrimaryFocus, Vector3.up);
        Vector3 direction = Vector3.ProjectOnPlane(frame.EncounterDirection, Vector3.up);
        float dot = radial.sqrMagnitude > .01f && direction.sqrMagnitude > .01f
            ? Vector3.Dot(radial.normalized, direction.normalized) : 0;
        bool target = visibility.InterestVisible(camera, rotation, frame.EncounterFocus, fov, aspect, near,
            frame.EncounterActor ? frame.EncounterActor.GetInstanceID() : 0);
        Vector3 local = Quaternion.Inverse(rotation) * (frame.EncounterFocus - camera);
        float size = local.z > near ? frame.EncounterTargetHeight / (2f * local.z * Mathf.Tan(fov * Mathf.Deg2Rad * .5f)) : 0;
        int opponents = 0, visible = 0;
        foreach (CameraSceneSubject subject in frame.Subjects)
            if (subject.IsOpponent)
            {
                opponents++;
                for (int i = 0; i < framed.Count; i++)
                    if (subject.Actor && framed[i].ActorId == subject.Actor.GetInstanceID() &&
                        (view.VisibleMask & (1 << i)) != 0) { visible++; break; }
            }
        float pressureWeight = frame.Encounter == EncounterKind.Threat && frame.EncounterSituation.Pressure == EncounterPressure.Presence ? .35f : 1f;
        return pressureWeight * EncounterFramingPolicy.ViewPenalty(frame.Encounter, frame.EncounterConfidence, dot, target,
            Mathf.Max(0, size), opponents, visible);
    }

    internal static EncounterCoverage Coverage(CameraSceneFrame frame, EncounterShotPurpose purpose,
        Vector3 camera, Quaternion rotation, float fov, float aspect, float near, PlayerShotVisibility visibility,
        PlayerShotVisibility.Result view, IReadOnlyList<PlayerShotVisibility.Subject> framed, bool primaryReadable)
    {
        int opponents = 0, companions = 0, visibleOpponents = 0, visibleCompanions = 0, sectors = 0, observedSectors = 0;
        bool keyVisible = false, keyAdmitted = false;
        foreach (CameraSceneSubject subject in frame.Subjects)
        {
            if (subject.IsPrimary) continue;
            int sector = EncounterCastPolicy.Sector(ToCore(frame.PrimaryFocus), ToCore(subject.Focus));
            if (subject.IsOpponent) { opponents++; observedSectors |= sector; } else companions++;
            bool visible = false;
            for (int i = 0; i < framed.Count; i++)
                if (subject.Actor && framed[i].ActorId == subject.Actor.GetInstanceID())
                { visible = (view.VisibleMask & (1 << i)) != 0; break; }
            if (subject.CharacterId == frame.EncounterTargetId) { keyAdmitted = true; keyVisible = visible; }
            if (!visible) continue;
            if (subject.IsOpponent) { visibleOpponents++; sectors |= sector; } else visibleCompanions++;
        }
        if (!keyAdmitted && frame.EncounterActor)
        {
            // A distant quarry is not forced into the group lens. Its actual
            // visible silhouette/size can still support an exchange overview.
            Vector3 local = Quaternion.Inverse(rotation) * (frame.EncounterFocus - camera);
            float height = local.z > near ? frame.EncounterTargetHeight / (2 * local.z * Mathf.Tan(fov * Mathf.Deg2Rad * .5f)) : 0;
            keyVisible = height >= .02f && visibility.InterestVisible(camera, rotation, frame.EncounterFocus,
                fov, aspect, near, frame.EncounterActor.GetInstanceID());
            if (keyVisible) visibleOpponents++;
        }
        // Wider crowd evidence informs whether the scene is explained, even
        // when some actors did not fit this shot's bounded observation cast.
        observedSectors |= frame.EncounterSituation.SectorMask;
        opponents = Mathf.Max(opponents, frame.EncounterSituation.HostileCount);
        return EncounterCoveragePolicy.Measure(purpose, primaryReadable, keyVisible, opponents, companions,
            observedSectors, visibleOpponents, visibleCompanions, sectors);
    }
    private static AutonomyPosition ToCore(Vector3 position) => new AutonomyPosition(position.x, position.y, position.z);
}
