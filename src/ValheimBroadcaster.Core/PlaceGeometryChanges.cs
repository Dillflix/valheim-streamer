using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>Separates discoveries made by moving the survey from changes to
/// already observed geometry. Partial observations cannot prove a removal.</summary>
public sealed class PlaceGeometryChanges
{
    private readonly Dictionary<string, PlaceBounds> previous = new Dictionary<string, PlaceBounds>(StringComparer.Ordinal);
    private AutonomyPosition origin;
    private float radius;
    private bool published, previousComplete;
    public void Reset() { previous.Clear(); published = previousComplete = false; }
    public bool Update(IReadOnlyList<PlacePieceObservation> pieces, AutonomyPosition center, float range, bool complete)
    {
        bool changed = false;
        var current = new Dictionary<string, PlaceBounds>(StringComparer.Ordinal);
        foreach (PlacePieceObservation piece in pieces)
        {
            current[piece.Id] = piece.Bounds;
            if (!published) continue;
            if (previous.TryGetValue(piece.Id, out PlaceBounds old))
                changed |= !Same(old, piece.Bounds);
            else if (previousComplete && Within(piece.Bounds.Center, origin, radius)) changed = true;
        }
        if (published && complete)
            foreach (var piece in previous)
                if (!current.ContainsKey(piece.Key) && Within(piece.Value.Center, center, range)) changed = true;
        previous.Clear(); foreach (var piece in current) previous.Add(piece.Key, piece.Value);
        origin = center; radius = range; published = true; previousComplete = complete; return changed;
    }
    private static bool Within(AutonomyPosition a, AutonomyPosition b, float radius)
    {
        double x = a.X - b.X, z = a.Z - b.Z;
        return x * x + z * z <= (double)radius * radius;
    }
    private static bool Same(PlaceBounds a, PlaceBounds b) =>
        a.Minimum.X == b.Minimum.X && a.Minimum.Y == b.Minimum.Y && a.Minimum.Z == b.Minimum.Z &&
        a.Maximum.X == b.Maximum.X && a.Maximum.Y == b.Maximum.Y && a.Maximum.Z == b.Maximum.Z;
}
