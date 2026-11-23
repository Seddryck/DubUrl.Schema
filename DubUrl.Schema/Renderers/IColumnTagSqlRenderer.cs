using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Plans provider-specific SQL for all tagged columns of a newly created table.</summary>
public interface IColumnTagSqlRenderer
{
    IReadOnlyList<SchemaSqlCommand> PlanColumnTags(IDialect dialect, DatabaseObjectName table,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tags);
}
