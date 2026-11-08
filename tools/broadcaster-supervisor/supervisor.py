#!/usr/bin/env python3
"""User-session supervisor for one explicitly configured broadcaster.

Python 3.10+, standard library only. This program never logs credential contents,
updates dependencies, restores saves, selects arbitrary GitHub builds, or kills
processes by name. See docs/UNATTENDED-UPDATES.md before enabling it.
"""
from __future__ import annotations

import argparse
import configparser
import contextlib
import errno
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
    import ctypes
    from ctypes import wintypes
    import msvcrt

    _kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    _kernel.CreateFileW.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                                    wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
    _kernel.CreateFileW.restype = wintypes.HANDLE
    _kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    _kernel.CloseHandle.restype = wintypes.BOOL
else:
    import fcntl
from sandboxie import Sandboxie, SandboxieError, guest_to_host, create_private_directory, verify_private_path


MAX_ARCHIVE = 128 * 1024 * 1024
MAX_PAYLOAD = 256 * 1024 * 1024
MAX_JSON = 2 * 1024 * 1024
MAX_DOWNLOAD_SECONDS = 180
SHA = re.compile(r"[0-9a-f]{40}\Z")
HASH = re.compile(r"[0-9a-f]{64}\Z")
VERSION = re.compile(r"\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?\Z")
OWNED_DLLS = {"ValheimBroadcaster.dll", "ValheimBroadcaster.Core.dll", "ValheimBroadcaster.Cinemachine.dll"}
NOTICE_FILES = {"LICENSE.md", "license.txt", "Third Party Notices.md", "package.json", "source-record.json", "BUILD.md"}
OWNED_FILES = (OWNED_DLLS | {"ThirdParty/Cinemachine/" + name for name in NOTICE_FILES} |
               {"ThirdParty/Cinemachine/Splines/" + name for name in ("LICENSE.md", "package.json", "source-record.json")})


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


def access_conflict(error):
    return getattr(error, "winerror", None) in (5, 32, 33) or error.errno == errno.EACCES


def atomic_json(path: Path, value: dict) -> None:
    temporary = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        for delay in (0.025, 0.05, 0.1, 0):
            try:
                os.replace(temporary, path)
                break
            except OSError as error:
                if os.name != "nt" or not delay or not access_conflict(error):
                    raise
                time.sleep(delay)
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


class LiveReadUnavailable(Failure):
    """An existing or inaccessible live control file could not be sampled."""


def open_live_json(path):
    if os.name != "nt":
        return os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK), "rb")
    # Share delete access with the game's native replacement operation and
    # open the reparse point itself so fstat can reject redirects.
    generic_read, share_read_write_delete = 0x80000000, 0x7
    open_existing, open_reparse_point = 3, 0x00200000
    handle = _kernel.CreateFileW(str(path), generic_read, share_read_write_delete, None,
                                 open_existing, open_reparse_point, None)
    if handle == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        fd = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
    except BaseException:
        _kernel.CloseHandle(handle)
        raise
    return os.fdopen(fd, "rb")


