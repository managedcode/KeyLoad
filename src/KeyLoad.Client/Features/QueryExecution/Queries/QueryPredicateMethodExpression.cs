using System.Linq.Expressions;
using KeyLoad.Query;

namespace KeyLoad.Client;

internal static class QueryPredicateMethodExpression
{
    internal static Predicate Translate(MethodCallExpression call, ParameterExpression parameter)
    {
        if (IsNullMarker(call))
        {
            var isMissing = call.Method.Name == nameof(QueryFunctions.IsMissing);
            return new NullTest(QueryExpressions.Operand(call.Arguments[0], parameter), false, isMissing);
        }

        if (QueryPredicateInExpression.TryGetCollection(call, out var set, out var candidate))
        {
            return QueryPredicateInExpression.Translate(set, candidate, parameter);
        }

        throw QueryExpressions.Unsupported();
    }

    private static bool IsNullMarker(MethodCallExpression call) => call.Method.DeclaringType == typeof(QueryFunctions)
        && call.Arguments.Count == 1
        && call.Method.Name is nameof(QueryFunctions.IsNull) or nameof(QueryFunctions.IsMissing);
}
