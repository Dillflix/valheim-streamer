using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>One angular cell of the actually proposed Journey picture. Depths
/// are measured along its viewing ray, not along the camera's travel corridor.
/// A clear query reports its bounded observation range, never invented infinity.</summary>
public readonly struct JourneyViewSample
{
    public readonly double SolidDepth, FoliageDepth;
    public readonly bool Known;
    public JourneyViewSample(double solidDepth, double foliageDepth, bool known = true)
    { SolidDepth = solidDepth; FoliageDepth = foliageDepth; Known = known; }
}

public enum JourneyViewStatus { Unknown, CameraBlocked, Obscured, Readable }

/// <summary>A travelling view describes surrounding space; its synthetic aim
/// is a direction, not a subject behind which all scenery must disappear.
/// Near opaque coverage and foliage veils matter. A visible tree, cliff or
/// building surface farther into the view is valid context, not a failed ray.</summary>
public static class JourneyViewPolicy
{
    public const int Columns = 5, Rows = 3, SampleCount = Columns * Rows;
    public const double ObservationRange = 12, FoliageRange = 6;

    public readonly struct Result
    {
        public readonly JourneyViewStatus Status;
        public readonly double Score, NearOcclusion, FoliageCoverage, MeanDepth;
        public readonly int UsefulColumns;
        public readonly string Reason;
        public bool Readable => Status == JourneyViewStatus.Readable;
        public Result(JourneyViewStatus status, double score, double near, double foliage,
            double depth, int columns, string reason)
        { Status = status; Score = score; NearOcclusion = near; FoliageCoverage = foliage;
          MeanDepth = depth; UsefulColumns = columns; Reason = reason; }
    }

    public static bool SurfaceIsNearOcclusion(double depth, double nearClip) =>
        Finite(depth) && depth >= 0 && Finite(nearClip) && nearClip > 0 &&
        depth <= Math.Max(1.5, nearClip + .35);

    public static Result Evaluate(IReadOnlyList<JourneyViewSample>? samples, bool cameraClear,
        bool surveyComplete, bool insideVegetation, double nearClip)
    {
        if (insideVegetation) return Failure(JourneyViewStatus.CameraBlocked, "Journey camera intersects retained vegetation bounds");
        if (!cameraClear) return Failure(JourneyViewStatus.CameraBlocked, "Journey camera or near plane intersects solid geometry");
        if (!surveyComplete || !Finite(nearClip) || nearClip <= 0 || samples == null || samples.Count != SampleCount)
            return Failure(JourneyViewStatus.Unknown, "Journey angular view observations are incomplete");
        double total = 0, near = 0, foliage = 0, depth = 0;
        int usefulColumns = 0;
        for (int column = 0; column < Columns; column++)
        {
            int useful = 0;
            for (int row = 0; row < Rows; row++)
            {
                JourneyViewSample sample = samples[row * Columns + column];
                if (!sample.Known || !Finite(sample.SolidDepth) || !Finite(sample.FoliageDepth) ||
                    sample.SolidDepth < 0 || sample.FoliageDepth < 0 ||
                    sample.SolidDepth > ObservationRange || sample.FoliageDepth > FoliageRange)
                    return Failure(JourneyViewStatus.Unknown, "Journey angular view has unknown depth evidence");
                // Central picture cells carry more weight, but a central tree
                // does not by itself invalidate an otherwise legible forest.
                double weight = row == 1 && column >= 1 && column <= 3 ? 2 : 1;
                double nearest = sample.FoliageDepth < FoliageRange
                    ? Math.Min(sample.SolidDepth, sample.FoliageDepth) : sample.SolidDepth;
                bool close = SurfaceIsNearOcclusion(nearest, nearClip);
                total += weight; near += close ? weight : 0;
                // Foliage behind a nearer opaque wall/cliff cannot obscure
                // that surface. A small depth tie tolerance retains leaves
                // coincident with their own solid vegetation geometry.
                bool visibleLeaves = sample.FoliageDepth < FoliageRange && sample.FoliageDepth <= sample.SolidDepth + .05;
                foliage += visibleLeaves ? weight : 0;
                depth += Math.Min(ObservationRange, nearest) * weight;
                if (!close) useful++;
            }
            if (useful >= 2) usefulColumns++;
        }
        near /= total; foliage /= total; depth /= total;
        bool opaque = near >= .60 || usefulColumns < 2;
        bool veil = foliage >= .85;
        double score = Clamp(.45 * (1 - near) + .25 * (1 - foliage) + .20 * depth / ObservationRange + .10 * usefulColumns / Columns);
        if (opaque || veil) return new Result(JourneyViewStatus.Obscured, score, near, foliage, depth, usefulColumns,
            opaque ? "Near opaque geometry dominates the Journey view" : "A foreground foliage veil dominates the Journey view");
        return new Result(JourneyViewStatus.Readable, score, near, foliage, depth, usefulColumns,
            "Readable travelling context with measured angular depth");
    }

    private static Result Failure(JourneyViewStatus status, string reason) => new Result(status, 0, 0, 0, 0, 0, reason);
    private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
