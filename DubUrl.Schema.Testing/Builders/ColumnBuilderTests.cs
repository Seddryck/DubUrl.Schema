using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Schema.Builders;
using DubUrl.Schema.Constraints;
using NUnit.Framework;

namespace DubUrl.Schema.Testing.Builders;

public class ColumnBuilderTests
{
    [Test]
    public void Build_StructuredTypesPreserveShapeAndRejectConflictingDeclarations()
    {
        var type = new StructuredColumnType(new ArrayType(new ScalarType(DbType.String), false), StructuredStorage.Json);
        var column = new ColumnBuilder().WithName("values").WithStructuredType(type).WithNullable().Build();
        Assert.That(column.StructuredType, Is.SameAs(type));
        Assert.That(column.Constraints.Get<NullableConstraint>(), Is.Not.Null);

        Assert.That(() => new ColumnBuilder().WithName("values").WithType(DbType.Object).WithStructuredType(type).Build(),
            Throws.InvalidOperationException);
        Assert.That(() => new ColumnBuilder().WithName("values").WithStructuredType(type).WithNativeType(new("JSON")).Build(),
            Throws.InvalidOperationException);
        Assert.That(() => new ColumnBuilder().WithName("values").WithType(DbType.String).WithLength(50).Build().StructuredType,
            Is.Null);
        Assert.That(() => new Column("values", type, "[]"), Throws.ArgumentException);
        Assert.That(() => new Column("values", type, constraints: [new NullableConstraint(), new NotNullableConstraint()]),
            Throws.ArgumentException);
        Assert.That(() => new Column("values", type, DBNull.Value, [new NotNullableConstraint()]), Throws.ArgumentException);
    }

