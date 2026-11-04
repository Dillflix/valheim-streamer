using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Shared, incremental read-only place observations from loaded pieces.
/// No scene enumeration, generation, ownership requests or additional physics
/// objects. A partial loaded settlement is explicitly reported as partial.</summary>
internal sealed class PlaceSurvey
{
    internal static readonly PlaceSurvey Shared = new PlaceSurvey();
    private const float SurveyRadius = 112f;
    private const int PerFrame = 128, MaximumInspected = 8192;
    private static readonly FieldInfo? AllPieces = typeof(Piece).GetField("s_allPieces", BindingFlags.Static | BindingFlags.NonPublic);
    private readonly PlaceGeometryChanges geometryChanges = new PlaceGeometryChanges();
    private readonly PlaceHierarchy hierarchy = new PlaceHierarchy();
    private readonly PlaceHierarchy staging = new PlaceHierarchy();
    private readonly List<PlacePieceObservation> observed = new List<PlacePieceObservation>(PlaceHierarchy.MaximumPieces);
    private readonly List<float> distances = new List<float>(PlaceHierarchy.MaximumPieces);
    private readonly List<Piece> retained = new List<Piece>(PlaceHierarchy.MaximumPieces);
    private readonly List<Piece> selected = new List<Piece>(PlaceHierarchy.MaximumPieces);
    private readonly List<PlaceSpaceObservation> spaces = new List<PlaceSpaceObservation>(PlaceHierarchy.MaximumSpaces);
    private readonly List<Collider> colliders = new List<Collider>(16);
    private readonly HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<ScenicFeature> features = new List<ScenicFeature>();
    private readonly List<PlaceNode> probeTargets = new List<PlaceNode>(PlaceHierarchy.MaximumBuildings);
    private readonly RaycastHit[] rays = new RaycastHit[24];
    private List<Piece>? source;
    private ZNet? world;
    private ZNetScene? scene;
    private long worldId;
    private float lastNow = -1;
    private Vector3 origin;
    private float nextSurvey, begunAt;
    private int index, initialCount, processed, frame = -1, mask, probeIndex, seedIndex, scanStart, nextStart, scanTarget;
    private bool collecting, probing, complete;
    internal PlaceSnapshot Snapshot => hierarchy.Snapshot;
    internal string Diagnostics => $"Places: {Snapshot.Nodes.Count} nodes; revision {Snapshot.Revision}; " +
        $"{(Snapshot.CoverageComplete ? "locally surveyed" : "partial coverage")}; scan {processed}/{initialCount}; " +
        $"{(collecting || probing ? "updating" : "ready")}";

