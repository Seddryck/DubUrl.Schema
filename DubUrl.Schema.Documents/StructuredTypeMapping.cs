using System.Data;
using DubUrl.Schema.Documents.V1;

namespace DubUrl.Schema.Documents;

internal static class StructuredTypeMapping
{
    private static readonly IReadOnlyDictionary<string, StructuredStorage> StorageNames =
        new Dictionary<string, StructuredStorage>(StringComparer.Ordinal)
        {
            ["native"] = StructuredStorage.Native,
            ["json"] = StructuredStorage.Json,
            ["json-text"] = StructuredStorage.JsonText,
            ["binary"] = StructuredStorage.Binary
        };

    internal static StructuredColumnType Map(ColumnDocument column, string path)
    {
        (string Name, object? Value)[] incompatible =
        [
            ("type", column.Type), ("nativeType", column.NativeType), ("length", column.Length),
            ("precision", column.Precision), ("scale", column.Scale), ("identity", column.Identity),
            ("defaultExpression", column.DefaultExpression)
        ];
        foreach (var (name, value) in incompatible)
        {
            if (value is not null)
                throw new InvalidDescriptor($"'{name}' cannot be combined with a structured type.", $"{path}.{name}");
        }
        if (column.Default is { } literal && (literal.Type != "null" || column.Nullable is not true))
            throw new InvalidDescriptor("A structured column can only declare a SQL NULL default when it is nullable.", $"{path}.default");

        var document = column.StructuredType!;
        path += ".structuredType";
        if (document.Storage is null || !StorageNames.TryGetValue(document.Storage, out var storage))
            throw new InvalidDescriptor("A known storage representation is required.", $"{path}.storage");
        var shape = MapShape(document.Shape, $"{path}.shape", 0);
        if (shape is ScalarType)
            throw new InvalidDescriptor("A column requires a structured shape.", $"{path}.shape");
        return new(shape, storage);
    }

    private static StructuredType MapShape(StructuredTypeDocument? shape, string path, int depth)
    {
        if (shape is null)
            throw new InvalidDescriptor("A type shape is required.", path);
        if (depth >= 64)
            throw new InvalidDescriptor("A type shape exceeds the maximum nesting depth.", path);

        try
        {
            switch (shape.Kind)
            {
                case "scalar":
                    ValidateProperties(shape, path, "type");
                    return new ScalarType(MapScalar(shape.Type, $"{path}.type"));
                case "json":
                    ValidateProperties(shape, path, "objectShape");
                    return new JsonType(shape.ObjectShape is null ? null
                        : MapObject(shape.ObjectShape, $"{path}.objectShape", depth + 1));
                case "array":
                    ValidateProperties(shape, path, "element", "elementNullable");
                    return new ArrayType(MapShape(shape.Element, $"{path}.element", depth + 1), shape.ElementNullable);
                case "map":
                    ValidateProperties(shape, path, "key", "value", "valueNullable");
                    return new MapType(MapShape(shape.Key, $"{path}.key", depth + 1),
                        MapShape(shape.Value, $"{path}.value", depth + 1), shape.ValueNullable);
                case "struct":
                    ValidateProperties(shape, path, "fields");
                    return MapFields(shape.Fields, $"{path}.fields", depth + 1);
                case "vector":
                    ValidateProperties(shape, path, "type", "dimensions");
                    if (shape.Dimensions is null or <= 0)
                        throw new InvalidDescriptor("Vector dimensions must be positive.", $"{path}.dimensions");
                    return new VectorType(MapScalar(shape.Type, $"{path}.type"), shape.Dimensions.Value);
                default:
                    throw new InvalidDescriptor("A known type shape kind is required.", $"{path}.kind");
            }
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "elementType")
        {
            throw new InvalidDescriptor("Vector components must have a numeric type.", $"{path}.type");
        }
    }

    private static StructType MapObject(StructuredTypeDocument shape, string path, int depth)
        => MapShape(shape, path, depth) as StructType
            ?? throw new InvalidDescriptor("A JSON object shape must be a struct.", path);

    private static StructType MapFields(List<StructFieldDocument>? fields, string path, int depth)
    {
        if (fields is not { Count: > 0 })
            throw new InvalidDescriptor("A struct requires at least one field.", path);
        var names = new HashSet<string>(StringComparer.Ordinal);
        return new(fields.Select((field, index) =>
        {
            var fieldPath = $"{path}[{index}]";
            if (field?.Name is not { } name || string.IsNullOrWhiteSpace(name) || name.Contains('\0') || !names.Add(name))
                throw new InvalidDescriptor("Struct fields require valid, unique names.", $"{fieldPath}.name");
            return new StructField(name, MapShape(field.Type, $"{fieldPath}.type", depth), field.Nullable);
        }));
    }

    private static DbType MapScalar(string? name, string path)
        => SchemaDocumentTypes.TryGetDbType(name, out var type) && type != DbType.Object ? type
            : throw new InvalidDescriptor("A known scalar type is required.", path);

    private static void ValidateProperties(StructuredTypeDocument shape, string path, params string[] allowed)
    {
        (string Name, object? Value)[] properties =
        [
            ("type", shape.Type), ("objectShape", shape.ObjectShape), ("element", shape.Element),
            ("elementNullable", shape.ElementNullable), ("key", shape.Key), ("value", shape.Value),
            ("valueNullable", shape.ValueNullable), ("fields", shape.Fields), ("dimensions", shape.Dimensions)
        ];
        foreach (var (name, value) in properties)
        {
            if (value is not null && !allowed.Contains(name, StringComparer.Ordinal))
                throw new InvalidDescriptor($"'{name}' is not valid for the '{shape.Kind}' shape.", $"{path}.{name}");
        }
    }

    internal static StructuredColumnTypeDocument Export(StructuredColumnType type) => new()
    {
        Storage = StorageNames.Single(pair => pair.Value == type.Storage).Key,
        Shape = ExportShape(type.Shape)
    };

    private static StructuredTypeDocument ExportShape(StructuredType type) => type switch
    {
        ScalarType scalar => new() { Kind = "scalar", Type = SchemaDocumentTypes.GetName(scalar.Type) },
        JsonType json => new() { Kind = "json", ObjectShape = json.ObjectShape is null ? null : ExportShape(json.ObjectShape) },
        ArrayType array => new() { Kind = "array", Element = ExportShape(array.Element), ElementNullable = array.ElementNullable },
        MapType map => new() { Kind = "map", Key = ExportShape(map.Key), Value = ExportShape(map.Value), ValueNullable = map.ValueNullable },
        StructType structure => new()
        {
            Kind = "struct",
            Fields = structure.Fields.Select(field => new StructFieldDocument
            {
                Name = field.Name, Type = ExportShape(field.Type), Nullable = field.Nullable
            }).ToList()
        },
        VectorType vector => new() { Kind = "vector", Type = SchemaDocumentTypes.GetName(vector.ElementType), Dimensions = vector.Dimensions },
        _ => throw new NotSupportedException("Unknown structured type.")
    };

    internal sealed class InvalidDescriptor(string message, string path) : ArgumentException(message)
    {
        internal string Path { get; } = path;
    }
}
