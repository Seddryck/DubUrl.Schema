using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema;
public class Column
{
    public string Name { get; }
    public System.Data.DbType Type { get; }
    /// <summary>Gets the literal default.</summary>
    /// <value>The literal value, DBNull.Value for an explicit SQL NULL, or null if omitted.</value>
    public object? DefaultValue { get; }
    public ColumnDefaultExpression? DefaultExpression { get; }
    public ConstraintCollection Constraints { get; }
    public string? Description { get; }
    public NativeDatabaseType? NativeType { get; }
    public NativeTypeFallback NativeTypeFallback { get; }
    public ColumnGeneration? Generation { get; }

    public Column(string name, System.Data.DbType type, object? defaultValue, IConstraint[]? constraints,
        string? description, NativeDatabaseType? nativeType, NativeTypeFallback nativeTypeFallback, ColumnGeneration? generation)
        : this(name, type, defaultValue, constraints, description, nativeType, nativeTypeFallback, generation, null)
    { }

    public Column(string name, System.Data.DbType type, object? defaultValue = null, IConstraint[]? constraints = null,
        string? description = null, NativeDatabaseType? nativeType = null, NativeTypeFallback nativeTypeFallback = NativeTypeFallback.Error,
        ColumnGeneration? generation = null, ColumnDefaultExpression? defaultExpression = null)
        : this(name, type, defaultValue, constraints, description, nativeType, nativeTypeFallback, generation,
            defaultExpression, null, null, null)
    { }

    private protected Column(string name, System.Data.DbType type, object? defaultValue, IConstraint[]? constraints,
        string? description, NativeDatabaseType? nativeType, NativeTypeFallback nativeTypeFallback, ColumnGeneration? generation,
        ColumnDefaultExpression? defaultExpression, int? length, int? precision, int? scale)
    {
        ValidateDefaultDeclarations(defaultValue, defaultExpression, generation);
        defaultExpression?.ValidateDestination(type, length, precision, scale);

        (Name, Type, DefaultValue, DefaultExpression, Constraints, Description, NativeType, NativeTypeFallback, Generation) =
            (name, type, defaultValue, defaultExpression, CreateConstraints(constraints, defaultExpression),
                NormalizeDescription(description), nativeType, nativeTypeFallback, generation);
    }

    private static void ValidateDefaultDeclarations(object? defaultValue, ColumnDefaultExpression? defaultExpression,
        ColumnGeneration? generation)
    {
        switch (defaultValue, defaultExpression, generation)
        {
            case (ColumnDefaultExpression, _, _) or (not null, not null, _):
                throw new ArgumentException("Literal and expression defaults must be declared separately and are mutually exclusive.", nameof(defaultValue));
            case (not null, _, not null) or (_, not null, not null):
                throw new ArgumentException("A generated column cannot also define a default value.", nameof(generation));
        }
    }

    private static ConstraintCollection CreateConstraints(IConstraint[]? constraints, ColumnDefaultExpression? defaultExpression)
    {
        constraints ??= [];
        if (defaultExpression?.IsContext is true && !constraints.Any(constraint => constraint is NullableConstraint or NotNullableConstraint))
            constraints = [.. constraints, new NotNullableConstraint()];
        return new ConstraintCollection(constraints);
    }

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description;
}
