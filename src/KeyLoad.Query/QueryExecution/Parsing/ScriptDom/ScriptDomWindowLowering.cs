using System.Globalization;
using KeyLoad.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.QueryExecution;

internal static class ScriptDomWindowLowering
{
    internal static (int Limit, int Offset) Read(Dom.QuerySpecification query, ScriptDomLoweringContext context)
    {
        context.Observe(RootDepth);
        var limit = SqlSyntax.DefaultLimit;
        var offset = EmptyCount;
        if (query.TopRowFilter is { } top)
        {
            if (top.Percent || top.WithTies || top.WithApproximate || query.OffsetClause is not null)
            {
                throw ScriptDomErrors.Unsupported();
            }
            limit = Count(top.Expression, context);
        }
        if (query.OffsetClause is { } window)
        {
            if (window.WithApproximate || query.OrderByClause is null)
            {
                throw ScriptDomErrors.Unsupported();
            }
            offset = Count(window.OffsetExpression, context);
            if (window.FetchExpression is not null)
            {
                limit = Count(window.FetchExpression, context);
            }
        }
        if (limit < MinimumLimit || offset < EmptyCount)
        {
            throw ScriptDomErrors.Invalid();
        }
        return (limit, offset);
    }
    private static int Count(Dom.ScalarExpression expression, ScriptDomLoweringContext context)
    {
        if (expression is Dom.PrimaryExpression { Collation: not null })
        {
            throw ScriptDomErrors.Unsupported();
        }
        while (expression is Dom.ParenthesisExpression grouped)
        {
            context.Observe(RootDepth);
            expression = grouped.Expression;
            if (expression is Dom.PrimaryExpression { Collation: not null })
            {
                throw ScriptDomErrors.Unsupported();
            }
        }
        if (expression is Dom.VariableReference or Dom.MoneyLiteral)
        {
            return ScriptDomParameterLowering.Count(expression, context);
        }
        if (expression is Dom.Literal and not Dom.IntegerLiteral
            || expression is Dom.UnaryExpression { Expression: Dom.Literal and not Dom.IntegerLiteral })
        {
            throw ScriptDomErrors.Invalid();
        }
        if (expression is Dom.UnaryExpression unary && unary.Expression is Dom.IntegerLiteral)
        {
            if (unary.UnaryExpressionType == Dom.UnaryExpressionType.Negative)
            {
                throw ScriptDomErrors.Invalid();
            }
            if (unary.UnaryExpressionType != Dom.UnaryExpressionType.Positive)
            {
                throw ScriptDomErrors.Unsupported();
            }
            expression = unary.Expression;
            if (expression is Dom.PrimaryExpression { Collation: not null })
            {
                throw ScriptDomErrors.Unsupported();
            }
        }
        if (expression is not Dom.IntegerLiteral literal)
        {
            throw ScriptDomErrors.Unsupported();
        }
        if (!int.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            throw ScriptDomErrors.Invalid();
        }
        return count;
    }
    private const int MinimumLimit = 1;

    private const int EmptyCount = 0;
    private const int RootDepth = 1;
}
