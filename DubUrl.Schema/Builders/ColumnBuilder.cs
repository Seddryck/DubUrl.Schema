using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Builders;

public class ColumnBuilder : IColumnName, IColumnTypeBuilder, IColumnNumericBuilder, IColumnTypeSettingsBuilder, IColumnConstraintBuilder
{
    public string Name { get; private set; } = string.Empty;
    private DbType Type { get; set; } = DbType.Object;
    private bool HasScalarType { get; set; }
    private StructuredColumnType? StructuredType { get; set; }
    private int? Length { get; set; }
    private int? Scale { get; set; }
    private object? DefaultValue { get; set; }
    private ColumnDefaultExpression? DefaultExpression { get; set; }
    private string? Description { get; set; }
    private IReadOnlyDictionary<string, string> Annotations { get; set; } = ImmutableSortedDictionary<string, string>.Empty;
    private NativeDatabaseType? NativeType { get; set; }
    private NativeTypeFallback NativeTypeFallback { get; set; }
    private ColumnGeneration? Generation { get; set; }
    private ColumnConstraintCollectionBuilder Constraints { get; } = [];

    public IColumnTypeBuilder WithName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Column name cannot be empty or whitespace.", nameof(name));
        Name = name;
        return this;
    }

    IColumnTypeSettingsBuilder IColumnTypeBuilder.WithType(DbType type)
    {
        Type = type;
        HasScalarType = true;
        return this;
    }

    IColumnConstraintBuilder IColumnTypeBuilder.WithStructuredType(StructuredColumnType type)
    {
        StructuredType = type ?? throw new ArgumentNullException(nameof(type));
        return this;
    }

    IColumnConstraintBuilder IColumnTypeBuilder.WithLength(int value)
    {
        Length = value;
        return this;
    }

    IColumnNumericBuilder IColumnTypeBuilder.WithPrecision(int value)
    {
        Length = value;
        return this;
    }

    IColumnConstraintBuilder IColumnNumericBuilder.WithScale(int value)
    {
        if (value>=Length)
            throw new ArgumentOutOfRangeException(nameof(value), "Scale must be less than precision");
        Scale = value;
        return this;
    }

    IColumnConstraintBuilder IColumnConstraintBuilder.WithDefaultValue(object value)
    {
        DefaultValue = value;
        return this;
    }

    IColumnConstraintBuilder IColumnConstraintBuilder.WithDefaultExpression(ColumnDefaultExpression expression)
    {
        DefaultExpression = expression ?? throw new ArgumentNullException(nameof(expression));
        return this;
    }
    IColumnConstraintBuilder IColumnConstraintBuilder.WithNullable(bool value)
    {
        if (value)
            Constraints.AddNullable();
        else
            Constraints.AddNotNullable();
        return this;
    }

    IColumnConstraintBuilder IColumnConstraintBuilder.WithNullable()
    {
        Constraints.AddNullable();
        return this;
    }
    IColumnConstraintBuilder IColumnConstraintBuilder.WithNotNullable()
    {
        Constraints.AddNotNullable();
        return this;
    }
    IColumnConstraintBuilder IColumnConstraintBuilder.WithUnique()
    {
        Constraints.AddUnique();
        return this;
    }
    IColumnConstraintBuilder IColumnConstraintBuilder.WithPrimaryKey()
    {
        Constraints.AddPrimaryKey();
        return this;
    }
    IColumnConstraintBuilder IColumnConstraintBuilder.WithIdentity(long seed, long increment)
    {
        Generation = new IdentityColumnGeneration(seed, increment);
        return this;
    }
    IColumnConstraintBuilder IColumnConstraintBuilder.WithGenerated(Expression expression, GeneratedColumnStorage storage)
    {
        Generation = new GeneratedColumnGeneration(expression, storage);
        return this;
    }
    IColumnConstraintBuilder IColumnConstraintBuilder.WithCheck(Func<ICheckBuilder, ICheckBuildable> builder)
    {
        Constraints.AddCheck(builder(new CheckBuilder(this)).Build());
        return this;
    }

    IColumnConstraintBuilder IColumnConstraintBuilder.WithDescription(string? description)
    {
        Description = description;
        return this;
    }

    IColumnConstraintBuilder IColumnConstraintBuilder.WithAnnotations(IReadOnlyDictionary<string, string> annotations)
    {
        Annotations = annotations ?? throw new ArgumentNullException(nameof(annotations));
        return this;
    }

    IColumnConstraintBuilder IColumnConstraintBuilder.WithNativeType(NativeDatabaseType nativeType, NativeTypeFallback fallback)
    {
        NativeType = nativeType ?? throw new ArgumentNullException(nameof(nativeType));
        NativeTypeFallback = fallback;
        return this;
    }

    Column IColumnBuilder.Build()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidOperationException("Column name must be set before building");

        var constraints = Constraints.Build();

        if (StructuredType is not null)
        {
            if (HasScalarType || Length.HasValue || Scale.HasValue || NativeType is not null)
                throw new InvalidOperationException("A structured type cannot be combined with a scalar type, scalar facets, or a native type override.");
            if (Generation is IdentityColumnGeneration || DefaultExpression is not null)
                throw new InvalidOperationException("A structured type cannot use identity generation or a typed default expression.");
            return new Column(Name, StructuredType, DefaultValue, constraints, Description, Generation as GeneratedColumnGeneration)
            { Annotations = Annotations };
        }

        if (Scale.HasValue && Length.HasValue)
        {
            return new NumericColumn(Name, Type, Length.Value, Scale.Value, DefaultValue, constraints, Description, NativeType, NativeTypeFallback, Generation, DefaultExpression)
            { Annotations = Annotations };
        }

        if (Length.HasValue)
        {
            return new VarLengthColumn(Name, Type, Length.Value, DefaultValue, constraints, Description, NativeType, NativeTypeFallback, Generation, DefaultExpression)
            { Annotations = Annotations };
        }

        return new Column(Name, Type, DefaultValue, constraints, Description, NativeType, NativeTypeFallback, Generation, DefaultExpression)
        { Annotations = Annotations };
    }
}
