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
        private readonly PlayerCoverageHistory _coverage = new PlayerCoverageHistory();
        private readonly PlayerActivitySelection _activity = new PlayerActivitySelection();
        private readonly PlayerPictureRecovery _pictureRecovery = new PlayerPictureRecovery();
        private readonly List<string> _activityRoster = new List<string>(128);
        private double _coverageNow;
        private bool _coverageRecorded;

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
        public double ReadyDwellSeconds => _dwellElapsed;
        public string ActivityReason => _activity.Reason;

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
            if (_roster.Count == 0) _coverage.Reset();
            if (_currentId != null && unique.ContainsKey(_currentId)) return;

            PlayerTarget? next = _roster.FirstOrDefault(player =>
                _currentId == null || StringComparer.Ordinal.Compare(player.Id, _currentId) > 0);
            ChangeSelection((next ?? _roster.FirstOrDefault())?.Id);
        }

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _coverage.Reset(); _activity.Reset(); _pictureRecovery.Reset(); _coverageNow = 0; _coverageRecorded = false;
            ResetTiming();
            State = InitialState();
        }

        /// <summary>Pauses both automatic dwell cycling and unavailable-target skipping.</summary>
        public void SetHolding(bool holding) { if (holding != IsHolding) { _activity.ResetRetention(); _pictureRecovery.ResetCurrent(); } IsHolding = holding; }

        public bool Select(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (!_roster.Any(player => player.Id == id)) return false;
            ChangeSelection(id);
            return true;
        }

        public bool Next() => Move(1);
        public bool Previous() => Move(-1);

        /// <summary>Report readable, rendered players, including companions, before Tick.</summary>
        public void RecordCoverage(double now, double dt, IReadOnlyList<string> visibleIds)
        {
            if (!IsEnabled) return;
            _coverage.Record(now, dt, visibleIds);
            foreach (string id in visibleIds) if (_coverage.RecentlyCovered(id, now, 1)) _activity.MarkCovered(id, now);
            _coverageNow = now; _coverageRecorded = true;
        }

        /// <summary>
        /// Advances by a finite nonnegative interval. At most one target switch occurs per
        /// call; excess time is discarded so a stalled frame cannot skip an entire roster.
        /// Losing readiness resets the dwell period. A lone unavailable target is retained.
        /// </summary>
        public void Tick(double deltaSeconds, bool currentTargetReady, IReadOnlyList<PlayerActivityEvidence>? activity = null, bool usablePlayerFootage = true, IReadOnlyList<string>? locallyReadyIds = null)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Elapsed time must be finite and nonnegative.");
            if (!IsEnabled) return;
            // A reported absolute timestamp already includes this frame's dt.
            // Only extrapolate when the adapter supplied no observation at all.
            if (!_coverageRecorded) _coverageNow = Math.Min(double.MaxValue, _coverageNow + deltaSeconds);
            _coverageRecorded = false;
            _activityRoster.Clear();
            foreach (PlayerTarget player in _roster) if (_activityRoster.Count < 128) _activityRoster.Add(player.Id);
            _activity.Update(_coverageNow, _activityRoster, activity);
            if (_currentId == null)
            {
                State = BroadcastState.WaitingForPlayers;
                return;
            }

            if (!currentTargetReady)
            {
                State = BroadcastState.WaitingForTarget;
                _pictureRecovery.Observe(_currentId, _coverageNow, false, false);
                _dwellElapsed = 0;
                if (IsHolding) return;

                _waitingElapsed = Math.Min(TargetTimeoutSeconds, _waitingElapsed + deltaSeconds);
                if (_waitingElapsed >= TargetTimeoutSeconds && _roster.Count > 1) Next();
                return;
            }

            State = BroadcastState.Following;
            _waitingElapsed = 0;
            _pictureRecovery.Observe(_currentId, _coverageNow, true, usablePlayerFootage);
            if (IsHolding) return;
            if (!usablePlayerFootage)
            {
                if (_pictureRecovery.ShouldTryAlternative(_coverageNow) && locallyReadyIds != null)
                {
                    foreach (PlayerTarget candidate in _roster)
                    {
                        if (candidate.Id == _currentId || _pictureRecovery.OnCooldown(candidate.Id, _coverageNow)) continue;
                        bool ready = false;
                        foreach (string id in locallyReadyIds) if (id == candidate.Id) { ready = true; break; }
                        if (!ready) continue;
                        _pictureRecovery.LeftUnusable(_currentId, _coverageNow); ChangeSelection(candidate.Id); break;
                    }
                }
                return;
            }

            _dwellElapsed = Math.Min(DwellSeconds, _dwellElapsed + deltaSeconds);
            if (_dwellElapsed >= DwellSeconds && _roster.Count > 1) MoveUncovered();
        }

        private void MoveUncovered()
        {
            int index = -1;
            for (int i = 0; i < _roster.Count; i++) if (_roster[i].Id == _currentId) { index = i; break; }
            PlayerTarget? chosen = null;
            double best = double.NegativeInfinity;
            long scene = _activity.SceneId(_currentId, _coverageNow);
            for (int step = 1; step < _roster.Count; step++)
            {
                PlayerTarget candidate = _roster[(index + step + _roster.Count) % _roster.Count];
                if (_coverage.RecentlyCovered(candidate.Id, _coverageNow, DwellSeconds) || _pictureRecovery.OnCooldown(candidate.Id, _coverageNow)) continue;
                // Ordinary mode does not expose readiness for other subjects. Do
                // not claim they are loaded to gain a scheduling bonus.
                double rank = _activity.Rank(candidate.Id, _coverageNow, false, scene);
                if (chosen == null || rank > best + .0001) { chosen = candidate; best = rank; }
            }
            if (chosen != null && (_currentId == null || !_activity.RetainEncounter(_currentId, _coverageNow, true,
                _activity.Overdue(chosen.Id, _coverageNow)))) ChangeSelection(chosen.Id);
            // Sustained readable group coverage can preserve this scene while
            // the shot director shifts emphasis without restarting selection.
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
            _activity.ResetRetention(); _pictureRecovery.ResetCurrent();
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
