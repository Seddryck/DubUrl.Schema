# Real-database schema QA

```powershell
./QA/run.ps1 -Database Sqlite -Framework net10.0
./QA/run.ps1 -Database DuckDb -Framework net10.0
./QA/run.ps1 -Database PostgreSql -Framework net10.0
```

The runner builds only the selected fixture and framework, provisions a disposable database on a free local port, waits for readiness, requires nonzero passing tests with no skips, saves redacted logs and TRX reports, and removes its own container/volumes in `finally`. It never connects to an existing database. Remove old `artifacts/results/<Database>/<Framework>` output before repeating a run. PostgreSQL, SQL Server, Oracle, SingleStore and MySQL require Docker; SQLite and DuckDB run their actual embedded engines. Oracle and SingleStore also retain the original pinned images and startup readiness support. PostgreSQL retains its original pinned image. SQL Server uses an explicit CU image.

CI runs all seven database jobs on .NET 10 only. The unit matrix still covers .NET 8, 9 and 10; its embedded DuckDB integration cases run only in the .NET 10 entry. PostgreSQL/SQL Server/Oracle/SingleStore/MySQL use Linux; SQLite/DuckDB retain Windows. SchemaContract and DiscoveryContract are shared internally through `DubUrl.Schema.QA`, which is not published as a NuGet package. Provider connection, raw driver metadata and application-level integration tests remain in DubUrl.

To use a deliberately configured disposable database without provisioning, run `dotnet test` directly and set the matching `DUBURL_*_QA_CONNECTION_STRING` variable. Discovery tests also require `DUBURL_POSTGRESQL_DISCOVERY_QA_CONNECTION_STRING` or `DUBURL_SQLSERVER_DISCOVERY_QA_CONNECTION_STRING`. The normal runner sets all required variables and fails on skips.


## Mandatory shared provider tests

Every provider fixture inherits ProviderContract (which extends SchemaContract) from the internal DubUrl.Schema.QA helper. All seven providers must implement the abstract connection, dialect, script execution, error-code, qualification, comments, column/index catalog, object-existence, metadata-reader and public-deployment hooks. A missing override is a compilation error. Shared code owns the assertions; provider code returns typed observations.

QA/shared-scenarios.json is the authoritative list of 60 required scenarios. The NUnit case source embeds that same manifest; each provider declares an explicit expectation for every scenario and rejects missing or extra declarations. The QA runner validates both original baseline results and every Shared_<scenario> result passed exactly once. Missing, duplicate, skipped or failed cases fail CI. NoDiscovery can only apply to discovery/diagnostic scenarios, never to disable deployment, constraint or comment tests.

The scenarios cover deployment/replacement, quoted names, comments and empty descriptions, type/facet/null/default behavior, identities and both generated-column modes, single/composite keys and relationships, dependency order, checks/null semantics/regex, indexes and sort order, views, feature-specific discovery/filtering/closure, partial/strict metadata, round trips, native CHAR/VARCHAR keys, expression/filtered-index and aggregate-view diagnostics, public and fluent deployment, and cleanup after failure.

All original provider-specific regression fixtures remain, including SQL Server's qualified/unqualified same-batch extended-property comments with literal dots/apostrophes/Unicode/CRLF, SQLite's rich metadata diagnostic counts, Oracle's catalog check, DuckDB's public deployment/index tests, and MySQL's three inferred/native bounded-key/check/comment cases. MySQL's original feature cases run separately on the same disposable server and retain independent TRX validation.

## Explicit current limitations

A passing scenario means the declared behavior was verified, not that every engine implements the feature. Unsupported behavior is asserted without skips; each expectation includes its rationale and any required exception/error code. The expectations are written explicitly in each provider's Features.cs rather than inferred from the implementation under test.

| Provider | Current expectations verified by the shared tests |
|---|---|
| PostgreSQL | Portable virtual generated columns are rejected; UNIQUE discovery reports SDI006 rather than inventing an index. |
| SQLite | Comments have no native storage; portable identity/regex rendering is rejected; generated-expression/check/comment metadata has explicit diagnostics. The published unquoted identifier formatter rejects punctuation; discovered Schema qualifiers cannot currently be rendered back. String length is not enforced. |
| DuckDB | Stored generated columns and filtered indexes are rejected; declared string length and index sort direction are not retained. |
| SQL Server | Regex rendering is unavailable; expression indexes require computed columns. The adapter executes CREATE VIEW in its required separate batch, while comments stay together to detect batch-variable collisions. |
| Oracle | The published dialect lacks comment, identity, generated-column, regex and portable-view renderers; it registers no schema discovery reader. Ordinary native catalog observations and supported deployment/index behavior are still tested. |
| MySQL | Custom identity seed/increment is rejected; the provider registers no discovery reader. All three bounded-key forms, constraints and persisted comments remain tested. |
| SingleStore | Foreign keys and portable UNIQUE declarations lacking shard-key configuration are rejected with specific codes; MySQL generated-column syntax is rejected. The pinned engine accepts but ignores CHECK clauses and table comments, while column comments persist. No discovery reader is registered. |

Where discovery is absent, every discovery scenario verifies the real provider has no SchemaDiscoveryCapability and SchemaInspector returns SDI001. Where a reader exists, tests execute live DDL, read actual metadata, and assert values/coverage/diagnostic codes. This does not substitute fabricated metadata for a missing reader.

## Framework and cleanup guarantees

The runner accepts only net10.0. A global NUnit setup guard also prevents every provider fixture, including retained regressions, from touching a database under another runtime. QA projects still compile with the multi-target solution; the CI database matrix contains .NET 10 only.

Each shared scenario opens its own connection, uses unique object names, records partially created objects, and verifies cleanup in finally. Embedded public-deployment tests use disposable files and close provider pools before deleting SQLite files. Container logs redact generated passwords and all provisioned containers/volumes are removed. MySQL readiness requires an authenticated TCP query against the initialized DubUrl database; the temporary socket-only bootstrap server cannot satisfy it.
