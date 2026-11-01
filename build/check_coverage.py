from pathlib import Path
import sys
import xml.etree.ElementTree as ET
reports=list(Path(sys.argv[1]).rglob("coverage.cobertura.xml"))
if len(reports)!=6: raise ValueError(f"Expected six unit coverage reports; found {len(reports)}")
for report in reports:
    if float(ET.parse(report).getroot().get("line-rate", "0")) < 0.01:
        raise ValueError("Coverage below the existing one-percent baseline: " + str(report))
print("Six coverage reports satisfy the existing one-percent baseline")
