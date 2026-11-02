"""Real filesystem transactions and deterministic managed-session lifecycle tests."""
from copy import deepcopy
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import supervisor as sup


def payload(path, marker):
    path.mkdir(parents=True, exist_ok=True)
    for name in sup.OWNED_FILES:
        target = path / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((marker + name).encode())


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.game = self.root / "game"
        self.plugin = self.game / "BepInEx/plugins/ValheimBroadcaster"
        self.state = self.root / "state"
        self.transactions = self.game / "BepInEx/.broadcaster-updates"
        for directory in (self.state, self.transactions):
            sup.private_dir(directory)
        payload(self.plugin, "old-")
        self.config = SimpleNamespace(plugin_dir=self.plugin, state_dir=self.state, transactions=self.transactions,
                                      game_dir=self.game, game_executable=self.game / "valheim.exe",
                                      platform="linux", launch_command=[str(self.game / "valheim.exe")],
                                      shutdown_timeout_seconds=5, health_timeout_seconds=30, stable_seconds=5,
                                      require_publishing=True, poll_seconds=30)
        self.s = sup.Supervisor(self.config, github=SimpleNamespace(candidate=lambda *_: None))
        self.s.state = {"schema": 1, "active": {"source_sha": "b" * 40, "run_number": 1, "version": "0.8.19"},
                        "quarantined": [], "paused": False, "restart_failures": 0}
        self.s.running_games = lambda: []
        self.s.stop = lambda: None
        self.launches = []
        self.s.launch = lambda version: self.launches.append(version)
        self.s.heartbeat = lambda **_: {"observer_authorized": True, "observer_active": True, "publishing": True,
                                      "picture_live": True, "state": "broadcasting", "version": "0.8.20"}
        self.s.wait_healthy = lambda: self.s.heartbeat()
        self.tx = self.transactions / ("a" * 32)
        sup.private_dir(self.tx)
        payload(self.tx / "new", "new-")
        self.candidate = {"source_sha": "a" * 40, "run_number": 2, "version": "0.8.20"}
        self.old = sup.inventory(self.plugin)
        self.new = sup.inventory(self.tx / "new")


