import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from build_docs import build

class DocsBundleTests(unittest.TestCase):
    def test_reproducible_bundle_and_manifest_hashes(self):
        with tempfile.TemporaryDirectory() as temp:
            first, second = Path(temp) / "first.zip", Path(temp) / "second.zip"
            self.assertEqual(build(first, "1.2.0", "a" * 40), build(second, "1.2.0", "a" * 40))
            self.assertEqual(first.read_bytes(), second.read_bytes())
            with zipfile.ZipFile(first) as archive:
                manifest=json.loads(archive.read("manifest.json"))
                self.assertEqual(set(archive.namelist()), {"manifest.json", *manifest["files"]})
                self.assertEqual(len(manifest["files"]), 13)
                for name, checksum in manifest["files"].items():
                    self.assertNotIn(b"\r\n", archive.read(name))
                    self.assertEqual(hashlib.sha256(archive.read(name)).hexdigest(), checksum)
                specification=json.loads(archive.read("content/schemas/schema-document/v1/schema.json"))
                self.assertEqual(specification["$id"], "https://seddryck.github.io/DubUrl/schemas/schema-document/v1/schema.json")

    def test_requires_full_commit(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError): build(Path(temp)/"docs.zip", "1.2.0", "main")

if __name__ == "__main__": unittest.main()
