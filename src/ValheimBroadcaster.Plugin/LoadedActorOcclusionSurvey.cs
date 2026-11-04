using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Uses the game's existing loaded-character registry. No global renderer scan,
// added collider, ownership request or simulation activation is involved. A
// character's body collider is a silhouette approximation; wings, weapons and
// tails do not turn its entire renderer AABB into an opaque wall.
internal sealed class LoadedActorOcclusionSurvey
{
    internal static readonly LoadedActorOcclusionSurvey Shared = new LoadedActorOcclusionSurvey();
    private const int Capacity = 128;
    private const float Radius = 100f;
    private readonly List<Character> shortlist = new List<Character>(Capacity);
    private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>(Capacity);
    private readonly List<int> stale = new List<int>(Capacity);
    private readonly ActorBodyCapsule[] bodies = new ActorBodyCapsule[Capacity];
    private int count, frame = -1, membershipFrame = -1, membershipIndex;
    private bool collecting;
    private float nextSurvey, lastNow = -1;
    private Vector3 origin;
    private ZNet? world;
    internal bool Truncated { get; private set; }

    private sealed class Entry
    {
        internal readonly Character Actor;
        internal Collider? Body;
        internal readonly List<Renderer> Renderers = new List<Renderer>(16);
        internal float NextRenderers, NextBody;
        internal bool Retained, RenderersKnown;
        internal Entry(Character actor)
        {
            Actor = actor;
            // The root collision body is bounded and stable. Child hitboxes can
            // include attack volumes and animated limbs, so they are excluded.
            Body = (Collider?)actor.GetComponent<CapsuleCollider>() ?? actor.GetComponent<SphereCollider>();
        }
    }

