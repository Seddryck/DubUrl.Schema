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
            _ => throw new ArgumentException("The default expression kind is unknown.")
        };
    }

    private static void ValidateArguments(DefaultExpressionDocument document)
    {
        var unexpectedPrecision = document.FractionalSecondsPrecision.HasValue && !SupportsFractionalSecondsPrecision(document.Kind);
        if (unexpectedPrecision)
        {
            throw new ArgumentException("The default expression has arguments that do not apply to its kind.");
        }
    }

    private static bool SupportsFractionalSecondsPrecision(string? kind)
        => kind is "current-timestamp" or "current-utc-timestamp" or "current-time";

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
            _ => throw new NotSupportedException("The default expression kind cannot be exported.")
        },
        FractionalSecondsPrecision = expression.FractionalSecondsPrecision
    };
}
