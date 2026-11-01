using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

internal sealed class CameraRig
{
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
    private bool Unready(string reason) { LastFailure = reason; return false; }

    internal bool Apply(GameCamera gameCamera, Vector3 focus, float azimuth, BroadcastSettings settings, long selectionRevision, float dt, Vector3? scenicOffset = null)
    {
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
        float radians = azimuth * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(radians) * settings.Distance.Value,
            settings.Height.Value, Mathf.Cos(radians) * settings.Distance.Value);
        Vector3 desired = focus + (scenicOffset ?? offset);
        if (!IdleShots.Ready(focus)) return Unready("Shot focus area loading");
        if (!IdleShots.Ready(desired)) return Unready("Desired camera area loading");
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
        if (!IdleShots.Ready(visiblePosition)) return Unready("Smoothed camera area loading");
        camera.transform.SetPositionAndRotation(visiblePosition, Quaternion.LookRotation(focus - visiblePosition, Vector3.up));
        camera.fieldOfView = settings.FieldOfView.Value;
        if (skyCamera) skyCamera.fieldOfView = settings.FieldOfView.Value;
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

    internal void ResetPose() { hasPose = false; }

    internal void Restore()
    {
        RestoreCamera();
        RestoreHud();
        hasPose = false;
        revision = -1;
    }

    private void RestoreCamera()
    {
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
