# Real-database schema QA

```powershell
./QA/run.ps1 -Database Sqlite -Framework net10.0
./QA/run.ps1 -Database DuckDb -Framework net10.0
./QA/run.ps1 -Database PostgreSql -Framework net10.0
```

The runner builds only the selected fixture and framework, provisions a disposable database on a free local port, waits for readiness, requires nonzero passing tests with no skips, saves redacted logs and TRX reports, and removes its own container/volumes in `finally`. It never connects to an existing database. Remove old `artifacts/results/<Database>/<Framework>` output before repeating a run. PostgreSQL, SQL Server, Oracle, SingleStore and MySQL require Docker; SQLite and DuckDB run their actual embedded engines. Oracle and SingleStore also retain the original pinned images and startup readiness support. PostgreSQL retains its original pinned image. SQL Server uses an explicit CU image.

CI runs all seven database jobs on every supported framework. PostgreSQL/SQL Server/Oracle/SingleStore/MySQL use Linux; SQLite/DuckDB retain Windows. SchemaContract and DiscoveryContract remain reusable in `DubUrl.Schema.Testing.Contracts`. Provider connection, raw driver metadata and application-level integration tests remain in DubUrl.

To use a deliberately configured disposable database without provisioning, run `dotnet test` directly and set the matching `DUBURL_*_QA_CONNECTION_STRING` variable. Discovery tests also require `DUBURL_POSTGRESQL_DISCOVERY_QA_CONNECTION_STRING` or `DUBURL_SQLSERVER_DISCOVERY_QA_CONNECTION_STRING`. The normal runner sets all required variables and fails on skips.
