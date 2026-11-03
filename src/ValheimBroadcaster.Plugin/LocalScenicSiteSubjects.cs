using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Read-only, bounded trader observations from the existing local survey.
/// Only an active Trader with a recognized exact name token and measured visible
/// renderers becomes a subject. A destination name alone never manufactures one.</summary>
internal sealed class LocalScenicSiteSubjects
{
    private const int MaximumTraders = 4;
    private readonly List<Trader> traders = new List<Trader>(MaximumTraders);
    private readonly List<Renderer> renderers = new List<Renderer>(32);
    private readonly Collider[] nearby = new Collider[96];
    private readonly HashSet<int> seen = new HashSet<int>();
    private static readonly Dictionary<string, ObservedNpc> active = new Dictionary<string, ObservedNpc>(StringComparer.Ordinal);
    private static readonly RaycastHit[] hits = new RaycastHit[24];
    private static ZNet? observedWorld;
    private int incompleteQueries;
    internal string Diagnostics => $"{traders.Count} observed traders, {incompleteQueries} partial NPC queries";
    private sealed class ObservedNpc
    {
        internal Trader Trader = null!;
        internal Bounds Bounds;
        internal Vector3 Position;
        internal float At;
    }

    internal void Reset()
    {
        traders.Clear(); renderers.Clear(); seen.Clear(); incompleteQueries = 0;
        if (observedWorld != ZNet.instance) { active.Clear(); observedWorld = ZNet.instance; }
    }

    internal void ObserveNearby(Vector3 point)
    {
        if (traders.Count >= MaximumTraders) return;
        // NPC detection is layer-inclusive and includes hover/interaction
        // triggers. It is separate from the static geometric survey so terrain,
        // actors and triggers cannot incorrectly invalidate a clearing.
        int count = Physics.OverlapSphereNonAlloc(point, 8f, nearby, ~0, QueryTriggerInteraction.Collide);
        if (count == nearby.Length) incompleteQueries++;
        for (int i = 0; i < count; i++) Observe(nearby[i]);
    }

