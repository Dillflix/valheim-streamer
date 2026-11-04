using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Presentation credit belongs to a stable encounter and its spatial
    /// topology. Nearby background presence gets a bounded contextual introduction;
    /// it does not permanently replace work or travel with threat coverage.</summary>
    public sealed class EncounterEpisodeCoverage
    {
        private long scene = -1;
        private string principal = "", pendingPrincipal = "";
        private int hostiles, players, sectors;
        private float spread;
        private EncounterPressure pressure;
        private double last = -1, pendingSince = -1, contextShown, overviewShown;
        private bool encircled, previousPresented;
        public bool ContextEstablished => contextShown >= 3;
        public bool OverviewEstablished => overviewShown >= 3;
        public long Revision { get; private set; }

        public EncounterShotPurpose Update(double now, long sceneId, string target, bool active,
            EncounterPressure currentPressure, int hostileCount, int playerCount, int sectorMask, float currentSpread,
            bool surrounded, bool protectMoment, bool presented, string presentedTarget, EncounterCoverage previousCoverage)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || hostileCount < 0 || playerCount < 0 ||
                float.IsNaN(currentSpread) || float.IsInfinity(currentSpread) || currentSpread < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!active) { Reset(); last = now; return EncounterShotPurpose.Player; }
            double dt = last >= 0 && now >= last && now - last <= 1 ? Math.Min(.25, now - last) : 0;
            if (last >= 0 && now < last) Reset();
            last = now;
            bool newScene = scene != sceneId;
            bool changed = principal != target || currentPressure > pressure || surrounded != encircled ||
                Math.Abs(hostileCount - hostiles) >= Math.Max(2, hostiles / 2) || Math.Abs(playerCount - players) >= 2 ||
                BitCount(sectors ^ sectorMask) >= 3 || Math.Abs(currentSpread - spread) >= Math.Max(10, spread * .6f);
            bool reset = newScene;
            if (changed && !newScene)
            {
                if (pendingSince < 0 || pendingPrincipal != target) { pendingSince = now; pendingPrincipal = target; }
                // Don't replace a protected action merely to refresh its overview.
                // Evidence is retained, so the change is addressed once it settles.
                reset = !protectMoment && now - pendingSince >= 1;
            }
            else { pendingSince = -1; pendingPrincipal = ""; }
            if (reset)
            {
                scene = sceneId; principal = target; hostiles = hostileCount; players = playerCount;
                sectors = sectorMask; spread = currentSpread; encircled = surrounded; pressure = currentPressure;
                contextShown = overviewShown = 0; previousPresented = false; pendingSince = -1; Revision++;
            }
            bool belongs = !changed && presented && presentedTarget == principal && previousCoverage.Fulfilled &&
                previousCoverage.Purpose != EncounterShotPurpose.Player;
            if (belongs && previousPresented)
            {
                contextShown += dt;
                if (previousCoverage.Purpose == EncounterShotPurpose.Overview) overviewShown += dt;
            }
            previousPresented = belongs;
            bool broad = surrounded || hostileCount >= 3 || playerCount >= 3;
            if (currentPressure == EncounterPressure.Presence && ContextEstablished && !changed)
                return EncounterShotPurpose.Player;
            return broad && !OverviewEstablished ? EncounterShotPurpose.Overview : EncounterShotPurpose.Exchange;
        }
        private static int BitCount(int value) { int count = 0; for (int i = 0; i < 8; i++) if ((value & (1 << i)) != 0) count++; return count; }
        public void Reset()
        {
            scene = -1; principal = pendingPrincipal = ""; last = pendingSince = -1;
            hostiles = players = sectors = 0; spread = 0; pressure = EncounterPressure.None;
            contextShown = overviewShown = 0; encircled = previousPresented = false; Revision++;
        }
    }
}
