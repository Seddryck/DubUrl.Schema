using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Plans provider-specific SQL to write tags after creating a table.</summary>
public interface ITableTagSqlRenderer
{
    IReadOnlyList<SchemaSqlCommand> PlanTableTags(IDialect dialect, DatabaseObjectName table,
        IReadOnlyDictionary<string, string> tags);
}
