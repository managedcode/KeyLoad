using System.Linq.Expressions;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateBinaryExpression
{
    internal static Predicate Translate(BinaryExpression binary, ParameterExpression parameter, int depth, QueryTranslationContext context)
    {
        if (binary.Method is not null && binary.Method.DeclaringType != typeof(decimal) && binary.Method.DeclaringType != typeof(string))
        {
            throw QueryExpressions.Unsupported();
        }

        if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
        {
            var left = QueryPredicateExpression.Filter(binary.Left, parameter, context, depth + QueryPredicateTokens.DepthIncrement);
            var right = QueryPredicateExpression.Filter(binary.Right, parameter, context, depth + QueryPredicateTokens.DepthIncrement);
            return new Logical(left, binary.NodeType == ExpressionType.AndAlso ? QueryPredicateTokens.And : QueryPredicateTokens.Or, right);
        }

        var operation = Operation(binary.NodeType);
        var leftOperand = context.Expressions.Operand(binary.Left, parameter);
        var rightOperand = context.Expressions.Operand(binary.Right, parameter);
        if (operation is QueryPredicateTokens.Equal or QueryPredicateTokens.NotEqual && IsNull(leftOperand, rightOperand))
        {
            var value = IsNull(leftOperand) ? rightOperand : leftOperand;
            return new NullTest(value, operation == QueryPredicateTokens.NotEqual, false);
        }

        return new Comparison(leftOperand, operation, rightOperand);
    }

    private static string Operation(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => QueryPredicateTokens.Equal,
        ExpressionType.NotEqual => QueryPredicateTokens.NotEqual,
        ExpressionType.GreaterThan => QueryPredicateTokens.GreaterThan,
        ExpressionType.GreaterThanOrEqual => QueryPredicateTokens.GreaterThanOrEqual,
        ExpressionType.LessThan => QueryPredicateTokens.LessThan,
        ExpressionType.LessThanOrEqual => QueryPredicateTokens.LessThanOrEqual,
        _ => throw QueryExpressions.Unsupported()
    };

    private static bool IsNull(Operand operand) => operand is ValueOperand { Value.ValueKind: JsonValueKind.Null };

    private static bool IsNull(Operand left, Operand right) => IsNull(left) || IsNull(right);
}
