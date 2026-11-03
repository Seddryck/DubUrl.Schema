# Table-aware column rendering

`IColumnDefinitionSqlRenderer` is an optional DubUrl.Core contract. Schema calls it
with resolved SQL type, object/column names, generation inputs, the entire primary
key (whether declared on the table or column), explicit nullability, uniqueness,
rendered default/check expressions and description. Core references no Schema models.

Return null to retain RenderIdentity, RenderGenerated and sequence prerequisites.
Otherwise return a complete type/generation specification and identify consumed
clauses. The template emits only unclaimed modifiers. A primary-key claim consumes
only the current column's single-column key; composite keys cannot be consumed by
one column plan. Generation claims also suppress auxiliary sequence creation.

SQLite uses this hook for INTEGER PRIMARY KEY AUTOINCREMENT, including named and
normalized single-column table keys. Composite keys, other resolved SQL types,
explicit nullable keys and non-default seed/increment are rejected before deployment.
The legacy context-free RenderIdentity entry point still rejects this operation.

## Dependency and release coordination

All three coordinated contracts are available in published DubUrl 0.57.1,
including [the namespace changes](https://github.com/Seddryck/DubUrl/pull/1788).
Core and every provider use the same `DubUrlVersion`; local candidate packages
and separate column-contract version overrides are no longer required.

Column plans and table plans retain separate ownership. The shared template
suppresses consumed clauses while preserving original key context for column
planning. A column plan claiming primary/unique clauses already owned by a table
plan is rejected explicitly. Providers without the optional contracts retain
their existing rendering paths. Real-database QA runs only on .NET 10.
