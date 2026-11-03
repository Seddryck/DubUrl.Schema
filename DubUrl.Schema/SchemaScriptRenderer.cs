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

        templates.Add(new CreateSchemaRenderer(dialect, nativeTypes, options == SchemaCreationOptions.DropIfExists));
        templates.Add(new ForeignKeyRenderer(dialect));
        if (CommentRenderer.Supports(dialect))
            templates.Add(new CommentRenderer(dialect));
        templates.Add(new CreateIndexRenderer(dialect));
        templates.Add(new CreateViewsRenderer(dialect));
        Templates = [.. templates];
    }

    public virtual string Render(Schema schema)
    {
        ValidateCommentRendering(schema);
        var tables = CreateTableModels(schema);
        ValidateForeignKeys(schema, tables);
        MarkUniqueIndexColumns(schema, tables);
        TableDefinitionPlanning.Apply(Dialect, schema, tables);
        var indexes = schema.Indexes.Values.Select(index => new IndexViewModel(index)
        {
            ConsumedByTable = Dialect.SchemaSqlRenderer is ITableDefinitionSqlRenderer
        }).ToArray();
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

    private void ValidateForeignKeys(Schema schema, TableViewModel[] tables)
    {
        // Validate both endpoints, including metadata for pre-existing reference tables.
        foreach (var table in schema.Tables.Values)
        {
            foreach (var key in table.Constraints.OfType<Constraints.ForeignKeyConstraint>())
            {
                var source = tables.Single(item => item.Identity == table.Identity);
                foreach (var name in key.SourceColumns)
                    source.Columns.Single(column => column.Name == name).IsKey = true;
                var target = schema.Tables.TryGetValue(key.TargetTable.Key, out var created)
                    ? created : schema.ExternalTables[key.TargetTable.Key];
                foreach (var name in key.TargetColumns)
                {
                    ValidateKeyColumn(target, name);
                }
            }
        }
    }

    private void ValidateKeyColumn(Table target, string name)
    {
        var column = new ColumnViewModel(target.Columns[name]);
        var type = CreateSchemaRenderer.RenderColumnType(column, Dialect, NativeTypes);
        type = Dialect.SchemaSqlRenderer.RenderColumnType(Dialect, type, column.DeclaredLength,
            column.Precision, column.Scale,
            column.NativeType is not null && NativeTypes.TryResolve(Dialect, column.NativeType, out _));
        Dialect.SchemaSqlRenderer.ValidateKeyColumn(Dialect, type, target.Identity.ToString(), name);
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
