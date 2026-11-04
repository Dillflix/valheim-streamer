using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed class CameraRig
{
    private readonly CinemachineShotRig shotCamera = new CinemachineShotRig();
    // Wildlife is discretionary. Its candidate solve must not replace the
    // current player's trajectories or the scenic view before admission.
    private readonly CinemachineShotRig creatureCamera = new CinemachineShotRig();
    private readonly PlayerComplementaryCamera complementary = new PlayerComplementaryCamera();
    private readonly ScenicCameraRig scenicCamera = new ScenicCameraRig();
    private bool proceduralActive, scenicActive, creatureActive, complementaryActive, presentationConfirmed, editorialHold;
    internal bool ComplementaryActive => complementaryActive;
    internal bool StoryContinuityReady { get; private set; }
    internal bool DisplayedPlayerUsable => !scenicActive && !creatureActive && !complementaryActive && LastPictureUsable;
    internal string PerspectiveDescription => complementaryActive ? complementary.Description : scenicActive ? "Scenic" : creatureActive ? "Creature" : "Player primary";
    internal long PresentedActorShotId => proceduralActive && !scenicActive ? ActorCamera.PresentedShotId : -1;
    private CinemachineShotRig ActorCamera => complementaryActive ? complementary.Rig : creatureActive ? creatureCamera : shotCamera;
    private string? presentationNotice;
    private string legacyMode = "awaiting coverage";
    internal string Diagnostics => presentationNotice != null ? "Camera: " + presentationNotice :
        scenicActive ? scenicCamera.Diagnostics : proceduralActive ? (complementaryActive ? complementary.Description + "; " : "") + ActorCamera.Diagnostics : "Camera: legacy " + legacyMode;
    internal bool LastPictureUsable { get; private set; }
    internal bool IsProcedural => proceduralActive;
    internal string PictureReason => scenicActive ? scenicCamera.LastFailure : proceduralActive ? ActorCamera.PictureReason : LastFailure;
    internal string EncounterCoverageDescription => proceduralActive && !scenicActive ? ActorCamera.EncounterCoverageDescription : "";
    internal int FramedParticipantCount => proceduralActive && !scenicActive ? ActorCamera.FramedParticipantCount : 0;
    internal int VisibleOpponentCount => proceduralActive && !scenicActive ? ActorCamera.VisibleOpponentCount : 0;
    internal float PrimaryActorOcclusion => proceduralActive && !scenicActive ? ActorCamera.PrimaryActorOcclusion : 0;
    internal float FieldOfView => camera ? camera.fieldOfView : 75f;
    internal Quaternion Rotation => camera ? camera.transform.rotation : Quaternion.identity;
    internal IReadOnlyList<string> VisiblePlayerIds => proceduralActive && !scenicActive && !creatureActive && !complementaryActive ? shotCamera.VisiblePlayerIds : Array.Empty<string>();
    internal RemoteObserverClient? RemoteContext { set => shotCamera.RemoteContext = value; }
    internal CameraSceneFrame? CurrentPlayerScene => proceduralActive && !scenicActive && !creatureActive ? shotCamera.CurrentPlayerScene : null;
    internal string? CurrentPlayerId => proceduralActive && !scenicActive && !creatureActive ? shotCamera.CurrentPlayerId : null;
    internal float PlayerSceneObservedAt => proceduralActive && !scenicActive && !creatureActive ? shotCamera.PlayerSceneObservedAt : -1;
    internal bool PresentationConfirmed { set { presentationConfirmed = value; } }
    internal bool EditorialHold { set { editorialHold = value; shotCamera.EditorialHold = value; creatureCamera.EditorialHold = value; } }
    internal RenderedPictureMonitor? PictureMonitor { set { shotCamera.PictureMonitor = value; creatureCamera.PictureMonitor = value; complementary.Rig.PictureMonitor = value; scenicCamera.PictureMonitor = value; } }
    internal RenderedPictureContext? PictureContext => scenicActive && LastPictureUsable ? scenicCamera.PictureContext :
        proceduralActive && LastPictureUsable ? ActorCamera.BuildPictureContext() : null;
    internal bool ScenicMovementAccepted => scenicActive && scenicCamera.MovementAccepted;
    internal float ScenicPresentedProgress => scenicActive ? scenicCamera.PresentedProgress : 0;
    internal long ScenicPresentedPlanId => scenicActive ? scenicCamera.PresentedPlanId : 0;
    internal Vector3? ScenicPresentedFocus => scenicActive && LastPictureUsable ? scenicCamera.PresentedFocus : null;
    internal ScenicRejectionKind ScenicFailureKind => scenicActive ? scenicCamera.FailureKind : ScenicRejectionKind.None;
    internal string ScenicRejectionReason => scenicActive ? scenicCamera.LastFailure : "Scenic camera inactive";
    private Camera? camera;
    private Camera? skyCamera;
    private float savedFov;
    private float savedSkyFov;
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private Vector3 smoothedPosition;
    private bool hasPose;
    private long revision = -1;
    private GameObject? hiddenHud;
    private bool hudWasActive;
    private static readonly FieldInfo? HudRoot = AccessTools.Field(typeof(Hud), "m_rootObject");
    private static readonly FieldInfo? SkyCamera = AccessTools.Field(typeof(GameCamera), "m_skyCamera");
    private int? obstructionMask;
    internal Vector3? Position => camera ? camera.transform.position : (Vector3?)null;
    internal string LastFailure { get; private set; } = "none";
    private bool Unready(string reason)
    { LastFailure = reason; LastPictureUsable = StoryContinuityReady = false; complementary.Reset(); complementaryActive = false; presentationNotice = "unavailable: " + reason; return false; }
    internal void MarkUnavailable(string reason) => Unready(reason);

    // Call at a bounded discovery cadence while coverage is not Creature. This
    // uses the same native solve and readability gate as presentation, without
    // writing the rendered camera, moving the carrier, or crediting footage.
    // Reusing the forthcoming coverage revision keeps that validated solve at
    // admission; Apply still checks the selected creature on each later frame.
    internal bool CanFrameCreature(GameCamera gameCamera, Character actor, BroadcastSettings settings,
        long selectionRevision, bool moveCarrier = true)
    {
        if (!settings.ProceduralPlayers.Value || !gameCamera || !actor || actor.IsDead()) return false;
        Camera output = gameCamera.GetComponent<Camera>();
        if (!output) output = gameCamera.GetComponentInChildren<Camera>();
        if (!output) return false;
        creatureCamera.RenderingCamera = output;
        creatureCamera.NearClipPlane = output.nearClipPlane;
        creatureCamera.Aspect = output.aspect;
        creatureCamera.PresentationConfirmed = false;
        creatureCamera.EditorialHold = false;
        return creatureCamera.TrySolve(CinematicShotInput.ForCreature(actor, selectionRevision), settings,
            0, false, moveCarrier, out _, out _, out _) && creatureCamera.LastPictureUsable;
    }

    internal bool Apply(GameCamera gameCamera, Vector3 focus, float azimuth, BroadcastSettings settings, long selectionRevision, float dt, Vector3? scenicOffset = null, bool continuousPlayer = false, TargetRoster.Target? playerTarget = null, bool moveCarrier = false, Character? creatureTarget = null, ScenicShotPlan? scenicPlan = null, float scenicProgress = 0, ScenicVisibility? scenicVisibility = null)
    {
        presentationNotice = null; LastPictureUsable = StoryContinuityReady = false;
        obstructionMask ??= LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Camera next = gameCamera.GetComponent<Camera>();
        if (!next) next = gameCamera.GetComponentInChildren<Camera>();
        if (!next) throw new InvalidOperationException("GameCamera has no Camera component.");
        if (camera != next)
        {
            RestoreCamera();
            camera = next;
            savedFov = next.fieldOfView;
            savedPosition = next.transform.position;
            savedRotation = next.transform.rotation;
        }
        Camera? nextSky = SkyCamera?.GetValue(gameCamera) as Camera;
        if (skyCamera != nextSky)
        {
            RestoreSky();
            skyCamera = nextSky;
            if (skyCamera) savedSkyFov = skyCamera.fieldOfView;
        }
        CinematicShotInput? input = playerTarget != null ? CinematicShotInput.ForPlayer(playerTarget, selectionRevision) :
            creatureTarget ? CinematicShotInput.ForCreature(creatureTarget!, selectionRevision) :
            scenicPlan != null ? CinematicShotInput.ForScenic(scenicPlan, scenicProgress, selectionRevision, scenicVisibility) : (CinematicShotInput?)null;
        if (settings.ProceduralPlayers.Value && playerTarget == null && !creatureTarget && scenicPlan?.IsDirected == true)
        {
            if (!scenicActive) { shotCamera.Reset(); scenicCamera.Reset(); }
            complementary.Reset(); complementaryActive = false;
            scenicActive = proceduralActive = true; creatureActive = false;
            if (!scenicCamera.TrySolve(scenicPlan, scenicProgress, scenicVisibility, camera.aspect, camera.nearClipPlane,
                continuousPlayer, moveCarrier, out Vector3 scenicPosition, out Quaternion scenicRotation, out float scenicFov))
                return Unready(scenicCamera.LastFailure);
            camera.transform.SetPositionAndRotation(scenicPosition, scenicRotation);
            camera.fieldOfView = scenicFov;
            if (skyCamera) skyCamera.fieldOfView = scenicFov;
            LastPictureUsable = scenicCamera.LastPictureUsable; LastFailure = "none"; hasPose = false;
            return true;
        }
        if (scenicActive) { scenicCamera.Reset(); scenicActive = proceduralActive = false; }
        if (settings.ProceduralPlayers.Value && input.HasValue)
        {
            proceduralActive = true;
            creatureActive = input.Value.Kind == CinematicShotKind.Creature;
            bool previouslyComplementary = complementaryActive;
            if (creatureActive) { complementary.Reset(); complementaryActive = false; }
            CinemachineShotRig actorCamera = creatureActive ? creatureCamera : shotCamera;
            actorCamera.PresentationConfirmed = presentationConfirmed && !previouslyComplementary;
            actorCamera.RenderingCamera = camera;
            actorCamera.NearClipPlane = camera.nearClipPlane;
            actorCamera.Aspect = camera.aspect;
            if (!actorCamera.TrySolve(input.Value, settings, dt, continuousPlayer, moveCarrier,
                    out Vector3 position, out Quaternion rotation, out float fov))
                return Unready(actorCamera.LastFailure);
            StoryContinuityReady = !creatureActive && actorCamera.LastPictureUsable;
            if (!creatureActive)
            {
                complementaryActive = complementary.TrySelect(shotCamera.CurrentPlayerScene, shotCamera.CurrentPlayerId ?? "", camera, settings, dt,
                    continuousPlayer, moveCarrier, editorialHold, presentationConfirmed, StoryContinuityReady, position,
                    out Vector3 alternatePosition, out Quaternion alternateRotation, out float alternateFov);
                if (complementaryActive)
                { position = alternatePosition; rotation = alternateRotation; fov = alternateFov; }
            }
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov;
            if (skyCamera) skyCamera.fieldOfView = fov;
            LastPictureUsable = ActorCamera.LastPictureUsable;
            LastFailure = "none";
            hasPose = false;
            return true;
        }
        if (proceduralActive) { complementary.Reset(); complementaryActive = false; shotCamera.Reset(); creatureCamera.Reset(); proceduralActive = creatureActive = false; }
        legacyMode = input.HasValue ? input.Value.Kind.ToString().ToLowerInvariant() + " framing" : "probe framing";
        float radians = azimuth * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(radians) * settings.Distance.Value,
            settings.Height.Value, Mathf.Cos(radians) * settings.Distance.Value);
        Vector3 desired = focus + (scenicOffset ?? offset);
        if (!TargetRoster.Finite(desired)) return Unready("Invalid camera destination");
        if (!PlayerShotReadiness.PositionReady(focus, continuousPlayer))
            return Unready(continuousPlayer ? "Shot focus geometry unavailable" : "Acquiring shot focus area");
        // Continuation validates the actual smoothed/clipped camera below. Its
        // unrealized desired offset can cross a tile edge or lie outside a small
        // room while the current picture remains fully supported.
        if (!continuousPlayer && !IdleShots.Ready(desired)) return Unready("Acquiring desired camera area");
        if (!hasPose || revision != selectionRevision)
        {
            smoothedPosition = desired;
            revision = selectionRevision;
            hasPose = true;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-settings.Smoothing.Value * Mathf.Max(0f, dt));
            smoothedPosition = Vector3.Lerp(smoothedPosition, desired, blend);
        }
        // Check the final smoothed ray, so smoothing cannot carry the camera through a wall.
        Vector3 ray = smoothedPosition - focus;
        Vector3 visiblePosition = smoothedPosition;
        if (ray.sqrMagnitude > 0.01f && Physics.SphereCast(focus, 0.3f, ray.normalized,
                out RaycastHit hit, ray.magnitude, obstructionMask.Value, QueryTriggerInteraction.Ignore))
        {
            // A scenic obstruction is a rejected composition, not a reason to
            // collapse a landscape camera into a branch beside its focus.
            if (scenicOffset.HasValue) return Unready("Scenic sightline obstructed");
            visiblePosition = focus + ray.normalized * Mathf.Max(0.1f, hit.distance - 0.1f);
        }
        if (!PlayerShotReadiness.PositionReady(visiblePosition, continuousPlayer))
            return Unready(continuousPlayer ? "Visible camera geometry unavailable" : "Acquiring visible camera area");
        camera.transform.SetPositionAndRotation(visiblePosition, Quaternion.LookRotation(focus - visiblePosition, Vector3.up));
        camera.fieldOfView = settings.FieldOfView.Value;
        if (skyCamera) skyCamera.fieldOfView = settings.FieldOfView.Value;
        LastPictureUsable = true; LastFailure = "none";
        return true;
    }

    internal void HideHud(bool hide)
    {
        GameObject? root = Hud.instance ? HudRoot?.GetValue(Hud.instance) as GameObject : null;
        if (!hide || hiddenHud != root) RestoreHud();
        if (hide && root)
        {
            if (hiddenHud != root) { hiddenHud = root; hudWasActive = root.activeSelf; }
            root.SetActive(false);
        }
    }

    internal double RequiredPlayerCameraMovement(TargetRoster.Target target, Vector3 point, BroadcastSettings settings)
    {
        if (settings.ProceduralPlayers.Value) return shotCamera.RequiredCarrierMovement(target, point, settings);
        if (!Player.m_localPlayer) return double.PositiveInfinity;
        Vector3 offset = point - Player.m_localPlayer.transform.position;
        return CameraReachability.RequiredDisplacement(offset.x, offset.y, offset.z,
            settings.Distance.Value, settings.Height.Value, settings.Azimuth.Value);
    }

    internal void ResetPose()
    { hasPose = false; complementary.Reset(); complementaryActive = StoryContinuityReady = false; shotCamera.Reset(); creatureCamera.Reset(); scenicCamera.Reset(); scenicActive = creatureActive = proceduralActive = false; LastPictureUsable = false; presentationNotice = "awaiting a ready shot"; }

    internal void Restore()
    {
        RestoreCamera();
        RestoreHud();
        hasPose = false;
        revision = -1;
    }

    private void RestoreCamera()
    {
        complementary.Dispose(); complementaryActive = StoryContinuityReady = false;
        shotCamera.Dispose(); creatureCamera.Dispose(); scenicCamera.Dispose(); scenicActive = creatureActive = proceduralActive = false; LastPictureUsable = false; presentationNotice = "inactive";
        if (camera)
        {
            camera.fieldOfView = savedFov;
            camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
        }
        camera = null;
        RestoreSky();
        hasPose = false;
    }

    private void RestoreSky()
    {
        if (skyCamera) skyCamera.fieldOfView = savedSkyFov;
        skyCamera = null;
    }

    private void RestoreHud()
    {
        if (hiddenHud) hiddenHud.SetActive(hudWasActive);
        hiddenHud = null;
    }
}
