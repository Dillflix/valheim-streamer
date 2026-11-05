"""Isolated real-process update/rollback exercises; these do not run Valheim.

The fixture executable is a private copy of Python which writes the supervisor
bridge protocol. The production Linux process, archive, transaction and health
implementations are used. Windows/Sandboxie launch and game/Steam/RTMP behavior
remain separate host acceptance checks; no existing installation is touched.
"""
from dataclasses import replace
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import supervisor as sup


OLD_SHA = "b" * 40
NEW_SHA = "a" * 40

# Deliberately fake DLL contents communicate fixture behavior to a separate
# process. They are hashed/installed as opaque bytes, never loaded as assemblies.
CLIENT = r'''
import hashlib, json, os, pathlib, time
root = pathlib.Path.cwd()
plugin = root / "BepInEx/plugins/ValheimBroadcaster/ValheimBroadcaster.dll"
data = plugin.read_bytes()
build = json.loads(data)
control = pathlib.Path(os.environ["VB_CONTROL_DIR"])
session = os.environ["VB_SESSION_ID"]
sequence = 0
def publish(state, reason):
    global sequence
    sequence += 1
    status = {"schema": 1, "supervisor_protocol": 1,
              "session_id": session, "pid": os.getpid(),
              "version": build["version"], "source_sha": build["source_sha"],
              "plugin_sha256": hashlib.sha256(data).hexdigest(),
              "sequence": sequence, "encoder_pid": 0, "state": state,
              "observer_authorized": state == "broadcasting",
              "observer_active": state == "broadcasting",
              "picture_live": state == "broadcasting", "publishing": state == "broadcasting",
              "reason": reason, "failure_category": "none",
              "github_token_inherited": bool(os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN"))}
    temporary = control / "status-fixture.tmp"
    temporary.write_text(json.dumps(status))
    os.replace(temporary, control / "status.json")
while True:
    command = control / "command.json"
    if command.exists():
        value = json.loads(command.read_text())
        if value.get("session_id") == session and value.get("command") == "shutdown":
            publish("stopped", "supervisor_shutdown")
            break
    publish("blocked" if build.get("blocked") else "broadcasting", "fixture")
    time.sleep(.025)
'''


def write_payload(directory, version, source_sha, blocked=False):
    for name in sup.OWNED_FILES:
        path = directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        content = {"file": name, "version": version, "source_sha": source_sha, "blocked": blocked}
        path.write_bytes(json.dumps(content, sort_keys=True).encode())


class LocalArtifact(sup.GitHub):
    """Only the remote transport is replaced; digest checking stays real."""
    def __init__(self, config, archive):
        super().__init__(config)
        self.archive = archive

    def open(self, suffix):
        if not suffix.startswith("actions/artifacts/") or not suffix.endswith("/zip"):
            raise AssertionError("Unexpected fixture transport request: " + suffix)
        return self.archive.open("rb")

    def json(self, suffix):
        if suffix.startswith("compare/"):
            return {"status": "ahead", "ahead_by": 1, "behind_by": 0,
                    "base_commit": {"sha": OLD_SHA}, "merge_base_commit": {"sha": OLD_SHA}}
        raise AssertionError("Unexpected fixture metadata request: " + suffix)


