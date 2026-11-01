# Migration validation (2026-11-01)

Local validation used Windows, the installed .NET 11 preview SDK and .NET 8/9/10 runtimes. Hosted CI reuses an installed stable .NET 10 SDK/runtime, retaining exact SDK versions for .NET 8/9 unit entries and using .NET 10 for database jobs.

## Current unit matrix and artifact reuse

- Scoped Release/ContinuousIntegrationBuild solution builds passed for net8.0, net9.0 and net10.0 using the calculated GitVersion SemVer.
- Schema tests: 176 passed on net8.0, 176 on net9.0, and 181 on net10.0, with no skips. Five embedded DuckDB integration cases are excluded on net8.0/net9.0 and run on net10.0. MySQL integration cases run in their dedicated .NET 10 database job.
- Document/specification tests: 127 passed on each of the three TFMs, with no skips. Every run produced a validated nonzero TRX result.
- Five library-artifact regression tests passed: complete binary/symbol transfer, version/commit mismatch rejection, changed build/package payload rejection, unexpected manifest paths and required PDBs.
- Collected binaries and PDBs for all three package projects and all three TFMs. A fresh checkout received only those files and their manifests, without unit-job obj state. Deliberately invalid fixture source was added to each package project. Full multi-target metadata restore plus `dotnet pack --no-build --no-restore` succeeded, proving packaging did not compile the projects or their references.
- All three NuGet packages and symbol packages validated. Every packaged DLL and PDB exactly matched its unit-matrix SHA-256 manifest. Package-only consumers passed on net8.0/net9.0/net10.0 with a fresh NuGet cache, preventing an older same-version local package from satisfying the check.
- `actionlint` passed for the revised workflow. CI now has seven database jobs on .NET 10 only, while unit builds/tests and package consumer checks retain all three TFMs.

## Earlier migration checks retained

- Real databases on net10.0: PostgreSQL 4, SQLite 3, DuckDB 2, SQL Server 4, Oracle 1, SingleStore 2 and MySQL 3 passed without skips. Disposable containers/volumes and embedded files were cleaned up.
- Six GitVersion 6.6.0 real-history cases passed: first 0.100.0 after extraction/fix/CI commits, tagged patch progression, feature/minor then patch, breaking major, explicit minor annotation and beta feature branch. Tested-commit/version/tag-reuse/collision guards passed.
- Two documentation producer tests passed for deterministic ZIP metadata/content and complete manifest hashes with the stable specification identity. Windows and Linux produced identical ZIP bytes for the same commit/version.
- The canonical coverage gate passed against all three downloaded hosted unit artifacts, excluding duplicate VSTest In/ attachments.
- The main repository's assembled Jekyll website built; all 10 imported page URLs/local links and the JSON specification URL validated. Its example verifier compiled 31 snippet groups and executed 26. Main integration checks are recorded separately in build/schema-migration-validation.md in DubUrl.

## Not run for the latest CI-only follow-up

- External database suites were not rerun; their SQL/fixtures are unchanged. Embedded DuckDB cases did run in the revised .NET 10 unit entry.
- Database tests on net8.0/net9.0 are intentionally excluded from CI and were not run locally.
- GitVersion, release guard and documentation producer tests were not repeated for artifact-transfer changes; their earlier results are listed above.
- Unrelated main-repository suites, site deployment, package publication and remote tag creation were not performed.


## Installed SDK/runtime reuse follow-up

- Eleven PowerShell regression scenarios passed for stable-version selection, no downloads when components are present, SDK-only/runtime-only installation, bundled-runtime reuse, preview rejection, additional consumer runtimes, unchanged older SDK pins, failed/partial SDK installs, preserved global.json settings and the real setup wrapper. The wrapper scenario blocks downloads and verifies SDK/environment selection.
- PowerShell syntax checks and actionlint passed. The full Release solution built on all three TFMs with zero warnings/errors.
- Installation callbacks were simulated; no SDK/runtime downloads or machine configuration changes were performed locally. Full domain/database suites were not repeated for this CI setup-only change.

## Codecov follow-up

- Matched DubUrl's pinned Codecov action, GitHub OIDC permissions and fail-on-error behavior, with a separate upload job. Coverage artifact selection uses only the original collector paths, excluding duplicate VSTest attachments.
- Workflow lint and report selection were checked against downloaded hosted unit artifacts. No application code changed; domain/database suites and the solution build were not repeated for this upload-only change.
- Actual service upload is validated by the pushed PR workflow; Codecov repository activation remains an external prerequisite.

## Shared QA project rename

- Renamed DubUrl.Schema.Testing.Contracts to DubUrl.Schema.QA, including its namespace, solution entry and provider references. Removed unused DuckDB/Oracle references.
- The shared fixture helper is internal and non-packable. CI transfers and publishes only the Schema and Documents library outputs; package validation expects exactly those two multi-target packages plus symbols.
- Five artifact integrity tests, workflow lint, the full three-TFM Release solution build (zero warnings/errors), all three SQLite QA tests on .NET 10 (zero skips), and validation of both no-build packages passed.
- Other database suites, full unit suites and consumer smoke programs were not rerun for this rename.
