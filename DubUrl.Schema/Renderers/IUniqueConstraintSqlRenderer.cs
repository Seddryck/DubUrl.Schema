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

/// <summary>A native UNIQUE constraint within CREATE TABLE.</summary>
/// <param name="NullPolicy">The policy the constraint enforces.</param>
/// <param name="CanReferenceWithForeignKey">Whether the tuple can be targeted under the provider's foreign-key DDL rules.</param>
/// <param name="NullPolicyClause">An optional trusted provider clause between UNIQUE and the key columns.</param>
public sealed record NativeUniqueConstraintPlan(UniqueNullPolicy NullPolicy, bool CanReferenceWithForeignKey, string? NullPolicyClause = null)
    : UniqueConstraintPlan(NullPolicy, CanReferenceWithForeignKey);

/// <summary>Ordered backing indexes that together enforce the declaration.</summary>
public sealed record UniqueIndexPlan(UniqueNullPolicy NullPolicy, bool CanReferenceWithForeignKey,
    IReadOnlyList<UniqueIndexDefinition> Indexes) : UniqueConstraintPlan(NullPolicy, CanReferenceWithForeignKey);

/// <summary>A unique index over the declaration's ordered columns.</summary>
/// <param name="Identity">The backing index's qualified identity.</param>
/// <param name="FilterPredicate">An optional trusted provider expression, without the WHERE keyword.</param>
/// <param name="KeySuffix">An optional trusted provider clause following the key columns and preceding WHERE.</param>
public sealed record UniqueIndexDefinition(DatabaseObjectName Identity, string? FilterPredicate = null, string? KeySuffix = null);
