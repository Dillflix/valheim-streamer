namespace ValheimBroadcaster.Core;

/// <summary>Brief opponent portraits require an opportunity to leave the
/// primary view. Surrounding or closing hostiles retain player/context coverage;
/// this is an editorial precaution, not a claim of aggro or imminent damage.</summary>
public static class EncounterPerspectivePolicy
{
    public static bool AllowsInsert(EncounterSituation situation) =>
        !situation.Encircled && situation.ApproachingCount == 0;
}