    internal void Refresh(Vector3 focus)
    {
        float now = Time.unscaledTime;
        focus = Player.m_localPlayer ? Player.m_localPlayer.transform.position : focus;
        if (!TargetRoster.Finite(focus)) return;
        if (world != ZNet.instance || now < lastNow || lastNow >= 0 && now - lastNow > 1f) Reset();
        world = ZNet.instance; lastNow = now;
        bool update = membershipFrame < 0 || now >= nextSurvey || (focus - origin).sqrMagnitude > 24f * 24f;
        if ((update || collecting) && membershipFrame != Time.frameCount &&
            CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease membershipLease))
        using (membershipLease)
        {
            membershipFrame = Time.frameCount;
            if (!collecting)
            { collecting = true; membershipIndex = 0; origin = focus; shortlist.Clear(); Truncated = false; }
            List<Character> registry = Character.GetAllCharacters();
            int steps = 0;
            while (membershipIndex < registry.Count && steps++ < 128 &&
                CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Observation))
            {
                Character actor = registry[membershipIndex++];
                if (!Live(actor) || actor == Player.m_localPlayer) continue;
                Vector3 position = actor.transform.position;
                if (!TargetRoster.Finite(position)) continue;
                float distance = (position - origin).sqrMagnitude;
                if (distance > Radius * Radius) continue;
                int at = 0;
                while (at < shortlist.Count && shortlist[at] &&
                    (shortlist[at].transform.position - origin).sqrMagnitude <= distance) at++;
                if (at >= Capacity) { Truncated = true; continue; }
                if (shortlist.Contains(actor)) continue;
                shortlist.Insert(at, actor);
                if (shortlist.Count > Capacity) { shortlist.RemoveAt(Capacity); Truncated = true; }
            }
            if (membershipIndex >= registry.Count)
            {
                collecting = false; nextSurvey = now + .25f;
                foreach (Entry entry in entries.Values) entry.Retained = false;
                foreach (Character actor in shortlist)
                {
                    if (!Live(actor)) continue;
                    int id = actor.GetInstanceID();
                    if (!entries.TryGetValue(id, out Entry? entry)) entries.Add(id, entry = new Entry(actor));
                    entry.Retained = true;
                }
                stale.Clear();
                foreach (KeyValuePair<int, Entry> pair in entries) if (!pair.Value.Retained) stale.Add(pair.Key);
                foreach (int id in stale) entries.Remove(id);
                frame = -1;
            }
        }
        if (frame == Time.frameCount) return;
        frame = Time.frameCount; count = 0;
        foreach (Entry entry in entries.Values)
        {
            if (!Live(entry.Actor) || entry.Actor == Player.m_localPlayer) continue;
            if (!entry.Body && now >= entry.NextBody)
            {
                entry.NextBody = now + 1f;
                entry.Body = (Collider?)entry.Actor.GetComponent<CapsuleCollider>() ?? entry.Actor.GetComponent<SphereCollider>();
            }
            if (!entry.Body || !entry.Body.enabled || !entry.Body.gameObject.activeInHierarchy || entry.Body.isTrigger) continue;
            if (now >= entry.NextRenderers && CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease renderLease))
            using (renderLease)
            {
                entry.NextRenderers = now + 1f;
                entry.Renderers.Clear();
                entry.Actor.GetComponentsInChildren<Renderer>(true, entry.Renderers);
                // Equipment/particles can be numerous; retain only actual mesh
                // renderers. Inspection remains bounded even for modded actors.
                for (int i = entry.Renderers.Count - 1; i >= 0; i--)
                    if (!(entry.Renderers[i] is SkinnedMeshRenderer) && !(entry.Renderers[i] is MeshRenderer))
                        entry.Renderers.RemoveAt(i);
                if (entry.Renderers.Count > 32) entry.Renderers.RemoveRange(32, entry.Renderers.Count - 32);
                entry.RenderersKnown = true;
            }
            Bounds bounds = entry.Body.bounds;
            // Deferred initial renderer discovery is unknown, not invisible.
            // Keep the measured capsule conservative until visual metadata arrives.
            bool visible = !entry.RenderersKnown;
            foreach (Renderer renderer in entry.Renderers)
            {
                if (renderer && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy &&
                    renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly && renderer.bounds.Intersects(bounds))
                { visible = true; break; }
            }
            if (visible && TryBody(entry, out ActorBodyCapsule body)) bodies[count++] = body;
        }
    }

    internal bool Occludes(Vector3 camera, Vector3 target, int subjectId)
    {
        if (subjectId == 0 || !Supported(camera) || !Supported(target)) return false;
        var from = Position(camera); var to = Position(target);
        ActorVisibilitySegment sample = ActorOcclusionGeometry.Prepare(from, to, subjectId);
        for (int i = 0; i < count; i++)
            if (ActorOcclusionGeometry.Occludes(sample, bodies[i])) return true;
        return false;
    }

    private static bool TryBody(Entry entry, out ActorBodyCapsule body)
    {
        body = default;
        Transform transform = entry.Body!.transform;
        Vector3 scale = transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 center, direction;
        float radius, halfAxis;
        if (entry.Body is CapsuleCollider capsule)
        {
            Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 2 ? Vector3.forward : Vector3.up;
            float lengthScale = capsule.direction == 0 ? scale.x : capsule.direction == 2 ? scale.z : scale.y;
            float radiusScale = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z) :
                capsule.direction == 2 ? Mathf.Max(scale.x, scale.y) : Mathf.Max(scale.x, scale.z);
            center = transform.TransformPoint(capsule.center);
            direction = transform.TransformDirection(axis).normalized;
            radius = capsule.radius * radiusScale;
            halfAxis = Mathf.Max(0, capsule.height * lengthScale * .5f - radius);
        }
        else if (entry.Body is SphereCollider sphere)
        {
            center = transform.TransformPoint(sphere.center); direction = Vector3.up;
            radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)); halfAxis = 0;
        }
        else return false;
        if (!TargetRoster.Finite(center) || !TargetRoster.Finite(direction) || !Finite(radius) || !Finite(halfAxis) ||
            radius <= .02f || radius > 32 || halfAxis > 32) return false;
        // Physics bodies are often slightly padded. Use their inner body core
        // for visual evidence instead of claiming their full hitbox is opaque.
        Vector3 a = center - direction * halfAxis, b = center + direction * halfAxis;
        if (!Supported(a) || !Supported(b)) return false;
        body = new ActorBodyCapsule(entry.Actor.GetInstanceID(), Position(a), Position(b), radius * .9f);
        return true;
    }

    internal void Reset()
    {
        shortlist.Clear(); entries.Clear(); stale.Clear(); count = 0;
        frame = membershipFrame = -1; membershipIndex = 0; collecting = false; nextSurvey = 0; lastNow = -1; world = null; Truncated = false;
    }
    private static bool Live(Character actor) => actor && actor.isActiveAndEnabled && !actor.IsDead();
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Supported(Vector3 value) => TargetRoster.Finite(value) &&
        Mathf.Abs(value.x) <= 20000 && Mathf.Abs(value.y) <= 20000 && Mathf.Abs(value.z) <= 20000;
    private static AutonomyPosition Position(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
