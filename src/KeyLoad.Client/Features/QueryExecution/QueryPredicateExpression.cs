using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateExpression
{
    internal static Predicate Filter(Expression expression, ParameterExpression parameter, int depth = 1)
    {
        if (depth > QueryPredicateTokens.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.PredicateDepthExceededMessage);
        }

        expression = QueryExpressions.Unwrap(expression);
        if (expression is BinaryExpression binary)
        {
            return QueryPredicateBinaryExpression.Translate(binary, parameter, depth);
        }

        if (expression is UnaryExpression { NodeType: ExpressionType.Not } negation)
        {
            return new Negation(Filter(negation.Operand, parameter, depth + 1));
        }

        if (expression is MethodCallExpression call)
        {
            return QueryPredicateMethodExpression.Translate(call, parameter);
        }

        if (expression.Type == typeof(bool))
        {
            return new Comparison(QueryExpressions.Operand(expression, parameter), QueryPredicateTokens.Equal, QueryExpressions.Literal(true));
        }

        throw QueryExpressions.Unsupported();
    }
}
