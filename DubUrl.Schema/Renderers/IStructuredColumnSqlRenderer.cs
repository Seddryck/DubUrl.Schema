using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Optional capability on a dialect's schema renderer for structured column types.</summary>
public interface IStructuredColumnSqlRenderer
{
    /// <summary>
    /// Returns trusted SQL for the type, including any type-specific parameters, or null if the
    /// shape, its constraints, or the requested storage cannot be supported. Quote identifiers
    /// within the descriptor as needed. Column clauses are rendered by the existing definition pipeline.
    /// </summary>
    string? RenderStructuredType(IDialect dialect, StructuredColumnTypeContext context);
}

public sealed record StructuredColumnTypeContext(DatabaseObjectName Table, Column Column,
    StructuredColumnType Type, bool? Nullable, bool IsKey, TablePhysicalOptions? PhysicalOptions);
