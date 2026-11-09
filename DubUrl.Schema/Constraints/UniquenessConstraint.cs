using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DubUrl.Schema.Constraints;

public enum UniqueNullPolicy
{
    /// <summary>Use the database's native UNIQUE behavior.</summary>
    Native,
    /// <summary>Enforce uniqueness only when every member of the tuple is non-null.</summary>
    NullsDistinct,
    /// <summary>Compare tuples member by member, treating null as equal to null.</summary>
    NullsNotDistinct
}

public class UniquenessConstraint : Constraint
{
    /// <summary>Empty for a column constraint; explicit column set for a table constraint.</summary>
    /// <value>
    /// Empty for a column constraint; explicit column set for a table constraint.
    /// </value>
    public IReadOnlyList<Column> Columns { get; } = [];
    public UniqueNullPolicy NullPolicy { get; }

    public UniquenessConstraint(Column[] columns, string? name = null)
        : this(columns, name, UniqueNullPolicy.Native)
    { }

    public UniquenessConstraint(Column[] columns, string? name, UniqueNullPolicy nullPolicy)
        : base(ValidateName(name))
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Length == 0 || columns.Any(column => column is null || string.IsNullOrWhiteSpace(column.Name))
            || columns.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count() != columns.Length)
            throw new ArgumentException("A table UNIQUE constraint requires distinct, non-null columns.", nameof(columns));
        if (!Enum.IsDefined(nullPolicy))
            throw new ArgumentOutOfRangeException(nameof(nullPolicy));
        Columns = Array.AsReadOnly(columns.ToArray());
        NullPolicy = nullPolicy;
    }

    public UniquenessConstraint(string? name = null)
        : base(ValidateName(name))
    { }

    private static string? ValidateName(string? name)
        => name is not null && string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A UNIQUE constraint name cannot be blank.", nameof(name))
            : name;
}
