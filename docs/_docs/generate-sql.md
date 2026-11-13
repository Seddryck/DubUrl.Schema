---
title: Generate SQL
parent: Working with schemas
nav_order: 4

---

SQL generation validates and maps a document, then renders it for a registered dialect. It does not require a live database or an installed ADO.NET provider.

## From the command line

```shell
duburl schema render schema.yaml --dialect pgsql --output schema.sql
```

Without `--output`, SQL is written to standard output. Use `duburl capabilities --json` to discover renderable dialect aliases. Review the generated SQL before deployment.

## Read, validate, map, and render in .NET

`SchemaDocumentReader` infers JSON from `.json` and YAML from `.yaml` or `.yml`. It requires an explicit supported version, rejects unknown properties, reports malformed input with source and location information, and leaves caller-owned streams open.

```csharp
using DubUrl.Schema;
using DubUrl.Schema.Documents;
using DubUrl.Querying.Dialects;
using DubUrl.Provider.PostgreSql.Sql;

var reader = new SchemaDocumentReader();
var document = reader.Read("schema.yaml");

var validator = new SchemaDocumentValidator();
var diagnostics = validator.Validate(document);
var errors = diagnostics
    .Where(item => item.Severity == SchemaDocumentDiagnosticSeverity.Error)
    .ToArray();

if (errors.Length > 0)
    throw new InvalidOperationException(string.Join(
        Environment.NewLine,
        errors.Select(item => $"{item.Code} at {item.Path}: {item.Message}")));

var schema = new SchemaDocumentMapper(validator).Map(document);

var dialects = new DialectRegistryBuilder();
dialects.AddDialect<PgsqlDialect>(["pgsql"]);
var sql = new SchemaScriptRenderer(dialects.Build().Get<PgsqlDialect>()).Render(schema);
```

Validation accumulates diagnostics without mutating the DTO. Each diagnostic has a stable `SDVnnn` code, severity, message, and document path. `SchemaDocumentMapper` always validates before mapping and throws `SchemaDocumentMappingException` with all error diagnostics when the document is invalid, so explicit validation is optional when the caller does not need to present errors separately.

For a stream, specify the format and optionally a source name:

```csharp
var document = reader.Read(stream, SchemaDocumentFormat.Yaml, "embedded-schema.yaml");
```

## Render the lifecycle

