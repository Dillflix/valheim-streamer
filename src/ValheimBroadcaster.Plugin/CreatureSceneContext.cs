using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Only observes an already loaded living actor. It does not awaken AI, claim
// ownership, change animation state or infer a replicated combat target.
internal sealed class CreatureSceneContext
{
    private readonly CameraSceneFrame frame = new CameraSceneFrame();
    private readonly CameraSceneSubject subject = new CameraSceneSubject();
    private Character? actor;
    private ZNet? world;
    private ZDOID characterId;
    private Vector3 previousPosition, observedPosition, observedVelocity;
    private float frameAt = -1, observedAt = -1, activitySince, attackUntil, damageUntil, previousHealth;
    private int sampledFrame = -1;
    private bool facingInitialized;
    private CinematicActivity pendingActivity;

    // Groups are deliberately not guessed from proximity or species names. The
    // shared setting remains accepted by the provider contract; this profile
    // currently observes one creature and never grants player coverage credit.
    internal CameraSceneFrame Update(Character primary, bool groupsEnabled = true)
    {
        float now = Time.unscaledTime;
        if (!Valid(primary) || !Finite(now)) { Reset(); return frame; }
        Vector3 position = primary.transform.position;
        if (actor != primary || characterId != primary.GetZDOID() || world != ZNet.instance ||
            now < frameAt || frameAt >= 0 && (now - frameAt > 1f || Vector3.Distance(position, previousPosition) > 16f))
        {
            Reset();
            actor = primary; characterId = primary.GetZDOID(); world = ZNet.instance;
            subject.Actor = primary; subject.Id = subject.CharacterId = characterId.ToString();
            subject.IsPrimary = true; subject.Weight = 2f;
            frame.Subjects.Add(subject);
        }
        if (sampledFrame == Time.frameCount) return frame;
        sampledFrame = Time.frameCount;
        float dt = frameAt < 0 ? 0 : Mathf.Clamp(now - frameAt, 0, .1f);
        frameAt = now; previousPosition = position;
        frame.PrimaryFocus = frame.Focus = subject.Focus = Focus(primary);
        frame.Interior = Character.InInterior(position);
        subject.Height = Height(primary); subject.Radius = Radius(primary);
        subject.BottomOffset = subject.Focus.y - position.y;
        if (observedAt < 0 || now - observedAt >= .25f) Observe(primary, position, now);
        frame.Velocity = frame.GroupVelocity = subject.Velocity = observedVelocity;
        UpdateFacing(primary.transform.forward, dt);
        return frame;
    }

    private void Observe(Character primary, Vector3 position, float now)
    {
        float health = primary.GetHealth();
        float elapsed = observedAt < 0 ? 0 : now - observedAt;
        if (elapsed > .001f && elapsed <= 1f && Vector3.Distance(position, observedPosition) <= 16f)
        {
            // Birds and swimming creatures can move vertically. Heading may be
            // planar, but the body trajectory must retain their observed height.
            Vector3 measured = Vector3.ClampMagnitude((position - observedPosition) / elapsed, 20f);
            observedVelocity = Vector3.Lerp(observedVelocity, measured, 1f - Mathf.Exp(-3f * elapsed));
            if (Finite(health) && Finite(previousHealth) && health < previousHealth - .1f) damageUntil = now + 4f;
        }
        else observedVelocity = Vector3.zero;
        if (primary.InAttack()) attackUntil = now + 1.5f;
        observedPosition = position; observedAt = now; previousHealth = health;

        CinematicActivity desired;
        string evidence;
        float confidence;
        if (attackUntil > now)
        { desired = CinematicActivity.Combat; evidence = "attack observed; target unknown"; confidence = .65f; }
        else if (observedVelocity.magnitude > 1.2f)
        { desired = CinematicActivity.Travel; evidence = "sustained observed movement"; confidence = .95f; }
        else
        {
            desired = CinematicActivity.Idle;
            evidence = damageUntil > now ? "recent health loss; cause unknown" : "stationary or slow movement";
            confidence = damageUntil > now ? .35f : .85f;
        }
        if (desired != pendingActivity) { pendingActivity = desired; activitySince = now; }
        if (desired == frame.Activity || now - activitySince >= (desired == CinematicActivity.Combat ? .25f : 1.25f))
            frame.Activity = desired;
        frame.Confidence = confidence;
        frame.Description = $"{frame.Activity}; one loaded creature; {evidence}; body {subject.Height:F1}m × {subject.Radius * 2f:F1}m" +
            (frame.Interior ? "; interior" : "");
    }

