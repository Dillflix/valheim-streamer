using System;

namespace ValheimBroadcaster.Core;

/// <summary>Frame a measured trader as a character rather than applying a
/// building's eight-metre minimum. These are proposals; geometry is unchanged.</summary>
public static class ScenicNpcComposition
{
    public static float Distance(float height, float radius, float fieldOfView, float aspect, bool detail)
    {
        if (!Finite(height) || height <= 0 || !Finite(radius) || radius <= 0 ||
            !Finite(fieldOfView) || fieldOfView < 30 || fieldOfView > 110 || !Finite(aspect) || aspect <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        double tangent = Math.Tan(fieldOfView * Math.PI / 360);
        double vertical = height / (2 * tangent * (detail ? .55 : .35));
        double horizontal = radius / (tangent * aspect * .65);
        return (float)Math.Max(2.5, Math.Max(vertical, horizontal));
    }
    public static float Elevation(float height)
    {
        if (!Finite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return Math.Max(.25f, Math.Min(.8f, height * .15f));
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
