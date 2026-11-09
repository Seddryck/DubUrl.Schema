using DubUrl.Querying.Dialects;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

/// <summary>Optional provider hook for UNIQUE declarations with an explicit null policy.</summary>
public interface IUniqueConstraintSqlRenderer
{
    /// <summary>Returns a faithful enforcement plan, or null when the declaration is unsupported.</summary>
    UniqueConstraintPlan? PlanUniqueConstraint(IDialect dialect, UniqueConstraintContext context);
}

public sealed record UniqueConstraintContext(Table Table, UniquenessConstraint Constraint,
    string SuggestedName, IReadOnlyList<Index> Indexes);

/// <param name="NullPolicy">The policy the plan enforces.</param>
/// <param name="CanReferenceWithForeignKey">Whether the tuple can be targeted under the provider's foreign-key DDL rules.</param>
public abstract record UniqueConstraintPlan(UniqueNullPolicy NullPolicy, bool CanReferenceWithForeignKey);

/// <summary>A trusted definition within CREATE TABLE, with no separate backing-index command.</summary>
public sealed record NativeUniqueConstraintPlan(UniqueNullPolicy NullPolicy, bool CanReferenceWithForeignKey, string Definition)
    : UniqueConstraintPlan(NullPolicy, CanReferenceWithForeignKey);

/// <summary>Ordered, trusted index commands that together enforce the declaration.</summary>
public sealed record UniqueIndexPlan(UniqueNullPolicy NullPolicy, bool CanReferenceWithForeignKey,
    IReadOnlyList<UniqueIndexCommand> Indexes) : UniqueConstraintPlan(NullPolicy, CanReferenceWithForeignKey);

public sealed record UniqueIndexCommand(DatabaseObjectName Identity, string CommandText);
