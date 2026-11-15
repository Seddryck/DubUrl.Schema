using System.Data;
using System.Text.RegularExpressions;
using DubUrl.Parser;
using DubUrl.Querying.Dialects;

namespace DubUrl.Schema.Renderers;

internal static class CheckPredicateSqlRenderer
{
    private static readonly Regex Number = new(
        @"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[Ee][+-]?[0-9]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static bool IsNumericLiteral(string? value) => value is not null && Number.IsMatch(value);

    internal static string Render(SqlExpression expression, IDialect dialect)
        => expression switch
        {
            SqlExpression.BinaryOp binary =>
                $"({Render(binary.Left, dialect)} {Operator(binary.Operator)} {Render(binary.Right, dialect)})",
            SqlExpression.IsNull test => $"({Render(test.Operand, dialect)} IS NULL)",
            SqlExpression.IsNotNull test => $"({Render(test.Operand, dialect)} IS NOT NULL)",
            SqlExpression.Identifier identifier when identifier.Parts.Length == 1 =>
                dialect.SchemaSqlRenderer.RenderIdentifier(dialect, identifier.Parts[0].Name),
            SqlExpression.Literal.Number number when IsNumericLiteral(number.Text) => number.Text,
            SqlExpression.Literal.String text => dialect.Renderer.Render(text.Value, "value"),
            SqlExpression.Literal.Boolean boolean => dialect.Renderer.Render(boolean.Value, "value"),
            SqlExpression.Literal.Null => "NULL",
            SqlExpression.Literal.TypedString literal => RenderTypedLiteral(literal, dialect),
            _ => throw new NotSupportedException($"Unsupported check predicate node '{expression.GetType().Name}'."),
        };

    internal static string Operator(SqlBinaryOperator op) => op switch
    {
        SqlBinaryOperator.Or => "OR",
        SqlBinaryOperator.And => "AND",
        SqlBinaryOperator.Equal => "=",
        SqlBinaryOperator.NotEqual => "<>",
        SqlBinaryOperator.LessThan => "<",
        SqlBinaryOperator.GreaterThan => ">",
        SqlBinaryOperator.LessThanOrEqual => "<=",
        SqlBinaryOperator.GreaterThanOrEqual => ">=",
        _ => throw new NotSupportedException($"Unsupported check predicate operator '{op}'."),
    };

    private static string RenderTypedLiteral(SqlExpression.Literal.TypedString literal, IDialect dialect)
    {
        var type = literal.SqlType switch
        {
            "DATE" => DbType.Date,
            "TIME" => DbType.Time,
            "TIMESTAMP" => DbType.DateTime,
            _ => throw new NotSupportedException($"Unsupported check literal type '{literal.SqlType}'."),
        };
        var value = dialect.Renderer.Render(literal.Value, "value");
        return $"CAST({value} AS {dialect.DbTypeMapper.ToDictionary()[type.ToString()]})";
    }
}
