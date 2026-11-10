using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

internal static class ColumnDefaultPlanning
{
    internal static void Apply(IDialect dialect, NativeTypeRegistry nativeTypes, TableViewModel table)
    {
        foreach (var column in table.Columns)
        {
            if (column.Source.DefaultExpression is { } expression)
                column.PlannedDefaultExpression = Plan(dialect, nativeTypes, table, column, expression);
        }
    }

    private static string Plan(IDialect dialect, NativeTypeRegistry nativeTypes, TableViewModel table,
        ColumnViewModel column, ColumnDefaultExpression expression)
    {
        var label = $"The {expression.Kind} default for '{table.Identity}.{column.Name}'";
        if (dialect.SchemaSqlRenderer is not IColumnDefaultSqlRenderer renderer)
        {
            throw new ColumnDefaultPlanningException(ColumnDefaultPlanningException.Unsupported,
                $"{label} is unsupported by '{dialect.GetType().Name}'.");
        }

        var context = CreateContext(dialect, nativeTypes, table, column, label);
        var plan = renderer.PlanColumnDefault(dialect, context);
        if (plan is null)
        {
            throw new ColumnDefaultPlanningException(ColumnDefaultPlanningException.Unsupported,
                $"{label} is unsupported or unresolved by '{dialect.GetType().Name}'.");
        }
        if (string.IsNullOrWhiteSpace(plan.Sql))
        {
            throw new ColumnDefaultPlanningException(ColumnDefaultPlanningException.Incomplete,
                $"{label} has no complete provider plan.");
        }
        return plan.Sql;
    }

    private static ColumnDefaultContext CreateContext(IDialect dialect, NativeTypeRegistry nativeTypes, TableViewModel table,
        ColumnViewModel column, string label)
    {
        var allowsNull = column.Nullable is not null && column.NotNullable is null
            && column.Source.Constraints.Get<PrimaryKeyConstraint>() is null
            && table.PrimaryKey?.Columns.Any(key => key.Name == column.Name) is not true;
        string type;
        try { type = CreateSchemaRenderer.ResolveColumnType(column, dialect, nativeTypes); }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException or NotSupportedException)
        {
            throw new ColumnDefaultPlanningException(ColumnDefaultPlanningException.Unsupported,
                $"{label} has an unsupported destination type for '{dialect.GetType().Name}'.", exception);
        }
        return new(table.Identity, column.Source, type, allowsNull, table.PhysicalOptions);
    }
}
