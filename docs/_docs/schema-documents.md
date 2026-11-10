---
title: Schema document format
parent: Reference
nav_order: 3
permalink: /schema-documents/
---

DubUrl schema documents describe a database schema in ordered, provider-independent JSON or YAML. Version 1 covers tables, columns, primary and foreign keys, unique tuples, check constraints, generated columns, indexes, and views. Primary keys use the table-level form.

Views use a deliberately small structured query model: one table-or-view source and an ordered list of column, value, or function projections with optional aliases. View dependencies are derived from their sources, so creation and removal can be rendered in dependency-safe order. Raw SQL, joins, aggregation, grouping, ordering, pagination, common table expressions, and set operations are not part of version 1.

For a tutorial, start with [Write a schema document]({{ '/docs/write-schema/' | relative_url }}). This page specifies the version 1 format.

## Version 1 model

Declarations are ordered. That order is retained for tables, columns, constraints, index columns, and projections; views are reordered only when their source dependencies require it.

| Element | Version 1 behavior |
|---|---|
| Object identity | `catalog`, `database`, and `schema` are optional qualifiers; `name` is required. |
| Tables | Contain ordered columns, one optional table-level primary key, table checks, and named foreign keys. |
| Columns | Support scalar or structured types, nullability, uniqueness, defaults, descriptions, generation, and column checks. Scalar types can also specify length, precision/scale, and native-type fallback. |
| Primary keys | Are table-level, named or unnamed, single-column or composite, with ordered unique column references. |
| Unique tuples | Use optional table-level `uniqueConstraints` with ordered `columns`, an optional `name`, and an optional `nullPolicy`. |
| Checks | Support comparison, membership, and regular-expression forms with structured expressions and explicit null behavior. |
| Indexes | Have their own qualified identity, target a table, can be unique, and preserve per-column ascending or descending direction. |
| Views | Have one table-or-view source and ordered structured projections with optional aliases. |

Column-level `unique: true` and tuple declarations with an omitted or `native` policy use the database's native behavior. With `nulls-distinct`, the uniqueness rule applies only when every member is non-null. With `nulls-not-distinct`, nulls compare equal at the same position and non-null members compare normally. For example, two tuples `(null, 1)` conflict under `nulls-not-distinct`, while `(null, 1)` and `(null, 2)` can coexist.

The v1 addition is optional. It leaves the meaning of existing documents intact. An explicit policy requires provider support when rendering or deploying. A tuple equivalent to the primary key stays in the document and shares the primary key's enforcement in SQL.

`length` applies only to string and binary types. `precision` and `scale` apply only to `currency`, `decimal`, and `varnumeric`, and scale requires precision. Identity generation applies only to integer types. Length and precision are mutually exclusive. Choose at most one of `default`, `defaultExpression`, `identity`, and `generated`.

The supported logical type identifiers are:

```text
ansi-string, ansi-string-fixed-length, binary, byte, boolean, currency,
date, datetime, datetime2, datetime-offset, decimal, double, guid,
int16, int32, int64, object, sbyte, single, string,
string-fixed-length, time, uint16, uint32, uint64, varnumeric, xml
```

Typed literal values use invariant string representations. Binary values are Base64; dates and date-times use round-trip parsing; `null` omits `value`. A column default's literal type must match the column logical type.

### Structured columns

A v1 column can declare `structuredType` with a recursive `shape` and an explicit `storage`. Existing `type` and `nativeType` declarations retain their v1 behavior. A structured column cannot also specify a scalar type, native override, scalar facets, identity generation, or an expression default. Its only supported literal default is SQL NULL on a nullable column.

```yaml
name: readings
nullable: false
structuredType:
  storage: native
  shape:
    kind: array
    elementNullable: false
    element:
      kind: struct
      fields:
        - name: sensor.id
          nullable: false
          type: { kind: scalar, type: string }
        - name: sample
          nullable: true
          type: { kind: vector, type: single, dimensions: 3 }
```

| Shape | Members |
|---|---|
| `scalar` | Nested leaf with a logical `type` other than `object`. |
| `json` | Arbitrary JSON, or a JSON object described by an optional struct in `objectShape`. |
| `array` | `element` shape and `elementNullable`. |
| `map` | `key` and `value` shapes and `valueNullable`. Map keys are non-null. |
| `struct` | Ordered `fields`, each with its literal, case-sensitive `name`, recursive `type`, and `nullable`. Names must be unique within the struct. |
| `vector` | Numeric component `type` and a positive `dimensions` count. Components are non-null. |

Column `nullable` describes the whole value. Omitted nested nullability records unknown metadata and can be exported. SQL planning requires known nested nullability. The descriptor records the shape without certifying database enforcement.

