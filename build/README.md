# Release and documentation delivery

The libraries and reusable NUnit contracts are portable multi-target NuGet packages (`net8.0`, `net9.0`, `net10.0`). All packages and the documentation ZIP use the same GitVersion SemVer calculated once from the source commit. The DubUrl dependency remains independently pinned by `DubUrlVersion` in `Directory.Build.props`. No executable RID, libc, or architecture distribution matrix is needed. Native SQLite/DuckDB tests retain Windows runners; PostgreSQL, SQL Server, Oracle, SingleStore and MySQL use Linux containers.

## Automatic versions and releases

`GitVersion.yml` uses the same GitHubFlow/v1 workflow, Conventional Commit expressions, feature branch labels and continuous-deployment main branch as DubUrl; Expressif follows the same commit-based approach. `next-version: v0.100.0` (the prefix matches the inherited `tag-prefix: v`) bootstraps the first main release. It is a lower bound, not a version file to edit for every release. Subsequent versions are calculated from commits and `v` tags.

- `fix:`, `chore:`, `refactor:`, `perf:`, `build:`, `ci:` and the other configured patch types increase the patch.
- `feat:` or `+semver: minor` increases the minor.
- Matching breaking-change messages increase the major; `+semver: none`/`skip` uses the inherited no-bump rule.
- Feature branches carry the inherited `beta.{BranchName}` labels. Pull requests and manual compatibility runs validate and package their calculated version without publishing.

Successful pushes to `main` reserve a `v<SemVer>` tag for every tested package version and publish the NuGet packages automatically, following Expressif's tag policy. Those tags allow successive patch releases to advance (`0.100.0`, `0.100.1`, `0.100.2`). As in both sibling repositories, only a zero patch number creates a GitHub release with `schema-docs.zip`; patch releases publish to NuGet without a GitHub release. No manual `publish=true` switch or synchronized version files remain.

The pipeline builds/tests each supported TFM, runs the real-database matrix, checks coverage, packs and smoke-tests consumers, and then recalculates GitVersion after refreshing main/tags. It rejects tested-commit/version drift and tag collisions, including a concurrent tag creation at the wrong commit. Publication consumes the already-tested artifacts. Configure NuGet trusted publishing for Seddryck/DubUrl.Schema, workflow `ci.yml`, environment `release`; `NUGET_USER` can override the default Seddryck account. The release guard blocks publication while a legacy `appveyor.yml` exists.

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
