using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// A virtual camera only. The ordinary player solver remains the prepared
// return; this never creates a rendering camera or changes acquisition target.
internal sealed class PlayerComplementaryCamera
{
    internal readonly CinemachineShotRig Rig = new CinemachineShotRig { PictureNamespace = 2 };
    private readonly ComplementaryPerspectivePolicy policy = new ComplementaryPerspectivePolicy();
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private Character? actor;
    private ZNet? world;
    private long sceneId;
    private string storyOwner = "";
    private float nextPreview, solvedAt = -1;
    private Vector3 position;
    private Quaternion rotation;
    private float fov;
    private bool ready;
    private int mask;
    internal bool Active => policy.Active;
    internal string Description => Active ? "Player complementary encounter subject; " + policy.Reason : "Player primary; " + policy.Reason;

    internal bool TrySelect(CameraSceneFrame? scene, string ownerId, Camera output, BroadcastSettings settings, float dt,
        bool continuous, bool moveCarrier, bool editorialHold, bool presentationConfirmed, bool primaryReady,
        Vector3 primaryCamera, out Vector3 selectedPosition, out Quaternion selectedRotation, out float selectedFov)
    {
        selectedPosition = primaryCamera; selectedRotation = Quaternion.identity; selectedFov = output.fieldOfView;
        float now = Time.unscaledTime;
        if (world != ZNet.instance || scene == null || scene.SceneId != sceneId || ownerId != storyOwner)
        { Reset(); world = ZNet.instance; sceneId = scene?.SceneId ?? 0; storyOwner = ownerId; }
        if (scene == null) return false;
        bool wasActive = policy.Active;
        bool editorial = settings.DynamicShots.Value && settings.ShotSequences.Value && !editorialHold;
        Character? candidate = scene.EncounterActor;
        bool related = candidate && candidate.isActiveAndEnabled && !candidate.IsDead() && !candidate.IsPlayer() &&
            !candidate.GetZDOID().IsNone() && scene.EncounterConfidence >= .6f &&
            scene.Encounter != EncounterKind.None && !scene.Interior && scene.Environment == ShotEnvironment.Open &&
            !scene.DoorwayTransition && TargetRoster.Finite(candidate.transform.position) &&
            !Character.InInterior(candidate.transform.position) &&
            Vector3.Distance(scene.PrimaryFocus, CreatureSceneContext.Focus(candidate)) <= 60;
        if (candidate != actor)
        {
            if (wasActive) policy.Reject(now);
            Rig.Reset(); actor = candidate; solvedAt = -1; nextPreview = now; ready = false;
        }
        bool prepare = related && primaryReady && (policy.Active || policy.MayPrepare(now, editorial, scene.ActionPhase));
        bool solvedNow = false;
        if (prepare && (policy.Active || now >= nextPreview))
        {
            nextPreview = now + .2f;
            ready = Connected(scene.PrimaryFocus, CreatureSceneContext.Focus(actor!));
            Rig.RenderingCamera = output; Rig.Aspect = output.aspect; Rig.NearClipPlane = output.nearClipPlane;
            Rig.PresentationConfirmed = wasActive && presentationConfirmed;
            // A short insert develops no independent ornamental sequence.
            Rig.EditorialHold = true;
            Vector3 offset = primaryCamera - scene.PrimaryFocus;
            Rig.InitialHeading = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            if (ready) ready = Solve(settings, dt, continuous, moveCarrier);
            solvedNow = true; solvedAt = now;
        }
        bool connectedView = prepare && ready && now >= solvedAt && now - solvedAt <= .25f &&
            Vector3.Distance(position, primaryCamera) <= 48 &&
            ComplementaryPerspectivePolicy.SameSide(ToCore(scene.PrimaryFocus), ToCore(scene.EncounterFocus),
                ToCore(primaryCamera), ToCore(position));
        bool active = policy.Update(now, scene.SceneId, related ? candidate!.GetZDOID().ToString() : "",
            editorial, scene.ActionPhase, !wasActive && presentationConfirmed && primaryReady,
            wasActive && presentationConfirmed, related, primaryReady, connectedView);
        if (!active) return false;
        // Admission may use cached background evidence to decide whether to try,
        // but only a fresh full solve can become this frame's actual picture.
        if (!solvedNow && !Solve(settings, 0, continuous, moveCarrier))
        { policy.Reject(now); ready = false; return false; }
        if (Vector3.Distance(position, primaryCamera) > 48 ||
            !ComplementaryPerspectivePolicy.SameSide(ToCore(scene.PrimaryFocus), ToCore(scene.EncounterFocus),
                ToCore(primaryCamera), ToCore(position)))
        { policy.Reject(now); return false; }
        selectedPosition = position; selectedRotation = rotation; selectedFov = fov;
        return true;
    }
    private bool Solve(BroadcastSettings settings, float dt, bool continuous, bool moveCarrier) => actor &&
        Rig.TrySolve(CinematicShotInput.ForCreature(actor, sceneId), settings, dt, continuous, moveCarrier,
            out position, out rotation, out fov) && Rig.LastPictureUsable;

    private bool Connected(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < 1) return false;
        if (mask == 0) mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) return false;
        for (int i = 0; i < count; i++)
            if (hits[i].collider && !hits[i].collider.GetComponentInParent<Character>()) return false;
        return true;
    }
    private static AutonomyPosition ToCore(Vector3 p) => new AutonomyPosition(p.x, p.y, p.z);
    internal void Reset()
    {
        policy.Reset(); Rig.Reset(); actor = null; world = null; sceneId = 0; storyOwner = "";
        nextPreview = 0; solvedAt = -1; ready = false;
    }
    internal void Dispose() { Reset(); Rig.Dispose(); }
}
