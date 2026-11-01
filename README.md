# DubUrl.Schema

Provider-neutral database schema models, fluent builders, SQL rendering, discovery mapping, and versioned YAML/JSON documents.

The user documentation is published as part of the [single DubUrl website](https://seddryck.github.io/DubUrl/docs/schemas/).

## Build and test

```powershell
dotnet build DubUrl.Schema.sln -c Release
dotnet test DubUrl.Schema.Testing -c Release -f net10.0 --no-build
dotnet test DubUrl.Schema.Documents.Testing -c Release -f net10.0 --no-build
```

CI selects the latest stable DubUrl release from NuGet once per run and uses that version for core and provider packages throughout build, QA, packaging and consumer checks. It fails early if any required provider is missing that release. Local builds require DubUrl 0.57.0 or later and default to 0.57.0; override `-p:DubUrlVersion=<candidate>` to select another release. No checkout of DubUrl is required. See [real-database QA](QA/README.md) and [release and documentation delivery](build/README.md).

Discovery identities preserve provider-native qualifiers: SQLite `main` and attached aliases belong to `MetadataName.Database` and `DatabaseObjectName.Database`; they are not schemas. Use `DiscoveryOptions.Databases` to select aliases, `Schemas` for actual schemas, and `Catalogs` for catalogs. These filters also apply to views and their inventory; table filters can match any combination of literal qualifier components. Referenced-table closure preserves each target's complete identity. Rendered attached names require the same aliases to exist in the destination connection.

## Git hooks

Enable the repository's pre-push check after cloning:

```sh
git config --local core.hooksPath .githooks
dotnet restore DubUrl.Schema.sln
```

The hook requires the .NET SDK on `PATH` and Bash (provided by Git for Windows on Windows). It runs `dotnet format DubUrl.Schema.sln analyzers --verify-no-changes --no-restore` and rejects pushes with analyzer violations. The StyleCop rules in `.editorconfig` match Expressif's CodeFactor policy.

To apply available analyzer fixes, run `dotnet format DubUrl.Schema.sln analyzers --no-restore`, review the changes, and rerun the check. Resolve any remaining diagnostics before pushing.

Source was extracted from Seddryck/DubUrl commit `7606da8d030c869dab0dd9d9946dcdd962dfe000`; earlier file history remains available there. Public namespaces, package identities, supported TFMs, and document URLs are preserved.

See [provider-owned table definitions and physical options](docs/provider-table-options.md).
