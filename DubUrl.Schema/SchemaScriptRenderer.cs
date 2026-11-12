using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Didot.Core.TemplateEngines;
using Didot.Core;
using System.Reflection;
using DubUrl.Querying.TypeMapping;
using DubUrl.Schema.Templating;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Renderers;

namespace DubUrl.Schema;

public class SchemaScriptRenderer
{
    private RendererEngine[] Templates { get; } = [];
    private IDialect Dialect { get; }
    private NativeTypeRegistry NativeTypes { get; }
    private bool DropExisting { get; }
    /// <summary>Whether descriptions can be rendered by this implementation.</summary>
    /// <value>
    /// Whether descriptions can be rendered by this implementation.
    /// </value>
    public bool SupportsComments { get; }
    public bool EngineSupportsComments => (Dialect.Capabilities ?? DialectCapabilities.Default).SupportsComments;

    public SchemaScriptRenderer(IDialect dialect, SchemaCreationOptions options = SchemaCreationOptions.None, NativeTypeRegistry? nativeTypes = null)
    {
        Dialect = dialect;
        DropExisting = options == SchemaCreationOptions.DropIfExists;
        NativeTypes = nativeTypes ?? NativeTypeRegistry.Default;
        SupportsComments = CommentRenderer.Supports(dialect) || dialect.SchemaSqlRenderer is ITableDescriptionSqlRenderer;
        var templates = new List<RendererEngine>();
        if (options == SchemaCreationOptions.DropIfExists)
        {
            templates.Add(new DropViewsIfExistsRenderer(dialect));
            templates.Add(new DropIndexesIfExistsRenderer(dialect));
            templates.Add(new DropTablesIfExistsRenderer(dialect));
        }

        templates.Add(new CreateSchemaRenderer(dialect, nativeTypes, options == SchemaCreationOptions.DropIfExists,
            includeIdentityPrerequisites: true, defaultsAlreadyPlanned: true));
        templates.Add(new ForeignKeyRenderer(dialect));
        if (CommentRenderer.Supports(dialect))
            templates.Add(new CommentRenderer(dialect));
        templates.Add(new CreateIndexRenderer(dialect));
        templates.Add(new CreateViewsRenderer(dialect));
        Templates = [.. templates];
    }

    public virtual string Render(Schema schema)
    {
        if (Dialect.SchemaSqlRenderer is ISchemaCommandSqlRenderer)
            return string.Join(Environment.NewLine, Plan(schema).Select(command => command.CommandText.TrimEnd()));

        var tables = PrepareTables(schema);
        var indexes = CreateIndexModels(schema);
        var orderedViews = ViewDependencyPlanner.Order(schema.Views.Values)
            .Select(view => new ViewViewModel(view)).ToArray();
        var model = new
        {
            model = new
            {
                Tables = tables.ToArray(),
                TablesToDrop = DropExisting ? OrderTablesToDrop(schema, tables) : tables.AsEnumerable().Reverse().ToArray(),
                Indexes = indexes,
                Views = orderedViews,
                ViewsToDrop = orderedViews.Reverse().ToArray()
            }
        };

        var script = new StringBuilder();
        foreach (var renderer in Templates)
            script.Append(renderer.Render(model));
        return script.ToString();
    }

