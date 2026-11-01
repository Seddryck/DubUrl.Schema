# Real-database schema QA

```powershell
./QA/run.ps1 -Database Sqlite -Framework net10.0
./QA/run.ps1 -Database DuckDb -Framework net10.0
./QA/run.ps1 -Database PostgreSql -Framework net10.0
```

The runner builds only the selected fixture and framework, provisions a disposable database on a free local port, waits for readiness, requires nonzero passing tests with no skips, saves redacted logs and TRX reports, and removes its own container/volumes in `finally`. It never connects to an existing database. Remove old `artifacts/results/<Database>/<Framework>` output before repeating a run. PostgreSQL, SQL Server, Oracle, SingleStore and MySQL require Docker; SQLite and DuckDB run their actual embedded engines. Oracle and SingleStore also retain the original pinned images and startup readiness support. PostgreSQL retains its original pinned image. SQL Server uses an explicit CU image.

CI runs all seven database jobs on .NET 10 only. The unit matrix still covers .NET 8, 9 and 10; its embedded DuckDB integration cases run only in the .NET 10 entry. PostgreSQL/SQL Server/Oracle/SingleStore/MySQL use Linux; SQLite/DuckDB retain Windows. SchemaContract and DiscoveryContract are shared internally through `DubUrl.Schema.QA`, which is not published as a NuGet package. Provider connection, raw driver metadata and application-level integration tests remain in DubUrl.

To use a deliberately configured disposable database without provisioning, run `dotnet test` directly and set the matching `DUBURL_*_QA_CONNECTION_STRING` variable. Discovery tests also require `DUBURL_POSTGRESQL_DISCOVERY_QA_CONNECTION_STRING` or `DUBURL_SQLSERVER_DISCOVERY_QA_CONNECTION_STRING`. The normal runner sets all required variables and fails on skips.


## Shared baseline and provider features

All seven providers inherit SchemaContract from the internal DubUrl.Schema.QA helper. The baseline declares table-creation support, renders and executes a two-column table, queries it and drops it. Provider fixtures supply the connection, dialect and identifier quoting; Oracle additionally removes the SQL*Plus terminator and uses plain DROP TABLE because its fixture does not declare DropTableIfExists support.

The runner accepts only net10.0 and verifies both named baseline results passed exactly once in each provider's TRX. The CI database matrix remains net10.0 only. Projects can still compile alongside the multi-target solution; database execution is restricted to .NET 10.

Additional feature tests remain separate: PostgreSQL/SQL Server share discovery tests; SQLite tests its metadata subset; DuckDB tests deployment and indexes; SQL Server tests extended-property comments; Oracle checks its catalog metadata. MySQL has a dedicated baseline QA project and retains its three bounded-key/check/comment integration cases in the existing test project, executed as a second .NET 10 run with independent result validation. Feature support is asserted only where implemented; the discovery fixture remains specific to PostgreSQL and SQL Server.
