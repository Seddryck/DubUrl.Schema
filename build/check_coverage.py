from pathlib import Path
import sys
import defusedxml.ElementTree as ET

root = Path(sys.argv[1])
for framework in ("net8.0", "net9.0", "net10.0"):
    for project in ("DubUrl.Schema.Testing", "DubUrl.Schema.Documents.Testing"):
        directory = root / ("unit-" + framework) / project
        # VSTest also copies attachments beneath <run>/In/<machine>; those are
        # duplicates of the canonical collector report, not additional runs.
        reports = [path for path in directory.rglob("coverage.cobertura.xml")
                   if "in" not in (part.casefold() for part in path.relative_to(directory).parts)]
        if len(reports) != 1:
            raise ValueError(f"Expected one canonical report for {project}/{framework}; found {len(reports)}")
        if float(ET.parse(reports[0]).getroot().get("line-rate", "0")) < 0.01:
            raise ValueError("Coverage below the existing one-percent baseline: " + str(reports[0]))
print("Six canonical coverage reports satisfy the existing one-percent baseline")
