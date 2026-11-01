"""Transfer tested library binaries and verify that NuGet packages reuse them."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = ("DubUrl.Schema", "DubUrl.Schema.Documents", "DubUrl.Schema.Testing.Contracts")
FRAMEWORKS = ("net8.0", "net9.0", "net10.0")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def collect(root, output, framework, version, commit):
    root, output = Path(root), Path(output)
    if framework not in FRAMEWORKS:
        raise ValueError("Unsupported framework")
    files = {}
    for project in PROJECTS:
        directory = Path(project) / "bin" / "Release" / framework
        for extension in ("dll", "pdb", "xml", "deps.json"):
            relative = directory / (project + "." + extension)
            source = root / relative
            if not source.is_file():
                if extension in ("dll", "pdb"):
                    raise ValueError("Missing tested build output: " + str(relative))
                continue
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            files[relative.as_posix()] = digest(source.read_bytes())
    manifest = {"schemaVersion": 1, "framework": framework, "version": version,
                "sourceCommit": commit, "files": files}
    path = output / "build-manifests" / (framework + ".json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Collected tested {framework} binaries and symbols")


def verify(root, version, commit):
    root = Path(root)
    manifests = {}
    for framework in FRAMEWORKS:
        path = root / "build-manifests" / (framework + ".json")
        manifest = json.loads(path.read_text(encoding="utf-8"))
        if (manifest["schemaVersion"] != 1 or manifest["framework"] != framework
                or manifest["version"] != version or manifest["sourceCommit"] != commit):
            raise ValueError("Build artifact identity mismatch: " + framework)
        allowed = {f"{p}/bin/Release/{framework}/{p}.{ext}"
                   for p in PROJECTS for ext in ("dll", "pdb", "xml", "deps.json")}
        required = {f"{p}/bin/Release/{framework}/{p}.{ext}"
                    for p in PROJECTS for ext in ("dll", "pdb")}
        if not required.issubset(manifest["files"]) or not set(manifest["files"]).issubset(allowed):
            raise ValueError("Unexpected build artifact file set: " + framework)
        for relative, expected in manifest["files"].items():
            if digest((root / relative).read_bytes()) != expected:
                raise ValueError("Tested build output changed: " + relative)
        manifests[framework] = manifest
    return manifests


def verify_packages(root, packages, version, commit):
    manifests = verify(root, version, commit)
    for project in PROJECTS:
        for suffix, extension in (("nupkg", "dll"), ("snupkg", "pdb")):
            with zipfile.ZipFile(Path(packages) / f"{project}.{version}.{suffix}") as archive:
                for framework in FRAMEWORKS:
                    relative = f"{project}/bin/Release/{framework}/{project}.{extension}"
                    expected = manifests[framework]["files"][relative]
                    actual = digest(archive.read(f"lib/{framework}/{project}.{extension}"))
                    if actual != expected:
                        raise ValueError("Package does not contain the tested build output: " + relative)
    print("All packaged DLLs and PDBs exactly match the unit-matrix artifacts")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("collect", "verify", "verify-packages"))
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--framework", choices=FRAMEWORKS)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--packages", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    args = parser.parse_args()
    if args.command == "collect":
        if not args.output or not args.framework:
            parser.error("collect requires --output and --framework")
        collect(args.root, args.output, args.framework, args.version, args.commit)
    elif args.command == "verify":
        verify(args.root, args.version, args.commit)
        print("All three tested build artifacts have the expected version, commit and hashes")
    else:
        if not args.packages:
            parser.error("verify-packages requires --packages")
        verify_packages(args.root, args.packages, args.version, args.commit)
