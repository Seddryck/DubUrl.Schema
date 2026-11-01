"""XML validators accept normal reports and reject entity expansion."""
from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from defusedxml.common import EntitiesForbidden
from assert_results import validate as validate_results
from validate_packages import validate as validate_packages


ENTITY_XML = '<!DOCTYPE root [<!ENTITY payload "expanded">]><root>&payload;</root>'
PROJECTS = ("DubUrl.Schema", "DubUrl.Schema.Documents")
FRAMEWORKS = ("net8.0", "net9.0", "net10.0")


class XmlValidatorTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)

    def test_results_reject_entities(self):
        (self.root / "results.trx").write_text(ENTITY_XML, encoding="utf-8")
        with self.assertRaises(EntitiesForbidden):
            validate_results(self.root)

    def write_coverage(self, content):
        for framework in FRAMEWORKS:
            for project in PROJECTS:
                report = self.root / ("unit-" + framework) / (project + ".Testing") / "coverage.cobertura.xml"
                report.parent.mkdir(parents=True)
                report.write_text(content, encoding="utf-8")

    def check_coverage(self):
        with patch.object(sys, "argv", ["check_coverage.py", str(self.root)]):
            runpy.run_path(str(Path(__file__).with_name("check_coverage.py")), run_name="__main__")

    def test_coverage_accepts_reports(self):
        self.write_coverage('<coverage line-rate="0.5"/>')
        self.check_coverage()

    def test_coverage_rejects_entities(self):
        self.write_coverage(ENTITY_XML)
        with self.assertRaises(EntitiesForbidden):
            self.check_coverage()

    def write_packages(self, metadata=None):
        for project in PROJECTS:
            package = self.root / (project + ".1.2.3.nupkg")
            with zipfile.ZipFile(package, "w") as archive:
                for framework in FRAMEWORKS:
                    archive.writestr(f"lib/{framework}/{project}.dll", b"assembly")
                archive.writestr("schemas/schema-document/v1/schema.json", "{}")
                xml = metadata or (
                    f"<package><metadata><id>{project}</id><version>1.2.3</version>"
                    '<repository url="https://github.com/Seddryck/DubUrl.Schema"/>'
                    "</metadata></package>"
                )
                archive.writestr(project + ".nuspec", xml)
            (self.root / (project + ".1.2.3.snupkg")).touch()

    def test_packages_accept_metadata(self):
        self.write_packages()
        validate_packages(self.root, "1.2.3")

    def test_packages_reject_entities(self):
        self.write_packages(ENTITY_XML)
        with self.assertRaises(EntitiesForbidden):
            validate_packages(self.root, "1.2.3")


if __name__ == "__main__":
    unittest.main()
