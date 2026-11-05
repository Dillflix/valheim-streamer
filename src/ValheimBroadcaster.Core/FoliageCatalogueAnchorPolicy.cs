using System;

namespace ValheimBroadcaster.Core;

public enum FoliageCatalogueAnchorAction { Keep, Reanchor, RestartScan }

/// <summary>Only an authoritative region or carrier displacement can replace
/// the shared catalogue's anchor. Local camera queries have no such authority.
/// An obsolete in-progress traversal must not delay the new region's survey.</summary>
public static class FoliageCatalogueAnchorPolicy
{
    public const double ReanchorMetres = 96;
    public static FoliageCatalogueAnchorAction Decide(bool anchored, double displacement, bool scanning,
        double retainedRadius, double requiredLocalRadius)
    {
        if (double.IsNaN(displacement) || double.IsInfinity(displacement) || displacement < 0)
            throw new ArgumentOutOfRangeException(nameof(displacement));
        if (double.IsNaN(retainedRadius) || double.IsInfinity(retainedRadius) ||
            double.IsNaN(requiredLocalRadius) || double.IsInfinity(requiredLocalRadius) ||
            requiredLocalRadius <= 0 || retainedRadius - requiredLocalRadius < ReanchorMetres)
            throw new ArgumentOutOfRangeException(nameof(retainedRadius));
        if (anchored && displacement < ReanchorMetres) return FoliageCatalogueAnchorAction.Keep;
        // Preserve a still-useful sweep during ordinary movement. Its local
        // survey footprint must remain inside the actual inventory radius;
        // cancelling every 96m can starve sustained 12m/s travel entirely.
        if (anchored && scanning && displacement < retainedRadius - requiredLocalRadius) return FoliageCatalogueAnchorAction.Keep;
        return scanning ? FoliageCatalogueAnchorAction.RestartScan : FoliageCatalogueAnchorAction.Reanchor;
    }
}