def read_live_json(path):
    """Read data and mtime from one opened generation of a replaceable file."""
    for delay in (0.025, 0.05, 0.1, 0):
        try:
            with open_live_json(path) as stream:
                info = os.fstat(stream.fileno())
                if (not stat.S_ISREG(info.st_mode) or info.st_size > MAX_JSON or
                        getattr(info, "st_file_attributes", 0) & 0x400):
                    raise Failure("Unsafe or oversized " + path.name)
                data = stream.read(MAX_JSON + 1)
                if len(data) > MAX_JSON:
                    raise Failure("Oversized " + path.name)
                return json.loads(data.decode("utf-8")), info.st_mtime
        except FileNotFoundError:
            return None, None
        except (UnicodeError, ValueError):
            raise Failure("Invalid JSON in " + path.name) from None
        except OSError as error:
            winerror = getattr(error, "winerror", None)
            if not access_conflict(error):
                raise
            if not delay:
                code = f"WinError {winerror}" if winerror is not None else f"errno {error.errno}"
                raise LiveReadUnavailable(f"{path.name} read denied ({code})") from None
            time.sleep(delay)


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
        missing = object()
        raw = read_json(path, default=missing)
        if raw is missing:
            raise Failure(f"Supervisor config file not found: {path.absolute()}. Pass --config with the path to your existing supervisor.json.")
        if not isinstance(raw, dict):
            raise Failure(f"Expected a JSON object in {path.absolute()}. Check that --config points to your supervisor settings.")
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

    def candidate(self, last_run: int, quarantined: set[str], accepted_sha: str | None = None) -> dict | None:
        if not isinstance(accepted_sha, str) or not SHA.fullmatch(accepted_sha):
            raise Failure("A healthy client's exact source revision is required before selecting updates")
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
            if sha in quarantined or sha == accepted_sha:
                continue
            # Workflow run numbers describe execution order, not source history.
            # A dispatch or branch rewind must not replace an accepted client
            # with unrelated/older code that happens to reuse its version.
            relation = self.json(f"compare/{accepted_sha}...{sha}")
            if (relation.get("status") != "ahead" or type(relation.get("ahead_by")) is not int or
                    relation["ahead_by"] <= 0 or not isinstance(relation.get("base_commit"), dict) or
                    relation["base_commit"].get("sha") != accepted_sha or
                    not isinstance(relation.get("merge_base_commit"), dict) or
                    relation["merge_base_commit"].get("sha") != accepted_sha):
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

    def download(self, candidate: dict, destination: Path, cancelled=None) -> None:
        total = 0
        checksum = hashlib.sha256()
        deadline = time.monotonic() + MAX_DOWNLOAD_SECONDS
        created = False

        def check_pending():
            if cancelled is not None and cancelled():
                raise ManualStop("Artifact download cancelled by operator request")
            if time.monotonic() >= deadline:
                raise Failure("Artifact download exceeded its total time limit")

        try:
            check_pending()
            with self.open(f"actions/artifacts/{candidate['artifact_id']}/zip") as response, destination.open("xb") as output:
                created = True
                # HTTPResponse.read(n) can wait for n bytes while a trickling
                # peer keeps resetting its socket timeout. read1 returns after
                # one underlying read, so wall time and pause remain bounded.
                read = getattr(response, "read1", response.read)
                while True:
                    check_pending()
                    block = read(64 * 1024)
                    check_pending()
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
            if created:
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
            type(run.get("run_attempt", 1)) is int and run.get("run_attempt", 1) > 0 and
            type(run.get("run_number")) is int and run["run_number"] > 0 and type(run.get("id")) is int and run["id"] > 0)


def validate_manifest(manifest: dict, source_sha: str) -> dict[str, dict]:
    if not isinstance(manifest, dict) or manifest.get("schema") != 1 or manifest.get("source_sha") != source_sha:
        raise Failure("Release manifest does not match the selected source revision")
    if (manifest.get("observer_protocol") != 3 or manifest.get("supervisor_protocol") != 1 or
            not isinstance(manifest.get("version"), str) or not VERSION.fullmatch(manifest["version"])):
        raise Failure("Release manifest version, observer protocol or supervisor capability is incompatible")
    files = manifest.get("files")
    if not isinstance(files, list) or len(files) != len(OWNED_FILES):
        raise Failure("Release manifest must contain exactly the owned runtime files and notices")
    entries = {}
    for entry in files:
        if not isinstance(entry, dict):
            raise Failure("Invalid release file record")
        path = entry.get("path")
        if not isinstance(path, str) or path not in OWNED_FILES or path in entries:
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
        allowed_dirs = {"ValheimBroadcaster/", "ValheimBroadcaster/ThirdParty/", "ValheimBroadcaster/ThirdParty/Cinemachine/",
                        "ValheimBroadcaster/ThirdParty/Cinemachine/Splines/"}
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
            no_symlinks(path)
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
            # Windows _commit/FlushFileBuffers requires a writable file handle.
            # This operates only on our new backup/restore copies, never live saves.
            with (Path(root) / name).open("r+b") as stream:
                os.fsync(stream.fileno())
        fsync_dir(Path(root))


def scan_duplicates(config: Config) -> None:
    if not config.plugin_dir.parent.is_dir():
        raise Failure("BepInEx plugins directory is missing")
    for root, dirs, files in os.walk(config.plugin_dir.parent, followlinks=False):
        for name in dirs:
            no_symlinks(Path(root) / name)
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


class RecoveryRequired(Failure):
    """A pending transaction must be reconciled before any further launch."""


