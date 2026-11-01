# DubUrl.Schema

Provider-neutral database schema models, fluent builders, SQL rendering, discovery mapping, and versioned YAML/JSON documents.

The user documentation is published as part of the [single DubUrl website](https://seddryck.github.io/DubUrl/docs/schemas/).

## Build and test

```powershell
dotnet build DubUrl.Schema.sln -c Release
dotnet test DubUrl.Schema.Testing -c Release -f net10.0 --no-build
dotnet test DubUrl.Schema.Documents.Testing -c Release -f net10.0 --no-build
```

Core and provider packages are pinned to DubUrl 0.55.1, the extraction baseline. Override `-p:DubUrlVersion=<candidate>` to check a candidate core/provider release. No checkout of DubUrl is required. See [real-database QA](QA/README.md) and [release and documentation delivery](build/README.md).

## Git hooks

Enable the repository's pre-push check after cloning:

```sh
git config --local core.hooksPath .githooks
dotnet restore DubUrl.Schema.sln
```

The hook requires the .NET SDK on `PATH` and Bash (provided by Git for Windows on Windows). It runs `dotnet format DubUrl.Schema.sln analyzers --verify-no-changes --no-restore` and rejects pushes with analyzer violations. The StyleCop rules in `.editorconfig` match Expressif's CodeFactor policy.

To apply available analyzer fixes, run `dotnet format DubUrl.Schema.sln analyzers --no-restore`, review the changes, and rerun the check. Resolve any remaining diagnostics before pushing.

Source was extracted from Seddryck/DubUrl commit `7606da8d030c869dab0dd9d9946dcdd962dfe000`; earlier file history remains available there. Public namespaces, package identities, supported TFMs, and document URLs are preserved.
