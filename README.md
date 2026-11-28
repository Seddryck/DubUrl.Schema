# DubUrl.Schema

Provider-neutral database schema models, fluent builders, SQL rendering, discovery mapping, and versioned YAML/JSON documents.

The user documentation is published as part of the [single DubUrl website](https://seddryck.github.io/DubUrl/docs/schemas/).

## Table and column tags

Use `WithTags` on table and column builders or set the `Tags` property on the schema model. In v1 JSON and YAML, use the optional `tags` map. For example, `tags: { pii: "true" }` assigns a string value. Tag keys are case-sensitive, and tags and annotations can use the same key independently.

Providers implement `ISchemaTagMetadataReader` to discover tags. Inspection reports `TableTagCoverage` and `ColumnTagCoverage` separately. A provider that supports neither scope leaves both as `Unsupported`. Inaccessible or unrepresentable tags make the inspection incomplete.

For deployment, providers implement `ITableTagSqlRenderer` and `IColumnTagSqlRenderer` for the scopes they support. `SchemaScriptRenderer` reports these capabilities separately and rejects declarations for unsupported scopes during planning.

## Table hierarchies

Use `WithHierarchy(new Hierarchy("id"))` on a table builder with a single-column scalar primary key. In schema documents, set `hierarchy: { nodeKey: id }` on the table. The declaration requires database enforcement of parent existence by default.

```csharp
var renderer = new SchemaScriptRenderer(dialect);
var hierarchies = renderer.PreflightHierarchies(schema);
```

Preflight reports each table's representation, enforcement, and any diagnostic. It uses the renderer's dialect and options without connecting to a database. A plan with `IsSupported` set to false prevents DDL generation.

DuckDB generates a nullable `parent_id` with the node key's type and a self-referencing foreign key in `CREATE TABLE`. Each row with a null parent is a root. The foreign key checks that parents exist and permits cycles. Insert a parent before any statement that inserts a child referencing it. Set `requireParentExists` to false to allow storage without enforcement on a target where enforcement is unavailable.

## Inspect Analysis Services metadata

Use the read-only `AnalyticalMetadataInspector` with the ADOMD provider registered for the connection URL.
The inspector lists visible catalogs and reads objects in the connection's current catalog. The URL
overload opens and disposes its own connection. The overload taking an already open connection leaves
its ownership and selected catalog with the caller.

```csharp
using DubUrl.Discovery;
using DubUrl.Schema;

var result = await new AnalyticalMetadataInspector().InspectAsync(connectionUrl, cancellationToken);
var measures = result.Objects.Where(item => item.Id.Kind == AnalyticalObjectKind.Measure);
var measuresComplete = result.Coverage[AnalyticalFeature.Measures] == AnalyticalCoverage.Available;
```

The tabular reader supplies tables, columns, measures, and perspectives. On endpoints without modern
tabular rowsets, it can read table and column inventory from legacy DBSCHEMA rowsets and measures from
MDSCHEMA. The multidimensional reader supplies cubes, dimensions, hierarchies, levels, measures, and
perspectives. Parent IDs link objects in their own scope. Use `PerspectiveMembers` for membership,
`Caption` for display, and `Id` or a reported `UniqueName` for identity. Native rowset IDs are catalog
or scope specific and can change if objects are recreated.

Check coverage before treating a missing object as a failed assertion. An available collection can be
empty. Partial, unsupported, and inaccessible metadata have separate coverage values. Data types
carry the native rowset value, the normalized `DbType` when known, and any reported length,
precision, or scale. Missing facets have null values. A missing or unmapped type is diagnosed per object.
Named sets, member properties, standalone measure groups, and dimension and measure-group relationship
assertions are outside this inspector's current coverage.

For live consumer checks, set `DUBURL_ADOMD_TABULAR_TEST_URL` and
`DUBURL_ADOMD_MULTIDIM_TEST_URL` to test models with the requested object types and run the filter
`FullyQualifiedName~AnalyticalMetadataInspectorTests` in `DubUrl.Schema.Testing`.

## Build and test

```powershell
dotnet build DubUrl.Schema.sln -c Release
dotnet test DubUrl.Schema.Testing -c Release -f net10.0 --no-build
dotnet test DubUrl.Schema.Documents.Testing -c Release -f net10.0 --no-build
```

CI selects a complete stable DubUrl release for the provider fixtures. Directory.Build.props sets separate minimum versions for DubUrl Core and the provider fixtures. Use `-p:DubUrlCoreVersion=<candidate>` to validate another Core and ADOMD release, or `-p:DubUrlVersion=<candidate>` to select a different provider fixture release. See [real-database QA](https://github.com/Seddryck/DubUrl.Providers/tree/1f07066a4584cc75a93ea8f3a8cb8868ea8948a6/QA/Schema) and [validation](build/VALIDATION.md).

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
