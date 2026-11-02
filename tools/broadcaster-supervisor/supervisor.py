#!/usr/bin/env python3
"""User-session supervisor for one explicitly configured broadcaster.

Python 3.10+, standard library only. This program never logs credential contents,
updates dependencies, restores saves, selects arbitrary GitHub builds, or kills
processes by name. See docs/UNATTENDED-UPDATES.md before enabling it.
"""
from __future__ import annotations

import argparse
import contextlib
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import signal
import stat
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile

if os.name == "nt":
    import msvcrt
else:
    import fcntl
from sandboxie import Sandboxie, SandboxieError, guest_to_host, create_private_directory, verify_private_path


MAX_ARCHIVE = 128 * 1024 * 1024
MAX_PAYLOAD = 256 * 1024 * 1024
MAX_JSON = 2 * 1024 * 1024
SHA = re.compile(r"[0-9a-f]{40}\Z")
HASH = re.compile(r"[0-9a-f]{64}\Z")
VERSION = re.compile(r"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?\Z")
OWNED_DLLS = {"ValheimBroadcaster.dll", "ValheimBroadcaster.Core.dll", "ValheimBroadcaster.Cinemachine.dll"}
NOTICE_FILES = {"LICENSE.md", "license.txt", "Third Party Notices.md", "package.json", "source-record.json", "BUILD.md"}
OWNED_FILES = OWNED_DLLS | {"ThirdParty/Cinemachine/" + name for name in NOTICE_FILES}


class Failure(RuntimeError):
    """Operator-safe error text; never include remote responses or credentials."""


class ManualStop(Failure):
    pass


class HealthFailure(Failure):
    pass


class TerminalBlock(HealthFailure):
    pass


def digest(path: Path) -> str:
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def fsync_dir(path: Path) -> None:
    if os.name == "nt":
        # Atomic replacement plus flushed file handles; Python cannot open a
        # Windows directory for FlushFileBuffers. Recovery journal is authoritative.
        return
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def atomic_json(path: Path, value: dict) -> None:
    temporary = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        fsync_dir(path.parent)
    finally:
        temporary.unlink(missing_ok=True)


def read_json(path: Path, default=None):
    if not path.exists():
        return default
    if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_JSON:
        raise Failure("Unsafe or oversized local JSON file")
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (UnicodeError, ValueError) as error:
        raise Failure("Invalid local JSON file") from error


