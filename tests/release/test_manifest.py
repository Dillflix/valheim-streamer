import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("manifest", Path(__file__).resolve().parents[2] / "scripts/create-release-manifest.py")
manifest = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(manifest)


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
        return manifest.create_manifest(self.root, "a" * 40, "0.8.19")

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