    /// <summary>Renders trusted command boundaries before any database connection is opened.</summary>
    /// <remarks>Text export remains available through Render; this path never parses exported SQL.</remarks>
    public virtual IReadOnlyList<SchemaSqlCommand> Plan(Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var commands = new List<SchemaSqlCommand>();
        var tables = PrepareTables(schema, commands);
        var indexes = CreateIndexModels(schema);
        var views = ViewDependencyPlanner.Order(schema.Views.Values).Select(view => new ViewViewModel(view)).ToArray();
        var provider = Dialect.SchemaSqlRenderer;

        void Add(RendererEngine renderer, object model, SchemaSqlOperation operation, DatabaseObjectName name)
            => commands.Add(new(renderer.Render(model), operation, name.ToString()));

        if (DropExisting)
        {
            foreach (var view in views.Reverse())
                Add(new DropViewsIfExistsRenderer(Dialect), new { model = new { ViewsToDrop = new[] { view } } }, SchemaSqlOperation.DropView, view.Identity);
            // All modeled indexes belong to modeled tables. Dropping those tables also drops their
            // indexes; separate DROP INDEX commands have incompatible provider syntax and are redundant.
            foreach (var table in OrderTablesToDrop(schema, tables))
                Add(new DropTablesIfExistsRenderer(Dialect), new { model = new { TablesToDrop = new[] { table } } }, SchemaSqlOperation.DropTable, table.Identity);
        }

        var createIndex = new CreateIndexRenderer(Dialect);
        AddTableCommands(commands, tables, createIndex);
        var foreignKeys = new ForeignKeyRenderer(Dialect);
        foreach (var table in tables.Where(table => !table.RenderForeignKeysInline))
        {
            foreach (var key in table.ForeignKeys)
            {
                Add(foreignKeys, new { model = new { Tables = new[] { new { table.Identity, RenderForeignKeysInline = false, ForeignKeys = new[] { key } } } } },
                    SchemaSqlOperation.ForeignKey, table.Identity);
            }
        }

        if (SupportsComments)
            AddCommentCommands(commands, tables);
        foreach (var index in indexes.Where(index => !index.ConsumedByTable))
            Add(createIndex, new { model = new { Indexes = new[] { index } } }, SchemaSqlOperation.CreateIndex, index.Identity);
        foreach (var view in views)
            Add(new CreateViewsRenderer(Dialect), new { model = new { Views = new[] { view } } }, SchemaSqlOperation.CreateView, view.Identity);

        // Snapshot the entire plan: rendering/validation failures must occur before execution begins.
        return Array.AsReadOnly(provider.PlanCommands(Dialect, commands.AsReadOnly()).ToArray());
    }

    private TableViewModel[] PrepareTables(Schema schema, List<SchemaSqlCommand>? commands = null)
    {
        ValidateCommentRendering(schema);
        var tables = CreateTableModels(schema);
        ValidateForeignKeys(schema, tables, commands);
        MarkUniqueIndexColumns(schema, tables);
        TableDefinitionPlanning.Apply(Dialect, schema, tables);
        UniqueConstraintPlanning.Apply(Dialect, schema, tables);
        foreach (var table in tables)
            ColumnDefaultPlanning.Apply(Dialect, NativeTypes, table);
        return tables;
    }

    private IndexViewModel[] CreateIndexModels(Schema schema)
    {
        var consumedByTable = Dialect.SchemaSqlRenderer is ITableDefinitionSqlRenderer;
        return schema.Indexes.Values.Select(index => new IndexViewModel(index) { ConsumedByTable = consumedByTable }).ToArray();
    }

    private void AddTableCommands(List<SchemaSqlCommand> commands, IEnumerable<TableViewModel> tables, CreateIndexRenderer createIndex)
    {
        var provider = Dialect.SchemaSqlRenderer as ISchemaCommandSqlRenderer;
        var create = new CreateSchemaRenderer(Dialect, NativeTypes, DropExisting,
            includeIdentityPrerequisites: false, defaultsAlreadyPlanned: true);
        foreach (var table in tables)
        {
            if (Dialect.SchemaSqlRenderer is ISequenceIdentitySqlRenderer sequence)
            {
                foreach (var column in table.Columns.Where(column => column.Generation is IdentityColumnGeneration))
                {
                    var identity = (IdentityColumnGeneration)column.Generation!;
                    var context = new IdentityColumnContext(table.Name, column.Name, table.Identity.Schema, table.Identity.Database, table.Identity.Catalog);
                    commands.AddRange(sequence.PlanIdentityPrerequisites(Dialect, context, identity.Seed, identity.Increment, DropExisting));
                }
            }
            commands.Add(new(create.Render(new
            {
                model = new { Tables = new[] { table } },
                PlanCommandsSeparately = true,
                PlanProviderChecks = provider is not null
            }),
                SchemaSqlOperation.CreateTable, table.Identity.ToString()));
            if (provider is not null)
                commands.AddRange(provider.PlanTableChecks(Dialect, table.Identity, CreateSchemaRenderer.RenderTableChecks(table, Dialect)));
            foreach (var index in table.ExplicitUniqueIndexes)
            {
                var sql = createIndex.Render(new { model = new { Indexes = new[] { index } } });
                commands.Add(new(sql, SchemaSqlOperation.CreateIndex, index.Identity.ToString()));
            }
        }
    }

    private void AddCommentCommands(List<SchemaSqlCommand> commands, IEnumerable<TableViewModel> tables)
    {
        void Add(object item, string name)
        {
            var sql = CommentRenderer.RenderComment(Dialect, item);
            if (!string.IsNullOrWhiteSpace(sql))
                commands.Add(new(sql, SchemaSqlOperation.Comment, name));
        }

        foreach (var table in tables)
        {
            Add(table, table.Identity.ToString());
            foreach (var column in table.Columns)
                Add(column, $"{table.Identity}.{column.Name}");
        }
    }

