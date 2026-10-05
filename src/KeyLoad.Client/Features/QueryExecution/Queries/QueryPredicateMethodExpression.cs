using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateMethodExpression
{
    internal static Predicate Translate(MethodCallExpression call, ParameterExpression parameter, QueryTranslationContext context)
    {
        if (IsNullMarker(call))
        {
            var isMissing = call.Method.Name == nameof(QueryFunctions.IsMissing);
            return new NullTest(context.Expressions.Operand(call.Arguments[QueryPredicateTokens.ParameterIndex], parameter), false, isMissing);
        }

        if (QueryPredicateInExpression.TryGetCollection(call, out var set, out var candidate))
        {
            return QueryPredicateInExpression.Translate(set, candidate, parameter, context);
        }

        throw QueryExpressions.Unsupported();
    }

    private static bool IsNullMarker(MethodCallExpression call) => call.Method.DeclaringType == typeof(QueryFunctions)
        && call.Arguments.Count == QueryPredicateTokens.MarkerArgumentCount
        && call.Method.Name is nameof(QueryFunctions.IsNull) or nameof(QueryFunctions.IsMissing);
}
