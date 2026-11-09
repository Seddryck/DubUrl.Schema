using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ddt = Didot.Core;
using System.Reflection;
using DubUrl.Querying.Dialects.Renderers;
using DubUrl.Querying.Dialects;
using DubUrl.Schema.Renderers;
using System.Text.Encodings.Web;

namespace DubUrl.Schema.Templating;
public abstract class RendererEngine
{
    protected Ddt.ITemplateEngine Engine { get; }
    private string TemplatePath { get; }
    private Assembly Assembly { get; }

    protected RendererEngine(Assembly asm, string templatePath)
    {
        var extension = Path.GetExtension(templatePath);
        Assembly = asm;
        TemplatePath = templatePath;
        var factory = Ddt.TemplateEngineFactory.Default;
        Engine = factory.Create(extension);
    }

    protected static Dictionary<string, Func<object?, string>> CreateHelpers(IRenderer renderer)
    {
        KeyValuePair<string, Func<object?, string>> create(string name) =>
            new(name, value => renderer.Render(value, name));

        return new([create("value"), create("identity")]);
    }

    protected static Dictionary<string, Func<object?, string>> CreateHelpers(IDialect dialect)
    {
        var helpers = CreateHelpers(dialect.Renderer);
        helpers["identity"] = value => dialect.SchemaSqlRenderer.RenderIdentifier(dialect, (string)value!);
        helpers.Add("objectname", value => DatabaseObjectNameFormatter.Render(value, dialect));
        helpers.Add("indextable", value => value is DatabaseObjectName table
            ? dialect.SchemaSqlRenderer.RenderIndexTableName(dialect, SqlName(table))
            : throw new ArgumentException("Index table naming requires an object identity."));
        helpers.Add("fktarget", value => value is ForeignKeyConstraintViewModel key && key.SourceTable is not null
            ? dialect.SchemaSqlRenderer.RenderForeignKeyTarget(dialect, SqlName(key.SourceTable), SqlName(key.TargetTable))
            : throw new ArgumentException("Foreign-key naming requires an owning table identity."));
        return helpers;
    }

    private static SchemaSqlObjectName SqlName(DatabaseObjectName name)
        => new(name.Name, name.Schema, name.Database, name.Catalog);

    protected void AddMappings(string mapKey, IDictionary<string, object> mappings)
        => Engine.AddMappings(mapKey, mappings);

    protected void AddFormatter(string functionName, Func<object?, string> function)
        => Engine.AddFormatter(functionName, function);

    protected void AddPartial(string name, string templatePath)
        => Engine.AddPartial(name, () =>
        {
            using var stream = OpenTemplate(templatePath);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

    private Stream OpenTemplate(string path)
        => Assembly.GetManifestResourceStream(path) ?? throw new FileNotFoundException(path);

    public string Render(object value)
    {
        using (var stream = OpenTemplate(TemplatePath))
        return Engine.Render(stream, value).ReplaceLineEndings(Environment.NewLine);
    }
}
