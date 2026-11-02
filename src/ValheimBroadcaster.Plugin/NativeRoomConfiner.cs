using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Acquires a conservative convex camera cell from loaded structural geometry.
// This is intentionally stricter than the aesthetic LocalRoomSurvey. A roof
// hint alone cannot invent a bounding volume. It never changes scene readiness.
internal sealed class NativeRoomConfiner : IDisposable
{
    private const float Margin = .35f, SurveyPeriod = .5f;
    private readonly Transform owner;
    private readonly RoomConfinementPolicy policy = new RoomConfinementPolicy();
    private readonly List<CinemachineConfiner3D> extensions = new List<CinemachineConfiner3D>(18);
    private readonly List<Boundary> boundaries = new List<Boundary>(64);
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly Collider[] overlaps = new Collider[64];
    private Scene helperScene;
    private GameObject? helper;
    private BoxCollider? volume;
    private Quaternion helperRotation;
    private float sampledAt = -1, helperYaw, lastNow = -1;
    private long lastScene;
    private int mask;
    private bool disposed, helperFailure, prepared;
    private RoomConfinementVolume preparedVolume;
    internal bool Active => !disposed && policy.Active && volume && volume.enabled && helperScene.IsValid() && helperScene.isLoaded;
    internal string Diagnostics => $"Confiner3D: {(Active ? "verified interior cell" : policy.Reason)}" +
        (Active ? $"; size {volume!.size.x:F1} x {volume.size.y:F1} x {volume.size.z:F1}m; isolated physics" : "");

    private readonly struct Boundary
    {
        private readonly Collider collider;
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;
        internal Boundary(Collider value)
        { collider = value; position = value.transform.position; rotation = value.transform.rotation; scale = value.transform.lossyScale; }
        internal bool Matches(Collider other) => collider == other;
        internal bool Valid => collider && collider.enabled && collider.gameObject.activeInHierarchy &&
            (collider.transform.position - position).sqrMagnitude < .000001f &&
            Quaternion.Angle(rotation, collider.transform.rotation) < .01f &&
            (collider.transform.lossyScale - scale).sqrMagnitude < .000001f;
    }

    internal NativeRoomConfiner(Transform owner) { this.owner = owner; }

    internal CinemachineConfiner3D Bind(CinemachineCamera camera)
    {
        var extension = camera.gameObject.AddComponent<CinemachineConfiner3D>();
        extension.enabled = false;
        extensions.Add(extension);
        return extension;
    }

    internal void Update(CameraSceneFrame context, float now)
    {
        if (disposed) return;
        CameraSceneSubject? primary = null;
        foreach (var subject in context.Subjects) if (subject.IsPrimary) { primary = subject; break; }
        bool allowed = owner && primary != null && primary.Actor && !context.DoorwayTransition &&
            context.EnvironmentConfidence >= .6f &&
            (context.Environment == ShotEnvironment.Room || context.Environment == ShotEnvironment.Dungeon) &&
            context.Locomotion != ShotLocomotion.Boat && context.Locomotion != ShotLocomotion.Swim;
        if (!allowed || !Finite(now)) { Clear(); return; }
        Character actor = primary!.Actor!;
        Vector3 position = actor.transform.position;
        Vector3 eye = position + Vector3.up * Mathf.Clamp(actor.GetHeight() * .65f, .7f, 2f);
        if (!Coordinates(eye)) { Clear(); return; }
        if (lastNow >= 0 && (now < lastNow || context.SceneId != lastScene)) Clear();
        lastNow = now; lastScene = context.SceneId;
        policy.Tick(now, context.SceneId, Point(eye), true);
        foreach (var boundary in boundaries)
            if (!boundary.Valid) { policy.Clear("Measured boundary moved or unloaded"); sampledAt = -1; break; }
        if (sampledAt < 0 || now - sampledAt >= SurveyPeriod)
        {
            sampledAt = now;
            boundaries.Clear();
            mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            RoomConfinementVolume? measured = TrySurvey(eye, context.Interior, out var candidate) ? candidate : (RoomConfinementVolume?)null;
            policy.Observe(now, context.SceneId, Point(eye), measured);
        }
        if (!policy.Active) { DisableBindings(); return; }
        bool revisedCell = prepared && (!Same(preparedVolume.Center, policy.Volume.Center) ||
            !Same(preparedVolume.HalfSize, policy.Volume.HalfSize) || preparedVolume.Yaw != policy.Volume.Yaw);
        if (!PrepareVolume(policy.Volume))
        { policy.Clear("Isolated collider query unavailable; ordinary geometry checks retained"); DisableBindings(); return; }
        foreach (var extension in extensions)
        {
            if (!extension) continue;
            var camera = extension.ComponentOwner;
            if (camera)
            {
                Vector3 previous = camera.State.GetFinalPosition();
                // Native slowing uses its previous position. A smaller fresh cell
                // must not interpolate from old history now outside that cell.
                // Cameras still inside retain their ordinary damping history.
                if (!extension.enabled || revisedCell && (!Coordinates(previous) || !policy.Contains(Point(previous))))
                    camera.PreviousStateIsValid = false;
            }
            extension.BoundingVolume = volume;
            extension.SlowingDistance = Mathf.Min(.7f, Mathf.Min(volume!.size.x, volume.size.z) * .2f);
            extension.enabled = true;
        }
    }

