using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Projected operator UI only: never creates a Renderer, game object,
/// physics component or camera. The caller draws after the clean source render.</summary>
internal sealed class CameraFixturePreview : IDisposable
{
    private const int Samples = 65;
    private readonly Vector3[] body = new Vector3[Samples], aim = new Vector3[Samples];
    private readonly Vector3[] corners = new Vector3[8];
    private readonly ScenicSplinePath spline = new ScenicSplinePath();
    private ScenicShotPlan? sampled;
    private bool available;
    private GUIStyle? labelStyle;
    private static readonly Color RouteColor = new Color(.2f, .95f, 1f, .85f);
    private static readonly Color AimColor = new Color(1f, .75f, .2f, .65f);
    private static readonly Color SubjectColor = new Color(.35f, 1f, .45f, .7f);

    internal void Draw(Camera camera, ScenicShotPlan plan, float progress, string label)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || !camera ||
            Screen.width < 16 || Screen.height < 16) return;
        if (!ReferenceEquals(sampled, plan)) Prepare(plan);
        if (!available) return;
        Matrix4x4 previousMatrix = GUI.matrix; Color previousColor = GUI.color;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            for (int i = 1; i < Samples; i++) Segment(camera, body[i - 1], body[i], RouteColor, 2f);
            for (int i = 0; i < Samples; i += 8) Segment(camera, body[i], aim[i], AimColor, 1f);
            for (int i = 1; i < Samples; i++) Segment(camera, aim[i - 1], aim[i], AimColor, 1f);
            if (plan.Feature?.MeasuredBounds is PlaceBounds bounds) Bounds(camera, bounds, SubjectColor);
            if (plan.Feature != null)
                foreach (PlaceBounds member in plan.Feature.MemberBounds) Bounds(camera, member, new Color(.35f, 1f, .45f, .35f));
            if (progress >= 0 && progress <= 1)
            {
                int index = Mathf.RoundToInt(progress * (Samples - 1));
                Segment(camera, body[index], aim[index], Color.white, 2f);
            }
            GUI.color = Color.white;
            labelStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            GUI.Box(new Rect(12, Mathf.Max(12, Screen.height - 66), Mathf.Min(680, Screen.width - 24), 54),
                label + "\nOperator preview only • cyan camera path • amber aim • green observed subject bounds", labelStyle);
        }
        finally { GUI.matrix = previousMatrix; GUI.color = previousColor; }
    }

    private void Prepare(ScenicShotPlan plan)
    {
        Reset(); sampled = plan;
        bool native = plan.UseNativeSpline && !plan.IsPan && !plan.IsPictureOnly;
        if (native && !spline.Prepare(plan, 1)) return;
        for (int i = 0; i < Samples; i++)
        {
            float fraction = i / (float)(Samples - 1);
            ScenicShotSample sample = native ? spline.Sample(fraction) : plan.Sample(fraction);
            body[i] = V(sample.CameraPosition); aim[i] = V(sample.AimPosition);
            if (!TargetRoster.Finite(body[i]) || !TargetRoster.Finite(aim[i])) return;
        }
        available = true;
    }

    private void Bounds(Camera camera, PlaceBounds value, Color color)
    {
        for (int i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? value.Minimum.X : value.Maximum.X,
            (i & 2) == 0 ? value.Minimum.Y : value.Maximum.Y, (i & 4) == 0 ? value.Minimum.Z : value.Maximum.Z);
        for (int i = 0; i < 8; i++)
            for (int bit = 1; bit <= 4; bit *= 2) if ((i & bit) == 0) Segment(camera, corners[i], corners[i | bit], color, 1f);
    }

    private static void Segment(Camera camera, Vector3 from, Vector3 to, Color color, float width)
    {
        Vector3 normal = camera.transform.forward, origin = camera.transform.position;
        float minimum = Mathf.Max(.05f, camera.nearClipPlane), aDepth = Vector3.Dot(from - origin, normal), bDepth = Vector3.Dot(to - origin, normal);
        if (aDepth < minimum && bDepth < minimum) return;
        if (aDepth < minimum) from = Vector3.Lerp(from, to, (minimum - aDepth) / (bDepth - aDepth));
        else if (bDepth < minimum) to = Vector3.Lerp(to, from, (minimum - bDepth) / (aDepth - bDepth));
        Vector3 a = camera.WorldToScreenPoint(from), b = camera.WorldToScreenPoint(to);
        if (!Finite(a.x) || !Finite(a.y) || !Finite(b.x) || !Finite(b.y)) return;
        Vector2 start = new Vector2(a.x, Screen.height - a.y), finish = new Vector2(b.x, Screen.height - b.y);
        if (!Clip(ref start, ref finish)) return;
        float distance = Vector2.Distance(start, finish);
        if (distance < .1f) return;
        Matrix4x4 previous = GUI.matrix;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(finish.y - start.y, finish.x - start.x) * Mathf.Rad2Deg, start);
        GUI.DrawTexture(new Rect(start.x, start.y - width * .5f, distance, width), Texture2D.whiteTexture);
        GUI.matrix = previous;
    }

    private static bool Clip(ref Vector2 from, ref Vector2 to)
    {
        float start = 0, end = 1, dx = to.x - from.x, dy = to.y - from.y;
        if (!Edge(-dx, from.x, ref start, ref end) || !Edge(dx, Screen.width - from.x, ref start, ref end) ||
            !Edge(-dy, from.y, ref start, ref end) || !Edge(dy, Screen.height - from.y, ref start, ref end)) return false;
        Vector2 point = from; from = new Vector2(point.x + start * dx, point.y + start * dy);
        to = new Vector2(point.x + end * dx, point.y + end * dy); return true;
    }
    private static bool Edge(float direction, float distance, ref float start, ref float end)
    {
        if (Mathf.Abs(direction) < .00001f) return distance >= 0;
        float parameter = distance / direction;
        if (direction < 0) { if (parameter > end) return false; start = Mathf.Max(start, parameter); }
        else { if (parameter < start) return false; end = Mathf.Min(end, parameter); }
        return true;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Vector3 V(AutonomyPosition point) => new Vector3(point.X, point.Y, point.Z);
    internal void Reset() { sampled = null; available = false; spline.Reset(); }
    public void Dispose() { sampled = null; available = false; spline.Dispose(); }
}