    [Test]
    public void StructuredTypes_ValidateAndSnapshotTheirShapes()
    {
        var scalar = new ScalarType(DbType.String);
        StructField[] fields = [new("Part.Name", scalar, true), new("part.name", scalar, false)];
        var structure = new StructType(fields);
        fields[1] = fields[0];
        Assert.That(structure.Fields.Select(field => field.Name), Is.EqualTo(new[] { "Part.Name", "part.name" }));
        Assert.That(() => new StructType(fields), Throws.ArgumentException);
        Assert.That(() => new StructuredColumnType(scalar, StructuredStorage.Native), Throws.ArgumentException);
        Assert.That(() => new StructuredColumnType(structure, (StructuredStorage)42), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new VectorType(DbType.String, 8), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new VectorType(DbType.Single, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Build_TypedDefaultConflictsAreRejectedInEitherOrder()
    {
        var expression = ColumnDefaultExpression.CurrentDate;
        var builder = new ColumnBuilder().WithName("date").WithType(DbType.Date);
        Assert.That(() => builder.WithDefaultExpression(expression).WithDefaultValue(DBNull.Value).Build(), Throws.ArgumentException);
        builder = new ColumnBuilder().WithName("date").WithType(DbType.Date);
        Assert.That(() => builder.WithDefaultValue(DateTime.Today).WithDefaultExpression(expression).Build(), Throws.ArgumentException);
        builder = new ColumnBuilder().WithName("date").WithType(DbType.Date);
        Assert.That(() => builder.WithIdentity().WithDefaultExpression(expression).Build(), Throws.ArgumentException);
        builder = new ColumnBuilder().WithName("date").WithType(DbType.Date);
        Assert.That(() => builder.WithDefaultExpression(expression).WithGenerated(new ValueExpression(DateTime.Today)).Build(), Throws.ArgumentException);
    }

    [Test]
    public void TypedDefaults_RejectInvalidArgumentsAndDestinations()
    {
        Assert.That(() => ColumnDefaultExpression.CurrentTime(10), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => ColumnDefaultExpression.NewUuid(UuidRepresentation.Binary), Throws.ArgumentException);
        Assert.That(() => ColumnDefaultExpression.NewUuid(UuidRepresentation.Native, UuidByteOrder.Network), Throws.ArgumentException);
        Assert.That(() => ColumnDefaultExpression.NextSequenceValue(new("unqualified")), Throws.ArgumentException);
        Assert.That(() => ColumnDefaultExpression.NextSequenceValue(new("name", " ")), Throws.ArgumentException);
        Assert.That(() => new Column("value", DbType.DateTime, defaultExpression: ColumnDefaultExpression.CurrentTime()), Throws.ArgumentException);
        Assert.That(() => new Column("value", DbType.String, defaultExpression: ColumnDefaultExpression.CurrentUser), Throws.ArgumentException);
        Assert.That(() => new VarLengthColumn("value", DbType.String, 35,
            defaultExpression: ColumnDefaultExpression.NewUuid(UuidRepresentation.CanonicalText)), Throws.ArgumentException);
        Assert.That(() => new VarLengthColumn("value", DbType.Binary, 17,
            defaultExpression: ColumnDefaultExpression.NewUuid(UuidRepresentation.Binary, UuidByteOrder.Network)), Throws.ArgumentException);
        var sequence = ColumnDefaultExpression.NextSequenceValue(new("sequence", "schema"));
        Assert.That(() => new Column("value", DbType.Double, defaultExpression: sequence), Throws.ArgumentException);
        Assert.That(() => new NumericColumn("value", DbType.Decimal, 2, 2, defaultExpression: sequence), Throws.ArgumentException);
    }

    [Test]
    public void Build_Simple_Expected()
    {
        var builder = new ColumnBuilder().WithName("value").WithType(DbType.Boolean);
        var column = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(column, Is.Not.Null);
            Assert.That(column, Is.TypeOf<Column>());
        });
        Assert.Multiple(() =>
        {
            Assert.That(column.Name, Is.EqualTo("value"));
            Assert.That(column.Type, Is.EqualTo(DbType.Boolean));
            Assert.That(column.DefaultValue, Is.Null);
        });
    }

    [Test]
    public void Build_Full_Expected()
    {
        var builder = new ColumnBuilder()
                        .WithName("value")
                        .WithType(DbType.Boolean)
                        .WithNullable()
                        .WithDefaultValue(false);
        var column = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(column, Is.Not.Null);
            Assert.That(column, Is.TypeOf<Column>());
        });
        Assert.Multiple(() =>
        {
            Assert.That(column.Name, Is.EqualTo("value"));
            Assert.That(column.Type, Is.EqualTo(DbType.Boolean));
            Assert.That(column.DefaultValue, Is.False);
        });
    }

    [Test]
    public void Build_VarLength_Expected()
    {
        var builder = new ColumnBuilder()
                        .WithName("value")
                        .WithType(DbType.AnsiString)
                        .WithLength(255);
        var column = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(column, Is.Not.Null);
            Assert.That(column, Is.TypeOf<VarLengthColumn>());
        });
        var varLength = (VarLengthColumn)column;
        Assert.Multiple(() =>
        {
            Assert.That(varLength.Name, Is.EqualTo("value"));
            Assert.That(varLength.Type, Is.EqualTo(DbType.AnsiString));
            Assert.That(varLength.Length, Is.EqualTo(255));
        });
    }

    [Test]
    public void Build_Numeric_Expected()
    {
        var builder = new ColumnBuilder()
                        .WithName("value")
                        .WithType(DbType.VarNumeric)
                        .WithPrecision(10)
                        .WithScale(3);
        var column = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(column, Is.Not.Null);
            Assert.That(column, Is.TypeOf<NumericColumn>());
        });
        var numeric = (NumericColumn)column;
        Assert.Multiple(() =>
        {
            Assert.That(numeric.Name, Is.EqualTo("value"));
            Assert.That(numeric.Type, Is.EqualTo(DbType.VarNumeric));
            Assert.That(numeric.Precision, Is.EqualTo(10));
            Assert.That(numeric.Scale, Is.EqualTo(3));
        });
    }

    [Test]
    public void Build_Identity_PreservesSeedAndIncrement()
    {
        var column = new ColumnBuilder()
            .WithName("Id")
            .WithType(DbType.Int64)
            .WithIdentity(100, 5)
            .Build();

        Assert.That(column.Generation, Is.TypeOf<IdentityColumnGeneration>());
        var identity = (IdentityColumnGeneration)column.Generation!;
        Assert.Multiple(() =>
        {
            Assert.That(identity.Seed, Is.EqualTo(100));
            Assert.That(identity.Increment, Is.EqualTo(5));
        });
    }

    [Test]
    public void Build_Generated_PreservesExpressionAndStorage()
    {
        var expression = new FunctionColumnIdentityExpression("Length", "Name");

        var column = new ColumnBuilder()
            .WithName("NameLength")
            .WithType(DbType.Int32)
            .WithGenerated(expression, GeneratedColumnStorage.Virtual)
            .Build();

        Assert.That(column.Generation, Is.TypeOf<GeneratedColumnGeneration>());
        var generated = (GeneratedColumnGeneration)column.Generation!;
        Assert.Multiple(() =>
        {
            Assert.That(generated.Expression, Is.SameAs(expression));
            Assert.That(generated.Storage, Is.EqualTo(GeneratedColumnStorage.Virtual));
        });
    }

    [Test]
    public void Build_GeneratedWithDefaultValue_Throws()
    {
        var builder = new ColumnBuilder()
            .WithName("NameLength")
            .WithType(DbType.Int32)
            .WithGenerated(new FunctionColumnIdentityExpression("Length", "Name"))
            .WithDefaultValue(0);

        var exception = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.That(exception.Message, Does.Contain("cannot also define a default value"));
    }
}
