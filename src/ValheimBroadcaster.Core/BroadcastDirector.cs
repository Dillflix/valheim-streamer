using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ValheimBroadcaster.Core
{
    public enum BroadcastState
    {
        Disabled,
        WaitingForPlayers,
        WaitingForTarget,
        Following
    }

    /// <summary>
    /// Deterministic target selection. The caller supplies monotonic elapsed seconds and
    /// reports readiness only after the selected player's scene object is available.
    /// This class never infers that a network position means a scene has loaded.
    /// </summary>
    public sealed class BroadcastDirector
    {
        private ReadOnlyCollection<PlayerTarget> _roster = Array.AsReadOnly(Array.Empty<PlayerTarget>());
        private string? _currentId;
        private double _dwellSeconds;
        private double _targetTimeoutSeconds;
        private double _dwellElapsed;
        private double _waitingElapsed;

        public BroadcastDirector(double dwellSeconds = 30, double targetTimeoutSeconds = 15)
        {
            DwellSeconds = dwellSeconds;
            TargetTimeoutSeconds = targetTimeoutSeconds;
        }

        public double DwellSeconds
        {
            get => _dwellSeconds;
            set => _dwellSeconds = PositiveFinite(value, nameof(DwellSeconds));
        }

        public double TargetTimeoutSeconds
        {
            get => _targetTimeoutSeconds;
            set => _targetTimeoutSeconds = PositiveFinite(value, nameof(TargetTimeoutSeconds));
        }

        public IReadOnlyList<PlayerTarget> Roster => _roster;
        public PlayerTarget? CurrentTarget => _roster.FirstOrDefault(player => player.Id == _currentId);
        public BroadcastState State { get; private set; } = BroadcastState.Disabled;
        public bool IsEnabled { get; private set; }
        public bool IsHolding { get; private set; }

        /// <summary>Changes when the selected ID changes, including selection becoming empty.</summary>
        public long SelectionRevision { get; private set; }

        /// <summary>
        /// Explicit solo camera test: replace the entire roster with the observer.
        /// Normal SetRoster calls still exclude the observer when its ID is supplied.
        /// </summary>
        public void SetSelfTarget(PlayerTarget observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            SetRoster(new[] { observer });
        }

        /// <summary>
        /// Replaces the snapshot, excludes the observer, and sorts by stable ID (ordinal).
        /// Duplicate IDs are collapsed using the ordinal-first name for order independence.
        /// A disappearing target advances to the next surviving ID, wrapping if necessary.
        /// </summary>
        public void SetRoster(IEnumerable<PlayerTarget> players, string? observerId = null)
        {
            if (players == null) throw new ArgumentNullException(nameof(players));

            var unique = new Dictionary<string, PlayerTarget>(StringComparer.Ordinal);
            foreach (PlayerTarget player in players)
            {
                if (player == null)
                    throw new ArgumentException("A roster cannot contain null players.", nameof(players));
                if (string.Equals(player.Id, observerId, StringComparison.Ordinal)) continue;

                if (!unique.TryGetValue(player.Id, out PlayerTarget? previous) ||
                    StringComparer.Ordinal.Compare(player.Name, previous.Name) < 0)
                    unique[player.Id] = player;
            }

            _roster = Array.AsReadOnly(unique.Values.OrderBy(player => player.Id, StringComparer.Ordinal).ToArray());
            if (_currentId != null && unique.ContainsKey(_currentId)) return;

            PlayerTarget? next = _roster.FirstOrDefault(player =>
                _currentId == null || StringComparer.Ordinal.Compare(player.Id, _currentId) > 0);
            ChangeSelection((next ?? _roster.FirstOrDefault())?.Id);
        }

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            ResetTiming();
            State = InitialState();
        }

        /// <summary>Pauses both automatic dwell cycling and unavailable-target skipping.</summary>
        public void SetHolding(bool holding) => IsHolding = holding;

        public bool Select(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (!_roster.Any(player => player.Id == id)) return false;
            ChangeSelection(id);
            return true;
        }

        public bool Next() => Move(1);
        public bool Previous() => Move(-1);

        /// <summary>
        /// Advances by a finite nonnegative interval. At most one target switch occurs per
        /// call; excess time is discarded so a stalled frame cannot skip an entire roster.
        /// Losing readiness resets the dwell period. A lone unavailable target is retained.
        /// </summary>
        public void Tick(double deltaSeconds, bool currentTargetReady)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Elapsed time must be finite and nonnegative.");
            if (!IsEnabled) return;
            if (_currentId == null)
            {
                State = BroadcastState.WaitingForPlayers;
                return;
            }

            if (!currentTargetReady)
            {
                State = BroadcastState.WaitingForTarget;
                _dwellElapsed = 0;
                if (IsHolding) return;

                _waitingElapsed = Math.Min(TargetTimeoutSeconds, _waitingElapsed + deltaSeconds);
                if (_waitingElapsed >= TargetTimeoutSeconds && _roster.Count > 1) Next();
                return;
            }

            State = BroadcastState.Following;
            _waitingElapsed = 0;
            if (IsHolding) return;

            _dwellElapsed = Math.Min(DwellSeconds, _dwellElapsed + deltaSeconds);
            if (_dwellElapsed >= DwellSeconds && _roster.Count > 1) Next();
        }

        private bool Move(int direction)
        {
            if (_roster.Count == 0) return false;

            int index = -1;
            for (int i = 0; i < _roster.Count; i++)
                if (_roster[i].Id == _currentId) { index = i; break; }

            int next = index < 0 ? (direction > 0 ? 0 : _roster.Count - 1) :
                (index + direction + _roster.Count) % _roster.Count;
            ChangeSelection(_roster[next].Id);
            return true;
        }

        private void ChangeSelection(string? id)
        {
            if (string.Equals(_currentId, id, StringComparison.Ordinal))
            {
                // Empty snapshots still need an accurate enabled/waiting state.
                if (id == null) State = InitialState();
                return;
            }
            _currentId = id;
            SelectionRevision++;
            ResetTiming();
            State = InitialState();
        }

        private BroadcastState InitialState() => !IsEnabled ? BroadcastState.Disabled :
            _currentId == null ? BroadcastState.WaitingForPlayers : BroadcastState.WaitingForTarget;

        private void ResetTiming()
        {
            _dwellElapsed = 0;
            _waitingElapsed = 0;
        }

        private static double PositiveFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name, "The duration must be finite and positive.");
            return value;
        }
    }
}
