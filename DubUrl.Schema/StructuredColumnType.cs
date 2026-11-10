using System.Data;

namespace DubUrl.Schema;

/// <summary>The storage requested by the caller for a structured column.</summary>
public enum StructuredStorage
{
    Native,
    Json,
    JsonText,
    Binary
}

public sealed record StructuredColumnType
{
    public StructuredType Shape { get; }
    public StructuredStorage Storage { get; }

    public StructuredColumnType(StructuredType shape, StructuredStorage storage)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape is ScalarType)
            throw new ArgumentException("A structured column requires a structured shape.", nameof(shape));
        if (!Enum.IsDefined(storage))
            throw new ArgumentOutOfRangeException(nameof(storage));
        Shape = shape;
        Storage = storage;
    }
}

public abstract record StructuredType
{
    private protected StructuredType() { }

    internal abstract bool HasUnknownNullability { get; }
}

/// <summary>A portable scalar leaf in a structured shape.</summary>
public sealed record ScalarType : StructuredType
{
    public DbType Type { get; }
    internal override bool HasUnknownNullability => false;

    public ScalarType(DbType type)
    {
        if (!Enum.IsDefined(type) || type == DbType.Object)
            throw new ArgumentOutOfRangeException(nameof(type), "A scalar leaf requires a known scalar type.");
        Type = type;
    }
}

/// <summary>JSON with an optional known object shape. An omitted shape allows arbitrary JSON.</summary>
public sealed record JsonType(StructType? ObjectShape = null) : StructuredType
{
    internal override bool HasUnknownNullability => ObjectShape?.HasUnknownNullability is true;
}

public sealed record ArrayType : StructuredType
{
    public StructuredType Element { get; }
    public bool? ElementNullable { get; }
    internal override bool HasUnknownNullability => ElementNullable is null || Element.HasUnknownNullability;

    public ArrayType(StructuredType element, bool? elementNullable)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
        ElementNullable = elementNullable;
    }
}

/// <summary>A map with non-null keys and independently declared value nullability.</summary>
public sealed record MapType : StructuredType
{
    public StructuredType Key { get; }
    public StructuredType Value { get; }
    public bool? ValueNullable { get; }
    internal override bool HasUnknownNullability => ValueNullable is null || Key.HasUnknownNullability || Value.HasUnknownNullability;

    public MapType(StructuredType key, StructuredType value, bool? valueNullable)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ValueNullable = valueNullable;
    }
}

public sealed record StructField
{
    public string Name { get; }
    public StructuredType Type { get; }
    public bool? Nullable { get; }

    public StructField(string name, StructuredType type, bool? nullable)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0'))
            throw new ArgumentException("A field requires a valid name.", nameof(name));
        Name = name;
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Nullable = nullable;
    }
}

public sealed record StructType : StructuredType
{
    public IReadOnlyList<StructField> Fields { get; }
    internal override bool HasUnknownNullability => Fields.Any(field => field.Nullable is null || field.Type.HasUnknownNullability);

    public StructType(IEnumerable<StructField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var snapshot = fields.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(field => field is null)
            || snapshot.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("A struct requires at least one field and unique field names.", nameof(fields));
        Fields = Array.AsReadOnly(snapshot);
    }

    public bool Equals(StructType? other) => other is not null && Fields.SequenceEqual(other.Fields);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (var field in Fields)
            hash.Add(field);
        return hash.ToHashCode();
    }
}

/// <summary>A fixed number of non-null numeric components.</summary>
public sealed record VectorType : StructuredType
{
    public DbType ElementType { get; }
    public int Dimensions { get; }
    internal override bool HasUnknownNullability => false;

    public VectorType(DbType elementType, int dimensions)
    {
        if (elementType is not (DbType.Byte or DbType.SByte or DbType.Int16 or DbType.UInt16 or DbType.Int32
            or DbType.UInt32 or DbType.Int64 or DbType.UInt64 or DbType.Single or DbType.Double or DbType.Decimal))
            throw new ArgumentOutOfRangeException(nameof(elementType), "Vector components must have a numeric type.");
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Vector dimensions must be positive.");
        ElementType = elementType;
        Dimensions = dimensions;
    }
}
