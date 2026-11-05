"""Artifact provenance and archive tests; no network or game process is used."""
from copy import deepcopy
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.request
import warnings
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import supervisor as sup


REVISION = "a" * 40
OLDER_REVISION = "b" * 40
BASE_REVISION = "c" * 40


def config():
    return SimpleNamespace(repository="Dillflix/valheim-streamer", workflow="ci.yml",
                           branch="feature-unattended-client-updates", token_file=None)


def run(number=10, revision=REVISION):
    return {"id": 1000 + number, "run_number": number, "run_attempt": 1,
            "workflow_id": 42, "status": "completed", "conclusion": "success", "event": "push",
            "head_branch": config().branch, "head_repository": {"full_name": config().repository},
            "path": ".github/workflows/ci.yml", "head_sha": revision}


def artifact(workflow_run=None):
    workflow_run = workflow_run or run()
    return {"id": workflow_run["id"] + 10000,
            "name": "valheim-broadcaster-" + workflow_run["head_sha"], "expired": False,
            "size_in_bytes": 128, "digest": "sha256:" + "d" * 64,
            "workflow_run": {key: workflow_run[key] for key in ("id", "head_sha", "head_branch")}}


class FixtureGitHub(sup.GitHub):
    def __init__(self, runs, artifacts=None, relationships=None):
        super().__init__(config())
        self.runs = runs
        self.artifacts = artifacts or {r["id"]: [artifact(r)] for r in runs}
        self.calls = []
        self.relationships = relationships or {}

    def json(self, suffix):
        self.calls.append(suffix)
        if suffix == "actions/workflows/ci.yml":
            return {"id": 42, "path": ".github/workflows/ci.yml"}
        if suffix.startswith("actions/workflows/42/runs?"):
            return {"workflow_runs": self.runs}
        if suffix.startswith("compare/"):
            sha = suffix.split("...")[1]
            return self.relationships.get(sha, {"status": "ahead", "ahead_by": 1,
                "base_commit": {"sha": BASE_REVISION}, "merge_base_commit": {"sha": BASE_REVISION}})
        if suffix.startswith("actions/runs/"):
            return {"artifacts": self.artifacts[int(suffix.split("/")[2])]}
        raise AssertionError("Unexpected API request")


