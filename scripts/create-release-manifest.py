#!/usr/bin/env python3
"""Describe the exact, owned client payload for the unattended updater.

Run after staging, before upload-artifact. This does not install anything.
The archive itself is authenticated independently with GitHub's artifact digest.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


PAYLOAD_FILES = frozenset({
    "ValheimBroadcaster.dll",
    "ValheimBroadcaster.Core.dll",
    "ValheimBroadcaster.Cinemachine.dll",
    "ThirdParty/Cinemachine/LICENSE.md",
    "ThirdParty/Cinemachine/license.txt",
    "ThirdParty/Cinemachine/Third Party Notices.md",
    "ThirdParty/Cinemachine/package.json",
    "ThirdParty/Cinemachine/source-record.json",
    "ThirdParty/Cinemachine/BUILD.md",
})


def create_manifest(staging: Path, source_sha: str, version: str) -> dict:
    if not re.fullmatch(r"[0-9a-f]{40}", source_sha):
        raise ValueError("source SHA must be a full lowercase Git commit SHA")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("version must be a numeric three-part plugin version")
    payload = staging / "ValheimBroadcaster"
    if not payload.is_dir() or payload.is_symlink() or staging.is_symlink():
        raise ValueError("expected a real staged ValheimBroadcaster directory")
    found = set()
    for path in staging.rglob("*"):
        if path.is_symlink():
            raise ValueError("symbolic links are forbidden in staged artifacts")
        if path.is_dir():
            if path not in {payload, payload / "ThirdParty", payload / "ThirdParty/Cinemachine"}:
                raise ValueError("unexpected staged directory")
            continue
        if not path.is_file():
            raise ValueError("non-regular staged file")
        if path == staging / "release-manifest.json":
            continue
        if payload not in path.parents:
            raise ValueError("unexpected file outside the owned plugin payload")
        found.add(path.relative_to(payload).as_posix())
    if found != PAYLOAD_FILES:
        raise ValueError("staged payload differs from the explicit owned client file list")
    files = []
    for name in sorted(found):
        data = (payload / name).read_bytes()
        if not data or len(data) > 64 * 1024 * 1024:
            raise ValueError("empty or oversized payload file")
        files.append({"path": name, "sha256": hashlib.sha256(data).hexdigest(), "size": len(data)})
    return {"schema": 1, "source_sha": source_sha, "version": version,
            "observer_protocol": 3, "files": files}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--staging", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--props", type=Path, default=Path(__file__).resolve().parents[1] / "Directory.Build.props")
    args = parser.parse_args()
    version = ET.parse(args.props).findtext(".//Version")
    manifest = create_manifest(args.staging, args.source_sha, version or "")
    (args.staging / "release-manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Manifest: {manifest['version']}, {len(manifest['files'])} owned files, {manifest['source_sha']}")


if __name__ == "__main__":
    main()
