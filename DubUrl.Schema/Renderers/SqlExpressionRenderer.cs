using DubUrl.Querying.Dialects;
using DubUrl.Querying.Dialects.Functions;
using DubUrl.Schema.Constraints;

namespace DubUrl.Schema.Renderers;

internal static class SqlExpressionRenderer
{
    public static string Render(Expression expression, IDialect dialect)
        => expression switch
        {
            ColumnIdentityExpression column => dialect.SchemaSqlRenderer.RenderIdentifier(dialect, column.Name),
            FunctionColumnIdentityExpression function =>
                $"{ResolveFunction(function.Function, dialect.SqlFunctionMapper)}({dialect.SchemaSqlRenderer.RenderIdentifier(dialect, function.Name)})",
            ValueExpression value => dialect.Renderer.Render(value.Value, "value"),
            ColumnExpression column => dialect.SchemaSqlRenderer.RenderIdentifier(dialect, column.Column.Name),
            FunctionExpression function =>
                $"{ResolveFunction(function.Function, dialect.SqlFunctionMapper)}({string.Join(", ", function.Expressions.Select(item => Render(item, dialect)))})",
            _ => throw new ArgumentException(
                $"Expression type '{expression.GetType().Name}' is not supported for SQL rendering.",
                nameof(expression))
        };

    public static string ResolveFunction(string function, ISqlFunctionMapper mapper)
        => mapper.ToDictionary().TryGetValue(function, out var mapped)
            ? mapped.ToString()!
            : throw new ArgumentException(
                $"Function '{function}' is not supported by the selected dialect.",
                nameof(function));
}
