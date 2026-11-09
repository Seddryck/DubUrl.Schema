using System.Data;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using NUnit.Framework;

namespace DubUrl.Schema.Testing.Builders;

public class UniquenessConstraintBuilderTests
{
    [TestCase("missing")]
    [TestCase("repeated")]
    [TestCase("empty")]
    public void Build_RejectsInvalidUniqueMembers(string kind)
    {
        var names = kind switch
        {
            "missing" => new[] { "unknown" },
            "repeated" => new[] { "id", "id" },
            _ => Array.Empty<string>()
        };
        var builder = new TableBuilder().WithName("sample")
            .WithColumns(columns => columns.Add(column => column.WithName("id").WithType(DbType.Int32)))
            .WithConstraints(keys => keys.AddUnique(key => key.WithColumnNames(names)));
        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Test]
    public void Table_RejectsUniqueNameCollisionWithInlineConstraint()
    {
        var id = new Column("id", DbType.Int32, constraints: [new UniquenessConstraint("duplicate")]);
        var code = new Column("code", DbType.Int32);
        Assert.That(() => new Table("sample", [id, code], [new UniquenessConstraint([code], "duplicate")]),
            Throws.ArgumentException.With.Message.Contains("ambiguous"));
    }

    [Test]
    public void Table_RejectsConflictingPoliciesEvenWhenMemberOrderDiffers()
    {
        var first = new Column("first", DbType.Int32);
        var second = new Column("second", DbType.Int32);
        var distinct = new UniquenessConstraint([first, second], "distinct", UniqueNullPolicy.NullsDistinct);
        var notDistinct = new UniquenessConstraint([second, first], "not_distinct", UniqueNullPolicy.NullsNotDistinct);
        Assert.That(() => new Table("sample", [first, second], [distinct, notDistinct]),
            Throws.ArgumentException.With.Message.Contains("conflicting"));
    }
}
