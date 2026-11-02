using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Conservative local encounter evidence. Caller must establish a
    /// living hostile, same space and clear structural connection first.</summary>
    public static class ObservedEncounterEvidence
    {
        public static bool SupportsCombat(bool exactGroupTarget, bool hostileAttack, bool groupAttack,
            bool hostileDamaged, float nearestGroupDistance)
        {
            if (float.IsNaN(nearestGroupDistance) || float.IsInfinity(nearestGroupDistance) || nearestGroupDistance < 0)
                throw new ArgumentOutOfRangeException(nameof(nearestGroupDistance));
            return exactGroupTarget || nearestGroupDistance <= 12 && (hostileAttack || groupAttack && hostileDamaged);
        }
    }
}