    private void UpdateFacing(Vector3 direction, float dt)
    {
        direction.y = 0;
        if (!TargetRoster.Finite(direction) || direction.sqrMagnitude < .001f)
        { frame.FacingConfidence = Mathf.MoveTowards(frame.FacingConfidence, 0, dt); return; }
        direction.Normalize();
        if (!facingInitialized) { frame.FacingDirection = direction; facingInitialized = true; }
        float before = Mathf.Atan2(frame.FacingDirection.x, frame.FacingDirection.z) * Mathf.Rad2Deg;
        float after = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float difference = Mathf.DeltaAngle(before, after);
        float turn = Mathf.Clamp(difference * (1f - Mathf.Exp(-2f * dt)), -45f * dt, 45f * dt);
        float radians = (before + turn) * Mathf.Deg2Rad;
        frame.FacingDirection = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
        frame.FacingConfidence = Mathf.MoveTowards(frame.FacingConfidence,
            Mathf.Abs(difference) > 75f ? .2f : .65f, dt * .8f);
    }

    internal static Vector3 Focus(Character primary) => primary
        ? primary.transform.position + Vector3.up * (Height(primary) * .6f) : Vector3.zero;
    private static float Height(Character primary) => Dimension(primary.GetHeight(), .2f, 24f, 2f);
    private static float Radius(Character primary) => Dimension(primary.GetRadius(), .15f, 8f, .6f);
    private static float Dimension(float value, float minimum, float maximum, float fallback) =>
        Finite(value) && value > 0 ? Mathf.Clamp(value, minimum, maximum) : fallback;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool WorldPosition(Vector3 value) => TargetRoster.Finite(value) &&
        Mathf.Abs(value.x) <= 20000 && Mathf.Abs(value.y) <= 20000 && Mathf.Abs(value.z) <= 20000;
    private static bool Valid(Character? value) => value && value.isActiveAndEnabled && value.gameObject.activeInHierarchy &&
        !value.IsPlayer() && !value.IsDead() && !value.GetZDOID().IsNone() && WorldPosition(value.transform.position) && WorldPosition(Focus(value));

    internal void Reset()
    {
        frame.Subjects.Clear();
        frame.PrimaryFocus = frame.Focus = frame.Velocity = frame.GroupVelocity = Vector3.zero;
        frame.FacingDirection = Vector3.forward; frame.InterestFocus = Vector3.zero;
        frame.FacingConfidence = frame.InterestConfidence = frame.Spread = frame.Confidence = 0;
        frame.HasInterest = frame.Interior = false; frame.InterestKind = "none";
        frame.Activity = pendingActivity = CinematicActivity.Idle;
        frame.Description = "Awaiting loaded creature context";
        subject.Actor = null!; subject.Player = null; subject.Id = subject.CharacterId = "";
        subject.Focus = subject.Velocity = Vector3.zero;
        subject.Weight = subject.Radius = subject.Height = subject.BottomOffset = 0;
        subject.IsPrimary = subject.IsOpponent = false;
        actor = null; world = null; characterId = default;
        previousPosition = observedPosition = observedVelocity = Vector3.zero;
        frameAt = observedAt = -1; activitySince = attackUntil = damageUntil = previousHealth = 0;
        sampledFrame = -1; facingInitialized = false;
    }
}
