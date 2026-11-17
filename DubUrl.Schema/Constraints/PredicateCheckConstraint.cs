using DubUrl.Parser;

namespace DubUrl.Schema.Constraints;

/// <summary>A check constraint defined by a typed row predicate.</summary>
public sealed class PredicateCheckConstraint : Constraint
{
    /// <summary>Initializes a named or unnamed predicate check.</summary>
    public PredicateCheckConstraint(SqlExpression predicate, string? name = null)
        : base(name)
        => Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));

    /// <summary>Gets the expression evaluated against the checked row.</summary>
    /// <value>The typed check predicate.</value>
    public SqlExpression Predicate { get; }
}
