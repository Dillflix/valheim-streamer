"""Real filesystem transactions and deterministic managed-session lifecycle tests."""
from copy import deepcopy
import errno
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
                                      require_publishing=True, poll_seconds=30, branch="main")
        self.s = sup.Supervisor(self.config, github=SimpleNamespace(candidate=lambda *_: None))
        self.s.state = {"schema": 1, "active": {"source_sha": "b" * 40, "run_number": 1, "version": "0.8.19"},
                        "quarantined": [], "paused": False, "restart_failures": 0}
        self.s.running_games = lambda: []
        self.s.stop = lambda: None
        self.launches = []
        self.s.launch = lambda version, source_sha=None, transaction_id=None: self.launches.append(version)
        self.s.heartbeat = lambda **_: {"observer_authorized": True, "observer_active": True, "publishing": True,
                                      "picture_live": True, "state": "broadcasting", "version": "0.8.20",
                                      "supervisor_protocol": 1, "source_sha": "a" * 40}
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

    def test_sandbox_update_with_pinned_plugin_directory_keeps_backup_and_identity(self):
        self.config.platform = "windows-sandboxie"
        original_move = sup.os.replace
        def pinned_root(src, dest):
            if self.plugin in (Path(src), Path(dest)):
                raise PermissionError("plugin directory is in use")
            return original_move(src, dest)
        identity = self.plugin.stat().st_ino
        with patch.object(sup.os, "replace", side_effect=pinned_root):
            self.s.update(self.candidate, self.tx)
        self.s.cleanup_staging(self.tx)
        self.assertEqual(self.plugin.stat().st_ino, identity)
        self.assertEqual(sup.inventory(self.plugin), self.new)
        self.assertEqual(sup.inventory(self.tx / "backup"), self.old)

    @unittest.skipUnless(os.name == "nt", "requires Windows directory sharing")
    def test_real_directory_handle_does_not_block_sandbox_update(self):
        self.config.platform = "windows-sandboxie"
        # Attribute-only opens do not participate in Windows share checks.
        # Request directory read access and omit FILE_SHARE_DELETE.
        handle = sup._kernel.CreateFileW(str(self.plugin), 0x80000000, 3, None, 3, 0x02000000, None)
        self.assertNotEqual(handle, sup.ctypes.c_void_p(-1).value)
        try:
            with self.assertRaises(PermissionError):
                os.replace(self.plugin, self.tx / "blocked-rename")
            self.s.update(self.candidate, self.tx)
        finally:
            sup._kernel.CloseHandle(handle)
        self.assertEqual(sup.inventory(self.plugin), self.new)

    def test_failed_dll_swap_restores_only_changed_files_and_can_retry_same_build(self):
        self.config.platform = "windows-sandboxie"
        broken = self.plugin / "ValheimBroadcaster.dll"
        rename = sup.os.replace
        def locked(src, dst):
            if Path(dst) == broken:
                self.assertNotEqual(sup.digest(self.plugin / "ValheimBroadcaster.Core.dll"),
                                    self.old["ValheimBroadcaster.Core.dll"]["sha256"])
                raise PermissionError(errno.EACCES, "secret fixture path")
            return rename(src, dst)
        with patch.object(sup.os, "replace", side_effect=locked):
            with self.assertRaisesRegex(sup.Failure, "ValheimBroadcaster.dll"):
                self.s.update(self.candidate, self.tx)
        self.assertNotIn("secret fixture path", str(self.s.state["last_update_failure"]))
        self.assertEqual(self.s.state["quarantined"], [])
        self.assertEqual(self.launches, [])
        self.assertFalse(self.s.journal_path.exists())
        self.assertEqual(sup.inventory(self.plugin), self.old)

    def test_crash_mid_file_update_cleans_partial_copy_and_reconciles_mixed_build(self):
        journal = self.journal()
        journal["method"] = "files"
        sup.atomic_json(self.s.journal_path, journal)
        new_name = "ThirdParty/Cinemachine/license.txt"
        # Simulate an older installation without this notice and a partially
        # applied update which has added it and changed one existing DLL.
        del journal["before"][new_name]
        (self.tx / "backup" / new_name).unlink()
        sup.atomic_json(self.s.journal_path, journal)
        for name in (new_name, "ValheimBroadcaster.dll"):
            (self.plugin / name).write_bytes((self.tx / "new" / name).read_bytes())
        (self.plugin / (".vb-" + self.tx.name + ".tmp")).write_bytes(b"partial")
        self.s.recover_transaction()
        self.assertEqual(sup.inventory(self.plugin), journal["before"])
        self.assertFalse(self.s.journal_path.exists())
        self.assertEqual(self.s.state["quarantined"], [])

    def test_legacy_prepared_journal_verifies_untouched_install_without_directory_move(self):
        self.journal()
        rename = sup.os.replace
        def blocked(src, dst):
            if Path(src) == self.plugin:
                raise PermissionError("pinned directory")
            return rename(src, dst)
        with patch.object(sup.os, "replace", side_effect=blocked):
            self.s.recover_transaction()
        self.assertFalse(self.s.journal_path.exists())
        self.assertEqual(self.s.state["quarantined"], [])

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

    def test_recovery_picture_defers_update_without_stopping_a_valid_session(self):
        stops = []
        self.s.stop = lambda: stops.append(True)
        self.s.heartbeat = lambda **_: {"state": "recovering", "picture_live": False,
                                      "observer_authorized": True, "observer_active": True, "publishing": True}
        with self.assertRaises(sup.Failure):
            self.s.update(self.candidate, self.tx)
        self.assertEqual(stops, [])
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertFalse(self.s.journal_path.exists())

    def test_failed_backup_flush_cleans_uninstalled_staging_without_stopping_game(self):
        stops = []
        self.s.stop = lambda: stops.append(True)
        with patch.object(sup, "flush_tree", side_effect=OSError("injected backup flush failure")):
            with self.assertRaises(OSError):
                self.s.update(self.candidate, self.tx)
        self.assertFalse(self.s.journal_path.exists())
        self.assertTrue((self.tx / "backup").exists())
        self.s.cleanup_staging(self.tx)
        self.assertFalse(self.tx.exists())
        self.assertEqual(stops, [])
        self.assertEqual(sup.inventory(self.plugin), self.old)

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

    def test_failed_rollback_blocks_further_launch_or_update_and_preserves_journal(self):
        def failed_health():
            (self.tx / "backup/ValheimBroadcaster.dll").write_bytes(b"corrupt")
            raise sup.HealthFailure("candidate failed")
        self.s.wait_healthy = failed_health
        with self.assertRaises(sup.RecoveryRequired):
            self.s.update(self.candidate, self.tx)
        self.assertEqual(sup.inventory(self.plugin), self.new)
        self.assertTrue(self.s.journal_path.exists())
        self.assertTrue(self.s.state["paused"])
        self.assertEqual(self.s.state["status"], "recovery_required")
        self.assertIn("verify candidate startup: candidate failed", self.s.state["last_update_failure"]["update"])
        self.assertIn("verify rollback backup", self.s.state["last_update_failure"]["rollback"])
        saved = self.s.journal_path.read_bytes()
        with self.assertRaises(sup.RecoveryRequired):
            sup.Supervisor.launch(self.s, "0.8.19", "b" * 40)
        with self.assertRaises(sup.RecoveryRequired):
            self.s.update(self.candidate, self.tx)
        self.assertEqual(self.s.journal_path.read_bytes(), saved)
        self.assertEqual(self.launches, ["0.8.20"])

    def test_pending_transaction_cannot_be_used_for_an_unrelated_launch(self):
        self.journal("installed")
        for version, source, txid in (("0.8.20", "a" * 40, "b" * 32),
                                     ("0.8.19", "a" * 40, self.tx.name),
                                     ("0.8.20", "b" * 40, self.tx.name)):
            with self.assertRaises(sup.RecoveryRequired):
                sup.Supervisor.launch(self.s, version, source, txid)
        self.assertEqual(self.launches, [])

    def test_rename_failure_after_retirement_restores_exact_payload(self):
        original = sup.os.replace
        def replace(source, destination):
            if Path(source) == self.tx / "new":
                raise PermissionError("injected candidate rename failure")
            return original(source, destination)
        with patch.object(sup.os, "replace", side_effect=replace):
            with self.assertRaises(PermissionError):
                self.s.update(self.candidate, self.tx)
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertFalse(self.s.journal_path.exists())
        self.assertEqual(self.launches, [])

    def test_failed_restore_rename_retains_recoverable_journal(self):
        self.s.wait_healthy = lambda: (_ for _ in ()).throw(sup.HealthFailure("candidate failed"))
        original = sup.os.replace
        def replace(source, destination):
            if Path(source).name.startswith("restore-"):
                raise PermissionError("injected rollback rename failure")
            return original(source, destination)
        with patch.object(sup.os, "replace", side_effect=replace):
            with self.assertRaises(sup.RecoveryRequired):
                self.s.update(self.candidate, self.tx)
        self.assertFalse(self.plugin.exists())
        self.assertTrue(self.s.journal_path.exists())
        self.s.recover_transaction()
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertFalse(self.s.journal_path.exists())

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
                       "supervisor_protocol": 1, "source_sha": "b" * 40,
                       "observer_authorized": True, "observer_active": True, "publishing": True}

    def write(self):
        sup.atomic_json(self.s.control_dir() / "status.json", self.status)

    def test_denied_live_json_retries_and_recovers_for_both_inputs(self):
        self.write()
        for filename in ("status.json", "operator.json"):
            path = self.s.control_dir() / filename if filename == "status.json" else self.state / filename
            if filename == "operator.json":
                sup.atomic_json(path, {"command": "pause"})
            for number in (None, 5, 32, 33):
                with self.subTest(filename=filename, winerror=number):
                    error = PermissionError(errno.EACCES, "denied")
                    if number is not None:
                        error.winerror = number
                    with patch.object(sup, "open_live_json", side_effect=error), patch.object(sup.time, "sleep") as sleep:
                        if filename == "status.json":
                            self.assertIsNone(self.s.heartbeat())
                            self.assertIn(filename, self.s.status_read_error)
                        else:
                            self.assertFalse(self.s.observe_pause())
                            self.assertIn(filename, self.s.operator_read_error)
                            with self.assertRaises(sup.LiveReadUnavailable):
                                self.s.requested_pause()
                            self.assertTrue(path.exists())
                        self.assertGreaterEqual(sleep.call_count, 3)
            if filename == "status.json":
                self.assertEqual(self.s.heartbeat(), self.status)
                self.assertIsNone(self.s.status_read_error)
            else:
                self.assertTrue(self.s.observe_pause())
                self.assertIsNone(self.s.operator_read_error)
                self.assertFalse(path.exists())

    def test_startup_accepts_fresh_output_after_plain_eacces_so_game_can_keep_running(self):
        self.status.update(startup_phase="authorized", picture_live=True)
        self.write()
        clock = [100.0]
        path = self.s.control_dir() / "status.json"
        real_open = sup.open_live_json
        def open_snapshot(file):
            if file == path and 103 <= clock[0] < 105:
                raise PermissionError(errno.EACCES, "access denied")
            return real_open(file)
        def advance(seconds):
            clock[0] += seconds
            self.status["sequence"] += 1
            self.write()
        with patch.object(sup, "open_live_json", side_effect=open_snapshot), \
             patch.object(sup.time, "monotonic", side_effect=lambda: clock[0]), \
             patch.object(sup.time, "sleep", side_effect=advance):
            self.config.stable_seconds = 6
            result = self.s.wait_healthy()
        self.assertTrue(result["picture_live"])
        self.assertGreaterEqual(clock[0], 106)
        self.assertLess(clock[0], 110)

    def test_publishing_baseline_survives_deadline_and_verifies_first_live_picture(self):
        self.config.health_timeout_seconds, self.config.stable_seconds = 240, 20
        clock = [0.]
        def advance(seconds):
            clock[0] += seconds
        def sample(**_):
            return dict(self.status, startup_phase="authorized", picture_live=clock[0] >= 255)
        with patch.object(self.s, "heartbeat", side_effect=sample), \
             patch.object(sup.time, "monotonic", side_effect=lambda: clock[0]), \
             patch.object(sup.time, "sleep", side_effect=advance):
            self.assertTrue(self.s.wait_healthy()["picture_live"])
        self.assertEqual(clock[0], 255)

    def test_update_candidate_requires_live_render_by_its_deadline(self):
        self.s.record["transaction_id"] = "e" * 32
        self.config.health_timeout_seconds, self.config.stable_seconds = 240, 20
        clock = [0.]
        def advance(seconds):
            clock[0] += seconds
        with patch.object(self.s, "heartbeat", return_value=dict(self.status, picture_live=False)), \
             patch.object(sup.time, "monotonic", side_effect=lambda: clock[0]), \
             patch.object(sup.time, "sleep", side_effect=advance):
            with self.assertRaisesRegex(sup.HealthFailure, "health deadline.*picture_live=False"):
                self.s.wait_healthy()
        self.assertEqual(clock[0], 240)

    def test_unreadable_status_never_satisfies_health(self):
        self.status.update(startup_phase="authorized", picture_live=True)
        self.write()
        clock = [100.0]
        real_open = sup.open_live_json
        def open_snapshot(file):
            if file.name == "status.json" and clock[0] > 100:
                raise PermissionError(errno.EACCES, "access denied")
            return real_open(file)
        def advance(seconds):
            clock[0] += seconds
        with patch.object(sup, "open_live_json", side_effect=open_snapshot), \
             patch.object(sup.time, "monotonic", side_effect=lambda: clock[0]), \
             patch.object(sup.time, "sleep", side_effect=advance):
            with self.assertRaisesRegex(sup.HealthFailure, "health deadline.*status.json"):
                self.s.wait_healthy()
        self.assertGreaterEqual(clock[0], 100 + self.config.health_timeout_seconds)

    @unittest.skipUnless(os.name == "nt", "requires real Windows file sharing")
    def test_windows_snapshot_shares_delete_access_and_recovers_after_exclusive_lock(self):
        path = self.s.control_dir() / "status.json"
        self.write()
        with sup.open_live_json(path) as old:
            # FILE_SHARE_DELETE permits a rename of the opened generation.
            # MoveFileExW can still reject replacing an open destination.
            path.rename(path.with_suffix(".old"))
            sup.atomic_json(path, {"new": True})
            self.assertEqual(json.load(old), self.status)
            self.assertEqual(sup.read_live_json(path)[0], {"new": True})
        handle = sup._kernel.CreateFileW(str(path), 0x80000000, 0, None, 3, 0, None)
        self.assertNotEqual(handle, sup.ctypes.c_void_p(-1).value)
        try:
            with self.assertRaisesRegex(sup.LiveReadUnavailable, "status.json"):
                sup.read_live_json(path)
        finally:
            sup._kernel.CloseHandle(handle)
        self.assertEqual(sup.read_live_json(path)[0], {"new": True})

    @unittest.skipUnless(os.name == "nt", "requires real Windows file sharing")
    def test_windows_host_can_publish_over_a_briefly_locked_destination(self):
        import threading
        path = self.state / "operator.json"
        sup.atomic_json(path, {"command": "pause"})
        with path.open("rb") as held:
            release = threading.Timer(0.04, held.close)
            release.start()
            try:
                sup.atomic_json(path, {"command": "resume"})
            finally:
                release.join()
        self.assertEqual(sup.read_live_json(path)[0], {"command": "resume"})

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

    def test_missing_protocol_or_source_cannot_establish_update_baseline(self):
        for key in ("supervisor_protocol", "source_sha"):
            value = self.status.pop(key)
            self.write()
            self.assertIsNone(self.s.heartbeat())
            self.status[key] = value
        self.s.record["source_sha"] = "a" * 40
        self.write()
        self.assertIsNone(self.s.heartbeat())

    def test_all_proven_live_encoders_survive_empty_shutdown_report(self):
        owned = {pid: {"pid": pid, "created": pid + 100} for pid in (40, 41, 42)}
        control = self.s.control_dir()
        self.s.control_dir = lambda: control
        self.s.adapter = SimpleNamespace(
            same_process=lambda record: owned.get(record["pid"]) == record["identity"],
            encoder_identity=lambda pid, _: owned.get(pid))
        self.status["encoder_pids"] = [40, 41, 42]
        self.write()
        self.assertIsNotNone(self.s.heartbeat())
        self.assertEqual(self.s.record["encoder_identities"], list(owned.values()))
        self.status.update(encoder_pids=[], sequence=2)
        self.write()
        self.assertIsNotNone(self.s.heartbeat())
        self.assertEqual(self.s.record["encoder_identities"], list(owned.values()))
        del owned[41]
        self.status.update(encoder_pids=[999], sequence=3)
        self.write()
        self.assertIsNotNone(self.s.heartbeat())
        self.assertEqual([identity["pid"] for identity in self.s.record["encoder_identities"]], [40, 42])

    def test_forced_cleanup_terminates_all_and_only_recorded_encoder_identities(self):
        self.s.is_running = lambda: False
        del self.s.stop
        closed, terminated = [], []
        class Child:
            def __init__(child, pid): child.pid, child.alive = pid, True
            def poll(child): return None if child.alive else 0
            def terminate(child): terminated.append(child.pid); child.alive = False
            def wait(child, timeout=None): return 0
            def close(child): closed.append(child.pid)
        self.s.record["encoder_identities"] = [{"pid": pid, "created": pid + 100} for pid in (40, 41, 42)]
        self.s.adapter = SimpleNamespace(attach=lambda record: None if record["pid"] == 1234 else Child(record["pid"]))
        self.s.stop()
        self.assertEqual(terminated, [40, 41, 42])
        self.assertEqual(closed, [40, 41, 42])
        self.assertIsNone(self.s.record)

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

    def test_failure_evidence_distinguishes_live_game_from_exit_before_stop(self):
        self.s.record = {"session_id": "e" * 32}
        self.s.startup_phase = "authorized"
        (self.game / "BepInEx/LogOutput.log").write_text("on-air before stop")
        error = PermissionError(13, "secret-argument")
        for code, expected in [(None, "running before supervisor cleanup"), (3221225477, "0xC0000005")]:
            self.s.process = SimpleNamespace(poll=lambda: code)
            message = self.s.capture_startup_failure(error, 2)
            self.assertIn(expected, message)
            self.assertIn("authorized", message)
            self.assertIn("PermissionError", message)
            self.assertNotIn("secret-argument", message)
            self.assertEqual(message, self.s.state["last_startup_failure"]["message"])
            self.assertEqual("on-air before stop", (self.state / "startup-failure-2.log").read_text())

    def test_manual_baseline_adopts_only_after_stable_health(self):
        new = {"version": "0.8.20", "source_sha": "a" * 40, "run_number": 0}
        old = dict(self.s.state["active"])
        self.s.installed_baseline = lambda path: dict(new)
        def launch(version, source_sha=None):
            self.assertEqual((version, source_sha), (new["version"], new["source_sha"]))
            self.s.record = {"pid": 123}
        self.s.launch = launch
        def ready():
            self.assertEqual(self.s.state["active"], old)
            return self.s.heartbeat()
        self.s.wait_healthy = ready
        with patch.object(sup.time, "sleep", side_effect=lambda _: setattr(self.s, "stopping", True)):
            self.s.run(self.root / "release-manifest.json")
        self.assertEqual(self.s.state["active"], new)
        self.assertEqual(self.s.state["last_update_check"]["result"], "no_eligible_build")

    def test_short_healthy_restart_does_not_reset_crash_budget(self):
        self.s.state["restart_failures"] = 2
        def launched(version, source_sha=None, transaction_id=None):
            self.launches.append(version)
            self.s.record = {"pid": 1234}
        self.s.launch = launched
        with patch.object(sup.time, "sleep", side_effect=lambda _: setattr(self.s, "stopping", True)):
            self.s.run()
        self.assertEqual(self.s.state["restart_failures"], 2)


if __name__ == "__main__":
    unittest.main()