    private void Observe(Collider collider)
    {
        if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || traders.Count >= MaximumTraders) return;
        Trader? trader = collider.GetComponentInParent<Trader>();
        if (!trader || !trader.isActiveAndEnabled || !ScenicSiteSequence.TryTrader(trader.m_name, out _, out _) ||
            !seen.Add(trader.GetInstanceID())) return;
        traders.Add(trader);
    }

    internal void Append(List<ScenicFeature> output, Vector3 origin, int maximum)
    {
        if (observedWorld != ZNet.instance) { active.Clear(); observedWorld = ZNet.instance; }
        foreach (Trader trader in traders)
        {
            if (!trader || !trader.isActiveAndEnabled || !ScenicSiteSequence.TryTrader(trader.m_name, out string label, out _) ||
                !TargetRoster.Finite(trader.transform.position) ||
                Vector3.Distance(origin, trader.transform.position) > 60f || !TryBody(trader, out Bounds body)) continue;
            string identity = trader.m_name;
            string id = "npc:" + identity.Substring(1) + ":" + Mathf.RoundToInt(trader.transform.position.x) + ":" + Mathf.RoundToInt(trader.transform.position.z);
            string parent = id + ":surroundings";
            Bounds context = body;
            int solids = 0;
            int count = Physics.OverlapSphereNonAlloc(body.center, 12f, nearby,
                LayerMask.GetMask("Default", "static_solid", "piece"), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = nearby[i];
                if (!c || !c.enabled || c.isTrigger || c.attachedRigidbody || !c.gameObject.activeInHierarchy ||
                    c.GetComponentInParent<Trader>() || c.GetComponentInParent<Character>() || c.GetComponentInParent<Ship>() ||
                    c.GetComponentInParent<Heightmap>() || ScenicVisibility.Vegetation(c.transform)) continue;
                Bounds b = c.bounds;
                if (!FoliageGeometryMath.Valid(b) || b.size.sqrMagnitude < .5f || b.extents.magnitude > 20f ||
                    Vector3.Distance(b.center, body.center) > 16f) continue;
                Bounds combined = context; combined.Encapsulate(b);
                if (combined.extents.magnitude > 25f) continue;
                context = combined; solids++;
            }
            // A trader alone can supply a contextual portrait, but cannot
            // establish a camp that has not been measured. Retry as more loads.
            if (solids < 2 || Mathf.Max(context.extents.x, context.extents.z) < 3f ||
                !ScenicSurface.TryGroundHeight(context.center, out float ground) ||
                !ScenicSurface.TryWaterHeight(context.center, out bool waterPresent, out float water)) continue;
            float bottom = Mathf.Max(context.min.y, ground + .25f);
            if (waterPresent) bottom = Mathf.Max(bottom, water + .25f);
            float top = context.max.y;
            if (top - bottom < .75f) continue;
            Vector3 center = context.center, size = context.size;
            center.y = (top + bottom) * .5f; size.y = top - bottom;
            context = new Bounds(center, size);
            Vector3 direction = trader.transform.forward; direction.y = 0;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            var site = new ScenicFeature(parent, ScenicFeatureKind.Structure, ToCore(context.center), ToCore(direction),
                Mathf.Clamp(Mathf.Max(context.extents.x, context.extents.z), 3f, 25f), Mathf.Clamp(context.size.y, 1f, 50f), .94f,
                scopeLabel: label + " surroundings", isPartial: true, subjectKind: ScenicSubjectKind.SiteContext, subjectIdentity: identity);
            var actor = new ScenicFeature(id, ScenicFeatureKind.Structure, ToCore(body.center), ToCore(direction),
                Mathf.Clamp(Mathf.Max(body.extents.x, body.extents.z), 1f, 6f), Mathf.Clamp(body.size.y, 1f, 12f), .96f,
                parentId: parent, scopeLabel: label, subjectKind: ScenicSubjectKind.Npc, subjectIdentity: identity);
            // Retain this pair together; unrelated high-density rock/piece
            // observations cannot leave an orphan detail without its context.
            while (output.Count > maximum - 2)
            {
                int discard = output.FindLastIndex(f => f.SubjectKind == ScenicSubjectKind.Environment);
                if (discard < 0) break;
                output.RemoveAt(discard);
            }
            if (output.Count > maximum - 2) continue;
            output.Add(site); output.Add(actor);
            if (active.Count >= 32 && !active.ContainsKey(id))
            {
                string? oldest = null; float at = float.PositiveInfinity;
                foreach (var pair in active) if (pair.Value.At < at) { oldest = pair.Key; at = pair.Value.At; }
                if (oldest != null) active.Remove(oldest);
            }
            active[id] = new ObservedNpc { Trader = trader, Bounds = body, Position = trader.transform.position, At = Time.unscaledTime };
        }
    }

    private bool TryBody(Trader trader, out Bounds body)
    {
        body = default; renderers.Clear(); trader.GetComponentsInChildren(false, renderers);
        if (renderers.Count > 64) return false;
        bool any = false;
        foreach (Renderer renderer in renderers)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
            Bounds b = renderer.bounds;
            if (!FoliageGeometryMath.Valid(b) || b.extents.magnitude > 8f || b.size.sqrMagnitude < .05f ||
                Vector3.Distance(b.center, trader.transform.position) > 8f) continue;
            if (!any) { body = b; any = true; } else body.Encapsulate(b);
        }
        return any && body.size.y >= .6f && body.extents.magnitude <= 8f;
    }

    internal static bool Validate(ScenicFeature feature, ScenicPose pose, ScenicVisibility visibility, float aspect, float nearClip, out string reason)
    {
        reason = "Observed scenic subject available";
        if (feature.SubjectKind == ScenicSubjectKind.Environment) return true;
        if (feature.SubjectKind == ScenicSubjectKind.SiteContext && !feature.Id.EndsWith(":surroundings", StringComparison.Ordinal))
        { reason = "Unknown scenic site identity"; return false; }
        string id = feature.SubjectKind == ScenicSubjectKind.SiteContext
            ? feature.Id.Substring(0, feature.Id.Length - ":surroundings".Length) : feature.Id;
        if (observedWorld != ZNet.instance || !active.TryGetValue(id, out ObservedNpc? observed) ||
            !observed.Trader || !observed.Trader.isActiveAndEnabled || observed.Trader.m_name != feature.SubjectIdentity ||
            Vector3.Distance(observed.Trader.transform.position, observed.Position) > .75f)
        { reason = "Observed trader disappeared or moved from the prepared site"; return false; }
        if (feature.SubjectKind == ScenicSubjectKind.SiteContext) return true;
        // Require independently clear upper-body samples. The scenic structure
        // extent exemption cannot make a wall around the NPC count as its face.
        Bounds body = observed.Bounds;
        Vector3 right = pose.Rotation * Vector3.right;
        int visible = 0;
        for (int i = 0; i < 3; i++)
        {
            Vector3 point = body.center + Vector3.up * body.extents.y * (i == 0 ? .5f : .15f) +
                right * (i == 1 ? -.2f : i == 2 ? .2f : 0) * Mathf.Min(body.size.x, body.size.z);
            if (!visibility.SubjectPointVisible(pose.Position, pose.Rotation, point, pose.FieldOfView, aspect, nearClip)) continue;
            Vector3 delta = point - pose.Position;
            int count = Physics.RaycastNonAlloc(pose.Position, delta.normalized, hits, Mathf.Max(0, delta.magnitude - .1f),
                LayerMask.GetMask("Default", "static_solid", "piece", "terrain"), QueryTriggerInteraction.Ignore);
            bool clear = count < hits.Length;
            for (int j = 0; j < count && clear; j++)
            {
                Collider c = hits[j].collider;
                if (c && c.enabled && !c.isTrigger && c.GetComponentInParent<Trader>() != observed.Trader) clear = false;
            }
            if (clear) visible++;
        }
        if (visible >= 2) return true;
        reason = "Trader upper body is outside the frame or obscured"; return false;
    }

    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
