using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Plans provider SQL for column annotations after creating their table.</summary>
public interface IColumnAnnotationSqlRenderer
{
    /// <param name="dialect">The target dialect.</param>
    /// <param name="table">The table identity.</param>
    /// <param name="annotations">Annotation maps keyed by literal column name. Only annotated columns are included.</param>
    /// <returns>Commands that recreate the annotations on the target table.</returns>
    IReadOnlyList<SchemaSqlCommand> PlanColumnAnnotations(IDialect dialect, DatabaseObjectName table,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> annotations);
}
