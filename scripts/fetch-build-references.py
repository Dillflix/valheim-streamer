#!/usr/bin/env python3
"""Fetch a pinned CI compilation baseline; never install or launch Valheim.

Linux x64 only, Python 3.10+. The free dedicated-server depot is accessible
through Steam's anonymous subscription. No Steam credentials are accepted.
Reference DLLs and downloaded tools must remain outside distributed artifacts.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile


DEPOT_URL = "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_3.4.0/DepotDownloader-linux-x64.zip"
DEPOT_SHA256 = "a999dec66b4850fc961bd50366696d23c2d0fad7b18790e6a5647b2f19097a53"
BEPINEX_URL = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_linux_x64_5.4.23.2.zip"
BEPINEX_SHA256 = "ddc446143a0a277ed92eb7a1f5bf794ff4fdd0023bddc0491f85eafd1898225b"
GAME_SHA256 = "648f3f8ed7b44c3d3cd612acd782743d9e03af7e60b7fe72d5827523e95505fd"
APP_ID = "896660"
DEPOT_ID = "896662"
MANIFEST_ID = "171106503873955013"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def download(url: str, destination: Path, expected_hash: str) -> None:
    """Bound network retries; a checksum mismatch is immediately fatal."""
    request = urllib.request.Request(url, headers={"User-Agent": "valheim-streamer-build-references/0.1.0"})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=60) as response, destination.open("wb") as output:
                size = 0
                while chunk := response.read(1024 * 1024):
                    size += len(chunk)
                    if size > 128 * 1024 * 1024:
                        raise ValueError(f"Download exceeds size limit: {url}")
                    output.write(chunk)
            break
        except (urllib.error.URLError, TimeoutError, OSError):
            destination.unlink(missing_ok=True)
            if attempt == 2:
                raise
            time.sleep(2 ** attempt)
    actual = sha256(destination)
    if actual != expected_hash:
        destination.unlink()
        raise ValueError(f"Checksum mismatch for {url}: expected {expected_hash}, got {actual}")


def extract_member(archive: Path, member: str, destination: Path) -> None:
    # Select exact archive members and explicitly choose their destination.
    # Never extract arbitrary paths supplied by an archive.
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as bundle, bundle.open(member) as source, destination.open("wb") as output:
        shutil.copyfileobj(source, output)


def fetch(output: Path) -> None:
    if platform.system() != "Linux" or platform.machine().lower() not in ("x86_64", "amd64"):
        raise ValueError("This CI reference fetcher requires Linux x64. Local builds can use installed game/BepInEx paths.")
    output = output.absolute()
    if output.exists() or output.is_symlink():
        raise ValueError(f"Output must be a new directory: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="valheim-references-", dir=output.parent) as temporary:
        root = Path(temporary)
        downloader_zip = root / "depotdownloader.zip"
        bepinex_zip = root / "bepinex.zip"
        print("Fetching checksummed DepotDownloader 3.4.0 and BepInEx 5.4.23.2 archives.", flush=True)
        download(DEPOT_URL, downloader_zip, DEPOT_SHA256)
        download(BEPINEX_URL, bepinex_zip, BEPINEX_SHA256)
        downloader = root / "DepotDownloader"
        extract_member(downloader_zip, "DepotDownloader", downloader)
        downloader.chmod(0o755)
        for name in ("BepInEx.dll", "0Harmony.dll", "Mono.Cecil.dll"):
            extract_member(bepinex_zip, f"BepInEx/core/{name}", root / "bepinex/core" / name)
        filelist = root / "managed-files.txt"
        filelist.write_text(r"regex:.*[\\/]Managed[\\/].*\.dll" + "\n", encoding="utf-8")
        print(f"Fetching Steam app {APP_ID}, depot {DEPOT_ID}, manifest {MANIFEST_ID}; managed DLLs only.", flush=True)
        subprocess.run(
            [str(downloader), "-app", APP_ID, "-depot", DEPOT_ID, "-manifest", MANIFEST_ID,
             "-os", "windows", "-osarch", "64", "-dir", str(root / "game"),
             "-filelist", str(filelist), "-validate", "-max-downloads", "4"],
            cwd=root, check=True, timeout=480,
        )
        game_dll = root / "game/valheim_server_Data/Managed/assembly_valheim.dll"
        if not game_dll.is_file() or sha256(game_dll) != GAME_SHA256:
            raise ValueError("Downloaded Valheim assembly does not match the pinned 1.0.21 compilation baseline.")
        provenance = {
            "purpose": "compile references only; no game runtime validation",
            "valheim_version": "1.0.21",
            "steam_app": APP_ID, "steam_depot": DEPOT_ID, "steam_manifest": MANIFEST_ID,
            "assembly_valheim_sha256": GAME_SHA256,
            "depotdownloader_archive": {"url": DEPOT_URL, "sha256": DEPOT_SHA256},
            "bepinex_archive": {"url": BEPINEX_URL, "sha256": BEPINEX_SHA256},
        }
        (root / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
        # Tools, archives and downloader caches are not build inputs.
        downloader.unlink()
        downloader_zip.unlink()
        bepinex_zip.unlink()
        filelist.unlink()
        shutil.rmtree(root / "game/.DepotDownloader", ignore_errors=True)
        os.rename(root, output)
    print(f"Verified references ready at {output}", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="New directory outside the repository for compilation references")
    arguments = parser.parse_args()
    try:
        fetch(arguments.output)
    except (OSError, ValueError, zipfile.BadZipFile, KeyError, subprocess.SubprocessError) as error:
        parser.exit(1, f"Reference fetch failed: {error}\n")


if __name__ == "__main__":
    main()