The following example uses a runtime `schema` built with the [C# schema builders]({{ '/schema-model/' | relative_url }}).

```csharp
var dialects = new DialectRegistryBuilder();
dialects.AddDialect<PgsqlDialect>(["pgsql"]);

var renderer = new SchemaScriptRenderer(
    dialects.Build().Get<PgsqlDialect>(),
    SchemaCreationOptions.DropIfExists);

var sql = renderer.Render(schema);
```

Creation renders tables and their constraints first, followed by deferred foreign keys, comments, indexes, and dependency-ordered views. With `DropIfExists`, dependent views are dropped first, then indexes, then tables. This ordering prevents the generated teardown from removing a source before its dependants.

Provider capabilities still apply. The renderer throws when the selected dialect cannot faithfully represent a requested identity, generated-column storage mode, regular-expression check, view, native type, or function.

For a portable JSON or YAML representation of the same model, see [Schema documents]({{ site.baseurl }}/schema-documents/).

## Preview hierarchy storage

Call `renderer.PreflightHierarchies(schema)` on the same `SchemaScriptRenderer` you will use to generate DDL. With a connection URL, call `connectionUrl.PreflightHierarchies(schema, options)`. The result for each table includes the selected representation, whether the target enforces parent existence, and a diagnostic for unsupported declarations. Preflight uses the target settings supplied to the renderer without opening a connection.

DuckDB generates `parent_id` with the node key's type and a self-referencing foreign key inside `CREATE TABLE`. PostgreSQL renders the foreign key in a separate command. SQLite and MySQL can declare the foreign key, but enforcement depends on connection or storage settings. Without confirmation from the provider, the declaration must explicitly allow storage without guaranteed enforcement.

## Safe column and constraint rendering

SQL Server and Teradata character columns require explicit dimensions. Use `new VarLengthColumn("name", DbType.String, 120)` (or `.WithLength(120)`) for bounded strings. For intentional unbounded SQL Server text, use `VarLengthColumn.Unbounded` (`-1`), which renders `NVARCHAR(MAX)` or `VARCHAR(MAX)`. Fixed-width strings and key columns cannot use `MAX`. Teradata requires bounded `VARCHAR`/`CHAR`; an explicitly selected native `CLOB` is available for large non-key text. No default business length is guessed. Native types and logical fallbacks follow the same dimension policy.

MySQL bounded logical strings render as `VARCHAR(length)`. Explicit native `VARCHAR`/`CHAR` dimensions are preserved. Primary, unique, unique-index and both foreign-key endpoints reject `TEXT`/`BLOB` key types with an error requesting bounded metadata.

DuckDB foreign keys render inside `CREATE TABLE`. Tables are created in dependency order and dropped in reverse order. Dependency cycles produce an error. Self references are supported by current DuckDB for rows referencing previously inserted parents; same-statement self references remain subject to engine limitations. SQLite retains inline foreign keys without requiring dependency order.

For an existing parent table, pass its metadata in `new Schema(tablesToCreate, indexes, views, externalTables: existingParents)`. External tables are validated as reference targets but never created or dropped. References without either created or external metadata still fail validation.

A `UniquenessConstraint` on a column is omitted when that column is the complete primary key. Table uniqueness can be declared with `new UniquenessConstraint(columns, name)`; a set matching the primary key is also omitted, regardless of column order. A singleton unique column within a composite primary key is preserved.

`SchemaScriptRenderer.EngineSupportsComments` describes database capability; `SupportsComments` describes the available renderer. SQL Server uses Unicode extended properties, MySQL uses column `COMMENT` clauses and table comments, and Snowflake uses `COMMENT ON`. Descriptions for an engine with an unimplemented renderer raise an actionable implementation-gap error. Engines without database comments, such as SQLite, expose false and omit descriptions.


## Deploy models with provider command boundaries

Use `connectionUrl.DeploySchema(schema, options)` or its fluent-builder overload to deploy a model. The renderer validates and builds the whole ordered plan before opening a connection. The deployer then executes each command on one connection, in order, stopping at the first error.

```csharp
var plan = new SchemaScriptRenderer(connectionUrl.Dialect, options).Plan(schema);
// Inspect command.Operation and command.ObjectName before execution if needed.
new SchemaScriptDeployer().DeploySchema(connectionUrl, plan);
```

Plans preserve sequence prerequisites, tables, foreign keys, comments, indexes and dependency-ordered views. Replacement drops dependent views and tables in reverse dependency order. Indexes belonging to those tables disappear with their tables and are recreated afterward. External reference tables are never dropped. SQL Server gives each `CREATE VIEW` a separate batch while keeping consecutive comments together. Oracle executes individual SQL commands without SQL*Plus terminators; explicitly marked procedural blocks retain their internal and final semicolons.

`Render` and `ScriptSchema` remain text-export APIs. Their output is a script for a suitable database client; it is not an execution plan. The existing `SchemaScriptDeployer.DeploySchema(connectionUrl, string)` overload still submits the exact supplied string as one command. It does not split semicolons, interpret `GO` or `/`, or apply provider terminator changes. Callers supplying strings own their batch boundaries and provider syntax.

Model deployment does not start a transaction or promise rollback. Oracle and MySQL DDL can commit implicitly; an error can leave earlier objects in place. The original provider exception is rethrown, preserving its type, code and stack, with zero-based `Data["SchemaCommandIndex"]`, `Data["SchemaCommandOperation"]`, and optional `Data["SchemaCommandObject"]` identifying the failed command. These details do not include SQL or connection URLs. Inspect and clean up partial deployments before retrying.

Provider implementations belong in DubUrl Core/provider packages. `ISchemaSqlRenderer.PlanCommands` receives complete trusted rendering units and owns batching and command terminators. `ISequenceIdentitySqlRenderer.PlanIdentityPrerequisites` must return explicit prerequisite commands; legacy string-only implementations fail before any commands execute rather than having their SQL guessed or split. Custom procedural units must set `IsProceduralBlock` when their terminator is part of the block.

Command-plan deployment requires DubUrl 0.60.0 or later. Align Core, dialect providers and optional native extensions to the same release.