@unittest.skipUnless(sys.platform == "linux", "Real Linux process adapter fixture; Sandboxie runtime is not simulated")
class RealProcessLifecycleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Some managed development containers virtualize process syscalls but
        # expose the host's /proc. That is not a valid Linux identity surface:
        # never signal its unrelated host PIDs to make this fixture pass.
        observed = Path("/proc/self/stat").read_text().split(" ", 1)[0]
        if observed != str(os.getpid()):
            raise unittest.SkipTest("Process PID namespace does not match /proc; run the real-process cases in Linux CI")

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="broadcaster-updater-process-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.game = self.root / "game"
        self.game.mkdir()
        self.executable = self.game / "fake-game"
        shutil.copy2(sys.executable, self.executable)
        self.script = self.game / "bridge_fixture.py"
        self.script.write_text(CLIENT)
        self.plugin = self.game / "BepInEx/plugins/ValheimBroadcaster"
        write_payload(self.plugin, "0.8.48", OLD_SHA)
        self.old = sup.inventory(self.plugin)
        self.keep = [self.game / "BepInEx/config/keep.cfg", self.game / "save-fixture.fch",
                     self.plugin.parent / "OtherMod.dll"]
        for path in self.keep:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"unrelated-existing-data")
        self.environment = patch.dict(os.environ, {"PYTHONHOME": sys.base_prefix,
                                                   "GH_TOKEN": "fixture-host-only",
                                                   "GITHUB_TOKEN": "fixture-host-only"})
        self.environment.start()
        self.addCleanup(self.environment.stop)
        self.config = sup.Config(repository="Dillflix/valheim-streamer", workflow="ci.yml", branch="main",
                                 game_dir=self.game, game_executable=self.executable, plugin_dir=self.plugin,
                                 state_dir=self.root / "state", launch_command=[str(self.executable), str(self.script)],
                                 platform="linux", stable_seconds=5, health_timeout_seconds=30,
                                 shutdown_timeout_seconds=5)
        self.s = sup.Supervisor(self.config)
        self.s.setup()  # Exercise normal path/config validation first.
        # Keep the same real clock and health logic, with shorter fixture-only
        # deadlines after the production configuration has been validated.
        self.s.config = replace(self.config, stable_seconds=.05, health_timeout_seconds=4,
                                shutdown_timeout_seconds=.5)
        self.addCleanup(self.s.stop)
        other_executable = self.root / "other-game" / self.executable.name
        other_executable.parent.mkdir()
        shutil.copy2(sys.executable, other_executable)
        self.other = subprocess.Popen([str(other_executable), "-c", "import time; time.sleep(60)"],
                                      stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.addCleanup(self.stop_other)

    def stop_other(self):
        if self.other.poll() is None:
            self.other.terminate()
        self.other.wait(timeout=5)

    def baseline(self):
        self.s.launch("0.8.48", OLD_SHA)
        status = self.s.wait_healthy()
        self.assertFalse(status["github_token_inherited"])
        self.s.state["active"] = {"source_sha": OLD_SHA, "run_number": 1, "version": "0.8.48"}
        self.s.save()
        return self.s.process, self.s.record["session_id"]

    def candidate(self, blocked=False):
        payload = self.root / "candidate"
        write_payload(payload, "0.8.49", NEW_SHA, blocked)
        files = [{"path": name, "size": (payload / name).stat().st_size,
                  "sha256": sup.digest(payload / name)} for name in sorted(sup.OWNED_FILES)]
        manifest = {"schema": 1, "version": "0.8.49", "source_sha": NEW_SHA,
                    "observer_protocol": 3, "supervisor_protocol": 1, "files": files}
        archive = self.root / "candidate.zip"
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
            bundle.writestr("release-manifest.json", json.dumps(manifest))
            for name in sorted(sup.OWNED_FILES):
                bundle.write(payload / name, "ValheimBroadcaster/" + name)
        candidate = {"source_sha": NEW_SHA, "run_id": 2, "run_number": 2, "artifact_id": 3,
                     "archive_size": archive.stat().st_size, "sha256": sup.digest(archive)}
        self.s.github = LocalArtifact(self.s.config, archive)
        return self.s.prepare(candidate)

    def assert_unrelated_preserved(self):
        self.assertIsNone(self.other.poll(), "An unrelated real process was terminated")
        for path in self.keep:
            self.assertEqual(path.read_bytes(), b"unrelated-existing-data")

    def test_real_process_update_stages_before_shutdown_and_binds_new_session(self):
        old_process, old_session = self.baseline()
        candidate, transaction = self.candidate()
        self.assertIsNone(old_process.poll(), "Staging interrupted the running baseline")
        self.assertEqual(sup.inventory(self.plugin), self.old)
        staged = sup.inventory(transaction / "new")
        self.s.update(candidate, transaction)
        self.assertIsNotNone(old_process.poll(), "Old process survived payload replacement")
        self.assertNotEqual(self.s.record["session_id"], old_session)
        self.assertNotEqual(self.s.record["pid"], old_process.pid)
        self.assertEqual(sup.inventory(self.plugin), staged)
        self.assertEqual(sup.inventory(transaction / "backup"), self.old)
        self.assertEqual(self.s.heartbeat()["source_sha"], NEW_SHA)
        self.assertEqual(self.s.state["active"], candidate)
        self.assertFalse(self.s.journal_path.exists())
        self.assert_unrelated_preserved()
        active = self.s.process
        self.s.stop()
        self.assertEqual(active.returncode, 0, "Fixture did not acknowledge graceful shutdown")
        self.assertFalse((self.config.state_dir / "process.json").exists())
        self.assert_unrelated_preserved()

    def test_real_blocked_candidate_is_stopped_restored_and_previous_build_rejoins(self):
        self.baseline()
        candidate, transaction = self.candidate(blocked=True)
        with self.assertRaises(sup.TerminalBlock):
            self.s.update(candidate, transaction)
        self.assertFalse(self.s.running_games(), "Rejected candidate remained alive after rollback")
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertIn(NEW_SHA, self.s.state["quarantined"])
        self.assertEqual(self.s.state["active"]["source_sha"], OLD_SHA)
        self.assertFalse(self.s.journal_path.exists())
        self.s.launch("0.8.48", OLD_SHA)
        self.assertEqual(self.s.wait_healthy()["source_sha"], OLD_SHA)
        self.assert_unrelated_preserved()

    def test_new_supervisor_recovers_installed_journal_and_stops_recorded_real_child(self):
        self.baseline()
        candidate, transaction = self.candidate()
        self.s.stop()
        shutil.copytree(self.plugin, transaction / "backup")
        after = sup.inventory(transaction / "new")
        journal = {"schema": 1, "transaction_id": transaction.name, "phase": "installed",
                   "before": self.old, "after": after, "previous": self.s.state["active"], "candidate": candidate}
        sup.atomic_json(self.s.journal_path, journal)
        os.replace(self.plugin, transaction / "retired")
        os.replace(transaction / "new", self.plugin)
        self.s.launch("0.8.49", NEW_SHA, transaction_id=transaction.name)
        child = self.s.process
        self.s.wait_healthy()
        recovered = sup.Supervisor(self.config)
        recovered.setup()  # Reads the real child's persisted ownership record.
        recovered.config = self.s.config
        self.addCleanup(recovered.stop)
        recovered.recover_transaction()
        child.wait(timeout=5)
        self.assertEqual(child.returncode, 0)
        self.assertFalse(recovered.running_games())
        self.assertEqual(sup.inventory(self.plugin), self.old)
        self.assertFalse(recovered.journal_path.exists())
        recovered.launch("0.8.48", OLD_SHA)
        self.assertEqual(recovered.wait_healthy()["source_sha"], OLD_SHA)
        self.assert_unrelated_preserved()


if __name__ == "__main__":
    unittest.main()