class Supervisor:
    def __init__(self, config: Config, github=None):
        self.config, self.github = config, github or GitHub(config)
        self.process: subprocess.Popen | None = None
        self.stopping = False
        self.record: dict | None = None
        self.state: dict = {}
        self.sequence = -1
        self.last_progress = 0.0
        self.startup_phase = None
        self.status_read_error = None
        self.operator_read_error = None
        self.adapter = None

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

    def require_reconciled(self) -> None:
        if self.journal_path.exists():
            self.state["paused"] = True
            self.report("recovery_required", "Unresolved update transaction; no further launch or update is permitted. Restart supervision to reconcile it.")
            raise RecoveryRequired("An unresolved update transaction requires recovery before continuing")

    def launch(self, version: str | None, source_sha: str | None = None, transaction_id: str | None = None) -> None:
        journal = read_json(self.journal_path)
        if transaction_id is None:
            self.require_reconciled()
        elif (not isinstance(journal, dict) or journal.get("transaction_id") != transaction_id or
              journal.get("phase") != "installed" or journal.get("candidate", {}).get("version") != version or
              journal.get("candidate", {}).get("source_sha") != source_sha):
            raise RecoveryRequired("Only the installed transaction may launch its candidate health check")
        else:
            verify_inventory(self.config.plugin_dir, journal["after"])
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
                       "version": version, "source_sha": source_sha, "plugin_sha256": plugin_hash, "started_at": time.time()}
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
            status, modified = read_live_json(path)
            self.status_read_error = None
            if not isinstance(status, dict) or not -5 <= time.time() - modified <= 10:
                return None
        except LiveReadUnavailable as error:
            if self.status_read_error != str(error):
                print(str(error) + "; waiting for a fresh heartbeat", flush=True)
            self.status_read_error = str(error)
            return None
        except Failure:
            self.status_read_error = None
            return None
        if status.get("schema") != 1 or status.get("session_id") != self.record["session_id"] or status.get("pid") != self.record["pid"]:
            return None
        if (status.get("supervisor_protocol") != 1 or not isinstance(status.get("source_sha"), str) or
                SHA.fullmatch(status["source_sha"]) is None or
                self.record.get("source_sha") is not None and status["source_sha"] != self.record["source_sha"]):
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
        if running and self.adapter:
            reported = status.get("encoder_pids", [])
            if not isinstance(reported, list) or len(reported) > 3 or any(type(pid) is not int or pid <= 0 for pid in reported):
                return None
            legacy = status.get("encoder_pid", 0)
            if type(legacy) is int and legacy > 0:
                reported = list(set(reported) | {legacy})
            retained = list(self.record.get("encoder_identities", []))
            if self.record.get("encoder_identity"):
                retained.append(self.record["encoder_identity"])
            identities = {identity["pid"]: identity for identity in retained
                          if self.adapter.same_process({"pid": identity["pid"], "identity": identity})}
            for pid in reported:
                identity = self.adapter.encoder_identity(pid, self.record)
                if identity:
                    identities[pid] = identity
            # Retain proven live children even if a later heartbeat is empty
            # during shutdown/rotation. Dead or reused PIDs never retain authority.
            if len(identities) > 16:
                raise Failure("Too many independently verified encoder children; stop supervision and inspect local diagnostics")
            current = sorted(identities.values(), key=lambda identity: identity["pid"])
            if current != self.record.get("encoder_identities", []) or "encoder_identity" in self.record:
                self.record["encoder_identities"] = current
                self.record.pop("encoder_identity", None)
                atomic_json(self.config.state_dir / "process.json", self.record)
        return status

    def final_operator_intent(self) -> bool:
        """A terminal user quit survives supervisor downtime; it is not live health."""
        if not self.record or self.is_running():
            return False
        path = self.control_dir() / "status.json"
        status, modified = read_live_json(path)
        if not isinstance(status, dict):
            return False
        return (status.get("schema") == 1 and status.get("session_id") == self.record.get("session_id") and
                status.get("supervisor_protocol") == 1 and isinstance(status.get("source_sha"), str) and
                SHA.fullmatch(status["source_sha"]) is not None and
                (self.record.get("source_sha") is None or status["source_sha"] == self.record["source_sha"]) and
                status.get("pid") == self.record.get("pid") and status.get("plugin_sha256") == self.record.get("plugin_sha256") and
                isinstance(status.get("version"), str) and VERSION.fullmatch(status["version"]) is not None and
                (self.record.get("version") is None or status["version"] == self.record["version"]) and type(status.get("sequence")) is int and
                status["sequence"] >= 0 and status.get("state") == "stopped" and
                status.get("reason") in {"operator_exit", "operator_stopped"} and
                self.record.get("started_at", 0) <= modified <= time.time() + 5)

    def requested_pause(self) -> bool:
        command, _ = read_live_json(self.config.state_dir / "operator.json")
        if command:
            if command.get("command") == "pause":
                self.state["paused"] = True
            elif command.get("command") == "resume":
                self.state["paused"] = False
                self.state["restart_failures"] = 0
            (self.config.state_dir / "operator.json").unlink(missing_ok=True)
            self.save()
        return self.state.get("paused", False)

    def observe_pause(self):
        # The running observer can keep broadcasting while we retry reading a
        # command. Launch and update boundaries must still defer new work.
        try:
            paused = self.requested_pause()
            self.operator_read_error = None
            return paused
        except LiveReadUnavailable as error:
            if self.operator_read_error != str(error):
                print(str(error) + "; deferring new launches and updates until readable", flush=True)
            self.operator_read_error = str(error)
            return self.state.get("paused", False)

    def wait_healthy(self) -> dict:
        deadline = time.monotonic() + self.config.health_timeout_seconds
        self.startup_phase = None
        self.status_read_error = None
        healthy_since = None
        while time.monotonic() < deadline:
            if self.stopping or self.observe_pause():
                raise ManualStop("Operator stopped or paused the supervisor")
            status = self.heartbeat(allow_final=True)
            if status:
                phase = status.get("startup_phase")
                if phase in ("waiting_for_menu", "selecting_character", "joining_server", "password_submitted", "authorized") and phase != self.startup_phase:
                    self.startup_phase = phase
                    self.report("starting", "Game startup: " + phase)
                if status.get("state") == "stopped":
                    raise ManualStop("Broadcaster was stopped by the operator")
                if status.get("state") == "blocked":
                    # Reason text is intentionally not copied; native third-party messages can contain secrets.
                    category = status.get("failure_category")
                    if category not in ("authentication", "configuration", "adapter", "connection", "observer_authorization", "streaming"):
                        category = "unknown"
                    raise TerminalBlock("Broadcaster startup blocked (" + category + "); inspect its local diagnostics")
                healthy = (status.get("observer_authorized") is True and status.get("observer_active") is True and
                           status.get("picture_live") is True and status.get("state") == "broadcasting" and
                           (not self.config.require_publishing or status.get("publishing") is True))
                if healthy:
                    healthy_since = healthy_since or time.monotonic()
                    if time.monotonic() - healthy_since >= self.config.stable_seconds:
                        return status
                else:
                    healthy_since = None
            elif not self.status_read_error or time.monotonic() - self.last_progress > 10:
                healthy_since = None
            if not self.is_running():
                raise HealthFailure("Broadcaster process exited during startup")
            time.sleep(1)
        reason = "Broadcaster did not establish stable authorized output before the health deadline"
        raise HealthFailure(f"{reason}: {self.status_read_error}" if self.status_read_error else reason)

    def capture_startup_failure(self, error, attempt):
        # Capture *before* stop() can send a shutdown or change the process exit code.
        if isinstance(error, OSError):
            code = getattr(error, "winerror", None)
            reason = f"{type(error).__name__} (WinError {code})" if code is not None else f"{type(error).__name__} (errno {error.errno})"
            trace = error.__traceback__
            while trace and trace.tb_next and trace.tb_next.tb_frame.f_code.co_filename == __file__:
                trace = trace.tb_next
            if trace:
                reason += " in " + trace.tb_frame.f_code.co_name
        else:
            reason = str(error)  # Failure contains operator-safe text.
        process = "no retained process"
        if self.process is not None:
            try:
                code = self.process.poll()
                process = "running before supervisor cleanup" if code is None else f"already exited, code {code} (0x{code & 0xffffffff:08X})"
            except (OSError, SandboxieError):
                process = "process state unavailable"
        phase = self.startup_phase or "before first startup heartbeat"
        message = f"{reason} | {phase} | {process}"
        self.state["last_startup_failure"] = {"at": int(time.time()), "message": message,
                                              "session_id": self.record.get("session_id") if self.record else None}
        print("Startup failure before cleanup:", message, flush=True)
        if self.record:
            # Reuse at most three slots in the private host state directory.
            self.preserve_log(self.config.state_dir, f"startup-failure-{min(attempt, 3)}.log")
        return message

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
        else:
            failed = False
            recorded = list(self.record.get("encoder_identities", []))
            if self.record.get("encoder_identity"):
                recorded.append(self.record["encoder_identity"])
            for identity in {entry["pid"]: entry for entry in recorded}.values():
                encoder = None
                try:
                    encoder = self.adapter.attach({"pid": identity["pid"], "identity": identity})
                    if encoder is None:
                        continue
                    if encoder.poll() is None:
                        encoder.terminate()
                        encoder.wait(timeout=10)
                except (SandboxieError, OSError, subprocess.TimeoutExpired):
                    failed = True
                finally:
                    if encoder is not None:
                        encoder.close()
            if failed:
                raise Failure("An owned encoder did not stop; retain process identity and forbid installation")
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
        self.require_reconciled()
        txid = uuid.uuid4().hex
        transaction = self.config.transactions / txid
        private_dir(transaction)
        archive = transaction / "download.zip"
        try:
            self.github.download(candidate, archive, cancelled=lambda: self.stopping or self.requested_pause())
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
        # A failed backup/flush before journal creation also leaves a backup
        # directory. It never touched the live payload and is not retained
        # rollback history. Otherwise each deferred poll can leak another copy.
        never_installed = ((transaction / "new").exists() and not (transaction / "retired").exists() and
                           not any(transaction.glob("rejected-*")))
        if transaction.parent == self.config.transactions and (not (transaction / "backup").exists() or never_installed):
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
            no_symlinks(target)
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
        self.require_reconciled()
        if self.stopping or self.requested_pause():
            raise ManualStop("Update deferred by operator request")
        status = self.heartbeat()
        if (not status or status.get("state") != "broadcasting" or status.get("picture_live") is not True or
                status.get("observer_authorized") is not True or status.get("observer_active") is not True or
                self.config.require_publishing and status.get("publishing") is not True):
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
            self.launch(candidate["version"], candidate["source_sha"], transaction.name)
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
            try:
                self.rollback(journal, "Operator stop during update; previous payload restored and supervision paused", quarantine=False)
            except BaseException:
                self.require_reconciled()
                raise
            raise
        except BaseException:
            try:
                self.rollback(journal, "New build failed health or installation checks; previous payload restored and build quarantined")
            except BaseException:
                self.require_reconciled()
                raise
            raise

    def installed_baseline(self, manifest_path: Path) -> dict:
        """Explicit hand-off after a manual install. Never install from the supplied manifest."""
        manifest = read_json(manifest_path)
        sha = manifest.get("source_sha") if isinstance(manifest, dict) else None
        if not isinstance(sha, str) or not SHA.fullmatch(sha):
            raise Failure("Provide the release-manifest.json from the installed client ZIP")
        files = validate_manifest(manifest, sha)
        verify_inventory(self.config.plugin_dir, {name: {"sha256": row["sha256"], "size": row["size"]}
                                                 for name, row in files.items()})
        if sha in self.state.get("quarantined", []):
            raise Failure("The installed baseline source is quarantined")
        return {"source_sha": sha, "version": manifest["version"], "run_number": 0}

    def run(self, installed_manifest: Path | None = None) -> None:
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
            baseline = self.installed_baseline(installed_manifest) if installed_manifest else None
            next_poll, next_restart = 0.0, 0.0
            degraded_since, healthy_since = None, None
            try:
                while not self.stopping:
                    self.require_reconciled()
                    if self.observe_pause():
                        self.stop()
                        if self.state.get("status") not in {"paused", "blocked"}:
                            self.report("paused", "Paused until an explicit resume")
                        time.sleep(1)
                        continue
                    if self.record is None:
                        if self.operator_read_error or time.monotonic() < next_restart:
                            time.sleep(1)
                            continue
                        try:
                            self.startup_phase = None
                            expected = baseline or self.state.get("active") or {}
                            if baseline and self.installed_baseline(installed_manifest) != baseline:
                                raise Failure("The supplied baseline manifest changed during startup")
                            self.launch(expected.get("version"), expected.get("source_sha"))
                            status = self.wait_healthy()
                            self.record["version"] = status["version"]
                            self.record["source_sha"] = status["source_sha"]
                            atomic_json(self.config.state_dir / "process.json", self.record)
                            if self.state.get("active") is None or (baseline and
                                    (self.state.get("active") or {}).get("source_sha") != baseline["source_sha"]):
                                self.state["active"] = {"version": status["version"], "run_number": 0, "source_sha": status["source_sha"]}
                            baseline = None
                            self.report("broadcasting", "Authorized observer and output are healthy")
                            degraded_since, healthy_since = None, time.monotonic()
                        except ManualStop:
                            self.state["paused"] = True
                            self.save()
                            continue
                        except TerminalBlock as error:
                            self.stop()
                            self.state["paused"] = True
                            self.report("blocked", str(error))
                            continue
                        except (Failure, OSError) as error:
                            count = self.state.get("restart_failures", 0) + 1
                            detail = self.capture_startup_failure(error, count)
                            self.stop()
                            self.state["restart_failures"] = count
                            if count >= 3:
                                self.state["paused"] = True
                                self.report("blocked", f"Startup failed three times. {detail}. Explicit resume required")
                            else:
                                delay = min(300, 30 * 2 ** (count - 1))
                                next_restart = time.monotonic() + delay
                                self.report("backoff", f"{detail}. Retry scheduled in {delay}s")
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
                    if healthy and not self.operator_read_error and time.monotonic() >= next_poll:
                        next_poll = time.monotonic() + self.config.poll_seconds
                        try:
                            active = self.state.get("active") or {}
                            candidate = self.github.candidate(active.get("run_number", 0), set(self.state.get("quarantined", [])), active.get("source_sha"))
                            self.state["last_update_check"] = {"at": int(time.time()), "branch": self.config.branch,
                                "result": "qualified" if candidate else "no_eligible_build",
                                "candidate_sha": candidate["source_sha"] if candidate else None}
                            self.save()
                            if candidate and candidate["source_sha"] != active.get("source_sha"):
                                candidate, transaction = self.prepare(candidate)
                                try:
                                    self.update(candidate, transaction)
                                finally:
                                    self.cleanup_staging(transaction)
                        except ManualStop:
                            self.state["paused"] = True
                            self.save()
                        except RecoveryRequired:
                            raise
                        except (Failure, SandboxieError, OSError, zipfile.BadZipFile) as error:
                            self.state["last_update_check"] = {"at": int(time.time()), "branch": self.config.branch,
                                "result": "deferred", "reason": str(error) if isinstance(error, Failure) else type(error).__name__}
                            # Download/validation errors leave the live game untouched. Failed update rolls back.
                            self.report("update_deferred", "Update check failed or build was rejected; retrying on the next poll")
                    time.sleep(1)
            finally:
                self.stop()
                if self.state.get("status") != "recovery_required":
                    self.report("stopped", "Supervisor stopped; owned broadcaster is stopped")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("command", choices=["check", "configure-startup", "run", "status", "pause", "resume"])
    parser.add_argument("--installed-manifest", type=Path, help="run: verify an explicitly installed client before adopting its healthy session")
    args = parser.parse_args(argv)
    if args.installed_manifest and args.command != "run":
        parser.error("--installed-manifest is only valid with run")
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
        if args.command == "configure-startup":
            if config.platform != "windows-sandboxie":
                raise Failure("Startup setup requires the Windows Sandboxie profile")
            from startup_setup import configure
            supervisor.setup()
            with InstanceLock(config.state_dir / "supervisor.lock"):
                supervisor.require_reconciled()
                supervisor.preflight()
                try:
                    configure(config, no_symlinks, private_dir, private_file)
                except (configparser.Error, UnicodeError):
                    raise Failure("Sandbox BepInEx configuration could not be parsed; original settings retained") from None
                except ValueError as error:
                    raise Failure(str(error)) from None
            return 0
        if args.command == "check":
            supervisor.setup()
            with InstanceLock(config.state_dir / "supervisor.lock"):
                supervisor.preflight()
                active = supervisor.state.get("active") or {}
                source = active.get("source_sha")
                proven = isinstance(source, str) and SHA.fullmatch(source) is not None
                candidate = supervisor.github.candidate(active.get("run_number", 0), set(supervisor.state.get("quarantined", [])), source) if proven else None
                print(json.dumps({"preflight": "passed", "candidate": candidate,
                                  "update_qualification": "baseline_verified" if proven else "requires_first_healthy_supervised_baseline",
                                  "installed_files": len(inventory(config.plugin_dir))}, indent=2))
            return 0
        signal.signal(signal.SIGINT, lambda *_: setattr(supervisor, "stopping", True))
        signal.signal(signal.SIGTERM, lambda *_: setattr(supervisor, "stopping", True))
        supervisor.run(args.installed_manifest)
        return 0
    except (Failure, SandboxieError, OSError, zipfile.BadZipFile) as error:
        # Only authored Failure messages are safe; OS errors can contain private arguments/paths.
        print("Supervisor: " + (str(error) if isinstance(error, (Failure, SandboxieError)) else type(error).__name__ + "; inspect local configuration"), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
