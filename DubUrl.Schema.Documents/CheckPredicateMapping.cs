using DubUrl.Parser;
using DubUrl.Schema.Documents.V1;
using DubUrl.Schema.Renderers;

namespace DubUrl.Schema.Documents;

internal static class CheckPredicateMapping
{
    private static readonly IReadOnlyDictionary<string, SqlBinaryOperator> Operators = Enum.GetValues<SqlBinaryOperator>()
        .ToDictionary(CheckPredicateSqlRenderer.Operator, StringComparer.Ordinal);

    public static CheckPredicateDocument Export(SqlExpression node) => node switch
    {
        SqlExpression.BinaryOp binary => new()
        {
            Kind = "binary",
            Operator = CheckPredicateSqlRenderer.Operator(binary.Operator),
            Operands = [Export(binary.Left), Export(binary.Right)]
        },
        SqlExpression.IsNull test => new() { Kind = "is-null", Operands = [Export(test.Operand)] },
        SqlExpression.IsNotNull test => new() { Kind = "is-not-null", Operands = [Export(test.Operand)] },
        SqlExpression.Identifier identifier when identifier.Parts.Length == 1 => new()
        {
            Kind = "column",
            Name = identifier.Parts[0].Name,
            QuoteStyle = identifier.Parts[0].QuoteStyle?.ToString()
        },
        SqlExpression.Literal literal => new() { Kind = "literal", Value = ExportLiteral(literal) },
        _ => throw new NotSupportedException("The check predicate contains an unsupported node.")
    };

    public static SqlExpression Map(CheckPredicateDocument? node, IReadOnlyList<ColumnDocument>? columns = null)
    {
        if (node is null)
            throw new FormatException("A check predicate is required.");

        return node.Kind switch
        {
            "binary" when node is { Name: null, QuoteStyle: null, Value: null, Operands.Count: 2 }
                && node.Operator is not null && Operators.TryGetValue(node.Operator, out var op)
                => new SqlExpression.BinaryOp(Map(node.Operands[0], columns), op, Map(node.Operands[1], columns)),
            "is-null" when IsNullTest(node) => new SqlExpression.IsNull(Map(node.Operands[0], columns)),
            "is-not-null" when IsNullTest(node) => new SqlExpression.IsNotNull(Map(node.Operands[0], columns)),
            "column" when node is { Operator: null, Value: null, Name.Length: > 0, Operands.Count: 0 }
                => MapColumn(node.Name, node.QuoteStyle, columns),
            "literal" when node is { Name: null, QuoteStyle: null, Operator: null, Operands.Count: 0 }
                => MapLiteral(node.Value),
            _ => throw new FormatException($"Invalid check predicate node '{node.Kind}'.")
        };
    }

    private static bool IsNullTest(CheckPredicateDocument node)
        => node is { Name: null, QuoteStyle: null, Operator: null, Value: null, Operands.Count: 1 };

    private static SqlExpression.Identifier MapColumn(string name, string? quoteStyle, IReadOnlyList<ColumnDocument>? columns)
    {
        if (columns is not null && !columns.Any(column => column?.Name == name))
            throw new FormatException($"Check predicate references missing column '{name}'.");
        if (quoteStyle is not (null or "\"" or "[" or "`"))
            throw new FormatException("Invalid check predicate column quote style.");

        return new([new(name, quoteStyle?[0])]);
    }

    private static LiteralDocument ExportLiteral(SqlExpression.Literal literal) => literal switch
    {
        SqlExpression.Literal.Number number => new() { Type = "number", Value = number.Text },
        SqlExpression.Literal.String text => new() { Type = text.IsNational ? "national-string" : "string", Value = text.Value },
        SqlExpression.Literal.Boolean boolean => new() { Type = "boolean", Value = boolean.Value ? "true" : "false" },
        SqlExpression.Literal.Null => new() { Type = "null" },
        SqlExpression.Literal.TypedString text when text.SqlType is "DATE" or "TIME" or "TIMESTAMP"
            => new() { Type = text.SqlType.ToLowerInvariant(), Value = text.Value },
        _ => throw new NotSupportedException("The check predicate contains an unsupported literal.")
    };

    private static SqlExpression.Literal MapLiteral(LiteralDocument? value) => value switch
    {
        { Type: "number", Value: { } text } when CheckPredicateSqlRenderer.IsNumericLiteral(text)
            => new SqlExpression.Literal.Number(text),
        { Type: "string", Value: { } text } => new SqlExpression.Literal.String(text),
        { Type: "national-string", Value: { } text } => new SqlExpression.Literal.String(text, IsNational: true),
        { Type: "boolean", Value: "true" } => new SqlExpression.Literal.Boolean(true),
        { Type: "boolean", Value: "false" } => new SqlExpression.Literal.Boolean(false),
        { Type: "null", Value: null } => new SqlExpression.Literal.Null(),
        { Type: "date" or "time" or "timestamp", Value: { } text }
            => new SqlExpression.Literal.TypedString(value.Type.ToUpperInvariant(), text),
        _ => throw new FormatException("Invalid check predicate literal.")
    };
}
