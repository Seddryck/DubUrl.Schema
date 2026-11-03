using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

internal static class TableDefinitionPlanning
{
    internal static void Apply(IDialect dialect, Schema schema, TableViewModel[] tables)
    {
        foreach (var table in schema.Tables.Values)
        {
            var indexes = schema.Indexes.Values.Where(index => index.TableIdentity == table.Identity).ToArray();
            if (dialect.SchemaSqlRenderer is not ITableDefinitionSqlRenderer renderer)
            {
                ValidatePortableOptions(dialect, table, indexes);
                continue;
            }

            foreach (var index in indexes) ValidateIndexNamespace(index.Identity, table.Identity);
            var plan = renderer.RenderTableDefinition(dialect, Context(table, indexes));
            ApplyPlan(tables.Single(item => item.Identity == table.Identity), plan);
        }
    }

    private static void ValidatePortableOptions(IDialect dialect, Table table, Index[] indexes)
    {
        if (table.PhysicalOptions is not null || indexes.Any(index => index.Structure != SchemaIndexStructure.Default))
            throw new NotSupportedException($"Dialect '{dialect.GetType().Name}' does not support physical table/index options.");
    }

    private static void ValidateIndexNamespace(DatabaseObjectName index, DatabaseObjectName table)
    {
        if ((index.Catalog is not null && index.Catalog != table.Catalog)
            || (index.Database is not null && index.Database != table.Database)
            || (index.Schema is not null && index.Schema != table.Schema))
            throw new NotSupportedException("An inline index must belong to its containing table's namespace.");
    }

    private static void ApplyPlan(TableViewModel model, TableDefinitionPlan plan)
    {
        model.CreatePrefix = plan.CreatePrefix;
        model.ProviderDefinitions = plan.Definitions.ToArray();
        model.PortableKeys = false;
        foreach (var column in model.Columns) column.TableConsumesKeys = true;
    }

    internal static TableDefinitionContext Context(Table table, IEnumerable<Index> indexes)
    {
        var primary = table.Constraints.Get<PrimaryKeyConstraint>();
        var inlinePrimary = table.Columns.Values.Where(column => column.Constraints.Get<PrimaryKeyConstraint>() is not null).ToArray();
        if (primary is null && inlinePrimary.Length > 1)
            throw new NotSupportedException("Multiple inline primary keys require an explicit composite primary key.");
        var primaryKey = primary is not null ? new SchemaKeyDefinition(primary.Name, primary.Columns.Keys.ToArray())
            : inlinePrimary.Length == 1 ? new SchemaKeyDefinition(inlinePrimary[0].Constraints.Get<PrimaryKeyConstraint>()!.Name, [inlinePrimary[0].Name]) : null;
        var unique = table.Constraints.OfType<UniquenessConstraint>()
            .Select(key => new SchemaKeyDefinition(key.Name, key.Columns.Select(column => column.Name).ToArray()))
            .Concat(table.Columns.Values.Where(column => column.Constraints.Get<UniquenessConstraint>() is not null)
                .Select(column => new SchemaKeyDefinition(column.Constraints.Get<UniquenessConstraint>()!.Name, [column.Name])))
            .Where(key => primaryKey is null || !key.Columns.ToHashSet(StringComparer.Ordinal).SetEquals(primaryKey.Columns))
            .ToArray();
        return new(Name(table.Identity), table.Columns.Keys.ToArray(), primaryKey, unique, table.PhysicalOptions,
            indexes.Select(index => new SchemaIndexDefinition(index.Name, Columns(index), index.IsUnique, index.Structure)).ToArray());
    }

    private static SchemaIndexColumn[] Columns(Index index) => index.Columns.Values.Select(column =>
        new SchemaIndexColumn(column.Name, column.SortDirection switch
        {
            IndexSortDirection.Ascending => false,
            IndexSortDirection.Descending => true,
            null => null,
            _ => throw new ArgumentException("Unknown index direction.")
        })).ToArray();

    private static SchemaSqlObjectName Name(DatabaseObjectName name) => new(name.Name, name.Schema, name.Database, name.Catalog);
}