`storage` accepts `native` for an engine's structured type, `json` for a database JSON type, `json-text` for JSON stored as text, and `binary` for binary storage. The caller selects storage and any value codec. In fluent schemas, use `WithStructuredType` with a `StructuredColumnType`. SQL export and deployment require a provider that implements `IStructuredColumnSqlRenderer` and supports the requested combination. The provider supplies the SQL type to the existing column-definition pipeline. Dialect implementations and live QA are tracked in [DubUrl.Providers #13](https://github.com/Seddryck/DubUrl.Providers/issues/13).

### Expression defaults

`defaultExpression` takes a symbolic `kind` and only the arguments listed below. The database evaluates the expression when an INSERT omits the column or explicitly requests DEFAULT. An explicit value, including NULL where allowed, bypasses the default. Existing v1 literal declarations use the same `default` field. Use `default: { type: "null" }` to declare a SQL NULL default explicitly.

| Kind | Destination and arguments |
|---|---|
| `current-timestamp` | `datetime`, `datetime2`, or `datetime-offset`. Uses session-local wall time and includes the session offset for an offset destination. |
| `current-utc-timestamp` | Same timestamp types. Uses UTC independently of session and server time zones and uses offset zero for an offset destination. |
| `current-date` | `date`. The calendar date in the inserting session. |
| `current-time` | `time`. The time of day in the inserting session. |
| `current-user`, `session-user` | A string with a declared length that can hold the provider's effective or session authorization identity, respectively. The database may allow the session identity to change. |
| `current-schema`, `current-database` | A string with a declared length that can hold the inserting session's context. Providers may confirm an alias between the two. A route that can return NULL requires `nullable: true`. |
| `new-uuid` | Set `representation` to `native` for `guid`, `binary` for exactly 16 bytes, or `canonical-text` for a hyphenated 36-character value. Text needs at least 36 characters, or exactly 36 for a fixed-length destination. Binary requires `byteOrder` set to `network` or `dotnet-mixed-endian`. |
| `next-sequence-value` | An integer or exact numeric type with room for an integral part. Supply `sequence` with a `name` and at least one of `schema`, `database`, and `catalog`. Each component is a literal identifier. |

Timestamps and time of day accept optional `fractionalSecondsPrecision` from 0 to 9. The provider must honor the requested precision. Omission uses the engine's default precision. Clock evaluation can follow the engine's statement or transaction clock. UUID version, ordering, and randomness depend on provider guarantees.

Context defaults use a nonnullable destination unless it is explicitly declared nullable.

```yaml
name: invoice_number
type: int64
defaultExpression:
  kind: next-sequence-value
  sequence:
    schema: billing
    name: invoice_numbers
```

For fluent schemas, pass the corresponding `ColumnDefaultExpression` factory or property to `WithDefaultExpression`. An external sequence must already exist when the database requires it. Schema creation and replacement only manage the declared tables, leaving the referenced sequence under its existing ownership.

Planning and SQL export require the provider's `IColumnDefaultSqlRenderer` capability. `SDD001` indicates an unsupported expression or unresolved reference. `SDD002` indicates an incomplete provider plan. Native SQL and live qualification are tracked in [DubUrl.Providers #11](https://github.com/Seddryck/DubUrl.Providers/issues/11).

`nativeType` is an intentional portability escape hatch. Its `fallback` is either `error` or `logical-type`, which tells the renderer whether an unregistered provider-native type must fail or may fall back to the portable `type`.

Expressions for checks, generated columns, and views have one of three shapes: a column reference, a typed literal value, or a function with ordered expression arguments. Comparison checks accept `=`, `<>`, `!=`, `<`, `<=`, `>`, and `>=`. Membership values must share a type; null membership requires `match-null`. Membership and regex checks accept a column expression or a single-column function expression.

## JSON Schema

Version 1 is published as a [Draft 2020-12 JSON Schema]({{ site.baseurl }}/schemas/schema-document/v1/schema.json). The same file is included in the `DubUrl.Schema.Documents` NuGet package at `schemas/schema-document/v1/schema.json`.

Set `version` to `1`. Unknown properties are rejected throughout the document so misspellings and unsupported features fail early. The JSON Schema checks document shape and local value constraints; `SchemaDocumentValidator` additionally checks references and relationships such as whether named columns exist.

See the complete [JSON example]({{ site.baseurl }}/schemas/schema-document/v1/examples/schema.json) and [YAML example]({{ site.baseurl }}/schemas/schema-document/v1/examples/schema.yaml).

## Related guides

- <span id="read-validate-map-and-render"></span>[Read, validate, map, and render in .NET]({{ '/docs/generate-sql/#read-validate-map-and-render-in-net' | relative_url }}).
- <span id="configurable-validation"></span><span id="configurable-literal-conversion"></span>[Customize validation and literal conversion]({{ '/docs/customize-schema/' | relative_url }}).
- <span id="editor-association"></span>[Configure your editor]({{ '/docs/write-schema/#editor-association' | relative_url }}).
