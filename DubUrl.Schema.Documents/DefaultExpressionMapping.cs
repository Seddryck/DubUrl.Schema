using DubUrl.Schema.Documents.V1;

namespace DubUrl.Schema.Documents;

internal static class DefaultExpressionMapping
{
    internal static ColumnDefaultExpression Map(DefaultExpressionDocument document)
    {
        ValidateArguments(document);
        return document.Kind switch
        {
            "current-timestamp" => ColumnDefaultExpression.CurrentTimestamp(document.FractionalSecondsPrecision),
            "current-date" => ColumnDefaultExpression.CurrentDate,
            "current-time" => ColumnDefaultExpression.CurrentTime(document.FractionalSecondsPrecision),
            "current-utc-timestamp" => ColumnDefaultExpression.CurrentUtcTimestamp(document.FractionalSecondsPrecision),
            "current-user" => ColumnDefaultExpression.CurrentUser,
            "session-user" => ColumnDefaultExpression.SessionUser,
            "current-schema" => ColumnDefaultExpression.CurrentSchema,
            "current-database" => ColumnDefaultExpression.CurrentDatabase,
            "new-uuid" => ColumnDefaultExpression.NewUuid(ParseRepresentation(document.Representation), ParseByteOrder(document.ByteOrder)),
            _ => throw new ArgumentException("The default expression kind is unknown.")
        };
    }

    private static void ValidateArguments(DefaultExpressionDocument document)
    {
        var unexpectedPrecision = document.FractionalSecondsPrecision.HasValue && !SupportsFractionalSecondsPrecision(document.Kind);
        var unexpectedUuidArguments = document.Kind != "new-uuid" && (document.Representation ?? document.ByteOrder) is not null;
        if (unexpectedPrecision || unexpectedUuidArguments)
        {
            throw new ArgumentException("The default expression has arguments that do not apply to its kind.");
        }
    }

    private static bool SupportsFractionalSecondsPrecision(string? kind)
        => kind is "current-timestamp" or "current-utc-timestamp" or "current-time";

    private static UuidRepresentation ParseRepresentation(string? value) => value switch
    {
        "native" => UuidRepresentation.Native,
        "binary" => UuidRepresentation.Binary,
        "canonical-text" => UuidRepresentation.CanonicalText,
        _ => throw new ArgumentException("A UUID representation of 'native', 'binary' or 'canonical-text' is required.")
    };

    private static UuidByteOrder? ParseByteOrder(string? value) => value switch
    {
        null => null,
        "network" => UuidByteOrder.Network,
        "dotnet-mixed-endian" => UuidByteOrder.DotNetMixedEndian,
        _ => throw new ArgumentException("UUID byte order must be 'network' or 'dotnet-mixed-endian'.")
    };

    internal static DefaultExpressionDocument Export(ColumnDefaultExpression expression) => new()
    {
        Kind = expression.Kind switch
        {
            ColumnDefaultKind.CurrentTimestamp => "current-timestamp",
            ColumnDefaultKind.CurrentDate => "current-date",
            ColumnDefaultKind.CurrentTime => "current-time",
            ColumnDefaultKind.CurrentUtcTimestamp => "current-utc-timestamp",
            ColumnDefaultKind.CurrentUser => "current-user",
            ColumnDefaultKind.SessionUser => "session-user",
            ColumnDefaultKind.CurrentSchema => "current-schema",
            ColumnDefaultKind.CurrentDatabase => "current-database",
            ColumnDefaultKind.NewUuid => "new-uuid",
            _ => throw new NotSupportedException("The default expression kind cannot be exported.")
        },
        FractionalSecondsPrecision = expression.FractionalSecondsPrecision,
        Representation = expression.Representation switch
        {
            UuidRepresentation.Native => "native",
            UuidRepresentation.Binary => "binary",
            UuidRepresentation.CanonicalText => "canonical-text",
            _ => null
        },
        ByteOrder = expression.ByteOrder switch
        {
            UuidByteOrder.Network => "network",
            UuidByteOrder.DotNetMixedEndian => "dotnet-mixed-endian",
            _ => null
        }
    };
}
