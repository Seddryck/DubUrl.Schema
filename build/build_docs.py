"""Build a reproducible Jekyll source bundle; run from any directory."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def build(output, version, commit):
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("A full source commit is required")
    files = {}
    for path in sorted((ROOT / "docs").rglob("*")):
        if path.is_file():
            relative = path.relative_to(ROOT / "docs").as_posix()
            if relative.startswith(("_docs/", "schemas/", "assets/schema/")):
                files["content/" + relative] = path.read_bytes().replace(b"\r\n", b"\n")
    if "content/schemas/schema-document/v1/schema.json" not in files:
        raise ValueError("The stable v1 specification is missing")
    manifest = {"bundleFormatVersion": 1, "packageVersion": version, "sourceCommit": commit,
                "files": {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}}
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    files["manifest.json"] = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_STORED) as archive:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3  # Fixed Unix metadata on every build host.
            info.compress_type = zipfile.ZIP_STORED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data)
    checksum = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(checksum + "  " + output.name + "\n", encoding="utf-8")
    return checksum

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=str(ROOT / "artifacts/schema-docs.zip"))
    parser.add_argument("--version", required=True, help="SemVer calculated by GitVersion for this source commit")
    parser.add_argument("--commit", default=None)
    args = parser.parse_args()
    commit = args.commit or subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    print(build(args.output, args.version, commit))