class TransactionTests(Fixture):
    def test_success_verifies_payload_and_preserves_unrelated_configuration(self):
        config = self.game / "BepInEx/config/settings.cfg"
        config.parent.mkdir()
        config.write_text("private-existing-values")
        other = self.plugin.parent / "OtherMod.dll"
        other.write_bytes(b"untouched")
        self.s.update(self.candidate, self.tx)
        self.assertEqual(sup.inventory(self.plugin), self.new)
        self.assertEqual(sup.inventory(self.tx / "backup"), self.old)
        self.assertEqual(config.read_text(), "private-existing-values")
        self.assertEqual(other.read_bytes(), b"untouched")
        self.assertEqual(self.s.state["active"], self.candidate)
        self.assertFalse(self.s.journal_path.exists())
        self.assertEqual(self.launches, ["0.8.20"])

    def test_failed_health_restores_verified_prior_bytes_and_quarantines_source(self):
        self.s.wait_healthy = lambda: (_ for _ in ()).throw(sup.HealthFailure("fixture"))
        with self.assertRaises(sup.HealthFailure):
            self.s.update(self.candidate, self.tx)
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertIn(self.candidate["source_sha"], self.s.state["quarantined"])
        self.assertEqual(self.s.state["active"]["source_sha"], "b" * 40)
        self.assertTrue(list(self.tx.glob("rejected-*")))
        self.assertFalse(self.s.journal_path.exists())

    def test_manual_stop_during_health_restores_without_quarantining(self):
        self.s.wait_healthy = lambda: (_ for _ in ()).throw(sup.ManualStop("operator"))
        with self.assertRaises(sup.ManualStop):
            self.s.update(self.candidate, self.tx)
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertTrue(self.s.state["paused"])
        self.assertEqual(self.s.state["quarantined"], [])

    def test_pause_received_during_shutdown_prevents_install_and_launch(self):
        self.s.stop = lambda: self.s.state.update(paused=True)
        with self.assertRaises(sup.ManualStop):
            self.s.update(self.candidate, self.tx)
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertEqual(self.launches, [])
        self.assertEqual(self.s.state["quarantined"], [])

    def test_unowned_file_or_duplicate_fails_before_stop(self):
        stops = []
        self.s.stop = lambda: stops.append(True)
        (self.plugin / "user-settings.json").write_text("keep")
        with self.assertRaises(sup.Failure):
            self.s.update(self.candidate, self.tx)
        self.assertEqual(stops, [])
        self.assertEqual(self.launches, [])
        self.assertFalse(self.s.journal_path.exists())

    def test_health_loss_while_downloaded_does_not_interrupt_game(self):
        stops = []
        self.s.stop = lambda: stops.append(True)
        self.s.heartbeat = lambda **_: None
        with self.assertRaises(sup.Failure):
            self.s.update(self.candidate, self.tx)
        self.s.cleanup_staging(self.tx)
        self.assertFalse(self.tx.exists())
        self.assertEqual(stops, [])

    def journal(self, phase="prepared"):
        import shutil
        shutil.copytree(self.plugin, self.tx / "backup")
        journal = {"schema": 1, "transaction_id": self.tx.name, "phase": phase,
                   "before": self.old, "after": self.new, "previous": deepcopy(self.s.state["active"]),
                   "candidate": self.candidate}
        sup.atomic_json(self.s.journal_path, journal)
        return journal

    def test_crash_between_directory_renames_recovers_before_launch(self):
        self.journal()
        os.replace(self.plugin, self.tx / "retired")
        self.s.recover_transaction()
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertEqual(self.launches, [])
        self.assertFalse(self.s.journal_path.exists())

    def test_accepted_journal_completes_state_commit_without_rollback(self):
        self.journal("accepted")
        os.replace(self.plugin, self.tx / "retired")
        os.replace(self.tx / "new", self.plugin)
        self.s.recover_transaction()
        self.assertEqual(sup.inventory(self.plugin), self.new)
        self.assertEqual(self.s.state["active"], self.candidate)
        self.assertEqual(self.s.state["quarantined"], [])

    def test_corrupt_backup_cannot_replace_live_payload(self):
        self.journal()
        (self.tx / "backup/ValheimBroadcaster.dll").write_bytes(b"corrupt")
        with self.assertRaises(sup.Failure):
            self.s.recover_transaction()
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertTrue(self.s.journal_path.exists())

    def test_history_cleanup_never_deletes_pending_journal(self):
        self.journal()
        for i in range(6):
            (self.transactions / f"{i:032x}").mkdir()
        self.s.prune_history()
        self.assertTrue(self.tx.exists())
        self.assertEqual(len(list(self.transactions.iterdir())), 4)


