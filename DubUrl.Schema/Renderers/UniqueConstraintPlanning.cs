using System.Globalization;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

internal static class UniqueConstraintPlanning
{
    internal static void Apply(IDialect dialect, Schema schema, TableViewModel[] tables)
    {
        var indexNames = schema.Indexes.Values.Select(index => index.Identity).ToHashSet();
        var foreignKeys = schema.Tables.Values.SelectMany(table => table.Constraints
            .OfType<ForeignKeyConstraint>().Select(key => (Table: table, Key: key))).ToArray();
        var referenced = foreignKeys.Select(item => item.Key.TargetTable).ToHashSet();
        var targets = new Dictionary<DatabaseObjectName, List<(UniquenessConstraint Key, UniqueConstraintPlan Plan)>>();
        foreach (var table in schema.Tables.Values.Concat(schema.ExternalTables.Values.Where(table => referenced.Contains(table.Identity))))
        {
            var keys = table.EffectiveTableUniqueConstraints().ToArray();
            if (keys.All(key => key.NullPolicy == UniqueNullPolicy.Native))
                continue;
            var indexes = schema.Indexes.Values.Where(index => index.TableIdentity == table.Identity).ToArray();
            var model = tables.SingleOrDefault(item => item.Identity == table.Identity);
            var declaredNames = table.Constraints.OfType<Constraint>()
                .Concat(table.Columns.Values.SelectMany(column => column.Constraints.OfType<Constraint>()))
                .Select(constraint => constraint.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var localIndexNames = indexes.Select(index => index.Name).ToHashSet(StringComparer.Ordinal);
            var reservedNames = declaredNames.Concat(localIndexNames).ToHashSet(StringComparer.Ordinal);
            for (var position = 0; position < keys.Length; position++)
            {
                var key = keys[position];
                if (key.NullPolicy == UniqueNullPolicy.Native)
                    continue;
                var name = key.Name ?? SuggestName(table.Name, position + 1, reservedNames);
                var label = $"UNIQUE '{name}' on table '{table.Identity}'";
                if (indexes.Any(index => index.IsUnique && index.Columns.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(key.Columns.Select(column => column.Name))))
                    throw new NotSupportedException($"{label} overlaps a separate unique index with an unknown null policy.");
                if (dialect.SchemaSqlRenderer is not IUniqueConstraintSqlRenderer renderer)
                    throw new NotSupportedException($"{label} requires a provider plan for {key.NullPolicy}.");
                var plan = renderer.PlanUniqueConstraint(dialect, new(table, key, name, Array.AsReadOnly(indexes)));
                if (plan is null || plan.NullPolicy != key.NullPolicy)
                    throw new NotSupportedException($"{label} cannot be enforced with null policy {key.NullPolicy}.");
                ApplyPlan(table, model, key, plan, indexNames, localIndexNames, declaredNames, label);
                reservedNames.UnionWith(localIndexNames);
                if (!targets.TryGetValue(table.Identity, out var targetKeys))
                    targets.Add(table.Identity, targetKeys = []);
                targetKeys.Add((key, plan));
            }
            if (model is not null)
                model.ExplicitUniqueConstraintsPlanned = true;
        }
        ValidateForeignKeys(foreignKeys, targets);
    }

    private static string SuggestName(string table, int position, HashSet<string> reserved)
    {
        var stem = $"UQ_{table}_{position.ToString(CultureInfo.InvariantCulture)}";
        var name = stem;
        for (var suffix = 2; !reserved.Add(name); suffix++)
            name = $"{stem}_{suffix.ToString(CultureInfo.InvariantCulture)}";
        return name;
    }

    private static void ApplyPlan(Table table, TableViewModel? model, UniquenessConstraint key,
        UniqueConstraintPlan plan, HashSet<DatabaseObjectName> indexNames, HashSet<string> localIndexNames,
        HashSet<string> declaredNames, string label)
    {
        switch (plan)
        {
            case NativeUniqueConstraintPlan native when !string.IsNullOrWhiteSpace(native.Definition):
                model?.ExplicitUniqueDefinitions.Add(native.Definition);
                return;
            case UniqueIndexPlan indexPlan when indexPlan.Indexes is { Count: > 0 }:
                var indexes = indexPlan.Indexes.ToArray();
                if (key.Name is not null && !indexes.Any(index => index?.Identity?.Name == key.Name))
                    throw new NotSupportedException($"{label} requires a backing index that preserves the declared name.");
                var columns = key.Columns.Select(column => new IndexColumn(column.Name)).ToArray();
                foreach (var index in indexes)
                {
                    if (index?.Identity is null
                        || (index.FilterPredicate is not null && string.IsNullOrWhiteSpace(index.FilterPredicate))
                        || (index.KeySuffix is not null && string.IsNullOrWhiteSpace(index.KeySuffix))
                        || !SameNamespace(index.Identity, table.Identity))
                        throw new NotSupportedException($"{label} has an invalid backing index plan.");
                    if ((model is not null && !indexNames.Add(index.Identity)) || !localIndexNames.Add(index.Identity.Name)
                        || (index.Identity.Name != key.Name && declaredNames.Contains(index.Identity.Name)))
                        throw new NotSupportedException($"{label} has an ambiguous backing index name '{index.Identity}'.");
                    model?.ExplicitUniqueIndexes.Add(new IndexViewModel(new Index(index.Identity, table.Identity, columns, true))
                    {
                        FilterPredicate = index.FilterPredicate,
                        KeySuffix = index.KeySuffix
                    });
                }
                return;
            default:
                throw new NotSupportedException($"{label} requires a native definition or a complete index plan.");
        }
    }

    private static bool SameNamespace(DatabaseObjectName index, DatabaseObjectName table)
        => index.Schema == table.Schema && index.Database == table.Database && index.Catalog == table.Catalog;

    private static void ValidateForeignKeys(IEnumerable<(Table Table, ForeignKeyConstraint Key)> foreignKeys,
        Dictionary<DatabaseObjectName, List<(UniquenessConstraint Key, UniqueConstraintPlan Plan)>> targets)
    {
        foreach (var (table, key) in foreignKeys)
        {
            if (!targets.TryGetValue(key.TargetTable, out var targetKeys))
                continue;
            var members = key.TargetColumns.ToHashSet(StringComparer.Ordinal);
            var matches = targetKeys.Where(item => members.SetEquals(item.Key.Columns.Select(column => column.Name))).ToArray();
            if (matches.Length == 0)
                continue;
            if (!matches.Any(item => item.Plan.CanReferenceWithForeignKey
                && item.Key.Columns.Select(column => column.Name).SequenceEqual(key.TargetColumns, StringComparer.Ordinal)))
                throw new NotSupportedException($"Foreign key '{key.Name}' on '{table.Identity}' has no eligible planned UNIQUE target on '{key.TargetTable}'.");
        }
    }
}
