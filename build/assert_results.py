import argparse
from pathlib import Path
import defusedxml.ElementTree as ET

def validate(directory, allow_skipped=False):
    reports = list(Path(directory).rglob("*.trx"))
    if len(reports) != 1:
        raise ValueError(f"Expected one TRX report, found {len(reports)}")
    report = ET.parse(reports[0])
    counters = next((node for node in report.iter() if node.tag.split("}")[-1] == "Counters"), None)
    if counters is None:
        raise ValueError("TRX counters missing")
    counts = {key: int(counters.get(key, "0")) for key in ("total", "executed", "passed", "failed")}
    if counts["executed"] == 0 or counts["failed"] or counts["passed"] != counts["executed"]:
        raise ValueError("No tests executed or tests failed")
    if not allow_skipped and counts["executed"] != counts["total"]:
        raise ValueError("Required tests were skipped")
    print(f"Validated {counts['passed']} passing tests")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory")
    parser.add_argument("--allow-skipped", action="store_true")
    args = parser.parse_args()
    validate(args.directory, args.allow_skipped)
