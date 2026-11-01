import argparse
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

def validate(directory, version):
    ids = ["DubUrl.Schema", "DubUrl.Schema.Documents", "DubUrl.Schema.Testing.Contracts"]
    packages = sorted(Path(directory).glob("*.nupkg"))
    if {p.name for p in packages} != {f"{name}.{version}.nupkg" for name in ids}:
        raise ValueError("Unexpected NuGet package set")
    for name in ids:
        with zipfile.ZipFile(Path(directory) / f"{name}.{version}.nupkg") as archive:
            names = set(archive.namelist())
            for tfm in ["net8.0", "net9.0", "net10.0"]:
                if f"lib/{tfm}/{name}.dll" not in names: raise ValueError("Missing supported assembly")
            metadata = ET.fromstring(archive.read(name + ".nuspec"))
            values = {node.tag.split("}")[-1]: node.text for node in metadata.iter()}
            if values["id"] != name or values["version"] != version: raise ValueError("Incorrect package identity")
            repository = next(n for n in metadata.iter() if n.tag.endswith("repository"))
            if repository.get("url") != "https://github.com/Seddryck/DubUrl.Schema": raise ValueError("Incorrect source repository")
            if name == "DubUrl.Schema.Documents" and "schemas/schema-document/v1/schema.json" not in names:
                raise ValueError("Missing packaged document specification")
        if not (Path(directory) / f"{name}.{version}.snupkg").is_file(): raise ValueError("Missing symbols")
    print("Validated three multi-target packages and symbol packages")

if __name__ == "__main__":
    parser=argparse.ArgumentParser();parser.add_argument("directory");parser.add_argument("version")
    args=parser.parse_args();validate(args.directory,args.version)