    internal bool Contains(Vector3 position) => Coordinates(position) && (!Active || policy.Contains(Point(position)));
    internal bool ValidateSegment(Vector3 from, Vector3 to) => Coordinates(from) && Coordinates(to) &&
        (!Active || policy.ValidateSegment(Point(from), Point(to)));

    private bool TrySurvey(Vector3 eye, bool dungeon, out RoomConfinementVolume result)
    {
        result = default;
        if (mask == 0) return false;
        float best = 11;
        Vector3 normal = Vector3.zero;
        // Establish a stable local room axis, not the actor's facing direction.
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            if (!Cast(eye, direction, 10, dungeon, true, out var hit)) continue;
            if (Mathf.Abs(hit.normal.y) > .25f || hit.distance >= best) continue;
            best = hit.distance; normal = hit.normal;
        }
        if (normal.sqrMagnitude < .5f) return false;
        normal.y = 0; normal.Normalize();
        float yaw = Mathf.Atan2(-normal.z, normal.x) * Mathf.Rad2Deg;
        // Opposite/adjacent walls of a rectangular room describe the same axes.
        yaw = ((yaw % 90) + 90) % 90;
        if (yaw > 89.5f || yaw < .5f) yaw = 0;
        Quaternion rotation = Quaternion.Euler(0, yaw, 0);
        Vector3 right = rotation * Vector3.right, forward = rotation * Vector3.forward;
        if (!Distance(eye, -right, 10, dungeon, true, out float left) ||
            !Distance(eye, right, 10, dungeon, true, out float rightDistance) ||
            !Distance(eye, -forward, 10, dungeon, true, out float back) ||
            !Distance(eye, forward, 10, dungeon, true, out float front)) return false;
        left -= Margin; rightDistance -= Margin; back -= Margin; front -= Margin;
        if (Mathf.Min(left, rightDistance, back, front) < .4f) return false;
        float floor = float.NegativeInfinity, ceiling = float.PositiveInfinity;
        // Require support and ceiling across the proposed cell, not just overhead
        // at the actor. Sloped roofs and floors reduce the safe vertical envelope.
        for (int x = -1; x <= 1; x++)
        for (int z = -1; z <= 1; z++)
        {
            Vector3 p = eye + right * (x < 0 ? -left * .9f : x * rightDistance * .9f) +
                forward * (z < 0 ? -back * .9f : z * front * .9f);
            if (!Cast(p, Vector3.down, 4, dungeon, false, out var down) || down.normal.y < .55f ||
                !Cast(p, Vector3.up, 10, dungeon, true, out var up) || up.normal.y > -.25f) return false;
            floor = Mathf.Max(floor, down.point.y + Margin);
            ceiling = Mathf.Min(ceiling, up.point.y - Margin);
        }
        if (ceiling - floor < .8f || eye.y < floor || eye.y > ceiling) return false;
        Vector3 center = eye + right * ((rightDistance - left) * .5f) + forward * ((front - back) * .5f);
        center.y = (floor + ceiling) * .5f;
        Vector3 half = new Vector3((left + rightDistance) * .5f, (ceiling - floor) * .5f, (back + front) * .5f);
        if (!WallFace(center, half, rotation, Vector3.right, dungeon) ||
            !WallFace(center, half, rotation, Vector3.left, dungeon) ||
            !WallFace(center, half, rotation, Vector3.forward, dungeon) ||
            !WallFace(center, half, rotation, Vector3.back, dungeon)) return false;
        // Furniture or internal structure can make a whole-room box unsafe.
        // Try smaller cells around the actor, all within the verified enclosure.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            float factor = attempt == 0 ? 1 : attempt == 1 ? .7f : .45f;
            Vector3 cellCenter = eye + right * ((rightDistance - left) * .5f * factor) + forward * ((front - back) * .5f * factor);
            cellCenter.y = center.y;
            Vector3 cellHalf = new Vector3(half.x * factor, half.y, half.z * factor);
            float reachX = Mathf.Max(left, rightDistance) * factor, reachZ = Mathf.Max(back, front) * factor;
            // An enclosing cell with no camera-subject separation can invalidate
            // every native candidate. Leave such spaces to the compact fallback.
            if (reachX * reachX + reachZ * reachZ < 1f) continue;
            if (cellHalf.x < .4f || cellHalf.z < .4f || !FreeCell(cellCenter, cellHalf, rotation)) continue;
            if (!Coordinates(cellCenter) || cellHalf.x > 12 || cellHalf.y > 8 || cellHalf.z > 12) return false;
            result = new RoomConfinementVolume(Point(cellCenter), Point(cellHalf), yaw);
            return true;
        }
        return false;
    }

    private bool WallFace(Vector3 center, Vector3 half, Quaternion rotation, Vector3 localNormal, bool dungeon)
    {
        Vector3 direction = rotation * localNormal;
        bool xFace = Mathf.Abs(localNormal.x) > .5f;
        for (int across = -1; across <= 1; across++)
        for (int height = -1; height <= 1; height++)
        {
            Vector3 p = new Vector3(xFace ? localNormal.x * (half.x - .1f) : across * half.x * .9f,
                height * half.y * .9f, xFace ? across * half.z * .9f : localNormal.z * (half.z - .1f));
            if (!Cast(center + rotation * p, direction, Margin + .65f, dungeon, true, out var hit) ||
                Vector3.Dot(hit.normal, -direction) < .7f) return false;
        }
        return true;
    }

    private bool Distance(Vector3 start, Vector3 direction, float range, bool dungeon, bool structural, out float distance)
    {
        bool found = Cast(start, direction, range, dungeon, structural, out var hit);
        distance = hit.distance;
        return found && Vector3.Dot(hit.normal, -direction) > .65f;
    }

    private bool Cast(Vector3 start, Vector3 direction, float range, bool dungeon, bool structural, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(start, direction, hits, range, mask, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) return false;
        float distance = range + 1;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i]; Collider collider = hit.collider;
            if (!StaticGeometry(collider) || !TargetRoster.Finite(hit.point) || !TargetRoster.Finite(hit.normal) ||
                !Finite(hit.distance) || hit.distance < .001f) continue;
            // Doors are movable openings, never enduring boundary evidence.
            if (collider.GetComponentInParent<Door>()) continue;
            if (structural && !dungeon && !collider.GetComponentInParent<Piece>()) continue;
            if (hit.distance < distance) { nearest = hit; distance = hit.distance; found = true; }
        }
        if (found) Remember(nearest.collider);
        return found;
    }

    private bool FreeCell(Vector3 center, Vector3 half, Quaternion rotation)
    {
        int count = Physics.OverlapBoxNonAlloc(center, half, overlaps, rotation, mask, QueryTriggerInteraction.Ignore);
        if (count >= overlaps.Length) return false;
        for (int i = 0; i < count; i++)
            if (overlaps[i] && overlaps[i].enabled && !overlaps[i].GetComponentInParent<Character>()) return false;
        return true;
    }

    private static bool StaticGeometry(Collider collider) => collider && collider.enabled && collider.gameObject.activeInHierarchy &&
        !collider.GetComponentInParent<Character>() && !collider.GetComponentInParent<TreeBase>() &&
        !collider.GetComponentInParent<TreeLog>() && !collider.GetComponentInParent<Ship>() && !collider.attachedRigidbody;

    private void Remember(Collider collider)
    {
        foreach (var boundary in boundaries) if (boundary.Matches(collider)) return;
        boundaries.Add(new Boundary(collider));
    }

    private bool PrepareVolume(RoomConfinementVolume measured)
    {
        if (helperFailure) return false;
        try
        {
            if (prepared && volume && volume.enabled && helperScene.IsValid() && helperScene.isLoaded &&
                Same(preparedVolume.Center, measured.Center) && Same(preparedVolume.HalfSize, measured.HalfSize) &&
                preparedVolume.Yaw == measured.Yaw) return true;
            if (!helperScene.IsValid() || !helperScene.isLoaded)
            {
                helper = null; volume = null;
                helperScene = SceneManager.CreateScene("ValheimBroadcaster.CameraCell." + owner.GetInstanceID(),
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            }
            if (!helper || !volume || Mathf.Abs(Mathf.DeltaAngle(helperYaw, measured.Yaw)) > .01f)
            {
                DisableBindings();
                if (helper) UnityEngine.Object.Destroy(helper);
                helper = new GameObject("Broadcast camera cell (isolated)") { hideFlags = HideFlags.HideAndDontSave };
                helperYaw = measured.Yaw;
                helperRotation = Quaternion.Euler(0, helperYaw, 0);
                helper.transform.SetPositionAndRotation(Vector3.zero, helperRotation);
                SceneManager.MoveGameObjectToScene(helper, helperScene);
                volume = helper.AddComponent<BoxCollider>();
                volume.isTrigger = true;
            }
            // The transform remains immutable. Geometry properties avoid a global
            // Physics.SyncTransforms call and keep all queries outside world physics.
            Vector3 center = Vector(measured.Center), half = Vector(measured.HalfSize);
            volume!.center = Quaternion.Inverse(helperRotation) * center;
            volume.size = half * 2;
            volume.enabled = true;
            // Unity must actually honor this isolated collider before the camera
            // can use it. No local scene simulation is run to make it valid.
            if ((volume.ClosestPoint(center) - center).sqrMagnitude > .0001f) return false;
            foreach (Vector3 axis in Axes)
            {
                Vector3 edge = center + helperRotation * Vector3.Scale(half, axis);
                Vector3 outside = edge + helperRotation * axis * 2;
                if ((volume.ClosestPoint(outside) - edge).sqrMagnitude > .0004f) return false;
            }
            prepared = true; preparedVolume = measured;
            return true;
        }
        catch (Exception)
        {
            // Unsupported local-physics scene/query behavior must fall back to
            // ordinary validated cameras, never leave a world collider behind.
            helperFailure = true;
            if (volume) volume.enabled = false;
            return false;
        }
    }

    private static readonly Vector3[] Axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
    private void DisableBindings()
    {
        prepared = false;
        foreach (var extension in extensions)
        {
            if (!extension) continue;
            if (extension.enabled && extension.ComponentOwner) extension.ComponentOwner.PreviousStateIsValid = false;
            extension.enabled = false; extension.BoundingVolume = null;
        }
        if (volume) volume.enabled = false;
    }

    internal void Clear()
    {
        policy.Reset(); boundaries.Clear(); sampledAt = lastNow = -1; lastScene = 0;
        DisableBindings();
    }
    public void Dispose()
    {
        if (disposed) return;
        Clear(); disposed = true;
        if (helper) UnityEngine.Object.Destroy(helper);
        helper = null; volume = null; extensions.Clear();
        // Unity can already be unloading additive scenes during world teardown.
        try { if (helperScene.IsValid() && helperScene.isLoaded) SceneManager.UnloadSceneAsync(helperScene); }
        catch (ArgumentException) { }
        helperScene = default;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
    private static bool Same(AutonomyPosition a, AutonomyPosition b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    private static bool Coordinates(Vector3 p) => TargetRoster.Finite(p) && Mathf.Abs(p.x) <= 20000 && Mathf.Abs(p.y) <= 20000 && Mathf.Abs(p.z) <= 20000;
    private static AutonomyPosition Point(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    private static Vector3 Vector(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
