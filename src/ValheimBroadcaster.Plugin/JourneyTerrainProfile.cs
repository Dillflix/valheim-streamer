using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

/// <summary>Fits measured route knots without moving the accepted seam or its
/// incoming tangent. Surface observations are supplied by the adapter. This
/// geometric fit is a proposal, not a collision/foliage certificate.</summary>
internal static class JourneyTerrainProfile
{
    internal const float MaximumGrade = .4f;
    internal static bool TryFit(IReadOnlyList<Vector3> knots, IReadOnlyList<float> floors,
        float clearance, float loft, out Vector3[] fitted, out string reason, bool preserveIncomingTangent = true)
    {
        fitted = Array.Empty<Vector3>(); reason = "Invalid measured journey terrain profile";
        if (knots == null || floors == null || knots.Count < 3 || knots.Count > 16 || floors.Count != knots.Count ||
            !Finite(clearance) || clearance < 0 || clearance > 18 || !Finite(loft) || loft < 0 || loft > 16) return false;
        var required = new float[floors.Count];
        for (int i = 0; i < required.Length; i++)
        {
            Vector3 p = knots[i];
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || !Finite(floors[i])) return false;
            if (i > 0 && HorizontalStep(knots[i - 1], p) < .05f) return false;
            required[i] = floors[i];
        }
        for (int i = required.Length - 2; i >= 0; i--)
            required[i] = Mathf.Max(required[i], required[i + 1] - HorizontalStep(knots[i], knots[i + 1]) * MaximumGrade);
        if (required[0] > knots[0].y + .005f || preserveIncomingTangent && required[1] > knots[1].y + .005f)
        { reason = "Terrain rises inside the preserved incoming tangent; route around it"; return false; }
        fitted = new Vector3[knots.Count]; fitted[0] = knots[0];
        if (preserveIncomingTangent) fitted[1] = knots[1];
        float distance = preserveIncomingTangent ? HorizontalStep(knots[0], knots[1]) : 0;
        for (int i = preserveIncomingTangent ? 2 : 1; i < fitted.Length; i++)
        {
            Vector3 point = knots[i]; float step = HorizontalStep(knots[i - 1], point); distance += step;
            float target = Mathf.Max(required[i], floors[i] + clearance + loft * Mathf.Clamp01(distance / 32f));
            point.y = Mathf.MoveTowards(fitted[i - 1].y, target, step * MaximumGrade);
            if (point.y < required[i] - .005f)
            { fitted = Array.Empty<Vector3>(); reason = "Terrain needs a steeper climb than this cinematic corridor permits"; return false; }
            fitted[i] = point;
        }
        reason = preserveIncomingTangent ? "Measured terrain profile retains its seam and bounded climbs/descents" :
            "Independent departure develops a measured initial climb without inheriting a previous shot's tangent"; return true;
    }
    private static float HorizontalStep(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(b - a, Vector3.up).magnitude;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
