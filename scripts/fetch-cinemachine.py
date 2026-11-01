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
# This broadcaster uses follow/composition and physics. Spline authoring is not
# part of its runtime, so do not introduce the Unity Splines package into Valheim.
EXCLUDED = {
    "Runtime/Behaviours/CinemachineSplineCart.cs",
    "Runtime/Behaviours/CinemachineSplineRoll.cs",
    "Runtime/Components/CinemachineSplineDolly.cs",
    "Runtime/Components/CinemachineSplineDollyLookAtTargets.cs",
    "Runtime/Core/SplineAutoDolly.cs",
    "Runtime/Core/SplineContainerExtensions.cs",
    "Runtime/Core/SplineSettings.cs",
    "Runtime/Helpers/CinemachineSplineSmoother.cs",
}
NOTICES = {"LICENSE.md", "license.txt", "Third Party Notices.md", "package.json"}
PATCHES = [
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


def prepare(archive: bytes, output: Path) -> None:
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
        if metadata["version"] != VERSION or count != 123:
            raise ValueError("Unexpected Cinemachine source inventory")
        for notice in NOTICES:
            if not (stage / notice).is_file():
                raise ValueError(f"Missing notice {notice}")
        record = {"version": VERSION, "archive_url": URL, "archive_sha256": SHA256,
                  "source_revision": metadata["repository"]["revision"],
                  "excluded": sorted(EXCLUDED), "patches": PATCHES}
        (stage / "source-record.json").write_text(json.dumps(record, indent=2) + "\n")
        if output.exists():
            shutil.rmtree(output)
        shutil.copytree(stage, output)
    print(f"Prepared Cinemachine {VERSION}: {count} runtime source files in {output}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent.parent / "third_party/Cinemachine/obj/upstream")
    parser.add_argument("--archive", type=Path, help="Use an already downloaded archive; the same checksum is required")
    args = parser.parse_args()
    if args.archive:
        data = args.archive.read_bytes()
    else:
        request = urllib.request.Request(URL, headers={"User-Agent": "ValheimBroadcaster-build"})
        with urllib.request.urlopen(request, timeout=90) as response:
            data = response.read()
    prepare(data, args.output.resolve())


if __name__ == "__main__":
    main()
