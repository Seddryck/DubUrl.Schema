# Extraction validation (2026-11-01)

Validated on Windows with the installed .NET 11 preview SDK and .NET 8, 9 and 10 runtimes. CI selects the matching stable SDK for each target framework.

## Passed locally

- Full `DubUrl.Schema.sln` Release build for net8.0, net9.0 and net10.0, zero warnings/errors.
- `DubUrl.Schema.Testing`, excluding `MySqlIntegration`: 181 passed on each of net8.0, net9.0 and net10.0; nonzero TRX results explicitly validated.
- `DubUrl.Schema.Documents.Testing`: 127 passed on each of net8.0, net9.0 and net10.0, including the JSON specification and bundled examples.
- Real-database QA on net10.0: PostgreSQL 4, SQLite 3, DuckDB 2, SQL Server 4, Oracle 1, SingleStore 2, and MySQL 3 passed. No live tests skipped. Container tests used disposable databases and cleaned up their containers/volumes. Embedded tests used in-memory or temporary files.
- Three multi-target NuGet packages and their symbol packages packed and validated. Package-only consumer restored from a local feed and rendered a schema on all three TFMs.
- Two documentation producer tests: byte-for-byte reproducibility, complete manifest hashes, stable specification identity and full source-commit requirement.
- `actionlint` for the new workflow.
- Main repository integration: assembled Jekyll site builds; all 10 imported page URLs/local links and stable specification URL validate. Existing example verification compiled 31 snippet groups and executed 26.

## Not run locally

- Real-database QA on net8.0/net9.0; CI includes all seven databases on all three TFMs.
- Hosted Ubuntu/Windows CI jobs, cross-platform package execution outside Windows, and actual NuGet/GitHub publication.
- Unrelated provider, authentication and bulk-copy suites in the main repository; see its migration validation record for the focused integration tests.

An initial net10 unit run skipped the three environment-dependent MySQL cases. They were subsequently run successfully in the dedicated required MySQL database job. The CI unit filter excludes those cases, and every live job rejects skipped or empty test results.