def no_symlinks(path: Path) -> None:
    if not path.is_absolute() or ".." in path.parts:
        raise Failure("Paths must be absolute without parent traversal")
    for part in [path, *path.parents]:
        if part.is_symlink():
            raise Failure("Symlink paths are not supported for installation or state")
        if part.exists() and getattr(part.lstat(), "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400):
            raise Failure("Windows reparse points and junctions are not supported")


def private_dir(path: Path) -> None:
    no_symlinks(path)
    if os.name == "nt":
        create_private_directory(path)
        return
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    info = path.stat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise Failure("State and transaction directories must be owned by this user with mode 0700")


def private_file(path: Path) -> None:
    no_symlinks(path)
    if os.name == "nt":
        if not path.is_file():
            raise Failure("Credential path is not a regular file")
        verify_private_path(path)
        return
    info = path.stat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise Failure("Credential files must be owned by this user with mode 0600")


@dataclass(frozen=True)
class Config:
    repository: str
    workflow: str
    branch: str
    game_dir: Path
    game_executable: Path
    plugin_dir: Path
    state_dir: Path
    launch_command: list[str]
    poll_seconds: float = 120
    health_timeout_seconds: float = 240
    stable_seconds: float = 20
    shutdown_timeout_seconds: float = 45
    require_publishing: bool = True
    token_file: Path | None = None
    platform: str = "windows-sandboxie"
    sandboxie_start_exe: Path | None = None
    sandbox_name: str = "ValheimBroadcastTest"
    sandbox_root: Path | None = None
    control_guest_root: str = r"C:\ValheimBroadcasterControl"

    @classmethod
    def load(cls, path: Path) -> Config:
        raw = read_json(path)
        if not isinstance(raw, dict):
            raise Failure("Configuration must be a JSON object")
        allowed = set(cls.__dataclass_fields__)
        if set(raw) - allowed:
            raise Failure("Unknown supervisor configuration key")
        try:
            for key in ("game_dir", "game_executable", "plugin_dir", "state_dir"):
                raw[key] = Path(raw[key])
            for key in ("token_file", "sandboxie_start_exe", "sandbox_root"):
                if raw.get(key):
                    raw[key] = Path(raw[key])
            config = cls(**raw)
        except (KeyError, TypeError, ValueError) as error:
            raise Failure("Required supervisor configuration is missing or malformed") from error
        config.validate()
        return config

    @property
    def channel(self) -> dict:
        return {"repository": self.repository, "workflow": self.workflow, "branch": self.branch}

    @property
    def transactions(self) -> Path:
        return self.plugin_dir.parent.parent / ".broadcaster-updates"

    def validate(self) -> None:
        if self.platform not in {"windows-sandboxie", "linux"}:
            raise Failure("Unsupported process platform")
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", self.repository):
            raise Failure("Invalid GitHub repository")
        if not re.fullmatch(r"[A-Za-z0-9_.-]+\.ya?ml", self.workflow):
            raise Failure("Workflow must be its exact YAML filename")
        if not self.branch or len(self.branch) > 200 or any(c.isspace() for c in self.branch):
            raise Failure("An exact branch is required")
        for path in (self.game_dir, self.game_executable, self.plugin_dir, self.state_dir):
            no_symlinks(path)
        if self.plugin_dir != self.game_dir / "BepInEx/plugins/ValheimBroadcaster":
            raise Failure("plugin_dir must be the dedicated ValheimBroadcaster folder in the configured game installation")
        if not self.game_dir.is_dir() or not self.game_executable.is_file() or self.game_executable.parent != self.game_dir:
            raise Failure("game_executable must exist directly inside game_dir")
        if self.platform == "linux" and not os.access(self.game_executable, os.X_OK):
            raise Failure("Configured game executable is not executable")
        if self.state_dir == self.game_dir or self.game_dir in self.state_dir.parents:
            raise Failure("Private state must be outside the game installation")
        if not isinstance(self.launch_command, list) or not self.launch_command or any(not isinstance(x, str) or not x or "\0" in x for x in self.launch_command):
            raise Failure("launch_command must be a nonempty argument array")
        if self.platform == "windows-sandboxie":
            if self.sandboxie_start_exe is None or self.sandbox_root is None:
                raise Failure("Windows requires explicit Sandboxie installation and physical sandbox root")
            for path in (self.sandboxie_start_exe, self.sandbox_root):
                no_symlinks(path)
            if self.game_dir != self.sandbox_root and self.sandbox_root not in self.game_dir.parents:
                raise Failure("Windows game_dir must be the physical copy inside the configured sandbox")
            if self.sandbox_root == self.state_dir or self.sandbox_root in self.state_dir.parents:
                raise Failure("Host supervisor state must be outside the sandbox")
            if guest_to_host(self.sandbox_root, self.launch_command[0]) != self.game_executable:
                raise Failure("Guest launch executable does not map to the configured physical game executable")
            guest_to_host(self.sandbox_root, self.control_guest_root)
        else:
            launcher = Path(self.launch_command[0])
            if not launcher.is_absolute() or not launcher.is_file() or not os.access(launcher, os.X_OK):
                raise Failure("The foreground launcher must be an absolute executable path")
        for value, minimum, maximum in ((self.poll_seconds, 30, 86400), (self.health_timeout_seconds, 30, 1800),
                                        (self.stable_seconds, 5, 120), (self.shutdown_timeout_seconds, 5, 180)):
            if type(value) not in (int, float) or not minimum <= value <= maximum:
                raise Failure("Supervisor time limits are outside the supported bounds")
        if self.stable_seconds >= self.health_timeout_seconds or type(self.require_publishing) is not bool:
            raise Failure("Invalid health policy")


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    """A GitHub API bearer token must never reach artifact object storage."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        parsed = urllib.parse.urlsplit(newurl)
        host = parsed.hostname or ""
        if parsed.scheme != "https" or parsed.username or parsed.password or parsed.port not in (None, 443):
            raise Failure("Rejected non-HTTPS artifact redirect")
        if not (host in {"api.github.com", "github.com"} or host.endswith(".githubusercontent.com") or host.endswith(".blob.core.windows.net")):
            raise Failure("Rejected artifact redirect outside GitHub storage")
        redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
        if redirected is not None and host != "api.github.com":
            for key in list(redirected.headers):
                if key.lower() == "authorization":
                    del redirected.headers[key]
            for key in list(redirected.unredirected_hdrs):
                if key.lower() == "authorization":
                    del redirected.unredirected_hdrs[key]
        return redirected


class GitHub:
    def __init__(self, config: Config):
        self.config = config
        self.opener = urllib.request.build_opener(SafeRedirect())

    def token(self) -> str:
        if self.config.token_file:
            private_file(self.config.token_file)
            if self.config.token_file.stat().st_size > 8192:
                raise Failure("GitHub token file is oversized")
            token = self.config.token_file.read_text(encoding="utf-8").strip()
        else:
            token = os.environ.get("GH_TOKEN", os.environ.get("GITHUB_TOKEN", "")).strip()
        if not token or any(c.isspace() for c in token):
            raise Failure("Provide a read-only GitHub Actions token through token_file or GH_TOKEN")
        return token

    def open(self, suffix: str):
        url = "https://api.github.com/repos/" + self.config.repository + "/" + suffix
        request = urllib.request.Request(url, headers={
            "Accept": "application/vnd.github+json", "Authorization": "Bearer " + self.token(),
            "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "ValheimBroadcaster-Supervisor/1"})
        try:
            return self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as error:
            raise Failure("GitHub request failed with HTTP " + str(error.code)) from None
        except (urllib.error.URLError, TimeoutError, OSError):
            raise Failure("GitHub request failed; existing broadcast remains unchanged") from None

    def json(self, suffix: str) -> dict:
        with self.open(suffix) as response:
            raw = response.read(MAX_JSON + 1)
        if len(raw) > MAX_JSON:
            raise Failure("GitHub metadata exceeded its size limit")
        try:
            value = json.loads(raw)
        except (ValueError, UnicodeError):
            raise Failure("GitHub returned invalid metadata") from None
        if not isinstance(value, dict):
            raise Failure("GitHub metadata is not an object")
        return value

    def candidate(self, last_run: int, quarantined: set[str]) -> dict | None:
        workflow = self.json("actions/workflows/" + self.config.workflow)
        workflow_id = workflow.get("id")
        if type(workflow_id) is not int or workflow.get("path") != ".github/workflows/" + self.config.workflow:
            raise Failure("Workflow identity could not be verified")
        query = urllib.parse.urlencode({"branch": self.config.branch, "status": "success", "per_page": 100})
        runs = self.json(f"actions/workflows/{workflow_id}/runs?{query}").get("workflow_runs", [])
        if not isinstance(runs, list):
            raise Failure("GitHub runs metadata is invalid")
        eligible = [r for r in runs if valid_run(r, self.config, workflow_id) and r["run_number"] > last_run]
        eligible.sort(key=lambda r: (r["run_number"], r.get("run_attempt", 1)), reverse=True)
        for run in eligible:
            sha = run["head_sha"]
            # A known-bad source remains quarantined across workflow reruns.
            if sha in quarantined:
                continue
            artifacts = self.json(f"actions/runs/{run['id']}/artifacts?per_page=100").get("artifacts", [])
            if not isinstance(artifacts, list):
                raise Failure("GitHub artifact metadata is invalid")
            expected = "valheim-broadcaster-" + sha
            matches = [a for a in artifacts if isinstance(a, dict) and a.get("name") == expected and not a.get("expired")]
            if len(matches) != 1:
                continue
            artifact = matches[0]
            if type(artifact.get("id")) is not int or artifact["id"] <= 0:
                continue
            association = artifact.get("workflow_run") or {}
            if not isinstance(association, dict):
                continue
            if association.get("id") != run["id"] or association.get("head_sha") != sha or association.get("head_branch") != self.config.branch:
                continue
            checksum = artifact.get("digest", "")
            if not isinstance(checksum, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", checksum):
                raise Failure("Artifact has no verifiable SHA-256 digest; refusing installation")
            size = artifact.get("size_in_bytes")
            if type(size) is not int or not 0 < size <= MAX_ARCHIVE:
                raise Failure("Artifact size is outside the supported limit")
            return {"source_sha": sha, "run_id": run["id"], "run_number": run["run_number"],
                    "artifact_id": artifact["id"], "sha256": checksum[7:], "archive_size": size}
        return None

    def download(self, candidate: dict, destination: Path) -> None:
        total = 0
        checksum = hashlib.sha256()
        try:
            with self.open(f"actions/artifacts/{candidate['artifact_id']}/zip") as response, destination.open("xb") as output:
                while True:
                    block = response.read(1024 * 1024)
                    if not block:
                        break
                    total += len(block)
                    if total > MAX_ARCHIVE:
                        raise Failure("Artifact download exceeded its size limit")
                    checksum.update(block)
                    output.write(block)
                output.flush()
                os.fsync(output.fileno())
            if total != candidate["archive_size"] or checksum.hexdigest() != candidate["sha256"]:
                raise Failure("Downloaded artifact digest or size did not match GitHub")
        except BaseException:
            destination.unlink(missing_ok=True)
            raise


def valid_run(run: dict, config: Config, workflow_id: int) -> bool:
    return (isinstance(run, dict) and run.get("workflow_id") == workflow_id and
            run.get("status") == "completed" and run.get("conclusion") == "success" and
            run.get("event") in {"push", "workflow_dispatch"} and run.get("head_branch") == config.branch and
            isinstance(run.get("head_repository"), dict) and
            isinstance(run["head_repository"].get("full_name"), str) and
            run["head_repository"]["full_name"].lower() == config.repository.lower() and
            run.get("path") == ".github/workflows/" + config.workflow and
            isinstance(run.get("head_sha"), str) and SHA.fullmatch(run["head_sha"]) is not None and
            type(run.get("run_number")) is int and run["run_number"] > 0 and type(run.get("id")) is int and run["id"] > 0)


def validate_manifest(manifest: dict, source_sha: str) -> dict[str, dict]:
    if not isinstance(manifest, dict) or manifest.get("schema") != 1 or manifest.get("source_sha") != source_sha:
        raise Failure("Release manifest does not match the selected source revision")
    if manifest.get("observer_protocol") != 3 or not isinstance(manifest.get("version"), str) or not VERSION.fullmatch(manifest["version"]):
        raise Failure("Release manifest version or observer protocol is incompatible")
    files = manifest.get("files")
    if not isinstance(files, list) or len(files) != len(OWNED_FILES):
        raise Failure("Release manifest must contain exactly the owned runtime files and notices")
    entries = {}
    for entry in files:
        if not isinstance(entry, dict):
            raise Failure("Invalid release file record")
        path = entry.get("path")
        if path not in OWNED_FILES or path in entries:
            raise Failure("Release contains an unknown or duplicate destination")
        if not isinstance(entry.get("sha256"), str) or not HASH.fullmatch(entry["sha256"]):
            raise Failure("Release file has an invalid checksum")
        if type(entry.get("size")) is not int or not 0 < entry["size"] <= MAX_PAYLOAD:
            raise Failure("Release file has an invalid size")
        entries[path] = entry
    if set(entries) != OWNED_FILES or sum(e["size"] for e in entries.values()) > MAX_PAYLOAD:
        raise Failure("Release inventory is incomplete or oversized")
    return entries


def stage_archive(archive: Path, destination: Path, source_sha: str) -> dict:
    if destination.exists():
        raise Failure("Staging destination already exists")
    with zipfile.ZipFile(archive) as bundle:
        infos = bundle.infolist()
        if len(infos) > 64 or sum(i.file_size for i in infos) > MAX_PAYLOAD:
            raise Failure("Archive exceeds its entry or uncompressed size limit")
        names, regular = set(), {}
        allowed_dirs = {"ValheimBroadcaster/", "ValheimBroadcaster/ThirdParty/", "ValheimBroadcaster/ThirdParty/Cinemachine/"}
        for info in infos:
            name = info.filename
            pure = PurePosixPath(name)
            if (not name or "\\" in name or "\0" in name or pure.is_absolute() or ".." in pure.parts or
                    str(pure) + ("/" if info.is_dir() else "") != name or name.casefold() in names):
                raise Failure("Archive contains an unsafe or colliding path")
            names.add(name.casefold())
            mode = (info.external_attr >> 16) & 0o170000
            if mode not in (0, stat.S_IFREG, stat.S_IFDIR) or info.flag_bits & 1:
                raise Failure("Archive links, special files and encrypted entries are forbidden")
            if info.is_dir():
                if name not in allowed_dirs:
                    raise Failure("Unknown archive directory")
            else:
                if mode == stat.S_IFDIR or info.file_size <= 0:
                    raise Failure("Invalid archive file")
                regular[name] = info
        expected = {"release-manifest.json"} | {"ValheimBroadcaster/" + name for name in OWNED_FILES}
        if set(regular) != expected or regular["release-manifest.json"].file_size > MAX_JSON:
            raise Failure("Archive inventory does not match the owned payload")
        try:
            manifest = json.loads(bundle.read("release-manifest.json"))
        except (ValueError, UnicodeError):
            raise Failure("Invalid release manifest JSON") from None
        entries = validate_manifest(manifest, source_sha)
        destination.mkdir(mode=0o700)
        try:
            for path, entry in entries.items():
                info = regular["ValheimBroadcaster/" + path]
                if info.file_size != entry["size"]:
                    raise Failure("Archive file size differs from release manifest")
                target = destination / path
                target.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
                with bundle.open(info) as source, target.open("xb") as output:
                    shutil.copyfileobj(source, output, 1024 * 1024)
                    output.flush()
                    os.fsync(output.fileno())
                if digest(target) != entry["sha256"]:
                    raise Failure("Archive payload checksum mismatch")
            fsync_dir(destination)
        except BaseException:
            shutil.rmtree(destination)
            raise
    return manifest


def inventory(directory: Path, require_complete=True) -> dict[str, dict]:
    no_symlinks(directory)
    if not directory.is_dir():
        raise Failure("Installed broadcaster directory is missing")
    result = {}
    for root, dirs, files in os.walk(directory, followlinks=False):
        for name in dirs + files:
            path = Path(root) / name
            if path.is_symlink():
                raise Failure("Installed broadcaster contains a symlink")
        for name in files:
            path = Path(root) / name
            relative = path.relative_to(directory).as_posix()
            if relative not in OWNED_FILES or not path.is_file():
                raise Failure("Broadcaster directory contains an unowned file; move it out before enabling updates")
            result[relative] = {"sha256": digest(path), "size": path.stat().st_size}
    if require_complete and not OWNED_DLLS.issubset(result):
        raise Failure("Installed broadcaster must contain all three runtime DLLs")
    return result


def verify_inventory(directory: Path, expected: dict) -> None:
    if inventory(directory) != expected:
        raise Failure("Installed or backup payload differs from its recorded inventory")


def flush_tree(directory: Path) -> None:
    for root, dirs, files in os.walk(directory, topdown=False):
        for name in files:
            with (Path(root) / name).open("rb") as stream:
                os.fsync(stream.fileno())
        fsync_dir(Path(root))


def scan_duplicates(config: Config) -> None:
    if not config.plugin_dir.parent.is_dir():
        raise Failure("BepInEx plugins directory is missing")
    for root, dirs, files in os.walk(config.plugin_dir.parent, followlinks=False):
        for name in dirs:
            if (Path(root) / name).is_symlink():
                raise Failure("Symlink plugin folders require manual inventory before enabling updates")
        for name in files:
            if name.casefold() in {x.casefold() for x in OWNED_DLLS}:
                if Path(root) / name != config.plugin_dir / name:
                    raise Failure("Duplicate broadcaster DLL found outside its owned directory")


def proc_identity(pid: int) -> dict | None:
    try:
        base = Path("/proc") / str(pid)
        data = (base / "stat").read_text()
        # Field 2 may contain spaces and parentheses.
        fields = data[data.rfind(")") + 2:].split()
        if fields[0] == "Z":
            return None
        return {"pid": pid, "start_ticks": fields[19], "boot_id": Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
                "uid": base.stat().st_uid, "pgid": int(fields[2])}
    except (FileNotFoundError, ProcessLookupError):
        return None


def same_process(record: dict) -> bool:
    return proc_identity(record.get("pid", -1)) == record.get("identity")


def game_processes(config: Config) -> list[int]:
    matches = []
    for path in Path("/proc").iterdir():
        if not path.name.isdigit():
            continue
        try:
            if (path / "exe").resolve(strict=True) == config.game_executable:
                matches.append(int(path.name))
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
    return matches


class InstanceLock:
    def __init__(self, path: Path):
        self.path, self.fd = path, None

    def __enter__(self):
        no_symlinks(self.path)
        self.fd = os.open(self.path, os.O_RDWR | os.O_CREAT | getattr(os, "O_NOFOLLOW", 0), 0o600)
        try:
            if os.name == "nt":
                if os.fstat(self.fd).st_size == 0:
                    os.write(self.fd, b"0")
                os.lseek(self.fd, 0, os.SEEK_SET)
                msvcrt.locking(self.fd, msvcrt.LK_NBLCK, 1)
            else:
                fcntl.flock(self.fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            os.close(self.fd)
            raise Failure("Another supervisor owns this state directory") from None
        return self

    def __exit__(self, *_):
        if self.fd is not None:
            if os.name == "nt":
                os.lseek(self.fd, 0, os.SEEK_SET)
                msvcrt.locking(self.fd, msvcrt.LK_UNLCK, 1)
            os.close(self.fd)


class Supervisor:
    def __init__(self, config: Config, github=None):
        self.config, self.github = config, github or GitHub(config)
        self.process: subprocess.Popen | None = None
        self.stopping = False
        self.record: dict | None = None
        self.state: dict = {}
        self.sequence = -1
        self.last_progress = 0.0
        self.adapter = None
        self.encoder = None

    @property
    def state_path(self):
        return self.config.state_dir / "state.json"

    @property
    def journal_path(self):
        return self.config.state_dir / "transaction.json"

    def save(self) -> None:
        atomic_json(self.state_path, self.state)

    def report(self, state: str, message: str) -> None:
        self.state["status"], self.state["message"] = state, message
        self.state["updated_at"] = int(time.time())
        self.save()
        print(time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), state + ":", message, flush=True)

    def setup(self) -> None:
        self.config.validate()
        if self.config.platform == "windows-sandboxie":
            self.adapter = Sandboxie(self.config.sandboxie_start_exe, self.config.sandbox_name, self.config.sandbox_root)
        private_dir(self.config.state_dir)
        private_dir(self.config.transactions)
        self.state = read_json(self.state_path, {"schema": 1, "channel": self.config.channel, "active": None,
                                                "quarantined": [], "paused": False, "restart_failures": 0})
        if self.state.get("schema") != 1 or self.state.get("channel") != self.config.channel:
            raise Failure("State belongs to a different update channel; use a distinct state directory")
        self.record = read_json(self.config.state_dir / "process.json")

    def is_running(self) -> bool:
        if not self.record:
            return False
        if self.adapter:
            if self.process is not None:
                return self.process.poll() is None
            return self.adapter.same_process(self.record)
        return same_process(self.record)

    def running_games(self) -> list[int]:
        if self.adapter:
            return self.adapter.game_processes(self.config.game_executable, self.config.launch_command[0])
        return game_processes(self.config)

    def preflight(self) -> dict:
        scan_duplicates(self.config)
        current = inventory(self.config.plugin_dir)
        if self.running_games():
            raise Failure("Configured Valheim executable is already running; stop it before handing control to the supervisor")
        return current

    def control_dir(self, record=None) -> Path:
        record = record or self.record
        if not record or not re.fullmatch(r"[0-9a-f]{32}", record.get("session_id", "")):
            raise Failure("Invalid managed process session")
        if self.adapter:
            return guest_to_host(self.config.sandbox_root, self.config.control_guest_root) / record["session_id"]
        return self.config.state_dir / "sessions" / record["session_id"]

    def launch(self, version: str | None) -> None:
        self.preflight()
        session = uuid.uuid4().hex
        control = self.control_dir({"session_id": session})
        private_dir(control)
        plugin_hash = digest(self.config.plugin_dir / "ValheimBroadcaster.dll")
        if self.adapter:
            guest_control = self.config.control_guest_root.rstrip("\\/") + "\\" + session
            self.process = self.adapter.launch(self.config.launch_command, self.config.game_executable, session, guest_control)
            identity = self.process.identity
        else:
            self._launch_linux(control, session)
            identity = proc_identity(self.process.pid)
        if identity is None:
            self.process.wait(timeout=5)
            raise HealthFailure("Foreground launcher exited before the game started")
        self.record = {"schema": 1, "session_id": session, "pid": self.process.pid, "identity": identity,
                       "version": version, "plugin_sha256": plugin_hash, "started_at": time.time()}
        atomic_json(self.config.state_dir / "process.json", self.record)
        self.sequence, self.last_progress = -1, time.monotonic()
        self.report("starting", "Launched the configured broadcaster in its selected process environment")

    def _launch_linux(self, control: Path, session: str) -> None:
        environment = os.environ.copy()
        for key in ("GH_TOKEN", "GITHUB_TOKEN"):
            environment.pop(key, None)
        environment.update(VB_CONTROL_DIR=str(control), VB_SESSION_ID=session, SteamAppId="892970")
        # A fixed-size rotation bounds logs without persisting launch arguments.
        log = self.config.state_dir / "game-console.log"
        if log.exists():
            os.replace(log, self.config.state_dir / "game-console.previous.log")
        with log.open("wb") as output:
            self.process = subprocess.Popen(self.config.launch_command, cwd=self.config.game_dir, env=environment,
                                            stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT,
                                            start_new_session=True, close_fds=True)

    def heartbeat(self, allow_final=False) -> dict | None:
        if not self.record:
            return None
        running = self.is_running()
        if not running and not allow_final:
            return None
        path = self.control_dir() / "status.json"
        try:
            status = read_json(path)
            if not isinstance(status, dict) or time.time() - path.stat().st_mtime > 10 or path.stat().st_mtime > time.time() + 5:
                return None
        except (Failure, FileNotFoundError):
            return None
        if status.get("schema") != 1 or status.get("session_id") != self.record["session_id"] or status.get("pid") != self.record["pid"]:
            return None
        if self.record["version"] is not None and status.get("version") != self.record["version"]:
            return None
        if status.get("plugin_sha256") != self.record.get("plugin_sha256"):
            return None
        if not running and (status.get("state") != "stopped" or status.get("reason") not in {"operator_exit", "operator_stopped"}):
            return None
        if not isinstance(status.get("version"), str) or not VERSION.fullmatch(status["version"]):
            return None
        sequence = status.get("sequence")
        if type(sequence) is not int or sequence < self.sequence:
            return None
        if sequence > self.sequence:
            self.sequence, self.last_progress = sequence, time.monotonic()
        if time.monotonic() - self.last_progress > 10:
            return None
        if running and self.adapter and type(status.get("encoder_pid")) is int and status["encoder_pid"] > 0:
            identity = self.adapter.encoder_identity(status["encoder_pid"], self.record)
            if identity and identity != self.record.get("encoder_identity"):
                self.record["encoder_identity"] = identity
                atomic_json(self.config.state_dir / "process.json", self.record)
        return status

    def final_operator_intent(self) -> bool:
        """A terminal user quit survives supervisor downtime; it is not live health."""
        if not self.record or self.is_running():
            return False
        path = self.control_dir() / "status.json"
        status = read_json(path)
        if not isinstance(status, dict):
            return False
        return (status.get("schema") == 1 and status.get("session_id") == self.record.get("session_id") and
                status.get("pid") == self.record.get("pid") and status.get("plugin_sha256") == self.record.get("plugin_sha256") and
                isinstance(status.get("version"), str) and VERSION.fullmatch(status["version"]) is not None and
                (self.record.get("version") is None or status["version"] == self.record["version"]) and type(status.get("sequence")) is int and
                status["sequence"] >= 0 and status.get("state") == "stopped" and
                status.get("reason") in {"operator_exit", "operator_stopped"} and
                self.record.get("started_at", 0) <= path.stat().st_mtime <= time.time() + 5)

    def requested_pause(self) -> bool:
        command = read_json(self.config.state_dir / "operator.json")
        if command:
            if command.get("command") == "pause":
                self.state["paused"] = True
            elif command.get("command") == "resume":
                self.state["paused"] = False
                self.state["restart_failures"] = 0
            (self.config.state_dir / "operator.json").unlink(missing_ok=True)
            self.save()
        return self.state.get("paused", False)

    def wait_healthy(self) -> dict:
        deadline = time.monotonic() + self.config.health_timeout_seconds
        healthy_since = None
        while time.monotonic() < deadline:
            if self.stopping or self.requested_pause():
                raise ManualStop("Operator stopped or paused the supervisor")
            status = self.heartbeat(allow_final=True)
            if status:
                if status.get("state") == "stopped":
                    raise ManualStop("Broadcaster was stopped by the operator")
                if status.get("state") == "blocked":
                    # Reason text is intentionally not copied; native third-party messages can contain secrets.
                    raise TerminalBlock("Broadcaster startup was blocked; inspect its local diagnostics")
                healthy = (status.get("observer_authorized") is True and status.get("observer_active") is True and
                           status.get("picture_live") is True and status.get("state") == "broadcasting" and
                           (not self.config.require_publishing or status.get("publishing") is True))
                if healthy:
                    healthy_since = healthy_since or time.monotonic()
                    if time.monotonic() - healthy_since >= self.config.stable_seconds:
                        return status
                else:
                    healthy_since = None
            else:
                healthy_since = None
            if not self.is_running():
                raise HealthFailure("Broadcaster process exited during startup")
            time.sleep(1)
        raise HealthFailure("Broadcaster did not establish stable authorized output before the health deadline")

    def signal_owned_group(self, sig: int) -> None:
        if not self.record:
            return
        pgid = self.record["identity"]["pgid"]
        if pgid != self.record["pid"]:
            raise Failure("Managed process group identity is invalid")
        members = []
        for path in Path("/proc").iterdir():
            if not path.name.isdigit():
                continue
            try:
                identity = proc_identity(int(path.name))
                if identity is None or identity["pgid"] != pgid:
                    continue
                environment = (path / "environ").read_bytes().split(b"\0")
                marker = ("VB_SESSION_ID=" + self.record["session_id"]).encode()
                if identity["uid"] != os.getuid() or marker not in environment:
                    raise Failure("Process group contains an unverified process; refusing forced shutdown")
                members.append(identity["pid"])
            except (FileNotFoundError, ProcessLookupError):
                continue
        if members:
            os.killpg(pgid, sig)

    def stop(self) -> None:
        if not self.record:
            return
        if self.adapter and self.process is None:
            self.process = self.adapter.attach(self.record)
        if self.adapter and self.record.get("encoder_identity"):
            identity = self.record["encoder_identity"]
            self.encoder = self.adapter.attach({"pid": identity["pid"], "identity": identity})
        if self.is_running():
            atomic_json(self.control_dir() / "command.json", {"schema": 1, "session_id": self.record["session_id"], "command": "shutdown"})
            deadline = time.monotonic() + self.config.shutdown_timeout_seconds
            while self.is_running() and time.monotonic() < deadline:
                time.sleep(0.2)
            if self.is_running():
                if self.adapter:
                    self.process.terminate()
                else:
                    self.signal_owned_group(signal.SIGTERM)
                deadline = time.monotonic() + 10
                while self.is_running() and time.monotonic() < deadline:
                    time.sleep(0.2)
            if self.is_running():
                if self.adapter:
                    self.process.kill()
                else:
                    self.signal_owned_group(signal.SIGKILL)
        # Also clear a proven, session-marked orphan encoder after the game exits.
        if not self.adapter:
            self.signal_owned_group(signal.SIGTERM)
        elif self.encoder:
            try:
                if self.encoder.poll() is None:
                    self.encoder.terminate()
                    self.encoder.wait(timeout=10)
            finally:
                self.encoder.close()
                self.encoder = None
        if self.process is not None:
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                raise Failure("Owned broadcaster did not stop; installation is forbidden") from None
        if self.is_running():
            raise Failure("Owned broadcaster still exists; installation is forbidden")
        if self.adapter and self.process:
            self.process.close()
        self.process, self.record = None, None
        (self.config.state_dir / "process.json").unlink(missing_ok=True)

    def recover_transaction(self) -> None:
        journal = read_json(self.journal_path)
        if journal is None:
            return
        txid = journal.get("transaction_id", "")
        if journal.get("schema") != 1 or not re.fullmatch(r"[0-9a-f]{32}", txid):
            raise Failure("Invalid update journal; manual inspection is required")
        transaction = self.config.transactions / txid
        private_dir(transaction)
        self.stop()
        if self.running_games():
            raise Failure("An unmanaged game process prevents transaction recovery")
        if journal.get("phase") == "accepted":
            verify_inventory(self.config.plugin_dir, journal["after"])
            self.state["active"] = journal["candidate"]
            self.save()
            self.journal_path.unlink()
            return
        self.rollback(journal, "Interrupted update restored from its verified backup")

    def rollback(self, journal: dict, reason: str, quarantine=True) -> None:
        transaction = self.config.transactions / journal["transaction_id"]
        backup = transaction / "backup"
        verify_inventory(backup, journal["before"])
        self.stop()
        if self.running_games():
            raise Failure("An unmanaged game process prevents rollback")
        self.preserve_log(transaction, "failed-game.log")
        restore = transaction / ("restore-" + uuid.uuid4().hex)
        shutil.copytree(backup, restore)
        verify_inventory(restore, journal["before"])
        flush_tree(restore)
        if self.config.plugin_dir.exists():
            # A changed live payload might be a manual edit; never discard it.
            os.replace(self.config.plugin_dir, transaction / ("rejected-" + uuid.uuid4().hex))
        os.replace(restore, self.config.plugin_dir)
        fsync_dir(self.config.plugin_dir.parent)
        verify_inventory(self.config.plugin_dir, journal["before"])
        self.state["active"] = journal.get("previous")
        if quarantine:
            self.state["quarantined"] = sorted(set(self.state.get("quarantined", [])) | {journal["candidate"]["source_sha"]})
        self.save()
        self.journal_path.unlink(missing_ok=True)
        fsync_dir(self.config.state_dir)
        self.report("rolled_back", reason)
        try:
            self.prune_history()
        except (Failure, OSError):
            pass

    def prepare(self, candidate: dict) -> tuple[dict, Path]:
        txid = uuid.uuid4().hex
        transaction = self.config.transactions / txid
        private_dir(transaction)
        archive = transaction / "download.zip"
        try:
            self.github.download(candidate, archive)
            manifest = stage_archive(archive, transaction / "new", candidate["source_sha"])
            prior = (self.state.get("active") or {}).get("version")
            if prior and tuple(map(int, manifest["version"].split("-", 1)[0].split("+", 1)[0].split("."))) < tuple(map(int, prior.split("-", 1)[0].split("+", 1)[0].split("."))):
                raise Failure("Selected build would downgrade the installed plugin version")
            candidate = {**candidate, "version": manifest["version"]}
            return candidate, transaction
        except BaseException:
            shutil.rmtree(transaction)
            raise

    def cleanup_staging(self, transaction: Path) -> None:
        journal = read_json(self.journal_path)
        if journal and journal.get("transaction_id") == transaction.name:
            return
        # A prepared-but-not-installed payload has no recovery evidence to keep.
        if transaction.parent == self.config.transactions and not (transaction / "backup").exists():
            no_symlinks(transaction)
            shutil.rmtree(transaction)

    def preserve_log(self, transaction: Path, name: str) -> None:
        source = self.config.game_dir / "BepInEx/LogOutput.log"
        if source.is_symlink() or not source.is_file():
            return
        try:
            no_symlinks(source)
            with source.open("rb") as stream:
                stream.seek(max(0, source.stat().st_size - 2 * 1024 * 1024))
                content = stream.read(2 * 1024 * 1024)
            target = transaction / name
            with target.open("wb") as stream:
                stream.write(content)
                stream.flush()
                os.fsync(stream.fileno())
        except (Failure, OSError):
            # Log retention cannot prevent restoration of a validated payload.
            pass

    def prune_history(self) -> None:
        journal = read_json(self.journal_path)
        pinned = journal.get("transaction_id") if journal else None
        candidates = []
        for path in self.config.transactions.iterdir():
            if re.fullmatch(r"[0-9a-f]{32}", path.name) and path.name != pinned and path.is_dir():
                no_symlinks(path)
                candidates.append(path)
        # Keep three prior transaction records, backups and failed payloads.
        candidates.sort(key=lambda p: p.stat().st_mtime, reverse=True)
        for path in candidates[3:]:
            shutil.rmtree(path)

    def update(self, candidate: dict, transaction: Path) -> None:
        if self.stopping or self.requested_pause():
            raise ManualStop("Update deferred by operator request")
        status = self.heartbeat()
        if not status or not status.get("observer_authorized") or not status.get("observer_active") or (self.config.require_publishing and not status.get("publishing")):
            raise Failure("Broadcast health changed while staging; leaving current installation in place")
        # Refuse unknown files/duplicate plugins BEFORE interrupting the broadcast.
        scan_duplicates(self.config)
        before = inventory(self.config.plugin_dir)
        after = inventory(transaction / "new")
        shutil.copytree(self.config.plugin_dir, transaction / "backup")
        verify_inventory(transaction / "backup", before)
        flush_tree(transaction / "backup")
        # Existing config/save files are outside this explicit owned-directory mapping.
        journal = {"schema": 1, "transaction_id": transaction.name, "phase": "prepared", "before": before,
                   "after": after, "previous": self.state.get("active"), "candidate": candidate}
        atomic_json(self.journal_path, journal)
        try:
            self.report("updating", "Validated build staged; stopping the owned broadcaster")
            self.stop()
            if self.stopping or self.requested_pause():
                raise ManualStop("Update cancelled while stopping the broadcaster")
            if self.running_games():
                raise Failure("An unmanaged game process prevents installation")
            self.preserve_log(transaction, "previous-game.log")
            verify_inventory(self.config.plugin_dir, before)
            os.replace(self.config.plugin_dir, transaction / "retired")
            fsync_dir(self.config.plugin_dir.parent)
            os.replace(transaction / "new", self.config.plugin_dir)
            fsync_dir(self.config.plugin_dir.parent)
            verify_inventory(self.config.plugin_dir, after)
            journal["phase"] = "installed"
            atomic_json(self.journal_path, journal)
            if self.stopping or self.requested_pause():
                raise ManualStop("Update cancelled before launching the new build")
            self.launch(candidate["version"])
            self.wait_healthy()
            journal["phase"] = "accepted"
            atomic_json(self.journal_path, journal)
            self.state["active"], self.state["restart_failures"] = candidate, 0
            self.save()
            self.journal_path.unlink()
            fsync_dir(self.config.state_dir)
            self.report("broadcasting", "Updated build passed stable observer and output health checks")
            try:
                self.prune_history()
            except (Failure, OSError):
                # Retention maintenance must not roll back an accepted build.
                pass
        except ManualStop:
            self.state["paused"] = True
            self.rollback(journal, "Operator stop during update; previous payload restored and supervision paused", quarantine=False)
            raise
        except BaseException:
            self.rollback(journal, "New build failed health or installation checks; previous payload restored and build quarantined")
            raise

    def run(self) -> None:
        self.setup()
        with InstanceLock(self.config.state_dir / "supervisor.lock"):
            # A supervisor crash may leave its exactly recorded child alive.
            if self.record:
                if self.final_operator_intent():
                    self.state["paused"] = True
                    self.save()
                self.stop()
            self.recover_transaction()
            self.preflight()
            next_poll, next_restart = 0.0, 0.0
            degraded_since, healthy_since = None, None
            try:
                while not self.stopping:
                    if self.requested_pause():
                        self.stop()
                        if self.state.get("status") != "paused":
                            self.report("paused", "Paused until an explicit resume")
                        time.sleep(1)
                        continue
                    if self.record is None:
                        if time.monotonic() < next_restart:
                            time.sleep(1)
                            continue
                        try:
                            self.launch((self.state.get("active") or {}).get("version"))
                            status = self.wait_healthy()
                            self.record["version"] = status["version"]
                            atomic_json(self.config.state_dir / "process.json", self.record)
                            if self.state.get("active") is None:
                                self.state["active"] = {"version": status["version"], "run_number": 0, "source_sha": None}
                            self.report("broadcasting", "Authorized observer and output are healthy")
                            degraded_since, healthy_since = None, time.monotonic()
                        except ManualStop:
                            self.state["paused"] = True
                            self.save()
                            continue
                        except TerminalBlock:
                            self.stop()
                            self.state["paused"] = True
                            self.report("blocked", "Startup reported a terminal block; inspect local diagnostics and explicitly resume")
                            continue
                        except (Failure, OSError):
                            self.stop()
                            count = self.state.get("restart_failures", 0) + 1
                            self.state["restart_failures"] = count
                            if count >= 3:
                                self.state["paused"] = True
                                self.report("blocked", "Three startup attempts failed; inspect local diagnostics and explicitly resume")
                            else:
                                next_restart = time.monotonic() + min(300, 30 * 2 ** (count - 1))
                                self.report("backoff", "Startup failed; bounded retry scheduled")
                            continue
                    status = self.heartbeat(allow_final=True)
                    if status and status.get("state") in {"stopped", "blocked"}:
                        self.state["paused"] = True
                        self.report("paused", "Game reported an operator stop or blocked startup; explicit resume required")
                        continue
                    healthy = status and status.get("observer_authorized") is True and status.get("observer_active") is True and (not self.config.require_publishing or status.get("publishing") is True)
                    if not healthy:
                        healthy_since = None
                        degraded_since = degraded_since or time.monotonic()
                        if time.monotonic() - degraded_since >= self.config.health_timeout_seconds:
                            self.stop()
                            self.state["restart_failures"] = self.state.get("restart_failures", 0) + 1
                            if self.state["restart_failures"] >= 3:
                                self.state["paused"] = True
                            next_restart = time.monotonic() + 30
                            self.report("backoff", "Broadcaster health expired; bounded restart scheduled")
                            degraded_since = None
                            continue
                    else:
                        degraded_since = None
                        healthy_since = healthy_since or time.monotonic()
                        if self.state.get("restart_failures", 0) and time.monotonic() - healthy_since >= 600:
                            self.state["restart_failures"] = 0
                            self.save()
                    if healthy and time.monotonic() >= next_poll:
                        next_poll = time.monotonic() + self.config.poll_seconds
                        try:
                            active = self.state.get("active") or {}
                            candidate = self.github.candidate(active.get("run_number", 0), set(self.state.get("quarantined", [])))
                            if candidate and candidate["source_sha"] != active.get("source_sha"):
                                candidate, transaction = self.prepare(candidate)
                                try:
                                    self.update(candidate, transaction)
                                finally:
                                    self.cleanup_staging(transaction)
                        except ManualStop:
                            self.state["paused"] = True
                            self.save()
                        except (Failure, SandboxieError, OSError, zipfile.BadZipFile):
                            # Download/validation errors leave the live game untouched. Failed update rolls back.
                            self.report("update_deferred", "Update check failed or build was rejected; retrying on the next poll")
                    time.sleep(1)
            finally:
                self.stop()
                self.report("stopped", "Supervisor stopped; owned broadcaster is stopped")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("command", choices=["check", "run", "status", "pause", "resume"])
    args = parser.parse_args(argv)
    try:
        config = Config.load(args.config)
        if config.platform == "windows-sandboxie" and os.name != "nt":
            raise Failure("The Windows Sandboxie adapter must run on Windows")
        if config.platform == "linux" and sys.platform != "linux":
            raise Failure("The Linux process adapter must run on Linux")
        if args.command == "status":
            print(json.dumps(read_json(config.state_dir / "state.json", {"status": "not_started"}), indent=2))
            return 0
        if args.command in {"pause", "resume"}:
            private_dir(config.state_dir)
            atomic_json(config.state_dir / "operator.json", {"command": args.command})
            print("Requested " + args.command + "; an active supervisor applies it within its next control check.")
            return 0
        if os.name != "nt" and os.geteuid() == 0:
            raise Failure("Run as the dedicated Steam desktop user, never root")
        supervisor = Supervisor(config)
        if args.command == "check":
            supervisor.setup()
            with InstanceLock(config.state_dir / "supervisor.lock"):
                supervisor.preflight()
                candidate = supervisor.github.candidate((supervisor.state.get("active") or {}).get("run_number", 0), set(supervisor.state.get("quarantined", [])))
                print(json.dumps({"preflight": "passed", "candidate": candidate, "installed_files": len(inventory(config.plugin_dir))}, indent=2))
            return 0
        signal.signal(signal.SIGINT, lambda *_: setattr(supervisor, "stopping", True))
        signal.signal(signal.SIGTERM, lambda *_: setattr(supervisor, "stopping", True))
        supervisor.run()
        return 0
    except (Failure, SandboxieError, OSError, zipfile.BadZipFile) as error:
        # Only authored Failure messages are safe; OS errors can contain private arguments/paths.
        print("Supervisor: " + (str(error) if isinstance(error, (Failure, SandboxieError)) else type(error).__name__ + "; inspect local configuration"), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
