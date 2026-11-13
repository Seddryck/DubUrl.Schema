using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Plans provider-specific SQL for literal annotations after creating a table.</summary>
public interface ITableAnnotationSqlRenderer
{
    IReadOnlyList<SchemaSqlCommand> PlanTableAnnotations(IDialect dialect, DatabaseObjectName table,
        IReadOnlyDictionary<string, string> annotations);
}
