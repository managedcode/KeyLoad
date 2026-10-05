using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateExpression
{
    internal static Predicate Filter(Expression expression, ParameterExpression parameter, QueryTranslationContext context, int depth = QueryPredicateTokens.FirstDepth)
    {
        if (depth > context.Limits.MaximumDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, QueryPredicateTokens.PredicateDepthExceededMessage);
        }

        expression = QueryExpressions.Unwrap(expression);
        if (expression is BinaryExpression binary)
        {
            return QueryPredicateBinaryExpression.Translate(binary, parameter, depth, context);
        }

        if (expression is UnaryExpression { NodeType: ExpressionType.Not } negation)
        {
            return new Negation(Filter(negation.Operand, parameter, context, depth + QueryPredicateTokens.DepthIncrement));
        }

        if (expression is MethodCallExpression call)
        {
            return QueryPredicateMethodExpression.Translate(call, parameter, context);
        }

        if (expression.Type == typeof(bool))
        {
            return new Comparison(context.Expressions.Operand(expression, parameter), QueryPredicateTokens.Equal, QueryExpressions.Literal(true));
        }

        throw QueryExpressions.Unsupported();
    }
}
