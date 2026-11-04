using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum CoverageMode { Disabled, Player, Scenic, Creature, Recovery }

    /// <summary>Shot policy only. Player rotation is supplied by BroadcastDirector;
    /// presence and ready players are deliberately separate inputs. No game objects are retained.</summary>
    public sealed class CoverageDirector
    {
        public CoverageMode Mode { get; private set; }
        public string? SubjectId { get; private set; }
        public bool AwaitingPlayers { get; private set; }
        public long Revision { get; private set; }
        public bool IsWildlifeCutaway { get; private set; }
        private double elapsed, unshownSeconds, cutawaySeconds;
        private bool creaturePresented;
        private double scenicSeconds = 45, creatureSeconds = 25;
        private string? lastCreature;
        private double clock;
        private readonly Dictionary<string, double> recentCreatures = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> failedCreatures = new Dictionary<string, double>(StringComparer.Ordinal);
        public const double CreaturePresentationTimeout = 3, CreatureFailedViewCooldown = 30;
        public const double ScenicPresentationTimeout = 3;
        public double ShotElapsed => elapsed;
        // Dedicated environmental shots have their own lifetime. This clock
        // continues to govern wildlife opportunities, not camera-plan edits.
        public bool DedicatedScenicShots { get; set; }
        public double CreatureCooldownSeconds { get; set; } = 300;
        public double ScenicSeconds { get => scenicSeconds; set => scenicSeconds = Duration(value); }
        public double CreatureSeconds { get => creatureSeconds; set => creatureSeconds = Duration(value); }

        /// <summary>Give an accepted replacement path its full presentation time without
        /// requesting another path through a revision change.</summary>
        public void ScenicPresentationStarted()
        {
            if (Mode == CoverageMode.Scenic) elapsed = 0;
        }

        public void Reset()
        {
            Change(CoverageMode.Disabled, null);
            elapsed = 0;
            lastCreature = null;
            clock = 0; recentCreatures.Clear(); failedCreatures.Clear();
            unshownSeconds = cutawaySeconds = 0; creaturePresented = IsWildlifeCutaway = false;
            AwaitingPlayers = false;
        }

        public void Tick(double dt, string? readyPlayerId, int connectedOthers,
            bool idleSceneReady, IReadOnlyList<string> readyCreatures, bool holding = false,
            bool usableFootage = true, bool allowWildlifeCutaway = false, string? retainedPlayerId = null,
            bool scenicSearchExhausted = true)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (connectedOthers < 0) throw new ArgumentOutOfRangeException(nameof(connectedOthers));
            if (readyCreatures == null) throw new ArgumentNullException(nameof(readyCreatures));
            Duration(CreatureCooldownSeconds);
            clock += Math.Min(dt, 86400);
            AwaitingPlayers = connectedOthers > 0 && readyPlayerId == null;
            if (IsWildlifeCutaway && readyPlayerId == null && retainedPlayerId != null)
            {
                // Selection can return before presentation is ready. The camera
                // still validates the retained actor/scene and may show recovery.
                Change(CoverageMode.Player, retainedPlayerId); return;
            }
            if (readyPlayerId != null)
            {
                bool mayCutaway = allowWildlifeCutaway && connectedOthers > 0 && idleSceneReady && !holding;
                if (IsWildlifeCutaway)
                {
                    cutawaySeconds += Math.Min(dt, 86400);
                    if (!mayCutaway || !Contains(readyCreatures, SubjectId) ||
                        !TickCreature(dt, false, usableFootage) || cutawaySeconds >= CreatureSeconds)
                        Change(CoverageMode.Player, readyPlayerId);
                    return;
                }
                if (mayCutaway && TrySelectCreature(readyCreatures))
                { IsWildlifeCutaway = true; cutawaySeconds = 0; return; }
                Change(CoverageMode.Player, readyPlayerId); return;
            }
            // If the player itself is unavailable, preserve the ordinary idle /
            // recovery behavior rather than inventing a ready return target.
            IsWildlifeCutaway = false; cutawaySeconds = 0;
            if (!idleSceneReady) { Change(CoverageMode.Recovery, null); return; }
            if (Mode != CoverageMode.Scenic && Mode != CoverageMode.Creature)
            { Change(CoverageMode.Scenic, null); return; }
            if (Mode == CoverageMode.Creature && !Contains(readyCreatures, SubjectId))
            { Change(CoverageMode.Scenic, null); return; }
            if (Mode == CoverageMode.Creature)
            {
                if (!TickCreature(dt, holding, usableFootage)) Change(CoverageMode.Scenic, null);
                return;
            }
            if (usableFootage) unshownSeconds = 0;
            else unshownSeconds += Math.Min(dt, 86400);
            if (Mode == CoverageMode.Scenic && !usableFootage && unshownSeconds >= ScenicPresentationTimeout)
            {
                // A failed landscape must not starve a loaded local creature.
                // Dedicated production first finishes its bounded route search.
                // Interrupting it on this generic presentation timeout repeatedly
                // suspends discovery before it can supply an actual scenic shot.
                // No available alternative means continue the adapter's bounded
                // scenic search, without claiming dwell or authorizing travel.
                if (!DedicatedScenicShots || scenicSearchExhausted) TrySelectCreature(readyCreatures);
                return;
            }
            // Holding means an operator-held live shot, never a compositor image.
            if (holding || !usableFootage) return;
            elapsed = Math.Min(ScenicSeconds, elapsed + dt);
            if (elapsed < ScenicSeconds) return;
            if (!TrySelectCreature(readyCreatures))
            { elapsed = 0; if (!DedicatedScenicShots) Revision++; } // Internal scenic editing can own its separate clock.
        }

        private bool TickCreature(double dt, bool holding, bool usableFootage)
        {
            if (usableFootage)
            {
                unshownSeconds = 0;
                if (!creaturePresented)
                { Remember(recentCreatures, SubjectId!, clock); creaturePresented = true; }
                if (!holding) elapsed = Math.Min(CreatureSeconds, elapsed + dt);
            }
            else
            {
                unshownSeconds += Math.Min(dt, 86400);
                // A loaded actor may have no usable local camera. This never
                // authorizes travel, including during a player cutaway.
                if (unshownSeconds >= CreaturePresentationTimeout)
                { Remember(failedCreatures, SubjectId!, clock); return false; }
            }
            return holding || elapsed < CreatureSeconds;
        }

        private bool TrySelectCreature(IReadOnlyList<string> readyCreatures)
        {
            // Ordinal round-robin, independent of provider enumeration order. Retain only
            // the last ID, not an ever-growing history of actors from a long-lived world.
            string? first = null, next = null;
            foreach (string id in readyCreatures)
            {
                if (recentCreatures.TryGetValue(id, out double shown) && clock - shown < CreatureCooldownSeconds) continue;
                if (failedCreatures.TryGetValue(id, out double failed) && clock - failed < CreatureFailedViewCooldown) continue;
                if (first == null || StringComparer.Ordinal.Compare(id, first) < 0) first = id;
                if ((lastCreature == null || StringComparer.Ordinal.Compare(id, lastCreature) > 0) &&
                    (next == null || StringComparer.Ordinal.Compare(id, next) < 0)) next = id;
            }
            string? selected = next ?? first;
            if (selected == null) return false;
            lastCreature = selected;
            Change(CoverageMode.Creature, selected);
            return true;
        }

        private static void Remember(Dictionary<string, double> history, string id, double now)
        {
            if (history.Count >= 64 && !history.ContainsKey(id))
            {
                string? oldest = null; double oldestTime = double.MaxValue;
                foreach (var item in history) if (item.Value < oldestTime) { oldest = item.Key; oldestTime = item.Value; }
                if (oldest != null) history.Remove(oldest);
            }
            history[id] = now;
        }

        private void Change(CoverageMode mode, string? id)
        {
            if (Mode == mode && SubjectId == id) return;
            Mode = mode; SubjectId = id; elapsed = unshownSeconds = cutawaySeconds = 0;
            creaturePresented = IsWildlifeCutaway = false; Revision++;
        }
        private static bool Contains(IReadOnlyList<string> values, string? id)
        {
            for (int i = 0; i < values.Count; i++) if (values[i] == id) return true;
            return false;
        }
        private static double Duration(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            return value;
        }
    }

    /// <summary>Bounds experimental remote interest attempts, including a lone target.
    /// The adapter advances candidates after each attempt and restores local interest during cooldown.</summary>
    public sealed class AcquisitionWindow
    {
        private double elapsed;
        public bool Active { get; private set; }
        public void Reset() { Active = false; elapsed = 0; }
        public bool Tick(double dt, bool needed, double attemptSeconds, double cooldownSeconds)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0 ||
                double.IsNaN(attemptSeconds) || double.IsInfinity(attemptSeconds) || attemptSeconds <= 0 ||
                double.IsNaN(cooldownSeconds) || double.IsInfinity(cooldownSeconds) || cooldownSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(dt));
            if (!needed) { Reset(); return false; }
            // Start with a local cooldown so an idle shot can establish before remote loading.
            elapsed += dt;
            if (elapsed >= (Active ? attemptSeconds : cooldownSeconds)) { Active = !Active; elapsed = 0; }
            return Active;
        }
    }
}
