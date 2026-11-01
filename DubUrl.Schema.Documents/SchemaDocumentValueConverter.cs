using DubUrl.Schema.Documents.V1;

namespace DubUrl.Schema.Documents;

internal sealed class SchemaDocumentValueConverter
{
    private readonly SchemaDocumentLiteralConverterRegistry registry;

    public SchemaDocumentValueConverter(SchemaDocumentLiteralConverterRegistry registry)
        => this.registry = (registry ?? throw new ArgumentNullException(nameof(registry))).Snapshot();

    public bool TryConvert(LiteralDocument literal, out object? value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(literal);
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(literal.Type))
        {
            error = "A literal type is required.";
            return false;
        }

        if (literal.Type == "null")
            return TryConvertNull(literal, out error);

        return TryConvertTypedValue(literal, out value, out error);
    }

    private static bool TryConvertNull(LiteralDocument literal, out string? error)
    {
        error = literal.Value is null ? null : "A null literal cannot contain a value.";
        return error is null;
    }

    private bool TryConvertTypedValue(LiteralDocument literal, out object? value, out string? error)
    {
        value = null;
        error = null;
        if (!SchemaDocumentTypes.TryGetDbType(literal.Type, out var type))
        {
            error = $"Literal type '{literal.Type}' is not supported.";
            return false;
        }
        if (literal.Value is null)
        {
            error = $"A value is required for literal type '{literal.Type}'.";
            return false;
        }

        if (!registry.TryGetConverter(type, out var converter))
        {
            error = $"Literal type '{literal.Type}' is not supported.";
            return false;
        }

        try
        {
            var result = converter.TryConvert(literal.Value);
            value = result.Value;
            if (result.IsSuccess)
                return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        { }

        value = null;
        error = $"Value '{literal.Value}' is not valid for literal type '{literal.Type}'.";
        return false;
    }
}
