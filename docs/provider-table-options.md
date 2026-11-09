# Provider-owned table definitions

The table contracts and coordinated namespace/column contracts are available in
published DubUrl Core/provider 0.57.1. Keep all DubUrl/provider package versions
aligned using `DubUrlVersion`; Schema and Schema.Documents ship together from
this repository. No unpublished candidate feed is required.

`SchemaScriptRenderer.Render(Schema)` is the orchestration entry point. Providers
without the optional interfaces retain portable table/index SQL and their
existing comment paths.

`ITableDescriptionSqlRenderer` is independent of physical options. Its result is
appended after the CREATE TABLE closing parenthesis and before the semicolon.
It receives the existing description and literal qualified table identity.
The post-create table comment is suppressed; inline and post-create column
comments retain their provider-defined behavior. A provider can implement this
interface without implementing `ITableDefinitionSqlRenderer`.

`Table.PhysicalOptions` uses Core's semantic `TablePhysicalOptions`: storage
(`Default`, `Rowstore`, `Columnstore`), shard columns, and ordered sort columns.
Null shard/sort keys preserve inference; empty lists explicitly request empty
keys. Schema snapshots the lists and validates enum values, duplicate names and
column membership. `Index.Structure` uses `SchemaIndexStructure` (`Default`,
`Ordered`, `Hash`). Providers validate engine restrictions and combinations.
No model accepts arbitrary SQL options.

Use `ITableBuilder.WithPhysicalOptions(...)` after `WithColumns(...)` and
`IIndexBuilder.WithStructure(...)` after `WithColumns(...)`. Existing constructors
remain available; new overloads accept options after the existing parameters.

Before emitting SQL, Schema passes all columns, the normalized primary key,
table/column unique keys, physical options and every index (including uniqueness,
structure and nullable direction) to `ITableDefinitionSqlRenderer` once per table.
This also validates tables without indexes. The returned CREATE prefix and
definitions replace portable primary/unique/index definitions, including column
keys. Other constraints retain their normal rendering. Indexes consumed by the
plan are omitted from CREATE INDEX output but retained for drop planning and
unique-column validation. Explicit options on a provider without this contract
throw `NotSupportedException`; they never silently fall back to portable SQL.
Lower-level template renderers do not perform schema-wide planning.

For example, SingleStore enforced unique keys must all include the nonempty
shard key. Select rowstore to support multiple unique keys and ordered indexes:

```csharp
var options = new TablePhysicalOptions(SchemaTableStorage.Rowstore, ["tenant"]);
var table = new Table(new DatabaseObjectName("items"), columns, constraints, null, options);
var index = new Index(new DatabaseObjectName("ordered"), table.Identity,
    [new IndexColumn("tenant", IndexSortDirection.Descending)],
    false, SchemaIndexStructure.Ordered);
var sql = new SchemaScriptRenderer(dialect).Render(new Schema([table], [index]));
```

SingleStore syntax and rejection decisions remain in its provider. In particular,
hash indexes cannot honor explicit direction and ordered indexes require
rowstore; columnstore ordering uses a sort key instead.

## Document compatibility

Document format v1 is unchanged: physical options are currently a code-only API.
The exporter rejects any non-null table physical options or non-default index
structure with `SchemaDocumentMappingException`, even explicitly empty/default
table options. It cannot silently discard them. Existing v1 DTOs, validation,
mapping and JSON Schema retain their semantics; no existing document is
reinterpreted. Persisting these choices requires a future explicitly versioned
document contract.

## SingleStore QA

The shared key/index cases explicitly choose rowstore and a compatible shard key;
comment cases verify persisted catalog values. Direction evidence comes from
`SHOW CREATE TABLE`, not `STATISTICS.COLLATION`, which can contain a character
collation rather than MySQL A/D. Real-database tests run only on .NET 10.

## Explicit UNIQUE policies

Declare table-level unique tuples with `WithConstraints(keys => keys.AddUnique(key => key.WithName("uq_pair").WithColumnNames("second", "first").WithNullPolicy(UniqueNullPolicy.NullsDistinct)))`. Declaration and member order are preserved.

`IUniqueConstraintSqlRenderer` is an opt-in extension of the dialect's schema renderer. Its context includes the containing table, canonical member columns with nullability and generation, physical table options, explicit indexes, and a deterministic name suggestion. For backing indexes, preserve the declared name when present and apply provider-specific identifier rules to generated names. Return null if the requested policy or table options cannot be honored.

A native plan supplies an optional provider-rendered null-policy clause between `UNIQUE` and the key columns. The shared constraint template renders the declared name and ordered columns inside `CREATE TABLE`. Omit the clause when the provider's default enforces the requested policy. An index plan supplies one or more ordered backing index identities. Each can include a provider-rendered clause after the key columns and a filter predicate without the `WHERE` keyword. The shared index template renders `CREATE UNIQUE INDEX` with the declaration's ordered columns and the dialect's identifier quoting. Schema emits each statement after creating the owning table and before deferred foreign keys. The provider reports whether the resulting ordered tuple can serve as a foreign-key target under its inline or deferred DDL rules. The existing table and column renderers receive only the legacy keys they own.

Discovery readers can implement `IUniqueConstraintMetadataReader` to report a known policy and the exact identities of indexes owned by a UNIQUE declaration. Schema omits only confirmed backing indexes from the independent index list. An ambiguous ownership claim or an overlapping unique index with unknown semantics produces `SDI010` under the existing strict/partial result rules.
