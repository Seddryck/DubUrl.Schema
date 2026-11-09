using DubUrl.Querying.Dialects;
using DubUrl.Schema.Templating;

namespace DubUrl.Schema.Renderers;

public class CreateIndexRenderer : RendererEngine
{
    internal static readonly string StatementTemplate = $"{typeof(CreateIndexRenderer).Namespace}.Templates.CreateIndex.sql.hbs";

    public CreateIndexRenderer(IDialect dialect)
        : this(CreateIndexHelpers(dialect))
    { }

    internal static IDictionary<string, Func<object?, string>> CreateIndexHelpers(IDialect dialect)
    {
        var helpers = CreateHelpers(dialect);
        helpers.Add("indexname", value =>
        {
            if (value is not IndexViewModel index)
                throw new ArgumentException("Index naming requires an index view model.");
            var identity = index.Identity;
            var table = index.TableIdentity;
            return dialect.SchemaSqlRenderer.RenderCreateIndexName(dialect,
                new(identity.Name, identity.Schema, identity.Database, identity.Catalog),
                new(table.Name, table.Schema, table.Database, table.Catalog));
        });
        return helpers;
    }

    protected CreateIndexRenderer(IDictionary<string, Func<object?, string>> helpers)
        : base(typeof(CreateIndexRenderer).Assembly, $"{typeof(CreateIndexRenderer).Namespace}.Templates.CreateIndexes.sql.hbs")
    {
        AddPartial("createIndex", StatementTemplate);
        if (!helpers.ContainsKey("indextable"))
            AddFormatter("indextable", helpers["objectname"]);
        if (!helpers.ContainsKey("indexname"))
        {
            AddFormatter("indexname", value => value is IndexViewModel index
                ? helpers["objectname"](index.Identity)
                : throw new ArgumentException("Index naming requires an index view model."));
        }

        foreach (var helper in helpers)
            AddFormatter(helper.Key, helper.Value);
    }
}
