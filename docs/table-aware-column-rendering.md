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
`DubUrlColumnDefinitionVersion` pins both DubUrl and DubUrl.Provider.Sqlite to
`0.55.2-tableaware.2` for candidate validation; other providers remain on
`DubUrlVersion` (0.55.1). The candidate is local, not a published release. Keep the
Schema PR in draft until compatible Core/provider packages are published and this
pin is replaced with their actual released version. Do not merge with this temporary
candidate pin or publish Schema packages against it.

SQLite's broad SupportsIdentityColumns capability remains unchanged: older Schema
orchestrators cannot consume this hook, and arbitrary identity seeds/increments are
still unsupported. Release the compatible consumer before advertising capabilities
that depend on this optional contract.

To reproduce validation, pack DubUrl.Core/DubUrl.Core.csproj and
Providers/Sqlite/DubUrl.Provider.Sqlite/DubUrl.Provider.Sqlite.csproj from the companion
branch with `-p:Version=0.55.2-tableaware.2 -o <candidate-feed>`, then restore/build
Schema with `-p:RestoreAdditionalProjectSources=<candidate-feed>`. No user or
machine-wide NuGet configuration changes are required. Run database QA only with
`-f net10.0`.