    internal void Update(Vector3 center, float now)
    {
        if (!TargetRoster.Finite(center) || !Finite(now) || now < 0) return;
        if (!Synchronize(now) || frame == Time.frameCount) return;
        frame = Time.frameCount;
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease lease)) return;
        using (lease)
        {
        if ((collecting || probing) && (now < begunAt || now - begunAt > 5 || Vector3.Distance(center, origin) > 40))
        { collecting = probing = false; nextSurvey = 0; }
        if (!collecting && !probing)
        {
            if (now < nextSurvey && Vector3.Distance(center, origin) < 24) return;
            Begin(center, now);
        }
        if (collecting)
        {
            if (source == null) { Publish(now, false); return; }
            if (source.Count != initialCount) complete = false;
            int budget = PerFrame;
            while ((seedIndex < retained.Count || index < scanTarget && source.Count > 0) && budget-- > 0 && CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Observation))
            {
                // Revalidate the previous nearest observations before rotating
                // through the bounded loaded-list window. Huge lists cannot
                // permanently starve later entries, or evict a found nearby base.
                Piece piece = seedIndex < retained.Count ? retained[seedIndex++] : source[(scanStart + index++) % source.Count];
                processed++;
                if (!piece || !piece.isActiveAndEnabled || !TargetRoster.Finite(piece.transform.position) ||
                    (piece.transform.position - origin).sqrMagnitude > SurveyRadius * SurveyRadius || piece.GetComponentInParent<Ship>()) continue;
                Observe(piece);
            }
            if (seedIndex < retained.Count || index < scanTarget && source.Count > 0) return;
            if (initialCount > MaximumInspected) complete = false;
            nextStart = source.Count > 0 ? (scanStart + index) % source.Count : 0;
            collecting = false;
            // A component index can cover only the already loaded scene. Check
            // the survey perimeter as well; partial world delivery stays partial.
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Vector3 edge = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * SurveyRadius;
                if (!IdleShots.Ready(edge) || !PlayerShotReadiness.PositionReady(edge, true)) complete = false;
            }
            staging.Update(observed, Array.Empty<PlaceSpaceObservation>(), ToCore(origin), SurveyRadius, complete, now);
            probeTargets.Clear();
            foreach (PlaceNode node in staging.Snapshot.Nodes) if (node.Kind == PlaceKind.Building) probeTargets.Add(node);
            probeTargets.Sort((a, b) => {
                float da = (FromCore(a.Bounds.Center) - origin).sqrMagnitude, db = (FromCore(b.Bounds.Center) - origin).sqrMagnitude;
                int distance = da.CompareTo(db); return distance != 0 ? distance : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            probing = true; probeIndex = 0;
        }
        if (probing)
        {
            // At most one local structural-space probe per frame, at most16
            // buildings per pass. Additional rooms remain unknown, not absent.
            if (probeIndex < Math.Min(16, probeTargets.Count))
            { ProbeSpace(probeTargets[probeIndex++]); return; }
            Publish(now, complete);
        }
        }
    }

    private void Begin(Vector3 center, float now)
    {
        origin = center; begunAt = now; index = processed = probeIndex = seedIndex = 0;
        retained.Clear(); retained.AddRange(selected); selected.Clear();
        observed.Clear(); distances.Clear(); identities.Clear(); spaces.Clear(); staging.Reset();
        source = AllPieces?.GetValue(null) as List<Piece>;
        initialCount = source?.Count ?? 0; complete = source != null; collecting = true; probing = false;
        scanStart = initialCount > 0 ? nextStart % initialCount : 0; scanTarget = Math.Min(initialCount, MaximumInspected);
        mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
    }

    private void Observe(Piece piece)
    {
        ZNetView? view = piece.GetComponent<ZNetView>();
        ZDO? zdo = view ? view.GetZDO() : null;
        string id = zdo != null && !zdo.m_uid.IsNone() ? zdo.m_uid.ToString() : "local:" + piece.GetInstanceID();
        if (!identities.Add(id)) return;
        colliders.Clear(); piece.GetComponentsInChildren(false, colliders);
        bool hasBounds = false; Bounds bounds = default;
        int used = 0;
        foreach (Collider collider in colliders)
        {
            if (!collider || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy ||
                collider.GetComponentInParent<Character>() || collider.GetComponentInParent<Piece>() != piece) continue;
            if (++used > 32) { complete = false; break; }
            Bounds value = collider.bounds;
            if (!FoliageGeometryMath.Valid(value)) { complete = false; continue; }
            if (!hasBounds) { bounds = value; hasBounds = true; } else bounds.Encapsulate(value);
        }
        if (!hasBounds || bounds.size.sqrMagnitude < .25f) return;
        if (bounds.size.x > 80 || bounds.size.z > 80 || bounds.size.y > 100)
        { complete = false; return; }
        float distance = bounds.SqrDistance(origin);
        var valuePiece = new PlacePieceObservation(id, ToCore(bounds), piece.GetCreator() != 0, piece.GetComponent<Door>() != null);
        if (observed.Count < PlaceHierarchy.MaximumPieces) { observed.Add(valuePiece); distances.Add(distance); selected.Add(piece); return; }
        complete = false;
        int farthest = 0;
        for (int i = 1; i < distances.Count; i++)
            if (distances[i] > distances[farthest] || distances[i] == distances[farthest] && StringComparer.Ordinal.Compare(observed[i].Id, observed[farthest].Id) > 0) farthest = i;
        if (distance < distances[farthest] || distance == distances[farthest] && StringComparer.Ordinal.Compare(id, observed[farthest].Id) < 0)
        { observed[farthest] = valuePiece; distances[farthest] = distance; selected[farthest] = piece; }
    }

    private void ProbeSpace(PlaceNode building)
    {
        if (spaces.Count >= PlaceHierarchy.MaximumSpaces || building.Bounds.Height < 2 || building.Bounds.Width < 3 || building.Bounds.Depth < 3) return;
        Vector3 center = FromCore(building.Bounds.Center);
        if (!IdleShots.Ready(center)) return;
        // Floor must be directly measured. Starting from the exposed extent's
        // centre handles raised floors without assuming terrain is the floor.
        Vector3 floorOrigin = center + Vector3.up * Mathf.Min(2, building.Bounds.Height * .3f);
        if (!Cast(floorOrigin, Vector3.down, Mathf.Min(12, building.Bounds.Height + 2), out RaycastHit floor, out bool floorKnown) || !floorKnown) return;
        center = floor.point + Vector3.up * 1.4f;
        // A high roof is not evidence of open sky. Inspect through the
        // observed vertical extent before classifying an open courtyard.
        float roofDistance = Mathf.Clamp(building.Bounds.Maximum.Y - center.y + 2, 8, 64);
        bool roof = Cast(center, Vector3.up, roofDistance, out RaycastHit ceiling, out bool roofKnown);
        if (!roof && building.Bounds.Maximum.Y - center.y >= roofDistance) return;
        if (!roofKnown) return;
        var limits = new float[4]; int walls = 0; bool known = true;
        Vector3[] directions = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
        for (int i = 0; i < 4; i++)
        {
            bool hit = Cast(center, directions[i], 8, out RaycastHit wall, out bool completeRay);
            if (!completeRay) known = false;
            bool structural = hit && wall.collider.GetComponentInParent<Piece>();
            limits[i] = structural ? wall.distance : 3;
            if (structural) walls++;
        }
        if (!known || (roof && !ceiling.collider.GetComponentInParent<Piece>())) return;
        PlaceKind kind;
        if (roof && walls == 4 && ceiling.distance >= .6f) kind = PlaceKind.Room;
        else if (!roof && walls >= 3) kind = PlaceKind.Courtyard;
        else return;
        // Local measured extent, not a claimed full floorplan or route volume.
        float top = roof ? ceiling.point.y : center.y + 2;
        Bounds bounds = new Bounds(); bounds.SetMinMax(new Vector3(center.x - limits[0], floor.point.y, center.z - limits[2]),
            new Vector3(center.x + limits[1], top, center.z + limits[3]));
        if (!FoliageGeometryMath.Valid(bounds)) return;
        string id = Mathf.RoundToInt(center.x / 4) + ":" + Mathf.RoundToInt(center.y / 3) + ":" + Mathf.RoundToInt(center.z / 4);
        spaces.Add(new PlaceSpaceObservation(id, kind, ToCore(bounds), .75f));
    }

    private bool Cast(Vector3 point, Vector3 direction, float distance, out RaycastHit nearest, out bool known)
    {
        nearest = default; known = true;
        int count = Physics.RaycastNonAlloc(point, direction, rays, distance, mask, QueryTriggerInteraction.Ignore);
        if (count >= rays.Length) { known = false; return false; }
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = rays[i].collider;
            if (!collider || !collider.enabled || collider.GetComponentInParent<Character>() || collider.GetComponentInParent<Ship>() ||
                !TargetRoster.Finite(rays[i].point) || !Finite(rays[i].distance)) continue;
            if (rays[i].distance < best) { best = rays[i].distance; nearest = rays[i]; }
        }
        return best < float.PositiveInfinity;
    }
    private void Publish(float now, bool localComplete)
    {
        bool geometryChanged = geometryChanges.Update(observed, ToCore(origin), SurveyRadius, localComplete);
        hierarchy.Update(observed, spaces, ToCore(origin), SurveyRadius, localComplete, now);
        if (geometryChanged)
            CameraSpatialScene.Shared.Invalidate();
        collecting = probing = false; nextSurvey = now + 2f;
    }

    internal PlaceNode? FindContext(Vector3 position, float now)
    {
        if (!TargetRoster.Finite(position) || !Finite(now) || now < 0 || !Synchronize(now) || !Snapshot.Fresh(now)) return null;
        return Snapshot.FindContext(ToCore(position));
    }

    internal IReadOnlyList<ScenicFeature> ScenicFeatures(Vector3 center, float radius, string? preferredId = null)
    {
        if (!TargetRoster.Finite(center) || !Synchronize(Time.unscaledTime) || !Snapshot.Fresh(Time.unscaledTime) ||
            !Finite(radius) || radius <= 0) { features.Clear(); return features; }
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease lease)) return features;
        using (lease)
        {
        features.Clear();
        foreach (PlaceNode node in PlaceScenicSelection.Select(Snapshot, ToCore(center), radius, preferredId))
        {
            if (features.Count >= PlaceScenicSelection.MaximumFeatures) break;
            PlaceBounds bounds = node.Bounds;
            // Features retain scale; a sprawling settlement is not squeezed
            // into a24m radius. Reachability and visibility belong to planning.
            var direction = bounds.Width >= bounds.Depth ? new AutonomyPosition(0, 0, 1) : new AutonomyPosition(1, 0, 0);
            Vector3 focus = FromCore(bounds.Center);
            if (!ScenicSurface.TryGroundHeight(focus, out float ground) ||
                !ScenicSurface.TryWaterHeight(focus, out bool water, out float waterHeight)) continue;
            float lower = Mathf.Max(bounds.Minimum.Y, ground + .25f);
            if (water) lower = Mathf.Max(lower, waterHeight + .25f);
            if (bounds.Maximum.Y <= lower || bounds.Width <= 0 || bounds.Depth <= 0 ||
                bounds.Maximum.Y - lower < .75f && node.Kind != PlaceKind.Courtyard) continue;
            focus.y = node.Kind == PlaceKind.Courtyard ? ground + 1.5f : (lower + bounds.Maximum.Y) * .5f;
            string? parent = node.ParentId;
            PlaceNode? ancestor = Snapshot.Find(parent);
            while (ancestor != null &&
                ((ancestor.Kind == PlaceKind.District || ancestor.Kind == PlaceKind.Settlement) && ancestor.Children.Count <= 1 ||
                 ancestor.Bounds.Radius > 120 || ancestor.Bounds.Height > 120 ||
                 Vector3.Distance(FromCore(ancestor.Bounds.Center), center) > radius))
            { parent = ancestor.ParentId; ancestor = Snapshot.Find(parent); }
            features.Add(new ScenicFeature(node.Id, node.Kind == PlaceKind.Courtyard ? ScenicFeatureKind.Clearing : ScenicFeatureKind.Structure,
                ToCore(focus), direction, Mathf.Max(1, bounds.Radius), Mathf.Max(1, bounds.Maximum.Y - lower), node.Confidence,
                parentId: parent, scopeLabel: node.Kind.ToString(), isPartial: node.IsPartial,
                measuredBounds: new PlaceBounds(new AutonomyPosition(bounds.Minimum.X, lower, bounds.Minimum.Z), bounds.Maximum),
                memberBounds: RepresentativeMembers(node)));
        }
        return features;
        }
    }

    private IReadOnlyList<PlaceBounds> RepresentativeMembers(PlaceNode parent)
    {
        var available = new List<PlaceBounds>();
        if (parent.Kind != PlaceKind.District && parent.Kind != PlaceKind.Settlement) return available;
        foreach (string id in parent.Children)
        {
            PlaceNode? child = Snapshot.Find(id);
            if (child == null) continue;
            if (child.Kind == PlaceKind.Building) available.Add(child.Bounds);
            else if (child.Kind == PlaceKind.District)
                foreach (string buildingId in child.Children)
                {
                    PlaceNode? building = Snapshot.Find(buildingId);
                    if (building != null && building.Kind == PlaceKind.Building) available.Add(building.Bounds);
                }
        }
        // Farthest-point sampling retains the actual near and far building
        // envelopes across a sprawling site, rather than its empty parent AABB.
        var result = new List<PlaceBounds>(8);
        while (result.Count < 8 && available.Count > 0)
        {
            int chosen = 0; double best = double.NegativeInfinity;
            for (int i = 0; i < available.Count; i++)
            {
                double distance = result.Count == 0 ? -(FromCore(available[i].Center) - origin).sqrMagnitude : double.PositiveInfinity;
                foreach (PlaceBounds selectedBound in result)
                    distance = Math.Min(distance, (FromCore(available[i].Center) - FromCore(selectedBound.Center)).sqrMagnitude);
                if (distance > best) { best = distance; chosen = i; }
            }
            result.Add(available[chosen]); available.RemoveAt(chosen);
        }
        return result;
    }

    private bool Synchronize(float now)
    {
        ZNet currentWorld = ZNet.instance;
        ZNetScene currentScene = ZNetScene.instance;
        long currentId = currentWorld && currentScene ? currentWorld.GetWorldUID() : 0;
        // Unity's overloaded null/equality must not hide destroyed instances,
        // and a reused ZNet instance may represent a different world session.
        if (!ReferenceEquals(world, currentWorld) || !ReferenceEquals(scene, currentScene) ||
            worldId != currentId || now < lastNow)
        { Reset(); world = currentWorld; scene = currentScene; worldId = currentId; }
        lastNow = now;
        return world && scene;
    }

    internal void Reset()
    {
        geometryChanges.Reset(); hierarchy.Reset(); staging.Reset(); source = null; world = null; scene = null; worldId = 0; lastNow = -1; observed.Clear(); distances.Clear(); spaces.Clear(); identities.Clear(); features.Clear(); colliders.Clear(); retained.Clear(); selected.Clear(); probeTargets.Clear();
        origin = default; nextSurvey = begunAt = 0; index = initialCount = processed = probeIndex = seedIndex = scanStart = nextStart = scanTarget = 0; frame = -1; collecting = probing = complete = false;
    }
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    private static AutonomyPosition ToCore(Vector3 p) => new AutonomyPosition(p.x, p.y, p.z);
    private static PlaceBounds ToCore(Bounds b) => new PlaceBounds(ToCore(b.min), ToCore(b.max));
    private static Vector3 FromCore(AutonomyPosition p) => new Vector3(p.X, p.Y, p.Z);
}
