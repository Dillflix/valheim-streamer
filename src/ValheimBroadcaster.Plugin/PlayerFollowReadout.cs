using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Sample the displayed camera and the locally observed player together. The
// compositor keeps this text with its captured pixels, including on a hold.
internal sealed class PlayerFollowReadout
{
    private Player? player;
    private string label = "", lines = "";
    private long revision, shot, scene;
    private float sampledAt = -1;
    private Vector3 sampledPosition;

    internal void Clear() { player = null; sampledAt = -1; lines = ""; }

    internal string Sample(Player? target, CameraRig rig, BroadcastSettings settings, long coverageRevision, long shotId, long sceneId)
    {
        Camera? output = rig.OutputCamera;
        if (!target || !output) { Clear(); return ""; }
        Player actor = target!;
        float now = Time.unscaledTime;
        float elapsed = now - sampledAt;
        bool samePlayer = player == actor && sampledAt >= 0 && elapsed >= 0 && elapsed <= 1;
        string kind = rig.PlayerFollowLabel;
        if (samePlayer && label == kind && revision == coverageRevision && shot == shotId && scene == sceneId && elapsed < .25f)
            return lines;

        Vector3 position = actor.transform.position;
        Vector3 delta = position - sampledPosition;
        string movement = "";
        if (samePlayer && elapsed >= .05f && delta.sqrMagnitude < 50f * 50f)
        {
            float speed = new Vector2(delta.x, delta.z).magnitude / elapsed;
            movement = speed < .15f ? "stationary" : $"{speed:F1} m/s ground speed";
        }
        string action = actor.InDodge() ? "dodging" : PlayerSceneContext.IsDrawingRanged(actor) ? "drawing ranged weapon" :
            actor.InAttack() ? "attacking" : actor.IsBlocking() ? "blocking" : actor.IsSwimming() ? "swimming" : "";
        // A stable upper-body landmark makes screen coordinates comparable
        // across camera families, including an insert in which the player exits frame.
        Vector3 upperBody = position + Vector3.up * (Mathf.Clamp(actor.GetHeight() * .8f, .5f, 14f) *
            Mathf.Max(.1f, actor.transform.lossyScale.y));
        Vector3 screen = output!.WorldToViewportPoint(upperBody);
        string location = PlayerFramePosition.Describe(screen.x, screen.y, screen.z);
        string tracking = action.Length == 0 ? movement : movement.Length == 0 ? action : action + ", " + movement;
        lines = "PLAYER: " + (tracking.Length == 0 ? "" : tracking + " | ") + "upper body " + location +
            $" | range {Vector3.Distance(output.transform.position, upperBody):F1} m\n" + rig.DescribeFollowCamera(settings);
        player = actor; sampledPosition = position; sampledAt = now;
        revision = coverageRevision; shot = shotId; scene = sceneId; label = kind;
        return lines;
    }
}
