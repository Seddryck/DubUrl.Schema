using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

/// <summary>Optional capability on a dialect's schema renderer for typed column defaults.</summary>
public interface IColumnDefaultSqlRenderer
{
    /// <summary>
    /// Returns a faithful INSERT-time expression for the dialect, provider route and destination, or null if unsupported
    /// or unresolved. The provider must preserve the requested precision, time-zone semantics and representation.
    /// </summary>
    /// <remarks>
    /// Planning must never evaluate a default. The result supplies only the SQL expression that follows DEFAULT.
    /// </remarks>
    ColumnDefaultPlan? PlanColumnDefault(IDialect dialect, ColumnDefaultContext context);
}

public sealed record ColumnDefaultContext(DatabaseObjectName Table, Column Column, string SqlType, bool AllowsNull,
    TablePhysicalOptions? PhysicalOptions);

/// <param name="Sql">Trusted provider SQL for the expression, without the DEFAULT keyword.</param>
/// <param name="RequiredTextCapacity">Maximum context value size in the destination's length units, including any encoding requirements.</param>
/// <param name="MayReturnNull">Whether a context expression can yield NULL for this route.</param>
/// <param name="EquivalentContext">A different context kind that is an explicitly confirmed semantic alias on this route.</param>
/// <param name="UuidSemantics">Optional provider guarantees for UUID generation.</param>
public sealed record ColumnDefaultPlan(string Sql, int? RequiredTextCapacity = null, bool? MayReturnNull = null,
    ColumnDefaultKind? EquivalentContext = null, UuidGenerationSemantics? UuidSemantics = null);

public sealed record UuidGenerationSemantics(int? Version = null, string? Ordering = null, string? Randomness = null);

/// <summary>A typed default could not be planned faithfully.</summary>
public sealed class ColumnDefaultPlanningException : NotSupportedException
{
    public const string Unsupported = "SDD001";
    public const string Incomplete = "SDD002";

    public string Code { get; }

    internal ColumnDefaultPlanningException(string code, string message, Exception? innerException = null)
        : base($"{code}: {message}", innerException)
        => Code = code;
}
