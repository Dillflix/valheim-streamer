#!/usr/bin/env python3
"""Fetch a pinned Cinemachine source package for an isolated, Unity-only runtime build."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import shutil
import tarfile
import tempfile
import urllib.request

VERSION = "3.1.7"
URL = f"https://packages.unity.com/com.unity.cinemachine/-/com.unity.cinemachine-{VERSION}.tgz"
SHA256 = "8b77a98259c6726ce1795690dc2459f0f26d8d17345d9758f019d8de2653998a"
# Compile the upstream spline camera mechanics with selected official Splines
# runtime source in the same isolated DLL. No game or Unity dependency DLL is shipped.
EXCLUDED = set()
SPLINES_VERSION = "2.0.0"
SPLINES_URL = f"https://packages.unity.com/com.unity.splines/-/com.unity.splines-{SPLINES_VERSION}.tgz"
SPLINES_SHA256 = "d58adb0438b3faa97ed3a7d774b501ff1e9194d6918b214ebc92a969ae7531fa"
SPLINES_EXCLUDED = {
    "Runtime/AssemblyInfo.cs", "Runtime/SplineAnimate.cs", "Runtime/SplineComponent.cs",
    "Runtime/SplineExtrude.cs", "Runtime/SplineInstantiate.cs", "Runtime/SplineJobs.cs",
    "Runtime/SplineMesh.cs", "Runtime/SplineShaderUtility.cs",
}
SPLINES_NOTICES = {"LICENSE.md", "package.json"}
SPLINES_PATCHES = [
    {"file": "Runtime/BezierKnot.cs",
     "from": "return HashCode.Combine(Position, TangentIn, TangentOut, Rotation);",
     "to": "return unchecked((((Position.GetHashCode() * 397) ^ TangentIn.GetHashCode()) * 397 ^ TangentOut.GetHashCode()) * 397 ^ Rotation.GetHashCode());",
     "reason": "Preserve equal-knot hash semantics without System.HashCode, which is unavailable in the net48 target."},
]
NOTICES = {"LICENSE.md", "license.txt", "Third Party Notices.md", "package.json"}
PATCHES = [
    {"file": "Runtime/Behaviours/CinemachineCamera.cs",
     "from": "if (m_Pipeline[(int)components[i].Stage] == null)",
     "to": "if (components[i].enabled && m_Pipeline[(int)components[i].Stage] == null)",
     "reason": "Select the enabled component when switching native Orbit/Dolly body stages; upstream OnEnable/OnDisable invalidates this cache, but originally the disabled first body still won."},
    {"file": "Runtime/Behaviours/CinemachineCamera.cs",
     "from": "public sealed class CinemachineCamera : CinemachineVirtualCameraBase",
     "to": "public sealed partial class CinemachineCamera : CinemachineVirtualCameraBase",
     "reason": "Allow a narrow bridge to temporarily provide validated external shot quality to native ClearShot without re-solving or mutating the presented pose."},
    {"file": "Runtime/Debug/CinemachineDebug.cs",
     "from": "s_AvailableStringBuilders[^1]",
     "to": "s_AvailableStringBuilders[s_AvailableStringBuilders.Count - 1]",
     "reason": "Avoid System.Index, which is unavailable in the net48 compilation target."},
    {"file": "Runtime/Core/CameraUpdateManager.cs",
     "from": "static Dictionary<CinemachineVirtualCameraBase, UpdateStatus> s_UpdateStatus;",
     "to": "static Dictionary<CinemachineVirtualCameraBase, UpdateStatus> s_UpdateStatus = new ();",
     "reason": "Initialize on first use when BepInEx loads this assembly after Unity startup; retain the upstream reset hook."},
    {"file": "Runtime/Core/RuntimeUtility.cs",
     "from": "        static int s_ScratchColliderRefCount;\n",
     "to": "",
     "reason": "The shared scratch collider is a recreatable cache, not a resource acquired once per component."},
    {"file": "Runtime/Core/RuntimeUtility.cs",
     "from": "            ++s_ScratchColliderRefCount;\n",
     "to": "",
     "reason": "Repeated physics queries must not increment an unbounded reference count."},
    {"file": "Runtime/Core/RuntimeUtility.cs",
     "from": "            if (--s_ScratchColliderRefCount == 0)",
     "to": "            if (s_ScratchColliderGameObject != null)",
     "reason": "Make component teardown release the cache safely, including teardown before any query or multiple components."},
    {"file": "Runtime/Core/RuntimeUtility.cs",
     "from": "            s_ScratchColliderRefCount = 0;\n",
     "to": "",
     "reason": "Remove the obsolete editor reset of the removed reference counter."},
]


def prepare(archive: bytes, splines_archive: bytes, output: Path) -> None:
    actual = hashlib.sha256(archive).hexdigest()
    if actual != SHA256:
        raise ValueError(f"Cinemachine archive checksum mismatch: {actual}")
    if output.exists() and any(output.iterdir()):
        record_file = output / "source-record.json"
        if not record_file.is_file() or json.loads(record_file.read_text()).get("archive_sha256") != SHA256:
            raise ValueError("Refusing to replace a directory that is not this prepared Cinemachine source")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="cinemachine-", dir=output.parent) as temporary:
        stage = Path(temporary)
        count = 0
        with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
            for member in package.getmembers():
                path = PurePosixPath(member.name)
                if not member.isfile() or not path.parts or path.parts[0] != "package":
                    continue
                relative = PurePosixPath(*path.parts[1:])
                if relative.is_absolute() or ".." in relative.parts:
                    raise ValueError("Unsafe package path")
                name = str(relative)
                if name not in NOTICES and not (name.startswith("Runtime/") and name.endswith(".cs") and name not in EXCLUDED):
                    continue
                stream = package.extractfile(member)
                if stream is None:
                    raise ValueError(f"Missing package file {name}")
                data = stream.read()
                patches = [patch for patch in PATCHES if patch["file"] == name]
                if patches:
                    source = data.decode("utf-8-sig")
                    for patch in patches:
                        if source.count(patch["from"]) != 1:
                            raise ValueError(f"Expected compatibility patch no longer matches: {name}: {patch['reason']}")
                        source = source.replace(patch["from"], patch["to"])
                    data = source.encode("utf-8")
                destination = stage.joinpath(*relative.parts)
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(data)
                count += name.endswith(".cs")
        metadata = json.loads((stage / "package.json").read_text())
        if metadata["version"] != VERSION or count != 131:
            raise ValueError("Unexpected Cinemachine source inventory")
        for notice in NOTICES:
            if not (stage / notice).is_file():
                raise ValueError(f"Missing notice {notice}")
        prepare_splines(splines_archive, stage / "Splines")
        record = {"version": VERSION, "archive_url": URL, "archive_sha256": SHA256,
                  "source_revision": metadata["repository"]["revision"],
                  "excluded": sorted(EXCLUDED), "patches": PATCHES,
                  "splines": json.loads((stage / "Splines/source-record.json").read_text())}
        (stage / "source-record.json").write_text(json.dumps(record, indent=2) + "\n")
        if output.exists():
            shutil.rmtree(output)
        shutil.copytree(stage, output)
    print(f"Prepared Cinemachine {VERSION}: {count} runtime source files in {output}")



def prepare_splines(archive: bytes, output: Path) -> None:
    actual = hashlib.sha256(archive).hexdigest()
    if actual != SPLINES_SHA256:
        raise ValueError(f"Splines archive checksum mismatch: {actual}")
    count = 0
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
        for member in package.getmembers():
            path = PurePosixPath(member.name)
            if not member.isfile() or not path.parts or path.parts[0] != "package":
                continue
            relative = PurePosixPath(*path.parts[1:])
            if relative.is_absolute() or ".." in relative.parts:
                raise ValueError("Unsafe Splines package path")
            name = str(relative)
            if name not in SPLINES_NOTICES and not (name.startswith("Runtime/") and name.endswith(".cs") and name not in SPLINES_EXCLUDED):
                continue
            stream = package.extractfile(member)
            if stream is None:
                raise ValueError(f"Missing Splines file {name}")
            destination = output.joinpath(*relative.parts)
            destination.parent.mkdir(parents=True, exist_ok=True)
            data = stream.read()
            for patch in (p for p in SPLINES_PATCHES if p["file"] == name):
                source = data.decode("utf-8-sig")
                if source.count(patch["from"]) != 1:
                    raise ValueError(f"Expected Splines compatibility patch no longer matches: {name}")
                data = source.replace(patch["from"], patch["to"]).encode("utf-8")
            destination.write_bytes(data)
            count += name.endswith(".cs")
    metadata = json.loads((output / "package.json").read_text())
    if metadata["version"] != SPLINES_VERSION or count != 23:
        raise ValueError("Unexpected Splines source inventory")
    for notice in SPLINES_NOTICES:
        if not (output / notice).is_file():
            raise ValueError(f"Missing Splines notice {notice}")
    record = {"version": SPLINES_VERSION, "archive_url": SPLINES_URL,
              "archive_sha256": SPLINES_SHA256, "source_revision": metadata["repository"]["revision"],
              "excluded": sorted(SPLINES_EXCLUDED), "patches": SPLINES_PATCHES}
    (output / "source-record.json").write_text(json.dumps(record, indent=2) + "\n")


def download(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "ValheimBroadcaster-build"})
    with urllib.request.urlopen(request, timeout=90) as response:
        return response.read()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent.parent / "third_party/Cinemachine/obj/upstream")
    parser.add_argument("--archive", type=Path, help="Use an already downloaded archive; the same checksum is required")
    parser.add_argument("--splines-archive", type=Path, help="Use an already downloaded, checksum-verified Splines archive")
    args = parser.parse_args()
    data = args.archive.read_bytes() if args.archive else download(URL)
    splines_data = args.splines_archive.read_bytes() if args.splines_archive else download(SPLINES_URL)
    prepare(data, splines_data, args.output.resolve())


if __name__ == "__main__":
    main()
