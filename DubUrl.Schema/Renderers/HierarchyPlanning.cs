using System.Security.Cryptography;
using System.Text;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;
using DbType = System.Data.DbType;

namespace DubUrl.Schema.Renderers;

internal static class HierarchyPlanning
{
    internal static (HierarchyPlan Report, Table Table)[] Plan(Schema schema, IDialect dialect,
        SchemaCreationOptions options, NativeTypeRegistry nativeTypes)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema.Tables.Values.Where(table => table.Hierarchy is not null)
            .Select(table => PlanTable(table, dialect, options, nativeTypes)).ToArray();
    }

    internal static Schema Expand(Schema schema, IDialect dialect, SchemaCreationOptions options, NativeTypeRegistry nativeTypes)
    {
        var plans = Plan(schema, dialect, options, nativeTypes);
        var failures = plans.Where(plan => !plan.Report.IsSupported).Select(plan => plan.Report.Diagnostic).ToArray();
        if (failures.Length > 0)
            throw new NotSupportedException(string.Join(Environment.NewLine, failures));
        if (plans.Length == 0) return schema;

        var expanded = plans.ToDictionary(plan => plan.Report.Table, plan => plan.Table);
        return new Schema(schema.Tables.Values.Select(table => expanded.GetValueOrDefault(table.Identity, table)).ToArray(),
            schema.Indexes.Values.ToArray(), schema.Views.Values.ToArray(), schema.ExternalTables.Values.ToArray());
    }

    private static (HierarchyPlan Report, Table Table) PlanTable(Table table, IDialect dialect,
        SchemaCreationOptions options, NativeTypeRegistry nativeTypes)
    {
        HierarchyStoragePlan? storage = null;
        try
        {
            var hierarchy = table.Hierarchy!;
            var key = GetNodeKey(table, hierarchy);
            var context = new HierarchyContext(table, hierarchy, key, options);
            storage = SelectStorage(dialect, context);
            if (storage is null)
                throw new NotSupportedException("The provider has no hierarchy storage plan for this declaration.");
            if (hierarchy.RequireParentExists && !storage.ParentExistenceEnforced)
                throw new NotSupportedException("The provider cannot enforce parent existence for this hierarchy with the supplied target configuration.");

            ValidateAdditions(table, storage);
            var expanded = new Table(table.Identity, [.. table.Columns.Values, .. storage.Columns],
                [.. table.Constraints.OfType<Constraint>(), .. storage.Constraints], table.Description, table.PhysicalOptions, table.Annotations);
            ValidateStorage(expanded, storage, dialect, nativeTypes);
            return (new(table.Identity, storage.Representation, storage.ParentExistenceEnforced, null), expanded);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
        {
            return (new(table.Identity, storage?.Representation, false,
                $"Hierarchy on table '{table.Identity}' for '{dialect.GetType().Name}': {exception.Message}"), table);
        }
    }

    private static Column GetNodeKey(Table table, Hierarchy hierarchy)
    {
        if (!table.PrimaryKeyColumns().SetEquals([hierarchy.NodeKey]))
            throw new ArgumentException("The node key must be the table's single-column primary key.");
        var key = table.Columns[hierarchy.NodeKey];
        if (key.Constraints.Get<NullableConstraint>() is not null)
            throw new ArgumentException("The node key must be non-nullable.");
        if (key.StructuredType is not null || (key.Type == DbType.Object && key.NativeType is null))
            throw new NotSupportedException("The node key must have a scalar type.");
        return key;
    }

    private static HierarchyStoragePlan? SelectStorage(IDialect dialect, HierarchyContext context)
    {
        var renderer = dialect.SchemaSqlRenderer;
        if (renderer is IHierarchySqlRenderer provider)
            return provider.PlanHierarchy(dialect, context);

        // These released dialects predate the hierarchy contract. Match the public dialect types
        // rather than connection aliases, which callers can configure.
        return dialect.GetType().FullName switch
        {
            "DubUrl.Provider.DuckDb.Sql.DuckDbDialect"
                or "DubUrl.Provider.PostgreSql.Sql.PgsqlDialect"
                or "DubUrl.Provider.PostgreSql.Sql.CockRoachDialect"
                or "DubUrl.Provider.Oracle.Sql.OracleDialect"
                or "DubUrl.Provider.FirebirdSql.Sql.FirebirdSqlDialect"
                => ParentReference(context, true),
            "DubUrl.Provider.SqlServer.Sql.TSqlDialect"
                => throw new NotSupportedException("This SQL Server provider does not implement hierarchyid planning."),
            "DubUrl.Provider.Sqlite.Sql.SqliteDialect" or "DubUrl.Provider.MySql.Sql.MySqlDialect"
                => ParentReference(context, false, declareForeignKey: true),
            _ => ParentReference(context, false)
        };
    }

    private static HierarchyStoragePlan ParentReference(HierarchyContext context, bool enforced, bool declareForeignKey = false)
    {
        var table = context.Table;
        var key = context.NodeKey;
        var parentName = context.Hierarchy.ParentColumnName;
        IConstraint[] nullable = [new NullableConstraint()];
        var parent = key switch
        {
            VarLengthColumn length => new VarLengthColumn(parentName, key.Type, length.Length,
                constraints: nullable, nativeType: key.NativeType, nativeTypeFallback: key.NativeTypeFallback),
            NumericColumn numeric => new NumericColumn(parentName, key.Type, numeric.Precision, numeric.Scale,
                constraints: nullable, nativeType: key.NativeType, nativeTypeFallback: key.NativeTypeFallback),
            _ => new Column(parentName, key.Type, constraints: nullable,
                nativeType: key.NativeType, nativeTypeFallback: key.NativeTypeFallback)
        };

        Constraint[] constraints = enforced || declareForeignKey
            ? [new ForeignKeyConstraint(ForeignKeyName(table.Identity, parentName), [parentName], table.Identity, [key.Name])]
            : [];
        return new(HierarchyRepresentation.ParentId, enforced, [parent], constraints);
    }

    private static string ForeignKeyName(DatabaseObjectName table, string parentName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{table.Key}\0{parentName}"));
        return "FK_Hierarchy_" + Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static void ValidateAdditions(Table table, HierarchyStoragePlan storage)
    {
        if (!Enum.IsDefined(storage.Representation) || storage.Columns.Count == 0)
            throw new NotSupportedException("The provider hierarchy plan must define a representation and at least one column.");
        var columns = table.Columns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in storage.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name) || column.Name.Contains('\0') || !columns.Add(column.Name))
                throw new ArgumentException($"Generated hierarchy column '{column.Name}' is invalid or already exists.");
        }

        var names = table.Constraints.Concat(table.Columns.Values.SelectMany(column => column.Constraints))
            .OfType<Constraint>().Select(constraint => constraint.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in storage.Constraints.Select(constraint => constraint.Name).OfType<string>())
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('\0') || !names.Add(name))
                throw new ArgumentException($"Generated hierarchy constraint '{name}' is invalid or already exists.");
        }
    }

    private static void ValidateStorage(Table table, HierarchyStoragePlan storage, IDialect dialect, NativeTypeRegistry nativeTypes)
    {
        var renderer = dialect.SchemaSqlRenderer;
        var identity = table.Identity;
        var name = new SchemaSqlObjectName(identity.Name, identity.Schema, identity.Database, identity.Catalog);
        _ = renderer.RenderObjectName(dialect, name.Catalog, name.Database, name.Schema, name.Name);
        var model = new TableViewModel(table, renderer.RenderForeignKeysInline(dialect));
        var foreignKeys = storage.Constraints.OfType<ForeignKeyConstraint>().ToArray();
        if (storage.ParentExistenceEnforced && foreignKeys.Length == 0)
            throw new NotSupportedException("An enforcing hierarchy plan must include a self-referencing foreign key.");
        foreach (var key in foreignKeys)
        {
            if (key.TargetTable != identity)
                throw new NotSupportedException("A hierarchy foreign key must reference its own table.");
            _ = renderer.RenderIdentifier(dialect, key.Name!);
            _ = renderer.RenderForeignKeyTarget(dialect, name, name);
            foreach (var columnName in key.SourceColumns.Concat(key.TargetColumns))
                model.Columns.Single(column => column.Name == columnName).IsKey = true;
            if (renderer is ISchemaCommandSqlRenderer provider)
                _ = provider.PlanForeignKeyPrerequisites(dialect, key, TableDefinitionPlanning.Context(table, []));
        }

        var primaryColumns = table.PrimaryKeyColumns();
        foreach (var column in model.Columns.Where(column => storage.Columns.Contains(column.Source) || primaryColumns.Contains(column.Name)))
        {
            _ = renderer.RenderIdentifier(dialect, column.Name);
            _ = CreateSchemaRenderer.ResolveColumnType(column, dialect, nativeTypes);
            if (column.Source.Generation is GeneratedColumnGeneration generated && storage.Columns.Contains(column.Source))
                _ = SqlExpressionRenderer.Render(generated.Expression, dialect);
        }
    }
}