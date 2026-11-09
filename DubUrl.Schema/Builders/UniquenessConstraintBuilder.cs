using DbType = System.Data.DbType;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Builders;

public sealed class UniquenessConstraintBuilder : IConstraintBuilder
{
    private string[] columns = [];
    private string? name;
    private UniqueNullPolicy nullPolicy;

    public UniquenessConstraintBuilder WithName(string? name)
    {
        this.name = name;
        return this;
    }

    public UniquenessConstraintBuilder WithColumnNames(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        columns = names.ToArray();
        return this;
    }

    public UniquenessConstraintBuilder WithNullPolicy(UniqueNullPolicy policy)
    {
        nullPolicy = policy;
        return this;
    }

    Constraint IConstraintBuilder.Build()
        => new UniquenessConstraint(columns.Select(column => new Column(column, DbType.Object)).ToArray(), name, nullPolicy);
}
