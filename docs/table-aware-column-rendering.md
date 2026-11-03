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

This change requires the companion Core/provider implementation in DubUrl issue
1770 ([implementation PR](https://github.com/Seddryck/DubUrl/pull/1787)).
The column contract is published in DubUrl 0.57.0. This consolidated PR also
includes namespace changes from [DubUrl PR #1788](https://github.com/Seddryck/DubUrl/pull/1788),
which are not in that release. All Core/provider packages use the same unpublished
local `0.57.1-schema-options.1` validation candidate. Keep this PR in draft until
that companion PR merges and compatible packages are published; replace the
candidate pin before merging. See [combined validation](../build/VALIDATION.md).

Column plans and table plans retain separate ownership: the shared template
suppresses clauses consumed by either plan while preserving original key context for column planning. A column plan claiming primary/unique clauses already owned by a table plan is rejected explicitly. Providers not implementing these
optional contracts keep their existing rendering paths. Real-database QA runs
only on .NET 10.