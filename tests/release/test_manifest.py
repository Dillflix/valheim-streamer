import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location("manifest", Path(__file__).resolve().parents[2] / "scripts/create-release-manifest.py")
manifest = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(manifest)
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/broadcaster-supervisor"))
import supervisor


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in manifest.PAYLOAD_FILES:
            p = self.root / "ValheimBroadcaster" / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(b"fixture payload")

    def build(self):
        return manifest.create_manifest(self.root, "a" * 40, "0.8.48")

    def test_producer_inventory_and_capability_match_actual_updater(self):
        self.assertEqual(manifest.PAYLOAD_FILES, supervisor.OWNED_FILES)
        result = self.build()
        self.assertEqual(result["supervisor_protocol"], 1)
        self.assertIn("ThirdParty/Cinemachine/Splines/LICENSE.md", manifest.PAYLOAD_FILES)
        with tempfile.TemporaryDirectory() as location:
            archive = Path(location) / "release.zip"
            destination = Path(location) / "validated"
            with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as output:
                output.writestr("release-manifest.json", json.dumps(result))
                for name in manifest.PAYLOAD_FILES:
                    output.write(self.root / "ValheimBroadcaster" / name, "ValheimBroadcaster/" + name)
            self.assertEqual(supervisor.stage_archive(archive, destination, "a" * 40), result)
            self.assertEqual(set(supervisor.inventory(destination)), manifest.PAYLOAD_FILES)

    def test_complete_deterministic_payload(self):
        result = self.build()
        self.assertEqual(result, self.build())
        self.assertEqual({x["path"] for x in result["files"]}, manifest.PAYLOAD_FILES)
        self.assertTrue(all(x["size"] == 15 and len(x["sha256"]) == 64 for x in result["files"]))

    def test_rejects_dependency_or_configuration_in_payload(self):
        for name in ["assembly_valheim.dll", "settings.cfg", "Jotunn.dll"]:
            p = self.root / "ValheimBroadcaster" / name
            p.write_bytes(b"not owned")
            with self.assertRaises(ValueError):
                self.build()
            p.unlink()

    def test_rejects_files_outside_payload(self):
        (self.root / "credentials.txt").write_bytes(b"not a credential")
        with self.assertRaises(ValueError):
            self.build()

    def test_rejects_missing_and_empty_file(self):
        p = self.root / "ValheimBroadcaster/ValheimBroadcaster.dll"
        p.write_bytes(b"")
        with self.assertRaises(ValueError):
            self.build()
        p.unlink()
        with self.assertRaises(ValueError):
            self.build()

    def test_rejects_symlink(self):
        p = self.root / "ValheimBroadcaster/ValheimBroadcaster.dll"
        p.unlink()
        p.symlink_to(self.root / "ValheimBroadcaster/ValheimBroadcaster.Core.dll")
        with self.assertRaises(ValueError):
            self.build()

    def test_rejects_inexact_commit(self):
        with self.assertRaises(ValueError):
            manifest.create_manifest(self.root, "main", "0.8.19")


if __name__ == "__main__":
    unittest.main()