class GitHubEligibilityTests(unittest.TestCase):
    def test_missing_baseline_provenance_never_selects_by_run_number_alone(self):
        for source in (None, "", "main", "abc"):
            api = FixtureGitHub([run()])
            with self.subTest(source=source), self.assertRaises(sup.Failure):
                api.candidate(0, set(), source)
            self.assertEqual(api.calls, [])

    def test_newer_run_from_older_or_divergent_source_is_not_an_update(self):
        for status in ("behind", "diverged", "identical"):
            relation = {"status": status, "ahead_by": 100, "base_commit": {"sha": BASE_REVISION},
                        "merge_base_commit": {"sha": BASE_REVISION}}
            api = FixtureGitHub([run(99)], relationships={REVISION: relation})
            with self.subTest(status=status):
                self.assertIsNone(api.candidate(10, set(), BASE_REVISION))
                self.assertFalse(any(call.startswith("actions/runs/") for call in api.calls))

    def test_ancestry_must_match_exact_accepted_revision(self):
        valid = {"status": "ahead", "ahead_by": 1, "base_commit": {"sha": BASE_REVISION},
                 "merge_base_commit": {"sha": BASE_REVISION}}
        for change in ({"base_commit": {"sha": OLDER_REVISION}}, {"merge_base_commit": {"sha": OLDER_REVISION}},
                       {"ahead_by": 0}, {"ahead_by": True}, {"merge_base_commit": None}):
            api = FixtureGitHub([run()], relationships={REVISION: {**valid, **change}})
            with self.subTest(change=change):
                self.assertIsNone(api.candidate(0, set(), BASE_REVISION))

    def test_same_source_rerun_does_not_download_or_reinstall(self):
        api = FixtureGitHub([run(100, BASE_REVISION)])
        self.assertIsNone(api.candidate(0, set(), BASE_REVISION))
        self.assertFalse(any(call.startswith(("actions/runs/", "compare/")) for call in api.calls))

    def test_latest_successful_exact_channel_and_client_artifact(self):
        older, newer = run(9, OLDER_REVISION), run(10)
        client = artifact(newer)
        server = {**client, "id": 90000, "name": "valheim-broadcaster-server-" + REVISION}
        helper = {**client, "id": 90001, "name": "valheim-broadcaster-supervisor-" + REVISION}
        api = FixtureGitHub([older, newer], {older["id"]: [artifact(older)], newer["id"]: [server, helper, client]})
        selected = api.candidate(8, set(), BASE_REVISION)
        self.assertEqual(selected["source_sha"], REVISION)
        self.assertEqual(selected["artifact_id"], client["id"])
        self.assertEqual(selected["run_number"], 10)
        self.assertEqual(selected["sha256"], "d" * 64)
        self.assertIn("branch=feature-unattended-client-updates", api.calls[1])

    def test_untrusted_or_unfinished_runs_never_request_artifacts(self):
        changes = [
            {"status": "in_progress"}, {"conclusion": "failure"}, {"conclusion": "cancelled"},
            {"event": "pull_request"}, {"event": "pull_request_target"},
            {"head_branch": "other-feature"}, {"head_repository": {"full_name": "someone/fork"}},
            {"workflow_id": 99}, {"path": ".github/workflows/other.yml"},
            {"head_sha": "main"}, {"run_number": True}, {"id": -1}, {"run_attempt": "1"}, {"run_attempt": None},
        ]
        for change in changes:
            with self.subTest(change=change):
                bad = {**run(), **change}
                api = FixtureGitHub([bad])
                self.assertIsNone(api.candidate(0, set(), BASE_REVISION))
                self.assertFalse(any(path.startswith("actions/runs/") for path in api.calls))

    def test_workflow_dispatch_is_eligible_but_old_run_is_not(self):
        current = {**run(), "event": "workflow_dispatch"}
        self.assertIsNotNone(FixtureGitHub([current]).candidate(9, set(), BASE_REVISION))
        self.assertIsNone(FixtureGitHub([current]).candidate(10, set(), BASE_REVISION))

    def test_quarantine_follows_source_across_new_run_ids(self):
        old, rerun = run(8, OLDER_REVISION), run(12)
        api = FixtureGitHub([run(10), rerun, old])
        selected = api.candidate(7, {REVISION}, BASE_REVISION)
        self.assertEqual(selected["source_sha"], OLDER_REVISION)
        artifact_queries = [path for path in api.calls if path.startswith("actions/runs/")]
        self.assertEqual(artifact_queries, [f"actions/runs/{old['id']}/artifacts?per_page=100"])

    def test_expired_missing_ambiguous_or_wrong_run_artifacts_are_rejected(self):
        good = artifact()
        fixtures = [[], [{**good, "expired": True}], [good, {**good, "id": 99999}],
                    [{**good, "name": "valheim-broadcaster-server-" + REVISION}],
                    [{**good, "workflow_run": {**good["workflow_run"], "id": 1}}],
                    [{**good, "workflow_run": {**good["workflow_run"], "head_sha": OLDER_REVISION}}],
                    [{**good, "workflow_run": {**good["workflow_run"], "head_branch": "main"}}]]
        for values in fixtures:
            with self.subTest(values=values):
                self.assertIsNone(FixtureGitHub([run()], {run()["id"]: values}).candidate(0, set(), BASE_REVISION))

    def test_artifact_requires_digest_and_bounded_declared_size(self):
        for changed in [{"digest": None}, {"digest": ""}, {"digest": "sha256:abcd"},
                        {"size_in_bytes": 0}, {"size_in_bytes": True},
                        {"size_in_bytes": sup.MAX_ARCHIVE + 1}]:
            with self.subTest(changed=changed):
                api = FixtureGitHub([run()], {run()["id"]: [{**artifact(), **changed}]})
                with self.assertRaises(sup.Failure):
                    api.candidate(0, set(), BASE_REVISION)


class DownloadAndRedirectTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.path = Path(self.temporary.name) / "download.zip"
        self.api = sup.GitHub(config())
        self.data = b"artifact fixture bytes"
        self.candidate = {"artifact_id": 1, "archive_size": len(self.data),
                          "sha256": hashlib.sha256(self.data).hexdigest()}

    def test_download_verifies_bytes_and_digest(self):
        with patch.object(self.api, "open", return_value=io.BytesIO(self.data)):
            self.api.download(self.candidate, self.path)
        self.assertEqual(self.path.read_bytes(), self.data)

    def test_download_refuses_existing_file_without_removing_it(self):
        self.path.write_bytes(b"existing evidence")
        with patch.object(self.api, "open", return_value=io.BytesIO(self.data)):
            with self.assertRaises(FileExistsError):
                self.api.download(self.candidate, self.path)
        self.assertEqual(self.path.read_bytes(), b"existing evidence")

    def test_digest_or_length_failure_removes_partial_download(self):
        for change in [{"sha256": "0" * 64}, {"archive_size": 1}]:
            with self.subTest(change=change), patch.object(self.api, "open", return_value=io.BytesIO(self.data)):
                with self.assertRaises(sup.Failure):
                    self.api.download({**self.candidate, **change}, self.path)
                self.assertFalse(self.path.exists())

    def test_download_size_cap_cleans_up(self):
        with patch.object(self.api, "open", return_value=io.BytesIO(self.data)), patch.object(sup, "MAX_ARCHIVE", 4):
            with self.assertRaises(sup.Failure):
                self.api.download(self.candidate, self.path)
        self.assertFalse(self.path.exists())

    def test_interrupted_stream_cleans_up(self):
        class BrokenStream(io.BytesIO):
            def read(self, count=-1):
                if self.tell():
                    raise TimeoutError("simulated transport interruption")
                return super().read(5)
            read1 = read
        with patch.object(self.api, "open", return_value=BrokenStream(self.data)):
            with self.assertRaises(TimeoutError):
                self.api.download(self.candidate, self.path)
        self.assertFalse(self.path.exists())

    def test_trickling_download_has_a_total_deadline(self):
        class Trickle(io.BytesIO):
            def read1(self, count=-1):
                return super().read(1)
        with patch.object(self.api, "open", return_value=Trickle(self.data)), \
                patch.object(sup, "MAX_DOWNLOAD_SECONDS", 2), \
                patch.object(sup.time, "monotonic", side_effect=[0, 0, 0, 1, 1, 2]):
            with self.assertRaisesRegex(sup.Failure, "total time limit"):
                self.api.download(self.candidate, self.path)
        self.assertFalse(self.path.exists())

    def test_operator_pause_cancels_download_before_installation(self):
        checks = iter([False, False, True])
        with patch.object(self.api, "open", return_value=io.BytesIO(self.data)):
            with self.assertRaises(sup.ManualStop):
                self.api.download(self.candidate, self.path, cancelled=lambda: next(checks))
        self.assertFalse(self.path.exists())

    def test_metadata_size_limit_and_shape(self):
        for raw in [b"[]", b"not json", b'{"oversized": "' + b"x" * 50 + b'"}']:
            with self.subTest(raw=raw), patch.object(self.api, "open", return_value=io.BytesIO(raw)), patch.object(sup, "MAX_JSON", 24):
                with self.assertRaises(sup.Failure):
                    self.api.json("actions/workflows/ci.yml")

    def test_artifact_redirect_strips_both_authorization_header_forms(self):
        for destination in ["https://github.com/download", "https://objects.githubusercontent.com/download",
                            "https://artifact.blob.core.windows.net/container/archive.zip"]:
            request = urllib.request.Request("https://api.github.com/repos/owner/repo/actions/artifacts/1/zip",
                                             headers={"Authorization": "Bearer fixture-token"})
            request.add_unredirected_header("Authorization", "Bearer alternate-fixture-token")
            redirected = sup.SafeRedirect().redirect_request(request, None, 302, "Found", {}, destination)
            self.assertFalse(any(key.lower() == "authorization" for key in redirected.headers))
            self.assertFalse(any(key.lower() == "authorization" for key in redirected.unredirected_hdrs))

    def test_unsafe_redirects_are_rejected(self):
        request = urllib.request.Request("https://api.github.com/download")
        for destination in ["http://github.com/download", "https://github.com.attacker.example/download",
                            "https://attacker.example/download", "https://user:secret@github.com/download",
                            "https://github.com:444/download", "file:///tmp/archive.zip"]:
            with self.subTest(destination=destination), self.assertRaises(sup.Failure):
                sup.SafeRedirect().redirect_request(request, None, 302, "Found", {}, destination)


class ArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.archive = self.root / "artifact.zip"
        self.destination = self.root / "stage"
        self.payload = {name: ("fixture for " + name).encode() for name in sup.OWNED_FILES}
        self.manifest = {"schema": 1, "source_sha": REVISION, "version": "0.8.48", "observer_protocol": 3, "supervisor_protocol": 1,
                         "files": [{"path": name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()}
                                   for name, data in sorted(self.payload.items())]}

    def write_archive(self, *, manifest=None, omit=(), extras=(), altered=None, directories=False):
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)  # Deliberate duplicate-name fixture.
            with zipfile.ZipFile(self.archive, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
                if directories:
                    for name in ["ValheimBroadcaster/", "ValheimBroadcaster/ThirdParty/", "ValheimBroadcaster/ThirdParty/Cinemachine/",
                                 "ValheimBroadcaster/ThirdParty/Cinemachine/Splines/"]:
                        bundle.writestr(name, b"")
                if "release-manifest.json" not in omit:
                    bundle.writestr("release-manifest.json", json.dumps(self.manifest if manifest is None else manifest))
                for name, data in self.payload.items():
                    full = "ValheimBroadcaster/" + name
                    if full not in omit:
                        bundle.writestr(full, (altered or {}).get(name, data))
                for name, data in extras:
                    bundle.writestr(name, data)

    def stage(self):
        return sup.stage_archive(self.archive, self.destination, REVISION)

    def assert_rejected(self):
        with self.assertRaises(sup.Failure):
            self.stage()
        self.assertFalse(self.destination.exists(), "Rejected archive must not leave a partial staged payload")

    def test_valid_archive_extracts_only_verified_owned_payload(self):
        self.write_archive(directories=True)
        self.assertEqual(self.stage(), self.manifest)
        files = {p.relative_to(self.destination).as_posix(): p.read_bytes() for p in self.destination.rglob("*") if p.is_file()}
        self.assertEqual(files, self.payload)
        self.assertFalse((self.destination / "release-manifest.json").exists())

    def test_cannot_overwrite_existing_staging_destination(self):
        self.write_archive()
        self.destination.mkdir()
        sentinel = self.destination / "existing.txt"
        sentinel.write_bytes(b"preserve")
        with self.assertRaises(sup.Failure):
            self.stage()
        self.assertEqual(sentinel.read_bytes(), b"preserve")

    def test_rejects_traversal_absolute_backslash_and_case_collision_paths(self):
        for name in ["../outside.txt", "/absolute.txt", "C:/drive.txt", "ValheimBroadcaster\\escape.dll",
                     "ValheimBroadcaster/../escape.dll", "ValheimBroadcaster/valheimbroadcaster.dll",
                     "ValheimBroadcaster//extra.txt", "ValheimBroadcaster/./extra.txt"]:
            with self.subTest(name=name):
                self.write_archive(extras=[(name, b"unexpected")])
                self.assert_rejected()
        self.assertFalse((self.root / "outside.txt").exists())

    def test_rejects_duplicate_entries_and_unowned_dependencies_or_settings(self):
        for name in ["ValheimBroadcaster/ValheimBroadcaster.dll", "release-manifest.json",
                     "ValheimBroadcaster/Jotunn.dll", "ValheimBroadcaster/assembly_valheim.dll",
                     "ValheimBroadcaster/settings.cfg", "BepInEx/config/server-password"]:
            with self.subTest(name=name):
                self.write_archive(extras=[(name, b"unexpected")])
                self.assert_rejected()

    def test_rejects_symbolic_link_and_other_special_zip_entries(self):
        for mode in [stat.S_IFLNK, stat.S_IFIFO, stat.S_IFSOCK]:
            with self.subTest(mode=mode):
                entry = zipfile.ZipInfo("ValheimBroadcaster/ValheimBroadcaster.dll")
                entry.create_system = 3
                entry.external_attr = (mode | 0o777) << 16
                self.write_archive(omit=[entry.filename], extras=[(entry, b"link-target")])
                self.assert_rejected()

    def test_missing_manifest_or_runtime_file_is_rejected(self):
        for missing in ["release-manifest.json", "ValheimBroadcaster/ValheimBroadcaster.Core.dll",
                        "ValheimBroadcaster/ThirdParty/Cinemachine/LICENSE.md",
                        "ValheimBroadcaster/ThirdParty/Cinemachine/Splines/LICENSE.md"]:
            with self.subTest(missing=missing):
                self.write_archive(omit=[missing])
                self.assert_rejected()

    def test_commit_protocol_and_file_inventory_must_match(self):
        changes = [{"source_sha": OLDER_REVISION}, {"schema": 2}, {"observer_protocol": 2}, {"supervisor_protocol": None},
                   {"supervisor_protocol": 2}, {"version": "latest"},
                   {"files": self.manifest["files"][:-1]},
                   {"files": [self.manifest["files"][0]] * len(self.manifest["files"])}]
        for change in changes:
            with self.subTest(change=change):
                self.write_archive(manifest={**self.manifest, **change})
                self.assert_rejected()

    def test_corrupt_payload_or_manifest_size_is_rejected_and_removed(self):
        name = "ValheimBroadcaster.dll"
        self.write_archive(altered={name: b"x" * len(self.payload[name])})
        self.assert_rejected()
        damaged = deepcopy(self.manifest)
        damaged["files"][0]["size"] += 1
        self.write_archive(manifest=damaged)
        self.assert_rejected()

    def test_invalid_manifest_path_type_is_a_handled_rejection(self):
        for invalid in ([], {}, None, 123):
            damaged = deepcopy(self.manifest)
            damaged["files"][0]["path"] = invalid
            self.write_archive(manifest=damaged)
            self.assert_rejected()

    def test_uncompressed_size_and_manifest_json_limits(self):
        self.write_archive()
        with patch.object(sup, "MAX_PAYLOAD", 100):
            self.assert_rejected()
        with patch.object(sup, "MAX_JSON", 10):
            self.assert_rejected()

    def test_excessive_entry_count_rejected(self):
        self.write_archive(extras=[(f"unexpected-{i}.txt", b"x") for i in range(65)])
        self.assert_rejected()

    @unittest.skipUnless(os.name == "nt", "Windows junctions are exercised by Windows CI")
    def test_nested_junction_is_rejected_before_inventory_or_duplicate_traversal(self):
        plugin = self.root / "plugins" / "ValheimBroadcaster"
        outside = self.root / "outside"
        plugin.mkdir(parents=True)
        outside.mkdir()
        (outside / "do-not-read.txt").write_bytes(b"preserve outside payload")
        junction = plugin / "ThirdParty"
        subprocess.run(["cmd", "/d", "/c", "mklink", "/J", str(junction), str(outside)],
                       check=True, capture_output=True)
        try:
            self.assertFalse(junction.is_symlink(), "Fixture must exercise a junction, not a symbolic link")
            with self.assertRaises(sup.Failure):
                sup.inventory(plugin, require_complete=False)
            with self.assertRaises(sup.Failure):
                sup.scan_duplicates(SimpleNamespace(plugin_dir=plugin))
            self.assertEqual((outside / "do-not-read.txt").read_bytes(), b"preserve outside payload")
        finally:
            os.rmdir(junction)


if __name__ == "__main__":
    unittest.main()
