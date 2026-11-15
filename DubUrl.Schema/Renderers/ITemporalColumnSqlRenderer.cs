using System.Data;
using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Renders a logical temporal type at its explicitly declared fractional-second precision.</summary>
public interface ITemporalColumnSqlRenderer
{
    /// <summary>Returns the exact type declaration, or null when that precision is unsupported.</summary>
    string? RenderTemporalColumnType(IDialect dialect, DbType type, int fractionalSecondsPrecision);
}
