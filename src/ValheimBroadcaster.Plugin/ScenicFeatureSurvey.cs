using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Incremental, read-only observations of the already loaded scene. These are
// geometric feature hints, not semantic map labels or newly generated terrain.
internal sealed class ScenicFeatureSurvey
{
    private const int Side = 7, CellCount = Side * Side, MaximumFeatures = 32, MaximumObjects = 96;
    private const float Spacing = 12f, ObjectRadius = 8f;
    private readonly Cell[] cells = new Cell[CellCount];
    private readonly Collider[] overlaps = new Collider[96], waterOverlaps = new Collider[16];
    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private readonly HashSet<int> seenObjects = new HashSet<int>(), seenTrees = new HashSet<int>();
    private readonly List<ObjectFeature> objects = new List<ObjectFeature>(MaximumObjects);
    private readonly List<ScenicFeature> features = new List<ScenicFeature>(MaximumFeatures);
    private ZNet? world;
    private Vector3 origin;
    private int nextCell, terrainMask, objectsMask, unavailable, truncated;
    private float waterPlane;
    private bool hasWaterPlane;
    internal bool Complete { get; private set; }
    internal IReadOnlyList<ScenicFeature> Features => features;
    internal string Diagnostics => $"Scenic survey: {nextCell}/{CellCount} terrain cells; {features.Count} features; " +
        $"{unavailable} unavailable cells; {truncated} incomplete object queries; loaded geometry only";

    private struct Cell
    {
        internal bool Valid, ObjectsComplete, Water;
        internal Vector3 Ground;
        internal int Trees, Solids;
    }
    private struct ObjectFeature
    {
        internal ScenicFeatureKind Kind;
        internal Bounds Bounds;
        internal ObjectFeature(ScenicFeatureKind kind, Bounds bounds) { Kind = kind; Bounds = bounds; }
    }

    internal void Begin(Vector3 surveyOrigin)
    {
        Reset();
        if (!TargetRoster.Finite(surveyOrigin) || !ZNet.instance || Character.InInterior(surveyOrigin))
        { Complete = true; return; }
        // A stable lattice makes identity independent of the carrier's small
        // camera-follow movements. Re-entering a region rediscovers the same cells.
        origin = new Vector3(Mathf.Round(surveyOrigin.x / Spacing) * Spacing, surveyOrigin.y,
            Mathf.Round(surveyOrigin.z / Spacing) * Spacing);
        world = ZNet.instance;
        terrainMask = LayerMask.GetMask("terrain");
        objectsMask = LayerMask.GetMask("Default", "static_solid", "piece");
        ZoneSystem? zones = ZoneSystem.instance;
        hasWaterPlane = zones && Finite(zones!.m_waterLevel);
        if (hasWaterPlane) waterPlane = zones!.m_waterLevel;
    }

    // Two cells bound each update to terrain rays and local overlap queries.
    // No scene-wide object enumeration, world generation, or additional render.
    internal bool Step()
    {
        if (Complete) return true;
        if (!world || world != ZNet.instance) { Reset(); Complete = true; return true; }
        for (int budget = 0; budget < 2 && nextCell < CellCount; budget++, nextCell++) Sample(nextCell);
        if (nextCell < CellCount) return false;
        BuildFeatures(); Complete = true;
        return true;
    }

