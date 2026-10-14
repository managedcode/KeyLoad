using System.Globalization;
using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomOperandLowering
{
    internal static Operand Read(Dom.ScalarExpression expression, string? alias, string? rightAlias,
        ScriptDomLoweringContext context, int depth)
    {
        context.Check(depth);
        if (expression is Dom.PrimaryExpression { Collation: not null })
        {
            throw ScriptDomErrors.Unsupported();
        }
        return expression switch
        {
            Dom.ColumnReferenceExpression column => Field(column, alias, rightAlias, context.UseDocumentMetadata),
            Dom.StringLiteral value => ValueOperand.Create(value.Value),
            Dom.NullLiteral => ValueOperand.Create(null),
            Dom.IntegerLiteral value => Number(value.Value),
            Dom.NumericLiteral value => Number(value.Value),
            Dom.RealLiteral value => Number(value.Value),
            Dom.VariableReference or Dom.MoneyLiteral => new ParameterOperand(ScriptDomParameterLowering.Name(expression, context)),
            Dom.ParenthesisExpression grouped => Read(grouped.Expression, alias, rightAlias, context, depth + NextDepth),
            Dom.UnaryExpression unary => Unary(unary, context, depth),
            _ => throw ScriptDomErrors.Unsupported()
        };
    }
    private static ValueOperand Number(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw ScriptDomErrors.Invalid();
        }
        return ValueOperand.Create(value);
    }
    private static ValueOperand Unary(Dom.UnaryExpression expression, ScriptDomLoweringContext context, int depth)
    {
        context.Check(depth + NextDepth);
        if (expression.Expression is not (Dom.IntegerLiteral or Dom.NumericLiteral or Dom.RealLiteral)
            || expression.Expression is not Dom.Literal literal
            || expression.UnaryExpressionType is not (Dom.UnaryExpressionType.Positive or Dom.UnaryExpressionType.Negative))
        {
            throw ScriptDomErrors.Unsupported();
        }
        return Number((expression.UnaryExpressionType == Dom.UnaryExpressionType.Negative ? NegativePrefix : PositivePrefix) + literal.Value);
    }
    private static FieldOperand Field(Dom.ColumnReferenceExpression column, string? alias, string? rightAlias, bool documentMetadata)
    {
        var parts = ScriptDomNames.Column(column);
        if (rightAlias is not null && parts.Count > SingleElement && parts[FirstIndex] == rightAlias)
        {
            throw ScriptDomErrors.Unsupported();
        }
        return new FieldOperand(ScriptDomNames.Path(parts, alias, documentMetadata));
    }

    private const string NegativePrefix = "-";
    private const string PositivePrefix = "+";
    private const int FirstIndex = 0;
    private const int SingleElement = 1;
    private const int NextDepth = 1;
}
