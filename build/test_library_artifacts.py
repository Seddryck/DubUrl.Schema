import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from library_artifacts import PROJECTS, FRAMEWORKS, collect, verify, verify_packages


class LibraryArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "source"
        self.staged = self.root / "staged"
        self.packages = self.root / "packages"
        self.version = "0.100.0"
        self.commit = "a" * 40
        for framework in FRAMEWORKS:
            for project in PROJECTS:
                directory = self.source / project / "bin/Release" / framework
                directory.mkdir(parents=True)
                for extension in ("dll", "pdb"):
                    (directory / (project + "." + extension)).write_bytes(f"{project}/{framework}/{extension}".encode())

    def stage(self):
        for framework in FRAMEWORKS:
            collect(self.source, self.staged, framework, self.version, self.commit)

    def pack(self, corrupt=False):
        self.packages.mkdir()
        for project in PROJECTS:
            for suffix, extension in (("nupkg", "dll"), ("snupkg", "pdb")):
                with zipfile.ZipFile(self.packages / f"{project}.{self.version}.{suffix}", "w") as archive:
                    for framework in FRAMEWORKS:
                        source = self.staged / project / "bin/Release" / framework / (project + "." + extension)
                        data = source.read_bytes()
                        if corrupt and project == PROJECTS[0] and framework == FRAMEWORKS[0] and extension == "dll":
                            data += b"rebuilt-different-payload"
                        archive.writestr(f"lib/{framework}/{project}.{extension}", data)

    def test_roundtrip_preserves_all_frameworks_and_symbols(self):
        self.stage()
        self.assertEqual(set(verify(self.staged, self.version, self.commit)), set(FRAMEWORKS))
        self.pack()
        verify_packages(self.staged, self.packages, self.version, self.commit)

    def test_rejects_a_different_commit_or_version(self):
        self.stage()
        for version, commit in (("0.101.0", self.commit), (self.version, "b" * 40)):
            with self.subTest(version=version, commit=commit), self.assertRaisesRegex(ValueError, "identity"):
                verify(self.staged, version, commit)

    def test_rejects_changed_build_or_package_payload(self):
        self.stage()
        self.pack(corrupt=True)
        with self.assertRaisesRegex(ValueError, "Package does not contain"):
            verify_packages(self.staged, self.packages, self.version, self.commit)
        binary = self.staged / PROJECTS[0] / "bin/Release" / FRAMEWORKS[0] / (PROJECTS[0] + ".dll")
        binary.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "output changed"):
            verify(self.staged, self.version, self.commit)

    def test_rejects_an_unexpected_manifest_path(self):
        self.stage()
        path = self.staged / "build-manifests/net8.0.json"
        manifest = json.loads(path.read_text())
        manifest["files"]["../outside"] = "0" * 64
        path.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "file set"):
            verify(self.staged, self.version, self.commit)

    def test_collect_requires_portable_symbols(self):
        (self.source / PROJECTS[0] / "bin/Release/net8.0" / (PROJECTS[0] + ".pdb")).unlink()
        with self.assertRaisesRegex(ValueError, "Missing tested build output"):
            collect(self.source, self.staged, "net8.0", self.version, self.commit)


if __name__ == "__main__":
    unittest.main()
