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
        private double elapsed;
        private double scenicSeconds = 45, creatureSeconds = 25;
        private string? lastCreature;
        public double ScenicSeconds { get => scenicSeconds; set => scenicSeconds = Duration(value); }
        public double CreatureSeconds { get => creatureSeconds; set => creatureSeconds = Duration(value); }

        public void Reset()
        {
            Change(CoverageMode.Disabled, null);
            elapsed = 0;
            lastCreature = null;
            AwaitingPlayers = false;
        }

        public void Tick(double dt, string? readyPlayerId, int connectedOthers,
            bool idleSceneReady, IReadOnlyList<string> readyCreatures, bool holding = false)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (connectedOthers < 0) throw new ArgumentOutOfRangeException(nameof(connectedOthers));
            if (readyCreatures == null) throw new ArgumentNullException(nameof(readyCreatures));
            AwaitingPlayers = connectedOthers > 0 && readyPlayerId == null;
            if (readyPlayerId != null) { Change(CoverageMode.Player, readyPlayerId); return; }
            if (!idleSceneReady) { Change(CoverageMode.Recovery, null); return; }
            if (Mode != CoverageMode.Scenic && Mode != CoverageMode.Creature)
            { Change(CoverageMode.Scenic, null); return; }
            if (Mode == CoverageMode.Creature && !Contains(readyCreatures, SubjectId))
            { Change(CoverageMode.Scenic, null); return; }
            if (holding) return;
            elapsed = Math.Min(Mode == CoverageMode.Scenic ? ScenicSeconds : CreatureSeconds, elapsed + dt);
            if (Mode == CoverageMode.Creature)
            {
                if (elapsed >= CreatureSeconds) Change(CoverageMode.Scenic, null);
                return;
            }
            if (elapsed < ScenicSeconds || readyCreatures.Count == 0) return;
            // Ordinal round-robin, independent of provider enumeration order. Retain only
            // the last ID, not an ever-growing history of actors from a long-lived world.
            string? first = null, next = null;
            foreach (string id in readyCreatures)
            {
                if (first == null || StringComparer.Ordinal.Compare(id, first) < 0) first = id;
                if ((lastCreature == null || StringComparer.Ordinal.Compare(id, lastCreature) > 0) &&
                    (next == null || StringComparer.Ordinal.Compare(id, next) < 0)) next = id;
            }
            lastCreature = next ?? first;
            Change(CoverageMode.Creature, lastCreature);
        }

        private void Change(CoverageMode mode, string? id)
        {
            if (Mode == mode && SubjectId == id) return;
            Mode = mode; SubjectId = id; elapsed = 0; Revision++;
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