class HeartbeatTests(Fixture):
    def setUp(self):
        super().setUp()
        del self.s.heartbeat
        del self.s.wait_healthy
        self.s.record = {"pid": 1234, "session_id": "f" * 32, "version": "0.8.19", "plugin_sha256": "d" * 64}
        self.s.is_running = lambda: True
        sup.private_dir(self.s.control_dir())
        self.status = {"schema": 1, "session_id": "f" * 32, "pid": 1234, "version": "0.8.19",
                       "plugin_sha256": "d" * 64, "sequence": 1, "state": "broadcasting", "reason": "none",
                       "observer_authorized": True, "observer_active": True, "publishing": True}

    def write(self):
        sup.atomic_json(self.s.control_dir() / "status.json", self.status)

    def test_fresh_matching_heartbeat_is_accepted(self):
        self.write()
        self.assertEqual(self.s.heartbeat(), self.status)

    def test_wrong_session_pid_version_or_actual_loaded_hash_is_rejected(self):
        for key, value in (("session_id", "e" * 32), ("pid", 4321), ("version", "0.8.18"), ("plugin_sha256", "c" * 64)):
            previous = self.status[key]
            self.status[key] = value
            self.write()
            self.assertIsNone(self.s.heartbeat())
            self.status[key] = previous

    def test_expired_or_replayed_sequence_cannot_keep_game_healthy(self):
        self.write()
        self.assertIsNotNone(self.s.heartbeat())
        self.s.last_progress -= 11
        self.assertIsNone(self.s.heartbeat())
        self.status["sequence"] = 2
        self.write()
        self.assertIsNotNone(self.s.heartbeat())

    def test_user_quit_between_polls_remains_authoritative_after_process_exit(self):
        self.s.is_running = lambda: False
        self.status.update(state="stopped", reason="operator_exit")
        self.write()
        self.assertIsNotNone(self.s.heartbeat(allow_final=True))
        self.assertIsNone(self.s.heartbeat())
        with self.assertRaises(sup.ManualStop):
            self.s.wait_healthy()

    def test_crash_cannot_reuse_stale_broadcasting_as_final_stop(self):
        self.s.is_running = lambda: False
        self.write()
        self.assertIsNone(self.s.heartbeat(allow_final=True))
        with self.assertRaises(sup.HealthFailure):
            self.s.wait_healthy()

    def test_supervisor_shutdown_status_does_not_become_operator_pause(self):
        self.s.is_running = lambda: False
        self.status.update(state="stopped", reason="supervisor_shutdown")
        self.write()
        self.assertIsNone(self.s.heartbeat(allow_final=True))

    def test_terminal_block_is_not_a_retryable_connection_failure(self):
        self.status.update(state="blocked", failure_category="authentication")
        self.write()
        with self.assertRaises(sup.TerminalBlock):
            self.s.wait_healthy()

    def test_final_operator_intent_survives_supervisor_downtime(self):
        self.s.is_running = lambda: False
        self.s.record["started_at"] = sup.time.time() - 7200
        self.status.update(state="stopped", reason="operator_exit")
        self.write()
        path = self.s.control_dir() / "status.json"
        os.utime(path, (sup.time.time() - 3600, sup.time.time() - 3600))
        self.assertIsNone(self.s.heartbeat(allow_final=True))
        self.assertTrue(self.s.final_operator_intent())
        self.status["session_id"] = "wrong"
        self.write()
        self.assertFalse(self.s.final_operator_intent())

    def test_first_baseline_manual_quit_does_not_require_a_known_version_yet(self):
        self.s.is_running = lambda: False
        self.s.record["version"] = None
        self.status.update(state="stopped", reason="operator_exit")
        self.write()
        self.assertTrue(self.s.final_operator_intent())


class LoopTests(Fixture):
    def setUp(self):
        super().setUp()
        self.s.setup = lambda: None
        self.s.preflight = lambda: None
        self.s.recover_transaction = lambda: None

    def test_terminal_configuration_block_pauses_after_one_attempt(self):
        self.s.wait_healthy = lambda: (_ for _ in ()).throw(sup.TerminalBlock("configuration"))
        original = self.s.report
        def report(state, message):
            original(state, message)
            if state == "blocked":
                self.s.stopping = True
        self.s.report = report
        self.s.run()
        self.assertEqual(len(self.launches), 1)
        self.assertTrue(self.s.state["paused"])

    def test_short_healthy_restart_does_not_reset_crash_budget(self):
        self.s.state["restart_failures"] = 2
        def launched(version):
            self.launches.append(version)
            self.s.record = {"pid": 1234}
        self.s.launch = launched
        with patch.object(sup.time, "sleep", side_effect=lambda _: setattr(self.s, "stopping", True)):
            self.s.run()
        self.assertEqual(self.s.state["restart_failures"], 2)


if __name__ == "__main__":
    unittest.main()
