# Release and documentation delivery

The libraries and reusable NUnit contracts are portable multi-target NuGet packages (`net8.0`, `net9.0`, `net10.0`). All packages and the documentation ZIP use the same GitVersion SemVer calculated once from the source commit. The DubUrl dependency remains independently pinned by `DubUrlVersion` in `Directory.Build.props`. No executable RID, libc, or architecture distribution matrix is needed. Native SQLite/DuckDB tests retain Windows runners; PostgreSQL, SQL Server, Oracle, SingleStore and MySQL use Linux containers.

## Automatic versions and releases

`GitVersion.yml` uses the same GitHubFlow/v1 workflow, Conventional Commit expressions, feature branch labels and continuous-deployment main branch as DubUrl; Expressif follows the same commit-based approach. `next-version: v0.100.0` (the prefix matches the inherited `tag-prefix: v`) bootstraps the first main release. It is a lower bound, not a version file to edit for every release. Subsequent versions are calculated from commits and `v` tags.

- `fix:`, `chore:`, `refactor:`, `perf:`, `build:`, `ci:` and the other configured patch types increase the patch.
- `feat:` or `+semver: minor` increases the minor.
- Matching breaking-change messages increase the major; `+semver: none`/`skip` uses the inherited no-bump rule.
- Feature branches carry the inherited `beta.{BranchName}` labels. Pull requests and manual compatibility runs validate and package their calculated version without publishing.

Successful pushes to `main` reserve a `v<SemVer>` tag for every tested package version and publish the NuGet packages automatically, following Expressif's tag policy. Those tags allow successive patch releases to advance (`0.100.0`, `0.100.1`, `0.100.2`). As in both sibling repositories, only a zero patch number creates a GitHub release with `schema-docs.zip`; patch releases publish to NuGet without a GitHub release. No manual `publish=true` switch or synchronized version files remain.

The pipeline builds/tests each supported TFM, runs the seven real-database jobs on .NET 10 only, checks coverage, packs and smoke-tests consumers, and then recalculates GitVersion after refreshing main/tags. It rejects tested-commit/version drift and tag collisions, including a concurrent tag creation at the wrong commit. Publication consumes the already-tested artifacts. Configure NuGet trusted publishing for Seddryck/DubUrl.Schema, workflow `ci.yml`, environment `release`; `NUGET_USER` can override the default Seddryck account. The release guard blocks publication while a legacy `appveyor.yml` exists.

For local versioned packaging, use GitVersion 6.6.0 (the version pinned in CI):

```powershell
$version = gitversion /showvariable SemVer
dotnet pack DubUrl.Schema/DubUrl.Schema.csproj -c Release -p:Version=$version
python build/build_docs.py --version $version
./build/Test-ReleaseGuards.ps1
python build/test_gitversion.py --gitversion <path-to-dotnet-gitversion>
```

Local unversioned builds use `0.0.0`, as in DubUrl. There is no need to modify build properties when preparing a release.

## Single documentation website

`schema-docs.zip` is a reproducible Jekyll source bundle with front matter and stable specification paths. The theme and hosting remain in Seddryck/DubUrl. PRs upload the bundle as a CI artifact. Preview an unreleased artifact using a temporary lock with its SemVer, source commit and checksum; production consumes reviewed GitHub releases.

Merge this repository first to release the initial 0.100.0 automatically, then update the consuming DubUrl PR's package version and CLI/worker lockfiles. Pin the actual published ZIP checksum/source commit in `docs/schema-docs.lock.json` (a squash merge changes the commit). Build with `python build/import_schema_docs.py`, which stages the complete site under `artifacts/docs-site` while retaining the existing URLs and JSON specification identity. Subsequent website updates pin a zero-patch GitHub release; NuGet-only patch versions do not imply a new documentation release.

Dispatch CI with `duburl_version` to test a candidate DubUrl/core provider package set. Manual dispatches never publish, matching the main-branch push boundary in the sibling repositories.

## Packaging the unit-matrix outputs

Each successful unit job uploads the three library DLLs and portable PDBs for its TFM, optional documentation/dependency files, and a SHA-256 manifest identifying the version and source commit. The package job downloads all three artifacts into their original project/bin/Release/TFM paths and verifies their identities/hashes. No framework-scoped obj or NuGet cache is transferred.

Packaging restores the complete multi-target project metadata on its own runner, then runs `dotnet pack --no-build --no-restore`. It does not rebuild the libraries. After packing, every DLL in the NuGet packages and every PDB in the symbol packages must exactly match the unit-matrix manifests. The package-only consumer smoke programs are built separately on all three runtimes.

Only .NET 10 runs live database tests, including the embedded DuckDB integration cases in the otherwise multi-target unit suite. Package contents retain all three supported TFMs.


## CI SDK and runtime reuse

The local setup action inspects installed SDKs and Microsoft.NETCore.App runtimes before downloading anything. .NET 10 jobs use the highest installed stable 10.0 SDK/runtime; SDK 11 and preview versions are excluded. A job-local global.json pins the detected SDK with roll-forward disabled and previews disallowed, so an installed newer compiler cannot silently take over.

Only missing components invoke Microsoft's dotnet-install script. A missing SDK is installed in RUNNER_TEMP and already includes its runtime; availability is checked again before any runtime-only installation. Missing runtimes are installed into the selected SDK root. There is no unconditional LTS runtime installation. The action exports PATH and DOTNET_ROOT for the job without updating registry or persistent user PATH settings.

The .NET 8/9 unit entries keep their existing exact SDK versions, reusing them when present. Packaging needs only a stable .NET 10 SDK and the 8/9 runtimes for its consumer smoke programs; it no longer installs the 8/9 SDKs. All three runtimes are reused when present and only missing runtimes are installed.

Run ./build/Test-DotNetSetup.ps1 to check stable selection, missing-component fallbacks, generated SDK selection and the real wrapper's no-download path. The tests use isolated inventory/installer fixtures and do not install or alter local SDKs.
