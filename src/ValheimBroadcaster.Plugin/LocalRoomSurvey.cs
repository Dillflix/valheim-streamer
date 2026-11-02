using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// A small structural survey, not semantic building recognition and never scene
// readiness. Tree canopies cannot become roofs merely because a ray hits them.
internal sealed class LocalRoomSurvey
{
    private readonly RoomAwarenessPolicy policy = new RoomAwarenessPolicy();
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private static readonly Vector3[] RoofOffsets = { Vector3.zero, Vector3.forward * .65f,
        Vector3.back * .65f, Vector3.left * .65f, Vector3.right * .65f };
    private float sampledAt = -1;
    private Vector3 sampledPosition, openingDirection;
    private int mask;
    internal Ship? SupportingShip { get; private set; }
    internal Collider? SupportingCollider { get; private set; }

    internal void Update(Character actor, Vector3 velocity, float now, CameraSceneFrame frame)
    {
        Vector3 position = actor.transform.position;
        if (sampledAt >= 0 && (now < sampledAt || now - sampledAt > 1 || Vector3.Distance(position, sampledPosition) > 12f)) Reset();
        if (sampledAt < 0 || now - sampledAt >= .25f)
        {
            sampledAt = now; sampledPosition = position;
            mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            bool complete = true;
            Vector3 center = position + Vector3.up * Mathf.Clamp(actor.GetHeight() * .7f, .7f, 2f);
            int roof = 0, walls = 0;
            float ceiling = 0;
            foreach (Vector3 offset in RoofOffsets)
            {
                Collider? found = Cast(center + offset, Vector3.up, 8f, frame.Interior, true, ref complete, out float distance);
                if (!found) continue;
                roof++;
                float height = center.y - position.y + distance;
                ceiling = ceiling <= 0 ? height : Mathf.Min(ceiling, height);
            }
            for (int i = 0; i < 8; i++)
            {
                float radians = i * Mathf.PI / 4f;
                if (Cast(center, new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians)), 7f,
                    frame.Interior, true, ref complete, out _)) walls++;
            }
            Vector3 heading = velocity; heading.y = 0;
            if (heading.sqrMagnitude < .36f) { heading = actor.transform.forward; heading.y = 0; }
            if (!TargetRoster.Finite(heading) || heading.sqrMagnitude < .01f) heading = Vector3.forward;
            heading.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, heading);
            Collider? left = Cast(center, -side, 1.6f, frame.Interior, true, ref complete, out _);
            Collider? right = Cast(center, side, 1.6f, frame.Interior, true, ref complete, out _);
            Collider? ahead = Cast(center, heading, 3.5f, frame.Interior, true, ref complete, out _);
            bool roofAhead = Cast(center + heading * 2f, Vector3.up, 8f, frame.Interior, true, ref complete, out _) != null;
            bool roofBehind = Cast(center - heading * 2f, Vector3.up, 8f, frame.Interior, true, ref complete, out _) != null;
            bool doorGeometry = left && left.GetComponentInParent<Door>() || right && right.GetComponentInParent<Door>();
            bool opening = left && right && !ahead && (roofAhead != roofBehind || doorGeometry);
            if (opening) openingDirection = heading;
            policy.Observe(now, new RoomObservation(complete, frame.Interior, roof, walls, ceiling, opening));

            // Local ground contact fields are not authoritative on remote players.
            // A directly supporting loaded ship collider is observable on either client.
            bool supportComplete = true;
            SupportingCollider = Cast(position + Vector3.up * .6f, Vector3.down, 1.9f,
                frame.Interior, false, ref supportComplete, out _);
            SupportingShip = supportComplete && SupportingCollider ? SupportingCollider.GetComponentInParent<Ship>() : null;
        }
        frame.Environment = policy.Environment; frame.EnvironmentConfidence = policy.Confidence;
        frame.CeilingHeight = policy.CeilingHeight; frame.DoorwayTransition = policy.DoorwayTransition;
        frame.OpeningDirection = policy.DoorwayTransition ? openingDirection : Vector3.zero;
    }

    private Collider? Cast(Vector3 start, Vector3 direction, float distance, bool dungeon, bool structural,
        ref bool complete, out float nearest)
    {
        nearest = distance;
        int count = Physics.RaycastNonAlloc(start, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) { complete = false; return null; }
        Collider? chosen = null;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (!collider || !collider.enabled || collider.GetComponentInParent<Character>() ||
                collider.GetComponentInParent<TreeBase>() || collider.GetComponentInParent<TreeLog>() ||
                !TargetRoster.Finite(hits[i].point) || float.IsNaN(hits[i].distance) || float.IsInfinity(hits[i].distance)) continue;
            if (structural)
            {
                if (collider.GetComponentInParent<Ship>()) continue;
                // Outdoors only constructed Piece geometry establishes shelter.
                // Dungeon meshes are structural but remain a separate layer flag.
                if (!dungeon && !collider.GetComponentInParent<Piece>()) continue;
                if (dungeon && collider.attachedRigidbody && !collider.GetComponentInParent<Piece>()) continue;
            }
            if (hits[i].distance < nearest) { nearest = hits[i].distance; chosen = collider; }
        }
        return chosen;
    }

    internal void Reset()
    { policy.Reset(); sampledAt = -1; sampledPosition = openingDirection = Vector3.zero; SupportingShip = null; SupportingCollider = null; }
}
