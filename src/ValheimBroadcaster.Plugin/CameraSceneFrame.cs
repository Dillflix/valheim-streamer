using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Observed scene data shared by camera subjects. Providers own acquisition and
// evidence; the camera solver owns presentation. A landscape has no fake actor.
internal sealed class CameraSceneSubject
{
    internal Character Actor = null!;
    internal Player? Player;
    internal string Id = "", CharacterId = "";
    internal Vector3 Focus, Velocity;
    internal float Weight, Radius, Height, BottomOffset;
    internal bool IsPrimary, IsOpponent;
}

internal sealed class CameraSceneFrame
{
    internal readonly List<CameraSceneSubject> Subjects = new List<CameraSceneSubject>(6);
    internal Vector3 PrimaryFocus, Focus, Velocity, GroupVelocity;
    // Body orientation is observable, but is not a claim about eye gaze or intent.
    internal Vector3 FacingDirection, InterestFocus;
    internal float FacingConfidence, InterestConfidence;
    internal bool HasInterest;
    internal string InterestKind = "none";
    internal float Spread, Confidence;
    // Interior retains Valheim's dungeon-layer meaning for readiness. Measured
    // buildings constrain cinematography without changing that physical contract.
    internal bool Interior;
    internal ShotEnvironment Environment = ShotEnvironment.Unknown;
    internal ShotLocomotion Locomotion;
    internal float EnvironmentConfidence, LocomotionConfidence, CeilingHeight;
    internal bool DoorwayTransition;
    internal Vector3 OpeningDirection;
    // Unique within this context lifetime; retained across a validated handoff
    // to an already admitted companion, never across world/respawn/travel gaps.
    internal long SceneId;
    internal CinematicActivity Activity;
    internal string Description = "Awaiting loaded scene context";
}