    private TableViewModel[] OrderTablesToDrop(Schema schema, TableViewModel[] tables)
    {
        try
        {
            return TableDependencyPlanner.Order(schema.Tables.Values).Reverse()
                .Select(table => tables.Single(model => model.Identity == table.Identity)).ToArray();
        }
        catch (NotSupportedException) when (!Dialect.SchemaSqlRenderer.RequiresForeignKeyDependencyOrder)
        {
            // Preserve the existing rendering contract for dialects accepting
            // cyclic declarations. Such cycles have no topological drop order.
            return tables.AsEnumerable().Reverse().ToArray();
        }
    }

    private void ValidateCommentRendering(Schema schema)
    {
        if (EngineSupportsComments && !CommentRenderer.Supports(Dialect) && schema.Tables.Values.Any(table =>
            (table.Description is not null && Dialect.SchemaSqlRenderer is not ITableDescriptionSqlRenderer)
            || table.Columns.Values.Any(column => column.Description is not null)))
            throw new NotSupportedException($"Database comments are supported, but comment rendering is not implemented for '{Dialect.GetType().Name}'. Add a provider ISchemaSqlRenderer comment implementation or omit descriptions.");
    }

    private TableViewModel[] CreateTableModels(Schema schema)
    {
        var tables = new List<TableViewModel>();
        var declaredTables = Dialect.SchemaSqlRenderer.RequiresForeignKeyDependencyOrder
            ? TableDependencyPlanner.Order(schema.Tables.Values) : schema.Tables.Values.ToArray();
        foreach (var table in declaredTables)
        {
            var tableRenderer = new TableViewModel(
                table,
                Dialect.SchemaSqlRenderer.RenderForeignKeysInline(Dialect));
            tables.Add(tableRenderer);
        }
        return tables.ToArray();
    }

    private void ValidateForeignKeys(Schema schema, TableViewModel[] tables, List<SchemaSqlCommand>? commands)
    {
        // Validate both endpoints, including metadata for pre-existing reference tables.
        foreach (var table in schema.Tables.Values)
        {
            foreach (var key in table.Constraints.OfType<Constraints.ForeignKeyConstraint>())
            {
                var source = tables.Single(item => item.Identity == table.Identity);
                foreach (var name in key.SourceColumns)
                    source.Columns.Single(column => column.Name == name).IsKey = true;
                var isCreated = schema.Tables.TryGetValue(key.TargetTable.Key, out var created);
                var target = isCreated ? created! : schema.ExternalTables[key.TargetTable.Key];
                ValidateTargetKeyColumns(tables, key.TargetColumns, target, isCreated);
                if (commands is not null && Dialect.SchemaSqlRenderer is ISchemaCommandSqlRenderer provider)
                {
                    commands.AddRange(provider.PlanForeignKeyPrerequisites(Dialect, key,
                        isCreated ? TableDefinitionPlanning.Context(target, []) : null));
                }
            }
        }
    }

    private void ValidateTargetKeyColumns(TableViewModel[] tables, IEnumerable<string> names, Table target, bool isCreated)
    {
        foreach (var name in names)
        {
            if (isCreated && target.Columns[name].StructuredType is not null)
                tables.Single(item => item.Identity == target.Identity).Columns.Single(column => column.Name == name).IsKey = true;
            else
                ValidateKeyColumn(target, name);
        }
    }

    private void ValidateKeyColumn(Table target, string name)
    {
        var column = new ColumnViewModel(target.Columns[name], includePrimaryKey: true, target.Identity) { IsKey = true };
        if (column.Source.StructuredType is { } type)
            column.PlannedStructuredType = CreateSchemaRenderer.PlanStructuredType(new TableViewModel(target), column, type, Dialect);
        CreateSchemaRenderer.ResolveColumnType(column, Dialect, NativeTypes);
    }

    private static void MarkUniqueIndexColumns(Schema schema, TableViewModel[] tables)
    {
        foreach (var index in schema.Indexes.Values.Where(index => index.IsUnique))
        {
            foreach (var name in index.Columns.Keys)
                tables.Single(table => table.Identity == index.TableIdentity).Columns.Single(column => column.Name == name).IsKey = true;
        }
    }

    protected internal string Render(object model)
    {
        var script = new StringBuilder();
        foreach (var renderer in Templates)
            script.Append(renderer.Render(model));
        return script.ToString();
    }
}
