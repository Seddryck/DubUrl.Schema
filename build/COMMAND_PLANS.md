# Command-plan rollout and validation

Schema issue [#6](https://github.com/Seddryck/DubUrl.Schema/issues/6) consumes the Core/provider API from [DubUrl PR #1799](https://github.com/Seddryck/DubUrl/pull/1799), published in DubUrl 0.60.0. The candidate results below record validation before that release.

For local validation, Core and all referenced dialect/native providers were packed into one temporary feed as `0.56.0-command-plans.1`. This is a local validation version, not a published release. A temporary NuGet configuration enabled that feed plus NuGet.org; no machine-wide source configuration or credentials were changed. Builds/restores/tests used `-p:DubUrlVersion=0.56.0-command-plans.1`. Never mix candidate providers with newer stable native extensions that can transitively select a different Core binary.

## Checks performed

- `dotnet build DubUrl.Schema.sln -c Release --no-restore`: passed across net8.0, net9.0 and net10.0, with the candidate override.
- `dotnet format DubUrl.Schema.sln analyzers --verify-no-changes --no-restore`: passed.
- Focused `DubUrl.Schema.Testing` tests: 81 passed on each of net8.0, net9.0 and net10.0. The filter selected `SchemaCommandPlanTests`, `SchemaScriptRendererTests`, `SchemaScriptDeployerTests`, `ConnectionUrlExtensionsTests` and `SqlServerCommentRendererTests` by fully qualified class name.
- Core/provider `SchemaCommandPlanningTests`: 4 passed on net10.0; DubUrl solution build passed on net10.0 with existing warnings. Local candidate packing compiled the changed Core/providers for all three supported frameworks.
- `dotnet test QA/DubUrl.Schema.<Provider>.QA/DubUrl.Schema.<Provider>.QA.csproj -c Release -f net10.0 --no-build --no-restore --filter TestCategory=PublicCommandPlan`: Oracle 4, SQL Server 4, MySQL 4, SQLite 4 and DuckDB 5 passed.

Additionally, all six changed Oracle shared expectations passed: `Shared_TableComments`, `Shared_ColumnComments`, `Shared_CommentsSameBatch`, `Shared_QualifiedComments`, `Shared_UnqualifiedComments` and `Shared_ProjectionView`.

The public database tests verify model and fluent overloads, multiple tables, table/column comments containing SQL delimiters, chained views, reverse-order replacement, unique index enforcement, foreign-key deployment, first-failure diagnostics and partial-deployment cleanup. DuckDB additionally verifies sequence-backed generated values, seed/increment reset and sequence cleanup. Server tests used the repository's pinned disposable compose databases, generated in-memory passwords and automatic container/volume removal. SQLite and DuckDB used disposable files. All real database tests ran only on .NET 10.

Full unit suites, unrelated provider database suites, PostgreSQL/SingleStore QA and database tests on .NET 8/9 were not run. No packages were published and no pull requests were merged.