    private void Sample(int index)
    {
        int x = index % Side - Side / 2, z = index / Side - Side / 2;
        Vector3 point = origin + new Vector3(x * Spacing, 0, z * Spacing);
        if (!IdleShots.Ready(point) || !PlayerShotReadiness.PositionReady(point, true))
        { unavailable++; return; }
        int hits = Physics.RaycastNonAlloc(point + Vector3.up * 100f, Vector3.down, groundHits,
            240f, terrainMask, QueryTriggerInteraction.Ignore);
        if (hits == 0 || hits >= groundHits.Length) { unavailable++; return; }
        float nearest = float.PositiveInfinity;
        Vector3 ground = default;
        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = groundHits[i];
            if (!hit.collider || !TargetRoster.Finite(hit.point) || !Finite(hit.distance) || hit.distance >= nearest) continue;
            nearest = hit.distance; ground = hit.point;
        }
        if (!Finite(nearest) || !IdleShots.Ready(ground)) { unavailable++; return; }
        Cell cell = new Cell { Valid = true, Ground = ground };
        int count = Physics.OverlapSphereNonAlloc(ground + Vector3.up * 3f, ObjectRadius, overlaps,
            objectsMask, QueryTriggerInteraction.Ignore);
        cell.ObjectsComplete = count < overlaps.Length;
        if (!cell.ObjectsComplete) truncated++;
        seenTrees.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            if (!StaticSceneObject(collider)) continue;
            TreeBase? tree = collider.GetComponentInParent<TreeBase>();
            TreeLog? log = collider.GetComponentInParent<TreeLog>();
            if (tree || log || Vegetation(collider.transform))
            {
                int identity = tree ? tree.GetInstanceID() : log ? log.GetInstanceID() : collider.transform.root.GetInstanceID();
                if (seenTrees.Add(identity)) cell.Trees++;
                continue;
            }
            cell.Solids++;
            Piece? piece = collider.GetComponentInParent<Piece>();
            bool rock = Named(collider.transform, "rock") || Named(collider.transform, "boulder");
            if (!piece && !rock || seenObjects.Count >= MaximumObjects || !seenObjects.Add(collider.GetInstanceID())) continue;
            Bounds bounds = collider.bounds;
            if (!TargetRoster.Finite(bounds.center) || !TargetRoster.Finite(bounds.size) ||
                bounds.size.sqrMagnitude < 4f || bounds.size.sqrMagnitude > 6400f) continue;
            AddObject(piece ? ScenicFeatureKind.Structure : ScenicFeatureKind.Rock, bounds);
        }
        // A terrain/water-plane crossing alone is insufficient: there must be an
        // active loaded WaterVolume at this sample. Merely reading bounds avoids
        // Floating's shared water cache and wave/world-generator calculations.
        if (hasWaterPlane && ground.y <= waterPlane + 2f)
        {
            Vector3 surface = new Vector3(ground.x, waterPlane, ground.z);
            int waterCount = Physics.OverlapSphereNonAlloc(surface, .15f, waterOverlaps, ~0, QueryTriggerInteraction.Collide);
            if (waterCount < waterOverlaps.Length)
                for (int i = 0; i < waterCount; i++)
                {
                    Collider collider = waterOverlaps[i];
                    WaterVolume? water = collider ? collider.GetComponentInParent<WaterVolume>() : null;
                    if (water && water.isActiveAndEnabled && water.m_waterSurface && water.m_waterSurface.enabled)
                    { cell.Water = true; break; }
                }
        }
        cells[index] = cell;
    }

    private void AddObject(ScenicFeatureKind kind, Bounds bounds)
    {
        // Adjacent building pieces describe a structure cluster, not dozens of
        // nominally different subjects. Clusters remain spatially bounded.
        for (int i = 0; i < objects.Count; i++)
        {
            ObjectFeature item = objects[i];
            if (item.Kind != kind || Vector3.Distance(item.Bounds.center, bounds.center) > (kind == ScenicFeatureKind.Structure ? 14f : 6f)) continue;
            Bounds merged = item.Bounds; merged.Encapsulate(bounds);
            if (merged.extents.magnitude > 24f) continue;
            item.Bounds = merged; objects[i] = item; return;
        }
        if (objects.Count < MaximumObjects) objects.Add(new ObjectFeature(kind, bounds));
    }

    private void BuildFeatures()
    {
        foreach (ObjectFeature item in objects)
        {
            Bounds bounds = item.Bounds;
            Vector3 center = bounds.center;
            if (Vector3.ProjectOnPlane(center - origin, Vector3.up).magnitude > 48f || !IdleShots.Ready(center)) continue;
            Vector3 direction = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
            Add(item.Kind, center, direction, Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 2f, 24f),
                Mathf.Clamp(bounds.size.y, 1f, 40f), item.Kind == ScenicFeatureKind.Structure ? .82f : .72f);
        }
        // Interior lattice cells have observations on both sides. Missing or
        // saturated evidence cannot certify a clearing, ridge or coastline.
        for (int z = 1; z < Side - 1; z++)
        for (int x = 1; x < Side - 1; x++)
        {
            int index = z * Side + x;
            Cell c = cells[index], left = cells[index - 1], right = cells[index + 1], back = cells[index - Side], front = cells[index + Side];
            if (!c.Valid || !left.Valid || !right.Valid || !back.Valid || !front.Valid) continue;
            if (hasWaterPlane)
            {
                Coast(c, right); Coast(c, front);
            }
            // Aim above the measured terrain, rather than at an invented object.
            Vector3 focus = c.Ground + Vector3.up * 1.5f;
            float xDrop = c.Ground.y - Mathf.Max(left.Ground.y, right.Ground.y);
            float zDrop = c.Ground.y - Mathf.Max(back.Ground.y, front.Ground.y);
            float xRelief = Mathf.Abs(left.Ground.y - right.Ground.y);
            float zRelief = Mathf.Abs(back.Ground.y - front.Ground.y);
            float relief = Mathf.Max(Mathf.Abs(c.Ground.y - left.Ground.y), Mathf.Abs(c.Ground.y - right.Ground.y),
                Mathf.Abs(c.Ground.y - back.Ground.y), Mathf.Abs(c.Ground.y - front.Ground.y));
            bool dry = !hasWaterPlane || c.Ground.y > waterPlane + .5f;
            if (!dry) continue;
            if (xDrop >= 2.5f || zDrop >= 2.5f)
            {
                Vector3 along = xDrop >= zDrop ? Vector3.forward : Vector3.right;
                Add(ScenicFeatureKind.Ridge, focus, along, 18f, Mathf.Clamp(relief, 3f, 30f),
                    Mathf.Lerp(.65f, .9f, Mathf.Clamp01(relief / 12f)));
            }
            bool complete = c.ObjectsComplete && left.ObjectsComplete && right.ObjectsComplete && back.ObjectsComplete && front.ObjectsComplete;
            if (complete && c.Trees == 0 && c.Solids == 0 && relief < 4f &&
                left.Trees + right.Trees + back.Trees + front.Trees >= 3)
                Add(ScenicFeatureKind.Clearing, focus, xRelief < zRelief ? Vector3.right : Vector3.forward, 10f, 8f, .65f);
            // A measured open terrain patch is a lower-confidence fallback. It
            // competes below actual feature evidence, rather than manufacturing
            // a named landmark from a random ground point.
            if (complete && c.Trees == 0 && c.Solids == 0)
                Add(ScenicFeatureKind.Landscape, focus, xRelief < zRelief ? Vector3.right : Vector3.forward,
                    18f, Mathf.Clamp(relief, 2f, 24f), .28f + Mathf.Clamp01(relief / 12f) * .2f);
        }
        features.Sort((a, b) => { int score = b.Confidence.CompareTo(a.Confidence); return score != 0 ? score : StringComparer.Ordinal.Compare(a.Id, b.Id); });
    }

    private void Coast(Cell a, Cell b)
    {
        bool aWet = a.Ground.y < waterPlane - .35f, bWet = b.Ground.y < waterPlane - .35f;
        if (aWet == bWet || !(aWet ? a.Water : b.Water)) return;
        Cell dry = aWet ? b : a;
        if (dry.Ground.y < waterPlane + .5f) return;
        float fraction = Mathf.Clamp01((waterPlane - a.Ground.y) / (b.Ground.y - a.Ground.y));
        Vector3 shore = Vector3.Lerp(a.Ground, b.Ground, fraction) + Vector3.up * 1.5f;
        Vector3 across = b.Ground - a.Ground; across.y = 0;
        if (across.sqrMagnitude < 1f) return;
        Add(ScenicFeatureKind.Coastline, shore, Vector3.Cross(Vector3.up, across).normalized, 18f,
            Mathf.Clamp(Mathf.Abs(b.Ground.y - a.Ground.y), 2f, 20f), .86f);
    }

    private void Add(ScenicFeatureKind kind, Vector3 center, Vector3 direction, float radius, float height, float confidence)
    {
        for (int i = 0; i < features.Count; i++)
        {
            ScenicFeature previous = features[i];
            if (previous.Kind == kind && Vector3.Distance(FromCore(previous.Center), center) < Mathf.Max(12f, radius)) return;
        }
        string id = kind.ToString().ToLowerInvariant() + ":" + Mathf.RoundToInt(center.x / 6f) + ":" + Mathf.RoundToInt(center.z / 6f);
        var feature = new ScenicFeature(id, kind, ToCore(center), ToCore(direction), radius, height, confidence);
        if (features.Count < MaximumFeatures) { features.Add(feature); return; }
        int worst = 0;
        for (int i = 1; i < features.Count; i++) if (features[i].Confidence < features[worst].Confidence) worst = i;
        if (confidence > features[worst].Confidence) features[worst] = feature;
    }

    private static bool StaticSceneObject(Collider collider) => collider && collider.enabled && collider.gameObject.activeInHierarchy &&
        !collider.isTrigger && !collider.attachedRigidbody && !collider.GetComponentInParent<Character>() &&
        !collider.GetComponentInParent<Ship>() && (collider.gameObject.hideFlags & HideFlags.HideAndDontSave) == 0;
    private static bool Vegetation(Transform transform) => Named(transform, "tree") || Named(transform, "bush") || Named(transform, "shrub");
    private static bool Named(Transform transform, string text)
    {
        for (Transform? item = transform; item; item = item.parent)
            if (item.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    internal void Reset()
    {
        Array.Clear(cells, 0, cells.Length); seenObjects.Clear(); seenTrees.Clear(); objects.Clear(); features.Clear();
        world = null; origin = default; nextCell = unavailable = truncated = 0; Complete = false; hasWaterPlane = false; waterPlane = 0;
    }
}
